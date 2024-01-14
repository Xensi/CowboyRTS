using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding;
using System.Threading.Tasks;

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
        FindInteractable,
        WalkToInteractable,
        Building,
        AfterBuildCheck,
        Spawn,
        Die,
        Harvesting,
        AfterHarvestCheck,
        Depositing,
        AfterDepositCheck,
        Garrisoning,
        AfterGarrisonCheck,
        WalkToRally
    }

    public enum AttackType
    {
        Instant, SelfDestruct, Artillery,
        Gatling, //for gatling gun
        None
    }
    #endregion
    public State lastState = State.Idle;
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
    //[HideInInspector] public bool followingMoveOrder = false;
    public Vector3 orderedDestination; //remembers where player told minion to go
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
    [HideInInspector] public Animator animator;
    [HideInInspector] public SelectableEntity targetEnemy;
    [HideInInspector] public MinionNetwork minionNetwork;
    bool playedAttackMoveSound = false;
    #endregion
    #region Variables

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
    #endregion
    #region NetworkVariables
    //maybe optimize this as vector 2 later
    public NetworkVariable<Vector3> realLocation = new NetworkVariable<Vector3>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    //controls where the AI will pathfind to
    public NetworkVariable<Vector3> destination = new NetworkVariable<Vector3>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public SelectableEntity.RallyMission givenMission = SelectableEntity.RallyMission.None;
    [HideInInspector]
    public NetworkVariable<State> state = new NetworkVariable<State>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
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
    }
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            realLocation.Value = transform.position;
            destination.Value = transform.position;
            state.Value = State.Spawn;
            Invoke(nameof(FinishSpawning), spawnDuration);
        }
        else
        {
            /*  rigid.isKinematic = true;
              ai.enabled = false;
              rayMod.enabled = false;
              seeker.enabled = false;*/
        }
        destination.OnValueChanged += OnDestinationChanged;
        //realLocation.OnValueChanged += OnRealLocationChanged;
        //enabled = IsOwner;
        oldPosition = transform.position; 
    }
    private void Update()
    {
        if (ai != null) ai.destination = destination.Value;
        if (IsOwner)
        {
            float updateThreshold = 0.5f;
            change = GetActualPositionChange();
            if (change > updateThreshold)
            { 
                realLocation.Value = transform.position; //only update when different enough
            }
        }
        if (!IsOwner)
        {
            CheckForLocationError();
        }
    }
    /*private void OnRealLocationChanged(Vector3 previous, Vector3 current)
    {
        
    }*/ 
    private void FixedUpdate()
    {
        if (IsOwner)
        {
            OwnerUpdateState();
        }
        else
        {
            NonOwnerUpdateState();
        }
    } 
    private void CheckForLocationError()
    {
        //owner can change real location with impunity
        //other clients: when their value for real location syncs up with owner, check if it's different enough to warrant a teleport
        //in future lerp to new location?
        float allowedError = 0.5f;
        if (!IsOwner)
        {
            if (Vector3.Distance(realLocation.Value, transform.position) > allowedError)
            {
                //Debug.Log("Telporting because distance too great");
                //transform.position = realLocation.Value;
                transform.position = LerpPosition(transform.position, realLocation.Value);
            }
        }
    }
    // Calculated start for the most recent interpolation
    Vector3 m_LerpStart;

    // Calculated time elapsed for the most recent interpolation
    float m_CurrentLerpTime;

    // The duration of the interpolation, in seconds
    float m_LerpTime = 0.3f;

    public Vector3 LerpPosition(Vector3 current, Vector3 target)
    {
        if (current != target)
        {
            m_LerpStart = current;
            m_CurrentLerpTime = 0f;
        }

        m_CurrentLerpTime += Time.deltaTime;

        /*//gentler lerp for shorter distances
        float dist = Vector3.Distance(current, target);
        float modifiedLerpTime = m_LerpTime * dist;

        if (m_CurrentLerpTime > modifiedLerpTime)
        {
            m_CurrentLerpTime = modifiedLerpTime;
        }

        var lerpPercentage = m_CurrentLerpTime / modifiedLerpTime;*/
        if (m_CurrentLerpTime > m_LerpTime)
        {
            m_CurrentLerpTime = m_LerpTime;
        }

        var lerpPercentage = m_CurrentLerpTime / m_LerpTime;

        return Vector3.Lerp(m_LerpStart, target, lerpPercentage);
    }
    private void OnDestinationChanged(Vector3 previous, Vector3 current)
    {
        ai.canMove = true; //generally, if we have received a new destination then we can move there
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
    #region States
    private bool DetectEnemy(float attackRange) //check if an enemy is in range at all, from perspective of local enemy
    {  
        int maxColliders = Mathf.RoundToInt(20 * attackRange);
        Collider[] hitColliders = new Collider[maxColliders]; 
        int numColliders = Physics.OverlapSphereNonAlloc(transform.position, attackRange, hitColliders, enemyMask); 
        for (int i = 0; i < numColliders; i++) 
        { 
            SelectableEntity select = hitColliders[i].GetComponent<SelectableEntity>();
            if (select != null) 
            {
                if (select.OwnerClientId != OwnerClientId)
                {
                    return true;
                }
                /*if (select.alive && select.isTargetable.Value && select.visibleInFog)
                {
                    if (select.teamBehavior == SelectableEntity.TeamBehavior.OwnerTeam)
                    {
                        
                    }
                } */
            } 
        } 
        return false;
    }
    private void NonOwnerUpdateState()
    {
        switch (state.Value)
        {
            case State.Idle:
                break;
            case State.Walk:
                break;
            case State.WalkBeginFindEnemies: 
            case State.WalkContinueFindEnemies:
            case State.WalkToEnemy:
            case State.Attacking:
            case State.AfterAttackCheck:
                if (DetectEnemy(attackRange))
                {
                    //default behavior is stopping when in range of enemy and trying to attack 
                    //Debug.Log("Simulated detected enemy, should stop until owner tells us something else");
                    ai.canMove = false;
                }
                break;
            case State.FindInteractable:
                break;
            case State.WalkToInteractable:
                break;
            case State.Building:
                break;
            case State.AfterBuildCheck:
                break;
            case State.Spawn:
                break;
            case State.Die:
                break;
            case State.Harvesting:
                break;
            case State.AfterHarvestCheck:
                break;
            case State.Depositing:
                break;
            case State.AfterDepositCheck:
                break;
            case State.Garrisoning:
                break;
            case State.AfterGarrisonCheck:
                break;
            case State.WalkToRally:
                break;
            default:
                break;
        }
    }
    private void OwnerUpdateState()
    {
        EnsureNotInteractingWithBusy();
        UpdateColliderStatus();
        UpdateAttackReadiness();
        if (attackType == AttackType.Gatling)
        {
            animator.SetFloat("AttackSpeed", 0);
        }
        switch (state.Value)
        {
            #region defaults
            case State.Spawn: //don't really do anything, just play the spawn animation
                animator.Play("Spawn");
                FreezeRigid();
                if (IsOwner) destination.Value = transform.position;
                break;
            case State.Die:
                animator.Play("Die");
                FreezeRigid();
                break;
            case State.Idle:
                HideMoveIndicator();
                animator.Play("Idle");
                FreezeRigid();
                if (IsOwner) destination.Value = transform.position; //stand still

                switch (givenMission)
                {
                    case SelectableEntity.RallyMission.None:
                        if (shouldAutoSeekOutEnemies)
                        {
                            if (TargetEnemyValid())
                            {
                                state.Value = State.WalkToEnemy;
                                break;
                            }
                            else
                            {
                                targetEnemy = FindClosestEnemy();
                            }
                        }
                        //only do this if not garrisoned
                        if (selectableEntity.occupiedGarrison == null)
                        {
                            if (selectableEntity.type == SelectableEntity.EntityTypes.Builder)
                            {
                                if (selectableEntity.interactionTarget == null || selectableEntity.interactionTarget.fullyBuilt)
                                {
                                    selectableEntity.interactionTarget = FindClosestBuildable();
                                }
                                else
                                {
                                    state.Value = State.WalkToInteractable;
                                    lastState = State.Building;
                                    break;
                                }
                            }
                        }
                        break;
                    case SelectableEntity.RallyMission.Move:
                        basicallyIdleInstances = 0;
                        if (IsOwner) destination.Value = orderedDestination;
                        state.Value = State.WalkToRally;
                        break;
                    case SelectableEntity.RallyMission.Harvest:
                        state.Value = State.WalkToInteractable;
                        lastState = State.Harvesting;
                        break;
                    case SelectableEntity.RallyMission.Build:
                        if (selectableEntity.type == SelectableEntity.EntityTypes.Builder)
                        {
                            if (selectableEntity.interactionTarget == null || selectableEntity.interactionTarget.fullyBuilt)
                            {
                                selectableEntity.interactionTarget = FindClosestBuildable();
                            }
                            else
                            {
                                state.Value = State.WalkToInteractable;
                                lastState = State.Building;
                            }
                        }
                        break;
                    case SelectableEntity.RallyMission.Garrison:
                        state.Value = State.WalkToInteractable;
                        lastState = State.Garrisoning;
                        break;
                    case SelectableEntity.RallyMission.Attack:
                        if (TargetEnemyValid())
                        {
                            state.Value = State.WalkToEnemy;
                            break;
                        }
                        else
                        {
                            targetEnemy = FindClosestEnemy();
                        }
                        break;
                    default:
                        break;
                }
                break;
            case State.Walk:
                UpdateMoveIndicator();
                FreezeRigid(false, false);
                destination.Value = orderedDestination;
                animator.Play("Walk");

                if (DetectIfStuck()) //!followingMoveOrder && !chasingEnemy && (selectableEntity.interactionTarget == null || selectableEntity.interactionTarget.fullyBuilt)
                {
                    //anim.ResetTrigger("Walk");
                    //playedAttackMoveSound = false;
                    state.Value = State.Idle;
                    break;
                }
                if (selectableEntity.occupiedGarrison != null)
                {
                    state.Value = State.Idle;
                    break;
                }
                break;
            case State.WalkToRally:
                FreezeRigid(false, false);
                UpdateMoveIndicator();
                if (IsOwner) destination.Value = orderedDestination;
                animator.Play("Walk");
                if (Vector3.Distance(transform.position, orderedDestination) <= 0.1f)
                {
                    state.Value = State.Idle;
                    //rallyPositionSet = false;
                    break;
                }
                break;
            #endregion
            #region Attacking
            case State.WalkBeginFindEnemies: //"ATTACK MOVE" 
                UpdateMoveIndicator();
                FreezeRigid(false, false);
                destination.Value = orderedDestination;

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
                    state.Value = State.WalkToEnemy;
                }
                else
                {
                    targetEnemy = FindClosestEnemy();
                }

                if (DetectIfStuck())
                {
                    state.Value = State.Idle;
                }
                if (selectableEntity.occupiedGarrison != null)
                {
                    state.Value = State.Idle;
                    break;
                }
                break;
            case State.WalkContinueFindEnemies:
                UpdateMoveIndicator();
                FreezeRigid(false, false);
                destination.Value = orderedDestination;

                if (TargetEnemyValid())
                {
                    state.Value = State.WalkToEnemy;
                }
                else
                {
                    targetEnemy = FindClosestEnemy();
                }

                if (DetectIfStuck())
                {
                    state.Value = State.Idle;
                }
                if (selectableEntity.occupiedGarrison != null)
                {
                    state.Value = State.Idle;
                    break;
                }
                break;
            case State.WalkToEnemy:
                FreezeRigid(false, false);
                if (TargetEnemyValid())
                {

                    UpdateAttackIndicator();
                    if (!InRangeOfEntity(targetEnemy, attackRange))
                    {
                        if (selectableEntity.occupiedGarrison != null)
                        {
                            state.Value = State.Idle;
                            break;
                        }
                        animator.Play("AttackWalk");
                        if (IsOwner) destination.Value = targetEnemy.transform.position;
                    }
                    else
                    {
                        stateTimer = 0;
                        state.Value = State.Attacking;
                        break;
                    }
                }
                else
                {
                    state.Value = State.WalkContinueFindEnemies;
                }
                break;
            case State.Attacking:
                if (attackType == AttackType.Gatling)
                {
                    animator.SetFloat("AttackSpeed", 1);
                }
                if (!TargetEnemyValid() && !attackReady)
                {
                    state.Value = State.Idle;
                }
                else if (!TargetEnemyValid() && attackReady)
                {
                    state.Value = State.WalkContinueFindEnemies;
                }
                else if (InRangeOfEntity(targetEnemy, attackRange))
                {
                    UpdateAttackIndicator();
                    FreezeRigid(!canMoveWhileAttacking, false);
                    if (IsOwner) destination.Value = transform.position; //stop in place
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
                            state.Value = State.AfterAttackCheck;
                        }
                    }
                    else if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
                    {
                        animator.Play("Idle");
                    }
                }
                else //walk to enemy if out of range
                {
                    state.Value = State.WalkToEnemy;
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
                    state.Value = State.WalkContinueFindEnemies;
                }
                else //if target enemy is alive
                {
                    stateTimer = 0;
                    state.Value = State.Attacking;
                }
                break;
            #endregion
            #region Building
            case State.FindInteractable:
                FreezeRigid();
                animator.Play("Idle");
                //prioritize based on last state.Value
                switch (lastState)
                {
                    case State.Building:
                        if (InvalidBuildable(selectableEntity.interactionTarget))
                        {
                            selectableEntity.interactionTarget = FindClosestBuildable();
                        }
                        else
                        {
                            state.Value = State.WalkToInteractable;
                        }
                        break;
                    case State.Harvesting:
                        if (InvalidHarvestable(selectableEntity.interactionTarget))
                        {
                            selectableEntity.interactionTarget = FindClosestHarvestable();
                        }
                        else
                        {
                            state.Value = State.WalkToInteractable;
                        }
                        break;
                    case State.Depositing:
                        if (InvalidDeposit(selectableEntity.interactionTarget))
                        {
                            selectableEntity.interactionTarget = FindClosestDeposit();
                        }
                        else
                        {
                            state.Value = State.WalkToInteractable;
                        }
                        break;
                    default:
                        break;
                }
                break;
            case State.WalkToInteractable:
                //garrisoned units should not interact
                if (selectableEntity.occupiedGarrison != null)
                {
                    selectableEntity.interactionTarget = null;
                    state.Value = State.Idle;
                }

                FreezeRigid(false, false);
                UpdateMoveIndicator();
                switch (lastState)
                {
                    case State.Building:
                        if (InvalidBuildable(selectableEntity.interactionTarget))
                        {
                            state.Value = State.FindInteractable;
                        }
                        else
                        {
                            if (InRangeOfEntity(selectableEntity.interactionTarget, attackRange))
                            {
                                stateTimer = 0;
                                state.Value = State.Building;
                            }
                            else
                            {
                                animator.Play("Walk");
                                if (IsOwner) destination.Value = selectableEntity.interactionTarget.transform.position;
                            }
                        }
                        break;
                    case State.Harvesting:
                        if (InvalidHarvestable(selectableEntity.interactionTarget))
                        {
                            state.Value = State.FindInteractable;
                        }
                        else
                        {
                            if (InRangeOfEntity(selectableEntity.interactionTarget, attackRange))
                            {
                                stateTimer = 0;
                                state.Value = State.Harvesting;
                            }
                            else
                            {
                                animator.Play("Walk");
                                if (IsOwner) destination.Value = selectableEntity.interactionTarget.transform.position;
                            }
                        }
                        break;
                    case State.Depositing:
                        if (InvalidDeposit(selectableEntity.interactionTarget))
                        {
                            state.Value = State.FindInteractable;
                        }
                        else
                        {
                            if (InRangeOfEntity(selectableEntity.interactionTarget, attackRange))
                            {
                                state.Value = State.Depositing;
                            }
                            else
                            {
                                animator.Play("Walk");
                                if (IsOwner) destination.Value = selectableEntity.interactionTarget.transform.position;
                            }
                        }
                        break;
                    case State.Garrisoning:
                        if (selectableEntity.interactionTarget == null)
                        {
                            state.Value = State.FindInteractable; //later make this check for nearby garrisonables in the same target?
                        }
                        else
                        {
                            if (selectableEntity.interactionTarget.type == SelectableEntity.EntityTypes.Portal) //walk into
                            {
                                if (selectableEntity.tryingToTeleport)
                                {
                                    animator.Play("Walk");
                                    if (IsOwner) destination.Value = selectableEntity.interactionTarget.transform.position;
                                }
                                else
                                {
                                    state.Value = State.Idle;
                                }
                            }
                            else
                            {
                                if (InRangeOfEntity(selectableEntity.interactionTarget, garrisonRange))
                                {
                                    state.Value = State.Garrisoning;
                                }
                                else
                                {
                                    animator.Play("Walk");
                                    if (IsOwner) destination.Value = selectableEntity.interactionTarget.transform.position;
                                }
                            }
                        }
                        break;
                    default:
                        break;
                }
                break;
            case State.Building:
                if (InvalidBuildable(selectableEntity.interactionTarget) || !InRangeOfEntity(selectableEntity.interactionTarget, attackRange))
                {
                    state.Value = State.FindInteractable;
                    lastState = State.Building;
                }
                else
                {
                    FreezeRigid(true, false);
                    LookAtTarget(selectableEntity.interactionTarget.transform);

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
                                BuildTarget(selectableEntity.interactionTarget);
                            }
                        }
                        else //animation finished
                        {
                            state.Value = State.AfterBuildCheck;
                        }
                    }
                }
                break;
            case State.AfterBuildCheck:
                animator.Play("Idle");
                FreezeRigid();
                if (InvalidBuildable(selectableEntity.interactionTarget))
                {
                    state.Value = State.FindInteractable;
                    lastState = State.Building;
                }
                else
                {
                    stateTimer = 0;
                    state.Value = State.Building;
                }
                break;
            #endregion
            #region Harvestable  
            case State.Harvesting:
                if (InvalidHarvestable(selectableEntity.interactionTarget))
                {
                    state.Value = State.FindInteractable;
                    lastState = State.Harvesting;
                }
                else
                {
                    FreezeRigid(true, false);

                    LookAtTarget(selectableEntity.interactionTarget.transform);

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
                                HarvestTarget(selectableEntity.interactionTarget);
                            }
                        }
                        else //animation finished
                        {
                            state.Value = State.AfterHarvestCheck;
                        }
                    }
                }
                break;
            case State.AfterHarvestCheck:
                animator.Play("Idle");
                FreezeRigid();
                if (selectableEntity.harvestedResourceAmount >= selectableEntity.harvestCapacity)
                {
                    state.Value = State.FindInteractable;
                    lastState = State.Depositing;
                }
                else if (InvalidHarvestable(selectableEntity.interactionTarget))
                {
                    stateTimer = 0;
                    state.Value = State.Harvesting;
                }
                else
                {
                    state.Value = State.FindInteractable;
                    lastState = State.Harvesting;
                }
                break;
            case State.Depositing:
                if (InvalidDeposit(selectableEntity.interactionTarget))
                {
                    state.Value = State.FindInteractable;
                    lastState = State.Depositing;
                }
                else
                {
                    FreezeRigid(true, false);
                    LookAtTarget(selectableEntity.interactionTarget.transform);

                    //anim.Play("Attack"); //replace with deposit animation
                    //instant dropoff
                    if (selectableEntity != null)
                    {
                        Global.Instance.localPlayer.gold += selectableEntity.harvestedResourceAmount;
                        selectableEntity.harvestedResourceAmount = 0;
                        Global.Instance.localPlayer.UpdateGUIFromSelections();
                    }
                    state.Value = State.AfterDepositCheck;
                }
                break;
            case State.AfterDepositCheck:
                animator.Play("Idle");
                FreezeRigid();
                if (InvalidHarvestable(selectableEntity.interactionTarget))
                {
                    stateTimer = 0;
                    state.Value = State.WalkToInteractable;
                    lastState = State.Harvesting;
                }
                else
                {
                    state.Value = State.FindInteractable;
                    lastState = State.Harvesting;
                }
                break;
            #endregion
            #region Garrison
            case State.Garrisoning:
                if (selectableEntity.interactionTarget == null)
                {
                    state.Value = State.FindInteractable;
                    lastState = State.Garrisoning;
                }
                else
                {
                    FreezeRigid(true, false);
                    //garrison into
                    selectableEntity.interactionTarget.ReceivePassenger(this);
                    state.Value = State.Idle;
                }
                break;
            #endregion
            default:
                break;
        }
    }
    #endregion
    #region UpdaterFunctions
    private void UpdateColliderStatus()
    {
        if (rigid != null)
        {
            rigid.isKinematic = state.Value switch
            {
                State.FindInteractable or State.WalkToInteractable or State.Harvesting or State.AfterHarvestCheck or State.Depositing or State.AfterDepositCheck
                or State.Building or State.AfterBuildCheck => true,
                _ => false,
            };
        }
        /*if (selectableEntity.RVO != null)
        {
            selectableEntity.RVO.enabled = state switch
            {
                State.FindHarvestable or State.WalkToHarvestable or State.Harvesting or State.AfterHarvestCheck
                or State.FindDeposit or State.WalkToDeposit or State.Depositing or State.AfterDepositCheck => false,
                _ => true,
            };
        }*/
    }
    private void EnsureNotInteractingWithBusy()
    {
        if (selectableEntity.interactionTarget != null && selectableEntity.interactionTarget.alive) //if we have a harvest target
        {
            if (!selectableEntity.interactionTarget.interactors.Contains(selectableEntity)) //if we are not in harvester list
            {
                if (selectableEntity.interactionTarget.interactors.Count < selectableEntity.interactionTarget.allowedInteractors) //if there is space
                {
                    //add us
                    selectableEntity.interactionTarget.interactors.Add(selectableEntity);
                }
                else //there is no space
                {
                    //get a new harvest target
                    selectableEntity.interactionTarget = null;
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
        state.Value = State.Die;
        ai.enabled = false;
        rayMod.enabled = false;
        seeker.enabled = false;
        Destroy(rigid);
        Destroy(col);
    }

    private void FinishSpawning()
    {
        state.Value = State.Idle;
    }
    public void SetBuildDestination(Vector3 pos, SelectableEntity ent)
    {
        destination.Value = pos;
        selectableEntity.interactionTarget = ent;
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
            if (attackMoving)
            {
                //Debug.Log("moving to ordered destination");
                destination.Value = orderedDestination;
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
        if (targetEnemy == null || targetEnemy.alive == false || targetEnemy.isTargetable.Value == false || targetEnemy.visibleInFog == false)
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
    public bool shouldAutoSeekOutEnemies = true;
    private bool InvalidBuildable(SelectableEntity target)
    {
        return target == null || target.fullyBuilt || target.alive == false;
    }
    private bool InvalidDeposit(SelectableEntity target)
    {
        return target == null || target.depositType == SelectableEntity.DepositType.None;
    }
    private bool InvalidHarvestable(SelectableEntity target)
    {
        return target == null || target.harvestType == SelectableEntity.HarvestType.None || target.alive == false;
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
    //could try cycling through entire list of enemy units .. .

   /* private async void AwaitableTest()
    {
        while (1)
        {
            await Awaitable.
        }
    }*/
    private SelectableEntity FindClosestEnemy() //bottleneck for unit spawning
    {
        float range = attackRange;

        int maxColliders = Mathf.RoundToInt(20 * range);
        Collider[] hitColliders = new Collider[maxColliders];
        debugNearby = hitColliders;
        int numColliders = Physics.OverlapSphereNonAlloc(transform.position, range, hitColliders, enemyMask);
        Collider closest = null;
        float distance = Mathf.Infinity;
        float threshold = -Mathf.Infinity;
        Vector3 forward = transform.TransformDirection(Vector3.forward).normalized;
        int numToCheck = Mathf.Clamp(numColliders, 0, maxColliders/2);
        //Debug.Log(numToCheck);
        for (int i = 0; i < numToCheck; i++) //this part is expensive
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
                if (select.visibleInFog == false) //we can't target those that are not visible
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
            if (item != null && item.alive)
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
            if (item != null && item.depositType != SelectableEntity.DepositType.None && item.fullyBuilt && item.alive)
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
    private void ReturnState()
    {
        switch (attackType)
        {
            case AttackType.Instant:
                state.Value = State.Idle;
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
        state.Value = State.Attacking;
        if (IsOwner) destination.Value = transform.position;

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
        return;
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
        float dist = Vector3.Distance(transform.position, oldPosition);
        oldPosition = transform.position;
        return dist/Time.deltaTime;
    }

    /*public void RallyToPos(Vector3 pos)
    {
        //followingMoveOrder = true;
        if (IsOwner) destination.Value = pos;
        orderedDestination = destination;
        //state = State.Walk;
    }*/
    public void SetRally(Vector3 pos)
    {
        Debug.Log("setting rally" + pos);
        //rallyPositionSet = true;
        orderedDestination = pos;
    }
    private void ClearTargets()
    { 
        targetEnemy = null;
        selectableEntity.interactionTarget = null; 
    }
    public void SetAttackMoveDestination() //called by local player
    {
        ClearTargets(); 
        basicallyIdleInstances = 0; 
        state.Value = State.WalkBeginFindEnemies; //default to walking state
        playedAttackMoveSound = false;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity))
        {
            destination.Value = hit.point;
            orderedDestination = destination.Value;   
        }
    }
    private void PlaceOnGround()
    {
        if (Physics.Raycast(transform.position + (new Vector3 (0, 100, 0)), Vector3.down, out RaycastHit hit, Mathf.Infinity, Global.Instance.localPlayer.groundLayer))
        {
            transform.position = hit.point;
        }
    }
    public void ForceBuildTarget(SelectableEntity target)
    {
        selectableEntity.interactionTarget = target;

        state.Value = State.WalkToInteractable;
        lastState = State.Building;
    }
    public void MoveTo(Vector3 target)
    {
        BasicWalkTo(target);
    }
    private void BasicWalkTo(Vector3 target)
    {
        ClearTargets();
        BecomeUnstuck();
        SetDestinations(target);
        state.Value = State.Walk;
    }
    private void BecomeUnstuck()
    { 
        basicallyIdleInstances = 0; //we're not idle anymore
    }
    private void SetDestinations(Vector3 target)
    {
        destination.Value = target; //set destination
        orderedDestination = target; //remember where we set destination 
    }
    /*public void SetDestinationRaycast(bool attackMoveVal = false)
    {
        if (state != State.Spawn)
        {
            ResetAllTargets(); 
            basicallyIdleInstances = 0; 
            attackMoving = attackMoveVal;
            state = State.Walk; //default to walking state
            selectableEntity.tryingToTeleport = false;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity))
            {
                destination.Value = hit.point;
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
                            if ((select.depositType == SelectableEntity.DepositType.Gold || select.depositType == SelectableEntity.DepositType.All) && select.fullyBuilt) //if deposit point
                            {
                                selectableEntity.interactionTarget = select;
                                state = State.WalkToInteractable;
                                lastState = State.Depositing;
                            }
                            else if (selectableEntity.type == SelectableEntity.EntityTypes.Builder && !select.fullyBuilt) //if buildable and this is a builder
                            {
                                selectableEntity.interactionTarget = select; 
                                state = State.WalkToInteractable;
                                lastState = State.Building;
                            } //target can be garrisoned, and passenger cannot garrison
                            else if (select.fullyBuilt && select.HasEmptyGarrisonablePosition() && selectableEntity.garrisonablePositions.Count <= 0) 
                            {
                                if (justLeftGarrison != select) //not perfect, fails on multiple units
                                {
                                    if (select.acceptsHeavy)
                                    {
                                        selectableEntity.interactionTarget = select;
                                        state = State.WalkToInteractable;
                                        lastState = State.Garrisoning;
                                    }
                                    else if (!selectableEntity.isHeavy)
                                    {
                                        selectableEntity.interactionTarget = select;
                                        state = State.WalkToInteractable;
                                        lastState = State.Garrisoning;
                                    } 
                                }
                            } //target is passenger of garrison
                            else if (select.occupiedGarrison != null && select.occupiedGarrison.HasEmptyGarrisonablePosition())
                            {
                                SelectableEntity garrison = select.occupiedGarrison;
                                if (justLeftGarrison != garrison) //not perfect, fails on multiple units
                                {
                                    if (garrison.acceptsHeavy)
                                    {
                                        selectableEntity.interactionTarget = garrison;
                                        state = State.WalkToInteractable;
                                        lastState = State.Garrisoning;
                                    }
                                    else if (!selectableEntity.isHeavy)
                                    {
                                        selectableEntity.interactionTarget = garrison;
                                        state = State.WalkToInteractable;
                                        lastState = State.Garrisoning;
                                    }
                                }
                            }
                            else if (select.type == SelectableEntity.EntityTypes.Portal)
                            {
                                selectableEntity.tryingToTeleport = true;
                                selectableEntity.interactionTarget = select;
                                state = State.WalkToInteractable;
                                lastState = State.Garrisoning;
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
                                    selectableEntity.interactionTarget = select;
                                    state = State.WalkToInteractable;
                                    lastState = State.Harvesting;
                                }
                                else
                                {  
                                    state = State.FindInteractable;
                                    lastState = State.Depositing;
                                }
                            }
                        }
                    } 
                } 
                CancelInvoke("SimpleDamageEnemy"); 
            }
        }
    }*/
    #endregion
}
