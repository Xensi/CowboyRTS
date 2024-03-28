using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewFaction", menuName = "Faction/Faction", order = 1)]
public class Faction : ScriptableObject
{
    public List<FactionEntity> spawnableEntities = new(); //includes units and buildings 
}