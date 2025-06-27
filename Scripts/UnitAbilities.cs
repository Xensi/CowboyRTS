using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Effect;

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
            if (abilityOptions.abilities[i].name == ability.name) return true;
            //if ability is in the used abilities list, then we still need to wait  
        }
        return false;
    }
    public void StartUsingAbility(FactionAbility ability)
    {
        //Debug.Log("Starting ability as minion");
        queuedAbility = ability;
        Global.instance.PlayMinionAbilitySound(ent);
        ActivateAbility(GetQueuedAbility());
        if (sm != null)
        {
            sm.SwitchState(StateMachineController.EntityStates.UsingAbility);
        }
    }
    public bool AbilityOffCooldown(FactionAbility ability)
    {
        for (int i = 0; i < usedAbilities.Count; i++)
        {
            //Debug.Log("Checking ability:" + ability.abilityName + "against: " + usedAbilities[i].abilityName);
            if (usedAbilities[i].abilityName == ability.name) return false; //if ability is in the used abilities list, then we still need to wait  
        }
        return true;
    }
    public void ActivateAbility(FactionAbility ability)
    {
        foreach (Effect effect in ability.effectsToApply)
        {
            Effect newEffect = new() //NECESSARY to prevent modifying original class
            {
                status = effect.status,
                expirationTime = effect.expirationTime,
                operation = effect.operation,
                statusNumber = effect.statusNumber,
                repeatTime = effect.repeatTime,
                repeatWhileLingering = effect.repeatWhileLingering,
                particles = effect.particles,
            };
            SelectableEntity target = ent;
            if (target == null) return;
            //on activate, play particles
            if (ability.onActivateParticles != null) Instantiate(ability.onActivateParticles, target.transform);
            target.ApplyEffect(newEffect);

            if (effect.applyAsLingeringEffect)
            {
                //if effect is already applied to entity, extend it
                bool foundMatch = false;
                foreach (Effect item in ent.appliedEffects) //extend matching effects
                {
                    if (item != null && item.status == newEffect.status && item.operation == newEffect.operation
                        && item.statusNumber == effect.statusNumber && item.expirationTime < newEffect.expirationTime)
                    {
                        foundMatch = true;
                        item.expirationTime = newEffect.expirationTime;
                        break;
                    }
                }
                //otherwise add it
                if (!foundMatch)
                {
                    ent.appliedEffects.Add(newEffect);
                }
            }

            if (!UsedSameNameAbility(ability)) //if this unit has not used this ability already, mark it as used
            {
                AbilityOnCooldown newAbility = new()
                {
                    abilityName = ability.name,
                    cooldownTime = ability.cooldownTime,
                    shouldCooldown = ability.shouldCooldown,
                    visitBuildingToRefresh = ability.visitBuildingToRefresh,
                };
                usedAbilities.Add(newAbility);
            }
        }
    }
    public bool UsedSameNameAbility(FactionAbility ability)
    {
        for (int i = 0; i < usedAbilities.Count; i++) //find the ability and set the cooldown
        {
            if (usedAbilities[i].abilityName == ability.name)
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
