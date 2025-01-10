using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static TargetedEffects;

public class UnitAbilities : EntityAddon
{
    [SerializeField] private AbilityOptions abilityOptions;
    private FactionAbility queuedAbility;
    private List<AbilityOnCooldown> usedAbilities = new();

    public FactionAbility GetQueuedAbility()
    {
        return queuedAbility;
    }
    public FactionAbility[] GetAbilities()
    {
        return abilityOptions.abilities;
    }
    public List<AbilityOnCooldown> GetUsedAbilities()
    {
        return usedAbilities;
    }
    public bool CanUseAbility(FactionAbility ability)
    {
        if (abilityOptions == null) return false;
        for (int i = 0; i < abilityOptions.abilities.Length; i++)
        {
            if (abilityOptions.abilities[i].abilityName == ability.abilityName) return true; //if ability is in the used abilities list, then we still need to wait  
        }
        return false;
    }
    public void StartUsingAbility(FactionAbility ability)
    {
        queuedAbility = ability;
        Global.Instance.PlayMinionAbilitySound(ent);
        if (sm != null)
        {
            sm.SwitchState(StateMachineController.EntityStates.UsingAbility);
        }
    }
    public bool AbilityOffCooldown(FactionAbility ability)
    {
        for (int i = 0; i < usedAbilities.Count; i++)
        {
            Debug.Log("Checking ability:" + ability.abilityName + "against: " + usedAbilities[i].abilityName);
            if (usedAbilities[i].abilityName == ability.abilityName) return false; //if ability is in the used abilities list, then we still need to wait  
        }
        return true;
    }
    public void ActivateAbility(FactionAbility ability)
    {
        Debug.Log("Activating ability: " + ability.name);
        List<SelectableEntity> targetedEntities = new();
        foreach (TargetedEffects effect in ability.effectsToApply)
        {
            switch (effect.targets)
            {
                case TargetedEffects.Targets.Self:
                    if (!targetedEntities.Contains(ent))
                    {
                        targetedEntities.Add(ent);
                    }
                    break;
            }
            foreach (SelectableEntity target in targetedEntities)
            {
                //get current variable
                float variableToChange = 0;
                float secondVariable = 0;
                switch (effect.status) //set variable to change;
                {
                    case StatusEffect.MoveSpeed:
                        /*if (target.sm != null && target.sm.ai != null)
                        {
                            variableToChange = target.sm.ai.maxSpeed;
                        }*/
                        break;
                    case StatusEffect.AttackDuration:
                        if (target.sm != null)
                        {
                            //variableToChange = target.sm.attackDuration;
                            //secondVariable = target.sm.impactTime;
                        }
                        break;
                    case StatusEffect.HP:
                        variableToChange = target.currentHP.Value;
                        break;
                }
                float attackAnimMultiplier = 1;
                float moveSpeedMultiplier = 1;
                switch (effect.operation) //apply change to variable
                {
                    case TargetedEffects.Operation.Set:
                        variableToChange = effect.statusNumber;
                        secondVariable = effect.statusNumber;
                        break;
                    case TargetedEffects.Operation.Add:
                        variableToChange += effect.statusNumber;
                        secondVariable += effect.statusNumber;
                        break;
                    case TargetedEffects.Operation.Multiply:
                        variableToChange *= effect.statusNumber;
                        secondVariable *= effect.statusNumber;
                        attackAnimMultiplier /= effect.statusNumber;
                        moveSpeedMultiplier *= effect.statusNumber;
                        break;
                    case Operation.Divide: //use to halve attackDuration
                        variableToChange /= effect.statusNumber;
                        secondVariable /= effect.statusNumber;
                        attackAnimMultiplier *= effect.statusNumber;
                        moveSpeedMultiplier /= effect.statusNumber;
                        break;
                }
                switch (effect.status) //set actual variable to new variable
                {
                    case TargetedEffects.StatusEffect.MoveSpeed:
                        /*if (target.sm != null && target.sm.ai != null)
                        {
                            target.sm.ai.maxSpeed = variableToChange;
                            target.sm.animator.SetFloat("moveSpeedMultiplier", moveSpeedMultiplier); //if we are halving, double animation speed
                        }*/
                        break;
                    case TargetedEffects.StatusEffect.AttackDuration:
                        if (target.sm != null)
                        {
                            //target.sm.attackDuration = variableToChange;
                            //target.sm.impactTime = secondVariable;
                            //target.sm.animator.SetFloat("attackMultiplier", attackAnimMultiplier); //if we are halving, double animation speed
                        }
                        break;
                    case StatusEffect.HP:
                        variableToChange = Mathf.Clamp(variableToChange, 0, ent.maxHP);
                        ent.currentHP.Value = (short)variableToChange;
                        Debug.Log("setting hitpoints to: " + variableToChange);
                        break;
                    case StatusEffect.CancelInProgress:
                        //if target is ghost, full refund
                        //if construction in progress, half refund
                        if (ent.constructionBegun)
                        {
                            Global.Instance.localPlayer.AddGold(target.factionEntity.goldCost / 2);
                        }
                        else
                        {
                            Global.Instance.localPlayer.AddGold(target.factionEntity.goldCost);
                        }
                        target.DestroyThis();
                        break;
                    case StatusEffect.DestroyThis:
                        target.DestroyThis();
                        break;
                    case StatusEffect.ToggleGate:
                        ent.ToggleGate();
                        break;
                }
                if (effect.applyAsLingeringEffect)
                {
                    TargetedEffects newEffect = new() //NECESSARY to prevent modifying original class
                    {
                        targets = effect.targets,
                        status = effect.status,
                        expirationTime = effect.expirationTime,
                        operation = effect.operation,
                        statusNumber = effect.statusNumber
                    };
                    bool foundMatch = false;
                    foreach (TargetedEffects item in ent.appliedEffects) //extend matching effects
                    {
                        if (item != null && item.status == newEffect.status && item.operation == newEffect.operation
                            && item.statusNumber == effect.statusNumber && item.expirationTime < newEffect.expirationTime)
                        {
                            foundMatch = true;
                            item.expirationTime = newEffect.expirationTime;
                            break;
                        }
                    }
                    if (!foundMatch)
                    {
                        ent.appliedEffects.Add(newEffect);
                    }
                }

                if (!UsedSameNameAbility(ability)) //if this unit has not used this ability already, mark it as used
                {
                    AbilityOnCooldown newAbility = new()
                    {
                        abilityName = ability.abilityName,
                        cooldownTime = ability.cooldownTime,
                        shouldCooldown = ability.shouldCooldown,
                        visitBuildingToRefresh = ability.visitBuildingToRefresh,
                    };
                    usedAbilities.Add(newAbility);
                }
            }
        }
    }
    public bool UsedSameNameAbility(FactionAbility ability)
    {
        for (int i = 0; i < usedAbilities.Count; i++) //find the ability and set the cooldown
        {
            if (usedAbilities[i].abilityName == ability.abilityName)
            {
                return true;
            }
        }
        return false;
    }
    public void UpdateUsedAbilities()
    {
        for (int i = usedAbilities.Count - 1; i >= 0; i--)
        {
            if (usedAbilities[i].shouldCooldown)
            {
                usedAbilities[i].cooldownTime -= Time.deltaTime;
                if (usedAbilities[i].cooldownTime <= 0)
                {
                    usedAbilities.RemoveAt(i);
                }
            }
        }
    }
}
