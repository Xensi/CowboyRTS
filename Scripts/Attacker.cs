using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using static StateMachineController;
using static UnitAnimator;

public class Attacker : SwingEntityAddon
{
    [SerializeField] private AttackSettings attackSettings;
    [HideInInspector] public float areaOfEffectRadius = 1; //ignore if not selfdestructer
    [HideInInspector] public AttackType attackType = AttackType.Instant;
    [HideInInspector] public Projectile attackProjectile;

    [HideInInspector] public float defaultAttackDuration = 0;
    [HideInInspector] public float defaultImpactTime = 0;
    [SerializeField] private Transform attackEffectSpawnPosition; 
    [HideInInspector] public bool attackMoving = false;

    /// <summary>
    /// The target we're currently trying to attack.
    /// </summary>
    public Entity targetEnemy;
    /// <summary>
    /// The target we were trying to attack previously, who we switched off of because we couldn't reach it.
    /// </summary>
    public Entity preferredAttackTarget;
    public Entity alternateAttackTarget;
    public enum Goal { None, AttackFromIdle, OrderedToAttackMove, OrderedToAttackSpecificTarget }
    public Goal longTermGoal = Goal.None; 
    public Vector3 lastIdlePosition;
    public EntitySearcher assignedEntitySearcher;
    private bool attackOver = false;

    public enum RequiredEnemyType { Any, Minion, Structure, MinionPreferred }
    public bool hasCalledEnemySearchAsyncTask = false;
    public Vector3 attackMoveDestination;
    public float sqrDistToTargetEnemy = Mathf.Infinity;
    private float sqrDistToAlternateTarget = Mathf.Infinity;
    private readonly float minAttackMoveDestinationViabilityRange = 4;
    bool hasSelfDestructed = false;


    public int attackMoveDestinationEnemyCount = 0;
    private readonly float defaultMeleeDetectionRange = 2;
    private readonly float rangedUnitRangeExtension = 2;

    CancellationTokenSource asyncSearchCancellationToken;
    private bool asyncSearchTimerActive = false;
    bool playedAttackMoveSound = false;

    public float maximumChaseRange = 5;
    private float searchTimerDuration = 0.1f;
    CancellationTokenSource hasCalledEnemySearchAsyncTaskTimerCancellationToken;
    Vector3 targetEnemyLastPosition;
    public override void InitAddon()
    {  
        attackType = GetAttackSettings().attackType; 
        range = GetAttackSettings().range;
        swingDelta = GetAttackSettings().damage;
        duration = GetAttackSettings().attackDuration;
        impactTime = GetAttackSettings().impactTime;
        areaOfEffectRadius = GetAttackSettings().areaOfEffectRadius; 
        attackProjectile = GetAttackSettings().attackProjectilePrefab;
        defaultAttackDuration = duration;
        defaultImpactTime = impactTime;
    } 
    public AttackSettings GetAttackSettings()
    {
        return attackSettings;
    }
    public void RemoveFromEntitySearcher()
    {
        if (assignedEntitySearcher != null)
        {
            assignedEntitySearcher.UnassignUnit(sm);
        }
    }
    #region States
    public void OnEnterState()
    {
        asyncSearchTimerActive = false;
        pf.pathfindingValidationTimerActive = false;
        hasCalledEnemySearchAsyncTask = false;
        alternateAttackTarget = null;
    }
    public void IdleState()
    {
        lastIdlePosition = transform.position;
        Entity found = IdleDetectEnemies();

        if (found != null)
        {
            targetEnemy = found;
            longTermGoal = Goal.AttackFromIdle;
            SetTargetEnemyAsDestination();
            SwitchState(EntityStates.WalkToSpecificEnemy);
            //Debug.Log("trying to target enemy idle");
        }
    }
    public void WalkToSpecificEnemyState()
    {
        /*if (IsEffectivelyIdle(.1f) && IsMelee()) //!pathReachesDestination && 
        //if we can't reach our specific target, find a new one
        {
            AutomaticAttackMove();
        }*/
        if (IsValidTarget(targetEnemy))
        {
            if (longTermGoal == Goal.AttackFromIdle && !InChaseRange(targetEnemy))
            {
                //Debug.Log("Outside chase range"); 
                HandleLackOfValidTargetEnemy();
                return;
            }
            //UpdateAttackIndicator();
            pf.ValidatePathStatus();
            pf.PushNearbyOwnedIdlers();
            if (ent.IsMelee()) // melee troops should detect when blocked by walls and attack them
            {
                Entity enemy = null;
                if (pf.PathBlocked() && pf.IsEffectivelyIdle(.1f)) //no path to enemy, attack structures in our way
                {
                    //periodically perform mini physics searches around us and if we get anything attack it 
                    enemy = GetFirstEnemyHashSearch(range, RequiredEnemyType.Structure);
                    //enemy = FindEnemyThroughPhysSearch(range, RequiredEnemyType.Structure, false); 
                    if (enemy != null)
                    {
                        //Debug.Log("Found new enemy while blocked");
                        targetEnemy = enemy;
                        SwitchState(EntityStates.Attacking);
                    }
                }
            }
            if (!InRangeOfEntity(targetEnemy, range))
            {
                /*if (ent.occupiedGarrison != null)
                {
                    SwitchState(EntityStates.Idle); 
                    return;
                }*/
                //anim.Play(CONTINUE_ATTACK_WALK);

                //if target is a structure, move the destination closer to us until it no longer hits obstacle
                SetTargetEnemyAsDestination();
            }
            else
            {
                SwitchState(EntityStates.Attacking);
                return;
            }
        }
        else
        {
            HandleLackOfValidTargetEnemy();
        }
    }

