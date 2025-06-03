using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitUpgrades : EntityAddon
{
    [SerializeField] private UpgradeOptions upgradeOptions;
    public FactionUpgrade[] GetUpgrades()
    {
        return upgradeOptions.upgrades;
    }
    public bool CanUseUpgrade(FactionUpgrade upgrade)
    {
        if (upgradeOptions == null) return false;
        for (int i = 0; i < upgradeOptions.upgrades.Length; i++)
        {
            if (upgradeOptions.upgrades[i].name == upgrade.name) return true; 
            //if ability is in the used abilities list, then we still need to wait  
        }
        return false;
    }

    public void ActivateUpgrade(FactionUpgrade upgrade)
    {
        Debug.Log("Activating upgrade");
        SelectableEntity target = ent;
        foreach (Stats stats in upgrade.addStats)
        {
            if (target == null) return;
            target.UpdateStats(stats);
        }
        target.UpdateSpawnables(upgrade.unlockedSpawnables);
    }
}
