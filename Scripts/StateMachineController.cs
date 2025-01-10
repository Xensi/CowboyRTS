//using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding;
using FoW;
using System.Linq;
using static RTSPlayer;
using static Player;
using System.Threading.Tasks;
using System.Data.Common;
using System;
using System.Threading;
using TMPro;
/*using static UnityEngine.GraphicsBuffer;
using Unity.Burst.CompilerServices;
using System.Data.Common;
using Unity.VisualScripting;*/
//using UnityEngine.Rendering;
//using UnityEngine.Windows;

//used for entities that can attack
[RequireComponent(typeof(SelectableEntity))]
public class StateMachineController : NetworkBehaviour
{
    #region Enums
    public enum CommandTypes
    {
        Move, Attack, Harvest, Build, Deposit
    }
    public enum EntityStates
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
        Depositing,
        AfterDepositCheck,
        Garrisoning,
        AfterGarrisonCheck,
        WalkToRally,
        WalkToTarget,
        UsingAbility,
    }
    #endregion

    public EntityStates lastMajorState = EntityStates.Idle;

    #region Hidden
    LayerMask enemyMask;
    private Camera cam;
    [HideInInspector] public SelectableEntity ent;
    private RaycastModifier rayMod;
    private Seeker seeker;
    private readonly float spawnDuration = .5f;
    private readonly sbyte buildDelta = 5; 
    public float stateTimer = 0;
    private float rotationSpeed = 10f;
    //[HideInInspector] public bool followingMoveOrder = false;
    [HideInInspector] public Vector3 orderedDestination; //remembers where player told minion to go
    private float effectivelyIdleInstances = 0;
    private readonly float idleThreshold = 3f;//3; //seconds of being stuck
    private float change;
    public readonly float walkAnimThreshold = 0.0001f;
    private Vector3 oldPosition;
    [HideInInspector] public AIPath ai;
    [HideInInspector] public Collider col;
    [HideInInspector] private Rigidbody rigid;
    [HideInInspector] public Animator animator; //comment this out soon 
    [HideInInspector] public MinionNetwork minionNetwork;
    private AIDestinationSetter setter;
    #endregion
    #region Variables

    [Header("Behavior Settings")]

    //[SerializeField] private LocalRotation localRotate;
    //[HideInInspector] public bool canMoveWhileAttacking = false; //Does not do anything yet
    [HideInInspector] public Transform pathfindingTarget;
    public readonly float garrisonRange = 1.1f;
    //50 fps fixed update
    //private readonly int delay = 0; 
    private EntityStates currentState = EntityStates.Spawn;
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
    [HideInInspector] public float defaultMoveSpeed = 0;

    public Vector3 attackMoveDestination;
    [SerializeField] private List<TrailRenderer> attackTrails = new();
    #endregion

    private float maximumChaseRange = 5;

    #region Core 
    void OnEnable()
    {
        rayMod = GetComponent<RaycastModifier>();
        seeker = GetComponent<Seeker>();
        col = GetComponent<Collider>();
        ent = GetComponent<SelectableEntity>();
        minionNetwork = GetComponent<MinionNetwork>();
        animator = GetComponentInChildren<Animator>();
        rigid = GetComponent<Rigidbody>();
        //obstacle = GetComponentInChildren<MinionObstacle>();  

        if (ent.fakeSpawn)
        {
            FreezeRigid();
            ai.enabled = false;
        }
        nearbyIndexer = 0;// Random.Range(0, Global.Instance.allFactionEntities.Count);

        setter = GetComponent<AIDestinationSetter>();
        if (setter != null && setter.target == null)
        {
            GameObject obj = new GameObject("target");
            obj.transform.parent = Global.Instance.transform;
            pathfindingTarget = obj.transform;
            pathfindingTarget.position = transform.position; //set to be on us
            setter.target = pathfindingTarget;
        }
        defaultMoveSpeed = ai.maxSpeed;
    }
    private void Awake()
    {
        ai = GetComponent<AIPath>();
        enemyMask = LayerMask.GetMask("Entity", "Obstacle");
        cam = Camera.main;

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        } 
    }
    public SelectableEntity[] attackMoveDestinationEnemyArray = new SelectableEntity[0];

    private bool IsMelee()
    {
        return ent.IsMelee();
    }
    private bool IsRanged()
    {
        return ent.IsRanged();
    }
    private void Start()
    {
        FactionEntity factionEntity = ent.factionEntity;
        if (factionEntity is FactionUnit)
        {
            FactionUnit factionUnit = factionEntity as FactionUnit;
            {
                if (ai != null)
                {
                    ai.maxSpeed = factionUnit.maxSpeed;
                    //Debug.Log("setting " + name + " speed to " + ai.maxSpeed);
                }
            }
        }
        if (ent.IsAttacker()) { 
            if (IsMelee())
            {
                maximumChaseRange = Global.Instance.defaultMeleeSearchRange;
            }
            else
            {
                maximumChaseRange = ent.attacker.range * 2f;
            }
        }
        attackMoveDestinationEnemyArray = new SelectableEntity[Global.Instance.attackMoveDestinationEnemyArrayBufferSize];
        ChangeAttackTrailState(false);
    }
    public bool IsAlive()
    {
        if (ent != null)
        { 
            return ent.alive;
        }
        else
        {
            return false;
        }
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

            SwitchState(EntityStates.Spawn);
            Invoke(nameof(FinishSpawning), spawnDuration);
            //finishedInitializingRealLocation = true;
            lastCommand.Value = CommandTypes.Move;
            defaultEndReachedDistance = ai.endReachedDistance;
        }
        else
        {
            rigid.isKinematic = true; //don't get knocked around
            //gameObject.layer = LayerMask.NameToLayer("OtherEntities"); //can pass through each other 
            nonOwnerRealLocationList.Add(transform.position);
            ent.RVO.enabled = false;
            /*
              ai.enabled = false;
              rayMod.enabled = false;
              seeker.enabled = false;*/
        }
        realLocation.OnValueChanged += OnRealLocationChanged;
        //enabled = IsOwner;
        oldPosition = transform.position;
        orderedDestination = transform.position;
    } 
    public bool IsCurrentlyBuilding()
    {
        return currentState == EntityStates.Building || currentState == EntityStates.WalkToInteractable && lastMajorState == EntityStates.Building;
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
    private void UpdateRepathRate()
    {
        float defaultPathRate = 2;
        float attackMovePathRate = 0.5f;
        ai.autoRepath.maximumPeriod = defaultPathRate;
        ai.autoRepath.mode = AutoRepathPolicy.Mode.Dynamic;

        if (lastOrderType == ActionType.AttackMove)
        {
            ai.autoRepath.maximumPeriod = attackMovePathRate;
        }
        else if (currentState == EntityStates.Idle || currentState == EntityStates.Harvesting
            || currentState == EntityStates.Building)
        {
            ai.autoRepath.mode = AutoRepathPolicy.Mode.Never;
        }
    } 
    public bool animatorUnfinished = false;
    private void Update()
    {
        //update real location, or catch up
        if (!ent.fakeSpawn && IsSpawned)
        {
            GetActualPositionChange();
            UpdateIdleCount();
            if (IsOwner)
            {   
                //EvaluateNearbyEntities();
                UpdateRealLocation();
                UpdateMinionTimers();
                UpdateReadiness();
                UpdateInteractors();
                OwnerUpdateState();
                UpdateRepathRate();
                //UpdateSetterTargetPosition();
                FixGarrisonObstacle();
                UpdateTargetEnemyLastPosition();
                animatorUnfinished = AnimatorUnfinished();
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
        if (IsGarrisoned())
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
            if (check != null && check.alive && check.teamNumber.Value != ent.teamNumber.Value && InRangeOfEntity(check, attackRange)) //  && check.visibleInFog <-- doesn't work?
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
        if (ent.alive)
        {
            if (ent.occupiedGarrison != null)
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
            if (check != null && check.alive 
                && check.IsOre())
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
            if (check != null && check.alive && check.teamNumber == ent.teamNumber &&
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
    private new void OnDestroy()
    {
        CancelAllAsyncTasks();
    } 
    #endregion
    #region States 
    /*private void StopWalkingInGarrison()
    {
        if (ent.occupiedGarrison != null)
        {
            ent.interactionTarget = null;
            SwitchState(EntityStates.Idle);
        }
    }*/
    private void FollowGivenMission()
    {
        switch (givenMission)
        {
            case SelectableEntity.RallyMission.None:
                //only do this if not garrisoned
                if (ent.occupiedGarrison == null)
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
                SwitchState(EntityStates.WalkToRally);
                break;
            case SelectableEntity.RallyMission.Harvest:
                SwitchState(EntityStates.WalkToInteractable);
                lastMajorState = EntityStates.Harvesting;
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
                SwitchState(EntityStates.WalkToInteractable);
                lastMajorState = EntityStates.Garrisoning;
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
                //Debug.LogWarning("attack rally mission not implemented");
                break;
            default:
                break;
        }
    }
    private void AutoSeekEnemies()
    {
        /*if (shouldAggressivelySeekEnemies)
        {
            targetEnemy = FindEnemyMinionToAttack(attackRange, true);
        }
        else
        {
            targetEnemy = FindEnemyMinionToAttack(attackRange, false);
        }*/
        /*if (IsValidTarget(targetEnemy))
        {
            SwitchState(MinionStates.WalkToSpecificEnemy);
        }*/
    }
    /*private void GarrisonedSeekEnemies()
    {
        if (IsValidTarget(targetEnemy))
        {
            SwitchState(EntityStates.Attacking);
        }
        else
        {
            targetEnemy = FindEnemyToAttack(attackRange);
        }
    }*/
    private void FinishSpawning()
    {
        SwitchState(EntityStates.Idle);
    }
    private void ChangeAttackTrailState(bool val)
    {
        //Debug.Log("changing attack trail state" + val);
        attackTrailActive = val;
        foreach (TrailRenderer item in attackTrails)
        {
            if (item != null)
            {
                item.emitting = val;
            }
        }
    }
    private void ExitState(EntityStates exitingState)
    {
        //Debug.Log("Exiting state" + exitingState + "Currently in state " + minionState); 
        switch (exitingState)
        {
            case EntityStates.Idle:
                break;
            case EntityStates.Attacking:
                ChangeAttackTrailState(false);
                CancelAsyncSearch();
                CancelTimers();
                break;
            case EntityStates.AttackMoving:
                CancelAsyncSearch();
                CancelTimers();
                break;
        }
    }

    private void EnterState(EntityStates state)
    {
        //Debug.Log("Entering state" + state + "Currently in state " + currentState);
        switch (state)
        {
            case EntityStates.Idle: //if we become idle, then create entity searcher on our position  
                break;
            case EntityStates.Attacking:
            case EntityStates.AttackMoving:
                asyncSearchTimerActive = false;
                pathfindingValidationTimerActive = false;
                hasCalledEnemySearchAsyncTask = false;
                alternateAttackTarget = null;
                break;
        }
    }
    private void ChangeBlockedByMinionObstacleStatus(bool blocked)
    {
        GraphMask includingObstacles = GraphMask.FromGraphName("GraphIncludingMinionNavmeshCuts");
        GraphMask excludingObstacles = GraphMask.FromGraphName("GraphExcludingMinionNavmeshCuts");
        if (seeker != null)
        {
            if (blocked)
            {
                seeker.graphMask = includingObstacles;
            }
            else
            {
                seeker.graphMask = excludingObstacles;
            }
        }
    }

    public async void SwitchState(EntityStates stateToSwitchTo)
    {
        //Debug.Log(name + " is switching state to: " + stateToSwitchTo);
        switch (stateToSwitchTo)
        {
            case EntityStates.Attacking:
                //attackOver = false;
                if (ent.IsAttacker()) ent.attacker.OnSwitchState();
                stateTimer = 0;
                timerUntilAttackTrailBegins = 0;
                attackTrailTriggered = false;
                ChangeAttackTrailState(false);
                FreezeRigid(true, false);
                canReceiveNewCommands = true;
                break;
            case EntityStates.Harvesting:
            case EntityStates.Building:
                stateTimer = 0;
                FreezeRigid(true, false);
                canReceiveNewCommands = true;
                break;
            case EntityStates.Idle:
            case EntityStates.Die:
            case EntityStates.Spawn:
            case EntityStates.FindInteractable: 
            case EntityStates.AfterDepositCheck:
            case EntityStates.AfterGarrisonCheck:
            case EntityStates.AfterAttackCheck:
            case EntityStates.AfterBuildCheck:
                FreezeRigid(true, true);
                canReceiveNewCommands = true;
                break;
            case EntityStates.UsingAbility:
                FreezeRigid(true, true);
                animator.Play("UseAbility");
                canReceiveNewCommands = false;
                skipFirstFrame = true;
                break;
            case EntityStates.Walk:
            case EntityStates.AttackMoving:
            case EntityStates.WalkToSpecificEnemy:
            case EntityStates.WalkToInteractable:
            case EntityStates.WalkToRally:
            case EntityStates.Garrisoning:
            case EntityStates.Depositing:
            case EntityStates.WalkToTarget:
                ai.endReachedDistance = defaultEndReachedDistance;
                //ClearObstacle();
                FreezeRigid(false, false);
                canReceiveNewCommands = true;
                break;
        }
        if (currentState != stateToSwitchTo)
        {
            ExitState(currentState);
            EnterState(stateToSwitchTo);
        }
        currentState = stateToSwitchTo;
        //Debug.Log("Switching state to " + minionState);

        if (currentState == EntityStates.Attacking)
        {
            ChangeBlockedByMinionObstacleStatus(false);
        }
        else //at first pathfind freely, and then become blocked by obstacles. by then our own obstacle should not be a worry
        {
            ChangeBlockedByMinionObstacleStatus(false);
            await Task.Delay(Global.Instance.changeBlockedDelayMs);
            ChangeBlockedByMinionObstacleStatus(true);
        }
    }
    private bool skipFirstFrame = true;
    private bool attackTrailActive = false;
    private void DetectIfShouldReturnToIdle()
    {
        if (IsEffectivelyIdle(idleThreshold))
        {
            //Debug.Log("Effectively Idle");
            SwitchState(EntityStates.Idle);
        }
        else if (walkStartTimer <= 0 && ai.reachedDestination)
        {
            //Debug.Log("AI reached");
            SwitchState(EntityStates.Idle);
        }

    }
    private bool IsEffectivelyIdle(float forXSeconds)
    {
        return effectivelyIdleInstances > forXSeconds;
    }
    /// <summary>
    /// Update pathfinding target to match actual destination
    /// </summary>
    private void UpdateSetterTargetPosition()
    {
        pathfindingTarget.position = destination;
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
    public ActionType lastOrderType;
    public void ProcessOrder(Player.UnitOrder order)
    {
        ResetGoal();
        Vector3 targetPosition = order.targetPosition;
        SelectableEntity target = order.target;
        lastOrderType = order.action;
        switch (order.action)
        {
            case ActionType.MoveToTarget:
                MoveToTarget(target);
                break;
            case ActionType.AttackMove:
                longTermGoal = Goal.OrderedToAttackMove;
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
                /*if (entity.HasResourcesToDeposit())
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
                }*/
                break;
            case ActionType.Garrison:
                WorkOnGarrisoningInto(target);
                break;
            case ActionType.BuildTarget://try determining how many things need to be built in total, and grabbing closest ones
                if (ent.IsBuilder())
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
    private void AttackTarget(SelectableEntity select)
    { 
        Debug.Log("Received order to attack " + select.name);
        lastCommand.Value = CommandTypes.Attack;
        ClearIdleness();
        if (ent.attacker.GetTargetEnemy().IsStructure()) nudgedTargetEnemyStructurePosition = ent.attacker.GetTargetEnemy().transform.position;
        if (ent.IsAttacker()) ent.attacker.AttackTarget(select);
    }
    [SerializeField] private float attackTrailBeginTime = 0.2f;
    private float timerUntilAttackTrailBegins = 0;
    private bool attackTrailTriggered = false;
    private bool AnimatorTransitioning()
    {
        return animator.IsInTransition(0);
    }
    private void CheckIfAttackTrailIsActiveErroneously()
    {
        if (currentState != EntityStates.Attacking)
        {
            if (attackTrailActive)
            {
                ChangeAttackTrailState(false);
            }
        }
    }
    private List<SelectableEntity> preservedAsyncSearchResults = new();

    CancellationTokenSource pathStatusTimerCancellationToken;

    CancellationTokenSource hasCalledEnemySearchAsyncTaskTimerCancellationToken;
    /// <summary>
    /// Cancel async timers.
    /// </summary>
    private void CancelTimers()
    {
        hasCalledEnemySearchAsyncTaskTimerCancellationToken?.Cancel();
        pathStatusTimerCancellationToken?.Cancel();
    }
    public void SetLastMajorState(EntityStates state)
    {
        lastMajorState = state;
    }
    private void CancelAllAsyncTasks()
    {
        CancelAsyncSearch();
        CancelTimers();
    }
    private bool asyncSearchTimerActive = false;
    private float searchTimerDuration = 0.1f;

    public bool InState(EntityStates state)
    {
        return currentState == state;
    }

    private void OwnerUpdateState()
    {
        CheckIfAttackTrailIsActiveErroneously();
        switch (currentState)
        {
            case EntityStates.Spawn: //play the spawn animation  
                animator.Play("Spawn");
                break;
            case EntityStates.Die:
                animator.Play("Die");
                break;
            case EntityStates.Idle:
                IdleOrWalkContextuallyAnimationOnly();
                if (ent.occupiedGarrison == null) //if not in garrison
                {
                    FollowGivenMission(); //if we have a rally mission, attempt to do it
                    //AutoSeekEnemies();
                }
                else
                {
                    GarrisonedSeekEnemies();
                }
                if (ent.IsAttacker()) ent.attacker.IdleState();
                break;
            case EntityStates.UsingAbility:
                if (skipFirstFrame) //neccesary to give animator a chance to catch up
                {
                    skipFirstFrame = false;
                }
                else if (!animator.GetCurrentAnimatorStateInfo(0).IsName("UseAbility"))
                {
                    ent.unitAbilities.ActivateAbility(ent.unitAbilities.GetQueuedAbility());
                    SwitchState(EntityStates.Idle);
                    ResumeLastOrder();
                }
                break;
            case EntityStates.Walk:
                UpdateStopDistance();
                IdleOrWalkContextuallyAnimationOnly();
                DetectIfShouldReturnToIdle();
                break;
            case EntityStates.WalkToTarget:
                UpdateStopDistance();
                IdleOrWalkContextuallyAnimationOnly();
                if (ent.interactionTarget != null)
                {
                    SetOrderedDestination(ent.interactionTarget.transform.position);
                }
                if (InRangeOfEntity(ent.interactionTarget, attackRange)) //if in range, check if this has an ability that can be satisfied
                {
                    List<AbilityOnCooldown> usedAbilities = ent.unitAbilities.GetUsedAbilities();
                    if (usedAbilities.Count > 0)
                    {
                        for (int i = usedAbilities.Count - 1; i >= 0; i--)
                        {
                            AbilityOnCooldown used = usedAbilities[i];
                            if (used == null) continue;
                            if (used.shouldCooldown) continue;
                            if (used != null && used.visitBuildingToRefresh.Count > 0)
                            {
                                foreach (BuildingAndCost b in used.visitBuildingToRefresh)
                                {
                                    if (b == null) continue;
                                    if (b.building == ent.interactionTarget.factionEntity && b.cost <= ent.controllerOfThis.gold) //this works
                                    {
                                        usedAbilities.Remove(used);
                                        ent.controllerOfThis.gold -= b.cost;

                                        Global.Instance.PlayMinionRefreshSound(ent);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                break;
            case EntityStates.WalkToRally:
                IdleOrWalkContextuallyAnimationOnly();
                break;
            case EntityStates.AttackMoving: //walk forwards while searching for enemies to attack
                if (ent.IsAttacker()) ent.attacker.AttackMovingState();
                break;
            case EntityStates.WalkToSpecificEnemy: //seek enemy without switching targets automatically
                if (ent.IsAttacker()) ent.attacker.WalkToSpecificEnemyState();
                break;
            case EntityStates.Attacking:
                if (ent.IsAttacker()) ent.attacker.AttackingState(); 
                break; 
            case EntityStates.FindInteractable:
                //FreezeRigid();
                animator.Play("Idle");
                //prioritize based on last state
                switch (lastMajorState) //this may be broken by the recent lastState change to switchstate
                {
                    case EntityStates.Building:
                        if (InvalidBuildable(ent.interactionTarget))
                        {
                            ent.interactionTarget = FindClosestBuildable();
                        }
                        else
                        {
                            SwitchState(EntityStates.WalkToInteractable);
                        }
                        break;
                    case EntityStates.Harvesting:
                        if (ent.harvester == null) return;
                        ent.harvester.FindHarvestableState();
                        break;
                    case EntityStates.Depositing:
                        if (ent.harvester == null) return;
                        ent.harvester.FindDepositState();
                        break;
                    default:
                        break;
                }
                break;
            case EntityStates.WalkToInteractable:
                //UpdateMoveIndicator();
                switch (lastMajorState)
                {
                    case EntityStates.Building:
                        if (ent.IsBuilder()) ent.builder.WalkToBuildable();
                        break;
                    case EntityStates.Harvesting:
                        if (ent.harvester == null) return;
                        ent.harvester.WalkToOre();
                        break;
                    case EntityStates.Depositing:
                        if (ent.harvester == null) return;
                        ent.harvester.WalkToDepot();
                        break;
                    case EntityStates.Garrisoning:
                        if (ent.interactionTarget == null)
                        {
                            SwitchState(EntityStates.FindInteractable); //later make this check for nearby garrisonables in the same target?
                        }
                        else
                        {
                            if (InRangeOfEntity(ent.interactionTarget, garrisonRange))
                            {
                                //Debug.Log("Garrisoning");
                                SwitchState(EntityStates.Garrisoning);
                            }
                            else
                            {
                                animator.Play("Walk");
                                if (ent.interactionTarget != null && ent.interactionTarget.physicalCollider != null)
                                {
                                    Vector3 closest = ent.interactionTarget.physicalCollider.ClosestPoint(transform.position);
                                    SetDestinationIfHighDiff(closest);
                                }
                            }
                        }
                        break;
                    default:
                        break;
                }
                break;
            case EntityStates.Building:
                if (ent.IsBuilder()) ent.builder.BuildingState();
                break;  
            case EntityStates.Harvesting:
                if (ent.harvester == null) return;
                ent.harvester.HarvestingState();
                break; 
            case EntityStates.Depositing:
                if (ent.IsHarvester()) ent.harvester.DepositingState();
                break;  
            #region Garrison
            case EntityStates.Garrisoning:
                if (ent.interactionTarget == null)
                {
                    currentState = EntityStates.FindInteractable;
                    lastMajorState = EntityStates.Garrisoning;
                }
                else
                {
                    //garrison into
                    LoadPassengerInto(ent.interactionTarget);
                    currentState = EntityStates.Idle;
                }
                break;
                #endregion
        }
        DrawPath();
        UpdatePathStatus();
        //UpdateColliderStatus();
        /*if (attackType == AttackType.Gatling)
        {
            animator.SetFloat("AttackSpeed", 0);
        }*/
    }
    public Vector3 nudgedTargetEnemyStructurePosition;
    /// <summary>
    /// Slight nudge
    /// </summary>
    /// <param name="entity"></param>
    private void NudgeTargetEnemyStructureDestination(SelectableEntity entity)
    {
        nudgedTargetEnemyStructurePosition = entity.transform.position;
        float step = 10 * Time.deltaTime;
        Vector3 newPosition = Vector3.MoveTowards(nudgedTargetEnemyStructurePosition, transform.position, step);
        nudgedTargetEnemyStructurePosition = newPosition;
        //Debug.DrawRay(entity.transform.position, Vector3.up, Color.red, 5);
        Debug.DrawRay(nudgedTargetEnemyStructurePosition, Vector3.up, Color.green, 5);
    } 
    /// <summary>
    /// Get the path and show it with lines.
    /// </summary>
    private void DrawPath()
    {
        switch (currentState)
        {
            case EntityStates.Walk:
            case EntityStates.AttackMoving:
            case EntityStates.WalkToSpecificEnemy:
            case EntityStates.WalkToInteractable:
            case EntityStates.WalkToRally:
            case EntityStates.WalkToTarget:

                var buffer = new List<Vector3>();
                ai.GetRemainingPath(buffer, out bool stale);
                if (ent.selected)
                {
                    if (ent.lineIndicator != null) ent.lineIndicator.enabled = true;
                    ent.UpdatePathIndicator(buffer.ToArray());
                }
                else
                {
                    if (ent.lineIndicator != null) ent.lineIndicator.enabled = false;
                }
                break;
        }

    }
    private void UpdatePathStatus()
    {
        bool pathReached = EndOfPathReachesPosition(pathfindingTarget.transform.position);
        if (ai.pathPending)
        {
            pathReachesDestination = PathStatus.Pending;
        }
        else if (pathReached)
        {
            pathReachesDestination = PathStatus.Reaches;
        }
        else
        {
            pathReachesDestination = PathStatus.Blocked;
        }
    }
    public enum PathStatus { Pending, Reaches, Blocked }
    public PathStatus pathReachesDestination = PathStatus.Pending;
    public float pathDistFromTarget = 0;
    /// <summary>
    /// Use to check if path reaches a position. Do not use to check if path reaches a structure. Assumes that the path has been searched already,
    /// so may fail if it has not been searched manually or automatically before calling this
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    private bool EndOfPathReachesPosition(Vector3 position)
    {
        float pathThreshold = 0.1f;
        var buffer = new List<Vector3>();
        ai.GetRemainingPath(buffer, out bool stale);
        float dist = (position - buffer.Last()).sqrMagnitude;

        Vector3 prePos = buffer[0];
        for (int i = 1; i < buffer.Count; i++)
        {
            Debug.DrawLine(prePos, buffer[i], Color.blue, 1);
            prePos = buffer[i];
        }
        //Debug.DrawRay(position, Vector3.up, Color.yellow, 4);
        //Debug.DrawRay(buffer.Last(), Vector3.up, Color.blue, 4);
        return dist < pathThreshold * pathThreshold;
    }
    private readonly float pathReachesThreshold = 0.25f;
    public Vector3 lastPathPosition;
    /*private void UpdatePathReachesTarget(Vector3 lastPathPos)
    {   
        lastPathPosition = lastPathPos;
        if (targetEnemy != null && targetEnemy.IsStructure()) //structures need to be evaluated based on closest point
        { 
            if (targetEnemy.physicalCollider != null) //get closest point on collider; //this has an issue
            {
                Vector3 centerToMax = targetEnemy.physicalCollider.bounds.center - targetEnemy.physicalCollider.bounds.max;
                float boundsFakeRadius = centerToMax.magnitude;
                float discrepancyThreshold = boundsFakeRadius + .5f;
                Vector3 closest = targetEnemy.physicalCollider.ClosestPoint(transform.position);
                float rawDist = Vector3.Distance(transform.position, targetEnemy.transform.position);
                float closestDist = Vector3.Distance(transform.position, closest);
                if (Mathf.Abs(rawDist - closestDist) > discrepancyThreshold)
                {
                    targetEnemyClosestPoint = targetEnemy.transform.position; 
                }
                else
                {
                    targetEnemyClosestPoint = closest;
                }
            }

            Vector2 flattenedClosestPoint = new Vector2(targetEnemyClosestPoint.x, targetEnemyClosestPoint.z);
            Vector2 flattenedLastPathPos = new Vector2(lastPathPos.x, lastPathPos.z);
            pathDistFromTarget = (flattenedClosestPoint - flattenedLastPathPos).sqrMagnitude;
            //Debug.DrawLine(targetEnemyClosestPoint, lastPathPos, Color.black);
            //Debug.DrawRay(lastPathPos, Vector3.up, Color.red);
            //Debug.DrawRay(targetEnemyClosestPoint, Vector3.up, Color.yellow);
        }
        else
        {
            pathDistFromTarget = (pathfindingTarget.transform.position - lastPathPos).sqrMagnitude;
        }
        pathReachesDestination = pathDistFromTarget < pathReachesThreshold;
    }*/
    #endregion
    #region UpdaterFunctions
    /*private void UpdateColliderStatus()
    {
        if (rigid != null && IsOwner)
        {
            rigid.isKinematic = currentState switch
            {
                EntityStates.FindInteractable or EntityStates.WalkToInteractable or EntityStates.Harvesting or EntityStates.Depositing or EntityStates.AfterDepositCheck
                or EntityStates.Building or EntityStates.AfterBuildCheck => true,
                _ => false,
            };
        }
    }*/
    private void UpdateInteractors()
    {
        if (ent.interactionTarget != null && ent.interactionTarget.alive)
        {
            switch (lastMajorState)
            {
                case EntityStates.Building:
                case EntityStates.Harvesting:

                    if (!ent.interactionTarget.workersInteracting.Contains(ent)) //if we are not in harvester list
                    {
                        if (ent.interactionTarget.workersInteracting.Count < ent.interactionTarget.allowedWorkers) //if there is space
                        {
                            ent.interactionTarget.workersInteracting.Add(ent);
                        }
                        else //there is no space
                        {
                            //get a new harvest target
                            ent.interactionTarget = null;
                        }
                    }
                    break;
                case EntityStates.Depositing:
                case EntityStates.Garrisoning:
                    if (!ent.interactionTarget.othersInteracting.Contains(ent))
                    {
                        if (ent.interactionTarget.othersInteracting.Count < ent.interactionTarget.allowedInteractors) //if there is space
                        {
                            ent.interactionTarget.othersInteracting.Add(ent);
                        }
                        else //there is no space
                        {
                            ent.interactionTarget = null;
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
        CancelAllAsyncTasks();

        if (ent.controllerOfThis is RTSPlayer)
        {
            RTSPlayer rts = ent.controllerOfThis as RTSPlayer;
            rts.selectedEntities.Remove(ent);
        }
        ent.Select(false);
        SwitchState(EntityStates.Die);
        ai.enabled = false;
        if (ent.RVO != null) ent.RVO.enabled = false;
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
        this.ent.interactionTarget = ent;
        lastMajorState = EntityStates.Building;
        SwitchState(EntityStates.WalkToInteractable);
    }
    #endregion
    #region DetectionFunctions
    /*private void DetectIfShouldStopFollowingMoveOrder()
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
    }*/
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
        if (ent != null)
        {
            ent.HideMoveIndicator();
        }
    }
    #endregion
    private void PlaceOnGroundIfNecessary()
    {
        if (ent.occupiedGarrison == null && (transform.position.y > 0.1f || transform.position.y < 0.1f))
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
        if (!freezePosition) //unfreeze
        {
            ClearObstacle();
            ai.canMove = true;
        }
        else //freeze
        {
            ForceUpdateRealLocation();
            BecomeObstacle();
            ai.canMove = false;
        }
        highPrecisionMovement = freezePosition;

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
    private float graphUpdateTime = 0.02f; //derived through trial and error
    //public bool allowBecomingObstacles = false;
    private void BecomeObstacle()
    {
        if (!ent.alive)
        {
            ent.ClearObstacle();
        }
        else if (!IsGarrisoned())
        {
            ent.MakeObstacle();
        }
    }
    public void ClearObstacle()
    {
        ent.ClearObstacle();
    }
    private void ForceUpdateRealLocation()
    {
        if (IsOwner)
        {
            SetRealLocation();
        }
    }
    public void LookAtTarget(Transform target)
    {
        ent.LookAtTarget(target);
    }


    public bool InRangeOfEntity(SelectableEntity target, float range)
    {
        return ent.InRangeOfEntity(target, range);
    }

    public Vector3 targetEnemyClosestPoint;
    [HideInInspector] public bool shouldAggressivelySeekEnemies = true;
    /*private bool CheckFacingTowards(Vector3 pos)
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
    } */ 
    private void UpdateReadiness()
    {
        if (ent.IsAttacker()) ent.attacker.UpdateReadiness();
        if (ent.IsHarvester()) ent.harvester.UpdateReadiness();
    }
    private float ConvertTimeToFrames(float seconds) //1 second is 50 frames
    {
        float frames = seconds * 50;
        return frames;
    }
    public bool AnimatorUnfinished()
    {
        return animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1; //animator.GetCurrentAnimatorStateInfo(0).length > 
    }
    private SelectableEntity FindClosestBuildable()
    {
        List<SelectableEntity> list = ent.controllerOfThis.ownedEntities;

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

    public int attackMoveDestinationEnemyCount = 0;
    private readonly float defaultMeleeDetectionRange = 4;
    private async Task<SelectableEntity> FindEnemyMinionToAttack(float range)
    {
        Debug.Log("Running idle find enemy minion to attack search");

        if (ent.IsMelee())
        {
            range = defaultMeleeDetectionRange;
        }
        else
        {
            range += 1;
        }

        List<SelectableEntity> enemyList = ent.controllerOfThis.visibleEnemies;
        SelectableEntity valid = null;

        for (int i = 0; i < enemyList.Count; i++)
        {
            SelectableEntity check = enemyList[i];
            if (IsEnemy(check) && check.alive && check.isTargetable.Value && check.IsMinion())
            //only check on enemies that are alive, targetable, visible, and in range. also only care about enemy minions
            {
                if (InRangeOfEntity(check, range)) //ai controlled doesn't care about fog
                {
                    valid = check;
                }
            }
            await Task.Yield();
            if (valid != null) return valid;
        }
        return valid;
    }
    [SerializeField] private bool canAttackStructures = true;
    private readonly float minAttackMoveDestinationViabilityRange = 4;
    private readonly float rangedUnitRangeExtension = 2;


    private float sqrDistToAlternateTarget = 0;

    private bool IsEnemy(SelectableEntity target)
    {
        if (target != null)
        {
            return target.controllerOfThis.allegianceTeamID != ent.controllerOfThis.allegianceTeamID;
        }
        return false;
        //return target.teamNumber.Value != entity.teamNumber.Value;
    } 
    
    #endregion
    #region Attacks 
    private void ReturnState()
    {
        switch (attackType)
        {
            case AttackType.Instant:
                SwitchState(EntityStates.Idle);
                attackReady = true;
                break;
            case AttackType.SelfDestruct:
                break;
            default:
                break;
        }
    }
    private void ClearTargets()
    {
        targetEnemy = null;
        sqrDistToTargetEnemy = Mathf.Infinity;
        ent.interactionTarget = null;
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
        SwitchState(EntityStates.AttackMoving);
        playedAttackMoveSound = false;
        SetDestination(target);
        orderedDestination = destination;
    }
    public void AttackMoveToPosition(Vector3 target) //called by local player
    {
        if (!ent.alive) return; //dead units cannot be ordered
        if (IsGarrisoned()) return;
        GenericAttackMovePrep(target);
    }
    public void PlaceOnGround()
    {
        ent.PlaceOnGround();
    }
    public void ForceBuildTarget(SelectableEntity target)
    {
        if (target.workersInteracting.Count < target.allowedWorkers)
        {
            target.workersInteracting.Add(ent);
        }
        ent.interactionTarget = target;

        SwitchState(EntityStates.WalkToInteractable);
        lastMajorState = EntityStates.Building;
    }
    private float walkStartTimer = 0;
    private readonly float walkStartTimerSet = 1.5f;
    private void BasicWalkTo(Vector3 target)
    {
        ClearTargets();
        ClearIdleness();
        SwitchState(EntityStates.Walk);
        SetOrderedDestination(target);
        walkStartTimer = walkStartTimerSet;

        SelectableEntity justLeftGarrison = null;
        if (ent.occupiedGarrison != null) //we are currently garrisoned
        {
            justLeftGarrison = ent.occupiedGarrison;
            RemovePassengerFrom(ent.occupiedGarrison);
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
        if (currentState != EntityStates.Spawn)
        {
            ClearTargets();
            ClearIdleness();
            SwitchState(EntityStates.WalkToTarget);
            ent.interactionTarget = target;
            SetOrderedDestination(ent.interactionTarget.transform.position);
            walkStartTimer = walkStartTimerSet;

            SelectableEntity justLeftGarrison = null;
            if (ent.occupiedGarrison != null) //we are currently garrisoned
            {
                justLeftGarrison = ent.occupiedGarrison;
                RemovePassengerFrom(ent.occupiedGarrison);
                PlaceOnGround(); //snap to ground
            }
        }
    }
    /// <summary>
    /// Use to send unit to the specified location.
    /// </summary>
    /// <param name="target"></param>
    public void MoveTo(Vector3 target)
    {
        lastCommand.Value = CommandTypes.Move;
        if (currentState != EntityStates.Spawn)
        {
            BasicWalkTo(target);
        }
    }
    public void CommandHarvestTarget(SelectableEntity select)
    {
        if (ent != null && ent.IsHarvester())
        {
            if (ent.harvester.BagHasSpace() && ent.harvester.ValidOreForHarvester(select))
            { 
                Debug.Log("Harvesting");
                lastCommand.Value = CommandTypes.Harvest;
                ent.interactionTarget = select;
                SwitchState(EntityStates.WalkToInteractable);
                lastMajorState = EntityStates.Harvesting;
            } 
            /*else
            {
                Debug.Log("Depositing");
                lastCommand.Value = CommandTypes.Deposit;
                SwitchState(EntityStates.FindInteractable);
                lastMajorState = EntityStates.Depositing;
            }*/
        }
    }
    public void DepositTo(SelectableEntity select)
    {
        lastCommand.Value = CommandTypes.Deposit;
        ent.interactionTarget = select;
        SwitchState(EntityStates.WalkToInteractable);
        lastMajorState = EntityStates.Depositing;
    }
    public void CommandBuildTarget(SelectableEntity select)
    {
        if (ent.IsBuilder())
        {
            if (select.workersInteracting.Count == 1 && select.workersInteracting[0].sm.currentState != EntityStates.Building)
            {
                select.workersInteracting[0].interactionTarget = null;
                select.workersInteracting.Clear();
            }

            //Debug.Log("can build");
            lastCommand.Value = CommandTypes.Build;
            ent.interactionTarget = select;
            SwitchState(EntityStates.WalkToInteractable);
            lastMajorState = EntityStates.Building;
        }
    }
    /*private bool IsGarrisoned()
    {
        return ent.occupiedGarrison != null;
    }*/
    /*public void WorkOnGarrisoningInto(SelectableEntity garrison)
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
                justLeftGarrison = ent.occupiedGarrison;
                ent.occupiedGarrison.UnloadPassenger(this); //leave garrison by moving out of it
                PlaceOnGround(); //snap to ground
            }
            // && selectableEntity.garrisonablePositions.Count <= 0
            if (justLeftGarrison != garrison) //not perfect, fails on multiple units
            {
                if (garrison.acceptsHeavy || !ent.isHeavy)
                {
                    ent.interactionTarget = garrison;
                    SwitchState(EntityStates.WalkToInteractable);
                    lastMajorState = EntityStates.Garrisoning;
                    //Debug.Log("Moving into garrison");
                }
            }
            lastCommand.Value = CommandTypes.Move;
            *//*SelectableEntity garrison = select.occupiedGarrison;
            selectableEntity.tryingToTeleport = true;
            selectableEntity.interactionTarget = select;
            state = State.WalkToInteractable;
            lastState = State.Garrisoning;*//*
        }
    }*/
    public void ChangeRVOStatus(bool val)
    {
        if (ent.RVO != null) ent.RVO.enabled = val;
    }
    private void LoadPassengerInto(SelectableEntity garrison)
    {
        if (garrison.controllerOfThis.playerTeamID == ent.controllerOfThis.playerTeamID)
        {
            ChangeRVOStatus(false);
            SwitchState(EntityStates.Idle);
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
