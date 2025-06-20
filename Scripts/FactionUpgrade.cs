using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scriptable object that defines a specific upgrade.
/// </summary>
[CreateAssetMenu(fileName = "NewUpgrade", menuName = "Faction/Upgrade", order = 0)]
[System.Serializable]
public class FactionUpgrade : ScriptableObject
{
    public new string name = "Upgrade Name";
    public float timeToUpgrade = 0;
    public List<ResourceQuantity> costs;
    public SpawnableOptions unlockedSpawnables; // new spawnables
    public List<Stats> addStats; // stats to add
    public int uses = 1;
    // transform into another unit
    //public GameObject upgradingParticles = null;
    //public GameObject upgradeFinishedParticles = null;
}
