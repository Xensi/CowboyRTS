using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Spawner : EntityAddon
{
    [SerializeField] private SpawnableOptions spawnableOptions;

    List<FactionUnit> spawnables = new();
    public override void InitAddon()
    {
        if (spawnableOptions != null)
        {
            spawnables = spawnableOptions.spawnables.ToList();
        }
    }
    public void UpdateSpawnables(SpawnableOptions options)
    {
        foreach (FactionUnit item in options.spawnables)
        {
            if (!spawnables.Contains(item)) spawnables.Add(item);
        }
    }
    public List<FactionUnit> GetSpawnables()
    {
        return spawnables;
    }
}
