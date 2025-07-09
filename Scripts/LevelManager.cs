using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelManager : MonoBehaviour
{
    public static LevelManager instance { get; private set; }

    [SerializeField] private List<string> levelNames;
    public readonly string LEVEL1 = "Level1";
    // Start is called before the first frame update
    private void Awake()
    { 
        if (instance != null && instance != this)
        {
            Destroy(this);
        }
        else
        {
            instance = this;
        }
    }
    Action loadAction;
    public string GetLevelName(int levelNum)
    {
        return levelNames[levelNum];
    }
    public void LoadLevel(string level, Action callback)
    {
        SceneManager.LoadSceneAsync(level, LoadSceneMode.Additive);
        loadAction = callback;
    } 
    public void StartLevelNoFrills(int levelNum)
    {
        LobbyManager.Instance.StartSinglePlayerGame(levelNum);
        UIManager.instance.ChangeGameUIStatus(true);
        UIManager.instance.ChangeLobbyUIStatus(false);
        UIManager.instance.ChangeCamStatus(true);
    }
    void OnEnable()
    { 
        SceneManager.sceneLoaded += OnSceneLoaded;
    } 
    void OnSceneLoaded(Scene scene, LoadSceneMode mode) //runs when scene is loaded
    {
        //Debug.Log("OnSceneLoaded: " + scene.name);
        //Debug.Log(mode);
        loadAction?.Invoke();
    } 
    void OnDisable()
    { 
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}
