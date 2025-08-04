using NUnit.Framework.Internal.Commands;
using Pathfinding;
using Pathfinding.RVO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Relay.Models;
using UnityEngine;
using static StateMachineController;
using static UnitAnimator;
using UtilityMethods;
using static Player;
using static EntityStates;

public class Pathfinder : EntityAddon
{
    public bool pathStatusValid = false; //when this is true, the current path result is valid
    public bool pathfindingValidationTimerActive = false;
    private float pathfindingValidationTimerDuration = 0.5f;
    public enum PathStatus { Pending, Reaches, Blocked }
    public PathStatus pathReachesDestination = PathStatus.Pending;
    public float pathDistFromTarget = 0;
    [HideInInspector] public Vector3 orderedDestination; //remembers where player told minion to go

    //controls where the AI will pathfind to
    [HideInInspector] public Vector3 destination;
    CancellationTokenSource pathStatusTimerCancellationToken;
    [HideInInspector] public AIPath ai;
    [HideInInspector] public Seeker seeker;
    [HideInInspector] public RVOController RVOAvoidance;

    public override void Awake()
    {
        base.Awake(); 
        setter = GetComponent<AIDestinationSetter>();
        ai = GetComponent<AIPath>();
        seeker = GetComponent<Seeker>();
        RVOAvoidance = GetComponent<RVOController>();
    }
    public override void OnNetworkSpawn()
    {
        SetRealLocation();
        realLocation.OnValueChanged += OnRealLocationChanged;
        oldPosition = transform.position;
        orderedDestination = transform.position;
        if (!IsOwner)
        {
            nonOwnerRealLocationList.Add(transform.position);
            RVOAvoidance.enabled = false;
        }
    }
    public void Start()
    { 
        FactionEntity factionEntity = ent.factionEntity;
        if (factionEntity is FactionUnit)
        {
            FactionUnit factionUnit = factionEntity as FactionUnit;
            {
                if (ai != null) 
                {
                    ai.maxSpeed = factionUnit.maxSpeed; 
                }
            }
        }
        if (setter != null && setter.target == null)
        { //create a target that our setter will use to update our pathfinding
            GameObject obj = new GameObject(ent.name + " pf target");
            //obj.transform.parent = Global.instance.transform;
            pathfindingTarget = obj.transform;
            pathfindingTarget.position = transform.position; //set to be on us
            setter.target = pathfindingTarget;
        }
        defaultMoveSpeed = ai.maxSpeed;
        defaultEndReachedDistance = ai.endReachedDistance;
    }

    public override void UpdateAddon()
    {
        UpdatePathStatus();
    }

    [HideInInspector] public float defaultMoveSpeed = 0;

