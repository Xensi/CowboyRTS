using UnityEngine;
/// <summary>
/// Ores are placed on the map
/// </summary>
[CreateAssetMenu(fileName = "NewOre", menuName = "Faction/Ore", order = 0)]
[System.Serializable]
public class FactionOre : FactionEntity
{
    public int maxHarvesters = 1;
}
