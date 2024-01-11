using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding;

//used for entities that can attack
[RequireComponent(typeof(SelectableEntity))]
public class MinionController : NetworkBehaviour
{
    #region Enums
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
        AfterDepositCheck,
        WalkToGarrisonable,
        Garrisoning,
        AfterGarrisonCheck,
        FindGarrisonable,
        WalkToRally
    }

    public enum AttackType
    {
        Instant, SelfDestruct, Artillery,
        Gatling, //for gatling gun
        None
    }
    #endregion
    #region Hidden
    LayerMask enemyMask;
    private Camera cam;
    [HideInInspector] public SelectableEntity selectableEntity;
    private RaycastModifier rayMod;
    private Seeker seeker;
    private readonly float spawnDuration = .5f;
    private readonly sbyte buildDelta = 5;
    private readonly sbyte harvestAmount = 1;
    private int stateTimer = 0;
    private float rotationSpeed = 10f;
    [HideInInspector] public bool attackMoving = false;
    [HideInInspector] public SelectableEntity depositTarget;
    [HideInInspector] public SelectableEntity buildTarget;
    [HideInInspector] public SelectableEntity garrisonTarget;
    [HideInInspector] public bool followingMoveOrder = false;
    public Vector3 orderedDestination;
    private int basicallyIdleInstances = 0;
    private readonly int idleThreshold = 60;
    private int attackReadyTimer = 0;
    private float change;
    private readonly float walkAnimThreshold = 0.01f;
    private Vector3 oldPosition;
    private bool attackReady = true;
    [HideInInspector] AIPath ai;
    [HideInInspector] public Collider col;
    [HideInInspector] private Rigidbody rigid;
    [HideInInspector] public Vector3 destination;
    [HideInInspector] public Animator animator;
    [HideInInspector] public SelectableEntity targetEnemy;
    [HideInInspector] public MinionNetwork minionNetwork;
    bool playedAttackMoveSound = false;
    #endregion
    #region Variables
    public State state = State.Spawn;

    [Header("Behavior Settings")]
    public AttackType attackType = AttackType.Instant;
    public bool directionalAttack = false;

    [SerializeField] private float attackRange = 1;

    //[SerializeField] private LocalRotation localRotate;
    [SerializeField] private bool canMoveWhileAttacking = false;
    [SerializeField] private Transform attackEffectSpawnPosition;
    [SerializeField] private sbyte damage = 1;
    [SerializeField] private float attackDuration = 1;
    [SerializeField] private float impactTime = .5f;
    public readonly float garrisonRange = 1.1f;
    //50 fps fixed update
    //private readonly int delay = 0;
    [Header("Self-Destruct Only")]
    [SerializeField] private float selfDestructAreaOfEffect = 1; //ignore if not selfdestructer
    public bool rallyPositionSet = false;
    #endregion
    #region Core
    //private MinionObstacle obstacle;
    //[SerializeField] private SphereCollider obstacleCollider;
    void OnEnable()
    {
        ai = GetComponent<AIPath>();
        rayMod = GetComponent<RaycastModifier>();
        seeker = GetComponent<Seeker>();
        col = GetComponent<Collider>();
        selectableEntity = GetComponent<SelectableEntity>();
        minionNetwork = GetComponent<MinionNetwork>();
        animator = GetComponentInChildren<Animator>();
        rigid = GetComponent<Rigidbody>();
        //obstacle = GetComponentInChildren<MinionObstacle>();
        // Update the destination right before searching for a path as well.
        // This is enough in theory, but this script will also update the destination every
        // frame as the destination is used for debugging and may be used for other things by other
        // scripts as well. So it makes sense that it is up to date every frame.
        if (ai != null) ai.onSearchPath += Update;


    }
    private void Update()
    {
        if (ai != null) ai.destination = destination;

    }
    void OnDisable()
    {
        if (ai != null) ai.onSearchPath -= Update;
    }
    private void Awake()
    {
        enemyMask = LayerMask.GetMask("Entity", "Obstacle");
        cam = Camera.main;

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
        state = State.Spawn;
        Invoke(nameof(FinishSpawning), spawnDuration);
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
    #endregion
    #region UpdaterFunctions
    private void UpdateColliderStatus()
    {
        if (rigid != null)
        {
            rigid.isKinematic = state switch
            {
                State.FindHarvestable or State.WalkToHarvestable or State.Harvesting or State.AfterHarvestCheck
                or State.FindDeposit or State.WalkToDeposit or State.Depositing or State.AfterDepositCheck 
                or State.FindBuildable or State.WalkToBuildable or State.Building or State.AfterBuildCheck => true,
                _ => false,
            };
        }
        if (selectableEntity.RVO != null)
        {
            selectableEntity.RVO.enabled = state switch
            {
                State.FindHarvestable or State.WalkToHarvestable or State.Harvesting or State.AfterHarvestCheck
                or State.FindDeposit or State.WalkToDeposit or State.Depositing or State.AfterDepositCheck => false,
                _ => true,
            };
        }
    }
    private void EnsureNotHarvestingFromBusy()
    {
        if (selectableEntity.harvestTarget != null && selectableEntity.harvestTarget.alive) //if we have a harvest target
        {
            if (!selectableEntity.harvestTarget.interactors.Contains(selectableEntity)) //if we are not in harvester list
            {
                if (selectableEntity.harvestTarget.interactors.Count < selectableEntity.harvestTarget.allowedInteractors) //if there is space
                {
                    //add us
                    selectableEntity.harvestTarget.interactors.Add(selectableEntity);
                }
                else //there is no space
                {
                    //get a new harvest target
                    selectableEntity.harvestTarget = null;
                }
            }
        }
    }
    private void EnsureNotBuildingBusy()
    {
        if (buildTarget != null && buildTarget.alive) //if we have a harvest target
        {
            if (!buildTarget.interactors.Contains(selectableEntity)) //if we are not in harvester list
            {
                if (buildTarget.interactors.Count < buildTarget.allowedInteractors) //if there is space
                {
                    //add us
                    buildTarget.interactors.Add(selectableEntity);
                }
                else //there is no space
                {
                    //get a new harvest target
                    buildTarget = null;
                }
            }
        }
    }
    #endregion
    #region SetterFunctions

    public void PrepareForDeath()
    {
        Global.Instance.localPlayer.selectedEntities.Remove(selectableEntity);
        selectableEntity.Select(false);
        state = State.Die;
        ai.enabled = false;
        rayMod.enabled = false;
        seeker.enabled = false;
        Destroy(rigid);
        Destroy(col);
    }

    private void FinishSpawning()
    {
        state = State.Idle;
    }
    public void SetBuildDestination(Vector3 pos, SelectableEntity ent)
    {
        destination = pos;
        buildTarget = ent;
    }
    #endregion
    #region DetectionFunctions
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
        if (ai.slowWhenNotFacingTarget || ai.maxSpeed <= 1) //because they're slow so special case
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
    #endregion
    #region More Stuff
    private void HideMoveIndicator()
    {
        if (selectableEntity != null)
        {
            selectableEntity.HideMoveIndicator();
        }
    }
    #endregion
    private bool TargetEnemyValid()
    {
        if (targetEnemy == null || targetEnemy.alive == false || targetEnemy.isTargetable.Value == false)
        {
            return false;
        }
        else
        {
            return true;
        }
    }
    private void PlaceOnGroundIfNecessary()
    {
        if (selectableEntity.occupiedGarrison == null && (transform.position.y > 0.1f || transform.position.y < 0.1f))
        {
            PlaceOnGround();
        }
    }
    /// <summary>
    /// Freeze rigidbody. Defaults to completely freezing it.
    /// </summary>
    /// <param name="freezePosition"></param>
    /// <param name="freezeRotation"></param>
    private void FreezeRigid(bool freezePosition = true, bool freezeRotation = true)
    {
        //ai.canMove = true;
        ai.canMove = !freezePosition;
        
        RigidbodyConstraints posCon;
        RigidbodyConstraints rotCon;
        //if (obstacle != null) obstacle.affectGraph = freezePosition; //should the minion act as a pathfinding obstacle?
        //obstacleCollider.enabled = freezePosition;
        if (freezePosition)
        {
            posCon = RigidbodyConstraints.FreezePosition;
        }
        else
        {
            posCon = RigidbodyConstraints.FreezePositionY;
        }
        //posCon = RigidbodyConstraints.FreezePositionY;
        if (freezeRotation)
        {
            rotCon = RigidbodyConstraints.FreezeRotation;
        }
        else
        {
            rotCon = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }
        if (rigid != null) rigid.constraints = posCon | rotCon;
    }
    private void LookAtTarget(Transform target)
    {
        transform.rotation = Quaternion.LookRotation(
            Vector3.RotateTowards(transform.forward, target.position - transform.position, Time.deltaTime * rotationSpeed, 0));
    }
    private bool InRangeOfEntity(SelectableEntity target, float range)
    {
        if (target.physicalCollider != null)
        {
            Vector3 closest = target.physicalCollider.ClosestPoint(transform.position);
            return Vector3.Distance(transform.position, closest) <= range;
        }
        else
        {
            return Vector3.Distance(transform.position, target.transform.position) <= range;
        }
    }
    private void UpdateState()
    {
        EnsureNotHarvestingFromBusy();
        EnsureNotBuildingBusy();
        UpdateColliderStatus();
        UpdateAttackReadiness();
        if (attackType == AttackType.Gatling)
        {
            animator.SetFloat("AttackSpeed", 0);
        }

        switch (state)
        {
            #region defaults
            case State.Spawn: //don't really do anything, just play the spawn animation
                animator.Play("Spawn");
                FreezeRigid();
                destination = transform.position;
                break;
            case State.Die:
                animator.Play("Die");
                FreezeRigid();
                break;
            case State.Idle:
                HideMoveIndicator();
                animator.Play("Idle");
                FreezeRigid();
                destination = transform.position;

                if (rallyPositionSet)
                {
                    basicallyIdleInstances = 0;
                    destination = orderedDestination;
                    state = State.WalkToRally;
                    break;
                }
                if (TargetEnemyValid())
                {
                    state = State.WalkToEnemy;
                    break;
                }
                else
                {
                    targetEnemy = GetClosestEnemy();
                }
                if (selectableEntity.type == SelectableEntity.EntityTypes.Builder)
                {
                    if (buildTarget == null || buildTarget.fullyBuilt)
                    {
                        buildTarget = FindClosestBuildable();
                    }
                    else
                    {
                        state = State.WalkToBuildable;
                        break;
                    }
                }
                break;
            case State.Walk:
                UpdateMoveIndicator();
                FreezeRigid(false, false);
                destination = orderedDestination;
                animator.Play("Walk"); 

                if (DetectIfStuck()) //!followingMoveOrder && !chasingEnemy && (buildTarget == null || buildTarget.fullyBuilt)
                {
                    //anim.ResetTrigger("Walk");
                    //playedAttackMoveSound = false;
                    state = State.Idle;
                    break;
                }
                if (selectableEntity.occupiedGarrison != null)
                {
                    state = State.Idle;
                    break;
                }
                break;
            case State.WalkToRally:
                FreezeRigid(false, false);
                UpdateMoveIndicator();
                destination = orderedDestination;
                animator.Play("Walk");
                if (Vector3.Distance(transform.position, orderedDestination) <= 0.1f)
                {
                    state = State.Idle;
                    rallyPositionSet = false;
                    break;
                }
                break;
            #endregion
            #region Attacking
            case State.WalkBeginFindEnemies: //"ATTACK MOVE" 
                UpdateMoveIndicator();
                FreezeRigid(false, false);
                destination = orderedDestination;

                if (!animator.GetCurrentAnimatorStateInfo(0).IsName("AttackWalkStart") && !animator.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk"))
                {
                    if (!playedAttackMoveSound)
                    {
                        playedAttackMoveSound = true;
                        selectableEntity.SimplePlaySound(2);
                    }
                    animator.Play("AttackWalkStart");
                }

                if (TargetEnemyValid())
                {
                    state = State.WalkToEnemy;
                }
                else
                {
                    targetEnemy = GetClosestEnemy();
                }

                if (DetectIfStuck())
                { 
                    state = State.Idle;
                }
                if (selectableEntity.occupiedGarrison != null)
                {
                    state = State.Idle;
                    break;
                }
                break;
            case State.WalkContinueFindEnemies: 
                UpdateMoveIndicator();
                FreezeRigid(false, false);
                destination = orderedDestination;
                
                if (TargetEnemyValid())
                {
                    state = State.WalkToEnemy;
                }
                else
                {
                    targetEnemy = GetClosestEnemy();
                }

                if (DetectIfStuck())
                {
                    state = State.Idle;
                }
                if (selectableEntity.occupiedGarrison != null)
                {
                    state = State.Idle;
                    break;
                }
                break;
            case State.WalkToEnemy:
                FreezeRigid(false, false);
                if (TargetEnemyValid())
                {
                    
                    UpdateAttackIndicator();
                    if (Vector3.Distance(transform.position, targetEnemy.transform.position) > attackRange)
                    {
                        if (selectableEntity.occupiedGarrison != null)
                        {
                            state = State.Idle;
                            break;
                        }
                        animator.Play("AttackWalk");
                        destination = targetEnemy.transform.position;
                    }
                    else
                    {
                        stateTimer = 0;
                        state = State.Attacking;
                        break;
                    }
                }
                else
                { 
                    state = State.WalkContinueFindEnemies;
                }
                break;
            case State.Attacking: 
                if (attackType == AttackType.Gatling)
                {
                    animator.SetFloat("AttackSpeed", 1);
                }
                if (!TargetEnemyValid() && !attackReady)
                {
                    state = State.Idle;
                }
                else if (!TargetEnemyValid() && attackReady)
                {
                    state = State.WalkContinueFindEnemies;
                }
                else if (Vector3.Distance(transform.position, targetEnemy.transform.position) <= attackRange)
                {
                    UpdateAttackIndicator();
                    FreezeRigid(!canMoveWhileAttacking, false);
                    rotationSpeed = ai.rotationSpeed / 60;
                    LookAtTarget(targetEnemy.transform);
                    
                    if (attackReady && CheckFacingTowards(targetEnemy.transform.position))
                    { 
                        animator.Play("Attack");  
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
                                        Explode(selfDestructAreaOfEffect);
                                        break;
                                    case AttackType.Artillery:
                                        ShootProjectileAtPosition(targetEnemy.transform.position);
                                        break;
                                    case AttackType.Gatling:
                                        DamageSpecifiedEnemy(targetEnemy, damage);
                                        break;
                                    case AttackType.None:
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
                    else if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
                    {
                        animator.Play("Idle");
                    }
                }
                else //walk to enemy if out of range
                {
                    state = State.WalkToEnemy;
                }
                break;
            case State.AfterAttackCheck:
                FreezeRigid();
                if (attackType == AttackType.Gatling)
                {
                    animator.SetFloat("AttackSpeed", 1);
                }
                animator.Play("Idle"); 
                if (!TargetEnemyValid())
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
                FreezeRigid();
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
                FreezeRigid(false, false);
                UpdateMoveIndicator();
                if (buildTarget == null)
                {
                    state = State.FindBuildable;
                }
                else
                {
                    if (InRangeOfEntity(buildTarget, attackRange))
                    {
                        stateTimer = 0;
                        state = State.Building;
                    }
                    else
                    {
                        animator.Play("Walk");
                        destination = buildTarget.transform.position;
                    }
                }
                break;
            case State.Building:
                if (buildTarget == null || !InRangeOfEntity(buildTarget, attackRange))
                {
                    state = State.FindBuildable;
                }
                else
                {
                    FreezeRigid(true, false);
                    LookAtTarget(buildTarget.transform);
                    
                    if (attackReady)
                    {
                        animator.Play("Attack");

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
                animator.Play("Idle");
                FreezeRigid();
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
                FreezeRigid();
                if (selectableEntity.harvestTarget == null || !selectableEntity.harvestTarget.alive)
                {
                    selectableEntity.harvestTarget = FindClosestHarvestable();
                }
                else
                {
                    state = State.WalkToHarvestable;
                }
                break;
            case State.WalkToHarvestable:
                UpdateMoveIndicator();
                FreezeRigid(false, false);
                if (selectableEntity.harvestTarget == null)
                {
                    state = State.FindHarvestable;
                }
                else
                {
                    if (Vector3.Distance(transform.position, selectableEntity.harvestTarget.transform.position) > attackRange) //if out of harvest range walk there
                    {
                        animator.Play("Walk");
                        destination = selectableEntity.harvestTarget.transform.position;
                    }
                    else
                    {
                        stateTimer = 0;
                        state = State.Harvesting;
                    }
                }
                break;
            case State.Harvesting:
                if (selectableEntity.harvestTarget == null)
                {
                    state = State.FindHarvestable;
                }
                else
                {
                    FreezeRigid(true, false);

                    LookAtTarget(selectableEntity.harvestTarget.transform);
                    
                    if (attackReady)
                    {
                        animator.Play("Attack");

                        if (AnimatorPlaying())
                        {
                            if (stateTimer < ConvertTimeToFrames(impactTime))
                            {
                                stateTimer++;
                            }
                            else if (attackReady)
                            {
                                attackReady = false;
                                HarvestTarget(selectableEntity.harvestTarget);
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
                animator.Play("Idle");
                FreezeRigid();
                if (selectableEntity.harvestedResourceAmount >= selectableEntity.harvestCapacity)
                {
                    state = State.FindDeposit;
                }
                else if (selectableEntity.harvestTarget != null)
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
                FreezeRigid();
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
                FreezeRigid(false, false);
                if (depositTarget == null)
                {
                    state = State.FindDeposit;
                }
                else
                {
                    if (Vector3.Distance(transform.position, depositTarget.transform.position) > attackRange)
                    {
                        ai.canMove = true;
                        animator.Play("Walk");
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
                    FreezeRigid(true, false);
                    LookAtTarget(depositTarget.transform);
                    
                    //anim.Play("Attack"); //replace with deposit animation
                    //instant dropoff
                    if (selectableEntity != null)
                    {
                        Global.Instance.localPlayer.gold += selectableEntity.harvestedResourceAmount;
                        selectableEntity.harvestedResourceAmount = 0;
                        Global.Instance.localPlayer.UpdateGUIFromSelections();
                    }
                    state = State.AfterDepositCheck;
                }
                break;
            case State.AfterDepositCheck:
                animator.Play("Idle");
                FreezeRigid();
                if (selectableEntity.harvestTarget != null)
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
            #region Garrison
            case State.WalkToGarrisonable:
                UpdateMoveIndicator();
                FreezeRigid(false, false);
                if (garrisonTarget == null)
                {
                    state = State.FindGarrisonable; //later make this check for nearby garrisonables in the same target?
                }
                else
                {
                    if (garrisonTarget.type == SelectableEntity.EntityTypes.Portal) //walk into
                    {
                        if (selectableEntity.tryingToTeleport)
                        {
                            ai.canMove = true;
                            animator.Play("Walk");
                            destination = garrisonTarget.transform.position;
                        }
                        else
                        {
                            state = State.Idle;
                        }
                    }
                    else
                    {
                        if (Vector3.Distance(transform.position, garrisonTarget.transform.position) > garrisonRange)
                        {
                            ai.canMove = true;
                            animator.Play("Walk");
                            destination = garrisonTarget.transform.position;
                        }
                        else
                        {
                            state = State.Garrisoning;
                        }
                    }
                }
                break;
            case State.Garrisoning:
                if (garrisonTarget == null)
                {
                    state = State.FindGarrisonable;
                }
                else
                {
                    FreezeRigid(true, false);
                    //garrison into
                    garrisonTarget.ReceivePassenger(this);
                    state = State.Idle;
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

    private void UpdateAttackIndicator()
    { 
        if (selectableEntity != null)
        {
            if (Input.GetKey(KeyCode.Space) && targetEnemy != null)
            {
                selectableEntity.UpdateAttackIndicator();
            }
            else
            {
                selectableEntity.HideMoveIndicator();
            }
        }
    }
    /// <summary>
    /// Update the indicator that shows where the minion will be moving towards.
    /// </summary>
    private void UpdateMoveIndicator()
    {
        if (selectableEntity != null)
        {
            if (Input.GetKey(KeyCode.Space))
            { 
                selectableEntity.UpdateMoveIndicator();
            }
            else
            {
                selectableEntity.HideMoveIndicator();
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
        return animator.GetCurrentAnimatorStateInfo(0).length > animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
    }
    private void HarvestTarget(SelectableEntity target)
    {
        selectableEntity.SimplePlaySound(1); //play impact sound 
        if (target != null)
        {
            int actualHarvested = Mathf.Clamp(harvestAmount, 0, target.hitPoints.Value); //max amount we can harvest clamped by hitpoints remaining
            int diff = selectableEntity.harvestCapacity - selectableEntity.harvestedResourceAmount;
            actualHarvested = Mathf.Clamp(actualHarvested, 0, diff); //max amount we can harvest clamped by remaining carrying capacity
            if (IsServer)
            {
                target.Harvest(harvestAmount);
            }
            else //client tell server to change the network variable
            {
                RequestHarvestServerRpc(harvestAmount, target);
            }

            selectableEntity.harvestedResourceAmount += actualHarvested;
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
            if (item != null && !item.fullyBuilt && item.interactors.Count < item.allowedInteractors)
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
    #region FindClosest
    private Collider[] debugNearby = new Collider[100];
    private SelectableEntity GetClosestEnemy() //gets pretty expensive
    {
        float range = attackRange;// + 2;

        int maxColliders = Mathf.RoundToInt(40 * range);
        Collider[] hitColliders = new Collider[maxColliders];
        debugNearby = hitColliders;
        int numColliders = Physics.OverlapSphereNonAlloc(transform.position, range, hitColliders, enemyMask);
        Collider closest = null;
        float distance = Mathf.Infinity;
        float threshold = -Mathf.Infinity;
        Vector3 forward = transform.TransformDirection(Vector3.forward).normalized;
        for (int i = 0; i < numColliders; i++) //this part is expensive
        {
            if (hitColliders[i].gameObject == gameObject || hitColliders[i].isTrigger) //skip self and triggers
            {
                continue;
            }
            SelectableEntity select = hitColliders[i].GetComponent<SelectableEntity>();
            if (select != null) //conditions that disqualify an entity from being targeted
            {
                if (!select.alive)
                {
                    continue;
                }
                if (select.isTargetable.Value == false)
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
            /*if (enemy.occupiedGarrison != null) //if garrisoned
            {
                enemy = enemy.occupiedGarrison;
            }*/
        }

        return enemy;
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
                if (item.interactors.Count < item.allowedInteractors) //there is space for a new harvester
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
    #endregion
    #region Attacks
    private void CancelAttack()
    {
        targetEnemy = null;
        buildTarget = null;
        state = State.Idle;
        CancelInvoke("SimpleDamageEnemy");
        CancelInvoke("SimpleBuildTarget");
        selectableEntity.UpdateIndicator();
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
    public void BuildTarget(SelectableEntity target) //since hp is a network variable, changing it on the server will propagate changes to clients as well
    {
        //fire locally
        selectableEntity.SimplePlaySound(1);

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
    private void RequestBuildServerRpc(sbyte buildDelta, NetworkBehaviourReference target)
    {
        //server must handle damage! 
        if (target.TryGet(out SelectableEntity select))
        {
            select.BuildThis(buildDelta);
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
                Explode(selfDestructAreaOfEffect);
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
        Global.Instance.localPlayer.CreateExplosionAtPoint(transform.position, explodeRadius, damage);
        DamageSpecifiedEnemy(selectableEntity, damage);
        SimpleExplosionEffect(transform.position);
    }  
    private void SimpleExplosionEffect(Vector3 pos)
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
            selectableEntity.SimplePlaySound(1);
            if (selectableEntity.attackEffects.Length > 0) //show muzzle flash
            {
                selectableEntity.DisplayAttackEffects();
            }
            if (selectableEntity.type == SelectableEntity.EntityTypes.Ranged ) //shoot trail
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
        Projectile proj = Instantiate(Global.Instance.cannonBall, spawnPos, Quaternion.identity);
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

    public void SimpleDamageEnemy() //since hp is a network variable, changing it on the server will propagate changes to clients as well
    {
        if (targetEnemy != null)
        { 
            //fire locally
            selectableEntity.SimplePlaySound(1);
            if (selectableEntity.attackEffects.Length > 0)
            {
                selectableEntity.DisplayAttackEffects();
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

    private float GetActualPositionChange()
    {
        float dist = Vector3.SqrMagnitude(transform.position - oldPosition);
        oldPosition = transform.position;
        return dist/Time.deltaTime;
    }

    /*public void RallyToPos(Vector3 pos)
    {
        //followingMoveOrder = true;
        destination = pos;
        orderedDestination = destination;
        //state = State.Walk;
    }*/
    public void SetRally(Vector3 pos)
    {
        Debug.Log("setting rally" + pos);
        rallyPositionSet = true;
        orderedDestination = pos;
    }
    private void ResetAllTargets()
    { 
        targetEnemy = null;
        buildTarget = null;
        selectableEntity.harvestTarget = null;
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
    private void PlaceOnGround()
    {
        if (Physics.Raycast(transform.position + (new Vector3 (0, 100, 0)), transform.TransformDirection(Vector3.down), out RaycastHit hit, Mathf.Infinity, Global.Instance.localPlayer.groundLayer))
        {
            transform.position = hit.point;
        }
    }
    public void ForceBuildTarget(SelectableEntity target)
    {
        buildTarget = target;
        state = State.WalkToBuildable;
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
            selectableEntity.tryingToTeleport = false;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity))
            {
                destination = hit.point;
                orderedDestination = destination;
                //check if player right clicked on an entity and what behavior unit should have
                SelectableEntity justLeftGarrison = null;
                if (selectableEntity.occupiedGarrison != null) //we are currently garrisoned
                {
                    justLeftGarrison = selectableEntity.occupiedGarrison;
                    selectableEntity.occupiedGarrison.UnloadPassenger(this); //leave garrison by moving out of it
                    PlaceOnGround(); //snap to ground
                }
                SelectableEntity select = hit.collider.GetComponent<SelectableEntity>(); 
                if (select != null)
                {
                    if (select.teamBehavior == SelectableEntity.TeamBehavior.OwnerTeam)
                    {
                        if (select.net.OwnerClientId == selectableEntity.net.OwnerClientId) //same team
                        {
                            if (select.depositType == SelectableEntity.DepositType.Gold && select.fullyBuilt) //if deposit point
                            {
                                depositTarget = select;
                                state = State.FindDeposit;
                            }
                            else if (selectableEntity.type == SelectableEntity.EntityTypes.Builder && !select.fullyBuilt) //if buildable and this is a builder
                            {
                                buildTarget = select;
                                state = State.WalkToBuildable;
                            }
                            else if (select.fullyBuilt && select.HasEmptyGarrisonablePosition() && selectableEntity.garrisonablePositions.Count <= 0) //target can be garrisoned, and passenger cannot garrison
                            {
                                if (justLeftGarrison != select) //not perfect, fails on multiple units
                                {
                                    if (select.acceptsHeavy)
                                    {
                                        garrisonTarget = select;
                                        state = State.WalkToGarrisonable;
                                    }
                                    else if (!selectableEntity.isHeavy)
                                    {
                                        garrisonTarget = select;
                                        state = State.WalkToGarrisonable;
                                    }
                                    
                                }
                            }
                            else if (select.type == SelectableEntity.EntityTypes.Portal)
                            {
                                selectableEntity.tryingToTeleport = true;
                                garrisonTarget = select;
                                state = State.WalkToGarrisonable;
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
                            if (selectableEntity.isHarvester)
                            {
                                if (selectableEntity.harvestedResourceAmount < selectableEntity.harvestCapacity) //we could still harvest more
                                { 
                                    selectableEntity.harvestTarget = select;
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
    #endregion
}
