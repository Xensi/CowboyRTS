using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static StateMachineController;
using static UnitAnimator;
using static SoundTypes;

public class Builder : SwingEntityAddon
{
    [SerializeField] private BuildableOptions buildableOptions;
    FactionBuilding[] buildables;
    public override void InitAddon()
    {
        buildables = new FactionBuilding[0];
        if (buildableOptions != null)
        {
            buildables = buildableOptions.buildables;
            swingDelta = buildableOptions.amountToBuildPerSwing;
            range = buildableOptions.interactRange;
            impactTime = buildableOptions.impactTime;
            duration = buildableOptions.duration;
        }
    }
    public FactionBuilding[] GetBuildables()
    {
        return buildables;
    }
    private bool InvalidBuildable(Entity target)
    {
        return target == null || target.initialized && target.fullyBuilt && !target.IsDamaged() || target.alive == false;
    }
    public void BuildingState()
    {
        Entity interactionTarget = ent.GetInteractionTarget();
        if (InvalidBuildable(interactionTarget) || !sm.InRangeOfEntity(interactionTarget, range))
        {
            SwitchState(EntityStates.FindInteractable);
            SetLastMajorState(EntityStates.Building);
        }
        else
        {
            sm.LookAtTarget(interactionTarget.transform);
            if (ready)
            {
                anim.Play(ATTACK);

                if (anim.InProgress())
                {
                    if (sm.stateTimer < impactTime)
                    {
                        sm.stateTimer += Time.deltaTime;
                    }
                    else if (ready)
                    {
                        sm.stateTimer = 0;
                        ready = false;
                        BuildTarget(interactionTarget);
                    }
                }
                else //animation finished
                {
                    AfterBuildCheck();
                }
            }
        }
    }

    public void BuildTarget(Entity target) //since hp is a network variable, changing it on the server will propagate changes to clients as well
    {
        //fire locally
        ent.SimplePlaySound(HitSound);

        if (target != null)
        {
            if (IsServer)
            {
                target.RaiseHP(swingDelta);
            }
            else //client tell server to change the network variable
            {
                RequestBuildServerRpc(swingDelta, target);
            }
        }
    }
    [ServerRpc]
    private void RequestBuildServerRpc(sbyte buildDelta, NetworkBehaviourReference target)
    {
        //server must handle damage! 
        if (target.TryGet(out Entity select))
        {
            select.RaiseHP(buildDelta);
        }
    }
    public void WalkToBuildable()
    {
        Entity interactionTarget = ent.GetInteractionTarget();
        if (InvalidBuildable(interactionTarget))
        {
            SwitchState(EntityStates.FindInteractable);
        }
        else
        {
            if (InRangeOfEntity(interactionTarget, range))
            {
                SwitchState(EntityStates.Building);
            }
            else
            {
                anim.Play(WALK);
                Vector3 closest = interactionTarget.physicalCollider.ClosestPoint(transform.position);
                pf.SetDestinationIfHighDiff(closest);
            }
        }
    }
    private void AfterBuildCheck()
    {
        Entity interactionTarget = ent.GetInteractionTarget();
        anim.Play(IDLE); 
        if (InvalidBuildable(interactionTarget))
        {
            SwitchState(EntityStates.FindInteractable);
            SetLastMajorState(EntityStates.Building);
        }
        else
        {
            SwitchState(EntityStates.Building);
        }
    }
}
