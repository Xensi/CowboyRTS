using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode; 
public class SelectableEntity : NetworkBehaviour
{
    #region Enums
    public enum DepositType
    {
        None,
        All,
        Gold
    }
    public enum TeamBehavior
    {
        OwnerTeam, //be on the same team as owner
        FriendlyNeutral, //anyone can select/use this. typically a resource
        EnemyNeutral //will attack anybody (except for other neutrals). typically npcs
    }
    public enum EntityTypes
    {
        Melee,
        Ranged,
        ProductionStructure,
        Builder,
        HarvestableStructure,
        DefensiveGarrison,
        Transport,
        Portal
    }
    public enum HarvestType
    {
        Gold, None
    }
    #endregion
    #region Hidden
    [HideInInspector] public MinionController minionController;
    [HideInInspector] public NetworkVariable<sbyte> hitPoints = new();
    [HideInInspector] public bool selected = false;
    [HideInInspector] public NetworkObject net;
    [HideInInspector] public Vector3 rallyPoint;
    [HideInInspector] public bool alive = true;
    [HideInInspector] public NetworkVariable<bool> isTargetable = new NetworkVariable<bool>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    [HideInInspector] public SelectableEntity occupiedGarrison;
    [HideInInspector] public bool isBuildIndicator = false;
    [HideInInspector] public bool tryingToTeleport = false;
    [HideInInspector] public int harvestedResourceAmount = 0; //how much have we already collected
    [HideInInspector] public SelectableEntity harvestTarget;

    [HideInInspector] public List<FactionEntityClass> buildQueue;
    [HideInInspector] public List<SelectableEntity> harvesters;
    private MeshRenderer[] allMeshes;
    private bool damaged = false;
    private readonly int delay = 50;
    private int count = 0;

    #endregion
    #region Variables
    [Header("Behavior Settings")]
    public string displayName = "name";
    [TextArea(2, 4)]
    public string desc = "desc";
    [SerializeField] private sbyte startingHP = 10; //buildings usually don't start with full HP
    public byte maxHP = 10;
    public DepositType depositType = DepositType.None;
    public TeamBehavior teamBehavior = TeamBehavior.OwnerTeam;
    public EntityTypes type = EntityTypes.Melee;
    public bool isHeavy = false; //heavy units can't garrison into pallbearers
    public bool fullyBuilt = true;

    [Header("Builder Only")]
    public List<int> builderEntityIndices; //list of indices that can be built with this builder.    
    public int spawnableAtOnce = 1; //how many minions can be spawned at at time from this unit.

    [Header("Harvester Only")]
    public bool isHarvester = false;
    public int allowedHarvesters = 1; //only relevant if this is a resource
    public HarvestType harvestType = HarvestType.None;
    public int harvestCapacity = 10;

    [Header("Garrison Only")]
    public List<GarrisonablePosition> garrisonablePositions = new();
    public bool passengersAreTargetable = false;
    public bool acceptsHeavy = false;

    [Header("Spawners Only")]
    public Transform positionToSpawnMinions; //used for buildings

