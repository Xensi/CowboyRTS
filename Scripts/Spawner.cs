using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Spawner : EntityAddon
{
    [SerializeField] private SpawnableOptions spawnableOptions;
    public FactionUnit[] GetSpawnables()
    {
        return spawnableOptions.spawnables;
    }
}
