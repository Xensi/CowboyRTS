using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine.Rendering;
using Pathfinding;
using System.Linq;
using UtilityMethods;

public class Global : NetworkBehaviour
{
    public static Global instance { get; private set; }
    public RTSPlayer localPlayer;
    public readonly string FRIENDLY_ENTITY = "Entity";
    public readonly string ENEMY_ENTITY = "EnemyEntity";
    public RectTransform selectionRect;
    public List<Material> colors;
    public List<Color> teamColors;
    public List<Color> aiTeamColors;
    public List<Faction> factions;
    public Material transparent;
    public Material blocked;
    public AudioClip[] footsteps;
    public Transform queueParent;
    public GameObject explosionPrefab;
    public GameObject gridVisual;

    public TrailController gunTrailGlobal;
    public ExplosiveProjectile cannonBall;
    public AudioClip explosion;
    public Volume fogVolume;
    public GraphUpdateScene graphUpdateScenePrefab;
    [HideInInspector] public int maxMapSize = 25; //radius
    #region Nonowner Movement
    [HideInInspector] public float allowedNonOwnerError = 1.5f; //should be greater than real loc threshold
    [HideInInspector] public float updateRealLocThreshold = .75f; //1
    public readonly float defaultMeleeSearchRange = 4f;

    [HideInInspector] public int maximumQueuedRealLocations = 5;
    [HideInInspector] public float closeEnoughDist = .3f;
    [HideInInspector] public float lerpScale = .15f;
    #endregion
    public readonly float maxFogValue = 255;
    public readonly float minFogStrength = 0.45f;
    public readonly float exploredFogStrength = 0.51f;
    //[SerializeField] public Camera mainCam;
    //[SerializeField] public Camera lineCam;
    public Camera[] cams;
    [HideInInspector] public LayerMask groundLayer;
    [HideInInspector] public LayerMask blockingLayer;
    [HideInInspector] public LayerMask gameLayer;
    [HideInInspector] public LayerMask allEntityLayer;
    [HideInInspector] public LayerMask enemyLayer;
    [HideInInspector] public LayerMask friendlyEntityLayer;
    [HideInInspector] public List<RTSPlayer> uninitializedPlayers = new();
    [HideInInspector] public List<RTSPlayer> initializedPlayers = new();
    [HideInInspector] public AIPlayer[] aiPlayers;
    [HideInInspector] public List<Player> allPlayers = new();
    public Grid grid;

    //
    //public List<SelectableEntity> enemyMinions = new();
    public Canvas gameCanvas;
    public GameObject defaultCaptureEffect;

    public GenericProgressBar structureProgressBar;
    public readonly int maxUnitsInProductionQueue = 10;


    public TMP_Text reinforcementText;
    [HideInInspector] public ArbitraryUnitSpawner unitSpawnerToTrackReinforcements;

    public EntitySearcher entitySearcher;
    public CrosshairDisplay crosshairPrefab;

    [HideInInspector] public bool playerHasWon = false;
    public readonly int attackMoveDestinationEnemyArrayBufferSize = 50;
    public readonly int fullEnemyArraySize = 50;

    public HashSet<Entity> allEntities = new();
    [HideInInspector] public SpatialHash spatialHash;

    private bool finishedInitializingNewPlayers = false;
    private const int maxEntities = 1000;
    private readonly int maxAIPlayers = 10;

    public GameObject rallyPrefab;
    private readonly int maxArmySize = 300;
    #region UI

    public GameObject resourcesParent;
    public TMP_Text resourceText;
    #endregion

    //Minion sound profile mapping:
    // 0: spawn
    // 1: damage
    // 2: attack move
    // 3: ability used
    // 4: ability refresh

    //Structure sound profile mapping:
    //0: spawn
    //1: selection
    public int GetMaxArmySize()
    {
        return maxArmySize;
    }

    #region Standard
    private void Awake()
    {
        groundLayer = LayerMask.GetMask("Ground");
        blockingLayer = LayerMask.GetMask(FRIENDLY_ENTITY, "Obstacle");
        gameLayer = LayerMask.GetMask(FRIENDLY_ENTITY, "Obstacle", "Ground", "OtherEntities", ENEMY_ENTITY);
        allEntityLayer = LayerMask.GetMask(FRIENDLY_ENTITY, "OtherEntities", ENEMY_ENTITY);
        enemyLayer = LayerMask.GetMask(ENEMY_ENTITY);
        friendlyEntityLayer = LayerMask.GetMask(FRIENDLY_ENTITY);

        if (instance != null && instance != this)
        {
            Destroy(this);
        }
        else
        {
            instance = this;
        }

        aiPlayers = new AIPlayer[maxAIPlayers];
        /*SelectableEntity[] array = FindObjectsOfType<SelectableEntity>();
        harvestableResources = new SelectableEntity[array.Length];
        int j = 0;
        for (int i = 0; i < array.Length; i++)
        {
            if (array[i] != null && array[i].IsOre())
            {
                harvestableResources[j] = array[i];
                j++;
            }
        }*/
        resourcesParent.SetActive(false);
        spatialHash = GetComponent<SpatialHash>();
    }
    private void Update()
    {
        InitializePlayers();
        if (!playerHasWon) CheckIfAPlayerHasWon();
        UpdateReinforcementText();
    }
    #endregion

