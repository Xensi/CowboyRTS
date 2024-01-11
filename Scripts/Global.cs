using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class Global : NetworkBehaviour
{
    public static Global Instance { get; private set; }
    public RectTransform selectionRect;
    public List<Material> colors;
    public List<Color> teamColors;
    public List<Transform> playerSpawn;
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
    public List<RTSPlayer> playerList = new();
    public TMP_Text popText;
    private void Awake()
    {
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
            if (array[i] != null && array[i].harvestType == SelectableEntity.HarvestType.Gold)
            {
                harvestableResources[j] = array[i];
                j++;
            }
        }
        selectedParent.SetActive(false);
        resourcesParent.SetActive(false);
    }
    private void Update()
    {
        if (!playerHasWon) CheckIfAPlayerHasWon();
    }
    public bool playerHasWon = false;
    public void CheckIfAPlayerHasWon()
    {
        if (playerList.Count <= 1) return;
        RTSPlayer potentialWinner = null;
        int inTheGameCount = 0;
        foreach (RTSPlayer item in playerList)
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
