using FoW;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;
using static UnityEditor.Progress;
using Pathfinding.Drawing;
/// <summary>
/// AI that is governed by the server
/// </summary>
public class AIPlayer : Player
{
    //to give a unit to the AI, set its desired team to a negative number 
    public float actionTime = 2;
    private float timer = 0;
    public Transform spawnPosition;
    public override void OnNetworkSpawn()
    {
        if (IsOwner) //spawn initial minions/buildings  
        {
            if (spawnPosition != null) SpawnMinion(spawnPosition.position, playerFaction.spawnableEntities[0]);
        }
    }
    public override void Start()
    {
        base.Start();
    }
    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= actionTime)
        {
            timer = 0;
            PerformAction();
        }
    }
    private void PerformAction()
    {
        if (visibleResources.Count < 1)
        {
            EvaluateVisibleResources();
        }
        else
        {
            int rand = Random.Range(0, 3);
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
            }
        }

    }
    public List<SelectableEntity> visibleResources = new();
    private void EvaluateVisibleResources()
    {
        visibleResources.Clear();
        FogOfWarTeam fow = FogOfWarTeam.GetTeam(teamID);
        foreach (SelectableEntity item in Global.Instance.harvestableResources)
        {
            bool visibleInFog = fow.GetFogValue(item.transform.position) < Global.Instance.minFogStrength * 255;
            if (visibleInFog)
            {
                visibleResources.Add(item);
            }
        }
    }
    private void TellInactiveMinersToHarvest()
    {
        Debug.Log("Telling miners to harvest");
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item.minionController != null && item.CanHarvest()) //minion
            {
                switch (item.minionController.minionState)
                {
                    case MinionController.MinionStates.Idle:
                    case MinionController.MinionStates.FindInteractable:
                        int rand = Random.Range(0, visibleResources.Count);
                        item.minionController.CommandHarvestTarget(visibleResources[rand]);
                        break;
                }
            }
        }
    }
    private void SetRallyPoint()
    {

    }
    private void TryToBuildStructure()
    {
        Debug.Log("Trying to build structure");
        //pick a unit/building that can spawn units. try to queue up a unit
        SelectableEntity chosenEntity = null;
        FactionBuilding building = null;
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item.CanConstruct())
            {
                for (int i = 0; i < item.constructableBuildings.Length; i++) //check if this has a unit we can spawn
                {
                    if (item.constructableBuildings[i].goldCost <= gold) //if this is affordable
                    {
                        chosenEntity = item;
                        building = item.constructableBuildings[i];
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
    private void TryToCreateUnit()
    {
        Debug.Log("Trying to spawn unit");
        //pick a unit/building that can spawn units. try to queue up a unit
        SelectableEntity chosenEntity = null;
        FactionUnit unit = null;
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item.CanProduceUnits())
            {
                for (int i = 0; i < item.spawnableUnits.Length; i++) //check if this has a unit we can spawn
                {
                    if (item.spawnableUnits[i].goldCost <= gold) //if this is affordable
                    {
                        chosenEntity = item;
                        unit = item.spawnableUnits[i];
                    }
                }
            }
        }
        if (chosenEntity != null && unit != null)
        {
            FactionUnit newUnit = FactionUnit.CreateInstance(unit.prefabToSpawn.name, unit.spawnTimeCost, unit.prefabToSpawn, unit.goldCost);
            int cost = newUnit.goldCost;
            gold -= cost;
            chosenEntity.buildQueue.Add(newUnit);
        }
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
                    select.desiredTeamNumber = (sbyte)teamID; //necessary for it to spawn as AI controlled
                    if (select.net == null) select.net = select.GetComponent<NetworkObject>();
                    select.net.Spawn();
                }
                return select;
            }
        }
        return null;
    }
    private void AttackMoveWithCombatUnits()
    {
        Debug.Log("Attack moving");
        int max = Global.Instance.maxMapSize;
        Vector3 randomTarget = new Vector3(Random.Range(-max, max), 0, Random.Range(-max, max));
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item.minionController != null && !item.CanHarvest()) //minion
            {
                item.minionController.AIAttackMove(randomTarget);
            }
        }
    }

}
