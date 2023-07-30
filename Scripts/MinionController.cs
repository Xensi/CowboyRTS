using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding;
 
public class MinionController : NetworkBehaviour
{
    #region Variables

    public enum State
    {
        Idle,
        Walk,
        WalkFindEnemies,
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
    LayerMask enemyMask;
    public SelectableEntity harvestTarget;
    private Camera cam;
    public Vector3 destination;
    public Animator anim;
    bool animsEnabled = false;
    AIPath ai;
    [SerializeField] private SelectableEntity selector;
    public State state = State.Spawn;
    public enum AttackType
    {
        Instant, SelfDestruct
    }
    public AttackType attackType = AttackType.Instant;
    private bool chasingEnemy = false;
    private float change;
    private float walkAnimThreshold = 0.01f;
    private Vector3 oldPosition;

    [SerializeField] private float attackRange = 1;

    public SelectableEntity targetEnemy;
    public bool followingMoveOrder = false;
    [SerializeField] private LocalRotation localRotate;
    private RaycastModifier rayMod;
    private Seeker seeker;
    private Collider col;

    public bool enemyInRange = false;
    [SerializeField] private Rigidbody rigid;
    private float spawnDuration = .5f;
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
        Invoke("FinishSpawning", spawnDuration);
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

        /*if (delay >= 24) //0 to 24 is half of 50
        {
            delay = 0; 
        }
        else
        {
            delay++;
        }  */ 
        /*if (selector != null)
        { 
            selector.UpdateTargetIndicator();
        }*/
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
        int maxColliders = Mathf.RoundToInt(40 * attackRange);
        Collider[] hitColliders = new Collider[maxColliders];
        int numColliders = Physics.OverlapSphereNonAlloc(transform.position, attackRange, hitColliders, enemyMask); 
        Collider closest = null;
        float distance = Mathf.Infinity;
        for (int i = 0; i < numColliders; i++)
        {
            if (hitColliders[i].gameObject == gameObject || hitColliders[i].isTrigger) //skip self and triggers
            {
                continue;
            }
            SelectableEntity select = hitColliders[i].GetComponent<SelectableEntity>();
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
            float newDist = Vector3.SqrMagnitude(transform.position - hitColliders[i].transform.position);
            if (newDist < distance)
            {
                closest = hitColliders[i];
                distance = newDist;
            }
        }
        SelectableEntity enemy = null;
        if (closest != null)
        {
            enemy = closest.GetComponent<SelectableEntity>();
        }
        return enemy;
    }
    #region Unused
    private void CheckTargetEnemy() //use spherecast to get an enemy. if in detect range they become our target
    {
        if (targetEnemy != null && targetEnemy.hitPoints.Value <= 0)
        {
            targetEnemy = null;
        }
        if (targetEnemy == null)
        {
            enemyInRange = false;
            //do a spherecast to get one
            Vector3 center = transform.position;
            float detectionRange = attackRange * 1.25f;
            if (attackMoving)
            {
                detectionRange = attackRange;
            }
            int maxColliders = Mathf.RoundToInt(40 * detectionRange);
            Collider[] hitColliders = new Collider[maxColliders];
            int numColliders = Physics.OverlapSphereNonAlloc(center, detectionRange, hitColliders, enemyMask);
            //get closest collider 
            Collider closest = null;
            float distance = Mathf.Infinity;
            for (int i = 0; i < numColliders; i++)
            {
                if (hitColliders[i].gameObject == gameObject || hitColliders[i].isTrigger) //skip self
                {
                    continue;
                }
                SelectableEntity select = hitColliders[i].GetComponent<SelectableEntity>();
                if (Global.Instance.localPlayer.ownedEntities.Contains(select))
                {
                    continue;
                }
                float newDist = Vector3.SqrMagnitude(transform.position - hitColliders[i].transform.position);
                if (newDist < distance)
                {
                    closest = hitColliders[i];
                    distance = newDist;
                }
            }
            if (closest != null)
            {
                float dist = Vector3.Distance(transform.position, closest.transform.position);
                if (dist <= attackRange)
                {
                    targetEnemy = closest.GetComponent<SelectableEntity>();
                    enemyInRange = true;
                    chasingEnemy = false;
                }
                else
                {
                    destination = closest.transform.position;
                    enemyInRange = false;
                    chasingEnemy = true;
                }
            }
            else
            {
                enemyInRange = false;
                chasingEnemy = false;
            }
        }
        else
        {
            float dist = Vector3.Distance(transform.position, targetEnemy.transform.position);
            if (dist > attackRange)
            {
                chasingEnemy = false;
                targetEnemy = null;
                enemyInRange = false;
                CancelAttack();
            }
            else
            {
                chasingEnemy = false;
                enemyInRange = true;
            }
        }
    }
    #endregion
    //50 fps fixed update
    private int delay = 0; 
    private int basicallyIdleInstances = 0;
    private int idleThreshold = 30;
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
        return val;
    }
    private void UpdateState()
    {
        UpdateAttackReadiness();
        switch (state)
        {
            case State.Spawn: //don't really do anything, just play the spawn animation
                anim.Play("Spawn");
                ai.canMove = false;
                destination = transform.position;
                break;
            case State.Idle:
                HideMoveIndicator();
                anim.Play("Idle");
                ai.canMove = false;
                destination = transform.position;
                if (targetEnemy == null)
                { 
                    targetEnemy = GetClosestEnemy();
                }
                else
                {
                    state = State.WalkToEnemy;
                }
                #region Unused
                /*if (anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk") || anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalkStart")) //transitioning from attack walk to idle
                { 
                    anim.SetTrigger("Idle");
                }
                else //otherwise idle
                { 
                    anim.Play("Idle");
                }*/
                //conditions to move to other states
                /*if (enemyInRange && !followingMoveOrder && attackReady)
                {
                    anim.ResetTrigger("Idle");
                    StartAttack();
                }    
                else if (followingMoveOrder || chasingEnemy)
                {
                    anim.ResetTrigger("Idle");
                    state = AnimStates.Walk;
                }*/
                #endregion
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
                #region Unused 
                /*if (enemyInRange && (!followingMoveOrder || attackMoving) && attackReady)
                {
                    playedAttackMoveSound = false;
                    anim.ResetTrigger("Walk");
                    StartAttack();
                }
                else if (buildTarget != null && Vector3.Distance(transform.position, buildTarget.transform.position) <= attackRange && attackReady)
                {
                    anim.ResetTrigger("Walk");
                    playedAttackMoveSound = false;
                    StartBuild();
                }
                else if (harvestTarget != null && Vector3.Distance(transform.position, harvestTarget.transform.position) <= attackRange && attackReady) //try to harvest
                {
                    anim.ResetTrigger("Walk");
                    StartHarvest();
                }
                */
                /*ai.canMove = true;
                if (attackMoving)
                {
                    
                }
                else
                {
                    if (anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk") || anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalkStart"))
                    {
                        playedAttackMoveSound = false;
                        anim.SetTrigger("Walk");
                    }
                    else
                    { 
                        anim.Play("Walk");
                    }
                }*/ 
                #endregion
                break;
            case State.WalkFindEnemies: //"ATTACK MOVE" 
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
                break;
            case State.WalkToEnemy:
                ai.canMove = true; 
                if (targetEnemy == null)
                {
                    state = State.WalkFindEnemies;
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
                if (targetEnemy == null)
                {
                    state = State.WalkFindEnemies;
                }
                else if (Vector3.Distance(transform.position, targetEnemy.transform.position) <= attackRange)
                {
                    UpdateAttackIndicator();
                    ai.canMove = false;
                    ai.canMove = moveWhileAttacking;
                    transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, targetEnemy.transform.position - transform.position, Time.deltaTime * ai.rotationSpeed, 0));

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
                                DamageSpecifiedEnemy(targetEnemy);
                            }
                        }
                        else //animation finished
                        {
                            state = State.AfterAttackCheck;
                        }
                    }
                }
                else
                {
                    state = State.WalkToEnemy;
                }
                break;
            case State.AfterAttackCheck:
                anim.Play("Idle");
                if (targetEnemy != null)
                {
                    if (targetEnemy.alive) //attack again.
                    {
                        stateTimer = 0;
                        state = State.Attacking;
                    }
                    else
                    {
                        state = State.WalkFindEnemies;
                    }
                }
                else
                {
                    state = State.WalkFindEnemies;
                }
                break;
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
            case State.FindHarvestable:
                if (harvestTarget == null)
                {
                    harvestTarget = FindClosestResource();
                }
                else
                {
                    state = State.WalkToHarvestable;
                }
                break;
            case State.WalkToHarvestable:
                UpdateMoveIndicator();
                ai.canMove = true;
                if (harvestTarget == null)
                {
                    state = State.FindHarvestable;
                }
                else
                {
                    if (Vector3.Distance(transform.position, harvestTarget.transform.position) > attackRange) //if out of harvest range walk there
                    {
                        ai.canMove = true;
                        anim.Play("Walk");
                        destination = harvestTarget.transform.position;
                    }
                    else
                    {
                        stateTimer = 0;
                        state = State.Harvesting;
                    }
                } 
                break;
            case State.Harvesting:
                if (harvestTarget == null)
                { 
                    state = State.FindHarvestable;
                }
                else
                { 
                    ai.canMove = false; 
                    transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, harvestTarget.transform.position - transform.position, Time.deltaTime * ai.rotationSpeed, 0));
                    
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
                                HarvestTarget(harvestTarget);
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
                else if (harvestTarget != null)
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

                    anim.Play("Attack"); //replace with deposit animation
                    if (!AnimatorPlaying())
                    {
                        if (selector != null)
                        {
                            Global.Instance.localPlayer.gold += selector.harvestedResource;
                            selector.harvestedResource = 0;
                            UpdateResourceGUI();
                        } 
                        state = State.AfterDepositCheck;
                    }  
                } 
                break;
            case State.AfterDepositCheck: 
                anim.Play("Idle");
                if (harvestTarget != null)
                {
                    stateTimer = 0;
                    state = State.WalkToHarvestable;
                }
                else
                {
                    state = State.FindHarvestable;
                }
                break;
            case State.Die:
                anim.Play("Die");
                break;
            default:
                break;
        }
    }
    private void UpdateAttackIndicator()
    { 
        if (selector != null)
        {
            if (Input.GetKey(KeyCode.Space))
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
            UpdateResourceGUI();
        }
    }
    private void UpdateResourceGUI()
    { 
        if (Global.Instance.resourcesParent.activeInHierarchy)
        {
            Global.Instance.resourceText.text = "Stored gold: " + selector.harvestedResource + "/" + selector.harvestCapacity;
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
    private SelectableEntity FindClosestResource()
    { 
        SelectableEntity[] list = Global.Instance.harvestableResources;

        SelectableEntity closest = null;
        float distance = Mathf.Infinity;
        foreach (SelectableEntity item in list)
        {
            if (item != null && item.harvestType != SelectableEntity.HarvestType.Gold)
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
    private SelectableEntity FindClosestDeposit() //right now resource agnostic
    {
        List<SelectableEntity> list = Global.Instance.localPlayer.ownedEntities;

        SelectableEntity closest = null;
        float distance = Mathf.Infinity;
        foreach (SelectableEntity item in list)
        { 
            if (item != null && item.depositType != SelectableEntity.DepositType.None)
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
    private sbyte harvestAmount = 1;
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
    private sbyte buildDelta = 1;
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
                Invoke("SimpleDamageEnemy", impactTime + Random.Range(-0.1f, 0.1f));
                Invoke("ReturnState", attackDuration);
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
            DamageSpecifiedEnemy(select);
            //Debug.Log(i);
        }
        GameObject prefab = Global.Instance.explosionPrefab;
        GameObject instantiated = Instantiate(prefab, transform.position, Quaternion.identity); 
        selector.ProperDestroyMinion();
    } 
    public void DamageSpecifiedEnemy(SelectableEntity enemy) //since hp is a network variable, changing it on the server will propagate changes to clients as well
    {
        if (enemy != null)
        {
            //fire locally
            selector.SimplePlaySound(1);
            if (selector.attackEffects.Length > 0)
            {
                selector.DisplayAttackEffects();
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
        harvestTarget = null;
        depositTarget = null;
    }
    public void SetAttackMoveDestination()
    {
        ResetAllTargets(); 
        basicallyIdleInstances = 0;
        followingMoveOrder = true; 
        state = State.WalkFindEnemies; //default to walking state
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
            enemyInRange = false;
            basicallyIdleInstances = 0;
            followingMoveOrder = true;
            attackMoving = attackMoveVal;
            state = State.Walk; //default to walking state

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity))
            {
                destination = hit.point;
                orderedDestination = destination;

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
                            attackMoving = true;
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
                                harvestTarget = select;
                                state = State.WalkToHarvestable;
                            }
                        }
                    } 
                } 
                CancelInvoke("SimpleDamageEnemy"); 
            }
        } 
    }
}
