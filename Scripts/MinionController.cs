using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding;
 
public class MinionController : NetworkBehaviour
{
    private Camera cam;
    public Vector3 destination;  
    public Animator anim;
    bool animsEnabled = false;
    AIPath ai;
    [SerializeField] private SelectableEntity selector;
    public AnimStates state = AnimStates.Spawn;
    public enum AnimStates
    {
        Idle,
        Walk,
        Attack,
        Build,
        Spawn,
        Die,
        Harvest
    }
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
    public void PrepareForDeath()
    { 
        Global.Instance.localPlayer.selectedEntities.Remove(selector);
        selector.Select(false);
        state = AnimStates.Die;
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
    private void Update()
    {
        
    }
    private void Awake()
    {
        enemyMask = LayerMask.GetMask("Entity", "Obstacle");
        cam = Camera.main;

        if (anim != null)
        {
            animsEnabled = true;
        }
        else
        {
            anim = GetComponentInChildren<Animator>();
        }
        if (anim != null)
        {
            anim.Play("Spawn");
        } 
        Invoke("FinishSpawning", spawnDuration);
        //rigid.isKinematic = true;
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
        //localRotate.rotSpeed = ai.rotationSpeed;
        //localRotate.enabled = !IsOwner; //if isOwner, localRotate is disabled
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
    LayerMask enemyMask; 
    public void SetBuildDestination(Vector3 pos, SelectableEntity ent)
    { 
        destination = pos;
        buildTarget = ent; 
    } 
    
    private void CheckTargetEnemy() //use spherecast to get an enemy. if in attack range they become our target
    {
        if (targetEnemy != null && targetEnemy.hitPoints.Value <= 0)
        {
            targetEnemy = null; 
        }


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
        if (targetEnemy != null)
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
    public bool enemyInRange = false;
    [SerializeField] private Rigidbody rigid;
    private float spawnDuration = .5f;
    private void FinishSpawning()
    {
        //rigid.isKinematic = false;
        state = AnimStates.Idle; 
    }
    private void FixedUpdate()
    { 
        change = GetActualPositionChange();
        //Debug.Log(change);
        if (ai != null) ai.destination = destination;
        //HandleMovement();
        DetectIfShouldStopFollowingMoveOrder();

        if (delay >= 24) //0 to 24 is half of 50
        {
            delay = 0;
            if ((!followingMoveOrder || attackMoving) && state != AnimStates.Build)
            { 
                CheckTargetEnemy();
            }
        }
        else
        {
            delay++;
        }  
        if (buildTarget != null && buildTarget.fullyBuilt)
        {
            buildTarget = null;
        }

        if (selector != null)
        { 
            selector.UpdateTargetIndicator();
        }
        if (animsEnabled) UpdateAnimations();
    }
    //50 fps fixed update
    private int delay = 0; 
    private int basicallyIdleInstances = 0;
    private int idleThreshold = 30;
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
                Debug.Log("moving to ordered destination");
                destination = orderedDestination;
            }
        }
    }
    bool playedAttackMoveSound = false;
    private void UpdateAnimations()
    {
        switch (state)
        {
            case AnimStates.Spawn:
                break;
            case AnimStates.Idle:
                if (anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk"))
                { 
                    anim.SetTrigger("Idle");
                }
                else
                { 
                    anim.Play("Idle");
                }
                if (enemyInRange && !followingMoveOrder && attackReady)
                {
                    anim.ResetTrigger("Idle");
                    StartAttack();
                }
                else if (buildTarget != null && Vector3.Distance(transform.position, buildTarget.transform.position) <= attackRange && attackReady) //try to build
                {
                    anim.ResetTrigger("Idle");
                    StartBuild();
                }
                else if (buildTarget != null && Vector3.Distance(transform.position, buildTarget.transform.position) > attackRange) //move towards build target
                {
                    anim.ResetTrigger("Idle");
                    state = AnimStates.Walk;
                }
                else if (harvestTarget != null && Vector3.Distance(transform.position, harvestTarget.transform.position) <= attackRange && attackReady) //try to harvest
                {
                    anim.ResetTrigger("Idle");
                    StartHarvest();
                }
                else if (harvestTarget != null && Vector3.Distance(transform.position, harvestTarget.transform.position) > attackRange) //move towards harvest target
                {
                    anim.ResetTrigger("Idle");
                    state = AnimStates.Walk;
                }
                else if (followingMoveOrder || chasingEnemy)
                {
                    anim.ResetTrigger("Idle");
                    state = AnimStates.Walk;
                }

                break;
            case AnimStates.Walk:
                ai.canMove = true;
                if (attackMoving)
                {
                    if (!anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalkStart") && !anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk"))
                    { 
                        if (!playedAttackMoveSound)
                        {
                            playedAttackMoveSound = true;
                            selector.SimplePlaySound(2);
                        }
                        anim.Play("AttackWalkStart"); 
                    }
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
                }
                if (enemyInRange && (!followingMoveOrder || attackMoving) && attackReady)
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
                else if (!followingMoveOrder && !chasingEnemy && (buildTarget == null || buildTarget.fullyBuilt))
                {
                    anim.ResetTrigger("Walk");
                    playedAttackMoveSound = false;
                    state = AnimStates.Idle;
                }
                break;
            case AnimStates.Attack:
                anim.Play("Attack");
                ai.canMove = moveWhileAttacking;
                if (targetEnemy != null)
                { 
                    transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, targetEnemy.transform.position - transform.position, Time.deltaTime * ai.rotationSpeed, 0));
                     
                } 
                break;
            case AnimStates.Build:
                anim.Play("Attack");
                if (buildTarget != null)
                { 
                    transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, buildTarget.transform.position - transform.position, Time.deltaTime * ai.rotationSpeed, 0));
                     
                    if (Vector3.Distance(transform.position, buildTarget.transform.position) > attackRange)
                    {
                        state = AnimStates.Walk;
                    }
                } 
                break;
            case AnimStates.Harvest:
                anim.Play("Attack");
                if (harvestTarget != null)
                {
                    transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, harvestTarget.transform.position - transform.position, Time.deltaTime * ai.rotationSpeed, 0));

                    if (Vector3.Distance(transform.position, harvestTarget.transform.position) > attackRange)
                    {
                        state = AnimStates.Walk;
                    }
                }
                break;
            case AnimStates.Die:
                anim.Play("Die");
                break;
            default:
                break;
        }
    }
    private sbyte harvestAmount = 1;
    [SerializeField] private bool moveWhileAttacking = false;
    private void CancelAttack()
    {
        targetEnemy = null;
        buildTarget = null;
        state = AnimStates.Idle;
        CancelInvoke("SimpleDamageEnemy");
        CancelInvoke("SimpleBuildTarget");
        selector.UpdateIndicator();
    } 
    private sbyte buildDelta = 1;
    [SerializeField] private float areaOfEffect = 1;
    private void StartBuild()
    {
        attackReady = false;
        state = AnimStates.Build;
        destination = transform.position;
        Invoke("SimpleBuildTarget", impactTime);
        Invoke("ReturnState", attackDuration);
    }
    public void SimpleBuildTarget() //since hp is a network variable, changing it on the server will propagate changes to clients as well
    {
        //fire locally
        selector.SimplePlaySound(1);

        if (buildTarget != null)
        {
            if (IsServer)
            {
                buildTarget.BuildThis(buildDelta);
            }
            else //client tell server to change the network variable
            {
                RequestBuildServerRpc(buildDelta, buildTarget);
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
        state = AnimStates.Attack;
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
    private void StartHarvest()
    { 
        attackReady = false;
        state = AnimStates.Harvest;
        destination = transform.position;
        Invoke("HarvestTarget", impactTime);
        Invoke("ReturnState", attackDuration);
    }
    private void HarvestTarget()
    { 
        selector.SimplePlaySound(1);

        if (harvestTarget != null)
        {  
            int actualHarvested = Mathf.Clamp(harvestAmount, 0, harvestTarget.hitPoints.Value); 

            if (IsServer)
            {
                harvestTarget.Harvest(harvestAmount);
            }
            else //client tell server to change the network variable
            {
                RequestHarvestServerRpc(harvestAmount, harvestTarget);
            }

            switch (selector.harvestType)
            {
                case SelectableEntity.HarvestType.Gold: 
                    selector.harvestedGold += actualHarvested;
                    break;
                default:
                    break;
            }
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
    private void ReturnState()
    {  
        switch (attackType)
        {
            case AttackType.Instant:
                state = AnimStates.Idle;
                attackReady = true;
                break;
            case AttackType.SelfDestruct:
                break;
            default:
                break;
        }
    }
    private float GetActualPositionChange()
    {
        float dist = Vector3.SqrMagnitude(transform.position - oldPosition);
        oldPosition = transform.position;
        return dist/Time.deltaTime;
    }

    public bool attackMoving = false;
    public Vector3 orderedDestination;
    public void SetDestinationToPos(Vector3 pos)
    {
        followingMoveOrder = true;
        destination = pos;
        orderedDestination = destination;
        state = AnimStates.Walk;
    }
    public void SetDestinationRaycast(bool attackMove = false)
    {
        if (state != AnimStates.Spawn)
        {
            targetEnemy = null;
            buildTarget = null;
            harvestTarget = null;
            enemyInRange = false;
            basicallyIdleInstances = 0;
            followingMoveOrder = true;
            attackMoving = attackMove;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity))
            {
                SelectableEntity select = hit.collider.GetComponent<SelectableEntity>();
                if (select != null)
                {
                    if (select.teamBehavior == SelectableEntity.TeamBehavior.OwnerTeam)
                    { 
                        if (select.net.OwnerClientId == selector.net.OwnerClientId) //same team
                        {
                            if (selector.type == SelectableEntity.EntityTypes.Builder && !select.fullyBuilt)
                            {
                                buildTarget = select;
                            }
                        }
                        else
                        {
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
                            if (selector.canGather)
                            {
                                harvestTarget = select;
                            }
                        }
                    } 
                }

                destination = hit.point;
                orderedDestination = destination;
                state = AnimStates.Walk;
                CancelInvoke("SimpleDamageEnemy");
                if (attackMoving) //more responsive?
                {
                    CheckTargetEnemy();
                }
            }
        } 
    }
    private SelectableEntity harvestTarget;
    /*private void HandleMovement()
    {
        transform.position = Vector3.MoveTowards(transform.position, destination, Time.deltaTime * speed);
        transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, destination - transform.position, Time.deltaTime * rotSpeed, 0));
        //transform.rotation = Quaternion.LookRotation(destination - transform.position);
    } */
}
