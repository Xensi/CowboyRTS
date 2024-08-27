using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }
    public readonly string LEVEL1 = "Level1";
    // Start is called before the first frame update
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
    Action loadAction;
    public void LoadLevel(string level, Action callback)
    {
        SceneManager.LoadScene(level, LoadSceneMode.Additive);
        loadAction = callback;
    } 
    void OnEnable()
    { 
        SceneManager.sceneLoaded += OnSceneLoaded;
    } 
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
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
