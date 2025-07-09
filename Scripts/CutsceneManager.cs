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

    public void LoadCutscene(int levelNum)
    {
        string level = LevelManager.instance.GetLevelName(levelNum) + "c";
        SceneManager.LoadScene(level, LoadSceneMode.Additive);
        UIManager.instance.HideAllUI();
    }
    public void EndCutscene(int levelNum)
    {
        string level = LevelManager.instance.GetLevelName(levelNum) + "c";
        SceneManager.UnloadSceneAsync(level);
        UIManager.instance.ChangeGameUIStatus(true);
        UIManager.instance.ChangeCamStatus(true);
    }
}
