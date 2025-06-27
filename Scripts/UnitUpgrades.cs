using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitUpgrades : EntityAddon
{
    [SerializeField] private UpgradeOptions staticUpgradeOptions;
    [SerializeField] private List<FactionUpgrade> upgrades;

    private void Start()
    {
        //populate upgrades with copies so we don't modify original object
        if (staticUpgradeOptions.upgrades.Length > 0)
        {
            foreach (FactionUpgrade item in staticUpgradeOptions.upgrades)
            {
                if (item == null) continue;
                FactionUpgrade newUpgrade = Instantiate(item);
                upgrades.Add(newUpgrade);
            }
        }
    }
    public List<FactionUpgrade> GetUpgrades()
    {
        return upgrades;
    }
    public bool CanUseUpgrade(FactionUpgrade upgrade)
    {
        if (upgrades == null) return false;
        for (int i = 0; i < upgrades.Count; i++)
        {
            if (upgrades[i].name == upgrade.name && upgrades[i].uses > 0) return true;
            //if ability is in the used abilities list, then we still need to wait  
        }
        return false;
    }

    public void ActivateUpgrade(FactionUpgrade upgrade)
    {
        
        Debug.Log("Activating upgrade");
        Debug.Log(upgrade.uses);
        upgrade.uses--;
        Debug.Log(upgrade.uses);
        Entity target = ent;
        foreach (Stats stats in upgrade.addStats)
        {
            if (target == null) return;
            target.UpdateStats(stats);
        }
        target.UpdateSpawnables(upgrade.unlockedSpawnables);
    }
}
