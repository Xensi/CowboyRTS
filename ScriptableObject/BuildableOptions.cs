using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BuildableOptions", menuName = "Faction/Buildable Options", order = 1)]
public class BuildableOptions : ScriptableObject
{
    public FactionBuilding[] buildables;
}
