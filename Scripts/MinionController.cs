//using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding;
using FoW;
using System.Linq; 
using static RTSPlayer;
using static UnityEngine.GraphicsBuffer;
using Unity.Burst.CompilerServices;
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
        AttackMoving,
        WalkToSpecificEnemy,
        Attacking,
        AfterAttackCheck,
        FindInteractable,
        WalkToInteractable,
        Building,
        AfterBuildCheck,
        Spawn,
        Die,
        Harvesting,
        AttackCooldown,
        AfterHarvestCheck,
        Depositing,
        AfterDepositCheck,
        Garrisoning,
        AfterGarrisonCheck,
        WalkToRally,
        WalkToTarget,
        UsingAbility,
    } 
    public enum AttackType
    {
        None,
        Instant, SelfDestruct, Artillery,
        Gatling, //for gatling gun
    }
    #endregion
    
    public MinionStates lastState = MinionStates.Idle;

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
    private float effectivelyIdleInstances = 0;
    private readonly int idleThreshold = 3; //seconds of being stuck
    public float attackReadyTimer = 0;
    private float change;
    public readonly float walkAnimThreshold = 0.0001f;
    private Vector3 oldPosition;
    public bool attackReady = true;
    [HideInInspector] public AIPath ai;
    [HideInInspector] public Collider col;
    [HideInInspector] private Rigidbody rigid;
    [HideInInspector] public Animator animator; 
    public SelectableEntity targetEnemy;
    [HideInInspector] public MinionNetwork minionNetwork;
    bool playedAttackMoveSound = false;
    private AIDestinationSetter setter;
    #endregion
    #region Variables

    [Header("Behavior Settings")]
    [HideInInspector] public AttackType attackType = AttackType.Instant;
    [HideInInspector] public bool directionalAttack = false;

    [HideInInspector] public float attackRange = 1;
    [HideInInspector] public float depositRange = 1;

    //[SerializeField] private LocalRotation localRotate;
    [HideInInspector] public bool canMoveWhileAttacking = false;
    [SerializeField] private Transform attackEffectSpawnPosition;
    [HideInInspector] public sbyte damage = 1;
    [HideInInspector] public float attackDuration = 1;
    [HideInInspector] public float impactTime = .5f;
    [HideInInspector] public float defaultMoveSpeed = 0;
    [HideInInspector] public float defaultAttackDuration = 0;
    [HideInInspector] public float defaultImpactTime = 0;
    private Transform pathfindingTarget;
    public readonly float garrisonRange = 1.1f;
    //50 fps fixed update
    //private readonly int delay = 0;
    [Header("Self-Destruct Only")]
    [HideInInspector] public float areaOfEffectRadius = 1; //ignore if not selfdestructer
    public MinionStates minionState = MinionStates.Spawn;
    [HideInInspector] public SelectableEntity.RallyMission givenMission = SelectableEntity.RallyMission.None;
    [HideInInspector] public Vector3 rallyPosition; //deprecated
    #endregion
    #region NetworkVariables
    //maybe optimize this as vector 2 later
    [HideInInspector]
    public NetworkVariable<Vector2Int> realLocation = new NetworkVariable<Vector2Int>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private Vector3 oldRealLocation;
    //controls where the AI will pathfind to
    [HideInInspector]
    public Vector3 destination;
    public bool canReceiveNewCommands = true;

    public Vector3 attackMoveDestination;
    #endregion
    #region Core 
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
            pathfindingTarget = obj.transform;
            pathfindingTarget.position = transform.position; //set to be on us
            setter.target = pathfindingTarget;
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
    private Vector2Int QuantizePosition(Vector3 vec) //(5.55)
    {
        int x = Mathf.RoundToInt(vec.x * 10); //56
        int y = Mathf.RoundToInt(vec.z * 10);

        return new Vector2Int(x, y);
    }
    private Vector3 DequantizePosition(Vector2Int vec) //(5.55)
    {
        float x = vec.x; //5.6
        float z = vec.y;

        return new Vector3(x / 10, 0, z / 10);
    }
    private void SetRealLocation()
    {
        oldRealLocation = transform.position;
        realLocation.Value = QuantizePosition(transform.position);
    }
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            SetRealLocation();
            //destination.Value = transform.position; 

            SwitchState(MinionStates.Spawn);
            Invoke(nameof(FinishSpawning), spawnDuration);
            //finishedInitializingRealLocation = true;
            lastCommand.Value = CommandTypes.Move;
            defaultEndReachedDistance = ai.endReachedDistance;
        }
        else
        {
            rigid.isKinematic = true; //don't get knocked around
            gameObject.layer = LayerMask.NameToLayer("OtherEntities"); //can pass through each other 
            nonOwnerRealLocationList.Add(transform.position);
            entity.RVO.enabled = false;
            /*  ;
              ai.enabled = false;
              rayMod.enabled = false;
              seeker.enabled = false;*/
        }
        realLocation.OnValueChanged += OnRealLocationChanged;
        //enabled = IsOwner;
        oldPosition = transform.position;
        orderedDestination = transform.position;
        if (IsOwner)
        {
            if (Global.Instance.graphUpdateScenePrefab != null)
                graphUpdateScene = Instantiate(Global.Instance.graphUpdateScenePrefab, transform.position, Quaternion.identity, Global.Instance.transform);
            if (graphUpdateScene != null && ai != null)
            {
                graphUpdateSceneCollider = graphUpdateScene.GetComponent<SphereCollider>();
                //graphUpdateSceneCollider.radius = ai.radius;
            }
        }
    }
    private int maxDetectable;
    public bool IsCurrentlyBuilding()
    {
        return minionState == MinionStates.Building || minionState == MinionStates.WalkToInteractable && lastState == MinionStates.Building;
    }

    /// <summary>
    /// Tells server this minion's destination so it can pathfind there on other clients
    /// </summary>
    /// <param name="position"></param>
    public void SetDestination(Vector3 position)
    {
        //print("setting destination");
        destination = position; //tell server where we're going
        UpdateSetterTargetPosition(); //move pathfinding target
    }
    public bool manualControlPathfinding = false;
    /// <summary>
    /// Update pathfinding target to match actual destination
    /// </summary>
    private void UpdateSetterTargetPosition()
    {
        if (!manualControlPathfinding) pathfindingTarget.position = destination;
    }
    private void NonOwnerPathfindToOldestRealLocation()
    {
        if (nonOwnerRealLocationList.Count > 0)
        {
            if (nonOwnerRealLocationList.Count >= Global.Instance.maximumQueuedRealLocations)
            {
                nonOwnerRealLocationList.RemoveAt(0); //remove oldest 

            }
            if (Vector3.Distance(nonOwnerRealLocationList[0], transform.position) > Global.Instance.allowedNonOwnerError)
            {
                transform.position = LerpPosition(transform.position, nonOwnerRealLocationList[0]);
                if (ai != null) ai.enabled = false;
            }
            else
            {
                pathfindingTarget.position = nonOwnerRealLocationList[0]; //update pathfinding target to oldest real location 
                if (ai != null) ai.enabled = true;
            }

            if (nonOwnerRealLocationList.Count > 1)
            {
                Vector3 offset = transform.position - pathfindingTarget.position;
                float dist = offset.sqrMagnitude;
                //for best results, make this higher than unit's slow down distance. at time of writing slowdown dist is .2
                if (dist < Global.Instance.closeEnoughDist * Global.Instance.closeEnoughDist) //square the distance to compare against
                {
                    nonOwnerRealLocationList.RemoveAt(0); //remove oldest 
                }
                ai.maxSpeed = defaultMoveSpeed * (1 + (nonOwnerRealLocationList.Count - 1) / Global.Instance.maximumQueuedRealLocations);
            }
        }
        else //make sure we have at least one position
        {
            nonOwnerRealLocationList.Add(transform.position);
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
    private void OnRealLocationChanged(Vector2Int prev, Vector2Int cur)
    {
        //finishedInitializingRealLocation = true;
        if (!IsOwner)
        {
            //may have to ray cast down to retrieve height data
            Vector3 deq = DequantizePosition(realLocation.Value);
            if (Physics.Raycast(deq + (new Vector3(0, 100, 0)), Vector3.down, out RaycastHit hit, Mathf.Infinity, Global.Instance.groundLayer))
            {
                deq.y = hit.point.y;
            }
            nonOwnerRealLocationList.Add(deq);
        }
    }
    public List<Vector3> nonOwnerRealLocationList = new(); //only used by non owners to store real locations that should be pathfound to sequentially

    //private bool finishedInitializingRealLocation = false;
    private void Update()
    {
        //update real location, or catch up
        if (!entity.fakeSpawn && IsSpawned)
        {
            GetActualPositionChange();
            UpdateIdleCount();
            if (IsOwner)
            {
                //EvaluateNearbyEntities();
                UpdateRealLocation();
                UpdateMinionTimers();
                UpdateAttackReadiness();
                UpdateInteractors();
                OwnerUpdateState();
                UpdateSetterTargetPosition();
                FixGarrisonObstacle();
            }
            else // if (finishedInitializingRealLocation) //not owned by us
            {
                NonOwnerPathfindToOldestRealLocation();
                //CatchUpIfHighError();
                //NonOwnerUpdateAnimationBasedOnContext(); //maybe there is issue with this 
                //if (ai != null) ai.destination = destination.Value;
            }
        }
    }
    /*[SerializeField] private List<SelectableEntity> nearbyEnemies; 
    private int nearbyEnemyIndexer = 0;
    private void EvaluateNearbyEntities()
    {
        float range = 0;
        if (entity.IsMelee())
        {
            range = defaultMeleeDetectionRange * 2;
        }
        else
        {
            range *= 2;
        }
        SelectableEntity enemy = Global.Instance.enemyEntities[nearbyEnemyIndexer];
        bool visible = enemy.visibleInFog;
        float dist = Vector3.Distance(transform.position, enemy.transform.position);
        if (visible dist <= range)
        {
            if (!nearbyEnemies.Contains(enemy))
            {
                nearbyEnemies.Add(enemy);
            }
        }
        else
        {
            nearbyEnemies.Remove(enemy);
            *//*if (nearbyEnemies.Contains(enemy))
            {
            }*//*
        }

        nearbyEnemyIndexer++;
        if (nearbyEnemyIndexer >= Global.Instance.enemyEntities.Count) nearbyEnemyIndexer = 0;
    }
    private void CleanNearbyEnemies()
    {

    }*/
    private void FixGarrisonObstacle()
    {
        if (graphUpdateScene != null && graphUpdateScene.setWalkability == false && IsGarrisoned())
        {
            ClearObstacle();
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
        if (change < limit * limit && walkStartTimer <= 0 || ai.reachedDestination) //basicallyIdleInstances > idleThreshold
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
        if (nearbyIndexer >= Global.Instance.allEntities.Count)
        {
            nearbyIndexer = Global.Instance.allEntities.Count - 1;
        }
        SelectableEntity check = Global.Instance.allEntities[nearbyIndexer]; //fix this so we don't get out of range 
        if (clientSideTargetInRange == null)
        {
            if (check != null && check.alive && check.teamNumber.Value != entity.teamNumber.Value && InRangeOfEntity(check, attackRange)) //  && check.visibleInFog <-- doesn't work?
            { //only check on enemies that are alive, targetable, visible, and in range  
                clientSideTargetInRange = check;
            }
        }
        nearbyIndexer++;
        if (nearbyIndexer >= Global.Instance.allEntities.Count) nearbyIndexer = 0;

        if (clientSideTargetInRange != null)
        {
            FreezeRigid();
        }
    }
    private void UpdateIdleCount()
    {
        if (change <= walkAnimThreshold && effectivelyIdleInstances <= idleThreshold)
        {
            effectivelyIdleInstances += Time.deltaTime;
        }
        if (change > walkAnimThreshold)
        {
            effectivelyIdleInstances = 0;
        }
    }
    private void NonOwnerUpdateAnimationBasedOnContext()
    {
        if (entity.alive)
        {
            if (entity.occupiedGarrison != null)
            {
                //ClientSeekEnemy();
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
        if (effectivelyIdleInstances > idleThreshold || ai.reachedDestination)
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
        if (effectivelyIdleInstances > idleThreshold || ai.reachedDestination)
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
        if (nearbyIndexer >= Global.Instance.allEntities.Count)
        {
            nearbyIndexer = Global.Instance.allEntities.Count - 1;
        }
        SelectableEntity check = Global.Instance.allEntities[nearbyIndexer]; //fix this so we don't get out of range 
        if (clientSideTargetInRange == null)
        {
            if (check != null && check.alive && check.selfHarvestableType == SelectableEntity.ResourceType.Gold && InRangeOfEntity(check, attackRange)) //  && check.visibleInFog <-- doesn't work?
            { //only check on enemies that are alive, targetable, visible, and in range  
                clientSideTargetInRange = check;
            }
        }
        nearbyIndexer++;
        if (nearbyIndexer >= Global.Instance.allEntities.Count) nearbyIndexer = 0;

        if (clientSideTargetInRange != null)
        {
            FreezeRigid();
        }
    }

    private void ClientSeekBuildable()
    {
        if (nearbyIndexer >= Global.Instance.allEntities.Count)
        {
            nearbyIndexer = Global.Instance.allEntities.Count - 1;
        }
        SelectableEntity check = Global.Instance.allEntities[nearbyIndexer]; //fix this so we don't get out of range 
        if (clientSideTargetInRange == null)
        {
            if (check != null && check.alive && check.teamNumber == entity.teamNumber &&
                !check.fullyBuilt && InRangeOfEntity(check, attackRange)) //  && check.visibleInFog <-- doesn't work?
            { //only check on enemies that are alive, targetable, visible, and in range  
                clientSideTargetInRange = check;
            }
        }
        nearbyIndexer++;
        if (nearbyIndexer >= Global.Instance.allEntities.Count) nearbyIndexer = 0;

        if (clientSideTargetInRange != null)
        {
            FreezeRigid();
        }
    }
    private SelectableEntity clientSideTargetInRange = null;
    private bool EnemyInRangeIsValid(SelectableEntity target)
    {
        if (target == null || target.alive == false || target.isTargetable.Value == false || target.isVisibleInFog == false || !InRangeOfEntity(target, attackRange))
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
        Vector3 offset = transform.position - oldRealLocation;
        float dist = offset.sqrMagnitude;//Vector3.Distance(transform.position, realLocation.Value);

        if (dist > Global.Instance.updateRealLocThreshold * Global.Instance.updateRealLocThreshold) //square the distance to compare against
        {
            //realLocationReached = false;
            SetRealLocation();
        }
    }
    //private bool realLocationReached = false;
    //private float updateRealLocThreshold = 1f; //1
    //private readonly float allowedNonOwnerError = 1.5f; //1.5 ideally higher than real loc update; don't want to lerp to old position
    private bool highPrecisionMovement = false;
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

        m_CurrentLerpTime += Time.deltaTime * Global.Instance.lerpScale;

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
    private void OnDrawGizmosSelected()
    {
        /*foreach (SelectableEntity item in nearbyEnemies)
        { 
            if (item != null) Gizmos.DrawWireSphere(item.transform.position, .1f);
        }*/
    }
    private void OnDrawGizmos()
    {
        /*if (entity.IsMelee())
        {
            float defaultDetectionRange = 5;
            Gizmos.DrawWireSphere(transform.position, defaultDetectionRange);
        }*/
        /*if (!IsOwner)
        {
            foreach (Vector3 loc in nonOwnerRealLocationList)
            {
                Gizmos.DrawWireSphere(loc, .1f);
            }
        }
        else
        {
            Gizmos.DrawWireSphere(DequantizePosition(realLocation.Value), .1f);
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
                /*if (entity.CanConstruct())
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
                }*/
                break;
            case SelectableEntity.RallyMission.Garrison:
                SwitchState(MinionStates.WalkToInteractable);
                lastState = MinionStates.Garrisoning;
                break;
            case SelectableEntity.RallyMission.Attack:
                /*if (TargetIsValidEnemy(targetEnemy))
                {
                    SwitchState(MinionStates.WalkToSpecificEnemy);
                    break;
                }
                else
                {
                    targetEnemy = FindEnemyToWalkTowards(attackRange);
                }*/
                Debug.LogWarning("attack rally mission not implemented");
                break;
            default:
                break;
        }
    }
    private void AutoSeekEnemies()
    {
        if (shouldAggressivelySeekEnemies)
        {
            targetEnemy = FindEnemyMinionToAttack(attackRange, true);
        }
        else
        {
            targetEnemy = FindEnemyMinionToAttack(attackRange, false);
        }
        if (IsValidTarget(targetEnemy))
        {
            SwitchState(MinionStates.WalkToSpecificEnemy);
        }
    }
    private void GarrisonedSeekEnemies()
    {
        if (IsValidTarget(targetEnemy))
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
        switch (stateToSwitchTo)
        {
            case MinionStates.Attacking:
                alternateAttackTarget = null;
                stateTimer = 0;
                FreezeRigid(true, false);
                canReceiveNewCommands = true;
                break;
            case MinionStates.Harvesting:
            case MinionStates.Building:
                stateTimer = 0;
                FreezeRigid(true, false);
                canReceiveNewCommands = true;
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
                canReceiveNewCommands = true;
                break;
            case MinionStates.UsingAbility:
                FreezeRigid(true, true);
                animator.Play("UseAbility");
                canReceiveNewCommands = false;
                skipFirstFrame = true;
                break;
            case MinionStates.Walk:
            case MinionStates.AttackMoving:
            case MinionStates.WalkToSpecificEnemy:
            case MinionStates.WalkToInteractable:
            case MinionStates.WalkToRally:
            case MinionStates.Garrisoning:
            case MinionStates.Depositing:
            case MinionStates.WalkToTarget:
                ai.endReachedDistance = defaultEndReachedDistance;
                ClearObstacle();
                FreezeRigid(false, false);
                canReceiveNewCommands = true;
                break;
        }
        minionState = stateToSwitchTo;
        //Debug.Log("Switching state to " + minionState);
    }
    private bool skipFirstFrame = true;
    private void DetectIfShouldReturnToIdle()
    {
        if (IsEffectivelyIdle(idleThreshold) || (walkStartTimer <= 0 && ai.reachedDestination))
        {
            SwitchState(MinionStates.Idle);
        }
    }
    private bool IsEffectivelyIdle(float forXSeconds)
    {
        return effectivelyIdleInstances > forXSeconds;
    } 
    private void SetDestinationIfHighDiff(Vector3 target)
    {
        Vector3 offset = target - destination;
        if (Vector3.SqrMagnitude(offset) > Mathf.Pow(0.1f, 2))
        {
            SetDestination(target);
        }
    }
    public void ClearGivenMission()
    {
        //givenMission = SelectableEntity.RallyMission.None;
    }
    public UnitOrder lastOrder = null;
    private bool OrderValid(UnitOrder order)
    {
        return order != null && order.unit != null;
    }
    private void ResumeLastOrder()
    {
        if (OrderValid(lastOrder))
        {
            ProcessOrder(lastOrder);
        }
    }
    private ActionType lastOrderType;
    public void ProcessOrder(UnitOrder order)
    {
        Vector3 targetPosition = order.targetPosition;
        SelectableEntity target = order.target;
        lastOrderType = order.action;
        switch (order.action)
        {
            case ActionType.MoveToTarget:
                MoveToTarget(target);
                break;
            case ActionType.AttackMove:
                AttackMoveToPosition(targetPosition);
                break;
            case ActionType.Move:
                MoveTo(targetPosition);
                break;
            case ActionType.AttackTarget:
                //Debug.Log("Processing order to " + order.action + order.target);
                AttackTarget(target);
                break;
            case ActionType.Harvest:
                CommandHarvestTarget(target);
                break;
            case ActionType.Deposit: //try to deposit if we have stuff to deposit
                if (entity.HasResourcesToDeposit())
                {
                    DepositTo(target);
                }
                else if (target.IsDamaged() && entity.CanConstruct()) //if its damaged, we can try to build it
                {
                    CommandBuildTarget(target);
                }
                else
                {
                    MoveToTarget(target);
                }
                break;
            case ActionType.Garrison:
                WorkOnGarrisoningInto(target);
                break;
            case ActionType.BuildTarget://try determining how many things need to be built in total, and grabbing closest ones
                if (entity.CanConstruct())
                {
                    CommandBuildTarget(target);
                }
                else
                {
                    WorkOnGarrisoningInto(target);
                }
                break;
        }
        lastOrder = order;
    }
    private bool AnimatorTransitioning()
    {
        return animator.IsInTransition(0);
    }
    private SelectableEntity alternateAttackTarget;
    private void OwnerUpdateState()
    {
        switch (minionState)
        {
            case MinionStates.Spawn: //play the spawn animation  
                animator.Play("Spawn");
                break;
            case MinionStates.Die:
                animator.Play("Die");
                break;
            case MinionStates.Idle:
                IdleOrWalkContextuallyAnimationOnly();
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
            case MinionStates.UsingAbility:
                if (skipFirstFrame) //neccesary to give animator a chance to catch up
                {
                    skipFirstFrame = false;
                }
                else if (!animator.GetCurrentAnimatorStateInfo(0).IsName("UseAbility"))
                {
                    entity.ActivateAbility(entity.abilityToUse);
                    SwitchState(MinionStates.Idle);
                    ResumeLastOrder();
                }
                break;
            case MinionStates.Walk:
                UpdateStopDistance();
                IdleOrWalkContextuallyAnimationOnly();
                DetectIfShouldReturnToIdle();
                break;
            case MinionStates.WalkToTarget:
                UpdateStopDistance();
                IdleOrWalkContextuallyAnimationOnly();
                if (entity.interactionTarget != null)
                {
                    SetOrderedDestination(entity.interactionTarget.transform.position);
                }
                if (InRangeOfEntity(entity.interactionTarget, attackRange)) //if in range, check if this has an ability that can be satisfied
                {
                    if (entity.usedAbilities.Count > 0)
                    {
                        for (int i = entity.usedAbilities.Count - 1; i >= 0; i--)
                        {
                            AbilityOnCooldown used = entity.usedAbilities[i];
                            if (used == null) continue;
                            if (used.shouldCooldown) continue;
                            if (used != null && used.visitBuildingToRefresh.Count > 0)
                            {
                                foreach (BuildingAndCost b in used.visitBuildingToRefresh)
                                {
                                    if (b == null) continue;
                                    if (b.building == entity.interactionTarget.factionEntity && b.cost <= entity.controllerOfThis.gold) //this works
                                    {
                                        entity.usedAbilities.Remove(used);
                                        entity.controllerOfThis.gold -= b.cost;

                                        Global.Instance.PlayMinionRefreshSound(entity);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                break;
            case MinionStates.WalkToRally:
                IdleOrWalkContextuallyAnimationOnly();
                break;
            case MinionStates.AttackMoving: //walk forwards while searching for enemies to attack   
                if (!animator.GetCurrentAnimatorStateInfo(0).IsName("AttackWalkStart") //if not playing attack move anim
                    && !animator.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk"))
                {
                    if (!playedAttackMoveSound) //play sound and anim
                    {
                        playedAttackMoveSound = true;
                        entity.SimplePlaySound(2);
                    }
                    animator.Play("AttackWalkStart");
                }
                
                FindClosestEnemyToAttackMoveTowards(attackRange);
                if (IsValidTarget(targetEnemy))
                {
                    SetDestination(targetEnemy.transform.position);
                    //SetDestinationIfHighDiff(targetEnemy.transform.position); //walk to the enemy
                    if (InRangeOfEntity(targetEnemy, attackRange))
                    {
                        SwitchState(MinionStates.Attacking);
                        break;
                    }
                }
                //currrently, this will prioritize minions. however, if a wall is in the way, then the unit will just walk into the wall
                break;
            case MinionStates.WalkToSpecificEnemy: //seek enemy without switching targets automatically
                if (!pathReachesTarget && IsEffectivelyIdle(.1f) && IsMelee())
                    //if we can't reach our specific target, find a new one
                { 
                    SwitchState(MinionStates.AttackMoving);
                }
                if (IsValidTarget(targetEnemy))
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

                        //if target is a structure, move the destination closer to us until it no longer hits obstacle
                        if (targetEnemy.IsStructure())
                        {
                            //adjust destination; //adjustedTargetEnemyStructurePosition
                            AdjustTargetEnemyStructureDestination(targetEnemy);
                        }
                        else
                        {
                            SetDestination(targetEnemy.transform.position);
                            //SetDestinationIfHighDiff(targetEnemy.transform.position);
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
                    SwitchState(MinionStates.AttackMoving);
                }
                break;
            case MinionStates.Attacking:
                /*if (attackType == AttackType.Gatling)
                {
                    animator.SetFloat("AttackSpeed", 1);
                }*/
                //can only invalidate targets if we are not attacking
                if (lastOrderType == ActionType.AttackMove && targetEnemy.IsStructure())
                {
                    //if we are attacking a structure, check to see if there are minions we could attack. 

                    if (alternateAttackTarget == null)
                    {
                        FindCloserAlternateAttackTarget(attackRange);
                        if (alternateAttackTarget != null) //just found it
                        { 
                            SetDestination(alternateAttackTarget.transform.position);
                        }
                    }
                    else //next update and we already found it
                    { 
                        bool reaches = DoesPathReachTarget(pathfindingTarget.transform.position);
                        if (reaches || !IsMelee())
                        {
                            //Debug.Log("Reaches!");
                            targetEnemy = alternateAttackTarget;
                            SwitchState(MinionStates.WalkToSpecificEnemy);
                        }
                        else
                        { 
                            FindCloserAlternateAttackTarget(attackRange);
                        }
                    } 
                }
                if (!IsValidTarget(targetEnemy) && !attackReady && !animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
                {
                    SwitchState(MinionStates.AttackMoving);
                }
                else if (!IsValidTarget(targetEnemy) && attackReady && !animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
                {
                    SwitchState(MinionStates.AttackMoving);
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
                        if (AnimatorUnfinished())
                        {
                            if (stateTimer < impactTime)
                            {
                                stateTimer += Time.deltaTime;
                            }
                            else if (attackReady)
                            {
                                stateTimer = 0;
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
                                //Debug.Log("impact");
                            }
                        }
                        else //animation finished
                        {
                            //Debug.Log("Attack Over");
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
                    SwitchState(MinionStates.WalkToSpecificEnemy);
                }
                break;
            case MinionStates.AfterAttackCheck:
                //FreezeRigid();
                /*if (attackType == AttackType.Gatling)
                {
                    animator.SetFloat("AttackSpeed", 1);
                }*/
                animator.Play("Idle");
                if (!IsValidTarget(targetEnemy))
                {
                    SwitchState(MinionStates.AttackMoving);
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
                                if (entity.interactionTarget != null && entity.interactionTarget.physicalCollider != null)
                                {
                                    Vector3 closest = entity.interactionTarget.physicalCollider.ClosestPoint(transform.position);
                                    SetDestinationIfHighDiff(closest);
                                }
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

                        if (AnimatorUnfinished())
                        {
                            if (stateTimer < impactTime)
                            {
                                stateTimer += Time.deltaTime;
                            }
                            else if (attackReady)
                            {
                                stateTimer = 0;
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
                else if (ReachedResourceCap() && attackReady)
                {
                    SwitchState(MinionStates.FindInteractable);
                    lastState = MinionStates.Depositing;
                }
                else
                {
                    LookAtTarget(entity.interactionTarget.transform);
                    if (attackReady)
                    {
                        animator.Play("Harvest");
                        if (AnimatorUnfinished())
                        {
                            if (stateTimer < impactTime)
                            {
                                stateTimer += Time.deltaTime;
                            }
                            else if (attackReady)
                            {
                                stateTimer = 0;
                                attackReady = false;
                                HarvestTarget(entity.interactionTarget);
                            }
                        }
                        else
                        {
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
                        entity.controllerOfThis.gold += entity.harvestedResourceAmount;
                        entity.harvestedResourceAmount = 0;
                        if (entity.controllerOfThis is RTSPlayer)
                        {
                            RTSPlayer rts = entity.controllerOfThis as RTSPlayer;
                            rts.UpdateGUIFromSelections();
                        }
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
    private Vector3 adjustedTargetEnemyStructurePosition;
    private void AdjustTargetEnemyStructureDestination(SelectableEntity entity)
    {
        bool obstructedByEntity = Physics.Raycast(adjustedTargetEnemyStructurePosition + (new Vector3(0, 100, 0)),
            Vector3.down, out RaycastHit entityHit, Mathf.Infinity, Global.Instance.localPlayer.entityLayer);
        if (obstructedByEntity)
        { 
            SelectableEntity hitEntity = Global.Instance.FindEntityFromObject(entityHit.collider.gameObject);
            if (hitEntity == entity)
            {
                //get ground position 
                bool hitGround = Physics.Raycast(adjustedTargetEnemyStructurePosition + (new Vector3(0, 100, 0)),
                    Vector3.down, out RaycastHit groundHit, Mathf.Infinity, Global.Instance.localPlayer.groundLayer);
                if (hitGround)
                {
                    float step = 4 * Time.deltaTime;
                    Vector3 newPosition = Vector3.MoveTowards(adjustedTargetEnemyStructurePosition, transform.position, step);
                    adjustedTargetEnemyStructurePosition = newPosition;
                    Debug.Log("adjusted to: " + adjustedTargetEnemyStructurePosition);
                    SetDestination(adjustedTargetEnemyStructurePosition);
                }
            }
        }
    }
    private bool IsMelee()
    {
        return attackRange < 3;
    }
    private bool ReachedResourceCap()
    {
        return entity.harvestedResourceAmount >= entity.harvestCapacity;
    }
    /// <summary>
    /// Get the path and show it with lines.
    /// </summary>
    private void DrawPath()
    {
        switch (minionState)
        {
            case MinionStates.Walk:
            case MinionStates.AttackMoving:
            case MinionStates.WalkToSpecificEnemy:
            case MinionStates.WalkToInteractable:
            case MinionStates.WalkToRally:
            case MinionStates.WalkToTarget: 
                if (entity.selected)
                {
                    entity.lineIndicator.enabled = true; 
                    var buffer = new List<Vector3>();
                    ai.GetRemainingPath(buffer, out bool stale);
                    entity.UpdatePathIndicator(buffer.ToArray());
                    UpdatePathReachesTarget(buffer.Last());
                }
                else
                {
                    entity.lineIndicator.enabled = false;
                }
                break;
        }
    }
    public bool pathReachesTarget = false;
    private bool DoesPathReachTarget(Vector3 target)
    {
        var buffer = new List<Vector3>();
        ai.GetRemainingPath(buffer, out bool stale);
        float dist = (target - buffer.Last()).sqrMagnitude;
        return dist < pathReachesThreshold;
    }
    private readonly float pathReachesThreshold = 0.05f;
    private void UpdatePathReachesTarget(Vector3 lastPathPos)
    {
        float dist = (pathfindingTarget.transform.position - lastPathPos).sqrMagnitude;
        pathReachesTarget = dist < pathReachesThreshold;
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
        if (entity.controllerOfThis is RTSPlayer)
        {
            RTSPlayer rts = entity.controllerOfThis as RTSPlayer;
            rts.selectedEntities.Remove(entity);
        }
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
        destination = pos;
        entity.interactionTarget = ent;
        lastState = MinionStates.Building;
        SwitchState(MinionStates.WalkToInteractable);
    }
    #endregion
    #region DetectionFunctions
    private void DetectIfShouldStopFollowingMoveOrder()
    {
        if (change <= walkAnimThreshold && effectivelyIdleInstances <= idleThreshold)
        {
            effectivelyIdleInstances++;
        }
        else if (change > walkAnimThreshold)
        {
            effectivelyIdleInstances = 0;
        }
        if (effectivelyIdleInstances > idleThreshold || ai.reachedDestination)
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
            if (change <= walkAnimThreshold && effectivelyIdleInstances <= idleThreshold)
            {
                effectivelyIdleInstances++;
            }
            else if (change > walkAnimThreshold)
            {
                effectivelyIdleInstances = 0;
            }
            if (effectivelyIdleInstances > idleThreshold || ai.reachedDestination)
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
    private void FleeFromPosition(Vector3 position)
    {
        if (ai == null) return;
        // The path will be returned when the path is over a specified length (or more accurately when the traversal cost is greater than a specified value).
        // A score of 1000 is approximately equal to the cost of moving one world unit.
        int fleeAmount = 1000 * 1;

        // Create a path object
        FleePath path = FleePath.Construct(transform.position, position, fleeAmount);
        // This is how strongly it will try to flee, if you set it to 0 it will behave like a RandomPath
        path.aimStrength = 1;
        // Determines the variation in path length that is allowed
        path.spread = 4000;
        ai.SetPath(path);
    }

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
    //public bool allowBecomingObstacles = false;
    private void BecomeObstacle()
    {
        if (graphUpdateScene != null && !IsGarrisoned())
        {
            //Debug.Log("Becoming obstacle");
            graphUpdateScene.transform.position = transform.position;
            graphUpdateScene.setWalkability = false;
            Invoke(nameof(UpdateGraph), graphUpdateTime); //this delay is necessary or it will not work
        }
        /*if (IsGarrisoned())
        { 
            ClearObstacle();
        }
        else
        {
            //tell graph update scene to start blocking
            
        }*/
    }
    private void UpdateGraph()
    {
        if (graphUpdateScene != null) graphUpdateScene.Apply();
        //AstarPath.active.FlushGraphUpdates(); 
    }
    //private Vector3 previousObstaclePositionToClear;
    public void ClearObstacle()
    {
        //tell graph update scene to stop blocking
        if (graphUpdateScene != null)
        {
            //Debug.Log("Clearing obstacle");
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
            SetRealLocation();
        }
    }
    private void LookAtTarget(Transform target)
    {
        if (target != null)
        {
            /*transform.rotation = Quaternion.LookRotation(
                Vector3.RotateTowards(transform.forward, target.position - transform.position, Time.deltaTime * rotationSpeed, 0));*/
            transform.LookAt(new Vector3(target.position.x, transform.position.y, target.position.z));
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
    [HideInInspector] public bool shouldAggressivelySeekEnemies = true;
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
        //invalid if null
        //or target is fully built and not damaged
        //or if dead
        return target == null || target.initialized && target.fullyBuilt && !target.IsDamaged() || target.alive == false;
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
    private bool AnimatorUnfinished()
    {
        return animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1; //animator.GetCurrentAnimatorStateInfo(0).length > 
    }
    private void HarvestTarget(SelectableEntity target)
    {
        entity.SimplePlaySound(1); //play impact sound 
        if (target != null && target.IsSpawned)
        {
            int actualHarvested = Mathf.Clamp(harvestAmount, 0, target.currentHP.Value); //max amount we can harvest clamped by hitpoints remaining
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
            if (entity.controllerOfThis is RTSPlayer)
            {
                RTSPlayer rts = entity.controllerOfThis as RTSPlayer;
                rts.UpdateGUIFromSelections();
            }
        }
        else if (target != null && !target.IsSpawned)
        {
            Debug.LogError("target not spawned ...");
        }
    }
    private SelectableEntity FindClosestBuildable()
    {
        List<SelectableEntity> list = entity.controllerOfThis.ownedEntities;

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

    private bool IsPlayerControlled()
    {
        return !entity.aiControlled;
    }

    //could try cycling through entire list of enemy units .. .
    //SelectableEntity currentClosestEnemy = null;
    int nearbyIndexer = 0;

    /// <summary>
    /// Is target nonnull, alive, targetable, etc?
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    private bool IsValidTarget(SelectableEntity target)
    {
        if (target == null || !target.alive || !target.isTargetable.Value || (IsPlayerControlled() && !target.isVisibleInFog)
            || (!canAttackStructures && target.IsStructure()))
            //reject if target is null, or target is dead, or target is untargetable, or this unit is player controlled and target is hidden,
            //or this unit can't attack structures and target is structure
        {
            return false;
        }
        else
        {
            return true;
        }
    }
    private readonly float defaultMeleeDetectionRange = 3;
    private SelectableEntity FindEnemyMinionToAttack(float range, bool shouldExtendAttackRange = true)
    {
        if (attackType == AttackType.None) return null;

        if (entity.IsMelee())
        { 
            range = defaultMeleeDetectionRange;
        }
        else
        {
            if (shouldExtendAttackRange) range += 1;
        }

        List<SelectableEntity> enemyList = entity.controllerOfThis.visibleEnemies;
        if (enemyList.Count <= 0) return null;
        SelectableEntity valid = null;
        if (nearbyIndexer >= enemyList.Count)
        {
            nearbyIndexer = enemyList.Count - 1;
        }
        //guarantee a target within .5 seconds
        int maxExpectedUnits = 200;
        int maxFramesToFindTarget = 30;
        int indexesToRunPerFrame = maxExpectedUnits / maxFramesToFindTarget;
        for (int i = 0; i < indexesToRunPerFrame; i++)
        {
            SelectableEntity check = enemyList[nearbyIndexer];
            if (IsEnemy(check) && check.alive && check.isTargetable.Value && check.IsMinion())
            //only check on enemies that are alive, targetable, visible, and in range. also only care about enemy minions
            {
                if (entity.aiControlled && InRangeOfEntity(check, entity.fogUnit.circleRadius) && InRangeOfEntity(check, range)) //ai controlled doesn't care about fog
                {
                    valid = check;
                }
                else if (check.isVisibleInFog && InRangeOfEntity(check, range))
                {
                    valid = check;
                }
            }
            nearbyIndexer++;
            if (nearbyIndexer >= enemyList.Count) nearbyIndexer = 0;
            if (valid != null) return valid;
        }
        return valid;
    }
    [SerializeField] private bool canAttackStructures = true;
    private readonly float defaultAttackMoveDestinationViabilityRange = 5;
    private void FindClosestEnemyToAttackMoveTowards(float range, bool shouldExtendAttackRange = true)
    {//check if visible enemies are in range of attackMoveDestination to count as a viable target
        if (attackType == AttackType.None) return;
        if (entity.IsMelee())
        { 
            range = defaultMeleeDetectionRange;
        }
        else
        {
            if (shouldExtendAttackRange) range += 1;
        }

        SelectableEntity valid = null;

        List<SelectableEntity> enemyList = entity.controllerOfThis.visibleEnemies;
        if (enemyList.Count <= 0) return;

        if (nearbyIndexer >= enemyList.Count)
        {
            nearbyIndexer = enemyList.Count - 1;
        }
        //guarantee a target within .5 seconds 
        int indexesToRunPerFrame = enemyList.Count / Global.Instance.maxFramesToFindTarget;
        for (int i = 0; i < indexesToRunPerFrame; i++)
        {
            SelectableEntity check = enemyList[nearbyIndexer];
            if (IsEnemy(check) && check.alive && check.isTargetable.Value) //only check on enemies that are alive, targetable, visible
            {
                if (entity.aiControlled 
                    || Vector3.Distance(check.transform.position, attackMoveDestination) <= defaultAttackMoveDestinationViabilityRange)
                //if AI controlled, continue; otherwise must be in range of the attack move destination
                //later add failsafe for if there's nobody in that range
                { 
                    if (canAttackStructures || check.IsMinion())
                    {
                        if (entity.aiControlled && InRangeOfEntity(check, entity.fogUnit.circleRadius)
                            && InRangeOfEntity(check, range)) //AI IGNORES ACTUAL FOG VISIBILITY BUT STILL OBEYS FOG RULES
                        {
                            valid = check;
                        }
                        else if (check.isVisibleInFog && InRangeOfEntity(check, range)) //is enemy in range and visible?
                        {
                            valid = check;
                        }
                    }
                }
            }
            if (IsValidTarget(targetEnemy))
            { //ensure dist is up to date
                sqrDistToTargetEnemy = (targetEnemy.transform.position - transform.position).sqrMagnitude;
            }
            else
            {
                targetEnemy = null;
                sqrDistToTargetEnemy = Mathf.Infinity;
            }
            if (valid != null)
            {
                Vector3 offset = valid.transform.position - transform.position;
                float validDist = offset.sqrMagnitude;
                //get sqr magnitude between this and valid
                //if our current target 
                //if our current target is a structure, jump to minion regardless of distance.
                //if our current target is a minion, only jump to other minions if lower distance
                //if our current destination is unreachable and we're melee, jump to something closer
                if (targetEnemy == null || //if we have no current target, this is our new target;
                    targetEnemy.IsStructure() && valid.IsMinion() ||
                    targetEnemy.IsStructure() && valid.IsStructure() && validDist < sqrDistToTargetEnemy ||
                    targetEnemy.IsMinion() && valid.IsMinion() && validDist < sqrDistToTargetEnemy
                    || !pathReachesTarget && IsMelee() && validDist < sqrDistToTargetEnemy) //
                {
                    sqrDistToTargetEnemy = validDist;
                    targetEnemy = valid;
                    //Debug.Log("Square distance to: " + targetEnemy.name + " is " + sqrDistToTargetEnemy);
                }
            }
            nearbyIndexer++;
            if (nearbyIndexer >= enemyList.Count) nearbyIndexer = 0;
        } 
    } 
    private float sqrDistToAlternateTarget = 0;
    /// <summary>
    /// Use to find a minion to attack when this is attacking a structure
    /// </summary>
    /// <param name="range"></param>
    /// <param name="shouldExtendAttackRange"></param>
    private void FindCloserAlternateAttackTarget(float range, bool shouldExtendAttackRange = true)
    {
        if (attackType == AttackType.None) return;
        if (entity.IsMelee())
        {
            range = defaultMeleeDetectionRange;
        }
        else
        {
            if (shouldExtendAttackRange) range += 1;
        }

        SelectableEntity valid = null;

        List<SelectableEntity> enemyList = entity.controllerOfThis.visibleEnemies;
        if (enemyList.Count <= 0) return;
        if (nearbyIndexer >= enemyList.Count)
        {
            nearbyIndexer = enemyList.Count - 1;
        }
        SelectableEntity check = enemyList[nearbyIndexer];
        if (IsEnemy(check) && check.alive && check.isTargetable.Value && check.IsMinion()) //only check on enemies that are alive, targetable, visible, and in range
        {
            if (entity.aiControlled && InRangeOfEntity(check, entity.fogUnit.circleRadius)
                && InRangeOfEntity(check, range)) //AI IGNORES ACTUAL FOG VISIBILITY BUT STILL OBEYS FOG RULES
            {
                valid = check;
            }
            else if (check.isVisibleInFog && InRangeOfEntity(check, range)) //is enemy in range and visible?
            {
                valid = check;
            }
        }
        if (IsValidTarget(alternateAttackTarget))
        { //ensure dist is up to date
            sqrDistToAlternateTarget = (alternateAttackTarget.transform.position - transform.position).sqrMagnitude;
        }
        else
        {
            alternateAttackTarget = null;
            sqrDistToAlternateTarget = Mathf.Infinity;
        }
        if (valid != null)
        {
            Vector3 offset = valid.transform.position - transform.position;
            float validDist = offset.sqrMagnitude; 
            if (alternateAttackTarget == null || validDist < sqrDistToAlternateTarget)
            {
                sqrDistToAlternateTarget = validDist;
                alternateAttackTarget = valid;
            }
        }
        nearbyIndexer++;
        if (nearbyIndexer >= enemyList.Count) nearbyIndexer = 0;
    }
    private bool IsEnemy(SelectableEntity target)
    {
        return target.teamNumber.Value != entity.teamNumber.Value;
    }
    private SelectableEntity FindEnemyToAttack(float range) //bottleneck for unit spawning
    {
        if (attackType == AttackType.None) return null;

        SelectableEntity valid = null;
        if (nearbyIndexer >= Global.Instance.allEntities.Count)
        {
            nearbyIndexer = Global.Instance.allEntities.Count - 1;
        }
        //guarantee a target within .5 seconds
        int maxExpectedUnits = 200;
        int maxFramesToFindTarget = 30;
        int indexesToRunPerFrame = maxExpectedUnits / maxFramesToFindTarget;
        for (int i = 0; i < indexesToRunPerFrame; i++)
        {
            SelectableEntity check = Global.Instance.allEntities[nearbyIndexer]; //fix this so we don't get out of range
            if (check.teamNumber.Value != entity.teamNumber.Value && check.alive && check.isTargetable.Value
                && check.isVisibleInFog && InRangeOfEntity(check, range))
            //only check on enemies that are alive, targetable, visible, and in range
            {
                valid = check;
            }
            nearbyIndexer++;
            if (nearbyIndexer >= Global.Instance.allEntities.Count) nearbyIndexer = 0;
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
        FogOfWarTeam fow = FogOfWarTeam.GetTeam((int)entity.controllerOfThis.playerTeamID);
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
        List<SelectableEntity> list = entity.controllerOfThis.ownedEntities;

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
            Debug.Log("self destructing");
            hasSelfDestructed = true;
            Global.Instance.localPlayer.CreateExplosionAtPoint(transform.position, explodeRadius, damage);
            SimpleExplosionEffect(transform.position);
            Global.Instance.localPlayer.DamageEntity(99, entity); //it is a self destruct, after all
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
        sqrDistToTargetEnemy = Mathf.Infinity;
        entity.interactionTarget = null;
    }
    [HideInInspector]
    public NetworkVariable<CommandTypes> lastCommand = new NetworkVariable<CommandTypes>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public void AIAttackMove(Vector3 target)
    {
        AttackMoveToPosition(target);
    }
    public float sqrDistToTargetEnemy = Mathf.Infinity;
    private void GenericAttackMovePrep(Vector3 target)
    { 
        attackMoveDestination = target;
        lastCommand.Value = CommandTypes.Attack;
        ClearTargets();
        ClearIdleness();
        SwitchState(MinionStates.AttackMoving);
        playedAttackMoveSound = false;
        SetDestination(target);
        orderedDestination = destination;
    }
    public void AttackMoveToPosition(Vector3 target) //called by local player
    {
        if (!entity.alive) return; //dead units cannot be ordered
        if (IsGarrisoned()) return;
        GenericAttackMovePrep(target);       
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
        ClearIdleness();
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
    private void ClearIdleness()
    {
        effectivelyIdleInstances = 0; //we're not idle anymore
    }
    private void SetOrderedDestination(Vector3 target)
    {
        SetDestination(target);
        //destination.Value = target; //set destination
        orderedDestination = target; //remember where we set destination 
    }
    public bool IsValidAttacker()
    {
        return attackType != AttackType.None;
    }
    public void MoveToTarget(SelectableEntity target)
    {
        if (target == null) return;
        Debug.Log("Moving to target");
        lastCommand.Value = CommandTypes.Move;
        if (minionState != MinionStates.Spawn)
        {
            ClearTargets();
            ClearIdleness();
            SwitchState(MinionStates.WalkToTarget);
            entity.interactionTarget = target;
            SetOrderedDestination(entity.interactionTarget.transform.position);
            walkStartTimer = walkStartTimerSet;

            SelectableEntity justLeftGarrison = null;
            if (entity.occupiedGarrison != null) //we are currently garrisoned
            {
                justLeftGarrison = entity.occupiedGarrison;
                RemovePassengerFrom(entity.occupiedGarrison);
                PlaceOnGround(); //snap to ground
            }
        }
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
        //Debug.Log("Received order to attack " + select.name);
        lastCommand.Value = CommandTypes.Attack;
        targetEnemy = select;
        ClearIdleness();
        if (targetEnemy.IsStructure()) adjustedTargetEnemyStructurePosition = targetEnemy.transform.position;

        SwitchState(MinionStates.WalkToSpecificEnemy);
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
            if (select.workersInteracting.Count == 1 && select.workersInteracting[0].minionController.minionState != MinionStates.Building)
            {
                select.workersInteracting[0].interactionTarget = null;
                select.workersInteracting.Clear();
            }

            //Debug.Log("can build");
            lastCommand.Value = CommandTypes.Build;
            entity.interactionTarget = select;
            SwitchState(MinionStates.WalkToInteractable);
            lastState = MinionStates.Building;
        }
    }
    private bool IsGarrisoned()
    {
        return entity.occupiedGarrison != null;
    }
    public void WorkOnGarrisoningInto(SelectableEntity garrison)
    {
        if (garrison.garrisonablePositions.Count <= 0)
        {
            MoveToTarget(garrison);
        }
        else
        {
            //Debug.Log("Trying to garrison");
            SelectableEntity justLeftGarrison = null;
            if (IsGarrisoned()) //we are currently garrisoned
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
    }
    public void ChangeRVOStatus(bool val)
    {
        if (entity.RVO != null) entity.RVO.enabled = val;
    }
    private void LoadPassengerInto(SelectableEntity garrison)
    {
        if (garrison.controllerOfThis.playerTeamID == entity.controllerOfThis.playerTeamID)
        {
            ChangeRVOStatus(false);
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
