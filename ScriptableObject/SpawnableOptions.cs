using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SpawnableOptions", menuName = "Faction/Spawnable Options", order = 1)]
public class SpawnableOptions : ScriptableObject
{
    public FactionUnit[] spawnables;
    //public int spawnableAtOnce = 1; //how many build queue units can be spawned at once 
}
