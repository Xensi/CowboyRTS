using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "UpgradeOptions", menuName = "Faction/Upgrade Options", order = 2)]
public class UpgradeOptions : ScriptableObject
{
    public FactionUpgrades[] upgrades;
}
