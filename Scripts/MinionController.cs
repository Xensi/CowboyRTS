using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding;
[RequireComponent(typeof(SelectableEntity))]
public class MinionController : NetworkBehaviour
{
    #region Variables

    public enum State
    {
        Idle,
        Walk,
        WalkBeginFindEnemies,
        WalkContinueFindEnemies,
        WalkToEnemy,
        Attacking,
        AfterAttackCheck,
        FindBuildable,
        WalkToBuildable,
        Building,
        AfterBuildCheck,
        Spawn,
        Die,
        FindHarvestable,
        WalkToHarvestable,
        Harvesting,
        AfterHarvestCheck,
        FindDeposit,
        WalkToDeposit,
        Depositing,
        AfterDepositCheck
    }

    private void UpdateColliderStatus()
    {
        if (rigid != null)
        { 
            rigid.isKinematic = state switch
            {
                State.FindHarvestable or State.WalkToHarvestable or State.Harvesting or State.AfterHarvestCheck
                or State.FindDeposit or State.WalkToDeposit or State.Depositing or State.AfterDepositCheck => true,
                _ => false,
            };
        }
    }
    LayerMask enemyMask;
    private Camera cam;
    public Vector3 destination;
    public Animator anim; 
    AIPath ai;
    [SerializeField] private SelectableEntity selector;
    public State state = State.Spawn;
    public enum AttackType
    {
        Instant, SelfDestruct, Artillery, 
        Gatling //for gatling gun
    }
    public AttackType attackType = AttackType.Instant;
    public bool directionalAttack = false;

    private float change;
    private readonly float walkAnimThreshold = 0.01f;
    private Vector3 oldPosition;

    [SerializeField] private float attackRange = 1;

    public SelectableEntity targetEnemy;
    public bool followingMoveOrder = false;
    [SerializeField] private LocalRotation localRotate;
    private RaycastModifier rayMod;
    private Seeker seeker;
    private Collider col;
     
    [SerializeField] private Rigidbody rigid;
    private readonly float spawnDuration = .5f;
    #endregion
    public void PrepareForDeath()
    { 
        Global.Instance.localPlayer.selectedEntities.Remove(selector);
        selector.Select(false);
        state = State.Die;
        ai.enabled = false;
        rayMod.enabled = false;
        seeker.enabled = false;
        Destroy(rigid);
        Destroy(col);
    }
    void OnEnable()
    {
        ai = GetComponent<AIPath>();
        rayMod = GetComponent<RaycastModifier>();
        seeker = GetComponent<Seeker>();
        col = GetComponent<Collider>();

        // Update the destination right before searching for a path as well.
        // This is enough in theory, but this script will also update the destination every
        // frame as the destination is used for debugging and may be used for other things by other
        // scripts as well. So it makes sense that it is up to date every frame.
        if (ai != null) ai.onSearchPath += Update;
    }

