using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Unity.Netcode;
using UnityEngine;
using static Player;
using static StateMachineController;
using static UnitAnimator;
using System.Threading.Tasks;

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
    public SelectableEntity targetEnemy;
    /// <summary>
    /// The target we were trying to attack previously, who we switched off of because we couldn't reach it.
    /// </summary>
    public SelectableEntity preferredAttackTarget;
    public SelectableEntity alternateAttackTarget;
    public enum Goal { None, AttackFromIdle, OrderedToAttackMove }
    public Goal longTermGoal = Goal.None; 
    public Vector3 lastIdlePosition;
    public EntitySearcher assignedEntitySearcher;
    private bool attackOver = false;

    enum RequiredEnemyType { Any, Minion, Structure, MinionPreferred }
    public bool hasCalledEnemySearchAsyncTask = false;
    public Vector3 attackMoveDestination;
    public float sqrDistToTargetEnemy = Mathf.Infinity;
    private float sqrDistToAlternateTarget = Mathf.Infinity;
    private readonly float minAttackMoveDestinationViabilityRange = 4;
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
    public async void AttackingState()
    {
        if (!IsValidTarget(targetEnemy))
        {
            HandleLackOfValidTargetEnemy();
            return;
        }
        MakeAsyncSearchAvailableAgain();
        pf.ValidatePathStatus();
        if (!sm.InState(EntityStates.Attacking)) return;
        await Task.Yield();

        if (!sm.InState(EntityStates.Attacking)) return;

        if (sm.InRangeOfEntity(targetEnemy, range))
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

    bool hasSelfDestructed = false;
    private void SelfDestructInExplosion(float explodeRadius)
    {
        if (!hasSelfDestructed)
        {
            Debug.Log("self destructing");
            hasSelfDestructed = true;
            Global.Instance.localPlayer.CreateExplosionAtPoint(transform.position, explodeRadius, swingDelta);
            SimpleExplosionEffect(transform.position);
            Global.Instance.localPlayer.DamageEntity(99, ent); //it is a self destruct, after all
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
            if (longTermGoal == Goal.OrderedToAttackMove && preferredAttackTarget != null && !pf.PathBlocked())
            {
                targetEnemy = preferredAttackTarget;
                preferredAttackTarget = null;
                SwitchState(EntityStates.AttackMoving);
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
    public bool IsValidTarget(SelectableEntity target)
    {
        if (target == null || !target.isAttackable || !target.alive || !target.isTargetable.Value
            || (IsPlayerControlled() && !target.isVisibleInFog || !target.IsEnemyOfTarget(ent)) || target.currentHP.Value <= 0
            )
        //reject if target is null, or target is dead, or target is untargetable, or this unit is player controlled and target is hidden,
        //or this unit can't attack structures and target is structure
        {
            //if enemy is not valid, remove them
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
    bool playedAttackMoveSound = false; 
    public void IdleState()
    {
        lastIdlePosition = transform.position;
        SelectableEntity found = IdleDetectEnemies();

        if (found != null)
        {
            targetEnemy = found;
            longTermGoal = Goal.AttackFromIdle;
            SwitchState(EntityStates.WalkToSpecificEnemy);
            //Debug.Log("trying to target enemy idle");
        }
    }
    private SelectableEntity IdleDetectEnemies()
    {
        float physSearchRange = range;
        if (ent.IsMelee())
        {
            physSearchRange = Global.Instance.defaultMeleeSearchRange;
        }
        SelectableEntity eligibleIdleEnemy = FindEnemyThroughPhysSearch(physSearchRange, RequiredEnemyType.Minion, false, true);
        return eligibleIdleEnemy;
    }

    private void GenericAttackMovePrep(Vector3 target)
    {
        attackMoveDestination = target;
        sm.lastCommand.Value = CommandTypes.Attack;
        sm.ClearTargets();
        pf.ClearIdleness();
        SwitchState(EntityStates.AttackMoving);
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
    public async void AttackMovingState()
    {
        //NOTE: On entering this state, hasCalledEnemySearchAsyncTask becomes false.
        AttackMovingAesthetics();
        #region Timers
        MakeAsyncSearchAvailableAgain();
        pf.ValidatePathStatus();
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
                SelectableEntity enemy = null;
                if (targetEnemy.IsMinion()) //target the first minion that enters our range
                {
                    enemy = FindEnemyThroughPhysSearch(range, RequiredEnemyType.Minion, false);
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
            SelectableEntity enemy = null;
            
            //if we can't get where we're trying to go, then we can fall back on attacking the structure we're stuck on
            /*if (pf.IsEffectivelyIdle(1)) //blocked for long time, attack anything
            {
                enemy = FindEnemyThroughPhysSearch(range, RequiredEnemyType.MinionPreferred, false);
            }
            else if (pf.IsEffectivelyIdle(0.1f)) //blocked for a short time; find enemies in search list
            {
                enemy = FindEnemyThroughPhysSearch(range, RequiredEnemyType.MinionPreferred, true);
            }
            else */
            if (pf.IsEffectivelyIdle(.1f)) //blocked for a very short time; melee can attack nearby enemies  && ent.IsMelee()
            {
                enemy = FindEnemyThroughPhysSearch(range, RequiredEnemyType.MinionPreferred, false);
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
    public void SetTargetEnemyAsDestination()
    {
        if (targetEnemy == null) return;
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
    private bool asyncSearchTimerActive = false;
    public void OnEnterState()
    { 
        asyncSearchTimerActive = false;
        pf.pathfindingValidationTimerActive = false;
        hasCalledEnemySearchAsyncTask = false;
        alternateAttackTarget = null;
    }
    private void HandleLackOfValidTargetEnemy()
    {
        targetEnemy = null;
        switch (longTermGoal)
        {
            case Goal.None:
                break;
            case Goal.AttackFromIdle:
                //check if there's more enemies within our idle attack range
                SelectableEntity found = IdleDetectEnemies();
                if (found != null && InChaseRange(found))
                {
                    targetEnemy = found;
                    longTermGoal = Goal.AttackFromIdle;
                    SwitchState(EntityStates.WalkToSpecificEnemy);
                    //Debug.Log("New target");
                }
                else
                {
                    pf.MoveTo(lastIdlePosition);
                    ResetGoal();
                }
                break;
            case Goal.OrderedToAttackMove:
                SwitchState(EntityStates.AttackMoving);
                break;
            default:
                break;
        }
    }

    public void DamageSpecifiedEnemy(SelectableEntity enemy, sbyte damage) //since hp is a network variable, changing it on the server will propagate changes to clients as well
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
            Global.Instance.localPlayer.DamageEntity(damage, enemy);
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
    /// <summary>
    /// Grab first enemy nearby through physics search.
    /// </summary>
    /// <param name="range"></param>
    /// <param name="requiredEnemyType"></param>
    /// <param name="mustBeInSearchList"></param>
    /// <returns></returns>
    private SelectableEntity FindEnemyThroughPhysSearch(float range, RequiredEnemyType requiredEnemyType, bool mustBeInSearchList,
        bool mustBeInChaseRange = false)
    {
        Collider[] enemyArray = new Collider[Global.Instance.attackMoveDestinationEnemyArrayBufferSize];
        int searchedCount = 0;
        if (ent.GetAllegiance() == 0) //our units should search enemy layer
        {
            searchedCount = Physics.OverlapSphereNonAlloc(transform.position, range, enemyArray, Global.Instance.enemyLayer);
        }
        else //enemy units should search friendly layer
        {
            searchedCount = Physics.OverlapSphereNonAlloc(transform.position, range, enemyArray, Global.Instance.friendlyEntityLayer);
        }
        SelectableEntity enemy = null;
        SelectableEntity valid = null;
        SelectableEntity backup = null;
        for (int i = 0; i < searchedCount; i++) //place valid entities into array
        {
            if (enemyArray[i] == null) continue; //if invalid do not increment slotToWriteTo 
            enemy = enemyArray[i].GetComponent<SelectableEntity>();
            if (!IsValidTarget(enemy)) continue;
            if (mustBeInChaseRange && !InChaseRange(enemy)) continue;
            bool inList = !mustBeInSearchList; //if must be in search list, this bool is false; otherwise it's true
            if (mustBeInSearchList && assignedEntitySearcher != null)
            {
                if (enemy.IsMinion())
                {
                    inList = Array.Exists(assignedEntitySearcher.searchedMinions, element => element == enemy);
                }
                else
                {
                    inList = Array.Exists(assignedEntitySearcher.searchedStructures, element => element == enemy);
                }
            }
            if (!inList) continue; //can only progress if in list
            bool matchesRequiredType = false;
            switch (requiredEnemyType)
            {
                case RequiredEnemyType.Any:
                    matchesRequiredType = true;
                    valid = enemy;
                    break;
                case RequiredEnemyType.Minion:
                    matchesRequiredType = enemy.IsMinion();
                    if (matchesRequiredType)
                    {
                        valid = enemy;
                    }
                    break;
                case RequiredEnemyType.Structure:
                    matchesRequiredType = enemy.IsStructure();
                    if (matchesRequiredType)
                    {
                        valid = enemy;
                    }
                    break;
                case RequiredEnemyType.MinionPreferred:
                    matchesRequiredType = enemy.IsMinion();
                    if (matchesRequiredType) //if it's a structure, we'll continue, but leave this open as an option if we can't find any minions
                    {
                        valid = enemy;
                    }
                    else
                    {
                        backup = enemy;
                    }
                    break;
                default:
                    break;
            }
            if (!matchesRequiredType) //go to next item if doesn't match
            {
                continue;
            }
            else //if it matches, stop looking
            {
                break;
            }
        }
        if (backup != null && valid == null) valid = backup;
        if (ent.GetAllegiance() == 0)
        {   
            if (valid != null)
            {
                //Debug.Log(name + " returning valid" + valid.name);
            }
            if (backup != null)
            {

                //Debug.Log(name + " returning backup" + backup.name);
            }
        }
        return valid;
    }
    private SelectableEntity FindEnemyInSearchListInRange(float range, RequiredEnemyType enemyType)
    {
        if (assignedEntitySearcher == null) return null;
        SelectableEntity[] searchArray = new SelectableEntity[Global.Instance.attackMoveDestinationEnemyArrayBufferSize];
        int searchCount = 0;
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
            SelectableEntity check = searchArray[i];
            if (ent.IsEnemyOfTarget(check) && check.alive && check.isTargetable.Value) //only check on enemies that are alive, targetable, visible
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
    private SelectableEntity FindSpecificEnemyInSearchListInRange(float range, SelectableEntity enemy)
    {
        if (assignedEntitySearcher == null) return null;
        SelectableEntity[] searchArray = new SelectableEntity[Global.Instance.attackMoveDestinationEnemyArrayBufferSize];
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
            SelectableEntity check = searchArray[i];
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

    CancellationTokenSource asyncSearchCancellationToken;

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
            if (ent.IsEnemyOfTarget(check) && check.alive && check.isTargetable.Value && check.IsMinion())
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
    public float maximumChaseRange = 5;
    /// <summary>
    /// Cancel an in-progress async search (searching through list of enemies for a target enemy).
    /// </summary>
    private void CancelAsyncSearch()
    {
        asyncSearchCancellationToken?.Cancel();
    }
    private bool InChaseRange(SelectableEntity target)
    {
        return Vector3.Distance(lastIdlePosition, target.transform.position) <= maximumChaseRange;
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
                Debug.Log("Outside chase range"); 
                HandleLackOfValidTargetEnemy();
                return;
            }
            //UpdateAttackIndicator();
            pf.ValidatePathStatus();
            if (ent.IsMelee()) // melee troops should detect when blocked by walls and attack them
            {
                SelectableEntity enemy = null;
                if (pf.PathBlocked() && pf.IsEffectivelyIdle(.1f)) //no path to enemy, attack structures in our way
                {
                    //periodically perform mini physics searches around us and if we get anything attack it 
                    enemy = FindEnemyThroughPhysSearch(range, RequiredEnemyType.Structure, false); 
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

    private float searchTimerDuration = 0.1f;
    CancellationTokenSource hasCalledEnemySearchAsyncTaskTimerCancellationToken;
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
    public SelectableEntity GetTargetEnemy()
    {
        return targetEnemy;
    }
    public void AttackTarget(SelectableEntity select)
    {
        targetEnemy = select;
        SwitchState(EntityStates.WalkToSpecificEnemy);
    }
    public void OnSwitchState()
    { 
        alternateAttackTarget = null;
    }
    public int attackMoveDestinationEnemyCount = 0;
    private readonly float defaultMeleeDetectionRange = 2;
    private readonly float rangedUnitRangeExtension = 2;

    /// <summary>
    /// Sets target enemy to the closest enemy in search list
    /// </summary>
    /// <param name="attackMoveDetectRange"></param>
    /// <returns></returns>
    private async Task AsyncSetTargetEnemyToClosestInSearchList(float attackMoveDetectRange) //called only once
    {
        asyncSearchCancellationToken = new CancellationTokenSource();
        if (assignedEntitySearcher == null) return;

        SelectableEntity valid = null;
        SelectableEntity[] searchArray = new SelectableEntity[Global.Instance.attackMoveDestinationEnemyArrayBufferSize];
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
            SelectableEntity checkedEnt = searchArray[i];
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
                    Debug.DrawRay(valid.transform.position, Vector3.up, Color.green, 1);
                    //  Debug.Log("Square distance to: " + targetEnemy.name + " is " + sqrDistToTargetEnemy);
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

        SelectableEntity[] searchArray = new SelectableEntity[Global.Instance.attackMoveDestinationEnemyArrayBufferSize];
        int searchCount = 0;
        if (assignedEntitySearcher.minionCount > 0) //if there are minions, only search those
        {
            searchArray = assignedEntitySearcher.searchedMinions;
            searchCount = assignedEntitySearcher.minionCount;
        }

        SelectableEntity valid = null;
        for (int i = 0; i < searchCount; i++)
        {
            SelectableEntity check = searchArray[i];
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

    Vector3 targetEnemyLastPosition;
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
    /// <summary>
    /// Use to find a minion to attack when this is attacking a structure
    /// </summary>
    /// <param name="range"></param>
    /// <param name="shouldExtendAttackRange"></param> 

    /*private async Task AsyncFindAlternateLowerHealthMinionAttackTarget(float range)
    {
        SelectableEntity valid = null;
        for (int i = 0; i < attackMoveDestinationEnemyCount; i++)
        {
            SelectableEntity check = attackMoveDestinationEnemyArray[i];
            if (IsEnemy(check) && check.alive && check.isTargetable.Value && check.IsMinion()) //only check on enemies that are alive, targetable, visible, and in range
            {
                if (InRangeOfEntity(check, range))
                {
                    valid = check;
                }
            }
            if (valid != null)
            {
                if (alternateAttackTarget == null || valid.currentHP.Value < alternateAttackTarget.currentHP.Value)
                {
                    alternateAttackTarget = valid;
                }
            }
            await Task.Yield();
        }
    }*/
}
