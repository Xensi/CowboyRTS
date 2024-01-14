using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding;
using Pathfinding.RVO;
using System.Linq;
using FoW;
public class SelectableEntity : NetworkBehaviour
{
    #region Enums
    public enum RallyMission
    {
        None,
        Move,
        Harvest,
        Build,
        Garrison,
        Attack
    }
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
        Portal,
        Wall
    }
    public enum HarvestType
    {
        Gold, None
    }
    #endregion
    #region NetworkVariables
    [HideInInspector] public NetworkVariable<sbyte> hitPoints = new();
    [HideInInspector] public NetworkVariable<bool> isTargetable = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    #endregion
    #region Hidden
    [HideInInspector] public MinionController minionController;
    [HideInInspector] public bool selected = false;
    [HideInInspector] public NetworkObject net;
    public Vector3 rallyPoint;
    [HideInInspector] public bool alive = true;

    [HideInInspector] public SelectableEntity occupiedGarrison;
    [HideInInspector] public bool isBuildIndicator = false;
    [HideInInspector] public bool tryingToTeleport = false;
    [HideInInspector] public int harvestedResourceAmount = 0; //how much have we already collected
    public SelectableEntity interactionTarget;

    [HideInInspector] public List<FactionEntityClass> buildQueue;
    public List<SelectableEntity> interactors;
    private MeshRenderer[] allMeshes;
    private bool damaged = false;
    private readonly int delay = 50;
    private int count = 0;
    public int consumePopulationAmount = 1;
    public int raisePopulationLimitBy = 0;
    #endregion
    #region Variables

    public RallyMission rallyMission;
    public SelectableEntity rallyTarget;
    public Collider physicalCollider;
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
    public bool isKeystone = false;

    [Header("Builder Only")]
    public List<int> builderEntityIndices; //list of indices that can be built with this builder.    
    public int spawnableAtOnce = 1; //how many minions can be spawned at at time from this unit.

    [Header("Harvester Only")]
    public bool isHarvester = false;
    public int allowedInteractors = 1; //only relevant if this is a resource
    public HarvestType harvestType = HarvestType.None;
    public int harvestCapacity = 10;

    [Header("Garrison Only")]
    public List<GarrisonablePosition> garrisonablePositions = new();
    public bool passengersAreTargetable = false;
    public bool acceptsHeavy = false;

    [Header("Spawners Only")]
    public Transform positionToSpawnMinions; //used for buildings

    [Header("Aesthetic Settings")]
    [SerializeField] private MeshRenderer rallyVisual;
    [SerializeField] private Material damagedState;
    public AudioClip[] sounds; //0 spawn, 1 attack, 2 attackMove
    public LineRenderer lineIndicator;
    public MeshRenderer[] unbuiltRenderers;
    public MeshRenderer[] finishedRenderers;
    [SerializeField] private MeshRenderer[] damageableMeshes;
    [SerializeField] private GameObject selectIndicator;
    public GameObject targetIndicator;
    public List<MeshRenderer> teamRenderers;
    private DynamicGridObstacle obstacle;
    [HideInInspector] public RVOController RVO;
    #endregion
    #region NetworkSpawn
    private void Start()
    {
        allMeshes = GetComponentsInChildren<MeshRenderer>();
        net = GetComponent<NetworkObject>();
        obstacle = GetComponent<DynamicGridObstacle>();
        RVO = GetComponent<RVOController>();
        if (isKeystone && Global.Instance.localPlayer.IsTargetExplicitlyOnOurTeam(this))
        {
            Global.Instance.localPlayer.keystoneUnits.Add(this);
        }
        if (isBuildIndicator)
        {
            if (obstacle != null)
            {
                obstacle.enabled = false;
            }
            if (RVO != null)
            {
                RVO.enabled = false;
            }
        }

        foreach (MeshRenderer item in finishedRenderers)
        {
            if (item != null)
            {
                item.enabled = false;
            }
        }
        foreach (MeshRenderer item in unbuiltRenderers)
        {
            if (item != null)
            {
                item.enabled = true;
            }
        }
    }
    public override void OnNetworkSpawn()
    {
        if (lineIndicator != null)
        {
            lineIndicator.enabled = false;
        }
        if (IsOwner)
        {
            isTargetable.Value = true; //initialize value

            if (teamBehavior == TeamBehavior.OwnerTeam)
            {
                Global.Instance.localPlayer.ownedEntities.Add(this);
                Global.Instance.localPlayer.lastSpawnedEntity = this;
                Global.Instance.localPlayer.population += consumePopulationAmount;

                if (!fullyBuilt)
                {
                    RequestBuilders();
                }
            }
        }
        if (IsServer)
        {
            hitPoints.Value = startingHP;
        }
        hitPoints.OnValueChanged += OnHitPointsChanged;
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
        fogUnit = GetComponent<FogOfWarUnit>();
        if (fogUnit != null) fogUnit.team = (int) OwnerClientId;
        /*fogHide = GetComponent<HideInFog>();
        if (fogHide != null) fogHide.team = (int)OwnerClientId;*/
    }
    private void Update()
    {
        SetHideFogTeam();
        HideInFog();

        if (rallyTarget != null)
        {
            rallyVisual.transform.position = rallyTarget.transform.position;
            rallyPoint = rallyTarget.transform.position;
        }
        if (IsSpawned)
        {
            UpdateInteractors();
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

                if (!damaged)
                {
                    CheckIfDamaged();
                }
                if (hitPoints.Value <= 0)
                {
                    ProperDestroyMinion();
                }

            }
        }
    }
    private void FixedUpdate()
    {
        if (IsSpawned)
        {
            if (!fullyBuilt)
            {
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
        if (IsOwner)
        {
            Global.Instance.localPlayer.population -= consumePopulationAmount;
            Global.Instance.localPlayer.maxPopulation -= raisePopulationLimitBy;
        }
        if (physicalCollider != null)
        {
            physicalCollider.enabled = false; //allows dynamic grid obstacle to update pathfinding nodes one last time
        }
        if (RVO != null)
        {
            Destroy(RVO);
        }
        if (minionController != null)
        {
            minionController.PrepareForDeath();

            foreach (MeshRenderer item in allMeshes)
            {
                if (item != null && !teamRenderers.Contains(item))
                {
                    item.material.color = Color.gray;
                }
            }
        }
        else
        {
            StructureCosmeticDestruction();
        }
        Invoke(nameof(Die), deathDuration);
        foreach (GarrisonablePosition item in garrisonablePositions)
        {
            if (item != null)
            {
                if (item.passenger != null)
                {
                    UnloadPassenger(item.passenger);
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
        CheckGameVictoryState();
    }
    private void CheckGameVictoryState()
    {
        if (isKeystone)
        {
            Global.Instance.localPlayer.keystoneUnits.Remove(this);
            if (Global.Instance.localPlayer.keystoneUnits.Count <= 0)
            {
                Global.Instance.localPlayer.LoseGame();
            }
        }
    }
    private HideInFog fogHide;
    private FogOfWarUnit fogUnit;
    private void OnHitPointsChanged(sbyte prev, sbyte current)
    {
        if (selected)
        {
            Global.Instance.localPlayer.UpdateHPText();
        }
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
        //hitPoints.Value += delta;
        hitPoints.Value = (sbyte)Mathf.Clamp(hitPoints.Value + delta, 0, maxHP);
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
    private void UpdateInteractors()
    {
        if (interactors.Count > 0)
        { 
            for (int i = 0; i < interactors.Count; i++)
            {
                if (interactors[i] != null)
                {
                    if (interactors[i].interactionTarget != this)
                    {
                        interactors.RemoveAt(i);
                        break;
                    }
                }
            }
        }
    }
    [Range(0.0f, 1.0f)]
    public float minFogStrength = 0.2f;
    public bool visibleInFog = false;
    public int hideFogTeam = 0; //set equal to the team whose fog will hide this. in mp this should be set equal to the localplayer's team
    public bool shouldHideInFog = true; // gold should not be hidden

    private void HideInFog()
    {
        if (IsSpawned)
        { 
            if (!IsOwner)
            { 
                if (shouldHideInFog)
                { 
                    FogOfWarTeam fow = FogOfWarTeam.GetTeam(hideFogTeam);
                    visibleInFog = fow.GetFogValue(transform.position) < minFogStrength * 255;

                    for (int i = 0; i < allMeshes.Length; i++)
                    {
                        if (allMeshes[i] != null) allMeshes[i].enabled = visibleInFog;
                    }
                }
            }
        }
    }
    bool hideFogTeamSet = false;
    private void SetHideFogTeam()
    {
        if (hideFogTeamSet) return;
        //if we don't own this, then set its' hide fog team equal to our team
        if (IsSpawned)
        {
            if (!IsOwner)
            {
                if (Global.Instance.localPlayer != null)
                {
                    hideFogTeam = (int)Global.Instance.localPlayer.OwnerClientId;
                    hideFogTeamSet = true;
                }
            }
        }
    }
    private void StructureCosmeticDestruction()
    {
        foreach (MeshRenderer item in allMeshes)
        {
            item.enabled = false;
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
        if (fogUnit != null) fogUnit.enabled = true;
        Global.Instance.localPlayer.maxPopulation += raisePopulationLimitBy;
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
        rallyMission = RallyMission.Move;
        rallyTarget = null;
        //determine if spawned units should be given a mission

        Ray ray = Global.Instance.localPlayer.cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity))
        {
            rallyPoint = hit.point;
            if (rallyVisual != null)
            {
                rallyVisual.transform.position = rallyPoint;
            }
            SelectableEntity target = hit.collider.GetComponent<SelectableEntity>();
            if (target != null)
            {
                if (target.teamBehavior == TeamBehavior.OwnerTeam)
                {
                    if (target.net.OwnerClientId == net.OwnerClientId) //same team
                    { 
                        if (target.fullyBuilt && target.HasEmptyGarrisonablePosition()) //target can be garrisoned
                        {
                            rallyMission = RallyMission.Garrison;
                            rallyTarget = target; 
                        }
                        else //clicking on structure causes us to try to build
                        { 
                            rallyMission = RallyMission.Build;
                            rallyTarget = target;
                        }
                    }
                    else //enemy
                    {
                        rallyMission = RallyMission.Attack;
                        rallyTarget = target;
                    }
                }
                else if (target.teamBehavior == TeamBehavior.FriendlyNeutral)
                {
                    if (target.type == EntityTypes.HarvestableStructure)
                    {
                        rallyMission = RallyMission.Harvest;
                        rallyTarget = target;
                    }
                }
            }
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
            if (fac.timeCost > 0)
            {
                fac.timeCost--;
            }
            if (fac.timeCost <= 0 && consumePopulationAmount <= Global.Instance.localPlayer.maxPopulation - Global.Instance.localPlayer.population)
            {
                //Debug.Log("spawn");

                BuildQueueSpawn(fac.buildID);
            }
        }
        Global.Instance.localPlayer.UpdateBuildQueue();
    } 
    private void BuildQueueSpawn(byte id)
    {
        buildQueue.RemoveAt(0);
        Global.Instance.localPlayer.FromBuildingSpawn(this, rallyPoint, id); //bug here
        //get last spawned
        SelectableEntity last = Global.Instance.localPlayer.ownedEntities.Last();
        MinionController controller = last.GetComponent<MinionController>();
        if (controller != null)
        {
            controller.givenMission = rallyMission;
            //assign mission to last
            switch (rallyMission)
            {
                case RallyMission.None:
                    break;
                case RallyMission.Move:
                    break;
                case RallyMission.Harvest:
                    last.interactionTarget = rallyTarget;
                    break;
                case RallyMission.Build:
                    last.interactionTarget = rallyTarget;
                    break;
                case RallyMission.Garrison:
                    last.interactionTarget = rallyTarget;
                    break;
                case RallyMission.Attack:
                    controller.targetEnemy = rallyTarget;
                    break;
                default:
                    break;
            }
        }
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
                    lineIndicator.SetPositions(LineArray(minionController.destination.Value));
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
            rallyVisual.enabled = selected;
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
