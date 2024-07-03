using UnityEngine;
[CreateAssetMenu(fileName = "NewUnit", menuName = "Faction/Unit", order = 0)]
[System.Serializable]
public class FactionUnit : FactionEntity
{
    [Header("Unit Properties")]
    public int spawnTimeCost = 5; //time it takes to spawn the unit
    public bool isHeavy = false;
    public bool canAttackWhileMoving = false;

    public enum UnitTypes
    {
        Generic, //default  
    }

    public void Init(string name, int spawnTime, GameObject prefab, int goldCost)
    {
        productionName = name;
        spawnTimeCost = spawnTime;
        prefabToSpawn = prefab;
        this.goldCost = goldCost; 
    }

    public static FactionUnit CreateInstance(string name, int spawnTime, GameObject prefab, int goldCost)
    {
        var data = CreateInstance<FactionUnit>();
        data.Init(name, spawnTime, prefab, goldCost);
        return data;
    }
}