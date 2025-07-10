using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelManager : MonoBehaviour
{
    public static LevelManager instance { get; private set; }

    private Level currentlyLoadedLevel;
    private Level levelToLoad;
    [SerializeField] private bool levelStarted = false;
    public void SetLevelStarted(bool val)
    {
        levelStarted = val;
    }
    public bool GetLevelStarted()
    {
        return levelStarted;
    }

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
    Action unloadAction;

    public void GeneralLoadLevel(Level level)
    {
        Debug.Log("Beginning level load");
        levelToLoad = level;
        if (currentlyLoadedLevel != null) //unload level. when finished, load cutscene/level
        {
            UnloadLevel(currentlyLoadedLevel, ContinueGeneralLoadLevel);
        }
        else
        {
            ContinueGeneralLoadLevel();
        }
    }
    private void ContinueGeneralLoadLevel()
    {
        Debug.Log("Continuing");
        Level level = levelToLoad;

        if (level.hasIntro)
        {
            CutsceneManager.Instance.LoadCutscene(level);
        }
        else
        {
            LoadLevel(level, GeneralLoadLevelFinished);
        }
    }
    /// <summary>
    /// Load level without cutscene.
    /// </summary>
    /// <param name="level"></param>
    public void FastLoadLevel(Level level)
    {
        Debug.Log("Fast level load");
        levelToLoad = level;
        if (currentlyLoadedLevel != null) //unload level. when finished, load cutscene/level
        {
            UnloadLevel(currentlyLoadedLevel, ContinueFastLoadLevel);
        }
        else
        {
            ContinueFastLoadLevel();
        }
    }
    private void ContinueFastLoadLevel()
    {
        Level level = levelToLoad;
        LoadLevel(level, GeneralLoadLevelFinished);
    }
    private void UnloadLevel(Level level, Action callback = null)
    {
        Debug.Log("Unloading level");
        SceneManager.UnloadSceneAsync(level.name);
        if (callback != null) unloadAction = callback;
        currentlyLoadedLevel = null;
    }
    public void LoadLevel(Level level, Action callback = null)
    {
        SetLevelStarted(false);
        currentlyLoadedLevel = level;
        SceneManager.LoadSceneAsync(level.name, LoadSceneMode.Additive);
        if (callback != null) loadAction = callback;
    }
    public void LoadLevelDuringCutscene(Level level)
    {
        Debug.Log("Loading level during cutscene");
        //EndCurrentLevel();
        FastLoadLevel(level);
    }
    private void GeneralLoadLevelFinished()
    {
        Scene scene = SceneManager.GetSceneByName(currentlyLoadedLevel.name);
        SceneManager.SetActiveScene(scene);
        UpdateLevelObjective();
        if (!CutsceneManager.Instance.Playing())
        {
            BeginLevelGameplay();
        }
    }
    public void BeginLevelGameplay()
    {
        UIManager.instance.ShowGameUI();
        //SetLevelStarted(true);
    }
    public void UpdateLevelObjective()
    {
        string obj = currentlyLoadedLevel.goal;
        UIManager.instance.UpdateLevelObjective(obj);
    }
    void OnEnable()
    { 
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) //runs when scene is loaded
    {
        loadAction?.Invoke();
        loadAction = null;
    }
    void OnSceneUnloaded(Scene scene) //runs when scene is loaded
    {
        unloadAction?.Invoke();
        unloadAction = null;
    }
    void OnDisable()
    { 
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }
}
