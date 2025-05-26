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
            AppliedEffect newEffect = new() //NECESSARY to prevent modifying original class
            {
                status = effect.status,
                expirationTime = effect.expirationTime,
                operation = effect.operation,
                statusNumber = effect.statusNumber,
                repeatTime = effect.repeatTime,
                repeatWhileLingering = effect.repeatWhileLingering,
                particles = effect.particles,
            };
            //get targets
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
                //on use particles
                Instantiate(ability.particles, target.transform);

                target.ApplyEffect(newEffect);

                if (effect.applyAsLingeringEffect)
                {
                    //if effect is already applied to entity, extend it
                    bool foundMatch = false;
                    foreach (AppliedEffect item in ent.appliedEffects) //extend matching effects
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