    public async void AttackMovingState()
    {
        //NOTE: On entering this state, hasCalledEnemySearchAsyncTask becomes false.
        AttackMovingAesthetics();
        #region Timers
        MakeAsyncSearchAvailableAgain();
        pf.ValidatePathStatus();
        pf.PushNearbyOwnedIdlers();
        if (!sm.InState(EntityStates.AttackMoving)) return;
        #endregion
        #region Mechanics
        //target enemy is provided by enterstate finding an enemy asynchronously
        //reminder: assigned entity searcher updates enemy lists; which are then searched by asyncFindClosestEnemyToAttackMoveTowards
        if (IsValidTarget(targetEnemy))
        {
            SetTargetEnemyAsDestination();
            //setting destination needs to be called once (or at least not constantly to the same position)

            if (ent.IsMelee())
            {
                Entity enemy = null;
                if (targetEnemy.IsMinion()) //target the first minion that enters our range
                {
                    enemy = GetFirstEnemyHashSearch(range, RequiredEnemyType.Minion);
                    //enemy = FindEnemyThroughPhysSearch(range, RequiredEnemyType.Minion, false);
                    if (enemy != null)
                    {
                        targetEnemy = enemy;
                        SwitchState(EntityStates.Attacking);
                    }
                }
                else //if it's a structure
                {
                    enemy = FindSpecificEnemyInSearchListInRange(range, targetEnemy);
                    if (enemy != null)
                    {
                        targetEnemy = enemy;
                        SwitchState(EntityStates.Attacking);
                    }
                }
            }
            else //is ranged
            {
                //set our destination to be the target enemy 
                if (ent.InRangeOfEntity(targetEnemy, range)) //if enemy is in our attack range, attack them
                {
                    SwitchState(EntityStates.Attacking);
                }
            }

            //Periodically recalculate which enemy in search is closest
            if (!hasCalledEnemySearchAsyncTask)
            {
                hasCalledEnemySearchAsyncTask = true;
                await AsyncSetTargetEnemyToClosestInSearchList(range); //sets target enemy 
                if (!sm.InState(EntityStates.AttackMoving)) return;
            }
        }
        else //enemy is not valid target
        {
            //Search for an enemy to target in attack move zone
            if (!hasCalledEnemySearchAsyncTask) //searcher could sort results into minions and structures 
            {  //if there is at least 1 minion we can just search through the minions and ignore structures 
                hasCalledEnemySearchAsyncTask = true;
                await AsyncSetTargetEnemyToClosestInSearchList(range); //sets target enemy 
                if (!sm.InState(EntityStates.AttackMoving)) return;
                if (targetEnemy == null)
                {
                    //Debug.Log("Did not find an enemy");
                    pf.SetDestinationIfHighDiff(attackMoveDestination); //can't find any enemies, so let's just go to center of a-move
                }
                else
                {
                    SetTargetEnemyAsDestination();
                }
            }
        }

        if (pf.PathBlocked()) //if we cannot reach the target destination, we should attack structures on our way
        {
            Entity enemy = null;
            if (pf.IsEffectivelyIdle(.1f)) //blocked for a very short time; melee can attack nearby enemies  && ent.IsMelee()
            {
                enemy = GetFirstEnemyHashSearch(range, RequiredEnemyType.MinionPreferred);
                //enemy = FindEnemyThroughPhysSearch(range, RequiredEnemyType.MinionPreferred, false);
            }
            if (enemy != null)
            {
                preferredAttackTarget = targetEnemy;
                targetEnemy = enemy;
                SwitchState(EntityStates.Attacking);
            }
        }
        #endregion
    }
    public void AttackingState()
    {
        if (!IsValidTarget(targetEnemy))
        {
            HandleLackOfValidTargetEnemy();
            return;
        }
        MakeAsyncSearchAvailableAgain();
        pf.ValidatePathStatus();

        if (!IsValidTarget(preferredAttackTarget) && longTermGoal == Goal.OrderedToAttackSpecificTarget)
        {
            if (targetEnemy.IsStructure()) //we can find alternate targets
            {
                Entity found = AttackingDetectEnemies();
                if (found != null) //only switch if we have a path to the new target
                {
                    pf.SetDestinationIfHighDiff(found.transform.position); //important not to constantly set destination;
                    preferredAttackTarget = found;
                }
            }
        }

        if (sm.InRangeOfEntity(targetEnemy, range))
        {
            ContinueAttack();
        }
        else if (!sm.InRangeOfEntity(targetEnemy, range)) //walk to enemy if out of range
        {
            if (longTermGoal == Goal.OrderedToAttackMove)
            {
                SwitchState(EntityStates.AttackMoving);
            }
            else
            {
                SwitchState(EntityStates.WalkToSpecificEnemy);
            }
        }

    }

