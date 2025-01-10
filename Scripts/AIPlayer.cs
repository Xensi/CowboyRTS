using FoW;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;
//using static UnityEditor.Progress;
using Pathfinding.Drawing;
using Unity.VisualScripting;
using static Player;
using System.Threading.Tasks;
/// <summary>
/// AI that is governed by the server
/// </summary>
public class AIPlayer : Player
{
    //to give a unit to the AI, set its desired team to a negative number 
    private readonly float minimumDecisionTime = 2;
    private float decisionTime = 1; 
    private readonly float maximumDecisionTime = 30;
    private float actionTime = 3;
    [SerializeField] private float attackTime = 5;
    public float timer = 0;
    private float attackTimer = 0;
    public Transform spawnPosition;
    public List<SelectableEntity> knownEnemyStructures = new();
    public List<SelectableEntity> knownEnemyUnits = new();
    public List<SelectableEntity> watchedEntities = new();
    public enum AIBehavior
    {
        Default, 
        Passive, //do nothing; minions will attack enemies that enter their range
        HuntDownMinions, //send attacks towards visible enemies, no scouting
        SwitchToHDMWhenWatchedEntityDestroyed, //passive until a watched entity is destroyed
    }
    public AIBehavior behavior = AIBehavior.Default;
    public override void OnNetworkSpawn()
    {
        if (!enable) return;
        if (IsOwner) //spawn initial minions/buildings  
        {
            //if (spawnPosition != null) SpawnMinion(spawnPosition.position, playerFaction.spawnableEntities[0]);
        } 
    }
    public override void Start()
    {
        if (!enable) return;
        base.Start();
        Global.Instance.aiPlayers[Mathf.Abs(playerTeamID) - 1] = this;
    }

    /// <summary>
    /// Time it takes the AI to make a decision is based on the number of units it is controlling.
    /// </summary>
    private void UpdateDecisionTimeBasedOnUnitCount()
    {
        decisionTime = Mathf.Clamp(Mathf.Pow(ownedMinions.Count, 2) * 1/50, minimumDecisionTime, maximumDecisionTime);
    }
    public override void Update()
    {
        if (!enable) return;
        base.Update();
        ProcessOrdersInBatches();
        if (IsOwner)
        {
            CheckIfShouldSwitchAIBehavior();
            UpdateDecisionTimeBasedOnUnitCount();
            UpdateKnownEnemyUnits();
            CleanLists();
            timer += Time.deltaTime;
            if (timer >= decisionTime)
            {
                timer = 0;
                MakeDecision();
            } 
        }
    }
    private void CheckIfShouldSwitchAIBehavior()
    {
        switch (behavior)
        {
            case AIBehavior.Default:
                break;
            case AIBehavior.Passive:
                break;
            case AIBehavior.HuntDownMinions:
                break;
            case AIBehavior.SwitchToHDMWhenWatchedEntityDestroyed:
                foreach (SelectableEntity item in watchedEntities)
                {
                    if (item != null && item.alive == false)
                    {
                        behavior = AIBehavior.HuntDownMinions;
                        break;
                    }
                }
                break;
            default:
                break;
        }
        
    }
    private void MakeDecision()
    {
        switch (behavior)
        {
            case AIBehavior.Default: 
                break;
            case AIBehavior.Passive: //do nothing
                break;
            case AIBehavior.HuntDownMinions:
                AttackMoveClosestKnownMinions();
                break;
            default:
                break;
        }
        /*if (timer >= actionTime)
        {
            timer = 0;
            switch (behavior)
            {
                case AIBehavior.Default:
                    EconAction();
                    break;
                case AIBehavior.Passive:
                    break;
                default:
                    break;
            }
        }
        attackTimer += Time.deltaTime;
        if (attackTimer >= attackTime)
        {
            attackTimer = 0; 
        }*/
    } 

