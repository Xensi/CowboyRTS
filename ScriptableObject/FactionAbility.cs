using System;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;
[CreateAssetMenu(fileName = "NewAbility", menuName = "Faction/Ability", order = 0)]
[System.Serializable]
public class FactionAbility : ScriptableObject
{
    public string abilityName = "Ability Name";
    public bool usableOnlyWhenBuilt = true;
    public float cooldownTime = 60; //used only if needsConstructing is false
    public bool shouldCooldown = true; //should cooldown timer tick down?
    public GameObject particles = null;
    public List<BuildingAndCost> visitBuildingToRefresh = new(); //can we visit a building to refresh this ability?
    public List<TargetedEffects> effectsToApply = new(); //effects to activate when this ability is used
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
public class TargetedEffects //name, target, status effect
{ 
    public enum Targets
    {
        Self,
    }
    public enum StatusEffect
    {
        MoveSpeed, AttackDuration, HP, CancelInProgress, ToggleGate, DestroyThis
    }
    public enum Operation //how to apply status number
    {
        Set, Add, Multiply, Divide
    }
    public Targets targets = Targets.Self;
    public StatusEffect status = StatusEffect.MoveSpeed;
    public bool applyAsLingeringEffect = true; //should effect be applied as lingering status?

    public float expirationTime = 5; //how long will lingering effect last?
    public Operation operation = Operation.Set;
    public float statusNumber = 1; //number to apply operation with
    public bool repeatWhileLingering = false;
    public float repeatTime = 1; //how quickly should effect repeat?
    public GameObject particles = null;
}

[System.Serializable]
public class AppliedEffect //name, target, status effect
{
    public TargetedEffects.StatusEffect status = TargetedEffects.StatusEffect.MoveSpeed;
    public bool applyAsLingeringEffect = true; //should effect be applied as lingering status?

    public float expirationTime = 5; //how long will lingering effect last?
    public TargetedEffects.Operation operation = TargetedEffects.Operation.Set;
    public float statusNumber = 1; //number to apply operation with
    public bool repeatWhileLingering = false;
    public float repeatTime = 1; //how quickly should effect repeat?
    [HideInInspector] public float internalTimer = 0;
    public GameObject particles = null;
}