    private void ContinueAttack()
    {

        //UpdateAttackIndicator(); 
        //if (IsOwner) SetDestination(transform.position);//destination.Value = transform.position; //stop in place
        //rotationSpeed = ai.rotationSpeed / 60;
        sm.LookAtTarget(targetEnemy.transform);

        if (ready) // && CheckFacingTowards(targetEnemy.transform.position
        {
            ent.anim.Play(ATTACK);
            //Debug.Log("Anim progress" + animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
            if (ent.anim.InProgress()) // && !attackOver
            { //is attackOver necessary?
                /*if (timerUntilAttackTrailBegins < attackTrailBeginTime)
                {
                    timerUntilAttackTrailBegins += Time.deltaTime;
                }
                else if (!attackTrailTriggered)
                {
                    attackTrailTriggered = true;
                    timerUntilAttackTrailBegins = 0;
                    ChangeAttackTrailState(true);
                }*/
                if (sm.stateTimer < impactTime)
                {
                    sm.stateTimer += Time.deltaTime;
                }
                else if (ready)
                {
                    ready = false;
                    sm.stateTimer = 0;
                    //attackOver = true;
                    AttackImpact();
                    //Debug.Log("impact");
                }
            }
            else //animation finished
            {
                //Debug.Log("Attack Complete");
                AfterAttackCheck();
            }
        }
        else if (!ent.anim.InState(ATTACK))
        {
            ent.anim.Play(IDLE);
        }
    }
    private void AttackImpact()
    {
        switch (attackType)
        {
            case AttackType.Instant:
                DamageSpecifiedEnemy(targetEnemy, swingDelta);
                break;
            case AttackType.SelfDestruct:
                SelfDestructInExplosion(areaOfEffectRadius);
                break;
            case AttackType.Projectile:
                Vector3 positionToShoot = targetEnemy.transform.position + new Vector3(0, 0.5f, 0);
                if (targetEnemy.physicalCollider != null) //get closest point on collider; //this has an issue
                {
                    Vector3 centerToMax = targetEnemy.physicalCollider.bounds.center - targetEnemy.physicalCollider.bounds.max;
                    float boundsFakeRadius = centerToMax.magnitude;
                    float discrepancyThreshold = boundsFakeRadius + .5f;
                    Vector3 closest = targetEnemy.physicalCollider.ClosestPoint(transform.position);
                    float rawDist = Vector3.Distance(transform.position, targetEnemy.transform.position);
                    float closestDist = Vector3.Distance(transform.position, closest);
                    if (Mathf.Abs(rawDist - closestDist) <= discrepancyThreshold)
                    {
                        positionToShoot = closest + new Vector3(0, 0.5f, 0);
                    }
                }
                ShootProjectileAtPosition(positionToShoot);
                break;
            case AttackType.None:
                break;
            default:
                break;
        }
    }
    #endregion

