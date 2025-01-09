using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StateMachineController;
using static UnityEditor.ObjectChangeEventStream;

public class Builder : EntityAddon
{
    [SerializeField] private BuildableOptions buildableOptions;  
    public FactionBuilding[] GetBuildables()
    {
        return buildableOptions.buildables; 
    }
    public void BuildingState()
    {

        if (InvalidBuildable(ent.interactionTarget) || !InRangeOfEntity(ent.interactionTarget, attackRange))
        {
            SwitchState(EntityStates.FindInteractable);
            lastMajorState = EntityStates.Building;
        }
        else
        {
            LookAtTarget(ent.interactionTarget.transform);
            if (attackReady)
            {
                animator.Play("Attack");

                if (AnimatorUnfinished())
                {
                    if (stateTimer < impactTime)
                    {
                        stateTimer += Time.deltaTime;
                    }
                    else if (attackReady)
                    {
                        stateTimer = 0;
                        attackReady = false;
                        BuildTarget(ent.interactionTarget);
                    }
                }
                else //animation finished
                {
                    AfterBuildCheck();
                }
            }
        }
    }
    public void WalkToBuildable()
    { 
        if (InvalidBuildable(ent.interactionTarget))
        {
            SwitchState(EntityStates.FindInteractable);
        }
        else
        {
            if (InRangeOfEntity(ent.interactionTarget, attackRange))
            {
                SwitchState(EntityStates.Building);
            }
            else
            {
                animator.Play("Walk");
                Vector3 closest = ent.interactionTarget.physicalCollider.ClosestPoint(transform.position);
                SetDestinationIfHighDiff(closest);
                //if (IsOwner) SetDestination(closest);//destination.Value = closest;
                /*selectableEntity.interactionTarget.transform.position;*/
            }
        }
    }
    private void AfterBuildCheck()
    { 
        animator.Play("Idle");
        if (InvalidBuildable(ent.interactionTarget))
        {
            SwitchState(EntityStates.FindInteractable);
            lastMajorState = EntityStates.Building;
        }
        else
        {
            SwitchState(EntityStates.Building);
        }
    }
}
