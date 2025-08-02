using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(fileName = "NewEntity", menuName = "Faction/Entity", order = 0)]
[System.Serializable]
public class FactionEntity : ScriptableObject
{
    [Header("Core Information")]
    public string productionName = "Name";
    [TextArea(2, 4)]
    public string description = "";
    public int maxHP = 10;
    public GameObject deathEffect;

    [Header("For spawnables/buildables")]
    public GameObject prefabToSpawn;
    public int goldCost = 0;
    public ResourceQuantity[] costs;

    public int consumePopulationAmount = 0;
    public int raisePopulationLimitBy = 0;

    public bool shouldHideInFog = true; //have this determined by if it is a building or unit
    public float visionRange = 7;
    public UnitSoundsProfile soundProfile;
    //public Entity.TeamBehavior teamType = Entity.TeamBehavior.OwnerTeam;

    [Header("Optional Behavior")]

    //[SerializeField, HideInInspector] public bool expandGarrisonOptions = false;
    //[SerializeField, HideInInspector] public bool passengersAreTargetable = false;
    //[SerializeField, HideInInspector] public bool acceptsHeavy = false;
    public bool hideModelOnDeath = false;
}
