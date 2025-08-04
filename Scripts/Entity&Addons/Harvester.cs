using FoW;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static StateMachineController;
using static UnitAnimator;
using static SoundTypes;
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
    public bool IsTargetValidOreForHarvester(Entity target)
    { 
        //Must have the correct resource type
        if (target != null && target.ore != null && target.alive && OreHasRoomForNewHarvesterClaim(target.ore)) // 
        {
            if (target.ore.resourceType == ResourceType.All) return true;
            if (allowedResources.Contains(target.ore.resourceType)) return true;
        }
        return false;
    }
    public bool IsOreValidForHarvester(Ore ore)
    {
        //Must have the correct resource type
        if (ore != null && ore.ent.alive && OreHasRoomForNewHarvesterClaim(ore)) // 
        {
            if (ore.resourceType == ResourceType.All) return true;
            if (allowedResources.Contains(ore.resourceType)) return true;
        }
        return false;
    }
    private bool OreHasRoomForNewHarvesterClaim(Ore ore)
    {
        int defaultWorkerSize = 1;
        if (claimedOre == ore) //we are already working on this target
        {
            return ore.harvestersWithClaimsOnThis.Count <= ore.GetMaxHarvesters(); //workers interacting includes this worker
        }
        else //check if we can add this worker
        {
            return ore.harvestersWithClaimsOnThis.Count + defaultWorkerSize <= ore.GetMaxHarvesters();
        }
    }
    public void TryClaimOre(Ore ore)
    {
        if (ore == null) return;
        if (ore.harvestersWithClaimsOnThis.Contains(this)) //claiming ore we already own case
        {
            claimedOre = ore;
        }
        else if (OreHasRoomForNewHarvesterClaim(ore)) //claiming ore with room case
        {
            ore.harvestersWithClaimsOnThis.Add(this);
            claimedOre = ore;
        }
        else //no room
        {
            ore.harvestersWithClaimsOnThis.Remove(this);
            claimedOre = null;
        }
    }
    public void UnclaimAllOre()
    {
        if (claimedOre != null) claimedOre.harvestersWithClaimsOnThis.Remove(this);
        claimedOre = null;
    }
    public bool ValidDepositForHarvester(Entity targetDepot)
    {
        if (targetDepot == null) return false;
        if (!targetDepot.IsDepot()) return false;
        if (!targetDepot.IsFullyBuilt()) return false;
        if (!targetDepot.IsControlledBy(ent.playerControllingThis)) return false;
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
        //Debug.Log("Finding harvestable");
        if (IsOreValidForHarvester(claimedOre)) //return to claimed ore
        {
            ent.interactionTarget = claimedOre.ent;
            SwitchState(EntityStates.WalkToInteractable);
        }
        else //revise this to find ore of the same type as our last ore
        {
            float oreSearchRange = 8;
            Ore foundOre = FindClosestVisibleOreInRange(oreSearchRange);
            if (foundOre == null) return;
            TryClaimOre(foundOre);
            ent.interactionTarget = foundOre.ent;
            SwitchState(EntityStates.WalkToInteractable);
        }
    }
    /// <summary>
    /// Returns closest harvestable resource with space for new harvesters.
    /// </summary>
    /// <returns></returns>
    private Ore FindClosestVisibleOreInRange(float range)
    {
        return Global.instance.spatialHash.GetClosestVisibleOreHashSearch(ent, range);
        
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
        Entity interactionTarget = ent.GetInteractionTarget();
        if (!IsTargetValidOreForHarvester(ent.interactionTarget))
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
    public Ore claimedOre; //This is the ore that the unit will be focusing on.
                                                //It will return to this ore after depositing.
    public void HarvestingState()
    {
        if (sm == null) return;
        if (!BagHasSpace())
        {
            sm.SetLastMajorState(EntityStates.Depositing);
            SwitchState(EntityStates.FindInteractable);
            return;
        }
        if (!IsOreValidForHarvester(claimedOre))
        {
            sm.SetLastMajorState(EntityStates.Harvesting);
            SwitchState(EntityStates.FindInteractable);
            return;
        }

        if (sm.InRangeOfEntity(claimedOre.ent, range)) //target is valid and bag has space
        {
            sm.LookAtTarget(claimedOre.transform);
            if (ready)
            {
                ent.anim.Play(HARVEST);
                if (ent.anim.InProgress())
                {
                    if (sm.stateTimer < impactTime)
                    {
                        sm.stateTimer += Time.deltaTime;
                    }
                    else if (ready) //harvest timer reached impact time
                    {
                        sm.stateTimer = 0;
                        ready = false;
                        HarvestTargetOnce(claimedOre);
                    }
                }
                else
                {
                    AfterHarvestCheck();
                }
            }
            else if (!ent.anim.InState(HARVEST))
            {
                ent.anim.Play(IDLE);
            }
        }
        else
        { 
            SwitchState(EntityStates.WalkToInteractable);
            sm.SetLastMajorState(EntityStates.Harvesting);
            return;
        }
    }
    private void AfterHarvestCheck()
    {
        ent.anim.Play(IDLE);
        //Debug.Log("AfterHarvestCheck");
        if (!BagHasSpace()) //we're full so deposit
        {
            SwitchState(EntityStates.FindInteractable);
            sm.SetLastMajorState(EntityStates.Depositing);
        }
        else if (IsTargetValidOreForHarvester(ent.interactionTarget)) //keep harvesting if valid harvestable
        {
            SwitchState(EntityStates.Harvesting);
        }
        else //find new thing to harvest from
        {
            SwitchState(EntityStates.FindInteractable);
            sm.SetLastMajorState(EntityStates.Harvesting);
        }
    }
    public void DepositingState() //keep this as a state so we can have deposits take time if we want
    {
        if (!ValidDepositForHarvester(ent.interactionTarget))
        {
            SwitchState(EntityStates.FindInteractable); 
            sm.SetLastMajorState(EntityStates.Depositing);
        }
        else
        {
            sm.LookAtTarget(ent.interactionTarget.transform);
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
            if (IsOreValidForHarvester(claimedOre))//double check this behavior
            {
                ent.interactionTarget = claimedOre.ent;
                SwitchState(EntityStates.WalkToInteractable, true);
                sm.SetLastMajorState(EntityStates.Harvesting); 
            }
            else
            {
                SwitchState(EntityStates.FindInteractable);
                sm.SetLastMajorState(EntityStates.Harvesting); 
            }
        }
    }
    public void HarvestTargetOnce(Ore ore)
    {
        //Debug.Log("Harvesting Target Once");
        ent.SimplePlaySound(HitSound); //play impact sound 
        if (ore != null && ore.IsSpawned)
        {
            int actualHarvested = Mathf.Clamp(swingDelta, 0, ore.ent.currentHP.Value); //max amount we can harvest clamped by hitpoints remaining
            //int diff = entity.harvestCapacity - entity.harvestedResourceAmount;
            int diff = bagSize - harvesterBag.Count;
            actualHarvested = Mathf.Clamp(actualHarvested, 0, diff); //max amount we can harvest clamped by remaining carrying capacity

            if (actualHarvested <= 0) return;

            if (ent.IsServer)
            {
                ore.ent.Harvest((sbyte)swingDelta);
            }
            else //client tell server to change the network variable
            {
                RequestHarvestServerRpc((sbyte)swingDelta, ore);
            }
            ResourceType resourceType = ore.resourceType;

            harvesterBag.Add(resourceType);

            //entity.harvestedResourceAmount += actualHarvested; //add to harvested resources

            /*if (ent.controllerOfThis is RTSPlayer)
            {
                RTSPlayer rts = ent.controllerOfThis as RTSPlayer;
                rts.UpdateGUIFromSelections();
            }*/
        }
        else if (ore != null && !ore.IsSpawned)
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