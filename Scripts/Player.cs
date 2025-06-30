using FoW;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using static RTSPlayer;
using System.Threading.Tasks;

[RequireComponent(typeof(NetworkObject))]
public class Player : NetworkBehaviour
{
    public Faction playerFaction;
    //public List<SelectableEntity> selectedEntities; //selected and we can control them
    private readonly int maxArmy = 300;
    public Entity[] selectedEntities;
    public int numSelectedEntities = 0;
    public List<Entity> ownedEntities;
    public List<StateMachineController> ownedMinions;
    public List<Entity> unbuiltStructures;
    public List<StateMachineController> ownedBuilders;
    public List<Harvester> ownedHarvesters = new();
    public List<Depot> ownedDepots = new();
    public List<Ore> friendlyOres = new();
    public int gold = 100;
    public int wood = 0;
    public int cactus = 0;
    public int population = 0;
    public int maxPopulation = 10;
    public int playerTeamID = 0; //used for a player's fog of war
    public int allegianceTeamID = 0; //used to determine who is friendly and who is enemy. by default: 0 is player, 1 is AI
    [HideInInspector] public FogOfWarTeam fow;
    public List<Entity> enemyEntities = new();
    public List<Entity> visibleEnemies = new();
    private int visibleIndexer = 0;
    public bool enable = true; //enabling in the middle of the game does not currently work
    public Color playerColor = Color.white;

    public void UpdateSelectedEntities(Entity ent, bool val)
    {
        //Debug.Log(selectedEntities.Length);
        if (val)
        {
            selectedEntities[numSelectedEntities] = ent;
            numSelectedEntities++;
            //selectedEntities.Add(ent);
        }
        else
        {
            selectedEntities[numSelectedEntities] = null;
            numSelectedEntities = Mathf.Clamp(numSelectedEntities--, 0, maxArmy);
            //selectedEntities.Remove(ent);
        }
    }
    public enum ActionType
    {
        Move, AttackTarget, Harvest, Deposit, Garrison, BuildTarget, AttackMove, MoveToTarget
    }
    public ActionType actionType = ActionType.Move;

    public List<UnitOrder> UnitOrdersQueue = new();
    [Serializable]
    public class UnitOrder
    {
        public StateMachineController unit;
        public ActionType action;
        public Entity target;
        public Vector3 targetPosition;
    }

    public void Awake()
    {
        if (!enable) return;
        Global.instance.allPlayers.Add(this);
        //allegianceTeamID = playerTeamID; //by default
        selectedEntities = new Entity[maxArmy];
    }
    public virtual void Start()
    {
        if (!enable) return;
        if (playerFaction != null)
        {
            //Debug.Log("Setting gold and pop");
            if (LevelInfo.Instance.overrideDefaultValues)
            { 
                gold = LevelInfo.Instance.goldValueOverride;
                maxPopulation = LevelInfo.Instance.startMaxPopOverride;
            }
            else
            { 
                gold = playerFaction.startingGold;
                maxPopulation = playerFaction.startingMaxPopulation;
            }
        }
        fow = FogOfWarTeam.GetTeam(playerTeamID);
        if (fow == null) Debug.LogError("No fow found");
    }

    public virtual void Update()
    {
        if (!enable) return;
        UpdateVisibilities();
        //CleanEntityLists();
    }

    public async void ProcessOrdersInBatches()
    {
        while (UnitOrdersQueue.Count > 0)
        {
            UnitOrder order = UnitOrdersQueue[0]; //fetch first order
            if (order != null)
            {
                StateMachineController orderedUnit = order.unit;
                if (orderedUnit != null && orderedUnit.canReceiveNewCommands)
                {
                    //Debug.Log("Batch processing orders" + orderedUnit); 
                    orderedUnit.ProcessOrder(order);
                    UnitOrdersQueue.RemoveAt(0);
                }
            }
            await Task.Yield();
        }
    }
    public EntitySearcher CreateEntitySearcherAtPosition(Vector3 position, int allegiance = 0)
    {
        EntitySearcher searcher = Instantiate(Global.instance.entitySearcher, position, Quaternion.identity);
        searcher.dr.radius = searcher.SearchRadius();
        searcher.dr.SetColor(Color.red);
        searcher.creatorAllegianceID = allegiance;
        searcher.playerCreator = this;
        return searcher;
    }

