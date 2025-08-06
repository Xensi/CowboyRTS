using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CutsceneManager : MonoBehaviour
{
    #region Instancing
    public static CutsceneManager Instance { get; private set; }
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }
    #endregion

    Action unloadAction;
    void OnEnable()
    {
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }
    void OnSceneUnloaded(Scene scene) //runs when scene is loaded
    {
        unloadAction?.Invoke();
        unloadAction = null;
    }
    void OnDisable()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }
    public string LevelCutsceneName(Level level)
    {
        return level.name + "c";
    }
    private bool cutscenePlaying = false;
    public void LoadCutscene(Level level)
    {
        Debug.Log("Loading cutscene");
        SceneManager.LoadSceneAsync(LevelCutsceneName(level), LoadSceneMode.Additive);
        UIManager.instance.HideAllUI();
        cutscenePlaying = true;
    }
    public void EndCutscene(Level level)
    {
        SceneManager.UnloadSceneAsync(LevelCutsceneName(level));
        unloadAction = LevelManager.instance.BeginLevelGameplay; // show UI on unload
        cutscenePlaying = false;
    }
    public bool Playing()
    {
        return cutscenePlaying;
    }
}