    [Header("Aesthetic Settings")]
    [SerializeField] private GameObject rallyVisual;
    [SerializeField] private Material damagedState;
    public AudioClip[] sounds; //0 spawn, 1 attack, 2 attackMove
    public LineRenderer lineIndicator;
    public MeshRenderer[] unbuiltRenderers;
    public MeshRenderer[] finishedRenderers;
    [SerializeField] private MeshRenderer[] damageableMeshes;
    [SerializeField] private GameObject selectIndicator;
    public GameObject targetIndicator;
    public List<MeshRenderer> teamRenderers;
    #endregion
    #region NetworkSpawn
    private void Start()
    {
        net = GetComponent<NetworkObject>();
    }
    public override void OnNetworkSpawn()
    {
        if (lineIndicator != null)
        {
            lineIndicator.enabled = false;
        }
        if (IsOwner)
        {
            Global.Instance.localPlayer.ownedEntities.Add(this);
            Global.Instance.localPlayer.lastSpawnedEntity = this;
            if (!fullyBuilt)
            {
                RequestBuilders();
            }
            isTargetable.Value = true;
        }
        allMeshes = GetComponentsInChildren<MeshRenderer>();
        if (IsServer)
        {
            hitPoints.Value = startingHP;
        }
        damagedThreshold = (sbyte)(maxHP / 2);
        rallyPoint = transform.position;
        SimplePlaySound(0);
        //AudioSource.PlayClipAtPoint(spawnSound, transform.position);
        UpdateTeamRenderers();

        if (targetIndicator != null)
        {
            targetIndicator.transform.parent = null;
        }
        if (selectIndicator != null) selectIndicator.SetActive(selected);
        minionController = GetComponent<MinionController>();
    }
    public override void OnNetworkDespawn()
    {
        Destroy(targetIndicator);
    }
    #endregion
    public void ReceivePassenger(MinionController newPassenger)
    {
        foreach (GarrisonablePosition item in garrisonablePositions)
        {
            if (item.passenger == null)
            {
                item.passenger = newPassenger;
                //newPassenger.transform.parent = item.transform;
                newPassenger.selectableEntity.occupiedGarrison = this;
                newPassenger.selectableEntity.isTargetable.Value = passengersAreTargetable;
                newPassenger.col.isTrigger = true;
                newPassenger.minionNetwork.verticalPosition.Value = item.transform.position.y;
                Global.Instance.localPlayer.DeselectSpecific(newPassenger.selectableEntity);
                //newPassenger.minionNetwork.positionDifferenceThreshold = .1f;
                //newPassenger.minionNetwork.ForceUpdatePosition(); //update so that passengers are more in the correct y-position
                break;
            }
        }
    }
    public void UnloadPassenger(MinionController exiting)
    {
        foreach (GarrisonablePosition item in garrisonablePositions)
        {
            if (item.passenger == exiting)
            {
                item.passenger = null;
                //exiting.transform.parent = null;
                exiting.selectableEntity.occupiedGarrison = null;
                exiting.selectableEntity.isTargetable.Value = true;
                exiting.col.isTrigger = false;
                exiting.minionNetwork.verticalPosition.Value = 0;
                //exiting.minionNetwork.positionDifferenceThreshold = exiting.minionNetwork.defaultPositionDifferenceThreshold;
                break;
            }
        }
    }
    public bool HasEmptyGarrisonablePosition()
    {
        if (garrisonablePositions.Count <= 0)
        {
            return false;
        }
        else
        {
            bool val = false;
            foreach (GarrisonablePosition item in garrisonablePositions)
            {
                if (item.passenger == null)
                {
                    val = true;
                }
            }
            return val;
        }
    }
    private void RequestBuilders()
    {
        //Debug.Log("request builders");
        RTSPlayer local = Global.Instance.localPlayer;
        foreach (SelectableEntity item in local.selectedEntities)
        {
            if (item.type == EntityTypes.Builder)
            {
                MinionController minion = item.GetComponent<MinionController>();
                minion.SetBuildDestination(transform.position, this);
            }
        }
    }
    
    private bool teamRenderersUpdated = false;
    private void UpdateTeamRenderers()
    {
        int id = System.Convert.ToInt32(net.OwnerClientId);
        foreach (MeshRenderer item in teamRenderers)
        {
            if (item != null)
            {
                ///item.material = Global.Instance.colors[System.Convert.ToInt32(net.OwnerClientId)];
                item.material.color = Global.Instance.teamColors[id];
            }
        }
        teamRenderersUpdated = true;
    }
    
