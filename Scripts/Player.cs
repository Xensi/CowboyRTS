using FoW;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(NetworkObject))]
public class Player : NetworkBehaviour
{
    public Faction playerFaction;
    public List<SelectableEntity> ownedEntities;
    public List<MinionController> ownedMinions;
    public List<SelectableEntity> unbuiltStructures;
    public List<MinionController> ownedBuilders;
    public int gold = 100;
    public int population = 0;
    public int maxPopulation = 10;
    public int playerTeamID = 0; //used for a player's fog of war
    public int allegianceTeamID = 0; //used to determine who is friendly and who is enemy. by default: 0 is player, 1 is AI
    public FogOfWarTeam fow;
    public List<SelectableEntity> enemyEntities = new();
    public List<SelectableEntity> visibleEnemies = new();
    private int visibleIndexer = 0;
    public bool enable = true; //enabling in the middle of the game does not currently work
    public void Awake()
    {
        if (!enable) return;
        Global.Instance.allPlayers.Add(this);
        allegianceTeamID = playerTeamID; //by default
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

    private void UpdateVisibilities()
    {
        if (enemyEntities.Count > 0)
        {
            int framesForFullSweep = 30;
            int numToUpdate = Mathf.Clamp(enemyEntities.Count / framesForFullSweep, 1, 999);
            for (int i = 0; i < numToUpdate; i++)
            {
                if (visibleIndexer >= enemyEntities.Count) visibleIndexer = enemyEntities.Count - 1;

                SelectableEntity enemy = enemyEntities[visibleIndexer];
                if (enemy != null)
                { 
                    bool visible = enemy.isVisibleInFog;
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

                visibleIndexer++;
                if (visibleIndexer >= enemyEntities.Count) visibleIndexer = 0;
            }
        }
    } 
    private void CleanEntityLists()
    {
        /*if (allFactionEntities.Count > 0)
        {
            if (indexer >= Instance.allFactionEntities.Count)
            {
                indexer = Instance.allFactionEntities.Count - 1;
            }
            SelectableEntity entity = allFactionEntities[indexer];
            if (entity == null || !entity.alive)
            {
                allFactionEntities.RemoveAt(indexer);
            }
            indexer++;
            if (indexer >= allFactionEntities.Count) indexer = 0;
        }*/
        /*if (enemyEntities.Count > 0)
        {
            if (enemyIndexer >= enemyEntities.Count)
            {
                enemyIndexer = enemyEntities.Count - 1;
            }
            SelectableEntity entity = enemyEntities[enemyIndexer];
            if (entity == null || !entity.alive)
            {
                enemyEntities.RemoveAt(enemyIndexer);
            }
            enemyIndexer++;
            if (enemyIndexer >= enemyEntities.Count) enemyIndexer = 0;
        }*/
        /*if (enemyMinions.Count > 0)
        {
            if (enemyMinionIndexer >= Instance.enemyMinions.Count)
            {
                enemyMinionIndexer = Instance.enemyMinions.Count - 1;
            }
            SelectableEntity entity = enemyMinions[enemyMinionIndexer];
            if (entity == null || !entity.alive)
            {
                enemyMinions.RemoveAt(enemyMinionIndexer);
            }
            enemyMinionIndexer++;
            if (enemyMinionIndexer >= enemyMinions.Count) enemyMinionIndexer = 0;
        }*/
    }
    public bool PositionFullyVisible(Vector3 position)
    {
        return fow.GetFogValue(position) < Global.Instance.minFogStrength * 255;
    }
    public bool PositionExplored(Vector3 position)
    {
        return fow.GetFogValue(position) <= Global.Instance.exploredFogStrength * 255;
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
        if (Physics.Raycast(testPosition, Vector3.down, out RaycastHit rampHit, Mathf.Infinity, Global.Instance.groundLayer))
        {
            float distance = Mathf.Abs(testPosition.y - rampHit.point.y);
            return distance > 0.01f + buffer || distance < buffer - 0.01f;
        }
        return false;
    }
    public bool IsPositionBlockedByEntity(SelectableEntity entity)
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
                    Global.Instance.blockingLayer, QueryTriggerInteraction.Ignore);

                debugPos = worldCenter;
                debugSize = box.size;
            }
        }
        return placementBlocked;
    }
    Vector3 debugPos;
    Vector3 debugSize;

    private void OnDrawGizmos()
    { 
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(debugPos, debugSize);
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
                    Global.Instance.blockingLayer, QueryTriggerInteraction.Ignore);
            }
        }
        return placementBlocked;
    }
}
