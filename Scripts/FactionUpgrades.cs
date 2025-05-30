using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewUpgrade", menuName = "Faction/Upgrade", order = 0)]
[System.Serializable]
public class FactionUpgrades : ScriptableObject
{
    public string upgradeName = "Upgrade Name";
    public float timeToUpgrade = 0;
    public bool shouldCooldown = true; //should cooldown timer tick down?
    public GameObject onActivateParticles = null;
    public List<Effect> effectsToApply = new(); //effects to activate when this ability is used
}