    /// <summary>
    /// Use to check if path reaches a position. Do not use to check if path reaches a structure. Assumes that the path has been searched already,
    /// so may fail if it has not been searched manually or automatically before calling this
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    public bool EndOfPathReachesPosition(Vector3 position)
    {
        float squaredDistance = SquaredDistanceOfPathEndToPos(position);


        float pathThreshold = 0.1f;
        float leeway = 0;
        if (at != null) leeway = at.range;
        bool val = squaredDistance < pathThreshold * pathThreshold + leeway * leeway;


        var buffer = new List<Vector3>();
        ai.GetRemainingPath(buffer, out bool stale);
        Debug.DrawRay(buffer.Last(), Vector3.up, Color.blue);

        if (val)
        {
            Debug.DrawRay(position, Vector3.up*2, Color.green);
        }
        else
        {
            Debug.DrawRay(position, Vector3.up*2, Color.red);
        }

        return val;
    }
    /// <summary>
    /// Returns squared length of the path to a position. Compare against a squared threshold.
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    private float SquaredDistanceOfPathEndToPos(Vector3 position)
    {
        var buffer = new List<Vector3>();
        ai.GetRemainingPath(buffer, out bool stale);
        float dist = (position - buffer.Last()).sqrMagnitude;

        /*Vector3 prePos = buffer[0];
        for (int i = 1; i < buffer.Count; i++)
        {
            Debug.DrawLine(prePos, buffer[i], Color.blue);
            prePos = buffer[i];
        }*/
        return dist;
    }
    private void CheckIfPathIsClear(Vector3 position)
    {
        var buffer = new List<Vector3>();
        ai.GetRemainingPath(buffer, out bool stale);

        Vector3 prePos = buffer[0];
        for (int i = 1; i < buffer.Count; i++)
        {
            Debug.DrawLine(prePos, buffer[i], Color.blue);
            Debug.DrawRay(prePos, Vector3.up, Color.red);
            prePos = buffer[i];
        }
    }
    public void GetPushedIfIdle()
    {
        if (!InState(EntityStates.Idle)) return;
        SwitchState(EntityStates.PushableIdle);
    }
    public async void PushNearbyOwnedIdlers()
    {
        if (IsEffectivelyIdle(0.25f))
        {
            //Debug.Log("Pushing idlers");
            int maxSearched = 10;
            Collider[] nearby = new Collider[maxSearched];
            int searchedCount = 0;
            float searchRange = ai.radius * 2;
            searchedCount = Physics.OverlapSphereNonAlloc(ent.transform.position, searchRange, nearby, Global.instance.friendlyEntityLayer);
            for (int i = 0; i < searchedCount; i++)
            {
                if (nearby[i] == null) continue;
                Entity ent = nearby[i].GetComponent<Entity>();
                if (ent == null) continue;
                if (ent.pf == null) continue;
                ent.pf.GetPushedIfIdle();
                await Task.Yield();
            }
        }
    }
    /// <summary>
    /// Update repath rate based on order or current state. Attack move has a faster repath rate.
    /// </summary>
    public void UpdateRepathRate()
    {
        float defaultPathRate = 1f;
        float slowPathRate = 2f;
        float fastPathRate = 0.5f;
        pf.ai.autoRepath.maximumPeriod = defaultPathRate;
        pf.ai.autoRepath.mode = AutoRepathPolicy.Mode.Dynamic;

        if (sm.lastOrderType == ActionType.AttackMove 
            || sm.currentState == EntityStates.WalkToSpecificEnemy || sm.currentState == EntityStates.Walk)
        {
            pf.ai.autoRepath.maximumPeriod = fastPathRate;
        }
        else if (sm.currentState == EntityStates.Idle)
        {
            //pf.ai.autoRepath.mode = AutoRepathPolicy.Mode.Never;
            pf.ai.autoRepath.maximumPeriod = slowPathRate;
        }
        else if (sm.currentState == EntityStates.Harvesting || sm.currentState == EntityStates.Building)
        {
            pf.ai.autoRepath.mode = AutoRepathPolicy.Mode.Never;
        }
    }
    private void BecomeObstacle()
    {
        if (!ent.alive)
        {
            ent.ClearObstacle();
        }
        else // if (!IsGarrisoned())
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
    [HideInInspector]
    public NetworkVariable<Vector2Int> realLocation = new NetworkVariable<Vector2Int>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public Vector3 oldRealLocation;
    public void SetRealLocation()
    {
        oldRealLocation = transform.position;
        realLocation.Value = QuantizePosition(transform.position);
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
    public List<Vector3> nonOwnerRealLocationList = new(); //only used by non owners to store real locations that should be pathfound to sequentially

    public void NonOwnerPathfindToOldestRealLocation()
    {
        if (nonOwnerRealLocationList.Count > 0)
        {
            if (nonOwnerRealLocationList.Count >= Global.instance.maximumQueuedRealLocations)
            {
                nonOwnerRealLocationList.RemoveAt(0); //remove oldest 

            }
            if (Vector3.Distance(nonOwnerRealLocationList[0], transform.position) > Global.instance.allowedNonOwnerError)
            {
                transform.position = LerpPosition(transform.position, nonOwnerRealLocationList[0]);
                if (pf.ai != null) pf.ai.enabled = false;
            }
            else
            {
                ent.pf.pathfindingTarget.position = nonOwnerRealLocationList[0]; //update pathfinding target to oldest real location 
                if (pf.ai != null) pf.ai.enabled = true;
            }

            if (nonOwnerRealLocationList.Count > 1)
            {
                Vector3 offset = transform.position - ent.pf.pathfindingTarget.position;
                float dist = offset.sqrMagnitude;
                //for best results, make this higher than unit's slow down distance. at time of writing slowdown dist is .2
                if (dist < Global.instance.closeEnoughDist * Global.instance.closeEnoughDist) //square the distance to compare against
                {
                    nonOwnerRealLocationList.RemoveAt(0); //remove oldest 
                }
                pf.ai.maxSpeed = pf.defaultMoveSpeed * (1 + (nonOwnerRealLocationList.Count - 1) / Global.instance.maximumQueuedRealLocations);
            }
        }
        else //make sure we have at least one position
        {
            nonOwnerRealLocationList.Add(transform.position);
        }
    }
    public void OnRealLocationChanged(Vector2Int prev, Vector2Int cur)
    {
        //finishedInitializingRealLocation = true;
        if (!IsOwner)
        {
            //may have to ray cast down to retrieve height data
            Vector3 deq = DequantizePosition(realLocation.Value);
            if (Physics.Raycast(deq + (new Vector3(0, 100, 0)), Vector3.down, out RaycastHit hit, Mathf.Infinity, Global.instance.groundLayer))
            {
                deq.y = hit.point.y;
            }
            nonOwnerRealLocationList.Add(deq);
        }
    }
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

        m_CurrentLerpTime += Time.deltaTime * Global.instance.lerpScale;

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
            if (ai != null) ai.canMove = true;
        }
        else //freeze
        {
            ForceUpdateRealLocation();
            BecomeObstacle();
            if (ai != null) ai.canMove = false;
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
        if (ent.rigid != null) ent.rigid.constraints = posCon | rotCon;
    }
    private readonly float pathReachesThreshold = 0.25f;
    public Vector3 lastPathPosition;
    public Transform pathfindingTarget;
    private Vector3 PFTargetPos()
    {
        return pathfindingTarget.transform.position;
    }
    public void IdleState()
    {
        ValidatePathStatus();
        //path reaches and we're a distance away from the pathfinding target
        if (!Util.FastDistanceCheck(ent.transform.position, PFTargetPos(), 0.75f) && PathReaches())
        {
            //Debug.Log("We can resume moving");
            SwitchState(EntityStates.Walk);
        }
    }
    public void WalkState()
    {
        //Debug.Log("WalkState");
        UpdateStopDistance();
        DetectIfShouldReturnToIdle();
        PushNearbyOwnedIdlers();
    }
    float pushableIdleTimer = 0;
    readonly float pushableIdleMaxTime = 2;

    public void PushableIdleState()
    {
        // pushable means unit is not frozen and will automatically walk back to its destination
        // stop walking back after some time
        if (pushableIdleTimer < pushableIdleMaxTime)
        {
            pushableIdleTimer += Time.deltaTime;
        }
        else
        {
            SwitchState(EntityStates.Idle);
        }
    }
    public void WalkToRallyState()
    {
        //Debug.Log("WalkRallyState");
        switch (sm.givenMission)
        {
            case Entity.RallyMission.None:
                break;
            case Entity.RallyMission.Move:
                SetDestinationIfHighDiff(ent.GetRallyDest());
                break;
            case Entity.RallyMission.Harvest:
                break;
            case Entity.RallyMission.Build:
                break;
            case Entity.RallyMission.Garrison:
                break;
            case Entity.RallyMission.Attack:
                break;
            default:
                break;
        }
        UpdateStopDistance();
        DetectIfShouldReturnToIdle();
        PushNearbyOwnedIdlers();
    }
    public void WalkToInteractableState()
    {
        PushNearbyOwnedIdlers();
    }
    public void ResetPushableIdleTimer()
    {
        pushableIdleTimer = 0;
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
        pathDistanceFromDestination = DistanceOfPathEndToPos(pathfindingTarget.transform.position);
    }
    /// <summary>
    /// Returns squared length of the path to a position. Compare against a squared threshold.
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    private float DistanceOfPathEndToPos(Vector3 position)
    {
        var buffer = new List<Vector3>();
        ai.GetRemainingPath(buffer, out bool stale);
        float dist = (position - buffer.Last()).magnitude;
        return dist;
    }
    public float pathDistanceFromDestination = Mathf.Infinity;
    /// <summary>
    /// This is a timer that runs for 100 ms if the path status is invalid. The path status can become invalid by the destination
    /// changing. After the timer elapses, the path status will become valid, meaning that the game has had enough time to do path
    /// calculations. This timer is set up in a way so that it can be safely cancelled. It will be cancelled if the attack moving
    /// state is exited.
    /// </summary>
    public async void ValidatePathStatus()
    {
        if (!pathStatusValid && !pathfindingValidationTimerActive) //path status becomes invalid if the destination changes, since we need to recalculate and ensure the
        { //blocked status is correct 
            pathStatusTimerCancellationToken = new CancellationTokenSource();
            pathfindingValidationTimerActive = true;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(pathfindingValidationTimerDuration), pathStatusTimerCancellationToken.Token);
            }
            catch
            {
                //Debug.Log("Timer1 was cancelled!");
                return;
            }
            finally
            {
                pathStatusTimerCancellationToken?.Dispose();
                pathStatusTimerCancellationToken = null;
                pathStatusValid = true;
                pathfindingValidationTimerActive = false;
            }
        }
    }

    public void ResetEndReachedDistance()
    {
        ai.endReachedDistance = defaultEndReachedDistance;
    }

    /// <summary>
    /// Returns a valid result when path status is validated. Current issue: reports path blocked even if path actually does reach enemy.
    /// </summary>
    /// <returns></returns>
    public bool PathBlocked()
    {
        return pathStatusValid && pathReachesDestination == PathStatus.Blocked;
    }
    //public Vector3 nudgedTargetEnemyStructurePosition;
    /// <summary>
    /// Slight nudge
    /// </summary>
    /// <param name="entity"></param>
    public Vector3 NudgeTargetEnemyStructureDestination(Entity entity)
    {
        Vector3 nudgedTargetEnemyStructurePosition = entity.transform.position;
        float step = 0.1f;
        Vector3 newPosition = Vector3.MoveTowards(nudgedTargetEnemyStructurePosition, transform.position, step);
        nudgedTargetEnemyStructurePosition = newPosition;
        //Debug.DrawRay(entity.transform.position, Vector3.up, Color.red, 5);
        Debug.DrawRay(nudgedTargetEnemyStructurePosition, Vector3.up, Color.green, 5);
        //Debug.Log("Nudged to " + nudgedTargetEnemyStructurePosition);
        return nudgedTargetEnemyStructurePosition;
    }
    public bool PathReaches()
    {
        return pathStatusValid && pathReachesDestination == PathStatus.Reaches;
    }

    /// <summary>
    /// Only set destination if there's a significant difference
    /// </summary>
    /// <param name="target"></param>
    public void SetDestinationIfHighDiff(Vector3 target, float threshold = 0.1f)
    {
        Vector3 offset = target - destination;
        if (Vector3.SqrMagnitude(offset) > threshold * threshold)
        {
            //Debug.Log("Setting destination bc diff");
            SetDestination(target);
        }
    }
    /// <summary>
    /// Tells server this minion's destination so it can pathfind there on other clients
    /// </summary>
    /// <param name="position"></param>
    public void SetDestination(Vector3 position)
    {
        //print("setting destination");
        destination = position; //tell server where we're going
        //Debug.Log("Setting destination to " + destination);
        UpdateSetterTargetPosition(); //move pathfinding target
        pathStatusValid = false;
    }
    /// <summary>
    /// Update pathfinding target to match actual destination
    /// </summary>
    private void UpdateSetterTargetPosition()
    {
        if (pathfindingTarget != null) pathfindingTarget.position = destination;
    }
    /// <summary>
    /// Use to send unit to the specified location.
    /// </summary>
    /// <param name="target"></param>
    public void MoveTo(Vector3 target)
    {
        //Debug.Log("Moving to " + target);
        sm.lastCommand.Value = CommandTypes.Move;
        if (sm.currentState != EntityStates.Spawn)
        {
            if (ent.IsAttacker()) ent.attacker.ResetGoal();
            BasicWalkTo(target);
        }
    }
    private void BasicWalkTo(Vector3 target)
    {
        sm.ClearTargets();
        SetOrderedDestination(target);

        SwitchState(EntityStates.Walk, true); //will clear obstacle and idleness


        /*SelectableEntity justLeftGarrison = null;
        if (ent.occupiedGarrison != null) //we are currently garrisoned
        {
            justLeftGarrison = ent.occupiedGarrison;
            RemovePassengerFrom(ent.occupiedGarrison);
            PlaceOnGround(); //snap to ground
        }*/
    }
    public void MoveToTarget(Entity target)
    {
        if (target == null) return;
        //Debug.Log("Moving to target");
        sm.lastCommand.Value = CommandTypes.Move;
        if (sm.currentState != EntityStates.Spawn)
        {
            if (ent.IsAttacker()) ent.attacker.ResetGoal();
            sm.ClearTargets();
            ClearIdleness();
            ent.interactionTarget = target;
            SetOrderedDestination(ent.interactionTarget.transform.position);
            SwitchState(EntityStates.WalkToTarget, true);

            Entity justLeftGarrison = null;
            if (ent.occupiedGarrison != null) //we are currently garrisoned
            {
                justLeftGarrison = ent.occupiedGarrison;
                sm.RemovePassengerFrom(ent.occupiedGarrison);
                sm.PlaceOnGround(); //snap to ground
            }
        }
    }
    public void UpdateIdleCount()
    {
        if (sqrDistChange < walkAnimThreshold && effectivelyIdleInstances < idleThreshold)
        {
            effectivelyIdleInstances += Time.deltaTime;
        }
        else if (sqrDistChange >= walkAnimThreshold)
        {
            effectivelyIdleInstances = 0;
        }
    }
    /// <summary>
    /// Timer that prevents unit from becoming idle while walking temporarily.
    /// </summary>
    public float walkStartTimer = 0;
    public readonly float walkStartTimerSet = 1.5f; 
    private AIDestinationSetter setter;
    
    public void DetectIfShouldReturnToIdle()
    {
        Vector3 offset = destination - transform.position;
        float sqrLen = offset.sqrMagnitude;
        float closeDist = 0.1f;
        bool close = false;

        // square the distance we compare with
        if (sqrLen < Mathf.Pow(closeDist, 2))
        {
            close = true;
        }

        if (IsEffectivelyIdle(idleThreshold))
        {
            //Debug.Log("Effectively Idle");
            SwitchState(EntityStates.Idle);
        }
        else if (close && ai.reachedDestination)
        {
            //Debug.Log("Idle bc reached dest");
            SwitchState(EntityStates.Idle);
        }
    }
    public bool IsEffectivelyIdle(float forXSeconds)
    {
        return effectivelyIdleInstances > forXSeconds;
    }
    private float moveTimer = 0;
    private readonly float moveTimerMax = .01f;
    public Vector3 oldPosition;
    public void GetActualPositionChange()
    {
        //float dist = Vector3.Distance(transform.position, oldPosition);
        //oldPosition = transform.position;
        moveTimer += Time.deltaTime;
        if (moveTimer >= moveTimerMax)
        {
            moveTimer = 0;
            Vector3 offset = transform.position - oldPosition;
            float sqrLen = offset.sqrMagnitude;
            sqrDistChange = sqrLen;
            oldPosition = transform.position;
            //Debug.Log(change);
        }
    }
    public float sqrDistChange;
    readonly public float changeThreshold = 0.001f;
    float defaultEndReachedDistance = 0.1f;
    public void UpdateStopDistance()    
    {
        float limit = changeThreshold;
        float reduceEndReachedDistanceScale = 0.1f;
        if (sqrDistChange < limit && walkStartTimer <= 0)
        {
            ai.endReachedDistance += Time.deltaTime;
        }
        if (sqrDistChange >= limit)
        {
            ai.endReachedDistance = Mathf.Clamp(ai.endReachedDistance -= Time.deltaTime * reduceEndReachedDistanceScale, 
                defaultEndReachedDistance, 50);
        }
    }
    public void UpdateMinionTimers()
    {
        if (walkStartTimer > 0)
        {
            walkStartTimer -= Time.deltaTime;
        }
    }
    public void SetWalkStartTimer()
    {
        walkStartTimer = walkStartTimerSet;
    }


    /// <summary>
    /// Set whether the unit's pathfinding should take obstacles into account or not.
    /// </summary>
    /// <param name="blocked"></param>
    public void ChangeBlockedByMinionObstacleStatus(bool blocked)
    {
        //Debug.Log("setting blocked to " + blocked);
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
    public void SetOrderedDestination(Vector3 target)
    {
        SetDestination(target);
        //destination.Value = target; //set destination
        orderedDestination = target; //remember where we set destination 
    }
    public void ClearIdleness()
    {
        effectivelyIdleInstances = 0; //we're not idle anymore
    }
    public readonly float walkAnimThreshold = 0.0001f;
    public float effectivelyIdleInstances = 0;
    private float idleThreshold = .5f; //seconds of being stuck
    private void OnDrawGizmos()
    {
        /*if (effectivelyIdleInstances / idleThreshold >= 1)
        {
            Gizmos.color = Color.green;
        }
        else
        {
            Gizmos.color = Color.red;
        }
        Gizmos.DrawWireSphere(transform.position, effectivelyIdleInstances / idleThreshold);*/
        /*switch (sm.GetState())
        {
            case EntityStates.Idle:
                Gizmos.color = Color.green;
                break;
            case EntityStates.PushableIdle:
                Gizmos.color = Color.red;
                break;
        }*/
        //Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}
