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
using NUnit.Framework.Internal.Commands;

public class Attacker : EntityAddon
{
    [SerializeField] private AttackSettings attackSettings;
    [HideInInspector] public sbyte damage = 1;
    [HideInInspector] public float duration = 1;
    [HideInInspector] public float impactTime = .5f;
    [HideInInspector] public float areaOfEffectRadius = 1; //ignore if not selfdestructer
    [HideInInspector] public AttackType attackType = AttackType.Instant;
    [HideInInspector] public float range = 1;
    [HideInInspector] public Projectile attackProjectile;

    [HideInInspector] public float defaultAttackDuration = 0;
    [HideInInspector] public float defaultImpactTime = 0;
    [SerializeField] private Transform attackEffectSpawnPosition;

    public float readyTimer = 0;
    public bool ready = true;
    [HideInInspector] public bool attackMoving = false;

    private SelectableEntity targetEnemy;
    public SelectableEntity alternateAttackTarget;
    private enum Goal { None, AttackFromIdle, OrderedToAttackMove }
    private Goal longTermGoal = Goal.None;
    private Vector3 lastIdlePosition;
    public EntitySearcher assignedEntitySearcher;
    private bool attackOver = false;

    enum RequiredEnemyType { Any, Minion, Structure }
    public void InitAttacker()
    {  
        attackType = GetAttackSettings().attackType; 
        range = GetAttackSettings().attackRange;
        damage = GetAttackSettings().damage;
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
    /// <summary>
    /// Updates attack readiness during the time between impact and the attack duration.
    /// </summary>
    public void UpdateReadiness()
    { 
        if (!ready)
        {
            if (readyTimer < Mathf.Clamp(duration - impactTime, 0, 999))
            {
                readyTimer += Time.deltaTime;
            }
            else
            {
                ready = true;
                readyTimer = 0; 
            }
        }
    }
    private void RemoveFromEntitySearcher()
    {
        if (assignedEntitySearcher != null)
        {
            assignedEntitySearcher.UnassignUnit(this);
        }
    }
    public async void AttackingState()
    {
        //If attack moving a structure, check if there's a path to an enemy. If there is, attack move again
        if (sm.lastOrderType == ActionType.AttackMove && targetEnemy != null && targetEnemy.IsStructure())
        {
            sm.MakeAsyncSearchAvailableAgain();
            sm.ValidatePathStatus();
            if (!sm.hasCalledEnemySearchAsyncTask)
            {
                sm.hasCalledEnemySearchAsyncTask = true;
                await sm.AsyncFindAlternateMinionInSearchArray(range); //sets target enemy 
                if (sm.currentState != EntityStates.Attacking) return;
                if (alternateAttackTarget != null)
                {
                    sm.SetDestinationIfHighDiff(alternateAttackTarget.transform.position);
                    if (sm.PathReaches())
                    {
                        targetEnemy = alternateAttackTarget;
                        SwitchState(EntityStates.AttackMoving);
                    }
                }
                //await Task.Delay(100); //right now this limits the ability of units to acquire new targets
                if (alternateAttackTarget == null) sm.hasCalledEnemySearchAsyncTask = false; //if we couldn't find anything, try again
            }
        }
        //it is very possible for the state to not be equal to attacking by this point because of our task.delay usage

        if (sm.currentState != EntityStates.Attacking) return; 
        if (sm.InRangeOfEntity(targetEnemy, range))
        {
            //UpdateAttackIndicator(); 
            //if (IsOwner) SetDestination(transform.position);//destination.Value = transform.position; //stop in place
            //rotationSpeed = ai.rotationSpeed / 60;
            sm.LookAtTarget(targetEnemy.transform);

            if (ready) // && CheckFacingTowards(targetEnemy.transform.position
            {
                ent.unitAnimator.Play(ATTACK); 
                //Debug.Log("Anim progress" + animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
                if (ent.unitAnimator.AnimInProgress()) // && !attackOver
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
                                DamageSpecifiedEnemy(targetEnemy, damage);
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
                            /*case AttackType.Gatling:
                                DamageSpecifiedEnemy(targetEnemy, damage);
                                break;*/
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
            else if (!ent.unitAnimator.InState(ATTACK))
            {
                ent.unitAnimator.Play(IDLE);
            }
        }
        else //walk to enemy if out of range
        {
            SwitchState(EntityStates.WalkToSpecificEnemy);
        }
    }

    bool hasSelfDestructed = false;
    private void SelfDestructInExplosion(float explodeRadius)
    {
        if (!hasSelfDestructed)
        {
            Debug.Log("self destructing");
            hasSelfDestructed = true;
            Global.Instance.localPlayer.CreateExplosionAtPoint(transform.position, explodeRadius, damage);
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
        ent.unitAnimator.Play(IDLE); 
        if (!IsValidTarget(targetEnemy)) //target enemy is not valid because it is dead or missing
        {
            HandleLackOfValidTargetEnemy();
        }
        else //if target enemy is alive
        {
            SwitchState(EntityStates.Attacking);
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
            || (IsPlayerControlled() && !target.isVisibleInFog)
            //|| (!canAttackStructures && target.IsStructure())
            )
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
    private bool IsPlayerControlled()
    {
        return !ent.aiControlled;
    }
    bool playedAttackMoveSound = false;
    public void AttackMovingState()
    { 
        //on entering this state, hasCalledEnemySearchAsyncTask = false;
        #region Aesthetics
        if (!ent.unitAnimator.InState(BEGIN_ATTACK_WALK) 
            && !ent.unitAnimator.InState(CONTINUE_ATTACK_WALK))
        {
            if (!playedAttackMoveSound) //play sound and anim
            {
                playedAttackMoveSound = true;
                ent.SimplePlaySound(2);
            }
            ent.unitAnimator.Play(BEGIN_ATTACK_WALK);
        }
        #endregion
        #region Timers
        MakeAsyncSearchAvailableAgain();
        ValidatePathStatus();
        if (currentState != EntityStates.AttackMoving) return;
        #endregion
        #region Mechanics
        //target enemy is provided by enterstate finding an enemy asynchronously
        //reminder: assigned entity searcher updates enemy lists; which are then searched by asnycFindClosestEnemyToAttackMoveTowards
        if (IsValidTarget(targetEnemy))
        {
            hasCalledEnemySearchAsyncTask = false; //allows new async search 
            SetTargetEnemyAsDestination();
            //setting destination needs to be called once (or at least not constantly to the same position)

            if (IsMelee())
            {
                SelectableEntity enemy = null;
                //check if we have path to enemy 
                //this should be done regardless of if we have a valid path since it won't matter
                if (targetEnemy.IsMinion())
                {
                    enemy = FindEnemyThroughPhysSearch(attackRange, RequiredEnemyType.Minion, false);
                    if (enemy != null)
                    {
                        targetEnemy = enemy;
                        SwitchState(EntityStates.Attacking);
                    }
                }
                else
                {
                    enemy = FindSpecificEnemyInSearchListInRange(attackRange, targetEnemy);
                    if (enemy != null)
                    {
                        targetEnemy = enemy;
                        SwitchState(EntityStates.Attacking);
                    }
                }
                if (PathBlocked()) //no path to enemy, attack structures in our way
                {
                    //periodically perform mini physics searches around us and if we get anything attack it 
                    enemy = FindEnemyThroughPhysSearch(attackRange, RequiredEnemyType.Structure, true);
                    if (enemy != null)
                    {
                        targetEnemy = enemy;
                        SwitchState(EntityStates.Attacking);
                    }
                }
            }
            else//is ranged
            {
                //set our destination to be the target enemy 
                if (InRangeOfEntity(targetEnemy, attackRange)) //if enemy is in our attack range, attack them
                {
                    SwitchState(EntityStates.Attacking);
                }
            }
        }
        else //enemy is not valid target
        {
            if (PathBlocked() && IsMelee()) //if we cannot reach the target destination, we should attack structures on our way
            {
                SelectableEntity enemy = null;
                enemy = FindEnemyThroughPhysSearch(attackRange, RequiredEnemyType.Structure, false);
                if (enemy != null)
                {
                    targetEnemy = enemy;
                    SwitchState(EntityStates.Attacking);
                }
            }
            if (!hasCalledEnemySearchAsyncTask) //searcher could sort results into minions and structures 
            {  //if there is at least 1 minion we can just search through the minions and ignore structures 
                hasCalledEnemySearchAsyncTask = true;
                //Debug.Log("Entity searching");
                await AsyncSetTargetEnemyToClosestInSearchList(attackRange); //sets target enemy 
                if (currentState != EntityStates.AttackMoving) return;
                SetTargetEnemyAsDestination();
                if (targetEnemy == null)
                {
                    hasCalledEnemySearchAsyncTask = false; //if we couldn't find anything, try again 
                    SetDestinationIfHighDiff(attackMoveDestination);
                }
            }
        }
        //currrently, this will prioritize minions. however, if a wall is in the way, then the unit will just walk into the wall
        #endregion
    }
    private void HandleLackOfValidTargetEnemy()
    {
        switch (longTermGoal)
        {
            case Goal.None:
                break;
            case Goal.AttackFromIdle:
                MoveTo(lastIdlePosition);
                ResetGoal();
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
    private SelectableEntity FindEnemyThroughPhysSearch(float range, RequiredEnemyType requiredEnemyType, bool mustBeInSearchList)
    {
        Collider[] enemyArray = new Collider[Global.Instance.attackMoveDestinationEnemyArrayBufferSize];
        int searchedCount = 0;
        if (ent.controllerOfThis.allegianceTeamID == 0) //our units should search enemy layer
        {
            searchedCount = Physics.OverlapSphereNonAlloc(transform.position, range, enemyArray, Global.Instance.enemyLayer);
        }
        else //enemy units should search friendly layer
        {
            searchedCount = Physics.OverlapSphereNonAlloc(transform.position, range, enemyArray, Global.Instance.friendlyEntityLayer);
        }
        SelectableEntity select = null;
        for (int i = 0; i < searchedCount; i++) //place valid entities into array
        {
            if (enemyArray[i] == null) continue; //if invalid do not increment slotToWriteTo 
            select = enemyArray[i].GetComponent<SelectableEntity>();
            if (select == null) continue;
            if (!select.alive || !select.isTargetable.Value) //overwrite these slots
            {
                continue;
            }
            bool matchesRequiredType = false;
            switch (requiredEnemyType)
            {
                case RequiredEnemyType.Any:
                    return select;
                case RequiredEnemyType.Minion:
                    matchesRequiredType = select.IsMinion();
                    break;
                case RequiredEnemyType.Structure:
                    matchesRequiredType = select.IsStructure();
                    break;
                default:
                    break;
            }
            if (matchesRequiredType)
            {
                if (mustBeInSearchList)
                {
                    if (assignedEntitySearcher != null)
                    {
                        bool inList = false;
                        if (select.IsMinion())
                        {
                            inList = assignedEntitySearcher.searchedMinions.Contains(select);
                        }
                        else
                        {
                            inList = assignedEntitySearcher.searchedStructures.Contains(select);
                        }
                        if (inList)
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
            select = null; //reaching the end of the loop without breaking resets select
        }
        return select;
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
            if (IsEnemy(check) && check.alive && check.isTargetable.Value) //only check on enemies that are alive, targetable, visible
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
            if (IsEnemy(check) && check.alive && check.isTargetable.Value && check == enemy) //only check on enemies that are alive, targetable, visible
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

    /// <summary>
    /// Cancel an in-progress async search (searching through list of enemies for a target enemy).
    /// </summary>
    private void CancelAsyncSearch()
    {
        asyncSearchCancellationToken?.Cancel();
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
            if (longTermGoal == Goal.AttackFromIdle && Vector3.Distance(lastIdlePosition, targetEnemy.transform.position) > maximumChaseRange)
            {
                HandleLackOfValidTargetEnemy();
                break;
            }
            //UpdateAttackIndicator();
            if (!InRangeOfEntity(targetEnemy, attackRange))
            {
                if (ent.occupiedGarrison != null)
                {
                    SwitchState(EntityStates.Idle);
                    break;
                }
                animator.Play("AttackWalk");

                //if target is a structure, move the destination closer to us until it no longer hits obstacle
                SetTargetEnemyAsDestination();
            }
            else
            {
                SwitchState(EntityStates.Attacking);
                break;
            }
        }
        else
        {
            HandleLackOfValidTargetEnemy();
            /*if (lastOrderType == ActionType.AttackTarget) //if we were last attacking a specific target, start a new attack move on that
            { //target's last position
                GenericAttackMovePrep(targetEnemyLastPosition);
            }
            //also we need to create new entity searcher at that position
            //we should be able to get all the other entities attacking that position and lump them into an entity searcher
            SwitchState(MinionStates.AttackMoving); 
            //AutomaticAttackMove();*/
        }
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

    /// <summary>
    /// Sets target enemy to the closest enemy
    /// </summary>
    /// <param name="range"></param>
    /// <returns></returns>
    private async Task AsyncSetTargetEnemyToClosestInSearchList(float range) //called only once
    {
        asyncSearchCancellationToken = new CancellationTokenSource();
        if (assignedEntitySearcher == null) return;

        //Debug.Log("Running find closest attack target search");
        if (ent.IsMelee())
        {
            range = defaultMeleeDetectionRange;
        }
        else
        {
            range += rangedUnitRangeExtension;
        }

        SelectableEntity valid = null;


        SelectableEntity[] searchArray = new SelectableEntity[Global.Instance.attackMoveDestinationEnemyArrayBufferSize];
        int searchCount = 0;
        if (assignedEntitySearcher.minionCount > 0) //if there are minions, only search those
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
        for (int i = 0; i < searchCount; i++)
        {
            SelectableEntity check = searchArray[i];
            if (IsEnemy(check) && check.alive && check.isTargetable.Value) //only check on enemies that are alive, targetable, visible
            {
                //viability range is 4 unless attack range is higher
                //viability range is how far targets can be from the attack move destination and still be a valid target
                float viabilityRange = minAttackMoveDestinationViabilityRange;
                if (attackRange > minAttackMoveDestinationViabilityRange) viabilityRange = attackRange;
                if (ent.aiControlled || Vector3.Distance(check.transform.position, attackMoveDestination) <= viabilityRange)
                //AI doesn't care about attack move range viability; otherwise must be in range of the attack move destination
                //later add failsafe for if there's nobody in that range
                {
                    if (canAttackStructures || check.IsMinion())
                    {
                        if (InRangeOfEntity(check, range)) //is enemy in range and visible?
                        {
                            valid = check;
                            //Debug.DrawRay(valid.transform.position, Vector3.up, Color.red, 1);
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
                        NudgeTargetEnemyStructureDestination(targetEnemy);
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
        //if (targetEnemy != null) Debug.Log("found target to attack move towards: " + targetEnemy.name); 
    }

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
            if (IsEnemy(check) && check.alive && check.isTargetable.Value && check.IsMinion())
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
    /// Use to find a minion to attack when this is attacking a structure
    /// </summary>
    /// <param name="range"></param>
    /// <param name="shouldExtendAttackRange"></param> 

    private async Task AsyncFindAlternateLowerHealthMinionAttackTarget(float range)
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
    }
}
