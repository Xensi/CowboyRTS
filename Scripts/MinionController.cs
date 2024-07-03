//using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding;
using FoW;
using static UnityEditorInternal.VersionControl.ListControl;
//using UnityEngine.Rendering;
//using UnityEngine.Windows;

//used for entities that can attack
[RequireComponent(typeof(SelectableEntity))]
public class MinionController : NetworkBehaviour
{
    #region Enums
    public enum CommandTypes
    {
        Move, Attack, Harvest, Build, Deposit
    }
    public enum MinionStates
    {
        Idle,
        Walk,
        WalkBeginFindEnemies,
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
        None,
        Instant, SelfDestruct, Artillery,
        Gatling, //for gatling gun
    }
    #endregion
    [HideInInspector] public MinionStates lastState = MinionStates.Idle;
    #region Hidden
    LayerMask enemyMask;
    private Camera cam;
    [HideInInspector] public SelectableEntity entity;
    private RaycastModifier rayMod;
    private Seeker seeker;
    private readonly float spawnDuration = .5f;
    private readonly sbyte buildDelta = 5;
    private readonly sbyte harvestAmount = 1;
    private float stateTimer = 0;
    private float rotationSpeed = 10f;
    [HideInInspector] public bool attackMoving = false;
    //[HideInInspector] public bool followingMoveOrder = false;
    [HideInInspector] public Vector3 orderedDestination; //remembers where player told minion to go
    private float basicallyIdleInstances = 0;
    private readonly int idleThreshold = 3; //seconds of being stuck
    private float attackReadyTimer = 0;
    private float change;
    public readonly float walkAnimThreshold = 0.0001f;
    private Vector3 oldPosition;
    private bool attackReady = true;
    [HideInInspector] public AIPath ai;
    [HideInInspector] public Collider col;
    [HideInInspector] private Rigidbody rigid;
    [HideInInspector] public Animator animator;
    [HideInInspector]
    public SelectableEntity targetEnemy;
    [HideInInspector] public MinionNetwork minionNetwork;
    bool playedAttackMoveSound = false;
    private AIDestinationSetter setter;
    #endregion
    #region Variables

    [Header("Behavior Settings")]
    public AttackType attackType = AttackType.Instant;
    [HideInInspector] public bool directionalAttack = false;

    [HideInInspector] public float attackRange = 1;
    [HideInInspector] public float depositRange = 1;

