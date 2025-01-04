using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;
using UnityEngine; 

public class UnitSpawner : NetworkBehaviour
{
    [SerializeField] private bool shouldSetAIDecisionTimerOnSpawn = false;
    [SerializeField] private float setTimerOnSpawnValue = 1;

    public bool shouldSpawn = false;

    public int spawnWaves = 1;

    [SerializeField] private int unitsToSpawnPerWave = 1;

    [SerializeField] private float timeDelayBetweenWaves = 2;
    [SerializeField] private float timeDelayBeforeFirstWave = 1;

    public float spawnTimer = 0;

    [SerializeField] private FactionEntity unitToSpawn;

    [SerializeField] private List<SelectableEntity> watchEntities = new();

    [SerializeField] private Player playerToGrantControlOverSpawnedUnits;
    [SerializeField] private VictoryManager victoryManagerToGrantUnitsTo;
    private enum StartSpawningCondition
    {
        None,
        WatchEntitiesDamaged
    }
    [SerializeField] private StartSpawningCondition spawnCondition = StartSpawningCondition.None;
    private void Start()
    {
        spawnTimer = timeDelayBeforeFirstWave;
    }
    private void Update()
    {
        CheckStartSpawningCondition();
        TickTimer();
    }
    private void SpawnWaves()
    {
        if (!shouldSpawn) return;
        if (spawnWaves <= 0) return;
        for (int i = 0; i < unitsToSpawnPerWave; i++)
        { //this needs to pick a position nearby to spawn them in if the position is blocked
            if (playerToGrantControlOverSpawnedUnits != null)
            {
                SpawnUnitUnderPlayerControl(playerToGrantControlOverSpawnedUnits);
            }
        }
        if (shouldSetAIDecisionTimerOnSpawn)
        {
            if (playerToGrantControlOverSpawnedUnits is AIPlayer)
            {
                AIPlayer ai = playerToGrantControlOverSpawnedUnits as AIPlayer;
                ai.timer = setTimerOnSpawnValue;
            }
        }
        spawnWaves--;
    }
    private void SpawnUnitUnderPlayerControl(Player player)
    {
        if (player is RTSPlayer)
        {
            RTSPlayer rts = player as RTSPlayer;
            rts.GenericSpawnMinion(transform.position, unitToSpawn, this);
        }
        else if (player is AIPlayer)
        {
            AIPlayer ai = player as AIPlayer;
            ai.SpawnMinion(transform.position, unitToSpawn);
        }
    }
    private void TickTimer()
    {
        if (!shouldSpawn) return;
        if (spawnWaves <= 0) return;
        if (spawnTimer > 0)
        {
            spawnTimer -= Time.deltaTime;
        }
        else //timer goes off
        {
            spawnTimer = timeDelayBetweenWaves;
            SpawnWaves();
        }
    }
    private async void CheckStartSpawningCondition()
    {
        if (shouldSpawn) return;
        await Task.Yield();
        switch (spawnCondition)
        {
            case StartSpawningCondition.None:
                break;
            case StartSpawningCondition.WatchEntitiesDamaged: 
                foreach (SelectableEntity item in watchEntities)
                {
                    if (item != null)
                    {
                        if (item.currentHP.Value < item.maxHP)
                        {
                            shouldSpawn = true;
                            Global.Instance.unitSpawnerToTrackReinforcements = this;
                            break;
                        }
                    } 
                }
                break;
            default:
                break;
        } 
    }

}
