using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Adding this component to an entity marks it as a resource that can be harvested.
/// </summary>
public class Ore : EntityAddon
{ 
    public ResourceType resourceType = ResourceType.Gold;
    //private int currentResourceCount = 100; //automatically set to max
    //private int maxResourceCount = 100;
    /*private enum DepletionBehavior //what to do when the currentResourceCount hits 0
    {
        Nothing,
        DestroyThis
    }*/
    //[SerializeField] private DepletionBehavior depletionBehavior = DepletionBehavior.Nothing;  
    public FactionOre oreFac;
    public List<Harvester> harvestersWithClaimsOnThis;

    public int GetMaxHarvesters()
    {
        return oreFac.maxHarvesters;
    }
    public override void InitAddon()
    {
        if (ent == null) Debug.LogError("Ent missing");
        oreFac = ent.factionEntity as FactionOre;
        if (oreFac == null)
        {
            Debug.LogError(ent.name + " faction entity needs to be ore");
            return;
        }
        ent.allowedWorkers = oreFac.maxHarvesters;
    }
}