    public void CreateEntitySearcherAndAssign(Vector3 position, StateMachineController minion)
    {
        /*if (minion == null) return;
        EntitySearcher searcher = CreateEntitySearcherAtPosition(position, minion.ent.controllerOfThis.allegianceTeamID);
        if (searcher == null) return;
        //if this unit is already assigned to an entity searcher, unassign it
        if (minion.assignedEntitySearcher != null)
        {
            minion.assignedEntitySearcher.UnassignUnit(minion);
        }
        //assign the entity searcher to selected units
        minion.assignedEntitySearcher = searcher;
        //update the entity searcher's assigned units list
        minion.assignedEntitySearcher.AssignUnit(minion);
        minion.hasCalledEnemySearchAsyncTask = false; //tell the minion to run a new search */
    }
    private void UpdateVisibilities() //AI will need a different solution
    {
        int framesForFullSweep = 30;
        int numToUpdate = Mathf.Clamp(enemyEntities.Count / framesForFullSweep, 1, 999);
        if (enemyEntities.Count > 0)
        {
            for (int i = 0; i < numToUpdate; i++)
            {
                if (visibleIndexer >= enemyEntities.Count) visibleIndexer = enemyEntities.Count - 1;

                Entity enemy = enemyEntities[visibleIndexer];
                if (enemy != null)
                {
                    if (this is AIPlayer)
                    {
                        //check visibility of unit manually
                        if (fow == null) fow = FogOfWarTeam.GetTeam(playerTeamID);
                        byte fogValue = fow.GetFogValue(enemy.transform.position); //get the value of the fog at this position
                        bool isVisibleInFog = fogValue < Global.instance.minFogStrength * 255;
                        if (isVisibleInFog)
                        {
                            if (!visibleEnemies.Contains(enemy))
                            {
                                visibleEnemies.Add(enemy);
                            }
                        }
                        else
                        {
                            visibleEnemies.Remove(enemy);
                        }
                    }
                    else
                    { //use MP visibility check
                        bool visible = enemy.IsVisibleInFog();
                        if (visible)
                        {
                            if (!visibleEnemies.Contains(enemy))
                            {
                                visibleEnemies.Add(enemy);
                            }
                        }
                        else
                        {
                            visibleEnemies.Remove(enemy);
                        }
                    } 
                }

                visibleIndexer++;
                if (visibleIndexer >= enemyEntities.Count) visibleIndexer = 0;
            }
            
        }
    }  
    public bool PositionFullyVisible(Vector3 position)
    {
        return fow.GetFogValue(position) < Global.instance.minFogStrength * 255;
    }
    public bool PositionExplored(Vector3 position)
    {
        return fow.GetFogValue(position) <= Global.instance.exploredFogStrength * 255;
    }
    public bool CheckIfPositionIsOnRamp(Vector3 position)
    {
        int use;
        //first check if hit position is very close to integer
        int checkAgainst = Mathf.RoundToInt(position.y);
        if (Mathf.Abs(checkAgainst - position.y) < 0.01f) //
        {
            use = checkAgainst;
        }
        else
        {
            use = Mathf.CeilToInt(position.y);
        }

        float buffer = 0.5f;
        Vector3 testPosition = new Vector3(position.x, use + buffer, position.z);
        if (Physics.Raycast(testPosition, Vector3.down, out RaycastHit rampHit, Mathf.Infinity, Global.instance.groundLayer))
        {
            float distance = Mathf.Abs(testPosition.y - rampHit.point.y);
            return distance > 0.01f + buffer || distance < buffer - 0.01f;
        }
        return false;
    }
    public bool IsPositionBlockedByEntity(Entity entity)
    {
        BoxCollider box = entity.physicalCollider as BoxCollider;
        if (box == null) return false;
        Vector3 position = entity.transform.position;
        bool onRamp = CheckIfPositionIsOnRamp(position);
        bool placementBlocked = false;

        FogOfWarTeam fow = FogOfWarTeam.GetTeam((int)playerTeamID);
        if (fow.GetFogValue(position) > 0.1f * 255)
        {
            placementBlocked = true;
        }
        else
        {
            if (onRamp)
            {
                placementBlocked = true;
            }
            else
            {
                //float sides = .24f;
                //float height = .5f;
                //Vector3 halfExtents = new Vector3(sides, height, sides);
                //Vector3 center = new Vector3(position.x, position.y + height, position.z);


                Vector3 worldCenter = box.transform.TransformPoint(box.center);
                Vector3 worldHalfExtents = Vector3.Scale(box.size, box.transform.lossyScale) * 0.45f; 
                placementBlocked = Physics.CheckBox(worldCenter, worldHalfExtents, entity.transform.rotation,
                    Global.instance.blockingLayer, QueryTriggerInteraction.Ignore);

                debugPos = worldCenter;
                debugSize = box.size;
            }
        }
        return placementBlocked;
    }
    Vector3 debugPos;
    Vector3 debugSize;
    private bool IsPlayer()
    {
        return allegianceTeamID == 0;
    }
    public bool IsValidTarget(Entity target)
    {

        if (target == null || !target.isAttackable || !target.alive || !target.isTargetable.Value
            || IsPlayer() && !target.IsVisibleInFog() || !target.IsEnemyOfPlayer(this) || target.currentHP.Value <= 0
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
    private void OnDrawGizmos()
    { 
        //Gizmos.color = Color.yellow;
        //Gizmos.DrawWireCube(debugPos, debugSize);
    }
    public bool IsPositionBlocked(Vector3 position)
    {
        bool onRamp = CheckIfPositionIsOnRamp(position);
        bool placementBlocked = false;

        FogOfWarTeam fow = FogOfWarTeam.GetTeam((int)playerTeamID);
        if (fow.GetFogValue(position) > 0.1f * 255)
        {
            placementBlocked = true;
        }
        else
        {
            if (onRamp)
            {
                placementBlocked = true;
            }
            else
            {
                float sides = .24f;
                float height = .5f;
                Vector3 halfExtents = new Vector3(sides, height, sides);
                Vector3 center = new Vector3(position.x, position.y + height, position.z);
                placementBlocked = Physics.CheckBox(center, halfExtents, Quaternion.identity, 
                    Global.instance.blockingLayer, QueryTriggerInteraction.Ignore);
            }
        }
        return placementBlocked;
    }
}
