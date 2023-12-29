using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[System.Serializable]
public class FactionEntityClass
{
    public string productionName = "not produced";
    public bool needsConstructing = true;
    public GameObject prefabToSpawn;
    public int linkedID = -1; //some buildings have a second part to them. ignore if -1
    public int goldCost = 0;
    public int timeCost = 5;
    [HideInInspector] public byte buildID = 0;
}