    #region Explosions
    private void SelfDestructInExplosion(float explodeRadius)
    {
        if (!hasSelfDestructed)
        {
            Debug.Log("self destructing");
            hasSelfDestructed = true;
            Global.instance.localPlayer.CreateExplosionAtPoint(transform.position, explodeRadius, swingDelta);
            SimpleExplosionEffect(transform.position);
            Global.instance.localPlayer.DamageEntity(99, ent); //it is a self destruct, after all
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
        GameObject prefab = Global.instance.explosionPrefab;
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

    #endregion
    /// <summary>
    /// Assumes destination is set to this target.
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    private bool CouldAttackTarget(Entity target)
    {
        if (target != null) return pf.PathReaches() || sm.InRangeOfEntity(target, range);
        else return false;
    }
    private void AfterAttackCheck()
    {
        ent.anim.Play(IDLE); 
        if (!IsValidTarget(targetEnemy)) //target enemy is not valid because it is dead or missing
        {
            HandleLackOfValidTargetEnemy();
        }
        else //if target enemy is alive
        {
            //if path is clear and we were previously trying to attack a different target
            if (CouldAttackTarget(preferredAttackTarget)) //!pf.PathBlocked()
            {
                targetEnemy = preferredAttackTarget;
                preferredAttackTarget = null;
                switch (longTermGoal)
                {
                    case Goal.OrderedToAttackMove:
                        SwitchState(EntityStates.AttackMoving);
                        break;
                    case Goal.OrderedToAttackSpecificTarget:
                        SwitchState(EntityStates.WalkToSpecificEnemy);
                        break;
                }
            }
            else
            {
                SwitchState(EntityStates.Attacking);
            }
        }
    }
    /// <summary>
    /// Is target nonnull, alive, targetable, etc?
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    public bool IsValidTarget(Entity target)
    {

        if (target == null || !target.isAttackable || !target.alive || !target.isTargetable.Value
            || (IsPlayerControlled() && !target.IsVisibleInFog()) 
            || !target.IsEnemyOfTarget(ent) || target.currentHP.Value <= 0)
        {
            if (targetEnemy == target) targetEnemy = null;
            return false;
        }
        else
        {
            return true;
        }
    }
    private bool IsPlayerControlled()
    {
        return !ent.aiControlled;
    }
    private Entity IdleDetectEnemies()
    {
        float physSearchRange = range;
        if (ent.IsMelee())
        {
            physSearchRange = Global.instance.defaultMeleeSearchRange;
        }
        Entity eligibleIdleEnemy = GetFirstEnemyHashSearch(physSearchRange, RequiredEnemyType.Minion);
        //Entity eligibleIdleEnemy = FindEnemyThroughPhysSearch(physSearchRange, RequiredEnemyType.Minion, false, true);
        return eligibleIdleEnemy;
    }
    private Entity AttackingDetectEnemies()
    {
        float physSearchRange = range;
        if (ent.IsMelee())
        {
            physSearchRange = Global.instance.defaultMeleeSearchRange;
        }
        Entity eligibleIdleEnemy = GetClosestMinionHashSearch(physSearchRange);
        return eligibleIdleEnemy;
    }


    private void GenericAttackMovePrep(Vector3 target)
    {
        attackMoveDestination = target;
        sm.lastCommand.Value = CommandTypes.Attack;
        sm.ClearTargets();
        pf.ClearIdleness();
        SwitchState(EntityStates.AttackMoving, true);
        playedAttackMoveSound = false;
        pf.SetDestination(target);
        pf.orderedDestination = pf.destination;
    }
    public void AttackMoveToPosition(Vector3 target) //called by local player
    {
        if (!ent.alive) return; //dead units cannot be ordered
                                //if (IsGarrisoned()) return;
        longTermGoal = Goal.OrderedToAttackMove;
        //Debug.Log(longTermGoal);
        GenericAttackMovePrep(target);
    }
    public void ResetGoal() //tell the unit to stop attacking from idle; other use cases: stop attack moving
    {
        longTermGoal = Goal.None;
        lastIdlePosition = transform.position;
        //Debug.Log("Resetting goal");
    }
    /// <summary>
    /// Run animations and sounds based off anim state.
    /// </summary>
    private void AttackMovingAesthetics()
    {
        if (!ent.anim.InState(BEGIN_ATTACK_WALK)
            && !ent.anim.InState(CONTINUE_ATTACK_WALK))
        {
            if (!playedAttackMoveSound) //play sound and anim
            {
                playedAttackMoveSound = true;
                ent.SimplePlaySound(2);
            }
            ent.anim.Play(BEGIN_ATTACK_WALK);
        }
    }
    public void SetTargetEnemyAsDestination()
    {
        if (targetEnemy == null || pf == null) return;
        if (targetEnemy.IsStructure()) //if target is a structure, first move the destination closer to us until it no longer hits obstacle
        {
            pf.NudgeTargetEnemyStructureDestination(targetEnemy);
            pf.SetDestinationIfHighDiff(pf.nudgedTargetEnemyStructurePosition);
        }
        else
        {
            pf.SetDestinationIfHighDiff(targetEnemy.transform.position);
        }
    }
    private void HandleLackOfValidTargetEnemy()
    {
        targetEnemy = null;
        //Debug.Log(longTermGoal);
        switch (longTermGoal)
        {
            case Goal.None:
                SwitchState(EntityStates.Idle);
                break;
            case Goal.AttackFromIdle:
                {
                    //check if there's more enemies within our idle attack range
                    Entity found = IdleDetectEnemies();
                    if (found != null && InChaseRange(found))
                    {
                        targetEnemy = found;
                        SwitchState(EntityStates.WalkToSpecificEnemy);
                    }
                    else
                    {
                        pf.MoveTo(lastIdlePosition);
                        ResetGoal();
                    }
                }
                break;
            case Goal.OrderedToAttackMove:
                SwitchState(EntityStates.AttackMoving);
                break;
            case Goal.OrderedToAttackSpecificTarget:
                {
                    Entity found = AttackingDetectEnemies();
                    if (found != null)
                    {
                        //Debug.Log(found);
                        targetEnemy = found;
                        SwitchState(EntityStates.WalkToSpecificEnemy);
                    }
                }
                break;
            default:
                break;
        }
    }

    public void DamageSpecifiedEnemy(Entity enemy, sbyte damage) //since hp is a network variable, changing it on the server will propagate changes to clients as well
    {
        if (enemy != null)
        { //fire locally
            ent.SimplePlaySound(1);
            if (ent.attackEffects.Length > 0) //show muzzle flash
            {
                ent.DisplayAttackEffects();
            }
            if (!ent.IsMelee()) //shoot trail
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
            Global.instance.localPlayer.DamageEntity(damage, enemy);
            //DamageUmbrella(damage, enemy);
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
        TrailController tra = Instantiate(Global.instance.gunTrailGlobal, start, Quaternion.identity);
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
    private void SpawnProjectile(Vector3 spawnPos, Vector3 destination)
    {
        if (attackProjectile != null)
        {
            Projectile proj = Instantiate(attackProjectile, spawnPos, Quaternion.identity);
            proj.groundTarget = destination;
            proj.entityToHomeOnto = targetEnemy;
            proj.isLocal = IsOwner;
            proj.firingUnitAttackRange = range;
        }
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
    private Entity GetFirstEnemyHashSearch(float range, RequiredEnemyType requiredEnemyType)
    {
        return Global.instance.spatialHash.GetFirstEnemyHashSearch(ent, range, requiredEnemyType);
        //return Global.instance.spatialHash.GetClosestEnemyHashSearch(ent, range, requiredEnemyType);
    }
    private Entity GetClosestMinionHashSearch(float range)
    {
        return Global.instance.spatialHash.GetClosestMinionHashSearch(ent, range);
    }
    private Entity FindSpecificEnemyInSearchListInRange(float range, Entity enemy)
    {
        if (assignedEntitySearcher == null) return null;
        Entity[] searchArray = new Entity[Global.instance.attackMoveDestinationEnemyArrayBufferSize];
        int searchCount = 0;
        RequiredEnemyType enemyType = RequiredEnemyType.Any;

        if (enemy.IsMinion())
        {
            enemyType = RequiredEnemyType.Minion;
        }
        else
        {
            enemyType = RequiredEnemyType.Structure;
        }

        switch (enemyType)
        {
            case RequiredEnemyType.Any:
                searchArray = assignedEntitySearcher.searchedAll;
                searchCount = assignedEntitySearcher.allCount;
                break;
            case RequiredEnemyType.Minion:
                searchArray = assignedEntitySearcher.searchedMinions;
                searchCount = assignedEntitySearcher.minionCount;
                break;
            case RequiredEnemyType.Structure:
                searchArray = assignedEntitySearcher.searchedStructures;
                searchCount = assignedEntitySearcher.structureCount;
                break;
            default:
                break;
        }
        for (int i = 0; i < searchCount; i++)
        {
            Entity check = searchArray[i];
            if (ent.IsEnemyOfTarget(check) && check.alive && check.isTargetable.Value && check == enemy) //only check on enemies that are alive, targetable, visible
            {
                Vector3 offset = check.transform.position - transform.position;
                if (offset.sqrMagnitude < range * range) //return first enemy that's in range
                {
                    return check;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Cancel an in-progress async search (searching through list of enemies for a target enemy).
    /// </summary>
    private void CancelAsyncSearch()
    {
        asyncSearchCancellationToken?.Cancel();
    }
    private bool InChaseRange(Entity target)
    {
        return Vector3.Distance(lastIdlePosition, target.transform.position) <= maximumChaseRange;
    }
    /// <summary>
    /// When this timer elapses, a new async search through the enemy list will become available.
    /// hasCalledEnemySearchAsyncTask == true means that we have started running a search through the enemy list already
    /// </summary>
    public async void MakeAsyncSearchAvailableAgain() //problem: the task is called over and over. the task needs to be called once.
    {
        if (hasCalledEnemySearchAsyncTask && asyncSearchTimerActive == false)
        {
            asyncSearchTimerActive = true;
            hasCalledEnemySearchAsyncTaskTimerCancellationToken = new CancellationTokenSource();
            try //exception may happen here
            {
                //100 ms originally
                await Task.Delay(TimeSpan.FromSeconds(searchTimerDuration), hasCalledEnemySearchAsyncTaskTimerCancellationToken.Token);
            }
            catch //caught exception
            {
                //Debug.Log("Timer1 was cancelled!");
                return;
            }
            finally //always runs when control leaves "try"
            {
                hasCalledEnemySearchAsyncTaskTimerCancellationToken?.Dispose();
                hasCalledEnemySearchAsyncTaskTimerCancellationToken = null;
                hasCalledEnemySearchAsyncTask = false;
                asyncSearchTimerActive = false;
            }
        }
    }
    public Entity GetTargetEnemy()
    {
        return targetEnemy;
    }
    public void AttackTarget(Entity select)
    {
        longTermGoal = Goal.OrderedToAttackSpecificTarget;
        preferredAttackTarget = select;
        targetEnemy = select;
        SwitchState(EntityStates.WalkToSpecificEnemy, true);
    }
    public void OnSwitchState()
    { 
        alternateAttackTarget = null;
    }
    /// <summary>
    /// Sets target enemy to the closest enemy in search list
    /// </summary>
    /// <param name="attackMoveDetectRange"></param>
    /// <returns></returns>
    private async Task AsyncSetTargetEnemyToClosestInSearchList(float attackMoveDetectRange) //called only once
    {
        asyncSearchCancellationToken = new CancellationTokenSource();
        if (assignedEntitySearcher == null) return;

        Entity valid = null;
        Entity[] searchArray = new Entity[Global.instance.attackMoveDestinationEnemyArrayBufferSize];
        int searchCount = 0;
        if (assignedEntitySearcher.MinionsInSearch()) //if there are minions, only search those
        {
            searchArray = assignedEntitySearcher.searchedMinions;
            searchCount = assignedEntitySearcher.minionCount;
        }
        else //allow searching structures
        {
            searchArray = assignedEntitySearcher.searchedStructures;
            searchCount = assignedEntitySearcher.structureCount;
        }
        //Debug.Log(searchCount);
        for (int i = 0; i < searchCount; i++) //run for each search result
        {
            Entity checkedEnt = searchArray[i];
            if (ent.IsEnemyOfTarget(checkedEnt) && checkedEnt.alive && checkedEnt.isTargetable.Value) //only check on enemies that are alive, targetable, visible
            {
                //float viabilityRange = minAttackMoveDestinationViabilityRange;
                //if (attackMoveDetectRange > minAttackMoveDestinationViabilityRange) viabilityRange = attackMoveDetectRange;
                
                //disallow targets that are too far from the attack move destination
                if (ent.aiControlled 
                    || Vector3.Distance(checkedEnt.transform.position, attackMoveDestination) <= assignedEntitySearcher.SearchRadius())
                {//AI doesn't care about attack move range viability; otherwise must be in range of the attack move destination
                 //later add failsafe for if there's nobody in that range
                    valid = checkedEnt;
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
            if (valid != null) //valid is a possibility, not definite
            {
                Vector3 offset = valid.transform.position - transform.position;
                float validDist = offset.sqrMagnitude;
                //get sqr magnitude between this and valid 
                //if our current target is a structure, jump to minion regardless of distance. targetEnemy.IsStructure() && valid.IsMinion() ||
                //if our current target is a minion, only jump to other minions if lower distance; targetEnemy.IsMinion() && valid.IsMinion() && ; targetEnemy.IsStructure() && valid.IsStructure() && validDist < sqrDistToTargetEnemy 
                //if our current destination is unreachable and we're melee, jump to something closer; !pathReachesTarget && IsMelee() && validDist < sqrDistToTargetEnemy
                if (targetEnemy == null || validDist < sqrDistToTargetEnemy)
                {
                    sqrDistToTargetEnemy = validDist;
                    targetEnemy = valid;
                    if (targetEnemy.IsStructure())
                    {
                        pf.NudgeTargetEnemyStructureDestination(targetEnemy);
                    }
                }
            }
            try
            {
                await Task.Yield();
            }
            catch
            {
                Debug.Log("Async alt search was cancelled!");
            }
            finally
            {
                asyncSearchCancellationToken?.Dispose();
                asyncSearchCancellationToken = null;
            }
        }
    }
    /// <summary>
    /// While attacking, find a closer target in our search array we could attack. 
    /// </summary>
    /// <param name="range"></param>
    /// <returns></returns>
    public async Task AsyncFindAlternateMinionInSearchArray(float range)
    {
        asyncSearchCancellationToken = new CancellationTokenSource();
        if (assignedEntitySearcher == null) return;
        //Debug.Log("Running alternate minion attack target search");
        if (ent.IsMelee())
        {
            range = defaultMeleeDetectionRange;
        }
        else
        {
            range += rangedUnitRangeExtension;
        }

        Entity[] searchArray = new Entity[Global.instance.attackMoveDestinationEnemyArrayBufferSize];
        int searchCount = 0;
        if (assignedEntitySearcher.minionCount > 0) //if there are minions, only search those
        {
            searchArray = assignedEntitySearcher.searchedMinions;
            searchCount = assignedEntitySearcher.minionCount;
        }

        Entity valid = null;
        for (int i = 0; i < searchCount; i++)
        {
            Entity check = searchArray[i];
            if (ent.IsEnemyOfTarget(check) && check.alive && check.isTargetable.Value && check.IsMinion())
            //only check on enemies that are alive, targetable, visible, and in range, and are minions
            {
                if (InRangeOfEntity(check, range))
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
                    //Debug.Log("Found alternate attack target" + valid.name);
                }
            }
            try
            {
                await Task.Yield();
            }
            catch
            {
                Debug.Log("Async alt search was cancelled!");
            }
            finally
            {
                asyncSearchCancellationToken?.Dispose();
                asyncSearchCancellationToken = null;
            }
        }
    }
    /// <summary>
    /// Update Attacker's target enemy last position.
    /// </summary>
    public void UpdateTargetEnemyLastPosition()
    {
        if (ent.IsAttacker())
        {
            if (ent.attacker.GetTargetEnemy() != null)
            {
                targetEnemyLastPosition = targetEnemy.transform.position;
            }
            else
            {
                targetEnemyLastPosition = transform.position;
            }
        }
    }
}
