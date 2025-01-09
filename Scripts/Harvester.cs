using FoW;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Netcode;
using UnityEngine;
using static StateMachineController;
using static UnitAnimator;
public enum ResourceType
{
    None,
    Gold,
    Wood,
    Cactus
}
public enum HowToFilterResources
{
    BanResources,
    AllowResources
}
public class Harvester : EntityAddon
{ 
    [SerializeField] private int amountToHarvestPerSwing = 1;
    [HideInInspector] public float depositRange = .75f;

    [SerializeField] private List<ResourceType> harvesterBag;
    [SerializeField] private int harvesterBagSize = 5;

    [SerializeField] private HowToFilterResources howToFilterResources = HowToFilterResources.BanResources;
    [SerializeField] private List<ResourceType> bannedResources; //Resources we can't harvest
    [SerializeField] private List<ResourceType> allowedResources; //Resources we can harvest


    //[SerializeField] private MeshRenderer[] resourceCollectingMeshes; //Add to this array to show meshes that indicate collected resources 
    /*private bool InvalidDeposit(SelectableEntity target) //Add a depot class that will have banned resources for depositing
    {
        return target == null || target.depositType == SelectableEntity.DepositType.None;
    }*/
    public void SetBagSize(int size)
    {
        harvesterBagSize = size;
    }
    public void WalkToDepot()
    { 
        if (!ValidDepositForHarvester(ent.interactionTarget))
        {
            SwitchState(EntityStates.FindInteractable);
        }
        else
        {
            if (sm.InRangeOfEntity(ent.interactionTarget, depositRange))
            {
                SwitchState(EntityStates.Depositing);
            }
            else
            {
                sm.animator.Play("Walk");
                Vector3 closest = ent.interactionTarget.physicalCollider.ClosestPoint(transform.position);
                sm.SetDestinationIfHighDiff(closest);
            }
        }
    }
    /// <summary>
    /// Is the target an valid harvestable resource?
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    public bool ValidOreForHarvester(SelectableEntity target)
    {
        bool val = false;
        if (target != null && target.ore != null && target.alive)
        {
            switch (howToFilterResources)
            {
                case HowToFilterResources.BanResources: //if the target's resource type is not in ban list, that's good
                    if (!bannedResources.Contains(target.ore.resourceType)) val = true;
                    break;
                case HowToFilterResources.AllowResources:
                    if (allowedResources.Contains(target.ore.resourceType)) val = true;
                    break;
                default:
                    break;
            }
        }
        return val;
    }
    public bool ValidDepositForHarvester(SelectableEntity targetDepot)
    {
        bool val = false;
        List<ResourceType> presentTypes = new();
        foreach (ResourceType item in harvesterBag)
        {
            if (!presentTypes.Contains(item)) presentTypes.Add(item);
        }
        if (targetDepot != null && targetDepot.depot != null && targetDepot.alive)
        {
            if (targetDepot.depot.howToFilterResources == HowToFilterResources.BanResources
                && targetDepot.depot.bannedResources.Count <= 0)
            {
                return true;
            }

            foreach (ResourceType type in presentTypes)
            {
                switch (targetDepot.depot.howToFilterResources)
                {
                    case HowToFilterResources.BanResources:
                        val = true; //assume true, but if in banlist, return false
                        if (targetDepot.depot.bannedResources.Contains(type)) return false;
                        break;
                    case HowToFilterResources.AllowResources: //if in allowed, that's good
                        if (targetDepot.depot.allowedResources.Contains(type))
                        {
                            return true;
                        }
                        break;
                    default:
                        break;
                }
            }
        }
        return val;
    }
    public void FindDepositState()
    { 
        if (ent.harvester.ValidDepositForHarvester(ent.interactionTarget))
        {
            SwitchState(EntityStates.WalkToInteractable);
        }
        else
        {
            ent.interactionTarget = FindClosestDeposit();
        }
    }
    public void FindHarvestableState()
    { 
        if (ent.harvester.ValidOreForHarvester(ent.interactionTarget))
        {
            SwitchState(EntityStates.WalkToInteractable);
        }
        else
        {
            ent.interactionTarget = FindClosestHarvestable();
        }
    }
    /// <summary>
    /// Returns closest harvestable resource with space for new harvesters.
    /// </summary>
    /// <returns></returns>
    private SelectableEntity FindClosestHarvestable()
    {
        //Debug.Log("Finding closest harvestable");
        FogOfWarTeam fow = FogOfWarTeam.GetTeam((int)ent.controllerOfThis.playerTeamID);
        List<Ore> oreList = ent.controllerOfThis.friendlyOres;

        SelectableEntity closest = null;
        float distance = Mathf.Infinity;
        foreach (Ore ore in oreList)
        {
            SelectableEntity item = ore.GetEntity();
            if (item != null && item.alive && fow.GetFogValue(item.transform.position) <= 0.5 * 255) //item is visible to some degree
            {
                if (item.workersInteracting.Count < item.allowedWorkers) //there is space for a new harvester
                {
                    float newDist = Vector3.SqrMagnitude(transform.position - item.transform.position);
                    if (newDist < distance)
                    {
                        closest = item;
                        distance = newDist;
                    }
                }
            }
        }
        return closest;
    }
    private SelectableEntity FindClosestDeposit() //right now resource agnostic
    {
        List<Depot> list = ent.controllerOfThis.ownedDepots;

        SelectableEntity closest = null;
        float distance = Mathf.Infinity;
        foreach (Depot depot in list)
        {
            SelectableEntity item = depot.GetEntity();
            if (item != null && item.fullyBuilt && item.alive && ValidDepositForHarvester(item))
            { 
                float newDist = Vector3.SqrMagnitude(transform.position - item.transform.position); //faster than .distance
                if (newDist < distance)
                {
                    closest = item;
                    distance = newDist;
                }
            }
        }
        return closest;
    }
    public bool BagHasSpace()
    {
        return harvesterBag.Count < harvesterBagSize;
        //return entity.harvestedResourceAmount >= entity.harvestCapacity;
    }
    private void SwitchState(EntityStates state)
    {
        if (sm != null) sm.SwitchState(state);
    }
    public void WalkToOre()
    {
        if (sm == null) return;
        if (!ValidOreForHarvester(ent.interactionTarget))
        {
            SwitchState(EntityStates.FindInteractable);
        }
        else
        {
            if (sm.InRangeOfEntity(ent.interactionTarget, sm.attackRange))
            {
                SwitchState(EntityStates.Harvesting);
            }
            else
            {
                sm.animator.Play("Walk");
                Vector3 closest = ent.interactionTarget.physicalCollider.ClosestPoint(transform.position);
                sm.SetDestinationIfHighDiff(closest);
            }
        }
    }
    private bool harvestOver = false;
    public void HarvestingState()
    {
        if (sm == null) return;
        if (!ValidOreForHarvester(ent.interactionTarget)) //invalid ore
        {
            SwitchState(EntityStates.FindInteractable);
            sm.SetLastMajorState(EntityStates.Harvesting);
        }
        else if (!BagHasSpace()) //bag has no space // && harvestReady
        {
            SwitchState(EntityStates.FindInteractable);
            sm.SetLastMajorState(EntityStates.Depositing);
        }
        else if (sm.InRangeOfEntity(ent.interactionTarget, sm.attackRange)) //target is valid and bag has space
        {
            sm.LookAtTarget(ent.interactionTarget.transform);
            if (sm.attackReady)
            {
                ent.unitAnimator.PlayAnimation(HARVEST);
                //sm.animator.Play("Harvest");
                if (ent.unitAnimator.AnimInProgress()) //sm.AnimatorUnfinished()
                {
                    if (sm.stateTimer < sm.impactTime)
                    {
                        sm.stateTimer += Time.deltaTime;
                    }
                    else if (sm.attackReady) //harvest timer reached impact time
                    {
                        sm.stateTimer = 0;
                        sm.attackReady = false;
                        //Debug.Log("Harvesting once");
                        HarvestTargetOnce(ent.interactionTarget);
                    }
                }
                else
                {
                    //Debug.Log("Finished harvesting");
                    //SwitchState(EntityStates.AfterHarvestCheck);
                    AfterHarvestCheck();
                }
            }
        }
        else
        { 
            SwitchState(EntityStates.WalkToInteractable);
            sm.SetLastMajorState(EntityStates.Harvesting);
        }
    }
    private void AfterHarvestCheck()
    {
        if (!BagHasSpace()) //we're full so deposit
        {
            SwitchState(EntityStates.FindInteractable);
            sm.SetLastMajorState(EntityStates.Depositing);
        }
        else if (ValidOreForHarvester(ent.interactionTarget)) //keep harvesting if valid harvestable
        {
            SwitchState(EntityStates.Harvesting);
        }
        else //find new thing to harvest from
        {
            SwitchState(EntityStates.FindInteractable);
            sm.SetLastMajorState(EntityStates.Harvesting);
        }
    } 
    public void DepositingState()
    {
        if (!ValidDepositForHarvester(ent.interactionTarget))
        {
            SwitchState(EntityStates.FindInteractable); 
            sm.SetLastMajorState(EntityStates.Depositing);
        }
        else
        {
            sm.LookAtTarget(ent.interactionTarget.transform);
            //anim.Play("Attack"); //replace with deposit animation
            //Defaults to instant dropoff, but can take time
            if (ent != null)
            {
                //TODO: add to resources based on stuff in bag

                //entity.controllerOfThis.gold += entity.harvestedResourceAmount;
                //entity.harvestedResourceAmount = 0;
                harvesterBag.Clear();
                if (ent.controllerOfThis is RTSPlayer)
                {
                    RTSPlayer rts = ent.controllerOfThis as RTSPlayer;
                    rts.UpdateGUIFromSelections();
                }
            } 
            if (ent.harvester.ValidOreForHarvester(ent.interactionTarget))//double check this behavior
            {
                SwitchState(EntityStates.WalkToInteractable);
                sm.SetLastMajorState(EntityStates.Harvesting); 
            }
            else
            {
                SwitchState(EntityStates.FindInteractable);
                sm.SetLastMajorState(EntityStates.Harvesting); 
            }
        }
    }
    public void HarvestTargetOnce(SelectableEntity target)
    {
        //Debug.Log("Harvesting Target Once");
        ent.SimplePlaySound(1); //play impact sound 
        if (target != null && target.IsSpawned && target.IsOre())
        {
            int actualHarvested = Mathf.Clamp(amountToHarvestPerSwing, 0, target.currentHP.Value); //max amount we can harvest clamped by hitpoints remaining
            //int diff = entity.harvestCapacity - entity.harvestedResourceAmount;
            int diff = harvesterBagSize - harvesterBag.Count;
            actualHarvested = Mathf.Clamp(actualHarvested, 0, diff); //max amount we can harvest clamped by remaining carrying capacity

            if (actualHarvested <= 0) return;

            if (ent.IsServer)
            {
                target.Harvest((sbyte)amountToHarvestPerSwing);
            }
            else //client tell server to change the network variable
            {
                RequestHarvestServerRpc((sbyte)amountToHarvestPerSwing, target);
            }
            ResourceType resourceType = target.ore.resourceType;

            harvesterBag.Add(resourceType);

            //entity.harvestedResourceAmount += actualHarvested; //add to harvested resources

            if (ent.controllerOfThis is RTSPlayer)
            {
                RTSPlayer rts = ent.controllerOfThis as RTSPlayer;
                rts.UpdateGUIFromSelections();
            }
        }
        else if (target != null && !target.IsSpawned)
        {
            Debug.LogError("target not spawned ...");
        }
    }

    [ServerRpc]
    private void RequestHarvestServerRpc(sbyte amount, NetworkBehaviourReference target)
    {
        //server must handle damage! 
        if (target.TryGet(out SelectableEntity select))
        {
            select.Harvest(amount);
        }
    }
    /*private void UpdateResourceCollectableMeshes()
    {
        if (entity == null) return;
        if (resourceCollectingMeshes.Length == 0) return;
        if (entity.isVisibleInFog)
        {
            //compare max resources against max resourceCollectingMeshes
            float frac = (float)harvestedResourceAmount / harvestCapacity;
            int numToActivate = Mathf.FloorToInt(frac * resourceCollectingMeshes.Length);
            for (int i = 0; i < resourceCollectingMeshes.Length; i++)
            {
                if (resourceCollectingMeshes[i] != null)
                {
                    resourceCollectingMeshes[i].enabled = i <= numToActivate - 1;
                }
            }
        }
        else
        {
            for (int i = 0; i < resourceCollectingMeshes.Length; i++)
            {
                if (resourceCollectingMeshes[i] != null)
                {
                    resourceCollectingMeshes[i].enabled = false;
                }
            }
        }
    }*/
}