    public void TakeDamage(sbyte damage) //always managed by SERVER
    {
        hitPoints.Value -= damage; 
    }
    public void Harvest(sbyte amount) //always managed by SERVER
    {
        hitPoints.Value -= amount;
    }
    private sbyte damagedThreshold;
    private void CheckIfDamaged()
    { 
        if (hitPoints.Value <= damagedThreshold)
        {
            damaged = true;
            for (int i = 0; i < damageableMeshes.Length; i++)
            {
                if (damageableMeshes[i] != null)
                { 
                    damageableMeshes[i].material = damagedState;
                }
            }
        }
    }
    public void BuildThis(sbyte delta)
    {
        hitPoints.Value += delta;
    }
    /*public void OnTriggerEnter(Collider other)
    {
        if (Global.Instance.localPlayer != null && Global.in)
        {
            Global.Instance.localPlayer.placementBlocked = true;
            Global.Instance.localPlayer.UpdatePlacement();
        }
    }
    public void OnTriggerExit(Collider other)
    {
        if (Global.Instance.localPlayer != null)
        {
            Global.Instance.localPlayer.placementBlocked = false;
            Global.Instance.localPlayer.UpdatePlacement();
        }
    } */
    private void UpdateHarvesters()
    {
        if (harvesters.Count > 0)
        { 
            for (int i = 0; i < harvesters.Count; i++)
            {
                if (harvesters[i] != null)
                {
                    if (harvesters[i].harvestTarget != this)
                    {
                        harvesters.RemoveAt(i);
                        break;
                    }
                }
            }
        }
    }
    private void FixedUpdate()
    { 
        if (IsSpawned)
        { 
            if (type == EntityTypes.HarvestableStructure)
            {
                UpdateHarvesters();
            }
            if (!teamRenderersUpdated)
            {
                UpdateTeamRenderers();
            }

            if (!alive)
            {
                if (minionController != null)
                {
                    minionController.state = MinionController.State.Die;
                }
                return;
            }

            if (!fullyBuilt)
            {
                CheckIfBuilt();
                if (hitPoints.Value < 0)
                {
                    ProperDestroyMinion();
                }
            }
            else
            {
                if (count < delay)
                {
                    count++;
                }
                else
                {
                    count = 0;
                    UpdateBuildQueue();
                }
                if (!damaged)
                {
                    CheckIfDamaged();
                }
                if (hitPoints.Value <= 0)
                {
                    ProperDestroyMinion();
                }
                //walking sounds
                if (minionController != null)
                {
                    if (minionController.animator.GetCurrentAnimatorStateInfo(0).IsName("AttackWalkStart") || minionController.animator.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk") || minionController.animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"))
                    {
                        if (footstepCount < footstepThreshold)
                        {
                            footstepCount++;
                        }
                        else
                        {
                            footstepCount = 0;
                            Global.Instance.PlayClipAtPoint(Global.Instance.footsteps[Random.Range(0, Global.Instance.footsteps.Length)], transform.position, .05f);
                        }
                    }
                }
            }
        }
    }
    public void ProperDestroyMinion()
    {
        foreach (GarrisonablePosition item in garrisonablePositions)
        {
            if (item != null)
            {
                if (item.passenger != null)
                {
                    UnloadPassenger(item.passenger);
                    /*if (item.passenger.selectableEntity != null)
                    {
                        item.passenger.selectableEntity.occupiedGarrison = null;
                        item.passenger.selectableEntity.isTargetable.Value = true;
                        item.passenger = null;
                    }*/
                }
            }
        }

        if (targetIndicator != null)
        { 
            targetIndicator.SetActive(false);
            lineIndicator.enabled = false;
            targetIndicator.transform.parent = transform;
        }

        alive = false;
        foreach (MeshRenderer item in allMeshes)
        { 
            if (item != null && !teamRenderers.Contains(item))
            { 
                item.material.color = Color.gray;
            }
        }
        if (type != EntityTypes.ProductionStructure && type != EntityTypes.HarvestableStructure)
        {
            if (minionController != null)
            {
                minionController.PrepareForDeath();
            }
            //Invoke("Die", deathDuration);
            Invoke(nameof(Die), deathDuration);
        }
        else
        {
            Die();
        }
    }
    private int footstepCount = 0;
    private readonly int footstepThreshold = 12;
    private void CheckIfBuilt()
    {
        if (hitPoints.Value >= maxHP)
        {
            BecomeFullyBuilt();
        }
    }
    private void BecomeFullyBuilt()
    {
        fullyBuilt = true;
        Global.Instance.localPlayer.UpdateGUIFromSelections();
        foreach (MeshRenderer item in finishedRenderers)
        {
            if (item != null)
            {
                item.enabled = true;
            }
        }
        foreach (MeshRenderer item in unbuiltRenderers)
        {
            if (item != null)
            {
                item.enabled = false;
            }
        }  
    } 
    private readonly float deathDuration = 10;
    private void Die()
    { 
        if (IsOwner)
        { 
            Global.Instance.localPlayer.ownedEntities.Remove(this);
            Global.Instance.localPlayer.selectedEntities.Remove(this);
            if (IsServer) //only the server may destroy networkobjects
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyObjectServerRpc(gameObject);
            }
        }
    }
    [ServerRpc]
    private void DestroyObjectServerRpc(NetworkObjectReference obj)
    {
        GameObject game = obj;
        Destroy(game);
    }
    public void SetRally()
    {
        /*Ray ray = Global.Instance.localPlayer.cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity))
        {
            rallyPoint = hit.point;
            if (rallyVisual != null)
            {
                rallyVisual.transform.position = rallyPoint;
            }
        }*/
        rallyPoint = Global.Instance.localPlayer.worldPosition;
        if (rallyVisual != null)
        {
            rallyVisual.transform.position = rallyPoint;
        }
    }
    public MeshRenderer[] attackEffects;

    private readonly float attackEffectDuration = 0.1f;
    public void DisplayAttackEffects()
    {
        //fire locally
        for (int i = 0; i < attackEffects.Length; i++)
        {
            attackEffects[i].enabled = true;
        }
        Invoke(nameof(HideAttackEffects), attackEffectDuration);
        //request server to send to other clients
        RequestEffectServerRpc();
    }
    [ServerRpc]
    private void RequestEffectServerRpc()
    {
        PlayAttackEffectClientRpc();
    }
    [ClientRpc]
    private void PlayAttackEffectClientRpc()
    {
        if (!IsOwner)
        {
            for (int i = 0; i < attackEffects.Length; i++)
            {
                attackEffects[i].enabled = true;
            }
            Invoke(nameof(HideAttackEffects), attackEffectDuration);
        }
    }
    private void HideAttackEffects()
    { 
        for (int i = 0; i < attackEffects.Length; i++)
        {
            attackEffects[i].enabled = false;
        }
    }
    public void SimplePlaySound(byte id)
    {
        //fire locally 
        AudioClip clip = sounds[id];
        Global.Instance.PlayClipAtPoint(clip, transform.position, 0.25f);
        //request server to send to other clients
        RequestSoundServerRpc(id);
    }
    [ServerRpc(RequireOwnership = false)]
    private void RequestSoundServerRpc(byte id)
    {
        PlaySoundClientRpc(id);
    }
    [ClientRpc]
    private void PlaySoundClientRpc(byte id)
    {
        if (!IsOwner)
        {
            AudioClip clip = sounds[id];
            Global.Instance.PlayClipAtPoint(clip, transform.position, 0.25f);
        }
    }
    private void UpdateBuildQueue()
    {
        if (buildQueue.Count > 0)
        {
            // todo add ability to build multiple from one structure
            FactionEntityClass fac = buildQueue[0];
            fac.timeCost--;
            if (fac.timeCost <= 0)
            {
                //Debug.Log("spawn");
                buildQueue.RemoveAt(0);
                Global.Instance.localPlayer.FromBuildingSpawn(this, rallyPoint, fac.buildID);
            }
        }
        Global.Instance.localPlayer.UpdateBuildQueue();
    } 
    private Vector3[] LineArray(Vector3 des)
    { 
        targetIndicator.transform.position = new Vector3(des.x, 0.05f, des.z);
        Vector3[] array = new Vector3[lineIndicator.positionCount];
        array[0] = transform.position;
        array[1] = des;
        return array;
    }
    public void HideMoveIndicator()
    {
        if (targetIndicator != null)
        {
            targetIndicator.SetActive(false);
            lineIndicator.enabled = false;
        }
    }
    public void UpdateAttackIndicator()
    { 
        if (targetIndicator != null && minionController != null && minionController.targetEnemy != null)
        {
            if (alive)
            {
                targetIndicator.SetActive(selected);
                lineIndicator.enabled = selected;
                if (selected)
                {
                    lineIndicator.SetPositions(LineArray(minionController.targetEnemy.transform.position));
                }
            }
            else //disable if dead
            {
                targetIndicator.SetActive(false);
                lineIndicator.enabled = false;
            }
        }
    }
    public void UpdateMoveIndicator()
    { 
        if (targetIndicator != null && minionController != null)
        {
            if (alive)
            {
                targetIndicator.SetActive(selected);
                lineIndicator.enabled = selected;
                if (selected)
                { 
                    lineIndicator.SetPositions(LineArray(minionController.destination));
                }   
            }
            else //disable if dead
            {
                targetIndicator.SetActive(false);
                lineIndicator.enabled = false;
            }
        }
    }
    public void UpdateTargetIndicator()
    { 
        if (targetIndicator != null)
        {
            if (alive)
            { 
                if (minionController != null && minionController.targetEnemy != null)
                {
                    targetIndicator.SetActive(selected);
                    lineIndicator.enabled = selected;
                    if (selected)
                    { 
                        targetIndicator.transform.position = new Vector3(minionController.targetEnemy.transform.position.x, 0.05f, minionController.targetEnemy.transform.position.z);
                        Vector3[] array = new Vector3[lineIndicator.positionCount];
                        array[0] = transform.position;
                        array[1] = minionController.targetEnemy.transform.position;
                        lineIndicator.SetPositions(array);
                    } 
                }

                else
                {
                    targetIndicator.SetActive(false);
                    lineIndicator.enabled = false;
                }
            }
            else
            {
                targetIndicator.SetActive(false);
                lineIndicator.enabled = false;
            }
        }
    }
    public void UpdateIndicator()
    {
        if (selectIndicator != null) selectIndicator.SetActive(selected);
        //UpdateTargetIndicator();
        if (rallyVisual != null)
        {
            rallyVisual.transform.position = rallyPoint;
            rallyVisual.SetActive(selected);
        }
    }
    public void Select(bool val)
    {
        if (alive)
        { 
            selected = val;
        }
        else
        {
            selected = false;
        }
        UpdateIndicator();
    }
}
