using UnityEngine;
[CreateAssetMenu(fileName = "NewUnit", menuName = "Faction/Unit", order = 0)]
[System.Serializable]
public class FactionUnit : FactionEntity
{
    [Header("Unit Properties")]
    public int maxSpawnTimeCost = 5; //time it takes to spawn the unit + 1 second to finish the job
    [HideInInspector] public float spawnTimer = 0;
    public bool isHeavy = false;
    //public bool canAttackWhileMoving = false;
    public float maxSpeed = 2f;

    /*public enum UnitTypes
    {
        Generic, //default  
    }*/

    public void Init(string name, int spawnTime, GameObject prefab, int goldCost)
    {
        productionName = name; 
        spawnTimer = 0;
        prefabToSpawn = prefab;
        this.goldCost = goldCost; 
    }

    public static FactionUnit CreateInstance(string name, int spawnTime, GameObject prefab, int goldCost)
    {
        var data = CreateInstance<FactionUnit>();
        data.Init(name, spawnTime, prefab, goldCost);
        return data;
    }
    public bool IsFighter()
    {
        return isHarvester == false;
    }
}