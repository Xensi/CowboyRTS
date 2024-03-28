using UnityEngine;
[System.Serializable]
public class FactionEntityClass
{
    public string productionName = "not produced";
    public SelectableEntity prefabToSpawn;
    public int goldCost = 0;
    [Header("Unit Only")]
    public int timeCost = 5; //used only if needsConstructing is false
    [Header("Building Only")]
    public int linkedID = -1; //some buildings have a second part to them. ignore if -1
    public bool needsConstructing = true;
    [HideInInspector] public byte buildID = 0;
}
