using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEditor.ObjectChangeEventStream;

public class Builder : EntityAddon
{
    [SerializeField] private BuildableOptions buildableOptions;  
    public FactionBuilding[] GetBuildables()
    {
        return buildableOptions.buildables; 
    }
}
