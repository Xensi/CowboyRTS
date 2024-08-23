using FoW;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;
//using static UnityEditor.Progress;
using Pathfinding.Drawing;
using Unity.VisualScripting;
/// <summary>
/// AI that is governed by the server
/// </summary>
public class AIPlayer : Player
{
    //to give a unit to the AI, set its desired team to a negative number 
    private float actionTime = 3;
    private float attackTime = 5;
    private float timer = 0;
    private float attackTimer = 0;
    public Transform spawnPosition;
    public List<SelectableEntity> knownEnemyStructures = new();
    public List<SelectableEntity> knownEnemyUnits = new();
    public bool enable = false;
    public override void OnNetworkSpawn()
    {
        if (!enable) return;
        if (IsOwner) //spawn initial minions/buildings  
        {
            if (spawnPosition != null) SpawnMinion(spawnPosition.position, playerFaction.spawnableEntities[0]);
        } 
    }
    public override void Start()
    {
        if (!enable) return;
        base.Start();
        Global.Instance.aiTeamControllers.Add(this);
        Global.Instance.allPlayers.Add(this);
        allegianceTeamID = playerTeamID; //by default
    }
    void Update()
    {
        if (!enable) return;
        if (IsOwner)
        { 
            timer += Time.deltaTime;
            if (timer >= actionTime)
            {
                timer = 0;
                PerformAction();
            }
            attackTimer += Time.deltaTime;
            if (attackTimer >= attackTime)
            {
                attackTimer = 0;
                AggressiveAction();
            }
        } 
    }
    private void AggressiveAction()
    { 
        CheckIfCanSeeEnemy();
        SendFightersToKnownPositions();
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
            if (item.CanHarvest())
            {
                harvesters++;
            } 
            if (item.CanProduceUnits())
            {
                spawners++;
            }
            if (!item.CanProduceUnits() && !item.CanHarvest())
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
    private void CheckIfCanSeeEnemy()
    {
        FogOfWarTeam fow = FogOfWarTeam.GetTeam(playerTeamID);
        //run through initialized players and AI team controllers (check their allegiance)
        foreach (Player player in Global.Instance.allPlayers)
        {
            if (player != null && player != this && player.allegianceTeamID != allegianceTeamID) //player exists and is enemy
            {
                foreach (SelectableEntity entity in player.ownedEntities)
                {
                    if (entity != null)
                    { 
                        bool visible = fow.GetFogValue(entity.transform.position) < Global.Instance.minFogStrength * 255;
                        if (visible)
                        {
                            if (entity.IsUnit() && !knownEnemyUnits.Contains(entity))
                            {
                                knownEnemyUnits.Add(entity);
                            }
                            else if (!entity.IsUnit() && !knownEnemyStructures.Contains(entity))
                            {
                                knownEnemyStructures.Add(entity);
                            }
                        }
                        else
                        {
                            if (entity.IsUnit() && knownEnemyUnits.Contains(entity))
                            {
                                knownEnemyUnits.Remove(entity);
                            }
                            //we don't remove structures that have slipped into fog
                        }
                    }
                }
            }
        }
    }
    private void CleanLists()
    {
        for (int i = knownEnemyUnits.Count - 1; i >= 0; i--)
        {
            SelectableEntity current = knownEnemyUnits[i];
            if (current == null || !current.alive) knownEnemyUnits.RemoveAt(i);
        } 
        for (int i = knownEnemyStructures.Count - 1; i >= 0; i--)
        {
            SelectableEntity current = knownEnemyStructures[i];
            if (current == null || !current.alive) knownEnemyStructures.RemoveAt(i);
        }
        for (int i = ownedEntities.Count - 1; i >= 0; i--)
        {
            SelectableEntity current = ownedEntities[i];
            if (current == null || !current.alive) ownedEntities.RemoveAt(i);
        }
        for (int i = ownedMinions.Count - 1; i >= 0; i--)
        {
            MinionController current = ownedMinions[i];
            if (current == null || !current.entity.alive) ownedMinions.RemoveAt(i);
        }
    }
    private void SendFightersToKnownPositions()
    {
        //pick a known position
        Vector3 pos = Vector3.zero;
        bool hasPosition = false;
        if (knownEnemyUnits.Count > 0)
        {
            pos = knownEnemyUnits[0].transform.position;
            hasPosition = true;
        }
        else if (knownEnemyStructures.Count > 0)
        {
            pos = knownEnemyStructures[0].transform.position;
            hasPosition = true;
        }

        if (hasPosition)
        { 
            List<MinionController> fighters = new();
            foreach (MinionController item in ownedMinions)
            {
                if (item != null && item.entity != null && item.entity.factionEntity != null)
                { 
                    if (item.entity.factionEntity is FactionUnit)
                    { 
                        FactionUnit facUnit = item.entity.factionEntity as FactionUnit;
                        if (facUnit != null && facUnit.IsFighter())
                        {
                            fighters.Add(item);
                        }
                    }
                }
            }
            foreach (MinionController item in fighters)
            {
                item.AIAttackMove(pos);
            }
        }
        else
        {
            SendScoutingParty();
        }
    }
    private void SendScoutingParty()
    {
        MinionController scout = null;
        foreach (MinionController item in ownedMinions)
        {
            if (item != null && item.entity != null && item.entity.factionEntity != null && item.minionState == MinionController.MinionStates.Idle)
            {
                if (item.entity.factionEntity is FactionUnit)
                {
                    FactionUnit facUnit = item.entity.factionEntity as FactionUnit;
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
        }
    }
    Vector3 currentScoutingDestination; 
    private void PerformAction()
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
        CleanLists();

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
        foreach (MinionController minion in ownedMinions)
        {
            if (minion == null) continue;
            SelectableEntity minionEntity = minion.entity;
            if (minionEntity == null) continue;
            if (minionEntity.CanConstruct() && !minion.IsCurrentlyBuilding())
            {
                for (int i = 0; i < minionEntity.constructableBuildings.Length; i++) //check if this has a unit we can spawn
                {
                    FactionBuilding entity = minionEntity.constructableBuildings[i];
                    if (CanAfford(entity))
                    { 
                        switch (desire)
                        {
                            case BuildingDesire.UnitSpawner:
                                if (entity.IsSpawner())
                                { 
                                    chosenEntity = minionEntity;
                                    building = entity;
                                }
                                break; 
                            case BuildingDesire.PopulationAdder:
                                if (entity.IsPopulationAdder())
                                {
                                    chosenEntity = minionEntity;
                                    building = entity;
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
                chosenEntity.minionController.ForceBuildTarget(last);
            }
        } 
    } 
    private void TryToSpawnType(UnitDesire desire)
    { 
        //pick a unit/building that can spawn units. try to queue up a unit
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item == null) continue;
            if (item.CanProduceUnits())
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
                    FactionUnit newUnit = FactionUnit.CreateInstance(unit.prefabToSpawn.name, unit.spawnTimeCost, unit.prefabToSpawn, unit.goldCost);
                    int cost = newUnit.goldCost;
                    gold -= cost;
                    chosenEntity.buildQueue.Add(newUnit);
                }
            }
        }
    }
     
    public List<SelectableEntity> visibleResources = new();
    private void EvaluateVisibleResources()
    {
        Debug.Log("Evaluating visible resources");
        visibleResources.Clear();
        FogOfWarTeam fow = FogOfWarTeam.GetTeam(playerTeamID);
        foreach (SelectableEntity item in Global.Instance.harvestableResources)
        {
            bool visibleInFog = fow.GetFogValue(item.transform.position) < Global.Instance.minFogStrength * 255;
            if (visibleInFog)
            {
                visibleResources.Add(item);
            }
        }
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
                    if (builder.minionController != null && builder.CanConstruct() && !builder.minionController.IsCurrentlyBuilding()) //minion
                    {
                        builder.minionController.CommandBuildTarget(building);
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
            if (item.minionController != null && item.CanHarvest() && !item.minionController.IsCurrentlyBuilding()) //minion
            {
                switch (item.minionController.minionState)
                {
                    case MinionController.MinionStates.Idle:
                    case MinionController.MinionStates.FindInteractable:
                        if (visibleResources.Count > 0)
                        {
                            int rand = Random.Range(0, visibleResources.Count);
                            item.minionController.CommandHarvestTarget(visibleResources[rand]); 
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
