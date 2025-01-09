using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

/// <summary>
/// Adding this component to an entity marks it as a resource that can be harvested.
/// </summary>
public class Ore : EntityAddon
{ 
    public ResourceType resourceType = ResourceType.Gold;
    //private int currentResourceCount = 100; //automatically set to max
    //private int maxResourceCount = 100;
    [SerializeField] private int maxHarvesters = 1;
    /*private enum DepletionBehavior //what to do when the currentResourceCount hits 0
    {
        Nothing,
        DestroyThis
    }*/
    //[SerializeField] private DepletionBehavior depletionBehavior = DepletionBehavior.Nothing;  
}
