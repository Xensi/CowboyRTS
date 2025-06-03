using System;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;
[CreateAssetMenu(fileName = "NewAbility", menuName = "Faction/Ability", order = 0)]
[System.Serializable]
public class FactionAbility : ScriptableObject
{
    public new string name = "Ability Name";
    public bool usableOnlyWhenBuilt = true;
    public float cooldownTime = 60; //used only if needsConstructing is false
    public bool shouldCooldown = true; //should cooldown timer tick down?
    public GameObject onActivateParticles = null;
    public List<BuildingAndCost> visitBuildingToRefresh = new(); //can we visit a building to refresh this ability?
    public List<Effect> effectsToApply = new(); //effects to activate when this ability is used
}

[System.Serializable]
public class BuildingAndCost
{
    public FactionBuilding building;
    public int cost;
}
[System.Serializable]
public class AbilityOnCooldown
{ 
    public string abilityName = "Ability Name";
    public float cooldownTime = 60; //used only if needsConstructing is false 
    public bool shouldCooldown = true; //should cooldown timer tick down?
    public float repeatTime = 0;
    public List<BuildingAndCost> visitBuildingToRefresh = new(); //can we visit a building to refresh this ability?
}

[System.Serializable]
public class Effect
{
    public enum StatusEffect
    {
        MoveSpeed, AttackDuration, HP, CancelInProgress, ToggleGate
    }
    public enum Operation //how to apply status number
    {
        Set, Add, Multiply, Divide
    }
    public StatusEffect status = StatusEffect.HP;

    public Operation operation = Operation.Set;
    public float statusNumber = 1; //number to apply operation with
    public GameObject particles = null;
    [HideInInspector] public float internalTimer = 0;

    public bool applyAsLingeringEffect = true; //should effect be applied as lingering status?
    public float expirationTime = 5; //how long will lingering effect last?
    public bool repeatWhileLingering = false;
    public float repeatTime = 1; //how quickly should effect repeat?
}
