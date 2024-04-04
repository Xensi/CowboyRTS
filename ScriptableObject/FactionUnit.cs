using UnityEngine;
[CreateAssetMenu(fileName = "NewUnit", menuName = "Faction/Unit", order = 0)]
[System.Serializable]
public class FactionUnit : FactionEntity
{
    public int timeCost = 5; //time it takes to spawn the unit
    public bool isHeavy = false;
    public bool canAttackWhileMoving = false;

}