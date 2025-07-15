using System.Collections.Generic;
using UnityEngine;
public class FactionEntity : ScriptableObject
{
    [Header("Core Information")]
    public string productionName = "Name";
    [TextArea(2, 4)]
    public string description = "";
    public GameObject prefabToSpawn;
    public int goldCost = 0;
    public ResourceQuantity[] costs;
    public int maxHP = 10;
    //public bool isKeystone = false; //if all keystone entities are destroyed, you lose
    public int consumePopulationAmount = 0;
    public int raisePopulationLimitBy = 0;
    public bool shouldHideInFog = true;
    public float visionRange = 7;
    //public AudioClip[] sounds;
    public UnitSoundsProfile soundProfile;
    public Entity.TeamBehavior teamType = Entity.TeamBehavior.OwnerTeam;
    //public SelectableEntity.EntityTypes entityType = SelectableEntity.EntityTypes.Generic; 

    public GameObject deathEffect;  

    [Header("Optional Behavior")]

    [SerializeField, HideInInspector] public bool isHarvester = false;
    [SerializeField, HideInInspector] public int harvestCapacity = 5;
    [SerializeField, HideInInspector] public float depositRange = 1;

    [SerializeField, HideInInspector] public bool expandGarrisonOptions = false;
    [SerializeField, HideInInspector] public bool passengersAreTargetable = false;
    [SerializeField, HideInInspector] public bool acceptsHeavy = false;
    public bool hideModelOnDeath = false;

    public bool IsHarvester()
    {
        return isHarvester;
    }  
}
