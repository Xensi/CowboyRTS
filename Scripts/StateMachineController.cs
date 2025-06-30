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
using static UnitAnimator;

//used for entities that can attack
[RequireComponent(typeof(Entity))]
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
        PushableIdle,
        Walk,
        AttackMoving,
        WalkToSpecificEnemy,
        Attacking,
        FindInteractable,
        WalkToInteractable,
        Building,
        Spawn,
        Die,
        Harvesting,
        AttackCooldown,
        Depositing,
        Garrisoning,
        WalkToRally,
        WalkToTarget,
        UsingAbility,
    }
    #endregion

    public EntityStates lastMajorState = EntityStates.Idle;

    #region Hidden
    LayerMask enemyMask;
    private Camera cam;
    private RaycastModifier rayMod;
    private readonly float spawnDuration = .5f;
    private readonly sbyte buildDelta = 5;
    public float stateTimer = 0;
    private float rotationSpeed = 10f;
    //[HideInInspector] public bool followingMoveOrder = false;
    #endregion
    #region Variables

    [Header("Behavior Settings")]

    //[SerializeField] private LocalRotation localRotate;
    //[HideInInspector] public bool canMoveWhileAttacking = false; //Does not do anything yet
    public readonly float garrisonRange = 1.1f;
    //50 fps fixed update
    //private readonly int delay = 0; 
    public EntityStates currentState = EntityStates.Spawn;
    public Entity.RallyMission givenMission = Entity.RallyMission.None;
    #endregion
    #region NetworkVariables 
    public bool canReceiveNewCommands = true;

    [SerializeField] private List<TrailRenderer> attackTrails = new();
    [HideInInspector]
    public NetworkVariable<CommandTypes> lastCommand = new NetworkVariable<CommandTypes>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    #endregion
    public Pathfinder pf;
    public UnitAnimator anim;

    #region Core 
    private bool CanPathfind()
    {
        return pf != null;
    }
    #region Just Spawned Code
    #region Awake
    private void Awake()
    {
        Initialize();
        enemyMask = LayerMask.GetMask("Entity", "Obstacle");
        nearbyIndexer = 0;
    }
    [HideInInspector] public Entity ent;
    [HideInInspector] public Collider col;
    [HideInInspector] private Rigidbody rigid;
    public Attacker attacker;
    private void Initialize()
    {
        col = GetComponent<Collider>();
        ent = GetComponent<Entity>();
        rigid = GetComponent<Rigidbody>();
        cam = Camera.main;
    }
    #endregion
    #region Network Spawn 
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            SwitchState(EntityStates.Spawn);
            Invoke(nameof(FinishSpawning), spawnDuration); //jank 
            lastCommand.Value = CommandTypes.Move;
        }
        else //Representation that other players see
        {
            rigid.isKinematic = true; //don't get knocked around
            //gameObject.layer = LayerMask.NameToLayer("OtherEntities"); //can pass through each other 
        }
    }
    #endregion
    #region Start Code 
    private void Start()
    {
        if (ent.IsAttacker())
        {
            if (IsMelee())
            {
                ent.attacker.maximumChaseRange = Global.instance.defaultMeleeSearchRange;
            }
            else
            {
                ent.attacker.maximumChaseRange = ent.attacker.range * 2f;
            }
        }
        attackMoveDestinationEnemyArray = new Entity[Global.instance.attackMoveDestinationEnemyArrayBufferSize];
        ChangeAttackTrailState(false);
    }
    #endregion
    #endregion
    public Entity[] attackMoveDestinationEnemyArray = new Entity[0];

    private bool IsMelee()
    {
        return ent.IsMelee();
    }
    private bool IsRanged()
    {
        return ent.IsRanged();
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
    public EntityStates GetState()
    {
        return currentState;
    }
    public bool IsCurrentlyBuilding()
    {
        return currentState == EntityStates.Building || currentState == EntityStates.WalkToInteractable && lastMajorState == EntityStates.Building;
    }

    //private bool finishedInitializingRealLocation = false;
    
    private void Update()
    {
        //update real location, or catch up
        if (!ent.fakeSpawn && IsSpawned)
        {
            ent.pf.GetActualPositionChange();
            ent.pf.UpdateIdleCount();
            if (IsOwner)
            {
                //EvaluateNearbyEntities();
                UpdateRealLocation();
                ent.pf.UpdateMinionTimers();
                UpdateReadiness();
                UpdateInteractors();
                OwnerUpdateState();
                if (pf != null) pf.UpdateRepathRate();
                //UpdateSetterTargetPosition();
                //FixGarrisonObstacle();
                ent.attacker.UpdateTargetEnemyLastPosition();
            }
            else // if (finishedInitializingRealLocation) //not owned by us
            {
                pf.NonOwnerPathfindToOldestRealLocation();
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
    /// <summary>
    /// No functionality.
    /// </summary>
    private void FixGarrisonObstacle()
    {
        /*if (IsGarrisoned())
        {
            ClearObstacle();
        }*/
    }
    //float stopDistIncreaseThreshold = 0.01f;
    private void ClientSeekEnemy()
    {
        /*if (nearbyIndexer >= Global.Instance.allEntities.Count)
        {
            nearbyIndexer = Global.Instance.allEntities.Count - 1;
        }
        SelectableEntity check = Global.Instance.allEntities[nearbyIndexer]; //fix this so we don't get out of range 
        if (clientSideTargetInRange == null)
        {
            if (check != null && check.alive && check.teamNumber.Value != ent.teamNumber.Value) //  && check.visibleInFog <-- doesn't work?
            { //only check on enemies that are alive, targetable, visible, and in range  
                clientSideTargetInRange = check;
            }
        }
        nearbyIndexer++;
        if (nearbyIndexer >= Global.Instance.allEntities.Count) nearbyIndexer = 0;

        if (clientSideTargetInRange != null)
        {
            FreezeRigid();
        }*/
    }
    private void NonOwnerUpdateAnimationBasedOnContext()
    {
        /*if (ent.alive)
        {
            if (ent.occupiedGarrison != null)
            {
                //ClientSeekEnemy();
                if (clientSideTargetInRange != null)
                {
                    //Debug.DrawLine(transform.position, clientSideEnemyInRange.transform.position, Color.red, 0.1f);
                    anim.Play("Attack");
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
                        ent.pf.IdleOrWalkContextuallyAnimationOnly();
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
        }*/
    }
    private void ContextualIdleOrHarvestBuild()
    {
        /*if (effectivelyIdleInstances > idleThreshold || ai.reachedDestination)
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
        }*/
    }
    private void ContextualIdleOrAttack()
    {
        /*if (effectivelyIdleInstances > idleThreshold || ai.reachedDestination)
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
        }*/
    }
    private void ClientSeekHarvestable()
    {
        /*if (nearbyIndexer >= Global.Instance.allEntities.Count)
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
        }*/
    }

    /*private void ClientSeekBuildable()
    {
        if (nearbyIndexer >= Global.instance.allEntities.Count)
        {
            nearbyIndexer = Global.instance.allEntities.Count - 1;
        }
        SelectableEntity check = Global.instance.allEntities[nearbyIndexer]; //fix this so we don't get out of range 
        if (clientSideTargetInRange == null)
        {
            if (check != null && check.alive && check.teamNumber == ent.teamNumber &&
                !check.fullyBuilt) //  && check.visibleInFog <-- doesn't work?
            { //only check on enemies that are alive, targetable, visible, and in range  
                clientSideTargetInRange = check;
            }
        }
        nearbyIndexer++;
        if (nearbyIndexer >= Global.instance.allEntities.Count) nearbyIndexer = 0;

        if (clientSideTargetInRange != null)
        {
            pf.FreezeRigid();
        }
    }*/
    private Entity clientSideTargetInRange = null;
    private void UpdateRealLocation()
    {
        //float updateThreshold = 1f; //does not need to be equal to allowed error, but seems to work when it is
        Vector3 offset = transform.position - pf.oldRealLocation;
        float dist = offset.sqrMagnitude;//Vector3.Distance(transform.position, realLocation.Value);

        if (dist > Global.instance.updateRealLocThreshold * Global.instance.updateRealLocThreshold) //square the distance to compare against
        {
            //realLocationReached = false;
            pf.SetRealLocation();
        }
    }
    //private bool realLocationReached = false;
    //private float updateRealLocThreshold = 1f; //1
    //private readonly float allowedNonOwnerError = 1.5f; //1.5 ideally higher than real loc update; don't want to lerp to old position
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
    private bool FollowGivenMission()
    {
        bool valid = false;
        switch (givenMission)
        {
            case Entity.RallyMission.None:
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
            case Entity.RallyMission.Move:
                SwitchState(EntityStates.WalkToRally);
                valid = true;
                break;
            case Entity.RallyMission.Harvest:
                SwitchState(EntityStates.WalkToInteractable);
                lastMajorState = EntityStates.Harvesting;
                valid = true;
                break;
            case Entity.RallyMission.Build:
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
                valid = true;
                break;
            case Entity.RallyMission.Garrison:
                SwitchState(EntityStates.WalkToInteractable);
                lastMajorState = EntityStates.Garrisoning;
                valid = true;
                break;
            case Entity.RallyMission.Attack:
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
                valid = true;
                break;
            default:
                break;
        }
        return valid;
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
                //CancelAsyncSearch();
                CancelTimers();
                break;
            case EntityStates.AttackMoving:
                //CancelAsyncSearch();
                CancelTimers();
                break;
        }
    }

    private void EnterState(EntityStates state)
    {
        //Debug.Log("Entering state" + state + "Currently in state " + currentState);
        switch (state)
        {
            case EntityStates.Attacking:
            case EntityStates.AttackMoving:
                if (ent.IsAttacker()) ent.attacker.OnEnterState();
                break;
        }
    }
    private void UpdateIfCanReceiveNewCommands(EntityStates state)
    {
        if (state == EntityStates.UsingAbility)
        {
            canReceiveNewCommands = false;
        }
        else
        {
            canReceiveNewCommands = true;
        }
    }
    public async void SwitchState(EntityStates stateToSwitchTo)
    {
        if (ent.IsAlliedTo(ent.controllerOfThis))
        {
            Debug.Log(name + " is switching state to: " + stateToSwitchTo);
        }
        UpdateIfCanReceiveNewCommands(stateToSwitchTo);
        switch (stateToSwitchTo)
        {
            case EntityStates.Attacking:
                //attackOver = false;
                if (ent.IsAttacker()) ent.attacker.OnSwitchState();
                stateTimer = 0;
                timerUntilAttackTrailBegins = 0;
                attackTrailTriggered = false;
                ChangeAttackTrailState(false);
                if (pf != null) pf.FreezeRigid(true, false);
                break;
            case EntityStates.Harvesting:
            case EntityStates.Building:
                stateTimer = 0;
                if (pf != null) pf.FreezeRigid(true, false);
                break;
            case EntityStates.Idle:
            case EntityStates.Die:
            case EntityStates.Spawn:
            case EntityStates.FindInteractable:
                if (pf != null)
                {
                    pf.FreezeRigid(true, true);
                    pf.ResetEndReachedDistance();
                }
                break;
            case EntityStates.PushableIdle:
                if (pf != null)
                {
                    pf.FreezeRigid(false, false);
                    pf.ResetEndReachedDistance();
                    pf.ResetPushableIdleTimer();
                }
                break;
            case EntityStates.UsingAbility:
                if (pf != null) pf.FreezeRigid(true, true);
                //animator.Play("UseAbility");
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
                if (pf != null)
                {
                    pf.ClearIdleness();
                    pf.SetWalkStartTimer();
                    pf.FreezeRigid(false, false);
                }
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
            if (pf != null) pf.ChangeBlockedByMinionObstacleStatus(false);
        }
    }
    private bool skipFirstFrame = true;
    private bool attackTrailActive = false;

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
    public void ProcessOrder(UnitOrder order)
    {
        //Debug.Log("Processing order");
        //if (attacker != null) attacker.ResetGoal();
        Vector3 targetPosition = order.targetPosition;
        Entity target = order.target;
        lastOrderType = order.action;
        switch (order.action)
        {
            case ActionType.Move:
            case ActionType.AttackTarget:
            case ActionType.Harvest:
            case ActionType.Deposit:
            case ActionType.Garrison:
            case ActionType.BuildTarget:
            case ActionType.MoveToTarget:
                if (attacker != null && attacker.assignedEntitySearcher != null) attacker.RemoveFromEntitySearcher();
                break;
        }
        //if we made a crosshair
        if (ent.createdCrosshair != null && ent.createdCrosshair.assignedEntity != null)
        {
            ent.createdCrosshair.assignedEntity.manualCrosshairTargetingThis = null;
            Destroy(ent.createdCrosshair.gameObject);
        }
        if (order.action == ActionType.AttackTarget) //if given attack order
        {
            //if target doesn't have a crosshair, create it
            if (target.manualCrosshairTargetingThis == null)
            {
                CrosshairDisplay cd = Instantiate(Global.instance.crosshairPrefab, target.transform);
                target.manualCrosshairTargetingThis = cd;
                cd.assignedEntity = target;
                cd.SetPulse(true);
                ent.createdCrosshair = cd;
            }
        }
        switch (order.action)
        {
            case ActionType.MoveToTarget:
                if (pf != null) pf.MoveToTarget(target);
                break;
            case ActionType.AttackMove:
                if (attacker != null) attacker.AttackMoveToPosition(targetPosition);
                break;
            case ActionType.Move:
                if (pf != null) pf.MoveTo(targetPosition);
                break;
            case ActionType.AttackTarget:
                //Debug.Log("Processing order to " + order.action + order.target);
                AttackTarget(target);
                break;
            case ActionType.Harvest:
                CommandHarvestTarget(target);
                break;
            case ActionType.Deposit: //try to deposit if we have stuff to deposit
                if (ent.HasResourcesToDeposit())
                {
                    DepositTo(target);
                }
                else if (target.IsDamaged() && ent.IsBuilder()) //if its damaged, we can try to build it
                {
                    CommandBuildTarget(target);
                }
                else
                {
                    if (pf != null) pf.MoveToTarget(target);
                }
                break;
            case ActionType.Garrison:
                //WorkOnGarrisoningInto(target);
                break;
            case ActionType.BuildTarget://try determining how many things need to be built in total, and grabbing closest ones
                if (ent.IsBuilder())
                {
                    CommandBuildTarget(target);
                }
                else
                {
                    //WorkOnGarrisoningInto(target);
                }
                break;
        }
        lastOrder = order;
    }
    private void AttackTarget(Entity select)
    {
        //Debug.Log("Received order to attack " + select.name);
        if (ent.IsAttacker() && ent.attacker.IsValidTarget(select))
        {
            lastCommand.Value = CommandTypes.Attack;
            if (pf != null) pf.ClearIdleness();
            if (select.IsStructure())
            {
                pf.NudgeTargetEnemyStructureDestination(select);
                //pf.nudgedTargetEnemyStructurePosition = select.transform.position;
            }
            ent.attacker.AttackTarget(select);
        }
    }
    [SerializeField] private float attackTrailBeginTime = 0.2f;
    private float timerUntilAttackTrailBegins = 0;
    private bool attackTrailTriggered = false;
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
    private List<Entity> preservedAsyncSearchResults = new();
    /// <summary>
    /// Cancel async timers.
    /// </summary>
    private void CancelTimers()
    {
        /*hasCalledEnemySearchAsyncTaskTimerCancellationToken?.Cancel();
        pathStatusTimerCancellationToken?.Cancel();*/
    }
    public void SetLastMajorState(EntityStates state)
    {
        lastMajorState = state;
    }
    private void CancelAllAsyncTasks()
    {
        /*CancelAsyncSearch();
        CancelTimers();*/
    }

    public bool InState(EntityStates state)
    {
        return currentState == state;
    }
    bool missionFollowed = false;
    private void OwnerUpdateState()
    {
        CheckIfAttackTrailIsActiveErroneously();
        if (anim != null) anim.StateBasedAnimations();
        switch (currentState)
        {
            case EntityStates.Spawn: //play the spawn animation  
                break;
            case EntityStates.Die:
                break;
            case EntityStates.Idle:
                /*if (ent.occupiedGarrison == null) //if not in garrison
                {
                }
                else
                {
                    //GarrisonedSeekEnemies();
                }*/
                if (!missionFollowed)
                {
                    missionFollowed = FollowGivenMission();
                }
                if (ent.IsAttacker()) ent.attacker.IdleState();
                if (pf != null) pf.IdleState();
                break;
            case EntityStates.PushableIdle:
                if (!missionFollowed)
                {
                    missionFollowed = FollowGivenMission();
                }
                if (ent.IsAttacker()) ent.attacker.IdleState();
                if (pf != null) pf.PushableIdleState();
                break;
            case EntityStates.UsingAbility:
                if (skipFirstFrame) //neccesary to give animator a chance to catch up
                {
                    skipFirstFrame = false;
                }
                else if (!anim.InState(USE_ABILITY))
                {
                    SwitchState(EntityStates.Idle);
                    ResumeLastOrder();
                }
                break;
            case EntityStates.Walk:
                if (pf != null) pf.WalkState();
                break;
            case EntityStates.WalkToTarget:
            /*UpdateStopDistance();
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
            }*/
            case EntityStates.WalkToRally:
                if (pf != null) pf.WalkToRallyState();
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
                //prioritize based on last state
                switch (lastMajorState) //this may be broken by the recent lastState change to switchstate
                {
                    case EntityStates.Building:
                        /*if (InvalidBuildable(ent.interactionTarget))
                        {
                            ent.interactionTarget = FindClosestBuildable();
                        }
                        else
                        {
                            SwitchState(EntityStates.WalkToInteractable);
                        }*/
                        break;
                    case EntityStates.Harvesting:
                        if (ent.harvester != null) ent.harvester.FindHarvestableState();
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
                if (pf != null) pf.WalkToInteractableState();
                switch (lastMajorState)
                {
                    case EntityStates.Building:
                        if (ent.IsBuilder()) ent.builder.WalkToBuildable();
                        break;
                    case EntityStates.Harvesting:
                        if (ent.harvester != null) ent.harvester.WalkToOre();
                        break;
                    case EntityStates.Depositing:
                        if (ent.harvester == null) return;
                        ent.harvester.WalkToDepot();
                        break;
                    case EntityStates.Garrisoning:
                        /*if (ent.interactionTarget == null)
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
                        }*/
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
        //UpdatePathStatus();

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
                pf.ai.GetRemainingPath(buffer, out bool stale);
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
            case EntityStates.Idle:
            case EntityStates.Attacking:
            case EntityStates.FindInteractable:
            case EntityStates.Building:
            case EntityStates.Spawn:
            case EntityStates.Die:
            case EntityStates.Harvesting:
            case EntityStates.AttackCooldown:
            case EntityStates.Depositing:
            case EntityStates.Garrisoning:
            case EntityStates.UsingAbility:
                if (ent.lineIndicator != null) ent.lineIndicator.enabled = false;
                break;
        }

    }
    #endregion
    #region UpdaterFunctions 
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
            //rts.selectedEntities.Remove(ent);
        }
        ent.Select(false);
        SwitchState(EntityStates.Die);
        pf.ai.enabled = false;
        if (ent.pf.RVO != null) ent.pf.RVO.enabled = false;
        pf.seeker.enabled = false;
        Destroy(rigid);
        Destroy(col);
    }
    /// <summary>
    /// Tell this minion to go and construct a building. Clears obstacle.
    /// </summary>
    /// <param name="pos"></param>
    /// <param name="ent"></param>
    public void SetBuildDestination(Vector3 pos, Entity ent)
    {
        //destination = pos;
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
        if (pf.ai == null) return;
        // The path will be returned when the path is over a specified length (or more accurately when the traversal cost is greater than a specified value).
        // A score of 1000 is approximately equal to the cost of moving one world unit.
        int fleeAmount = 1000 * 1;

        // Create a path object
        FleePath path = FleePath.Construct(transform.position, position, fleeAmount);
        // This is how strongly it will try to flee, if you set it to 0 it will behave like a RandomPath
        path.aimStrength = 1;
        // Determines the variation in path length that is allowed
        path.spread = 4000;
        pf.ai.SetPath(path);
    }
    private float graphUpdateTime = 0.02f; //derived through trial and error
    //public bool allowBecomingObstacles = false;
    public void LookAtTarget(Transform target)
    {
        ent.LookAtTarget(target);
    }

    public bool InRangeOfEntity(Entity target, float range)
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
        if (ent.IsBuilder()) ent.builder.UpdateReadiness();
    }
    private float ConvertTimeToFrames(float seconds) //1 second is 50 frames
    {
        float frames = seconds * 50;
        return frames;
    }
    private Entity FindClosestBuildable()
    {
        List<Entity> list = ent.controllerOfThis.ownedEntities;

        Entity closest = null;
        float distance = Mathf.Infinity;
        foreach (Entity item in list)
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



    #endregion
    #region Attacks  
    public void AIAttackMove(Vector3 target)
    {
        //AttackMoveToPosition(target);
    }
    public void PlaceOnGround()
    {
        ent.PlaceOnGround();
    }
    public void ForceBuildTarget(Entity target)
    {
        if (target.workersInteracting.Count < target.allowedWorkers)
        {
            target.workersInteracting.Add(ent);
        }
        ent.interactionTarget = target;

        SwitchState(EntityStates.WalkToInteractable);
        lastMajorState = EntityStates.Building;
    }
    public void ClearTargets()
    {
        ent.attacker.targetEnemy = null;
        ent.attacker.sqrDistToTargetEnemy = Mathf.Infinity;
        ent.interactionTarget = null;
    }
    public void CommandHarvestTarget(Entity select)
    {
        //Debug.Log("Received command to harvest");
        if (ent != null && ent.IsHarvester())
        {
            //Debug.Log("Is harvester");
            if (ent.harvester.BagHasSpace())
            {
                //Debug.Log("bag");
                if (ent.harvester.ValidOreForHarvester(select))
                {
                    //Debug.Log("valid ore");
                    lastCommand.Value = CommandTypes.Harvest;
                    ent.interactionTarget = select;
                    SwitchState(EntityStates.WalkToInteractable);
                    lastMajorState = EntityStates.Harvesting;
                }
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
    public void DepositTo(Entity select)
    {
        lastCommand.Value = CommandTypes.Deposit;
        ent.interactionTarget = select;
        SwitchState(EntityStates.WalkToInteractable);
        lastMajorState = EntityStates.Depositing;
    }
    public void CommandBuildTarget(Entity select)
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
    public void ChangeRVOStatus(bool val) //used
    {
        if (ent.pf.RVO != null) ent.pf.RVO.enabled = val;
    }
    private void LoadPassengerInto(Entity garrison)
    {
        /*if (garrison.controllerOfThis.playerTeamID == ent.controllerOfThis.playerTeamID)
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
        }*/
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
            if (reference.TryGet(out Entity select))
            {
                //select
                select.ReceivePassenger(this);
            }
        }
    }
    public void RemovePassengerFrom(Entity garrison)
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
            if (reference.TryGet(out Entity select))
            {
                //select
                select.UnloadPassenger(this);
            }
        }
    }
    #endregion
}