    public void AddEntityToMainList(Entity ent)
    {
        allEntities.Add(ent);
    }
    public void RemoveEntityFromMainList(Entity ent)
    {
        allEntities.Remove(ent);
    }
    public int GetMaxEntities()
    {
        return maxEntities;
    }
    public int GetNumEntities()
    {
        return allEntities.Count;
    }
    /// <summary>
    /// Passes all entity list by reference.
    /// </summary>
    /// <returns></returns>
    public ref HashSet<Entity> GetEntityList()
    {
        return ref allEntities;
    }

    public void PlayStructureSelectSound(Entity entity)
    {
        if (entity.sounds.Length > 1) PlayClipAtPoint(entity.sounds[1], entity.transform.position, .75f);
    }
    public void PlayMinionRefreshSound(Entity entity)
    {
        if (entity.sounds.Length > 4) PlayClipAtPoint(entity.sounds[4], entity.transform.position, 1f, 1, true);
    }
    public void PlayMinionAbilitySound(Entity entity)
    {
        if (entity.sounds.Length > 3) PlayClipAtPoint(entity.sounds[3], entity.transform.position, .5f, 1, true);
    }
    public Entity FindEntityFromObject(GameObject obj)
    {
        Entity entity = obj.GetComponent<Entity>();
        if (entity == null)
        {
            entity = obj.GetComponentInParent<Entity>();
        }
        if (entity == null)
        {
            entity = obj.GetComponentInChildren<Entity>();
        }
        return entity;
    }
    
    private void UpdateReinforcementText()
    {
        if (reinforcementText != null)
        {
            if (unitSpawnerToTrackReinforcements != null)
            { 
                if (unitSpawnerToTrackReinforcements.shouldSpawn && unitSpawnerToTrackReinforcements.spawnWaves > 0)
                {
                    reinforcementText.transform.parent.gameObject.SetActive(true);
                    reinforcementText.text = "Reinforcements arrive in: " + Mathf.RoundToInt(unitSpawnerToTrackReinforcements.spawnTimer) + "s";
                }
                else
                {
                    reinforcementText.transform.parent.gameObject.SetActive(false);
                }
            }
            else
            { 
                reinforcementText.transform.parent.gameObject.SetActive(false);
            }
        } 
    }

    private void InitializePlayers()
    {
        if (uninitializedPlayers.Count > 0)
        {
            List<RTSPlayer> movedPlayers = new();
            foreach (RTSPlayer player in uninitializedPlayers)
            {
                if (player.inTheGame.Value == true)
                {
                    initializedPlayers.Add(player);
                    if (!allPlayers.Contains(player)) allPlayers.Add(player);
                    movedPlayers.Add(player);
                }
            }
            foreach (RTSPlayer player in movedPlayers)
            {
                uninitializedPlayers.Remove(player);
            }
            if (!finishedInitializingNewPlayers)
            {
                finishedInitializingNewPlayers = true;
                UpdateEnemyLists();
            }
        }
    }
    public void UpdateEnemyLists()
    {
        //Debug.Log("Updating Enemy Lists");
        foreach (Entity entity in allEntities)
        {
            if (entity != null) entity.StartGameAddToEnemyLists();
        }
    }
    public void CheckIfAPlayerHasWon()
    {
        if (initializedPlayers.Count <= 1) return;

        RTSPlayer potentialWinner = null;
        int inTheGameCount = 0;
        foreach (RTSPlayer item in initializedPlayers)
        {
            if (item.inTheGame.Value == true)
            {
                potentialWinner = item;
                inTheGameCount++;
            }
        }
        if (potentialWinner != null && inTheGameCount == 1)
        {
            TargetPlayerWinsTheGame(potentialWinner);
        }
    }
    private void TargetPlayerWinsTheGame(RTSPlayer player)
    {
        playerHasWon = true;
        Debug.Log("player has won");
    }
    public AudioSource PlayClipAtPoint(AudioClip clip, Vector3 pos, float volume = 1, float pitch = 1, bool useChorus = true)
    {
        GameObject tempGO = new("TempAudio"); // create the temp object
        tempGO.transform.SetParent(transform);
        tempGO.transform.position = pos; // set its position
        AudioSource tempASource = tempGO.AddComponent<AudioSource>(); // add an audio source
        if (useChorus)
        {
            tempGO.AddComponent<AudioChorusFilter>();
        }
        tempASource.clip = clip;
        tempASource.volume = volume;
        tempASource.pitch = pitch;
        tempASource.spatialBlend = 1; //3d   
        tempASource.Play(); // start the sound
        Destroy(tempGO, tempASource.clip.length * pitch); // destroy object after clip duration (this will not account for whether it is set to loop) 
        return tempASource;
    }
    public void TellRTSPlayerToSetRally()
    {
        localPlayer.ReadySetRallyPoint();
    }

}