    //[SerializeField] private LocalRotation localRotate;
    [HideInInspector] public bool canMoveWhileAttacking = false;
    [SerializeField] private Transform attackEffectSpawnPosition;
    [HideInInspector] public sbyte damage = 1;
    [SerializeField] public float attackDuration = 1;
    [SerializeField] public float impactTime = .5f;
    [HideInInspector] public float defaultMoveSpeed = 0;
    [HideInInspector] public float defaultAttackDuration = 0;
    [HideInInspector] public float defaultImpactTime = 0;
    private Transform target;
    public readonly float garrisonRange = 1.1f;
    //50 fps fixed update
    //private readonly int delay = 0;
    [Header("Self-Destruct Only")]
    [HideInInspector] public float areaOfEffectRadius = 1; //ignore if not selfdestructer
    public MinionStates minionState = MinionStates.Spawn;
    [HideInInspector] public SelectableEntity.RallyMission givenMission = SelectableEntity.RallyMission.None;
    [HideInInspector] public Vector3 rallyTarget;
    #endregion
    #region NetworkVariables
    //maybe optimize this as vector 2 later
    [HideInInspector]
    public NetworkVariable<Vector3> realLocation = new NetworkVariable<Vector3>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    //controls where the AI will pathfind to
    [HideInInspector]
    public NetworkVariable<Vector3> destination = new NetworkVariable<Vector3>(default,
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
        entity = GetComponent<SelectableEntity>();
        minionNetwork = GetComponent<MinionNetwork>();
        animator = GetComponentInChildren<Animator>();
        rigid = GetComponent<Rigidbody>();
        //obstacle = GetComponentInChildren<MinionObstacle>();  

        if (entity.fakeSpawn)
        {
            FreezeRigid();
            ai.enabled = false;
        }
        nearbyIndexer = 0;// Random.Range(0, Global.Instance.allFactionEntities.Count);

        setter = GetComponent<AIDestinationSetter>();
        if (setter != null && setter.target == null)
        {
            GameObject obj = new GameObject("target");
            target = obj.transform;
            target.position = transform.position; //set to be on us
            setter.target = target;
        }
        defaultMoveSpeed = ai.maxSpeed;
        defaultAttackDuration = attackDuration;
        defaultImpactTime = impactTime;
    }
    private void Awake()
    {
        enemyMask = LayerMask.GetMask("Entity", "Obstacle");
        cam = Camera.main;

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
        maxDetectable = Mathf.RoundToInt(20 * attackRange);
    }
    private int maxDetectable;
    private void SetDestination(Vector3 position)
    {
        //print("setting destination");
        if (IsOwner) destination.Value = position; //tell server where we're going
        target.position = position; //move pathfinding target there
    }
    private void UpdateSetterTargetPosition()
    {
        target.position = destination.Value;
    }
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            realLocation.Value = transform.position;
            //destination.Value = transform.position; 
            SetDestination(transform.position);
            SwitchState(MinionStates.Spawn);
            Invoke(nameof(FinishSpawning), spawnDuration);
            finishedInitializingRealLocation = true;
            lastCommand.Value = CommandTypes.Move;
            defaultEndReachedDistance = ai.endReachedDistance;
        }
        else
        {
            /*  rigid.isKinematic = true;
              ai.enabled = false;
              rayMod.enabled = false;
              seeker.enabled = false;*/
        }
        destination.OnValueChanged += OnDestinationChanged;
        realLocation.OnValueChanged += OnRealLocationChanged;
        //enabled = IsOwner;
        oldPosition = transform.position;
        orderedDestination = transform.position;
        if (IsOwner)
        {
            if (Global.Instance.graphUpdateScenePrefab != null)
                graphUpdateScene = Instantiate(Global.Instance.graphUpdateScenePrefab, transform.position, Quaternion.identity);
            if (graphUpdateScene != null && ai != null)
            {
                graphUpdateSceneCollider = graphUpdateScene.GetComponent<SphereCollider>();
                graphUpdateSceneCollider.radius = ai.radius;
            }
        }
    }
    private float moveTimer = 0;
    private readonly float moveTimerMax = .5f;
    private void GetActualPositionChange()
    {
        //float dist = Vector3.Distance(transform.position, oldPosition);
        //oldPosition = transform.position;
        moveTimer += Time.deltaTime;
        if (moveTimer >= moveTimerMax)
        {
            moveTimer = 0;
            Vector3 offset = transform.position - oldPosition;
            float sqrLen = offset.sqrMagnitude;
            change = sqrLen;
            oldPosition = transform.position;
            //Debug.Log(change);
        }
    }
    private void OnRealLocationChanged(Vector3 prev, Vector3 cur)
    {
        finishedInitializingRealLocation = true;
    }
    private bool finishedInitializingRealLocation = false;
    private void Update()
    {
        //update real location, or catch up
        if (!entity.fakeSpawn && IsSpawned)
        {
            GetActualPositionChange();
            UpdateIdleCount();
            if (IsOwner)
            {
                UpdateRealLocation();
                UpdateMinionTimers();
                UpdateAttackReadiness();
                UpdateInteractors();
                OwnerUpdateState();
            }
            else if (finishedInitializingRealLocation)
            {
                CatchUpIfHighError();
                NonOwnerUpdateAnimationBasedOnContext();
                //if (ai != null) ai.destination = destination.Value;
            }
            UpdateSetterTargetPosition();
        }
    }
    //float stopDistIncreaseThreshold = 0.01f;
    float defaultEndReachedDistance = 0.5f;
    private void UpdateStopDistance()
    {
        float limit = 0.1f;
        if (change < limit * limit && walkStartTimer <= 0)
        {
            ai.endReachedDistance += Time.deltaTime;
        }
    }
    private void UpdateMinionTimers()
    {
        if (walkStartTimer > 0)
        {
            walkStartTimer -= Time.deltaTime;
        }
    }
    private void IdleOrWalkContextuallyAnimationOnly()
    {
        float limit = 0.1f;
        if (change < limit * limit || ai.reachedDestination) //basicallyIdleInstances > idleThreshold
        {
            animator.Play("Idle");
        }
        else
        {
            animator.Play("Walk");
        }
    }
    private void ClientSeekEnemy()
    {
        if (nearbyIndexer >= Global.Instance.allFactionEntities.Count)
        {
            nearbyIndexer = Global.Instance.allFactionEntities.Count - 1;
        }
        SelectableEntity check = Global.Instance.allFactionEntities[nearbyIndexer]; //fix this so we don't get out of range 
        if (clientSideTargetInRange == null)
        {
            if (check != null && check.alive && check.teamNumber.Value != entity.teamNumber.Value && InRangeOfEntity(check, attackRange)) //  && check.visibleInFog <-- doesn't work?
            { //only check on enemies that are alive, targetable, visible, and in range  
                clientSideTargetInRange = check;
            }
        }
        nearbyIndexer++;
        if (nearbyIndexer >= Global.Instance.allFactionEntities.Count) nearbyIndexer = 0;

        if (clientSideTargetInRange != null)
        {
            FreezeRigid();
        }
    }
    private void UpdateIdleCount()
    {
        if (change <= walkAnimThreshold && basicallyIdleInstances <= idleThreshold)
        {
            basicallyIdleInstances += Time.deltaTime;
        }
        if (change > walkAnimThreshold)
        {
            basicallyIdleInstances = 0;
        }
    }
    private void NonOwnerUpdateAnimationBasedOnContext()
    {
        if (entity.alive)
        {
            if (entity.occupiedGarrison != null)
            {
                ClientSeekEnemy();
                if (clientSideTargetInRange != null)
                {
                    //Debug.DrawLine(transform.position, clientSideEnemyInRange.transform.position, Color.red, 0.1f);
                    animator.Play("Attack");
                    LookAtTarget(clientSideTargetInRange.transform);
                }
                else
                {
                    animator.Play("Idle");
                }
            }
            else
            {
                switch (lastCommand.Value)
                {
                    case CommandTypes.Move:
                    case CommandTypes.Deposit:
                        IdleOrWalkContextuallyAnimationOnly();
                        break;
                    case CommandTypes.Attack:
                        ClientSeekEnemy();
                        ContextualIdleOrAttack();
                        break;
                    case CommandTypes.Harvest:
                        ClientSeekHarvestable();
                        ContextualIdleOrHarvestBuild();
                        break;
                    case CommandTypes.Build:
                        ClientSeekBuildable();
                        ContextualIdleOrHarvestBuild();
                        break;
                }
            }
        }
        else
        {
            animator.Play("Die");
        }
    }
    private void ContextualIdleOrHarvestBuild()
    {
        if (basicallyIdleInstances > idleThreshold || ai.reachedDestination)
        {
            if (clientSideTargetInRange != null)
            {
                //Debug.DrawLine(transform.position, clientSideEnemyInRange.transform.position, Color.red, 0.1f);
                animator.Play("Attack");
                LookAtTarget(clientSideTargetInRange.transform);
            }
        }
        else
        {
            animator.Play("Walk");
        }
    }
    private void ContextualIdleOrAttack()
    {
        if (basicallyIdleInstances > idleThreshold || ai.reachedDestination)
        {
            if (clientSideTargetInRange != null)
            {
                //Debug.DrawLine(transform.position, clientSideEnemyInRange.transform.position, Color.red, 0.1f);
                LookAtTarget(clientSideTargetInRange.transform);
                animator.Play("Attack");
            }
        }
        else
        {
            if (!animator.GetCurrentAnimatorStateInfo(0).IsName("AttackWalkStart") && !animator.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk"))
            {
                animator.Play("AttackWalkStart");
            }
        }
    }
    private void ClientSeekHarvestable()
    {
        if (nearbyIndexer >= Global.Instance.allFactionEntities.Count)
        {
            nearbyIndexer = Global.Instance.allFactionEntities.Count - 1;
        }
        SelectableEntity check = Global.Instance.allFactionEntities[nearbyIndexer]; //fix this so we don't get out of range 
        if (clientSideTargetInRange == null)
        {
            if (check != null && check.alive && check.selfHarvestableType == SelectableEntity.ResourceType.Gold && InRangeOfEntity(check, attackRange)) //  && check.visibleInFog <-- doesn't work?
            { //only check on enemies that are alive, targetable, visible, and in range  
                clientSideTargetInRange = check;
            }
        }
        nearbyIndexer++;
        if (nearbyIndexer >= Global.Instance.allFactionEntities.Count) nearbyIndexer = 0;

        if (clientSideTargetInRange != null)
        {
            FreezeRigid();
        }
    }

    private void ClientSeekBuildable()
    {
        if (nearbyIndexer >= Global.Instance.allFactionEntities.Count)
        {
            nearbyIndexer = Global.Instance.allFactionEntities.Count - 1;
        }
        SelectableEntity check = Global.Instance.allFactionEntities[nearbyIndexer]; //fix this so we don't get out of range 
        if (clientSideTargetInRange == null)
        {
            if (check != null && check.alive && check.teamNumber == entity.teamNumber &&
                !check.fullyBuilt && InRangeOfEntity(check, attackRange)) //  && check.visibleInFog <-- doesn't work?
            { //only check on enemies that are alive, targetable, visible, and in range  
                clientSideTargetInRange = check;
            }
        }
        nearbyIndexer++;
        if (nearbyIndexer >= Global.Instance.allFactionEntities.Count) nearbyIndexer = 0;

        if (clientSideTargetInRange != null)
        {
            FreezeRigid();
        }
    }
    private SelectableEntity clientSideTargetInRange = null;
    private bool EnemyInRangeIsValid(SelectableEntity target)
    {
        if (target == null || target.alive == false || target.isTargetable.Value == false || target.visibleInFog == false || !InRangeOfEntity(target, attackRange))
        {
            return false;
        }
        else
        {
            return true;
        }
    }
    private void UpdateRealLocation()
    {
        //float updateThreshold = 1f; //does not need to be equal to allowed error, but seems to work when it is
        Vector3 offset = transform.position - realLocation.Value;
        float dist = offset.sqrMagnitude;//Vector3.Distance(transform.position, realLocation.Value);

        if (dist > updateRealLocThreshold * updateRealLocThreshold)
        {
            //realLocationReached = false;
            realLocation.Value = transform.position; //only update when different enough
        }
    }
    //private bool realLocationReached = false;
    private float updateRealLocThreshold = 1f; //1
    private readonly float allowedNonOwnerError = 1.5f; //1.5 ideally higher than real loc update; don't want to lerp to old position
    private bool highPrecisionMovement = false;
    private void CatchUpIfHighError()
    {
        //owner can change real location with impunity
        //other clients: when their value for real location syncs up with owner, check if it's different enough to warrant a teleport
        //in future lerp to new location? 
        if (!IsOwner && entity.alive) //prevents dead units from teleporting
        {
            if (Vector3.Distance(realLocation.Value, transform.position) > allowedNonOwnerError || highPrecisionMovement) //&& realLocationReached == false
            {
                //Debug.Log("Telporting because distance too great");
                //transform.position = realLocation.Value;
                transform.position = LerpPosition(transform.position, realLocation.Value);
                if (rigid != null) rigid.isKinematic = true;
                if (ai != null) ai.enabled = false;
            }
            else
            {
                //realLocationReached = true;
                if (rigid != null) rigid.isKinematic = false;
                if (ai != null) ai.enabled = true;
            }
        }
    }
    // Calculated start for the most recent interpolation
    Vector3 m_LerpStart;

    // Calculated time elapsed for the most recent interpolation
    float m_CurrentLerpTime;

    // The duration of the interpolation, in seconds    
    float m_LerpTime = .1f;

    public Vector3 LerpPosition(Vector3 current, Vector3 target)
    {
        if (current != target)
        {
            m_LerpStart = current;
            m_CurrentLerpTime = 0f;
        }

        m_CurrentLerpTime += Time.deltaTime;

        /*// gentler lerp for shorter distances
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
        if (Vector3.Distance(realLocation.Value, transform.position) <= allowedNonOwnerError)
        {
            Gizmos.color = Color.green;
        }
        else
        {
            Gizmos.color = Color.red;
        }
        Gizmos.DrawWireSphere(realLocation.Value, .1f);
        Gizmos.DrawLine(transform.position, realLocation.Value);
        /*Gizmos.DrawWireSphere(transform.position, attackRange);
        if (targetEnemy != null)
        {
            Gizmos.DrawSphere(targetEnemy.transform.position, .1f);
        }*/
    }
    #endregion
    #region States 
    private void StopWalkingInGarrison()
    {
        if (entity.occupiedGarrison != null)
        {
            entity.interactionTarget = null;
            SwitchState(MinionStates.Idle);
        }
    }
    private void FollowGivenMission()
    {
        switch (givenMission)
        {
            case SelectableEntity.RallyMission.None:
                //only do this if not garrisoned
                if (entity.occupiedGarrison == null)
                {
                    /*if (selectableEntity.type == SelectableEntity.EntityTypes.Builder)
                    {
                        if (selectableEntity.interactionTarget == null || selectableEntity.interactionTarget.fullyBuilt)
                        {
                            selectableEntity.interactionTarget = FindClosestBuildable();
                        }
                        else
                        {
                            state = State.WalkToInteractable;
                            lastState = State.Building;
                            break;
                        }
                    }*/
                }
                break;
            case SelectableEntity.RallyMission.Move:
                SwitchState(MinionStates.WalkToRally);
                break;
            case SelectableEntity.RallyMission.Harvest:
                SwitchState(MinionStates.WalkToInteractable);
                lastState = MinionStates.Harvesting;
                break;
            case SelectableEntity.RallyMission.Build:
                if (entity.CanConstruct())
                {
                    if (entity.interactionTarget == null || entity.interactionTarget.fullyBuilt)
                    {
                        entity.interactionTarget = FindClosestBuildable();
                    }
                    else
                    {
                        SwitchState(MinionStates.WalkToInteractable);
                        lastState = MinionStates.Building;
                    }
                }
                break;
            case SelectableEntity.RallyMission.Garrison:
                SwitchState(MinionStates.WalkToInteractable);
                lastState = MinionStates.Garrisoning;
                break;
            case SelectableEntity.RallyMission.Attack:
                if (TargetIsValidEnemy(targetEnemy))
                {
                    SwitchState(MinionStates.WalkToEnemy);
                    break;
                }
                else
                {
                    targetEnemy = FindEnemyToWalkTowards(attackRange);
                }
                break;
            default:
                break;
        }
    }
    private void AutoSeekEnemies()
    {
        if (shouldAutoSeekOutEnemies)
        {
            if (TargetIsValidEnemy(targetEnemy))
            {
                SwitchState(MinionStates.WalkToEnemy);
            }
            else
            {
                targetEnemy = FindEnemyToWalkTowards(attackRange);
            }
        }
    }
    private void GarrisonedSeekEnemies()
    {
        if (TargetIsValidEnemy(targetEnemy))
        {
            SwitchState(MinionStates.Attacking);
        }
        else
        {
            targetEnemy = FindEnemyToAttack(attackRange);
        }
    }
    private void FinishSpawning()
    {
        SwitchState(MinionStates.Idle);
    }
    public void SwitchState(MinionStates stateToSwitchTo)
    {
        minionState = stateToSwitchTo;
        switch (stateToSwitchTo)
        {
            case MinionStates.Attacking:
            case MinionStates.Harvesting:
            case MinionStates.Building:
                stateTimer = 0;
                FreezeRigid(true, false);
                break;
            case MinionStates.Idle:
            case MinionStates.Die:
            case MinionStates.Spawn:
            case MinionStates.FindInteractable:
            case MinionStates.AfterHarvestCheck:
            case MinionStates.AfterDepositCheck:
            case MinionStates.AfterGarrisonCheck:
            case MinionStates.AfterAttackCheck:
            case MinionStates.AfterBuildCheck:
                FreezeRigid(true, true);
                break;
            case MinionStates.Walk:
            case MinionStates.WalkBeginFindEnemies:
            case MinionStates.WalkToEnemy:
            case MinionStates.WalkToInteractable:
            case MinionStates.WalkToRally:
            case MinionStates.Garrisoning:
            case MinionStates.Depositing:
                ai.endReachedDistance = defaultEndReachedDistance;
                ClearObstacle();
                FreezeRigid(false, false);
                break;
        }
    }
    private void DetectIfShouldReturnToIdle()
    {
        if (basicallyIdleInstances > idleThreshold || (walkStartTimer <= 0 && ai.reachedDestination))
        {
            SwitchState(MinionStates.Idle);
        }
    }
    private void SetDestinationIfHighDiff(Vector3 target)
    {
        Vector3 offset = target - destination.Value;
        if (Vector3.SqrMagnitude(offset) > Mathf.Pow(0.1f, 2))
        {
            SetDestination(target);
        }
    }
    public void ClearGivenMission()
    {
        //givenMission = SelectableEntity.RallyMission.None;
    }
    private void OwnerUpdateState()
    {
        switch (minionState)
        {
            case MinionStates.Spawn: //play the spawn animation  
                animator.Play("Spawn");
                //if (IsOwner) SetDestination(transform.position);//destination.Value = transform.position; 
                /*if (Physics.Raycast(transform.position + (new Vector3(0, 100, 0)), Vector3.down, out RaycastHit hit, Mathf.Infinity, Global.Instance.localPlayer.entityLayer))
                {
                    Collider col = hit.collider;
                    Rigidbody rigid = col.GetComponent<Rigidbody>();
                    rigid.AddForce(transform.forward * 1); 
                }*/
                //PlaceOnGround(); 
                break;
            case MinionStates.Die:
                animator.Play("Die");
                break;
            case MinionStates.Idle:
                IdleOrWalkContextuallyAnimationOnly();
                //HideMoveIndicator(); 

                if (entity.occupiedGarrison == null) //if not in garrison
                {
                    FollowGivenMission(); //if we have a rally mission, attempt to do it
                    AutoSeekEnemies();
                }
                else
                {
                    GarrisonedSeekEnemies();
                }
                break;
            case MinionStates.Walk:
                UpdateStopDistance();
                IdleOrWalkContextuallyAnimationOnly();
                DetectIfShouldReturnToIdle();
                //UpdateMoveIndicator(); 
                //SetDestination(orderedDestination);//destination.Value = orderedDestination; 
                break;
            case MinionStates.WalkToRally:
                //UpdateMoveIndicator();
                //if (IsOwner) SetDestination(rallyTarget);//destination.Value = rallyTarget;
                IdleOrWalkContextuallyAnimationOnly();
                break;
            case MinionStates.WalkBeginFindEnemies: //"ATTACK MOVE" 
                //UpdateMoveIndicator();
                //FreezeRigid(false, false);
                //SetDestination(orderedDestination);//destination.Value = orderedDestination;

                if (!animator.GetCurrentAnimatorStateInfo(0).IsName("AttackWalkStart") && !animator.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk"))
                {
                    if (!playedAttackMoveSound)
                    {
                        playedAttackMoveSound = true;
                        entity.SimplePlaySound(2);
                    }
                    animator.Play("AttackWalkStart");
                }

                if (TargetIsValidEnemy(targetEnemy))
                {
                    SwitchState(MinionStates.WalkToEnemy);
                }
                else
                {
                    targetEnemy = FindEnemyToWalkTowards(attackRange);
                }
                break;
            case MinionStates.WalkToEnemy:
                if (TargetIsValidEnemy(targetEnemy))
                {
                    //UpdateAttackIndicator();
                    if (!InRangeOfEntity(targetEnemy, attackRange))
                    {
                        if (entity.occupiedGarrison != null)
                        {
                            SwitchState(MinionStates.Idle);
                            break;
                        }
                        animator.Play("AttackWalk");
                        SetDestinationIfHighDiff(targetEnemy.transform.position);

                        SelectableEntity attackable = FindEnemyToAttack(attackRange);
                        if (attackable != null)
                        {
                            targetEnemy = attackable;
                            SwitchState(MinionStates.Attacking);
                            break;
                        }
                    }
                    else
                    {
                        SwitchState(MinionStates.Attacking);
                        break;
                    }
                }
                else
                {
                    SwitchState(MinionStates.WalkBeginFindEnemies);
                }
                break;
            case MinionStates.Attacking:
                /*if (attackType == AttackType.Gatling)
                {
                    animator.SetFloat("AttackSpeed", 1);
                }*/
                //can only invalidate targets if we are not attacking
                if (!TargetIsValidEnemy(targetEnemy) && !attackReady && !animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
                {
                    SwitchState(MinionStates.WalkBeginFindEnemies);
                }
                else if (!TargetIsValidEnemy(targetEnemy) && attackReady && !animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
                {
                    SwitchState(MinionStates.WalkBeginFindEnemies);
                }
                else if (InRangeOfEntity(targetEnemy, attackRange))
                {
                    //UpdateAttackIndicator();
                    //FreezeRigid(!canMoveWhileAttacking, false);
                    //if (IsOwner) SetDestination(transform.position);//destination.Value = transform.position; //stop in place
                    rotationSpeed = ai.rotationSpeed / 60;
                    LookAtTarget(targetEnemy.transform);

                    if (attackReady && CheckFacingTowards(targetEnemy.transform.position))
                    {
                        animator.Play("Attack");
                        if (AnimatorPlaying())
                        {
                            if (stateTimer < impactTime)
                            {
                                stateTimer += Time.deltaTime;
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
                                        SelfDestruct(areaOfEffectRadius);
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
                                Debug.Log("impact");
                            }
                        }
                        else //animation finished
                        {
                            Debug.Log("Attack Over");
                            SwitchState(MinionStates.AfterAttackCheck);
                        }
                    }
                    else if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
                    {
                        animator.Play("Idle");
                    }
                }
                else //walk to enemy if out of range
                {
                    SwitchState(MinionStates.WalkToEnemy);
                }
                break;
            case MinionStates.AfterAttackCheck:
                //FreezeRigid();
                /*if (attackType == AttackType.Gatling)
                {
                    animator.SetFloat("AttackSpeed", 1);
                }*/
                animator.Play("Idle");
                if (!TargetIsValidEnemy(targetEnemy))
                {
                    SwitchState(MinionStates.WalkBeginFindEnemies);
                }
                else //if target enemy is alive
                {
                    SwitchState(MinionStates.Attacking);
                }
                break;
            case MinionStates.FindInteractable:
                //FreezeRigid();
                animator.Play("Idle");
                //prioritize based on last state
                switch (lastState)
                {
                    case MinionStates.Building:
                        if (InvalidBuildable(entity.interactionTarget))
                        {
                            entity.interactionTarget = FindClosestBuildable();
                        }
                        else
                        {
                            SwitchState(MinionStates.WalkToInteractable);
                        }
                        break;
                    case MinionStates.Harvesting:
                        if (InvalidHarvestable(entity.interactionTarget))
                        {
                            entity.interactionTarget = FindClosestHarvestable();
                        }
                        else
                        {
                            SwitchState(MinionStates.WalkToInteractable);
                        }
                        break;
                    case MinionStates.Depositing:
                        if (InvalidDeposit(entity.interactionTarget))
                        {
                            entity.interactionTarget = FindClosestDeposit();
                        }
                        else
                        {
                            SwitchState(MinionStates.WalkToInteractable);
                        }
                        break;
                    default:
                        break;
                }
                break;
            case MinionStates.WalkToInteractable:
                //UpdateMoveIndicator();
                switch (lastState)
                {
                    case MinionStates.Building:
                        if (InvalidBuildable(entity.interactionTarget))
                        {
                            SwitchState(MinionStates.FindInteractable);
                        }
                        else
                        {
                            if (InRangeOfEntity(entity.interactionTarget, attackRange))
                            {
                                SwitchState(MinionStates.Building);
                            }
                            else
                            {
                                animator.Play("Walk");
                                Vector3 closest = entity.interactionTarget.physicalCollider.ClosestPoint(transform.position);
                                SetDestinationIfHighDiff(closest);
                                //if (IsOwner) SetDestination(closest);//destination.Value = closest;
                                /*selectableEntity.interactionTarget.transform.position;*/
                            }
                        }
                        break;
                    case MinionStates.Harvesting:
                        if (InvalidHarvestable(entity.interactionTarget))
                        {
                            SwitchState(MinionStates.FindInteractable);
                        }
                        else
                        {
                            if (InRangeOfEntity(entity.interactionTarget, attackRange))
                            {
                                SwitchState(MinionStates.Harvesting);
                            }
                            else
                            {
                                animator.Play("Walk");
                                Vector3 closest = entity.interactionTarget.physicalCollider.ClosestPoint(transform.position);
                                SetDestinationIfHighDiff(closest);
                            }
                        }
                        break;
                    case MinionStates.Depositing:
                        if (InvalidDeposit(entity.interactionTarget))
                        {
                            SwitchState(MinionStates.FindInteractable);
                        }
                        else
                        {
                            if (InRangeOfEntity(entity.interactionTarget, depositRange))
                            {
                                SwitchState(MinionStates.Depositing);
                            }
                            else
                            {
                                animator.Play("Walk");
                                Vector3 closest = entity.interactionTarget.physicalCollider.ClosestPoint(transform.position);
                                SetDestinationIfHighDiff(closest);
                            }
                        }
                        break;
                    case MinionStates.Garrisoning:
                        if (entity.interactionTarget == null)
                        {
                            SwitchState(MinionStates.FindInteractable); //later make this check for nearby garrisonables in the same target?
                        }
                        else
                        {
                            if (InRangeOfEntity(entity.interactionTarget, garrisonRange))
                            {
                                //Debug.Log("Garrisoning");
                                SwitchState(MinionStates.Garrisoning);
                            }
                            else
                            {
                                animator.Play("Walk");
                                Vector3 closest = entity.interactionTarget.physicalCollider.ClosestPoint(transform.position);
                                SetDestinationIfHighDiff(closest);
                            }
                            /*if (selectableEntity.interactionTarget.type == SelectableEntity.EntityTypes.Portal) //walk into
                            {
                                if (selectableEntity.tryingToTeleport)
                                {
                                    animator.Play("Walk");
                                    Vector3 closest = selectableEntity.interactionTarget.physicalCollider.ClosestPoint(transform.position);
                                    SetDestinationIfHighDiff(closest);
                                }
                                else
                                {
                                    SwitchState(State.Idle);
                                }
                            }
                            else
                            {
                                
                            }*/
                        }
                        break;
                    default:
                        break;
                }
                break;
            case MinionStates.Building:
                if (InvalidBuildable(entity.interactionTarget) || !InRangeOfEntity(entity.interactionTarget, attackRange))
                {
                    SwitchState(MinionStates.FindInteractable);
                    lastState = MinionStates.Building;
                }
                else
                {
                    LookAtTarget(entity.interactionTarget.transform);
                    if (attackReady)
                    {
                        animator.Play("Attack");

                        if (AnimatorPlaying())
                        {
                            if (stateTimer < impactTime)
                            {
                                stateTimer += Time.deltaTime;
                            }
                            else if (attackReady)
                            {
                                attackReady = false;
                                BuildTarget(entity.interactionTarget);
                            }
                        }
                        else //animation finished
                        {
                            SwitchState(MinionStates.AfterBuildCheck);
                        }
                    }
                }
                break;
            case MinionStates.AfterBuildCheck:
                animator.Play("Idle");
                if (InvalidBuildable(entity.interactionTarget))
                {
                    SwitchState(MinionStates.FindInteractable);
                    lastState = MinionStates.Building;
                }
                else
                {
                    SwitchState(MinionStates.Building);
                }
                break;
            #region Harvestable  
            case MinionStates.Harvesting:
                if (InvalidHarvestable(entity.interactionTarget))
                {
                    SwitchState(MinionStates.FindInteractable);
                    lastState = MinionStates.Harvesting;
                }
                else
                {
                    LookAtTarget(entity.interactionTarget.transform);
                    if (attackReady)
                    {
                        animator.Play("Attack");
                        if (AnimatorPlaying())
                        {
                            if (stateTimer < impactTime)
                            {
                                stateTimer += Time.deltaTime;
                            }
                            else if (attackReady)
                            {
                                attackReady = false;
                                HarvestTarget(entity.interactionTarget);
                                //Debug.Log("impact");
                            }
                        }
                        else //animation finished
                        {
                            //Debug.Log("harvest over");
                            SwitchState(MinionStates.AfterHarvestCheck);
                        }
                    }
                }
                break;
            case MinionStates.AfterHarvestCheck:
                animator.Play("Idle");
                if (entity.harvestedResourceAmount >= entity.harvestCapacity) //we're full so deposit
                {
                    SwitchState(MinionStates.FindInteractable);
                    lastState = MinionStates.Depositing;
                }
                else if (!InvalidHarvestable(entity.interactionTarget)) //keep harvesting if valid harvestable
                {
                    SwitchState(MinionStates.Harvesting);
                }
                else //find new thing to harvest from
                {
                    SwitchState(MinionStates.FindInteractable);
                    lastState = MinionStates.Harvesting;
                }
                break;
            case MinionStates.Depositing:
                if (InvalidDeposit(entity.interactionTarget))
                {
                    SwitchState(MinionStates.FindInteractable);
                    lastState = MinionStates.Depositing;
                }
                else
                {
                    LookAtTarget(entity.interactionTarget.transform);
                    //anim.Play("Attack"); //replace with deposit animation
                    //instant dropoff
                    if (entity != null)
                    {
                        Global.Instance.localPlayer.gold += entity.harvestedResourceAmount;
                        entity.harvestedResourceAmount = 0;
                        Global.Instance.localPlayer.UpdateGUIFromSelections();
                    }
                    minionState = MinionStates.AfterDepositCheck;
                }
                break;
            case MinionStates.AfterDepositCheck:
                animator.Play("Idle");
                if (InvalidHarvestable(entity.interactionTarget))
                {
                    SwitchState(MinionStates.WalkToInteractable);
                    lastState = MinionStates.Harvesting;
                }
                else
                {
                    SwitchState(MinionStates.FindInteractable);
                    lastState = MinionStates.Harvesting;
                }
                break;
            #endregion 
            #region Garrison
            case MinionStates.Garrisoning:
                if (entity.interactionTarget == null)
                {
                    minionState = MinionStates.FindInteractable;
                    lastState = MinionStates.Garrisoning;
                }
                else
                {
                    //garrison into
                    LoadPassengerInto(entity.interactionTarget);
                    minionState = MinionStates.Idle;
                }
                break;
                #endregion
        }
        DrawPath();
        //UpdateColliderStatus();
        /*if (attackType == AttackType.Gatling)
        {
            animator.SetFloat("AttackSpeed", 0);
        }*/
    }
    private void DrawPath()
    {
        if (entity.selected && minionState != MinionStates.Idle)
        {
            entity.lineIndicator.enabled = true;
            switch (minionState)
            {
                case MinionStates.Walk:
                case MinionStates.WalkBeginFindEnemies:
                case MinionStates.WalkToEnemy:
                case MinionStates.WalkToInteractable:
                case MinionStates.WalkToRally:
                    var buffer = new List<Vector3>();
                    ai.GetRemainingPath(buffer, out bool stale);
                    entity.UpdatePathIndicator(buffer.ToArray());
                    break;
            }
        }
        else
        {
            entity.lineIndicator.enabled = false;
        }
    }
    #endregion
    #region UpdaterFunctions
    private void UpdateColliderStatus()
    {
        if (rigid != null && IsOwner)
        {
            rigid.isKinematic = minionState switch
            {
                MinionStates.FindInteractable or MinionStates.WalkToInteractable or MinionStates.Harvesting or MinionStates.AfterHarvestCheck or MinionStates.Depositing or MinionStates.AfterDepositCheck
                or MinionStates.Building or MinionStates.AfterBuildCheck => true,
                _ => false,
            };
        }
    }
    private void UpdateInteractors()
    {
        if (entity.interactionTarget != null && entity.interactionTarget.alive)
        {
            switch (lastState)
            {
                case MinionStates.Building:
                case MinionStates.Harvesting:

                    if (!entity.interactionTarget.workersInteracting.Contains(entity)) //if we are not in harvester list
                    {
                        if (entity.interactionTarget.workersInteracting.Count < entity.interactionTarget.allowedWorkers) //if there is space
                        { 
                            entity.interactionTarget.workersInteracting.Add(entity);
                        }
                        else //there is no space
                        {
                            //get a new harvest target
                            entity.interactionTarget = null;
                        }
                    }
                    break;
                case MinionStates.Depositing:
                case MinionStates.Garrisoning:
                    if (!entity.interactionTarget.othersInteracting.Contains(entity)) 
                    {
                        if (entity.interactionTarget.othersInteracting.Count < entity.interactionTarget.allowedInteractors) //if there is space
                        { 
                            entity.interactionTarget.othersInteracting.Add(entity);
                        }
                        else //there is no space
                        {
                            entity.interactionTarget = null;
                        }
                    }
                    break;
            }
        }
    }

    private void EnsureNotInteractingWithBusy()
    {
    }
    #endregion
    #region SetterFunctions

    public void PrepareForDeath()
    {
        Global.Instance.localPlayer.selectedEntities.Remove(entity);
        entity.Select(false);
        SwitchState(MinionStates.Die);
        ai.enabled = false;
        if (entity.RVO != null) entity.RVO.enabled = false;
        if (rayMod != null) rayMod.enabled = false;
        seeker.enabled = false;
        Destroy(rigid);
        Destroy(col);
    }
    /// <summary>
    /// Tell this minion to go and construct a building. Clears obstacle.
    /// </summary>
    /// <param name="pos"></param>
    /// <param name="ent"></param>
    public void SetBuildDestination(Vector3 pos, SelectableEntity ent)
    {
        destination.Value = pos;
        entity.interactionTarget = ent;
        lastState = MinionStates.Building;
        SwitchState(MinionStates.WalkToInteractable);
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
                SetDestination(orderedDestination);//destination.Value = orderedDestination;
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
        if (entity != null)
        {
            entity.HideMoveIndicator();
        }
    }
    #endregion
    private void PlaceOnGroundIfNecessary()
    {
        if (entity.occupiedGarrison == null && (transform.position.y > 0.1f || transform.position.y < 0.1f))
        {
            PlaceOnGround();
        }
    }
    //
    //ai.GetRemainingPath
    /*private void FleeFromPosition(Vector3 position)
    {
        if (ai == null) return;
        // The path will be returned when the path is over a specified length (or more accurately when the traversal cost is greater than a specified value).
        // A score of 1000 is approximately equal to the cost of moving one world unit.
        int fleeAmount = 1000*1;

        // Create a path object
        FleePath path = FleePath.Construct(transform.position, position, fleeAmount);
        // This is how strongly it will try to flee, if you set it to 0 it will behave like a RandomPath
        path.aimStrength = 1;
        // Determines the variation in path length that is allowed
        path.spread = 4000;   
        ai.SetPath(path);
    } */

    /// <summary>
    /// Freeze rigidbody. Defaults to completely freezing it.
    /// </summary>
    /// <param name="freezePosition"></param>
    /// <param name="freezeRotation"></param>
    public void FreezeRigid(bool freezePosition = true, bool freezeRotation = true)
    {
        //ai.canMove = true;
        ai.canMove = !freezePosition;

        highPrecisionMovement = freezePosition;
        if (freezePosition)
        {
            ForceUpdateRealLocation();
            BecomeObstacle();
        }
        else
        {
            //ClearObstacle();
        }

        RigidbodyConstraints posCon;
        RigidbodyConstraints rotCon;
        //if (obstacle != null) obstacle.affectGraph = freezePosition; //should the minion act as a pathfinding obstacle?
        //obstacleCollider.enabled = freezePosition;
        if (freezePosition)
        {
            posCon = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
        }
        else
        {
            posCon = RigidbodyConstraints.None;
            //posCon = RigidbodyConstraints.FreezePositionY;
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
    private GraphUpdateScene graphUpdateScene;
    private SphereCollider graphUpdateSceneCollider;

    private float graphUpdateTime = 0.02f; //derived through trial and error
    private void BecomeObstacle()
    {
        //tell graph update scene to start blocking
        if (graphUpdateScene != null)
        {
            graphUpdateScene.transform.position = transform.position;
            graphUpdateScene.setWalkability = false;
            Invoke(nameof(UpdateGraph), graphUpdateTime); //this delay is necessary or it will not work
        }
    }
    private void UpdateGraph()
    {
        graphUpdateScene.Apply();
        //AstarPath.active.FlushGraphUpdates(); 
    }
    public void ClearObstacle()
    {
        //tell graph update scene to stop blocking
        if (graphUpdateScene != null)
        {
            graphUpdateScene.transform.position = transform.position;
            graphUpdateScene.setWalkability = true;
            graphUpdateScene.Apply();
            AstarPath.active.FlushGraphUpdates();
        }
    }
    public void DestroyObstacle()
    {
        ClearObstacle();
        if (graphUpdateScene != null)
        {
            Destroy(graphUpdateScene.gameObject);
        }
    }
    private void ForceUpdateRealLocation()
    {
        if (IsOwner)
        {
            realLocation.Value = transform.position;
        }
    }
    private void LookAtTarget(Transform target)
    {
        if (target != null)
        {
            transform.rotation = Quaternion.LookRotation(
                Vector3.RotateTowards(transform.forward, target.position - transform.position, Time.deltaTime * rotationSpeed, 0));
        }
    }
    private bool InRangeOfEntity(SelectableEntity target, float range)
    {
        if (target == null) return false;
        if (target.physicalCollider != null) //get closest point on collider
        {
            Vector3 closest = target.physicalCollider.ClosestPoint(transform.position);
            return Vector3.Distance(transform.position, closest) <= range;
        }
        else //check dist to center
        {
            return Vector3.Distance(transform.position, target.transform.position) <= range;
        }
    }
    [HideInInspector] public bool shouldAutoSeekOutEnemies = true;
    private bool InvalidBuildable(SelectableEntity target)
    {
        /*if (target == null)
        {
            Debug.LogWarning("Target Null");
        }
        else
        { 
            if (target.initialized && target.fullyBuilt) Debug.LogWarning("Target Already Built");
            if (target.alive == false) Debug.LogWarning("Target Not Alive");
        }*/
        return target == null || target.initialized && target.fullyBuilt || target.alive == false;
    }
    private bool InvalidDeposit(SelectableEntity target)
    {
        return target == null || target.depositType == SelectableEntity.DepositType.None;
    }
    private bool InvalidHarvestable(SelectableEntity target)
    {
        return target == null || target.selfHarvestableType == SelectableEntity.ResourceType.None || target.alive == false;
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
        if (entity != null)
        {
            if (UnityEngine.Input.GetKey(KeyCode.Space) && targetEnemy != null)
            {
                entity.UpdateAttackIndicator();
            }
            else
            {
                entity.HideMoveIndicator();
            }
        }
    }
    /// <summary>
    /// Update the indicator that shows where the minion will be moving towards.
    /// </summary>
    private void UpdateMoveIndicator()
    {
        if (entity != null)
        {
            if (UnityEngine.Input.GetKey(KeyCode.Space))
            {
                entity.UpdateMoveIndicator();
            }
            else
            {
                entity.HideMoveIndicator();
            }
        }
    }
    private void UpdateAttackReadiness()
    {
        if (!attackReady)
        {
            if (attackReadyTimer < Mathf.Clamp(attackDuration - impactTime, 0, 999))
            {
                attackReadyTimer += Time.deltaTime;
            }
            else
            {
                attackReady = true;
                attackReadyTimer = 0;
                //Debug.Log("attack ready");
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
        entity.SimplePlaySound(1); //play impact sound 
        if (target != null && target.IsSpawned)
        {
            int actualHarvested = Mathf.Clamp(harvestAmount, 0, target.hitPoints.Value); //max amount we can harvest clamped by hitpoints remaining
            int diff = entity.harvestCapacity - entity.harvestedResourceAmount;
            actualHarvested = Mathf.Clamp(actualHarvested, 0, diff); //max amount we can harvest clamped by remaining carrying capacity
            if (IsServer)
            {
                target.Harvest(harvestAmount);
            }
            else //client tell server to change the network variable
            {
                RequestHarvestServerRpc(harvestAmount, target);
            }

            entity.harvestedResourceAmount += actualHarvested;
            Global.Instance.localPlayer.UpdateGUIFromSelections();
        }
        else if (target != null && !target.IsSpawned)
        {
            Debug.LogError("target not spawned ...");
        }
    }
    private SelectableEntity FindClosestBuildable()
    {
        List<SelectableEntity> list = Global.Instance.localPlayer.ownedEntities;

        SelectableEntity closest = null;
        float distance = Mathf.Infinity;
        foreach (SelectableEntity item in list)
        {
            if (item != null && !item.fullyBuilt && item.workersInteracting.Count < item.allowedWorkers)
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
    //could try cycling through entire list of enemy units .. .
    //SelectableEntity currentClosestEnemy = null;
    int nearbyIndexer = 0;
    private bool TargetIsValidEnemy(SelectableEntity target)
    {
        if (target == null || !target.alive || !target.isTargetable.Value || (!entity.aiControlled && !target.visibleInFog))
        {
            return false;
        }
        else
        {
            return true;
        }
    }
    private SelectableEntity FindEnemyToWalkTowards(float range)
    {
        if (attackType == AttackType.None) return null;

        if (entity.IsMelee())
        {
            float defaultDetectionRange = 5;
            range = defaultDetectionRange;
        }
        else
        {
            range += 1;
        }

        SelectableEntity valid = null;
        if (nearbyIndexer >= Global.Instance.allFactionEntities.Count)
        {
            nearbyIndexer = Global.Instance.allFactionEntities.Count - 1;
        }
        //guarantee a target within .5 seconds
        int maxExpectedUnits = 200;
        int maxFramesToFindTarget = 30;
        int indexesToRunPerFrame = maxExpectedUnits / maxFramesToFindTarget;
        for (int i = 0; i < indexesToRunPerFrame; i++)
        {
            SelectableEntity check = Global.Instance.allFactionEntities[nearbyIndexer]; //fix this so we don't get out of range
            if (IsEnemy(check) && check.alive && check.isTargetable.Value)
            //only check on enemies that are alive, targetable, visible, and in range
            {
                if (entity.aiControlled && InRangeOfEntity(check, entity.fogUnit.circleRadius) && InRangeOfEntity(check, range)) //ai controlled doesn't care about fog
                {
                    valid = check;
                }
                else if (check.visibleInFog && InRangeOfEntity(check, range))
                {
                    valid = check;
                }
            }
            nearbyIndexer++;
            if (nearbyIndexer >= Global.Instance.allFactionEntities.Count) nearbyIndexer = 0;
            if (valid != null) return valid;
        }
        return valid;
    }
    private bool IsEnemy(SelectableEntity target)
    {
        return target.teamNumber.Value != entity.teamNumber.Value;
    }
    private SelectableEntity FindEnemyToAttack(float range) //bottleneck for unit spawning
    {
        if (attackType == AttackType.None) return null;

        SelectableEntity valid = null;
        if (nearbyIndexer >= Global.Instance.allFactionEntities.Count)
        {
            nearbyIndexer = Global.Instance.allFactionEntities.Count - 1;
        }
        //guarantee a target within .5 seconds
        int maxExpectedUnits = 200;
        int maxFramesToFindTarget = 30;
        int indexesToRunPerFrame = maxExpectedUnits / maxFramesToFindTarget;
        for (int i = 0; i < indexesToRunPerFrame; i++)
        {
            SelectableEntity check = Global.Instance.allFactionEntities[nearbyIndexer]; //fix this so we don't get out of range
            if (check.teamNumber.Value != entity.teamNumber.Value && check.alive && check.isTargetable.Value
                && check.visibleInFog && InRangeOfEntity(check, range))
            //only check on enemies that are alive, targetable, visible, and in range
            {
                valid = check;
            }
            nearbyIndexer++;
            if (nearbyIndexer >= Global.Instance.allFactionEntities.Count) nearbyIndexer = 0;
            if (valid != null) return valid;
        }
        return valid;
    }
    /*
    if (currentClosestEnemy == null)
    {
        currentClosestEnemy = check;
    }
    else
    {
        float distToOld = Vector3.SqrMagnitude(transform.position - currentClosestEnemy.transform.position);
        float distToNew = Vector3.SqrMagnitude(transform.position - check.transform.position);
        if (distToNew < distToOld)
        {
            currentClosestEnemy = check;
        }
    }*/
    /*if (currentClosestEnemy != null && (!currentClosestEnemy.alive || !currentClosestEnemy.isTargetable.Value || !currentClosestEnemy.visibleInFog 
        || !InRangeOfEntity(currentClosestEnemy, range))) //if invalid enemy remove
    {
        currentClosestEnemy = null;
    } 
    //check if current index is closer than current closest 
     */
    /// <summary>
    /// Returns closest harvestable resource with space for new harvesters.
    /// </summary>
    /// <returns></returns>
    private SelectableEntity FindClosestHarvestable()
    {
        FogOfWarTeam fow = FogOfWarTeam.GetTeam((int)OwnerClientId);
        SelectableEntity[] list = Global.Instance.harvestableResources;

        SelectableEntity closest = null;
        float distance = Mathf.Infinity;
        foreach (SelectableEntity item in list)
        {
            if (item != null && item.alive && fow.GetFogValue(item.transform.position) <= 0.5 * 255) //item is visible to some degree
            {
                if (item.workersInteracting.Count < item.allowedWorkers) //there is space for a new harvester
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
                float newDist = Vector3.SqrMagnitude(transform.position - item.transform.position); //faster than .distance
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
                SwitchState(MinionStates.Idle);
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
        entity.SimplePlaySound(1);

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
    [ServerRpc]
    private void RequestHarvestServerRpc(sbyte amount, NetworkBehaviourReference target)
    {
        //server must handle damage! 
        if (target.TryGet(out SelectableEntity select))
        {
            select.Harvest(amount);
        }
    }
    bool hasSelfDestructed = false;
    private void SelfDestruct(float explodeRadius)
    {
        if (!hasSelfDestructed)
        {
            hasSelfDestructed = true;
            Global.Instance.localPlayer.CreateExplosionAtPoint(transform.position, explodeRadius, damage);
            SimpleExplosionEffect(transform.position);
            Global.Instance.localPlayer.DamageEntity(damage, entity);
            //selectableEntity.ProperDestroyEntity();
            //DamageSpecifiedEnemy(selectableEntity, damage);
        }
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
            entity.SimplePlaySound(1);
            if (entity.attackEffects.Length > 0) //show muzzle flash
            {
                entity.DisplayAttackEffects();
            }
            if (!entity.IsMelee()) //shoot trail
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
            Global.Instance.localPlayer.DamageEntity(damage, enemy);
            //DamageUmbrella(damage, enemy);
        }
    }

    /*[ServerRpc]
    private void RequestDamageServerRpc(sbyte damage, NetworkBehaviourReference enemy)
    {
        //server must handle damage! 
        if (enemy.TryGet(out SelectableEntity select))
        {
            select.TakeDamage(damage);
        }
    }*/
    /*private void KillClientSide(SelectableEntity enemy)
    {
        enemy.ProperDestroyEntity();
    }*/
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

    private void ClearTargets()
    {
        targetEnemy = null;
        entity.interactionTarget = null;
    }
    [HideInInspector]
    public NetworkVariable<CommandTypes> lastCommand = new NetworkVariable<CommandTypes>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public void AIAttackMove(Vector3 target)
    {
        if (!entity.alive) return; //dead units cannot be ordered
        lastCommand.Value = CommandTypes.Attack;
        ClearTargets();
        basicallyIdleInstances = 0;
        SwitchState(MinionStates.WalkBeginFindEnemies);
        playedAttackMoveSound = false;
        SetDestination(target);
        //destination.Value = target;
        orderedDestination = target;
    }
    public void SetAttackMoveDestination() //called by local player
    {
        lastCommand.Value = CommandTypes.Attack;
        ClearTargets();
        basicallyIdleInstances = 0;
        SwitchState(MinionStates.WalkBeginFindEnemies);
        playedAttackMoveSound = false;
        Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity))
        {
            SetDestination(hit.point);
            //destination.Value = hit.point;
            orderedDestination = destination.Value;
        }
    }
    public void PlaceOnGround()
    {
        entity.PlaceOnGround();
    }
    public void ForceBuildTarget(SelectableEntity target)
    {
        if (target.workersInteracting.Count < target.allowedWorkers)
        {
            target.workersInteracting.Add(entity);
        }
        entity.interactionTarget = target;

        SwitchState(MinionStates.WalkToInteractable);
        lastState = MinionStates.Building;
    }
    private float walkStartTimer = 0;
    private readonly float walkStartTimerSet = .6f;
    private void BasicWalkTo(Vector3 target)
    {
        //selectableEntity.tryingToTeleport = false;
        ClearTargets();
        BecomeUnstuck();
        SwitchState(MinionStates.Walk);
        SetOrderedDestination(target);
        walkStartTimer = walkStartTimerSet;

        SelectableEntity justLeftGarrison = null;
        if (entity.occupiedGarrison != null) //we are currently garrisoned
        {
            justLeftGarrison = entity.occupiedGarrison;
            RemovePassengerFrom(entity.occupiedGarrison);
            PlaceOnGround(); //snap to ground
        }
    }
    private void BecomeUnstuck()
    {
        basicallyIdleInstances = 0; //we're not idle anymore
    }
    private void SetOrderedDestination(Vector3 target)
    {
        SetDestination(target);
        //destination.Value = target; //set destination
        orderedDestination = target; //remember where we set destination 
    }
    public void MoveTo(Vector3 target)
    {
        lastCommand.Value = CommandTypes.Move;
        if (minionState != MinionStates.Spawn)
        {
            BasicWalkTo(target);
        }
    }
    public void AttackTarget(SelectableEntity select)
    {
        lastCommand.Value = CommandTypes.Attack;
        targetEnemy = select;

        SwitchState(MinionStates.WalkToEnemy);
    }
    public void CommandHarvestTarget(SelectableEntity select)
    {
        if (entity.isHarvester)
        {
            if (entity.harvestedResourceAmount < entity.harvestCapacity) //we could still harvest more
            {
                Debug.Log("Harvesting");
                lastCommand.Value = CommandTypes.Harvest;
                entity.interactionTarget = select;
                SwitchState(MinionStates.WalkToInteractable);
                lastState = MinionStates.Harvesting;
            }
            else
            {
                Debug.Log("Depositing");
                lastCommand.Value = CommandTypes.Deposit;
                SwitchState(MinionStates.FindInteractable);
                lastState = MinionStates.Depositing;
            }
        }
    }
    public void DepositTo(SelectableEntity select)
    {
        lastCommand.Value = CommandTypes.Deposit;
        entity.interactionTarget = select;
        SwitchState(MinionStates.WalkToInteractable);
        lastState = MinionStates.Depositing;
    }
    public void CommandBuildTarget(SelectableEntity select)
    {
        if (entity.CanConstruct())
        {
            //Debug.Log("can build");
            lastCommand.Value = CommandTypes.Build;
            entity.interactionTarget = select;
            SwitchState(MinionStates.WalkToInteractable);
            lastState = MinionStates.Building;
        }
    }
    public void GarrisonInto(SelectableEntity garrison)
    {
        //Debug.Log("Trying to garrison");
        SelectableEntity justLeftGarrison = null;
        if (entity.occupiedGarrison != null) //we are currently garrisoned
        {
            justLeftGarrison = entity.occupiedGarrison;
            entity.occupiedGarrison.UnloadPassenger(this); //leave garrison by moving out of it
            PlaceOnGround(); //snap to ground
        }
        // && selectableEntity.garrisonablePositions.Count <= 0
        if (justLeftGarrison != garrison) //not perfect, fails on multiple units
        {
            if (garrison.acceptsHeavy || !entity.isHeavy)
            {
                entity.interactionTarget = garrison;
                SwitchState(MinionStates.WalkToInteractable);
                lastState = MinionStates.Garrisoning;
                //Debug.Log("Moving into garrison");
            }
        }
        lastCommand.Value = CommandTypes.Move;
        /*SelectableEntity garrison = select.occupiedGarrison;

        selectableEntity.tryingToTeleport = true;
        selectableEntity.interactionTarget = select;
        state = State.WalkToInteractable;
        lastState = State.Garrisoning;*/
    }
    private void LoadPassengerInto(SelectableEntity garrison)
    {
        SwitchState(MinionStates.Idle);
        garrison.ReceivePassenger(this);
        //we should tell other players that we're locked to a certain passenger seat
        if (IsServer)
        {
            LockToSeatClientRpc(garrison);
        }
        else
        {
            LockToSeatServerRpc(garrison);
        }
    }
    [ServerRpc]
    private void LockToSeatServerRpc(NetworkBehaviourReference reference)
    {
        LockToSeatClientRpc(reference);
    }
    [ClientRpc]
    private void LockToSeatClientRpc(NetworkBehaviourReference reference)
    {
        if (!IsOwner)
        {
            if (reference.TryGet(out SelectableEntity select))
            {
                //select
                select.ReceivePassenger(this);
            }
        }
    }
    private void RemovePassengerFrom(SelectableEntity garrison)
    {
        garrison.UnloadPassenger(this); //leave garrison by moving out of it

        if (IsServer)
        {
            FreeFromSeatClientRpc(garrison);
        }
        else
        {
            FreeFromSeatServerRpc(garrison);
        }
    }

    [ServerRpc]
    private void FreeFromSeatServerRpc(NetworkBehaviourReference reference)
    {
        FreeFromSeatClientRpc(reference);
    }
    [ClientRpc]
    private void FreeFromSeatClientRpc(NetworkBehaviourReference reference)
    {
        if (!IsOwner)
        {
            if (reference.TryGet(out SelectableEntity select))
            {
                //select
                select.UnloadPassenger(this);
            }
        }
    }
    #endregion
}