    void OnDisable()
    {
        if (ai != null) ai.onSearchPath -= Update;
    }
    private void Awake()
    {
        enemyMask = LayerMask.GetMask("Entity", "Obstacle");
        cam = Camera.main;

        if (anim == null)
        {
            anim = GetComponentInChildren<Animator>();
        }
        state = State.Spawn;
        Invoke(nameof(FinishSpawning), spawnDuration);
    }
    private void FinishSpawning()
    { 
        state = State.Idle;
    }
    private void Update()
    { 
        if (ai != null) ai.destination = destination;
    }
    private void FixedUpdate()
    {
        change = GetActualPositionChange(); 
        UpdateState();
    }
    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            rigid.isKinematic = true;
            ai.enabled = false;
            rayMod.enabled = false;
            seeker.enabled = false;
        } 
        enabled = IsOwner;
        destination = transform.position;
        oldPosition = transform.position;
    }
    private void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(transform.position, attackRange);
        if (targetEnemy != null)
        { 
            Gizmos.DrawSphere(targetEnemy.transform.position, .1f);
        }
    }
    public void SetBuildDestination(Vector3 pos, SelectableEntity ent)
    { 
        destination = pos;
        buildTarget = ent; 
    } 
    private SelectableEntity GetClosestEnemy()
    {
        float range = attackRange + 2;

        int maxColliders = Mathf.RoundToInt(40 * range);
        Collider[] hitColliders = new Collider[maxColliders];
        int numColliders = Physics.OverlapSphereNonAlloc(transform.position, range, hitColliders, enemyMask); 
        Collider closest = null;
        float distance = Mathf.Infinity;
        float threshold = -Mathf.Infinity;
        Vector3 forward = transform.TransformDirection(Vector3.forward).normalized;
        for (int i = 0; i < numColliders; i++)
        {
            if (hitColliders[i].gameObject == gameObject || hitColliders[i].isTrigger) //skip self and triggers
            {
                continue;
            }
            SelectableEntity select = hitColliders[i].GetComponent<SelectableEntity>();
            if (select != null)
            {
                if (!select.alive)
                {
                    continue;
                }
                if (select.teamBehavior == SelectableEntity.TeamBehavior.OwnerTeam)
                {
                    if (Global.Instance.localPlayer.ownedEntities.Contains(select))
                    {
                        continue;
                    }
                }
                else if (select.teamBehavior == SelectableEntity.TeamBehavior.FriendlyNeutral)
                {
                    continue;
                }
            }
            
            if (directionalAttack)
            { 
                Vector3 heading = (hitColliders[i].transform.position - transform.position).normalized;
                float dot = Vector3.Dot(forward, heading);
                if (dot > threshold)
                {
                    closest = hitColliders[i];
                    threshold = dot;
                }
            }
            else
            { 
                float newDist = Vector3.SqrMagnitude(transform.position - hitColliders[i].transform.position);
                if (newDist < distance)
                {
                    closest = hitColliders[i];
                    distance = newDist;
                }
            }
        }
        SelectableEntity enemy = null;
        if (closest != null)
        {
            enemy = closest.GetComponent<SelectableEntity>();
        }
        return enemy;
    } 
    //50 fps fixed update
    private readonly int delay = 0; 
    private int basicallyIdleInstances = 0;
    private readonly int idleThreshold = 30;
    bool playedAttackMoveSound = false;
    private int attackReadyTimer = 0;
    private void HideMoveIndicator()
    {
        if (selector != null)
        {
            selector.HideMoveIndicator();
        }
    }
    private void DetectIfShouldStopFollowingMoveOrder()
    {
        if (change <= walkAnimThreshold && basicallyIdleInstances <= idleThreshold)
        {
            basicallyIdleInstances++;
        }
        else if (change > walkAnimThreshold)
        {
            basicallyIdleInstances = 0;
        }
        if (basicallyIdleInstances > idleThreshold || ai.reachedDestination)
        {
            followingMoveOrder = false;
            if (attackMoving)
            {
                //Debug.Log("moving to ordered destination");
                destination = orderedDestination;
            }
        }
    }
    private bool DetectIfStuck()
    {
        bool val = false;
        if (ai.slowWhenNotFacingTarget)
        {
            if (ai.reachedDestination)
            {
                val = true; 
            }
        }
        else
        { 
            if (change <= walkAnimThreshold && basicallyIdleInstances <= idleThreshold)
            {
                basicallyIdleInstances++;
            }
            else if (change > walkAnimThreshold)
            {
                basicallyIdleInstances = 0;
            }
            if (basicallyIdleInstances > idleThreshold || ai.reachedDestination)
            {
                val = true;
                //followingMoveOrder = false;
                /*if (attackMoving)
                {
                    //Debug.Log("moving to ordered destination");
                    destination = orderedDestination;
                }*/
            }
        } 
        return val;
    } 
    private void UpdateHarvestables()
    {  
        if (selector.harvestTarget != null && selector.harvestTarget.alive) //if we have a harvest target
        {
            if (!selector.harvestTarget.harvesters.Contains(selector) ) //if we are not in harvester list
            {
                if (selector.harvestTarget.harvesters.Count < selector.harvestTarget.allowedHarvesters) //if there is space
                {
                    //add us
                    selector.harvestTarget.harvesters.Add(selector);
                }
                else //there is no space
                {
                    //get a new harvest target
                    selector.harvestTarget = null;
                }
            } 
        }
    }
    private float rotationSpeed = 10f;
    private void UpdateState()
    {
        UpdateHarvestables();
        UpdateColliderStatus();
        UpdateAttackReadiness();

        if (attackType == AttackType.Gatling)
        {
            anim.SetFloat("AttackSpeed", 0);
        }

        switch (state)
        {
            case State.Spawn: //don't really do anything, just play the spawn animation
                anim.Play("Spawn");
                ai.canMove = false;
                destination = transform.position;
                break;
            case State.Die:
                anim.Play("Die");
                break;
            case State.Idle:
                HideMoveIndicator();
                anim.Play("Idle");
                ai.canMove = false;
                destination = transform.position;
                if (targetEnemy == null || !targetEnemy.alive)
                {
                    targetEnemy = GetClosestEnemy();
                }
                else
                {
                    state = State.WalkToEnemy;
                }
                if (selector.type == SelectableEntity.EntityTypes.Builder)
                {
                    if (buildTarget == null || buildTarget.fullyBuilt)
                    {
                        buildTarget = FindClosestBuildable();
                    }
                    else
                    {
                        state = State.WalkToBuildable;
                    }
                }
                break;
            case State.Walk: 
                UpdateMoveIndicator();
                ai.canMove = true;
                destination = orderedDestination;
                anim.Play("Walk"); 

                if (DetectIfStuck()) //!followingMoveOrder && !chasingEnemy && (buildTarget == null || buildTarget.fullyBuilt)
                {
                    //anim.ResetTrigger("Walk");
                    //playedAttackMoveSound = false;
                    state = State.Idle;
                } 
                break;
            #region Attacking
            case State.WalkBeginFindEnemies: //"ATTACK MOVE" 
                UpdateMoveIndicator();
                ai.canMove = true;
                destination = orderedDestination;

                if (!anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalkStart") && !anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk"))
                {
                    if (!playedAttackMoveSound)
                    {
                        playedAttackMoveSound = true;
                        selector.SimplePlaySound(2);
                    }
                    anim.Play("AttackWalkStart");
                }

                if (targetEnemy == null || !targetEnemy.alive)
                {
                    targetEnemy = GetClosestEnemy();
                }
                else
                {
                    state = State.WalkToEnemy;
                }

                if (DetectIfStuck())
                { 
                    state = State.Idle;
                }
                break;
            case State.WalkContinueFindEnemies: 
                UpdateMoveIndicator();
                ai.canMove = true;
                destination = orderedDestination;
                 
                if (targetEnemy == null || !targetEnemy.alive)
                {
                    targetEnemy = GetClosestEnemy();
                }
                else
                {
                    state = State.WalkToEnemy;
                }

                if (DetectIfStuck())
                {
                    state = State.Idle;
                }
                break;
            case State.WalkToEnemy:
                ai.canMove = true;
                if (targetEnemy == null || !targetEnemy.alive)
                {
                    state = State.WalkContinueFindEnemies;
                }
                else
                {
                    UpdateAttackIndicator();
                    if (Vector3.Distance(transform.position, targetEnemy.transform.position) > attackRange)
                    {
                        anim.Play("AttackWalk");
                        destination = targetEnemy.transform.position;
                    }
                    else
                    {
                        stateTimer = 0;
                        state = State.Attacking;
                    }
                }
                break;
            case State.Attacking: 
                if (attackType == AttackType.Gatling)
                {
                    anim.SetFloat("AttackSpeed", 1);
                }
                if ((targetEnemy == null || !targetEnemy.alive) && !attackReady)
                {
                    state = State.Idle;
                }
                else if ((targetEnemy == null || !targetEnemy.alive) && attackReady)
                {
                    state = State.WalkContinueFindEnemies;
                }
                else if (Vector3.Distance(transform.position, targetEnemy.transform.position) <= attackRange)
                {
                    UpdateAttackIndicator();
                    ai.canMove = false;
                    ai.canMove = moveWhileAttacking;
                    rotationSpeed = ai.rotationSpeed / 60;
                    transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, targetEnemy.transform.position - transform.position, Time.deltaTime * rotationSpeed, 0));

                    if (attackReady && CheckFacingTowards(targetEnemy.transform.position))
                    { 
                        anim.Play("Attack");  
                        if (AnimatorPlaying())
                        {
                            if (stateTimer < ConvertTimeToFrames(impactTime))
                            {
                                stateTimer++;
                            }
                            else if (attackReady)
                            {
                                attackReady = false;
                                switch (attackType)
                                {
                                    case AttackType.Instant:
                                        DamageSpecifiedEnemy(targetEnemy, damage);
                                        break;
                                    case AttackType.SelfDestruct:
                                        Explode(areaOfEffect);
                                        break;
                                    case AttackType.Artillery:
                                        ShootProjectileAtPosition(targetEnemy.transform.position);
                                        break;
                                    case AttackType.Gatling:
                                        DamageSpecifiedEnemy(targetEnemy, damage);
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }
                        else //animation finished
                        {
                            state = State.AfterAttackCheck;
                        }
                    }
                    else if (!anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
                    {
                        anim.Play("Idle");
                    }
                }
                else //walk to enemy if out of range
                {
                    state = State.WalkToEnemy;
                }
                break;
            case State.AfterAttackCheck:
                if (attackType == AttackType.Gatling)
                {
                    anim.SetFloat("AttackSpeed", 1);
                }
                anim.Play("Idle"); 
                if (targetEnemy == null || !targetEnemy.alive)
                {
                    state = State.WalkContinueFindEnemies;
                }
                else //if target enemy is alive
                { 
                    stateTimer = 0;
                    state = State.Attacking;
                }  
                break;
            #endregion
            #region Building
            case State.FindBuildable:
                if (buildTarget == null || buildTarget.fullyBuilt)
                {
                    buildTarget = FindClosestBuildable();
                }
                else
                {
                    state = State.WalkToBuildable;
                }
                break;
            case State.WalkToBuildable:
                ai.canMove = true;
                UpdateMoveIndicator();
                if (buildTarget == null)
                {
                    state = State.FindBuildable;
                }
                else
                {
                    if (Vector3.Distance(transform.position, buildTarget.transform.position) > attackRange)
                    {
                        ai.canMove = true;
                        anim.Play("Walk");
                        destination = buildTarget.transform.position;
                    }
                    else
                    {
                        stateTimer = 0;
                        state = State.Building;
                    }
                }
                break;
            case State.Building:
                if (buildTarget == null)
                {
                    state = State.FindBuildable;
                }
                else
                {
                    ai.canMove = false;
                    transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, buildTarget.transform.position - transform.position, Time.deltaTime * ai.rotationSpeed, 0));

                    if (attackReady)
                    {
                        anim.Play("Attack");

                        if (AnimatorPlaying())
                        {
                            if (stateTimer < ConvertTimeToFrames(impactTime))
                            {
                                stateTimer++;
                            }
                            else if (attackReady)
                            {
                                attackReady = false;
                                BuildTarget(buildTarget);
                            }
                        }
                        else //animation finished
                        {
                            state = State.AfterBuildCheck;
                        }
                    }
                }
                break;
            case State.AfterBuildCheck:
                anim.Play("Idle");
                if (buildTarget != null)
                {
                    if (buildTarget.fullyBuilt)
                    {
                        state = State.FindBuildable;
                    }
                    else
                    {
                        stateTimer = 0;
                        state = State.Building;
                    }
                }
                else
                {
                    state = State.FindBuildable;
                }
                break;
            #endregion
            #region Harvestable 
            case State.FindHarvestable:
                if (selector.harvestTarget == null || !selector.harvestTarget.alive)
                {
                    selector.harvestTarget = FindClosestHarvestable();
                }
                else
                {
                    state = State.WalkToHarvestable;
                }
                break;
            case State.WalkToHarvestable:
                UpdateMoveIndicator();
                ai.canMove = true;
                if (selector.harvestTarget == null)
                {
                    state = State.FindHarvestable;
                }
                else
                {
                    if (Vector3.Distance(transform.position, selector.harvestTarget.transform.position) > attackRange) //if out of harvest range walk there
                    {
                        ai.canMove = true;
                        anim.Play("Walk");
                        destination = selector.harvestTarget.transform.position;
                    }
                    else
                    {
                        stateTimer = 0;
                        state = State.Harvesting;
                    }
                }
                break;
            case State.Harvesting:
                if (selector.harvestTarget == null)
                {
                    state = State.FindHarvestable;
                }
                else
                {
                    ai.canMove = false;
                    transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, selector.harvestTarget.transform.position - transform.position, Time.deltaTime * ai.rotationSpeed, 0));

                    if (attackReady)
                    {
                        anim.Play("Attack");

                        if (AnimatorPlaying())
                        {
                            if (stateTimer < ConvertTimeToFrames(impactTime))
                            {
                                stateTimer++;
                            }
                            else if (attackReady)
                            {
                                attackReady = false;
                                HarvestTarget(selector.harvestTarget);
                            }
                        }
                        else //animation finished
                        {
                            state = State.AfterHarvestCheck;
                        }
                    }
                }
                break;
            case State.AfterHarvestCheck:
                anim.Play("Idle");
                if (selector.harvestedResource >= selector.harvestCapacity)
                {
                    state = State.FindDeposit;
                }
                else if (selector.harvestTarget != null)
                {
                    stateTimer = 0;
                    state = State.Harvesting;
                }
                else
                {
                    state = State.FindHarvestable;
                }
                break;
            case State.FindDeposit:
                if (depositTarget == null)
                {
                    depositTarget = FindClosestDeposit();
                }
                else
                {
                    state = State.WalkToDeposit;
                }
                break;
            case State.WalkToDeposit:
                UpdateMoveIndicator();
                ai.canMove = true;
                if (depositTarget == null)
                {
                    state = State.FindDeposit;
                }
                else
                {
                    if (Vector3.Distance(transform.position, depositTarget.transform.position) > attackRange)
                    {
                        ai.canMove = true;
                        anim.Play("Walk");
                        destination = depositTarget.transform.position;
                    }
                    else
                    {
                        state = State.Depositing;
                    }
                }
                break;
            case State.Depositing:
                if (depositTarget == null)
                {
                    state = State.FindDeposit;
                }
                else
                {
                    ai.canMove = false;
                    transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, depositTarget.transform.position - transform.position, Time.deltaTime * ai.rotationSpeed, 0));

                    //anim.Play("Attack"); //replace with deposit animation
                    //instant dropoff
                    if (selector != null)
                    {
                        Global.Instance.localPlayer.gold += selector.harvestedResource;
                        selector.harvestedResource = 0;
                        Global.Instance.localPlayer.UpdateGUIFromSelections();
                    }
                    state = State.AfterDepositCheck;
                }
                break;
            case State.AfterDepositCheck:
                anim.Play("Idle");
                if (selector.harvestTarget != null)
                {
                    stateTimer = 0;
                    state = State.WalkToHarvestable;
                }
                else
                {
                    state = State.FindHarvestable;
                }
                break;
            #endregion 
            default:
                break;
        }
    }

    private bool CheckFacingTowards(Vector3 pos)
    {
        if (!directionalAttack) return true;
        float threshold = .95f; //-1 opposite, 0 perpendicular, 1 facing
        Vector3 forward = transform.TransformDirection(Vector3.forward).normalized;
        Vector3 heading = (pos - transform.position).normalized;  
        //Debug.Log(Vector3.Dot(forward, heading));
        if (Vector3.Dot(forward, heading) >= threshold)
        {
            return true;
        }
        return false;
    }

    [SerializeField] private Transform attackEffectSpawnPosition; 
    private void UpdateAttackIndicator()
    { 
        if (selector != null)
        {
            if (Input.GetKey(KeyCode.Space) && targetEnemy != null)
            {
                selector.UpdateAttackIndicator();
            }
            else
            {
                selector.HideMoveIndicator();
            }
        }
    }
    private void UpdateMoveIndicator()
    {
        if (selector != null)
        {
            if (Input.GetKey(KeyCode.Space))
            { 
                selector.UpdateMoveIndicator();
            }
            else
            {
                selector.HideMoveIndicator();
            }
        }
    }
    private void UpdateAttackReadiness()
    {  
        if (!attackReady)
        {
            if (attackReadyTimer < ConvertTimeToFrames(attackDuration - impactTime))
            {
                attackReadyTimer++;
            }
            else
            {
                attackReady = true;
                attackReadyTimer = 0;
            }
        }
    }
    private float ConvertTimeToFrames(float seconds) //1 second is 50 frames
    {
        float frames = seconds * 50;
        return frames;
    }
    private bool AnimatorPlaying()
    {
        return anim.GetCurrentAnimatorStateInfo(0).length > anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
    }
    private int stateTimer = 0;
    private void HarvestTarget(SelectableEntity target)
    {
        selector.SimplePlaySound(1); //play impact sound 
        if (target != null)
        {
            int actualHarvested = Mathf.Clamp(harvestAmount, 0, target.hitPoints.Value); //max amount we can harvest clamped by hitpoints remaining
            int diff = selector.harvestCapacity - selector.harvestedResource;
            actualHarvested = Mathf.Clamp(actualHarvested, 0, diff); //max amount we can harvest clamped by remaining carrying capacity
            if (IsServer)
            {
                target.Harvest(harvestAmount);
            }
            else //client tell server to change the network variable
            {
                RequestHarvestServerRpc(harvestAmount, target);
            }

            selector.harvestedResource += actualHarvested;
            Global.Instance.localPlayer.UpdateGUIFromSelections();
        }
    } 
    private SelectableEntity FindClosestBuildable()
    {
        List<SelectableEntity> list = Global.Instance.localPlayer.ownedEntities;

        SelectableEntity closest = null;
        float distance = Mathf.Infinity;
        foreach (SelectableEntity item in list)
        {
            if (item != null && !item.fullyBuilt)
            {
                float newDist = Vector3.SqrMagnitude(transform.position - item.transform.position);
                if (newDist < distance)
                {
                    closest = item;
                    distance = newDist;
                }
            }
        }
        return closest;
    }
    /// <summary>
    /// Returns closest harvestable resource with space for new harvesters.
    /// </summary>
    /// <returns></returns>
    private SelectableEntity FindClosestHarvestable()
    { 
        SelectableEntity[] list = Global.Instance.harvestableResources;

        SelectableEntity closest = null;
        float distance = Mathf.Infinity;
        foreach (SelectableEntity item in list)
        {
            if (item != null)
            {
                if (item.harvesters.Count < item.allowedHarvesters) //there is space for a new harvester
                {
                    float newDist = Vector3.SqrMagnitude(transform.position - item.transform.position);
                    if (newDist < distance)
                    {
                        closest = item;
                        distance = newDist;
                    }
                } 
            }
        }
        return closest;
    }
    private SelectableEntity FindClosestDeposit() //right now resource agnostic
    {
        List<SelectableEntity> list = Global.Instance.localPlayer.ownedEntities;

        SelectableEntity closest = null;
        float distance = Mathf.Infinity;
        foreach (SelectableEntity item in list)
        { 
            if (item != null && item.depositType != SelectableEntity.DepositType.None && item.fullyBuilt)
            {
                float newDist = Vector3.SqrMagnitude(transform.position - item.transform.position);
                if (newDist < distance)
                {
                    closest = item;
                    distance = newDist;
                }
            }
        }  
        return closest;
    }
    public SelectableEntity depositTarget;
    private readonly sbyte harvestAmount = 1;
    [SerializeField] private bool moveWhileAttacking = false;
    private void CancelAttack()
    {
        targetEnemy = null;
        buildTarget = null;
        state = State.Idle;
        CancelInvoke("SimpleDamageEnemy");
        CancelInvoke("SimpleBuildTarget");
        selector.UpdateIndicator();
    }
    private void ReturnState()
    {
        switch (attackType)
        {
            case AttackType.Instant:
                state = State.Idle;
                attackReady = true;
                break;
            case AttackType.SelfDestruct:
                break;
            default:
                break;
        }
    }
    private readonly sbyte buildDelta = 1;
    [SerializeField] private float areaOfEffect = 1; 
    public void BuildTarget(SelectableEntity target) //since hp is a network variable, changing it on the server will propagate changes to clients as well
    {
        //fire locally
        selector.SimplePlaySound(1);

        if (target != null)
        {
            if (IsServer)
            {
                target.BuildThis(buildDelta);
            }
            else //client tell server to change the network variable
            {
                RequestBuildServerRpc(buildDelta, target);
            }
        }
    }
    [ServerRpc]
    private void RequestBuildServerRpc(sbyte damage, NetworkBehaviourReference target)
    {
        //server must handle damage! 
        if (target.TryGet(out SelectableEntity select))
        {
            select.BuildThis(damage);
        }
    }
    private void StartAttack()
    {
        attackReady = false;
        state = State.Attacking;
        destination = transform.position;

        switch (attackType)
        {
            case AttackType.Instant:
                Invoke(nameof(SimpleDamageEnemy), impactTime + Random.Range(-0.1f, 0.1f));
                Invoke(nameof(ReturnState), attackDuration);
                break;
            case AttackType.SelfDestruct:
                Explode(areaOfEffect);
                break;
            default:
                break;
        }
    }
    [ServerRpc]
    private void RequestHarvestServerRpc(sbyte amount, NetworkBehaviourReference target)
    {
        //server must handle damage! 
        if (target.TryGet(out SelectableEntity select))
        {
            select.Harvest(amount);
        }
    }

    private void Explode(float explodeRadius)
    {
        Vector3 center = transform.position;  
        Collider[] hitColliders = new Collider[40];
        int numColliders = Physics.OverlapSphereNonAlloc(center, explodeRadius, hitColliders, enemyMask);  
        for (int i = 0; i < numColliders; i++)
        {
            if (hitColliders[i].gameObject == gameObject || hitColliders[i].isTrigger) //skip self
            {
                continue;
            }
            SelectableEntity select = hitColliders[i].GetComponent<SelectableEntity>();
            if (Global.Instance.localPlayer.ownedEntities.Contains(select)) //skip teammates
            {
                continue;
            }
            if (select.teamBehavior == SelectableEntity.TeamBehavior.FriendlyNeutral)
            {
                continue;
            }
            DamageSpecifiedEnemy(select, damage);
            //Debug.Log(i);
        }
        DamageSpecifiedEnemy(selector, damage); //kill this also
        SimpleExplosion(transform.position);
    }  
    private void SimpleExplosion(Vector3 pos)
    { 
        //spawn explosion prefab locally
        SpawnExplosion(pos);

        //spawn for other clients as well
        if (IsServer)
        {
            SpawnExplosionClientRpc(pos);
        }
        else
        {
            RequestExplosionServerRpc(pos);
        }
    }
    private void SpawnExplosion(Vector3 pos)
    { 
        GameObject prefab = Global.Instance.explosionPrefab;
        _ = Instantiate(prefab, pos, Quaternion.identity);
    }
    /// <summary>
    /// Ask server to play explosions
    /// </summary> 
    [ServerRpc] 
    private void RequestExplosionServerRpc(Vector3 pos)
    {
        SpawnExplosionClientRpc(pos);
    }
    /// <summary>
    /// Play explosion for all other clients
    /// </summary> 
    [ClientRpc]  
    private void SpawnExplosionClientRpc(Vector3 pos)
    {
        if (!IsOwner)
        {
            SpawnExplosion(pos);
        }
    }
    public void DamageSpecifiedEnemy(SelectableEntity enemy, sbyte damage) //since hp is a network variable, changing it on the server will propagate changes to clients as well
    {
        if (enemy != null)
        {
            //fire locally
            selector.SimplePlaySound(1);
            if (selector.attackEffects.Length > 0) //show muzzle flash
            {
                selector.DisplayAttackEffects();
            }
            if (selector.type == SelectableEntity.EntityTypes.Ranged ) //shoot trail
            {
                Vector3 spawnPos;
                if (attackEffectSpawnPosition != null)
                {
                    spawnPos = attackEffectSpawnPosition.position;
                }
                else
                {
                    spawnPos = transform.position;
                }
                SimpleTrail(spawnPos, enemy.transform.position);
            }
            if (IsServer)
            {
                enemy.TakeDamage(damage);
            }
            else //client tell server to change the network variable
            {
                RequestDamageServerRpc(damage, enemy);
            }
        }
    }
    private void SimpleTrail(Vector3 star, Vector3 dest)
    {
        SpawnTrail(star, dest); //spawn effect locally

        //spawn for other clients as well
        if (IsServer)
        {
            TrailClientRpc(star, dest);
        }
        else
        {
            RequestTrailServerRpc(star, dest);
        }
    }
    private void SpawnTrail(Vector3 start, Vector3 destination)
    {
        TrailController tra = Instantiate(Global.Instance.gunTrailGlobal, start, Quaternion.identity);
        tra.start = start;
        tra.destination = destination; 
    }
    [ServerRpc]
    private void RequestTrailServerRpc(Vector3 star, Vector3 dest)
    {
        TrailClientRpc(star, dest);
    }
    [ClientRpc]
    private void TrailClientRpc(Vector3 star, Vector3 dest)
    {
        if (!IsOwner)
        {
            SpawnTrail(star, dest);
        }
    } 
    private void ShootProjectileAtPosition(Vector3 dest)
    {
        Vector3 star;
        if (attackEffectSpawnPosition != null)
        {
            star = attackEffectSpawnPosition.position;
        }
        else
        {
            star = transform.position;
        }

        //Spawn locally
        SpawnProjectile(star, dest);
        //spawn for other clients as well
        if (IsServer)
        {
            ProjectileClientRpc(star, dest);
        }
        else
        {
            ProjectileServerRpc(star, dest);
        }
    }
    private void SpawnProjectile(Vector3 spawnPos, Vector3 destination)
    { 
        //instantiate projectile
        Projectile proj = Instantiate(Global.Instance.projectileGlobal, spawnPos, Quaternion.identity);
        //tell projectile where to go 
        proj.target = destination;
        proj.isLocal = IsOwner;
    }
    [ClientRpc]
    private void ProjectileClientRpc(Vector3 star, Vector3 dest)
    {
        if (!IsOwner)
        {
            SpawnProjectile(star, dest);
        }
    }
    [ServerRpc]
    private void ProjectileServerRpc(Vector3 star, Vector3 dest)
    {
        ProjectileClientRpc(star, dest);
    }

    public SelectableEntity buildTarget;
    [SerializeField] private sbyte damage = 1;
    private bool attackReady = true;
    public void SimpleDamageEnemy() //since hp is a network variable, changing it on the server will propagate changes to clients as well
    {
        if (targetEnemy != null)
        { 
            //fire locally
            selector.SimplePlaySound(1);
            if (selector.attackEffects.Length > 0)
            {
                selector.DisplayAttackEffects();
            }
            if (IsServer)
            {
                targetEnemy.TakeDamage(damage);
            }
            else //client tell server to change the network variable
            {
                RequestDamageServerRpc(damage, targetEnemy);
            }
        }
    }
    [ServerRpc]
    private void RequestDamageServerRpc(sbyte damage, NetworkBehaviourReference enemy)
    {
        //server must handle damage! 
        if (enemy.TryGet(out SelectableEntity select))
        {
            select.TakeDamage(damage);
        }  
    }

    [SerializeField] private float attackDuration = 1;
    [SerializeField] private float impactTime = .5f;
    private float GetActualPositionChange()
    {
        float dist = Vector3.SqrMagnitude(transform.position - oldPosition);
        oldPosition = transform.position;
        return dist/Time.deltaTime;
    }

    public bool attackMoving = false;
    public Vector3 orderedDestination;
    public void RallyToPos(Vector3 pos)
    {
        //followingMoveOrder = true;
        destination = pos;
        orderedDestination = destination;
        //state = State.Walk;
    }
    private void ResetAllTargets()
    { 
        targetEnemy = null;
        buildTarget = null;
        selector.harvestTarget = null;
        depositTarget = null;
    }
    public void SetAttackMoveDestination()
    {
        ResetAllTargets(); 
        basicallyIdleInstances = 0;
        followingMoveOrder = true; 
        state = State.WalkBeginFindEnemies; //default to walking state
        playedAttackMoveSound = false;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity))
        {
            destination = hit.point;
            orderedDestination = destination;   
        }
    }
    public void SetDestinationRaycast(bool attackMoveVal = false)
    {
        if (state != State.Spawn)
        {
            ResetAllTargets(); 
            basicallyIdleInstances = 0;
            followingMoveOrder = true;
            attackMoving = attackMoveVal;
            state = State.Walk; //default to walking state

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity))
            {
                destination = hit.point;
                orderedDestination = destination;
                //check if player right clicked on an entity and what behavior unit should have
                SelectableEntity select = hit.collider.GetComponent<SelectableEntity>(); 
                if (select != null)
                {
                    if (select.teamBehavior == SelectableEntity.TeamBehavior.OwnerTeam)
                    {
                        if (select.net.OwnerClientId == selector.net.OwnerClientId) //same team
                        {
                            if (select.depositType == SelectableEntity.DepositType.Gold && select.fullyBuilt) //if deposit point
                            {
                                depositTarget = select;
                                state = State.FindDeposit;
                            }
                            else if (selector.type == SelectableEntity.EntityTypes.Builder && !select.fullyBuilt) //if buildable and this is a builder
                            {
                                buildTarget = select;
                                state = State.WalkToBuildable;
                            }
                        }
                        else //enemy
                        { //try to target this enemy specifically
                            targetEnemy = select;
                            state = State.WalkToEnemy; 
                        }
                    }
                    else if (select.teamBehavior == SelectableEntity.TeamBehavior.FriendlyNeutral)
                    {
                        if (select.type == SelectableEntity.EntityTypes.HarvestableStructure)
                        {
                            //check if clicked is resource
                            //if it is, then tell resource collectors to gather it
                            if (selector.isHarvester)
                            {
                                if (selector.harvestedResource < selector.harvestCapacity) //we could still harvest more
                                { 
                                    selector.harvestTarget = select;
                                    state = State.WalkToHarvestable;
                                }
                                else
                                { 
                                    state = State.FindDeposit;
                                }
                            }
                        }
                    } 
                } 
                CancelInvoke("SimpleDamageEnemy"); 
            }
        } 
    }
}
