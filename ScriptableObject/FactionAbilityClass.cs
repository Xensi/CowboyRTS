using System;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;
[System.Serializable]
public class FactionAbilityClass
{
    public string abilityName = "Ability Name";   
    public int cooldownTime = 60; //used only if needsConstructing is false 
    public List<TargetedEffects> effectsToApply = new(); //effects to activate when this ability is used
}

[System.Serializable]
public class TargetedEffects //name, target, status effect
{
    public string name = "Targeted Effect Name";
    public enum Targets
    {
        Self,
    }
    public Targets targets = Targets.Self;
    public enum StatusEffect
    {
        MoveSpeed, AttackSpeed
    }
    public StatusEffect status = StatusEffect.MoveSpeed;
    public enum Operation //how to apply status number
    {
        Set, Add, Multiply
    }
    public Operation operation = Operation.Set;
    public float statusNumber = 1;
}
