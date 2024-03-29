using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding;
using Pathfinding.RVO;
using System.Linq;
using FoW;
using UnityEngine.XR;
using static UnityEngine.GraphicsBuffer;
using static TargetedEffects;
public class SelectableEntity : NetworkBehaviour
{
    public bool fakeSpawn = false;
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
        ExtendableWall
    }
    public enum ResourceType
    {
        Gold, None
    }
    #endregion
    #region NetworkVariables
    //public ushort fakeSpawnNetID = 0; 
    [HideInInspector] public NetworkVariable<bool> isTargetable = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    #endregion
    #region Hidden
    [HideInInspector] public MinionController minionController;
    [HideInInspector] public bool selected = false;
    public NetworkObject net;
    public Vector3 rallyPoint;
    [HideInInspector] public bool alive = true;

    public SelectableEntity occupiedGarrison;
    [HideInInspector] public bool isBuildIndicator = false;
    [HideInInspector] public bool tryingToTeleport = false;
    [HideInInspector] public int harvestedResourceAmount = 0; //how much have we already collected
    public SelectableEntity interactionTarget;

    [HideInInspector] public List<FactionUnit> buildQueue;
    public List<SelectableEntity> interactors;
    private MeshRenderer[] allMeshes;
    //when fog of war changes, check if we should hide or show attack effects
    private bool damaged = false;
    private readonly int delay = 50;
    private int count = 0;
    public int consumePopulationAmount = 1;
    public int raisePopulationLimitBy = 0;
    #endregion
    #region Variables

    public RallyMission rallyMission;
    public SelectableEntity rallyTarget;
    [HideInInspector] public Collider physicalCollider;
    [Header("Behavior Settings")]
    public string displayName = "name";
    [TextArea(2, 4)]
    public string desc = "desc";
    public NetworkVariable<short> hitPoints = new();
    [SerializeField] private short startingHP = 10; //buildings usually don't start with full HP
    public short maxHP = 10;
    public DepositType depositType = DepositType.None;
    public TeamBehavior teamBehavior = TeamBehavior.OwnerTeam;
    public EntityTypes type = EntityTypes.Melee;
    public bool isHeavy = false; //heavy units can't garrison into pallbearers
    [HideInInspector] public bool fullyBuilt = true;
    public bool isKeystone = false;

    [Header("Building Only")]
    public Vector3 buildOffset = new Vector3(0.5f, 0, 0.5f);

    [Header("Builder Only")]
    [Tooltip("Spawnable units and constructable buildings")]
    //public List<int> builderEntityIndices; //list of indices that can be built with this builder.    
    public int spawnableAtOnce = 1; //how many minions can be spawned at at time from this unit. 

    public FactionUnit[] spawnableUnits;
    public FactionBuilding[] constructableBuildings;
    public FactionAbility[] usableAbilities;

    [Header("Harvester Only")]
    public int allowedInteractors = 1; //only relevant if this is a resource or deposit point
    public bool isHarvester = false;
    public int harvestCapacity = 10;

    [Header("Resource Only")]
    public ResourceType selfHarvestableType = ResourceType.None;

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
    [SerializeField] private MeshRenderer[] resourceCollectingMeshes;
    [SerializeField] private GameObject selectIndicator;
    public GameObject targetIndicator;
    public List<MeshRenderer> teamRenderers;
    private DynamicGridObstacle obstacle;
    [HideInInspector] public RVOController RVO;
    public NetworkVariable<sbyte> teamNumber = new NetworkVariable<sbyte>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); //negative team numbers are AI controlled
    public sbyte desiredTeamNumber = 0; //only matters if negative
    #endregion
    #region NetworkSpawn 
    public byte clientIDToSpawnUnder = 0;
    public bool aiControlled = false;
    private Rigidbody rigid;
    private void OnDrawGizmos()
    {
        if (fakeSpawn)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(transform.position, .1f);
        }
    }
    private int oldHarvestedResourceAmount = 0;
    private void DetectChangeHarvestedResourceAmount()
    {
        if (harvestedResourceAmount != oldHarvestedResourceAmount)
        {
            oldHarvestedResourceAmount = harvestedResourceAmount;
            UpdateResourceCollectableMeshes();
        }
    }
    private void UpdateResourceCollectableMeshes()
    {
        if (resourceCollectingMeshes.Length == 0) return;
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
    public override void OnNetworkSpawn() //invoked before start()
    {
        Initialize();
        if (lineIndicator != null)
        {
            lineIndicator.enabled = false;
        }
        if (IsOwner)
        {
            if (desiredTeamNumber < 0) //AI controlled
            {
                teamNumber.Value = desiredTeamNumber;
                if (teamBehavior == TeamBehavior.OwnerTeam)
                {
                    Global.Instance.aiTeamControllers[Mathf.Abs(desiredTeamNumber) - 1].ownedEntities.Add(this);
                }
            }
            else
            {
                teamNumber.Value = (sbyte)OwnerClientId;
                if (teamBehavior == TeamBehavior.OwnerTeam)
                {
                    Global.Instance.localPlayer.ownedEntities.Add(this);
                    Global.Instance.localPlayer.lastSpawnedEntity = this;

                    if (!fullyBuilt)
                    {
                        RequestBuilders();
                    }
                    ChangePopulation(consumePopulationAmount);
                }
            }
            if (hitPoints.Value <= 0 && !constructionBegun) //buildings begun as untargetable (by enemies)
            {
                isTargetable.Value = false;
            }
            else
            {
                isTargetable.Value = true;
            }
        }
        if (IsServer)
        {
            hitPoints.Value = startingHP;
        }
        hitPoints.OnValueChanged += OnHitPointsChanged;
        damagedThreshold = (sbyte)(maxHP / 2);
        rallyPoint = transform.position;
        //SimplePlaySound(0);
        Global.Instance.PlayClipAtPoint(sounds[0], transform.position, .5f); //play spawning sound
        //AudioSource.PlayClipAtPoint(spawnSound, transform.position);

        if (targetIndicator != null)
        {
            targetIndicator.transform.parent = null;
        }
        if (selectIndicator != null) selectIndicator.SetActive(selected);

        /*if (IsOwner && !hasRegisteredRallyMission)
        {
            hasRegisteredRallyMission = true;
            TryToRegisterRallyMission();
        }*/
        TryToRegisterRallyMission();
        SetHideFogTeam();
        UpdateTeamRenderers();
        FixFogTeam();
    }
    private bool hasRegisteredRallyMission = false;
    private List<Material> savedMaterials = new();
    private void Start()
    {
        Initialize();
        //if (fogUnit != null) fogUnit.team = desiredTeamNumber;
        aiControlled = desiredTeamNumber < 0;
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
        if (!isBuildIndicator)
        {
            for (int i = 0; i < unbuiltRenderers.Length; i++)
            {
                if (unbuiltRenderers[i] != null)
                {
                    savedMaterials.Add(unbuiltRenderers[i].material);
                    unbuiltRenderers[i].material = Global.Instance.transparent;
                }
            }
        }
        if (rallyVisual != null) rallyVisual.enabled = false;
        if (teamBehavior == TeamBehavior.OwnerTeam) Global.Instance.allFactionEntities.Add(this);

        switch (type) //determine if should be fullyBuilt
        {
            case EntityTypes.Melee:
            case EntityTypes.Ranged:
            case EntityTypes.Builder:
            case EntityTypes.Transport:
                fullyBuilt = true;
                break;
            case EntityTypes.ProductionStructure:
            case EntityTypes.HarvestableStructure:
            case EntityTypes.DefensiveGarrison:
            case EntityTypes.Portal:
            case EntityTypes.ExtendableWall:
                fullyBuilt = false;
                break;
            default:
                fullyBuilt = true;
                break;
        }
    }
    private void Initialize()
    {
        if (minionController == null) minionController = GetComponent<MinionController>();
        if (fogUnit == null) fogUnit = GetComponent<FogOfWarUnit>();
        allMeshes = GetComponentsInChildren<MeshRenderer>();
        if (net == null) net = GetComponent<NetworkObject>();
        if (obstacle == null) obstacle = GetComponentInChildren<DynamicGridObstacle>();
        if (RVO == null) RVO = GetComponent<RVOController>();
        if (physicalCollider == null) physicalCollider = GetComponent<Collider>();
        if (rigid == null) rigid = GetComponent<Rigidbody>();
    }
    private void TryToRegisterRallyMission()
    {
        if (spawnerThatSpawnedThis != null && minionController != null)
        {
            //Debug.Log("mission registered");
            RallyMission spawnerRallyMission = spawnerThatSpawnedThis.rallyMission;
            SelectableEntity rallyTarget = spawnerThatSpawnedThis.rallyTarget;
            Vector3 rallyPoint = spawnerThatSpawnedThis.rallyPoint;
            minionController.givenMission = spawnerRallyMission;
            //assign mission to last
            switch (spawnerRallyMission)
            {
                case RallyMission.None:
                    break;
                case RallyMission.Move:
                    minionController.rallyTarget = rallyPoint;
                    break;
                case RallyMission.Harvest:
                    interactionTarget = rallyTarget;
                    break;
                case RallyMission.Build:
                    interactionTarget = rallyTarget;
                    break;
                case RallyMission.Garrison:
                    interactionTarget = rallyTarget;
                    break;
                case RallyMission.Attack:
                    minionController.targetEnemy = rallyTarget;
                    break;
            }
        }
    }
    public bool AbilityOffCooldown(FactionAbility ability)
    {
        for (int i = 0; i < usedAbilities.Count; i++)
        {
            if (usedAbilities[i].abilityName == ability.name) return false; //if ability is in the used abilities list, then we still need to wait  
        }
        return true;
    }
    public void UseAbility(FactionAbility ability)
    {
        List<SelectableEntity> targetedEntities = new();
        foreach (TargetedEffects effect in ability.effectsToApply)
        {
            switch (effect.targets)
            {
                case TargetedEffects.Targets.Self:
                    targetedEntities.Add(this);
                    break; 
            }
            foreach (SelectableEntity target in targetedEntities)
            {
                //get current variable
                float variableToChange = 0;
                float secondVariable = 0;
                switch (effect.status) //set variable to change;
                {
                    case TargetedEffects.StatusEffect.MoveSpeed:
                        if (target.minionController != null && target.minionController.ai != null)
                        {
                            variableToChange = target.minionController.ai.maxSpeed;
                        }
                        break;
                    case TargetedEffects.StatusEffect.AttackSpeed:
                        if (target.minionController != null)
                        {
                            variableToChange = target.minionController.attackDuration;
                            secondVariable = target.minionController.impactTime;
                        }
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
                        if (target.minionController != null && target.minionController.ai != null)
                        {
                            target.minionController.ai.maxSpeed = variableToChange;
                            target.minionController.animator.SetFloat("moveSpeedMultiplier", moveSpeedMultiplier); //if we are halving, double animation speed
                        }
                        break;
                    case TargetedEffects.StatusEffect.AttackSpeed:
                        if (target.minionController != null)
                        {
                            target.minionController.attackDuration = variableToChange;
                            target.minionController.impactTime = secondVariable;
                            target.minionController.animator.SetFloat("attackMultiplier", attackAnimMultiplier); //if we are halving, double animation speed
                        }
                        break; 
                }
                TargetedEffects newEffect = new() //NECESSARY to prevent modifying original class
                {
                    targets = effect.targets,
                    status = effect.status,
                    expirationTime = effect.expirationTime,
                    operation = effect.operation,
                    statusNumber = effect.statusNumber
                };
                appliedEffects.Add(newEffect);

                AbilityOnCooldown newAbility = new()
                {
                    abilityName = ability.abilityName,
                    cooldownTime = ability.cooldownTime
                };
                usedAbilities.Add(newAbility);
            }
        }
    }
    private List<AbilityOnCooldown> usedAbilities = new();
    private List<TargetedEffects> appliedEffects = new();
    private void UpdateUsedAbilities()
    {
        for (int i = usedAbilities.Count - 1; i >= 0; i--)
        {
            usedAbilities[i].cooldownTime -= Time.deltaTime;
            if (usedAbilities[i].cooldownTime <= 0)
            {
                usedAbilities.RemoveAt(i);
            }
        }
    }
    private void UpdateAppliedEffects() //handle expiration of these effects; this implementation may be somewhat slow
    {
        for (int i = appliedEffects.Count - 1; i >= 0; i--)
        {
            appliedEffects[i].expirationTime -= Time.deltaTime;
            if (appliedEffects[i].expirationTime <= 0)
            {
                ResetVariableFromStatusEffect(appliedEffects[i]);
                appliedEffects.RemoveAt(i);
            }
        }
    }
    private void ResetVariableFromStatusEffect(TargetedEffects effect)
    {
        switch (effect.status)
        {
            case TargetedEffects.StatusEffect.MoveSpeed:
                if (minionController != null && minionController.ai != null)
                {
                    minionController.ai.maxSpeed = minionController.defaultMoveSpeed;
                    minionController.animator.SetFloat("moveSpeedMultiplier", 1); //if we are halving, double animation speed
                }
                break;
            case TargetedEffects.StatusEffect.AttackSpeed: 
                if (minionController != null)
                {
                    minionController.attackDuration = minionController.defaultAttackDuration;
                    minionController.impactTime = minionController.defaultImpactTime;
                    minionController.animator.SetFloat("attackMultiplier", 1);
                }
                break;
            default:
                break;
        }
    }
    private void UpdateRallyVariables()
    {
        if (rallyTarget != null) //update rally visual and rally point to rally target position
        {
            rallyVisual.transform.position = rallyTarget.transform.position;
            rallyPoint = rallyTarget.transform.position;
        }
    }
    bool constructionBegun = false;
    private void DetectIfBuilt()
    {
        if (!fullyBuilt && hitPoints.Value >= maxHP) //detect if built
        {
            BecomeFullyBuilt();
        }
    }
    private void DetectIfDamaged()
    {
        if (fullyBuilt && !damaged) //if built, detect if damaged
        {
            CheckIfDamaged();
        }
    }
    private void DetectIfShouldDie()
    {
        if (minionController != null && !alive && minionController.state != MinionController.State.Die) //force go to death state
        {
            minionController.SwitchState(MinionController.State.Die);
        }
        if (hitPoints.Value <= 0 && constructionBegun && alive) //detect death if "present" in game world ie not ghost/corpse
        {
            alive = false;
            PrepareForEntityDestruction();
        }
    }
    private void DetectIfShouldUnghost()
    {
        if (!isBuildIndicator && !constructionBegun && hitPoints.Value > 0)
        {
            constructionBegun = true;
            Unghost();
        }
    }
    private void Unghost()
    {
        isTargetable.Value = true;
        physicalCollider.isTrigger = false;
        //rigid.isKinematic = false;
        for (int i = 0; i < unbuiltRenderers.Length; i++)
        {
            if (unbuiltRenderers[i] != null)
            {
                unbuiltRenderers[i].material = savedMaterials[i];
            }
        }
        if (obstacle != null) //update pathfinding
        {
            obstacle.DoUpdateGraphs();
            AstarPath.active.FlushGraphUpdates();
        }
    }
    private void FixFogTeam() //temporary fix
    {
        if (fogUnit != null && fogUnit.team != teamNumber.Value)
        {
            fogUnit.team = teamNumber.Value;
        }
    }
    private void Update()
    {
        if (IsSpawned)
        {
            DetectIfShouldDie();
            if (minionController != null && minionController.state == MinionController.State.Die) return; //do not do other things if dead
            UpdateVisibilityFromFogOfWar();
            DetectIfShouldUnghost();
            DetectIfBuilt();
            if (!fullyBuilt) return; //do not pass if not built
            UpdateRallyVariables();
            UpdateTimers();
            UpdateAppliedEffects();
            UpdateUsedAbilities();
            UpdateInteractors(); //costly for loop
            //DetectIfDamaged();
            DetectChangeHarvestedResourceAmount();
        }
    }
    private float attackEffectTimer = 0;
    private void FixedUpdate()
    {
        if (IsSpawned && IsOwner)
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
                    if (minionController.animator.GetCurrentAnimatorStateInfo(0).IsName("AttackWalkStart")
                        || minionController.animator.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk")
                        || minionController.animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"))
                    {
                        if (footstepCount < footstepThreshold)
                        {
                            footstepCount++;
                        }
                        else
                        {
                            footstepCount = 0;
                            Global.Instance.PlayClipAtPoint(Global.Instance.footsteps[Random.Range(0, Global.Instance.footsteps.Length)], transform.position, .01f);
                        }
                    }
                }
            }
        }
    }

    [ServerRpc]
    public void ChangeHitPointsServerRpc(sbyte value)
    {
        hitPoints.Value = value;
    }
    private void FixPopulationOnDeath()
    {
        ChangePopulation(-consumePopulationAmount);
        ChangeMaxPopulation(-raisePopulationLimitBy);
    }
    public void PrepareForEntityDestruction()
    {
        Global.Instance.allFactionEntities.Remove(this);
        if (IsOwner)
        {
            FixPopulationOnDeath();
        }
        else
        {
            //play death animation right away
            if (minionController != null)
            {
                minionController.animator.Play("Die");
            }
        }
        if (fogUnit != null)
        {
            fogUnit.enabled = false;
        }
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
        CheckGameVictoryState();
        if (physicalCollider != null)
        {
            physicalCollider.enabled = false; //allows dynamic grid obstacle to update pathfinding nodes one last time
        }
        if (minionController != null) minionController.DestroyObstacle();

        foreach (MeshRenderer item in allMeshes)
        {
            if (item != null && !teamRenderers.Contains(item))
            {
                item.material.color = Color.gray;
            }
        }
        if (minionController != null)
        {
            minionController.FreezeRigid(true, true);
            minionController.PrepareForDeath();

            Invoke(nameof(Die), deathDuration);
        }
        else
        {
            //StructureCosmeticDestruction();
            Invoke(nameof(Die), deathDuration); //structures cannot be deleted immediately because we need some time
            //for values to updated. better method is to destroy cosmetically
        }
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
    public FogOfWarUnit fogUnit;
    private void OnHitPointsChanged(short prev, short current)
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
            if (item != null)
            {
                if (item.passenger == null)
                {
                    item.passenger = newPassenger;
                    //newPassenger.transform.parent = item.transform;
                    newPassenger.selectableEntity.occupiedGarrison = this;
                    if (IsOwner)
                    {
                        newPassenger.selectableEntity.isTargetable.Value = passengersAreTargetable;
                        /*if (newPassenger.minionNetwork != null)
                        {
                            newPassenger.minionNetwork.verticalPosition.Value = item.transform.position.y;
                        }*/
                    }
                    newPassenger.col.isTrigger = true;
                    Global.Instance.localPlayer.DeselectSpecific(newPassenger.selectableEntity);
                    //newPassenger.minionNetwork.positionDifferenceThreshold = .1f;
                    //newPassenger.minionNetwork.ForceUpdatePosition(); //update so that passengers are more in the correct y-position
                    break;
                }
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
                if (IsOwner)
                {
                    exiting.selectableEntity.isTargetable.Value = true;
                    exiting.selectableEntity.PlaceOnGround();
                    if (IsServer)
                    {
                        exiting.selectableEntity.PlaceOnGroundClientRpc();
                    }
                    else
                    {
                        exiting.selectableEntity.PlaceOnGroundServerRpc();
                    }

                    /*if (exiting.minionNetwork != null) exiting.minionNetwork.verticalPosition.Value = 0;*/
                }
                exiting.col.isTrigger = false;

                //exiting.minionNetwork.positionDifferenceThreshold = exiting.minionNetwork.defaultPositionDifferenceThreshold;
                break;
            }
        }
    }
    [ServerRpc]
    private void PlaceOnGroundServerRpc()
    {
        PlaceOnGroundClientRpc();
    }
    [ClientRpc]
    private void PlaceOnGroundClientRpc()
    {
        if (!IsOwner) PlaceOnGround();
    }
    public void PlaceOnGround()
    {
        if (Physics.Raycast(transform.position + (new Vector3(0, 100, 0)), Vector3.down, out RaycastHit hit, Mathf.Infinity, Global.Instance.localPlayer.groundLayer))
        {
            transform.position = hit.point;
            //Debug.Log(gameObject.name + "trying to place on ground");
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
        int id = Mathf.Abs(System.Convert.ToInt32(teamNumber.Value)); //net.OwnerClientId
        if (teamNumber.Value < 0)
        {
            foreach (MeshRenderer item in teamRenderers)
            {
                if (item != null)
                {
                    ///item.material = Global.Instance.colors[System.Convert.ToInt32(net.OwnerClientId)];
                    item.material.color = Global.Instance.aiTeamColors[id];
                }
            }
        }
        else
        {
            foreach (MeshRenderer item in teamRenderers)
            {
                if (item != null)
                {
                    ///item.material = Global.Instance.colors[System.Convert.ToInt32(net.OwnerClientId)];
                    item.material.color = Global.Instance.teamColors[id];
                }
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
    private int interactorIndex = 0;
    private void UpdateInteractors()
    {
        if (interactors.Count > 0)
        {
            if (interactors[interactorIndex] != null)
            {
                if (interactors[interactorIndex].interactionTarget != this)
                {
                    interactors.RemoveAt(interactorIndex);
                }
            }
            interactorIndex++;
            if (interactorIndex >= interactors.Count) interactorIndex = 0;
        }
    }
    public readonly float minFogStrength = 0.45f;
    public bool visibleInFog = false;
    public bool oldVisibleInFog = false;
    public int hideFogTeam = 0; //set equal to the team whose fog will hide this. in mp this should be set equal to the localplayer's team
    public bool shouldHideInFog = true; // gold should not be hidden

    private void UpdateVisibilityFromFogOfWar()
    {
        if (IsSpawned && (!IsOwner || aiControlled)) //only hide in fog if not owner, or is ai controlled
        {
            if (shouldHideInFog)
            {
                FogOfWarTeam fow = FogOfWarTeam.GetTeam(hideFogTeam);
                visibleInFog = fow.GetFogValue(transform.position) < minFogStrength * 255;
                if (visibleInFog != oldVisibleInFog) //update if there is a change
                {
                    oldVisibleInFog = visibleInFog;

                    for (int i = 0; i < allMeshes.Length; i++)
                    {
                        if (allMeshes[i] != null) allMeshes[i].enabled = visibleInFog;
                    }
                }
                for (int i = 0; i < attackEffects.Length; i++)
                {
                    attackEffects[i].enabled = showAttackEffects && visibleInFog;
                }
            }
            else
            {
                visibleInFog = true;
            }
        }
    }
    private void UpdateTimers()
    {
        if (attackEffectTimer > 0) //if not zero display attack effect
        {
            attackEffectTimer -= Time.deltaTime;
            showAttackEffects = true;
        }
        else
        {
            showAttackEffects = false;
        }
    }

    bool hideFogTeamSet = false;
    /// <summary>
    /// One time event to set fog hide team
    /// </summary>
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
    private void BecomeFullyBuilt()
    {
        constructionBegun = true;
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
        ChangeMaxPopulation(raisePopulationLimitBy);

        if (!isBuildIndicator && obstacle != null && !obstacle.enabled)
        {
            obstacle.enabled = true;
        }
    }
    private void ChangeMaxPopulation(int change)
    {
        if (IsOwner && teamBehavior == TeamBehavior.OwnerTeam) Global.Instance.localPlayer.maxPopulation += change;
    }
    private void ChangePopulation(int change)
    {
        if (IsOwner && teamBehavior == TeamBehavior.OwnerTeam)
        {
            Global.Instance.localPlayer.population += change;
        }
    }
    private readonly float deathDuration = 10;
    private void Die()
    {
        if (IsOwner) //only the owner does this
        {
            Global.Instance.localPlayer.ownedEntities.Remove(this);
            Global.Instance.localPlayer.selectedEntities.Remove(this);
            if (IsServer) //only the server may destroy networkobjects
            {
                net.Despawn(gameObject);
                //Destroy(gameObject);
            }
            else
            {
                DespawnServerRpc(gameObject);
            }
        }
        else //destroy clientside representation
        {
            //experimental
            if (!IsSpawned)
            {
                Destroy(gameObject);
            }
            else
            {
                DespawnServerRpc(gameObject);
            }
        }
    }
    [ServerRpc(RequireOwnership = false)]
    private void DespawnServerRpc(NetworkObjectReference obj)
    {
        GameObject game = obj;
        //Destroy(game);
        net.Despawn(game);
    }
    public void SetRally()
    {
        Debug.Log("Setting rally");
        rallyMission = RallyMission.Move;
        rallyTarget = null;
        //determine if spawned units should be given a mission

        Ray ray = Global.Instance.localPlayer.mainCam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity, Global.Instance.localPlayer.gameLayer))
        {
            rallyPoint = hit.point;
            if (rallyVisual != null)
            {
                rallyVisual.transform.position = rallyPoint;
            }
            SelectableEntity target = Global.Instance.FindEntityFromObject(hit.collider.gameObject);
            //SelectableEntity target = hit.collider.GetComponent<SelectableEntity>();
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
        attackEffectTimer = attackEffectDuration;
        //request server to send to other clients
        //RequestEffectServerRpc();
    }
    private bool showAttackEffects = false;

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
        Global.Instance.PlayClipAtPoint(clip, transform.position, 0.1f);
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
            FactionUnit fac = buildQueue[0];
            if (fac.timeCost > 0)
            {
                fac.timeCost--;
            }
            if (fac.timeCost <= 0 && consumePopulationAmount <= Global.Instance.localPlayer.maxPopulation - Global.Instance.localPlayer.population)
            { //spawn the unit
                //Debug.Log("spawn");

                BuildQueueSpawn(fac);
            }
        }
        Global.Instance.localPlayer.UpdateBuildQueue();
    }
    private void BuildQueueSpawn(FactionUnit unit)
    {
        buildQueue.RemoveAt(0);
        SpawnFromSpawner(this, rallyPoint, unit);
    }
    public SelectableEntity spawnerThatSpawnedThis;
    public void SpawnFromSpawner(SelectableEntity select, Vector3 rally, FactionUnit unit)
    {
        //spawner is this
        Vector3 pos;
        if (select.positionToSpawnMinions != null)
        {
            pos = new Vector3(select.positionToSpawnMinions.position.x, 0, select.positionToSpawnMinions.position.z);
        }
        else
        {
            pos = select.transform.position;
        }
        //GenericSpawnMinion(pos, id, true, rally);
        Global.Instance.localPlayer.GenericSpawnMinion(pos, unit, this);
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
        /*if (targetIndicator != null && minionController != null && minionController.targetEnemy != null)
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
        }*/
    }
    public void UpdatePathIndicator(Vector3[] list)
    {
        lineIndicator.positionCount = list.Length;
        lineIndicator.SetPositions(list);
    }
    public void UpdateMoveIndicator()
    {
        /*if (targetIndicator != null && minionController != null)
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
        }*/
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
