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
            bool foundPosition = false;
            Vector3 positionToSpawn = transform.position;
            Vector2 dir = Random.insideUnitCircle.normalized;
            float step = 0.1f;
            while (!foundPosition)
            {
                if (Physics.Raycast(positionToSpawn + (new Vector3(0, 100, 0)), Vector3.down,
                            out RaycastHit hit, Mathf.Infinity, Global.Instance.gameLayer))
                {
                    SelectableEntity target = Global.Instance.FindEntityFromObject(hit.collider.gameObject);
                    if (target != null) //blocked
                    {
                        positionToSpawn = positionToSpawn + new Vector3(step * dir.x, 0, step * dir.y);
                    }
                    else
                    {
                        foundPosition = true;
                    }
                }
            }
            if (foundPosition)
            {
                if (playerToGrantControlOverSpawnedUnits != null)
                {
                    SpawnUnitUnderPlayerControl(playerToGrantControlOverSpawnedUnits, positionToSpawn);
                }
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
    
    private void SpawnUnitUnderPlayerControl(Player player, Vector3 position)
    { 
        if (player == null)
        {
            Debug.Log("Spawning under local player");
            RTSPlayer rts = Global.Instance.localPlayer;
            rts.GenericSpawnMinion(position, unitToSpawn, this);
        }
        else if (player is RTSPlayer)
        {
            if (player != null)
            { 
                RTSPlayer rts = player as RTSPlayer;
                rts.GenericSpawnMinion(position, unitToSpawn, this);
            }
        }
        else if (player is AIPlayer)
        {
            AIPlayer ai = player as AIPlayer;
            ai.SpawnMinion(position, unitToSpawn);
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
