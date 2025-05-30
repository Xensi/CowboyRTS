using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewUpgrade", menuName = "Faction/Upgrade", order = 0)]
[System.Serializable]
public class FactionUpgrades : ScriptableObject
{
    public string upgradeName = "Upgrade Name";
    public float timeToUpgrade = 0;
    public List<ResourceQuantity> costs;
    public SpawnableOptions unlockedSpawnables; // new spawnables
    public List<Stats> addStats; // stats to add
    // transform into another unit
    //public GameObject upgradingParticles = null;
    //public GameObject upgradeFinishedParticles = null;
}
