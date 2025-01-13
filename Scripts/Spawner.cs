using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Spawner : EntityAddon
{
    [SerializeField] private SpawnableOptions spawnableOptions;

    FactionUnit[] spawnables;
    public override void InitAddon()
    {
        spawnables = new FactionUnit[0];
        if (spawnableOptions != null)
        {
            spawnables = spawnableOptions.spawnables; 
        }
    }
    public FactionUnit[] GetSpawnables()
    {
        return spawnables;
    }
}
