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
}