using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BuildableOptions", menuName = "Faction/Buildable Options", order = 1)]
public class BuildableOptions : ScriptableObject
{
    public FactionBuilding[] buildables;
    public sbyte amountToBuildPerSwing = 1;
    public float interactRange = .75f;
    public float impactTime = 0.5f;
    public float duration = 1;
}
