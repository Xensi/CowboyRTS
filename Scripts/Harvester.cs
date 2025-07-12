using FoW;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static StateMachineController;
using static UnitAnimator;
public enum HowToFilterResources
{
    BanResources,
    AllowResources
}
public class Harvester : SwingEntityAddon
{
    [SerializeField] private HarvesterSettings harvesterSettings;
    [SerializeField] private List<ResourceType> harvesterBag;

    private int bagSize = 5;
      
    private List<ResourceType> allowedResources; //Resources we can harvest


    //[SerializeField] private MeshRenderer[] resourceCollectingMeshes; //Add to this array to show meshes that indicate collected resources 
    /*private bool InvalidDeposit(SelectableEntity target) //Add a depot class that will have banned resources for depositing
    {
        return target == null || target.depositType == SelectableEntity.DepositType.None;
    }*/
    public HarvesterSettings GetHarvesterSettings()
    {
        return harvesterSettings;
    }
    public bool BagContainsResources()
    {
        return harvesterBag.Count > 0;
    }
    public override void InitAddon()
    {
        if (harvesterSettings != null)
        { 
            bagSize = harvesterSettings.bagSize;
            swingDelta = harvesterSettings.amountToHarvestPerSwing;
            range = harvesterSettings.interactRange;
            impactTime = harvesterSettings.impactTime;
            duration = harvesterSettings.duration;
            allowedResources = harvesterSettings.allowedResources;
        }
    } 
    public void WalkToDepot()
    { 
        if (!ValidDepositForHarvester(ent.interactionTarget))
        {
            SwitchState(EntityStates.FindInteractable);
        }
        else
        {
            if (sm.InRangeOfEntity(ent.interactionTarget, range))
            {
                SwitchState(EntityStates.Depositing);
            }
            else
            {
                anim.Play(WALK);
                Vector3 closest = ent.interactionTarget.physicalCollider.ClosestPoint(transform.position);
                pf.SetDestinationIfHighDiff(closest);
            }
        }
    }
    /// <summary>
    /// Is the target an valid harvestable resource?
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    public bool ValidOreForHarvester(Entity target)
    { 
        if (target != null && target.ore != null && target.alive)
        {
            if (target.ore.resourceType == ResourceType.All) return true;
            if (allowedResources.Contains(target.ore.resourceType)) return true;
        }
        return false;
    }
    public bool ValidDepositForHarvester(Entity targetDepot)
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
        if (ValidOreForHarvester(ent.interactionTarget))
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
    private Entity FindClosestHarvestable()
    {
        //Debug.Log("Finding closest harvestable");
        FogOfWarTeam fow = FogOfWarTeam.GetTeam((int)ent.playerControllingThis.playerTeamID);
        List<Ore> oreList = ent.playerControllingThis.friendlyOres;

        Entity closest = null;
        float distance = Mathf.Infinity;
        foreach (Ore ore in oreList)
        {
            Entity item = ore.GetEntity();
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
    private Entity FindClosestDeposit() //right now resource agnostic
    {
        List<Depot> list = ent.playerControllingThis.ownedDepots;

        Entity closest = null;
        float distance = Mathf.Infinity;
        foreach (Depot depot in list)
        {
            Entity item = depot.GetEntity();
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
        return harvesterBag.Count < bagSize;
        //return entity.harvestedResourceAmount >= entity.harvestCapacity;
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
            if (sm.InRangeOfEntity(ent.interactionTarget, range))
            {
                SwitchState(EntityStates.Harvesting);
            }
            else
            {
                anim.Play(WALK);
                Vector3 closest = ent.interactionTarget.physicalCollider.ClosestPoint(transform.position);
                pf.SetDestinationIfHighDiff(closest);
            }
        }
    }
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
        else if (sm.InRangeOfEntity(ent.interactionTarget, range)) //target is valid and bag has space
        {
            sm.LookAtTarget(ent.interactionTarget.transform);
            if (ready)
            {
                ent.anim.Play(HARVEST);
                //sm.animator.Play("Harvest");
                if (ent.anim.InProgress()) //sm.AnimatorUnfinished()
                {
                    if (sm.stateTimer < impactTime)
                    {
                        sm.stateTimer += Time.deltaTime;
                    }
                    else if (ready) //harvest timer reached impact time
                    {
                        sm.stateTimer = 0;
                        ready = false;
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

                for (int i = harvesterBag.Count - 1; i >= 0; i--)
                {
                    ResourceType resource = harvesterBag[i];
                    switch (resource)
                    {
                        case ResourceType.None:
                            break;
                        case ResourceType.All:
                            break;
                        case ResourceType.Gold:
                            ent.playerControllingThis.gold++;
                            break;
                        case ResourceType.Wood:
                            ent.playerControllingThis.wood++;
                            break;
                        case ResourceType.Cactus:
                            ent.playerControllingThis.cactus++;
                            break;
                        default:
                            break;
                    }
                    harvesterBag.RemoveAt(i);
                }
                //entity.controllerOfThis.gold += entity.harvestedResourceAmount;
                //entity.harvestedResourceAmount = 0; 
                /*if (ent.controllerOfThis is RTSPlayer)
                {
                    RTSPlayer rts = ent.controllerOfThis as RTSPlayer;
                    rts.UpdateGUIFromSelections();
                }*/
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
    public void HarvestTargetOnce(Entity target)
    {
        //Debug.Log("Harvesting Target Once");
        ent.SimplePlaySound(1); //play impact sound 
        if (target != null && target.IsSpawned && target.IsOre())
        {
            int actualHarvested = Mathf.Clamp(swingDelta, 0, target.currentHP.Value); //max amount we can harvest clamped by hitpoints remaining
            //int diff = entity.harvestCapacity - entity.harvestedResourceAmount;
            int diff = bagSize - harvesterBag.Count;
            actualHarvested = Mathf.Clamp(actualHarvested, 0, diff); //max amount we can harvest clamped by remaining carrying capacity

            if (actualHarvested <= 0) return;

            if (ent.IsServer)
            {
                target.Harvest((sbyte)swingDelta);
            }
            else //client tell server to change the network variable
            {
                RequestHarvestServerRpc((sbyte)swingDelta, target);
            }
            ResourceType resourceType = target.ore.resourceType;

            harvesterBag.Add(resourceType);

            //entity.harvestedResourceAmount += actualHarvested; //add to harvested resources

            /*if (ent.controllerOfThis is RTSPlayer)
            {
                RTSPlayer rts = ent.controllerOfThis as RTSPlayer;
                rts.UpdateGUIFromSelections();
            }*/
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
        if (target.TryGet(out Entity select))
        {
            select.Harvest(amount);
        }
    }
}