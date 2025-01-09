using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "HarvesterSettings", menuName = "Faction/HarvesterSettings", order = 0)]

public class HarvesterSettings : ScriptableObject
{
    public int bagSize = 5;
    public int amountToHarvestPerSwing = 1; 
    public float interactRange = .75f;
    public float impactTime = 0.5f;
    public float duration = 1;  
    public List<ResourceType> allowedResources; //Resources we can harvest
} 