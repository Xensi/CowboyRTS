using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine.Rendering;
using Pathfinding;

public class Global : NetworkBehaviour
{
    public static Global Instance { get; private set; }
    public RectTransform selectionRect;
    public List<Material> colors;
    public List<Color> teamColors;
    public List<Color> aiTeamColors;
    public List<Faction> factions; 
    public List<Button> productionButtons;
    public Material transparent;
    public Material blocked;
    public RTSPlayer localPlayer;
    public TMP_Text goldText;
    public AudioClip[] footsteps;
    public List<Button> queueButtons;
    public Transform queueParent;
    public GameObject explosionPrefab;

    public GameObject selectedParent;
    public TMP_Text nameText;
    public TMP_Text descText;
    public SelectableEntity[] harvestableResources;
    public GameObject resourcesParent;
    public TMP_Text resourceText;
    public GameObject gridVisual;
    public TMP_Text hpText;

    public TrailController gunTrailGlobal;
    public Projectile cannonBall;
    public AudioClip explosion;
    public GameObject singleUnitInfoParent;
    public TMP_Text popText;
    public Volume fogVolume;
    public GraphUpdateScene graphUpdateScenePrefab;
    public int maxMapSize = 25; //radius
    public float allowedNonOwnerError = 1.5f; //should be greater than real loc threshold
    public float updateRealLocThreshold = .75f; //1

    public int maximumQueuedRealLocations = 5;
    public float closeEnoughDist = .3f;
    public float lerpScale = 1;
    public readonly float minFogStrength = 0.45f;
    public readonly float exploredFogStrength = 0.51f;
    //[SerializeField] public Camera mainCam;
    //[SerializeField] public Camera lineCam;
    public Camera[] cams;
    public LayerMask groundLayer;
    public LayerMask blockingLayer;
    public LayerMask gameLayer;
    public LayerMask entityLayer;
    public List<RTSPlayer> uninitializedPlayers = new();
    public List<RTSPlayer> initializedPlayers = new();
    public List<AIPlayer> aiTeamControllers = new();
    public List<Player> allPlayers = new();
    public Grid grid;

    public List<SelectableEntity> allFactionEntities = new();
    public List<SelectableEntity> enemyEntities = new();
    public Canvas gameCanvas;

    //Minion sound profile mapping:
    // 0: spawn
    // 1: damage
    // 2: attack move
    // 3: ability used
    // 4: ability refresh

    //Structure sound profile mapping:
    //0: spawn
    //1: selection
    //
    //

    public void PlayStructureSelectSound(SelectableEntity entity)
    {  
        if (entity.sounds.Length > 1) PlayClipAtPoint(entity.sounds[1], entity.transform.position, .75f);
    }
    public void PlayMinionRefreshSound(SelectableEntity entity)
    {
        if (entity.sounds.Length > 4) PlayClipAtPoint(entity.sounds[4], entity.transform.position, 1f, 1, true);
    }
    public void PlayMinionAbilitySound(SelectableEntity entity)
    {
        if (entity.sounds.Length > 3) PlayClipAtPoint(entity.sounds[3], entity.transform.position, .5f, 1, true);
    }

    private void Awake()
    {
        groundLayer = LayerMask.GetMask("Ground");
        blockingLayer = LayerMask.GetMask("Entity", "Obstacle");
        gameLayer = LayerMask.GetMask("Entity", "Obstacle", "Ground");
        entityLayer = LayerMask.GetMask("Entity", "OtherEntities");
        // If there is an instance, and it's not me, delete myself.

        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }

        foreach (Button item in productionButtons)
        {
            item.gameObject.SetActive(false);
        }
        SelectableEntity[] array = FindObjectsOfType<SelectableEntity>();
        harvestableResources = new SelectableEntity[array.Length];
        int j = 0;
        for (int i = 0; i < array.Length; i++)
        {
            if (array[i] != null && array[i].selfHarvestableType == SelectableEntity.ResourceType.Gold)
            {
                harvestableResources[j] = array[i];
                j++;
            }
        }
        selectedParent.SetActive(false);
        resourcesParent.SetActive(false);
    }
    private int indexer = 0;
    public SelectableEntity FindEntityFromObject(GameObject obj)
    {
        SelectableEntity entity = obj.GetComponent<SelectableEntity>();
        if (entity == null)
        {
            entity = obj.GetComponentInParent<SelectableEntity>();
        }
        if (entity == null)
        {
            entity = obj.GetComponentInChildren<SelectableEntity>();
        }
        return entity;
    }
    private void Update()
    {
        InitializePlayers();
        if (!playerHasWon) CheckIfAPlayerHasWon();
        CleanEntityLists();
    }
    private void CleanEntityLists()
    {
        if (allFactionEntities.Count > 0)
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
        }
        if (enemyEntities.Count > 0)
        {
            if (enemyIndexer >= Instance.enemyEntities.Count)
            {
                enemyIndexer = Instance.enemyEntities.Count - 1;
            }
            SelectableEntity entity = enemyEntities[enemyIndexer];
            if (entity == null || !entity.alive)
            {
                enemyEntities.RemoveAt(enemyIndexer);
            }
            enemyIndexer++;
            if (enemyIndexer >= enemyEntities.Count) enemyIndexer = 0;
        }
    }
    private int enemyIndexer = 0;
    public bool playerHasWon = false;
    private void InitializePlayers()
    {
        List<RTSPlayer> movedPlayers = new();
        foreach (RTSPlayer player in uninitializedPlayers)
        {
            if (player.inTheGame.Value == true)
            {
                initializedPlayers.Add(player);
                allPlayers.Add(player);
                movedPlayers.Add(player);
            }
        }
        foreach (RTSPlayer player in movedPlayers)
        {
            uninitializedPlayers.Remove(player);
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
