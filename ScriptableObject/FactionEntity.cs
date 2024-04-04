using System.Collections.Generic;
using UnityEngine;

public class FactionEntity : ScriptableObject
{
    public string productionName = "Name";
    [TextArea(2, 4)]
    public string description = "";
    public int goldCost = 0;
    public GameObject prefabToSpawn; 
    public int maxHP = 10; 
    public FactionUnit[] spawnableUnits;
    public FactionBuilding[] constructableBuildings;
    public FactionAbility[] usableAbilities;

    public SelectableEntity.TeamBehavior teamType = SelectableEntity.TeamBehavior.OwnerTeam;
    public SelectableEntity.DepositType depositType = SelectableEntity.DepositType.None;
    public SelectableEntity.EntityTypes entityType = SelectableEntity.EntityTypes.Generic;
    public SelectableEntity.ResourceType selfHarvestableType = SelectableEntity.ResourceType.None;
    public bool isHarvester = false;
    public int harvestCapacity = 5;

    public bool isKeystone = false; //if all keystone entities are destroyed, you lose
    public int spawnableAtOnce = 1; //how many build queue units can be spawned at once

    public int allowedUnfinishedInteractors = 1;
    public int allowedFinishedInteractors = 1;

    public bool passengersAreTargetable = false;
    public bool acceptsHeavy = false;

    public int consumePopulationAmount = 1;
    public int raisePopulationLimitBy = 0;
    public bool shouldHideInFog = true;
    //[HideInInspector] public byte buildID = 0;
}
