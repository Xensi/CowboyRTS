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
    public List<TargetedEffects> effectsToApply = new(); //effects to activate when this ability is used
}

[System.Serializable]
public class AbilityOnCooldown
{ 
    public string abilityName = "Ability Name";
    public float cooldownTime = 60; //used only if needsConstructing is false 
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
        MoveSpeed, AttackSpeed, HP, CancelInProgress
    }
    public enum Operation //how to apply status number
    {
        Set, Add, Multiply, Divide
    }
    public Targets targets = Targets.Self;
    public StatusEffect status = StatusEffect.MoveSpeed;
    public bool applyAsLingeringEffect = true; //should effect be applied as lingering status?

    public float expirationTime = 5; 
    public Operation operation = Operation.Set;
    public float statusNumber = 1; //number to apply operation with
}
