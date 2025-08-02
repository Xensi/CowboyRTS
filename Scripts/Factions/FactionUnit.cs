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

    public void Init(string name, int spawnTime, GameObject prefab, int goldCost, int popCost)
    {
        productionName = name; 
        spawnTimer = 0;
        maxSpawnTimeCost = spawnTime;
        prefabToSpawn = prefab;
        this.goldCost = goldCost;
        consumePopulationAmount = popCost;
    }

    public static FactionUnit CreateInstance(string name, int spawnTime, GameObject prefab, int goldCost, int popCost)
    {
        var data = CreateInstance<FactionUnit>();
        data.Init(name, spawnTime, prefab, goldCost, popCost);
        return data;
    }
}