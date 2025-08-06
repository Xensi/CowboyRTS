using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scriptable object that defines the upgrades available to a unit.
/// </summary>
[CreateAssetMenu(fileName = "UpgradeOptions", menuName = "Faction/Upgrade Options", order = 2)]
public class UpgradeOptions : ScriptableObject
{
    public FactionUpgrade[] upgrades;
}