    private void AttackMoveClosestKnownMinions()
    {
        //compare distance between one of our units and enemies
        StateMachineController compareUnit = null;
        if (ownedMinions.Count > 0) compareUnit = ownedMinions[0];
        if (compareUnit == null) return;
        SelectableEntity closestEnemy = null;
        float closestDist = Mathf.Infinity;
        foreach (SelectableEntity enemy in knownEnemyUnits)
        {
            if (Vector3.Distance(compareUnit.transform.position, enemy.transform.position) < closestDist)
            {
                closestEnemy = enemy;
            } 
        }
        if (closestEnemy == null) return; 

        List<StateMachineController> fighters = new();
        foreach (StateMachineController item in ownedMinions)
        {
            if (item != null && item.ent != null && item.ent.factionEntity != null)
            {
                if (item.ent.factionEntity is FactionUnit)
                {
                    FactionUnit facUnit = item.ent.factionEntity as FactionUnit;
                    if (facUnit != null && facUnit.IsFighter())
                    {
                        fighters.Add(item);
                    }
                }
            } 
        }
        AIAttackersAttackMove(fighters, closestEnemy.transform.position);
    }

    public int desiredPopAdders = 0;
    public int desiredHarvesters = 5;
    public int desiredUnitSpawner = 2;
    public int desiredFighters = 10;
    private int numberOfHarvesters = 0;
    private int numberOfSpawners = 0;
    private int numberOfFighters = 0;
    private int numberOfPopAdders = 0;
    private void EvaluateNumberOfTypes()
    {
        int harvesters = 0;
        int spawners = 0;
        int fighters = 0;
        int popadders = 0;
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item.IsHarvester())
            {
                harvesters++;
            } 
            if (item.IsSpawner())
            {
                spawners++;
            }
            if (!item.IsSpawner() && !item.IsHarvester())
            {
                fighters++;
            }
            if (item.raisePopulationLimitBy > 0)
            {
                popadders++;
            }
        }
        numberOfHarvesters = harvesters;
        numberOfSpawners = spawners;
        numberOfFighters = fighters;
        numberOfPopAdders = popadders;
    }
    /// <summary>
    /// Go through each enemy player and update which of their entities we can see.
    /// </summary>
    private void UpdateKnownEnemyUnits()
    {
        FogOfWarTeam fow = FogOfWarTeam.GetTeam(playerTeamID);
        //run through initialized players and AI team controllers (check their allegiance)
        foreach (Player player in Global.Instance.allPlayers)
        {
            if (player != null && player != this && player.allegianceTeamID != allegianceTeamID) //player exists and is enemy
            {
                for (int i = 0; i < player.ownedEntities.Count; i++)
                {
                    SelectableEntity entity = player.ownedEntities[i];
                    if (entity != null)
                    {
                        bool visible = fow.GetFogValue(entity.transform.position) < Global.Instance.minFogStrength * Global.Instance.maxFogValue;
                        if (visible)
                        {
                            if (entity.IsMinion() && !knownEnemyUnits.Contains(entity))
                            {
                                knownEnemyUnits.Add(entity);
                            }
                            else if (!entity.IsMinion() && !knownEnemyStructures.Contains(entity))
                            {
                                knownEnemyStructures.Add(entity);
                            }
                        }
                        else
                        {
                            if (entity.IsMinion() && knownEnemyUnits.Contains(entity))
                            {
                                knownEnemyUnits.Remove(entity);
                            } //we don't remove structures that have slipped into fog
                        }
                    } 
                } 
            }
        }
    }
    private void CleanLists()
    {
        if (knownEnemyUnits.Count > 0)
        { 
            for (int i = knownEnemyUnits.Count - 1; i >= 0; i--)
            {

                SelectableEntity current = knownEnemyUnits[i];
                if (current == null || !current.alive) knownEnemyUnits.RemoveAt(i); 
            }
        }
        if (knownEnemyStructures.Count > 0)
        { 
            for (int i = knownEnemyStructures.Count - 1; i >= 0; i--)
            {
                SelectableEntity current = knownEnemyStructures[i];
                if (current == null || !current.alive) knownEnemyStructures.RemoveAt(i); 
            }
        }
        /*for (int i = ownedEntities.Count - 1; i >= 0; i--)
        {
            SelectableEntity current = ownedEntities[i];
            if (current == null || !current.alive) ownedEntities.RemoveAt(i);
            await Task.Yield();
        }
        for (int i = ownedMinions.Count - 1; i >= 0; i--)
        {
            MinionController current = ownedMinions[i];
            if (current == null || !current.entity.alive) ownedMinions.RemoveAt(i);
            await Task.Yield();
        }*/
    }
    private void AIAttackersAttackMove(List<StateMachineController> fighters, Vector3 target)
    { 
        //create an entity searcher at the clicked position
        /*EntitySearcher searcher = CreateEntitySearcherAtPosition(target, 1);

        UnitOrdersQueue.Clear();

        foreach (StateMachineController fighter in fighters)
        {
            if (fighter != null && fighter.IsValidAttacker()) //minion
            {
                //if this unit is already assigned to an entity searcher, unassign it
                if (fighter.assignedEntitySearcher != null)
                {
                    fighter.assignedEntitySearcher.UnassignUnit(fighter);
                }
                //assign the entity searcher to selected units
                fighter.assignedEntitySearcher = searcher;
                //update the entity searcher's assigned units list
                fighter.assignedEntitySearcher.AssignUnit(fighter);
                fighter.hasCalledEnemySearchAsyncTask = false; //tell the minion to run a new search
                UnitOrder order = new();
                order.unit = fighter;
                order.targetPosition = target;
                order.action = ActionType.AttackMove;
                UnitOrdersQueue.Add(order);
            }
        } */
    }
    private void SendScoutingParty()
    {
        /*StateMachineController scout = null;
        foreach (StateMachineController item in ownedMinions)
        {
            if (item != null && item.ent != null && item.ent.factionEntity != null && item.currentState == StateMachineController.EntityStates.Idle)
            {
                if (item.ent.factionEntity is FactionUnit)
                {
                    FactionUnit facUnit = item.ent.factionEntity as FactionUnit;
                    if (facUnit != null && facUnit.IsFighter())
                    {
                        scout = item;
                        break;
                    }
                }
            }
        }
        if (scout != null && scout.ai != null) //only move if not busy
        { 
            int max = Global.Instance.maxMapSize;
            FogOfWarTeam fow = FogOfWarTeam.GetTeam(playerTeamID); 
            for (int i = 0; i < 100; i++)
            {
                Vector3 randomTarget = new Vector3(Random.Range(-max, max), 0, Random.Range(-max, max));
                bool visible = fow.GetFogValue(randomTarget) < Global.Instance.minFogStrength * 255;
                if (!visible)
                { 
                    currentScoutingDestination = randomTarget;
                    scout.AIAttackMove(currentScoutingDestination);
                    Debug.DrawRay(currentScoutingDestination, Vector3.up, Color.green, 2);
                    break;
                }
                else
                { 
                    Debug.DrawRay(randomTarget, Vector3.up, Color.red, 1);
                }
            } 
        }*/
    }
    Vector3 currentScoutingDestination; 
    private void EconAction()
    {
        UpdateUnbuilt();
        EvaluateNumberOfTypes();
        if (visibleResources.Count < 1)
        {
            EvaluateVisibleResources();
        }
        else
        {
            TellInactiveBuildersToBuild();
            TellInactiveMinersToHarvest();
        }
        if (population >= maxPopulation && numberOfPopAdders >= desiredPopAdders)
        {
            desiredPopAdders++;
        }
        if (numberOfPopAdders < desiredPopAdders)
        {
            TryToConstructType(BuildingDesire.PopulationAdder);
        }
        else
        { 
            if (numberOfHarvesters < desiredHarvesters)
            {
                TryToSpawnType(UnitDesire.Harvester);
            }
            if (numberOfFighters < desiredFighters)
            {
                TryToSpawnType(UnitDesire.Fighter);
            }
        }
        if (numberOfSpawners < desiredUnitSpawner)
        {
            TryToConstructType(BuildingDesire.UnitSpawner);
        }
        if (numberOfFighters >= desiredFighters && numberOfHarvesters >= desiredHarvesters && numberOfSpawners >= desiredUnitSpawner || gold >= 500)
        {
            ExpandDesires();
        }
        /*int rand = Random.Range(0, 3);
            if (rand == 0)
            {
                TryToCreateUnit();
            }
            else if (rand == 1)
            {
                TellInactiveMinersToHarvest();
            }
            else if (rand == 2)
            {
                TryToBuildStructure();
            }
            else if (rand == 3)
            {
                AttackMoveWithCombatUnits();
            }*/

    }
    private void ExpandDesires()
    {
        int rand = Random.Range(0, 100);
        if (rand <= 40)
        {
            desiredHarvesters += 5;
        }
        else if (rand <= 80)
        {
            desiredFighters += 5;
        }
        else if (rand <= 100)
        {
            desiredUnitSpawner++;
        } 
    }
    private enum BuildingDesire
    {
        UnitSpawner, PopulationAdder
    }
    private enum UnitDesire
    {
        Harvester, Fighter
    }
    private bool CanAfford(FactionEntity entity)
    {
        return entity.goldCost <= gold;
    }
    private void TryToConstructType(BuildingDesire desire)
    {  
        //pick a unit/building that can spawn units. try to queue up a unit
        SelectableEntity chosenEntity = null;
        FactionBuilding building = null;
        foreach (StateMachineController minion in ownedMinions)
        {
            if (minion == null) continue;
            SelectableEntity minionEntity = minion.ent;
            if (minionEntity == null) continue;
            if (minionEntity.IsBuilder() && !minion.IsCurrentlyBuilding())
            {
                FactionBuilding[] buildables = minionEntity.builder.GetBuildables();
                for (int i = 0; i < buildables.Length; i++) //check if this has a unit we can spawn
                {
                    FactionBuilding facBuilding = buildables[i];
                    if (CanAfford(facBuilding))
                    { 
                        switch (desire)
                        {
                            case BuildingDesire.UnitSpawner:
                                if (facBuilding.isSpawner)
                                { 
                                    chosenEntity = minionEntity;
                                    building = facBuilding;
                                }
                                break; 
                            case BuildingDesire.PopulationAdder:
                                if (facBuilding.isPopAdder)
                                {
                                    chosenEntity = minionEntity;
                                    building = facBuilding;
                                }
                                break;
                            default:
                                break;
                        }
                    } 
                }
            }
        }
        Vector3 validPosition = Vector3.zero;
        if (chosenEntity != null && building != null)
        {
            bool foundValidPosition = false;
            //pick a random point around the keystone
            for (int i = 0; i < 100; i++)
            {
                Vector2 randomCircle = Random.insideUnitCircle * 5;
                int verticalOffset = 10;
                Vector3 randCircle3D = new Vector3(randomCircle.x, verticalOffset, randomCircle.y);
                Vector3 randomPosition = ownedEntities[0].transform.position + randCircle3D;
                //ray cast down
                bool rayHit = Physics.Raycast(randomPosition, Vector3.down, out RaycastHit hit, Mathf.Infinity, Global.Instance.groundLayer);
                if (rayHit)
                {
                    Grid grid = Global.Instance.grid;
                    if (grid != null)
                    {
                        Vector3 buildOffset = building.buildOffset;
                        Vector3Int gridPos = grid.WorldToCell(hit.point);
                        Vector3 worldGridPos = grid.CellToWorld(gridPos) + buildOffset;
                        worldGridPos = new Vector3(worldGridPos.x, hit.point.y, worldGridPos.z);

                        if (IsPositionBlocked(worldGridPos))
                        {
                            Debug.DrawRay(worldGridPos, transform.up, Color.red, 10);
                        }
                        else
                        {
                            Debug.DrawRay(worldGridPos, transform.up, Color.green, 10);
                            validPosition = worldGridPos;
                            foundValidPosition = true;
                            break;
                        }
                    }
                }
            }
            if (foundValidPosition)
            {
                gold -= building.goldCost;
                SelectableEntity last = SpawnMinion(validPosition, building);
                chosenEntity.sm.ForceBuildTarget(last);
            }
        } 
    } 
    private void TryToSpawnType(UnitDesire desire)
    { 
        /*//pick a unit/building that can spawn units. try to queue up a unit
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item == null) continue;
            if (item.IsSpawner())
            {
                SelectableEntity chosenEntity = null;
                FactionUnit unit = null;
                List<FactionUnit> options = new();
                for (int i = 0; i < item.spawnableUnits.Length; i++) //check if this has a unit we can spawn
                {
                    FactionUnit entity = item.spawnableUnits[i];
                    if (entity.goldCost <= gold)
                    {
                        switch (desire)
                        {
                            case UnitDesire.Harvester:
                                if (entity.IsHarvester())
                                { 
                                    chosenEntity = item;
                                    options.Add(item.spawnableUnits[i]);
                                }
                                break;
                            case UnitDesire.Fighter: 
                                if (entity.IsFighter())
                                {
                                    chosenEntity = item;
                                    options.Add(item.spawnableUnits[i]);
                                }
                                break;
                            default:
                                break;
                        }
                    }
                }
                int rand = Random.Range(0, options.Count);
                if (options.Count > 0) unit = options[rand];
                if (chosenEntity != null && unit != null)
                {
                    FactionUnit newUnit = FactionUnit.CreateInstance(unit.prefabToSpawn.name, unit.maxSpawnTimeCost, unit.prefabToSpawn, unit.goldCost);
                    int cost = newUnit.goldCost;
                    gold -= cost;
                    chosenEntity.buildQueue.Add(newUnit);
                }
            }
        }*/
    }
     
    public List<SelectableEntity> visibleResources = new();
    private void EvaluateVisibleResources()
    {
        //Debug.Log("Evaluating visible resources");
        visibleResources.Clear();
        FogOfWarTeam fow = FogOfWarTeam.GetTeam(playerTeamID);
        //Broken for now TODO
        /*foreach (SelectableEntity item in Global.Instance.friendlyOres)
        {
            bool visibleInFog = fow.GetFogValue(item.transform.position) < Global.Instance.minFogStrength * 255;
            if (visibleInFog)
            {
                visibleResources.Add(item);
            }
        }*/
    }
    private void UpdateUnbuilt()
    {
        foreach (SelectableEntity building in unbuiltStructures) //get an unbuilt structure
        {
            if (building != null && building.IsFullyBuilt())
            {
                unbuiltStructures.Remove(building);
                break;
            }
        }
    }
    private void TellInactiveBuildersToBuild()
    { 
        foreach (SelectableEntity building in unbuiltStructures) //get an unbuilt structure
        {
            if (building != null && building.IsNotYetBuilt())
            {
                foreach (SelectableEntity builder in ownedEntities) //get a builder
                {
                    if (builder.sm != null && builder.IsBuilder() && !builder.sm.IsCurrentlyBuilding()) //minion
                    {
                        builder.sm.CommandBuildTarget(building);
                        break;
                    }
                }
            } 
        }
    }
    private void TellInactiveMinersToHarvest()
    {
        //Debug.Log("Telling miners to harvest");
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item.sm != null && item.IsHarvester() && !item.sm.IsCurrentlyBuilding()) //minion
            {
                switch (item.sm.currentState)
                {
                    case StateMachineController.EntityStates.Idle:
                    case StateMachineController.EntityStates.FindInteractable:
                        if (visibleResources.Count > 0)
                        {
                            int rand = Random.Range(0, visibleResources.Count);
                            item.sm.CommandHarvestTarget(visibleResources[rand]); 
                        }
                        break;
                }
            }
        }
    }
    private void SetRallyPoint()
    {

    }  
    /// <summary>
    /// The server will spawn in a minion at a position.
    /// </summary> 
    public SelectableEntity SpawnMinion(Vector3 spawnPosition, FactionEntity unit)
    {
        if (playerFaction == null)
        {
            Debug.LogError("Missing faction");
            return null;
        }
        if (IsServer)
        {
            if (unit != null && unit.prefabToSpawn != null)
            {
                GameObject minion = Instantiate(unit.prefabToSpawn.gameObject, spawnPosition, Quaternion.identity); //spawn the minion
                SelectableEntity select = null;
                if (minion != null)
                {
                    select = minion.GetComponent<SelectableEntity>(); //get select
                }
                if (select != null)
                {
                    select.desiredTeamNumber = (sbyte)playerTeamID; //necessary for it to spawn as AI controlled
                    if (select.net == null) select.net = select.GetComponent<NetworkObject>();
                    select.net.Spawn();
                }
                return select;
            }
        }
        return null;
    }
}
