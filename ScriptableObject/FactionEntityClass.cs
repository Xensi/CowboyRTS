using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[System.Serializable]
public class FactionEntityClass
{
    public string productionName = "not produced";
    public bool needsConstructing = true;
    public GameObject prefabToSpawn;
    public int goldCost = 0;
}
