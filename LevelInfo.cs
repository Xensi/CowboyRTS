using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LevelInfo : MonoBehaviour
{
    public static LevelInfo Instance { get; private set; }
    public List<Transform> spawnsList; //places camera
    public bool overrideDefaultValues = false;
    public int goldValueOverride = 0;
    public int startMaxPopOverride = 0;
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
} 