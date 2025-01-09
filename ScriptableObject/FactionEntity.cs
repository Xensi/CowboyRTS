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
    public int maxHP = 10;
    //public bool isKeystone = false; //if all keystone entities are destroyed, you lose
    public int consumePopulationAmount = 0;
    public int raisePopulationLimitBy = 0;
    public bool shouldHideInFog = true;
    public float visionRange = 7;
    //public AudioClip[] sounds;
    public UnitSoundsProfile soundProfile;
    public SelectableEntity.TeamBehavior teamType = SelectableEntity.TeamBehavior.OwnerTeam;
    //public SelectableEntity.EntityTypes entityType = SelectableEntity.EntityTypes.Generic; 

    public GameObject deathEffect;  

    [Header("Optional Behavior")]
    [SerializeField, HideInInspector] public StateMachineController.AttackType attackType = StateMachineController.AttackType.None;
    [SerializeField, HideInInspector] public sbyte damage = 1;
    [SerializeField, HideInInspector] public bool directionalAttack = false;
    [SerializeField, HideInInspector] public float attackRange = 1;
    [SerializeField, HideInInspector] public float attackDuration = 1;
    [SerializeField, HideInInspector] public float impactTime = 0.5f;
    [SerializeField, HideInInspector] public float areaOfEffectRadius = 1;
    [SerializeField, HideInInspector] public bool shouldAggressivelySeekEnemies = false; 

    [SerializeField, HideInInspector] public bool isHarvester = false;
    [SerializeField, HideInInspector] public int harvestCapacity = 5;
    [SerializeField, HideInInspector] public float depositRange = 1;

    [SerializeField, HideInInspector] public bool expandGarrisonOptions = false;
    [SerializeField, HideInInspector] public bool passengersAreTargetable = false;
    [SerializeField, HideInInspector] public bool acceptsHeavy = false;
    [SerializeField, HideInInspector] public Projectile attackProjectilePrefab;
    public bool hideModelOnDeath = false;

    public bool IsHarvester()
    {
        return isHarvester;
    }  
}
