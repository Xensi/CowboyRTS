using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "AbilityOptions", menuName = "Faction/Ability Options", order = 1)]
public class AbilityOptions : ScriptableObject
{
    public FactionAbility[] abilities;
}
