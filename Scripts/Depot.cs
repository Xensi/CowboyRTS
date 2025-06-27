using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Entity;

public class Depot : EntityAddon
{ 
    //[SerializeField] private int maxDepositers = 1;
    public HowToFilterResources howToFilterResources = HowToFilterResources.BanResources;
    //Resources that cannot be deposited here; empty means that all resources are allowed
    public List<ResourceType> bannedResources;
    public List<ResourceType> allowedResources; //Resources that can be deposited here
    //by default, cannot self-deposit 
}
