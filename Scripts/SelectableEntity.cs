using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding;
using Pathfinding.RVO;
using FoW;
using static TargetedEffects;
public class SelectableEntity : NetworkBehaviour
{
    [HideInInspector] public bool fakeSpawn = false;
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
        Generic,
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
    public FactionEntity factionEntity;
    [HideInInspector] public MinionController minionController;
    [HideInInspector] public bool selected = false;
    [HideInInspector] public NetworkObject net;
    [HideInInspector] public Vector3 rallyPoint;
    [HideInInspector] public bool alive = true;

    [HideInInspector] public SelectableEntity occupiedGarrison;
    [HideInInspector] public bool isBuildIndicator = false;
    [HideInInspector] public bool tryingToTeleport = false;
    [HideInInspector] public int harvestedResourceAmount = 0; //how much have we already collected
    public SelectableEntity interactionTarget;

    public List<FactionUnit> buildQueue;
    public MeshRenderer[] allMeshes;
    public MeshRenderer[] unbuiltRenderers;
    public MeshRenderer[] finishedRenderers;
    //when fog of war changes, check if we should hide or show attack effects
    private bool damaged = false;
    private readonly int delay = 50;
    private int count = 0;
    [HideInInspector]
    public int consumePopulationAmount = 1;
    [HideInInspector]
    public int raisePopulationLimitBy = 0;
    #endregion
    #region Variables

    [HideInInspector] public RallyMission rallyMission;
    [HideInInspector] public SelectableEntity rallyTarget;
    [HideInInspector] public Collider physicalCollider;
    [Header("Behavior Settings")]
    [HideInInspector] public string displayName = "name";
    //[TextArea(2, 4)]
    [HideInInspector]
    public string desc = "desc";
    public NetworkVariable<short> hitPoints = new();

    //[SerializeField] 
    private short startingHP = 10; //buildings usually don't start with full HP
    [HideInInspector] public short maxHP = 10;
    [HideInInspector]
    public DepositType depositType = DepositType.None;
    [HideInInspector]
    public TeamBehavior teamType = TeamBehavior.OwnerTeam;
    [HideInInspector]
    public EntityTypes entityType = EntityTypes.Melee;
    [HideInInspector]
    public bool isHeavy = false; //heavy units can't garrison into pallbearers
    public bool fullyBuilt = true; //[HideInInspector] 
    [HideInInspector]
    public bool isKeystone = false;
    public int allowedWorkers = 1; //how many can build/repair/harvest at a time
    public int allowedInteractors = 10; //how many can interact (not build/repair)
    public List<SelectableEntity> workersInteracting;
    public List<SelectableEntity> othersInteracting;

    [Header("Building Only")]
    [HideInInspector]
    public Vector3 buildOffset = new Vector3(0.5f, 0, 0.5f);

    [Header("Builder Only")]
    [Tooltip("Spawnable units and constructable buildings")]
    //public List<int> builderEntityIndices; //list of indices that can be built with this builder.    
    [HideInInspector]
    public int spawnableAtOnce = 1; //how many minions can be spawned at at time from this unit. 

    [HideInInspector]
    public FactionUnit[] spawnableUnits;
    [HideInInspector]
    public FactionBuilding[] constructableBuildings;
    //[HideInInspector]
    public FactionAbility[] usableAbilities { get; set; }

    //[Header("Harvester Only")]
    [HideInInspector]
    public bool isHarvester = false;
    [HideInInspector]
    public int harvestCapacity = 10;

    //[Header("Resource Only")]
    [HideInInspector]
    public ResourceType selfHarvestableType = ResourceType.None;

    public List<GarrisonablePosition> garrisonablePositions = new();
    [HideInInspector]
    public bool passengersAreTargetable = false;
    [HideInInspector]
    public bool acceptsHeavy = false;

    public Transform positionToSpawnMinions; //used for buildings

    [Header("Aesthetic Settings")]
    [SerializeField] private MeshRenderer rallyVisual;
    [SerializeField] private Material damagedState;
    [HideInInspector] public AudioClip[] sounds; //0 spawn, 1 attack, 2 attackMove
    public LineRenderer lineIndicator;
    [SerializeField] private MeshRenderer[] damageableMeshes;
    [SerializeField] private MeshRenderer[] resourceCollectingMeshes;
    [SerializeField] private GameObject selectIndicator;
    public GameObject targetIndicator;
    public List<MeshRenderer> teamRenderers;
    private DynamicGridObstacle obstacle;
    [HideInInspector] public RVOController RVO;
    public NetworkVariable<sbyte> teamNumber = new NetworkVariable<sbyte>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); //negative team numbers are AI controlled

    public int localTeamNumber = 0;

    public sbyte desiredTeamNumber = 0; //only matters if negative
    #endregion
    #region NetworkSpawn 
    [HideInInspector] public byte clientIDToSpawnUnder = 0;
    public bool aiControlled = false;
    private Rigidbody rigid;
    //private bool hasRegisteredRallyMission = false;
    private List<Material> savedMaterials = new();
    public Player controllerOfThis;
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
        if (visibleInFog)
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
    }
    public bool CanProduceUnits()
    {
        return spawnableUnits.Length > 0;
    }
    public bool CanConstruct()
    {
        return constructableBuildings.Length > 0;
    }
    public bool HasResourcesToDeposit()
    {
        return harvestedResourceAmount > 0;
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
    private void InitializeEntityInfo()
    {
        if (factionEntity == null) return;
        //set faction entity information
        displayName = factionEntity.name;
        desc = factionEntity.description;
        maxHP = (short)factionEntity.maxHP;

        spawnableUnits = factionEntity.spawnableUnits;
        constructableBuildings = factionEntity.constructableBuildings;
        usableAbilities = factionEntity.usableAbilities;
        isKeystone = factionEntity.isKeystone;
        isHarvester = factionEntity.isHarvester;
        harvestCapacity = factionEntity.harvestCapacity;
        spawnableAtOnce = factionEntity.spawnableAtOnce;
        //allowedUnfinishedInteractors = factionEntity.allowedUnfinishedInteractors;
        //allowedFinishedInteractors = factionEntity.allowedFinishedInteractors;
        if (garrisonablePositions.Count > 0)
        {
            allowedInteractors = garrisonablePositions.Count; //automatically set 
        }
        else
        {
            allowedInteractors = 100;
        }

        passengersAreTargetable = factionEntity.passengersAreTargetable;
        acceptsHeavy = factionEntity.acceptsHeavy;

        consumePopulationAmount = factionEntity.consumePopulationAmount;
        raisePopulationLimitBy = factionEntity.raisePopulationLimitBy;
        //DIFFERENTIATE BETWEEN BUILDING AND UNIT TYPES
        //entityType = factionEntity.entityType;
        depositType = factionEntity.depositType;
        teamType = factionEntity.teamType;
        selfHarvestableType = factionEntity.selfHarvestableType;
        shouldHideInFog = factionEntity.shouldHideInFog;
        if (factionEntity.soundProfile != null)
        {
            sounds = factionEntity.soundProfile.sounds;
        }
        else
        {
            sounds = new AudioClip[0];
        }
        //Debug.Log("Trying to initialize");
        if (minionController != null)
        {
            //Debug.Log("Initializing attack type as " + factionEntity.attackType);
            minionController.attackType = factionEntity.attackType;
            minionController.directionalAttack = factionEntity.directionalAttack;
            minionController.attackRange = factionEntity.attackRange;
            minionController.depositRange = factionEntity.depositRange;
            minionController.damage = factionEntity.damage;
            minionController.attackDuration = factionEntity.attackDuration;
            minionController.impactTime = factionEntity.impactTime;
            minionController.areaOfEffectRadius = factionEntity.areaOfEffectRadius;
            minionController.shouldAutoSeekOutEnemies = factionEntity.shouldAutoSeekEnemies;
        }

        if (factionEntity is FactionBuilding)
        {
            FactionBuilding factionBuilding = factionEntity as FactionBuilding;
            buildOffset = factionBuilding.buildOffset;
            fullyBuilt = !factionBuilding.needsConstructing;
            if (factionBuilding.needsConstructing)
            {
                startingHP = 0;
            }
            else
            {
                startingHP = maxHP;
            }
        }
        else if (factionEntity is FactionUnit)
        {
            FactionUnit factionUnit = factionEntity as FactionUnit;
            isHeavy = factionUnit.isHeavy;
            if (minionController != null) minionController.canMoveWhileAttacking = factionUnit.canAttackWhileMoving;
            startingHP = maxHP;
        }
    }
    public bool IsDamaged()
    {
        return hitPoints.Value < maxHP;
    }
    private void Awake() //awake, networkspawn, start; verified through testing
    {
        //Debug.Log("Awake");
        Initialize();
        InitializeEntityInfo();
        initialized = true;
    }
    public bool initialized = false;
    public bool CanHarvest()
    {
        return isHarvester;
    }
    public override void OnNetworkSpawn()
    {
        //Debug.Log("NetworkSpawn");

        if (lineIndicator != null)
        {
            lineIndicator.enabled = false;
        }
        if (IsOwner)
        {
            if (desiredTeamNumber < 0) //AI controlled
            {
                teamNumber.Value = desiredTeamNumber;
                if (teamType == TeamBehavior.OwnerTeam)
                {
                    AIPlayer AIController = Global.Instance.aiTeamControllers[Mathf.Abs(desiredTeamNumber) - 1];
                    AIController.ownedEntities.Add(this);
                    controllerOfThis = AIController;
                }
                //fogUnit.enabled = false;
            }
            else
            {
                teamNumber.Value = (sbyte)OwnerClientId;
                if (teamType == TeamBehavior.OwnerTeam)
                {
                    RTSPlayer playerController = Global.Instance.localPlayer;
                    playerController.ownedEntities.Add(this);
                    playerController.lastSpawnedEntity = this;
                    controllerOfThis = playerController;

                    if (!fullyBuilt)
                    {
                        RequestBuilders();
                    }
                }
            }
            //place effect dependent on "controller of this" being defined after this line
            if (fogUnit != null) fogUnit.team = desiredTeamNumber;
            if (teamType == TeamBehavior.OwnerTeam)
            {
                ChangePopulation(consumePopulationAmount);
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

        if (targetIndicator != null)
        {
            targetIndicator.transform.parent = null;
        }
        if (selectIndicator != null) selectIndicator.SetActive(selected);


        //allowedBuilders = allowedUnfinishedInteractors;
        /*if (IsOwner && !hasRegisteredRallyMission)
        {
            hasRegisteredRallyMission = true;
            TryToRegisterRallyMission();
        }*/
        localTeamNumber = System.Convert.ToInt32(net.OwnerClientId);
    }
    private void Start() //call stuff here so that network variables are valid (isSpawned)
    {
        PlaySpawnSound();
        TryToRegisterRallyMission();
        SetInitialVisuals();
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
        if (teamType == TeamBehavior.OwnerTeam) Global.Instance.allFactionEntities.Add(this);
        //Debug.Log("Team Value" + teamNumber.Value); //team number is unchanged during initialization :(
    }
    private void SetInitialVisuals()
    {
        //FixFogTeam();
        SetHideFogTeam();
        UpdateTeamRenderers();
    }
    private void FixFogTeam() //temporary fix
    {
        if (fogUnit != null) // && fogUnit.team != teamNumber.Value
        {
            fogUnit.team = localTeamNumber;//teamNumber.Value;
            Debug.Log("setting fog unit team to" + fogUnit.team);
        }
    } /// <summary>
      /// One time event to set fog hide team
      /// </summary>
    private void SetHideFogTeam()
    {
        //if we don't own this, then set its' hide fog team equal to our team
        if (FogHideable())
        {
            if (Global.Instance.localPlayer != null)
            {
                hideFogTeam = (int)Global.Instance.localPlayer.OwnerClientId;
            }
        }
    }

    //private bool teamRenderersUpdated = false;
    private void UpdateTeamRenderers()
    {
        //int id = Mathf.Abs(System.Convert.ToInt32(teamNumber.Value)); //net.OwnerClientId
        //int id = Mathf.Abs(System.Convert.ToInt32(localTeamNumber));
        int id = localTeamNumber;
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
        //teamRenderersUpdated = true;
    }

    private void PlaySpawnSound()
    {
        if (sounds.Length > 0) Global.Instance.PlayClipAtPoint(sounds[0], transform.position, .5f); //play spawning sound
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
    public bool CanUseAbility(FactionAbility ability)
    {
        for (int i = 0; i < usableAbilities.Length; i++)
        {
            if (usableAbilities[i].abilityName == ability.abilityName) return true; //if ability is in the used abilities list, then we still need to wait  
        }
        return false;
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
                    case StatusEffect.MoveSpeed:
                        if (target.minionController != null && target.minionController.ai != null)
                        {
                            variableToChange = target.minionController.ai.maxSpeed;
                        }
                        break;
                    case StatusEffect.AttackSpeed:
                        if (target.minionController != null)
                        {
                            variableToChange = target.minionController.attackDuration;
                            secondVariable = target.minionController.impactTime;
                        }
                        break;
                    case StatusEffect.HP:
                        variableToChange = target.hitPoints.Value;
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
                    case StatusEffect.HP:
                        hitPoints.Value = (short)variableToChange;
                        break;
                    case StatusEffect.CancelInProgress:
                        //if target is ghost, full refund
                        //if construction in progress, half refund
                        if (constructionBegun)
                        {
                            Global.Instance.localPlayer.AddGold(target.factionEntity.goldCost / 2);
                        }
                        else
                        {
                            Global.Instance.localPlayer.AddGold(target.factionEntity.goldCost);
                        }
                        target.DestroyThis();
                        break;
                }
                if (effect.applyAsLingeringEffect)
                {
                    TargetedEffects newEffect = new() //NECESSARY to prevent modifying original class
                    {
                        targets = effect.targets,
                        status = effect.status,
                        expirationTime = effect.expirationTime,
                        operation = effect.operation,
                        statusNumber = effect.statusNumber
                    };
                    appliedEffects.Add(newEffect);
                }

                if (!UsedSameNameAbility(ability)) //if this unit has not used this ability already, mark it as used
                {
                    AbilityOnCooldown newAbility = new()
                    {
                        abilityName = ability.abilityName,
                        cooldownTime = ability.cooldownTime
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
    public List<AbilityOnCooldown> usedAbilities = new();
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
        if (minionController != null && !alive && minionController.minionState != MinionController.MinionStates.Die) //force go to death state
        {
            minionController.SwitchState(MinionController.MinionStates.Die);
        }
        if (hitPoints.Value <= 0 && constructionBegun && alive) //detect death if "present" in game world ie not ghost/corpse
        {
            alive = false;
            PrepareForEntityDestruction();
        }
    }
    public void DestroyThis()
    {
        PrepareForEntityDestruction();
    }
    private void DetectIfShouldUnghost()
    {
        if (!isBuildIndicator && !constructionBegun && hitPoints.Value > 0)
        {
            constructionBegun = true;
            Unghost();
        }
    }
    private void Unghost() //sets thong to look solid and not transparent
    {
        if (IsOwner) isTargetable.Value = true;

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
    private void Update()
    {
        if (IsSpawned)
        {
            DetectIfShouldDie();
            if (minionController != null && minionController.minionState == MinionController.MinionStates.Die) return; //do not do other things if dead
            UpdateVisibilityFromFogOfWar();
            DetectIfShouldUnghost();
            DetectIfBuilt();
            UpdateInteractors();
            if (fullyBuilt)
            {
                UpdateRallyVariables();
                UpdateTimers();
                UpdateAppliedEffects();
                UpdateUsedAbilities();
                //DetectIfDamaged();
                DetectChangeHarvestedResourceAmount();
            }
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
                    OwnerUpdateBuildQueue();
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
    public bool IsMelee()
    {
        float maxMeleeRange = 2;
        if (minionController != null && minionController.attackRange <= maxMeleeRange)
        {
            return true;
        }
        return false;
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
    [HideInInspector] public FogOfWarUnit fogUnit;
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
                    newPassenger.entity.occupiedGarrison = this;
                    if (IsOwner)
                    {
                        newPassenger.entity.isTargetable.Value = passengersAreTargetable;
                        /*if (newPassenger.minionNetwork != null)
                        {
                            newPassenger.minionNetwork.verticalPosition.Value = item.transform.position.y;
                        }*/
                    }
                    newPassenger.col.isTrigger = true;
                    Global.Instance.localPlayer.DeselectSpecific(newPassenger.entity);
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
                exiting.entity.occupiedGarrison = null;
                if (IsOwner)
                {
                    exiting.entity.isTargetable.Value = true;
                    exiting.entity.PlaceOnGround();
                    if (IsServer)
                    {
                        exiting.entity.PlaceOnGroundClientRpc();
                    }
                    else
                    {
                        exiting.entity.PlaceOnGroundServerRpc();
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
            if (item.CanConstruct())
            {
                //Debug.Log("requesting builder");
                MinionController minion = item.GetComponent<MinionController>();
                minion.SetBuildDestination(transform.position, this);
            }
        }
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
    private int othersInteractorIndex = 0;
    public bool IsBusy()
    {
        return interactionTarget != null;
    }
    /// <summary>
    /// Remove any units that are no longer interacting with this from its list
    /// </summary>
    private void UpdateInteractors()
    {
        if (workersInteracting.Count > 0)
        {
            if (workersInteracting[interactorIndex] != null)
            {
                if (workersInteracting[interactorIndex].interactionTarget != this)
                {
                    workersInteracting.RemoveAt(interactorIndex);
                }
            }
        }
        if (othersInteracting.Count > 0)
        {
            if (othersInteracting[interactorIndex] != null)
            {
                if (othersInteracting[interactorIndex].interactionTarget != this)
                {
                    othersInteracting.RemoveAt(interactorIndex);
                }
            }
        }
        interactorIndex++;
        othersInteractorIndex++;
        if (interactorIndex >= workersInteracting.Count) interactorIndex = 0;
        if (othersInteractorIndex >= othersInteracting.Count) othersInteractorIndex = 0;
    }
    [HideInInspector] public bool visibleInFog = false;
    [HideInInspector] public bool oldVisibleInFog = false;
    public int hideFogTeam = 0; //set equal to the team whose fog will hide this. in mp this should be set equal to the localplayer's team
    public bool shouldHideInFog = true; // gold should not be hidden
    private bool oneTimeForceUpdateFog = false;
    private void UpdateVisibilityFromFogOfWar()
    {
        if (FogHideable())
        {
            FogOfWarTeam fow = FogOfWarTeam.GetTeam(hideFogTeam);
            visibleInFog = fow.GetFogValue(transform.position) < Global.Instance.minFogStrength * 255;
            if (visibleInFog != oldVisibleInFog || oneTimeForceUpdateFog == false) //update if there is a change
            {
                oneTimeForceUpdateFog = true;
                oldVisibleInFog = visibleInFog;
                UpdateMeshVisibility(visibleInFog);
            }
            for (int i = 0; i < attackEffects.Length; i++)
            {
                attackEffects[i].enabled = showAttackEffects && visibleInFog;
            }
        }
        /*else
        {
            visibleInFog = true;
        }*/
    }
    private void UpdateMeshVisibility(bool val)
    {
        if (minionController == null)
        {
            if (fullyBuilt)
            {
                for (int i = 0; i < finishedRenderers.Length; i++)
                {
                    if (finishedRenderers[i] != null) finishedRenderers[i].enabled = val;
                }
            }
            else
            {
                for (int i = 0; i < unbuiltRenderers.Length; i++)
                {
                    if (unbuiltRenderers[i] != null) unbuiltRenderers[i].enabled = val;
                }
            }
        }
        else
        {
            for (int i = 0; i < allMeshes.Length; i++)
            {
                if (allMeshes[i] != null) allMeshes[i].enabled = val;
            }
        }
    }
    private bool FogHideable()
    {
        return IsSpawned && (!IsOwner || aiControlled) && shouldHideInFog;
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
        //Debug.Log("Becoming Fully Built");
        //allowedBuilders = allowedFinishedInteractors;
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
        if (IsOwner && teamType == TeamBehavior.OwnerTeam)
        {
            if (controllerOfThis != null) controllerOfThis.maxPopulation += change;
            //Global.Instance.localPlayer.maxPopulation += change;
        }
    }
    private void ChangePopulation(int change)
    {
        if (IsOwner && teamType == TeamBehavior.OwnerTeam)
        {
            //Global.Instance.localPlayer.population += change;
            if (controllerOfThis != null) controllerOfThis.population += change; 
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
    public bool IsHarvestable()
    {
        return selfHarvestableType != ResourceType.None;
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
                if (target.teamType == TeamBehavior.OwnerTeam)
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
                else if (target.teamType == TeamBehavior.FriendlyNeutral)
                {
                    if (target.IsHarvestable())
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
        if (sounds.Length > 0)
        {
            AudioClip clip = sounds[id];
            Global.Instance.PlayClipAtPoint(clip, transform.position, 0.1f);
            //request server to send to other clients
            RequestSoundServerRpc(id);
        }
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
    private void OwnerUpdateBuildQueue()
    {
        if (buildQueue.Count > 0)
        {
            // todo add ability to build multiple from one structure
            FactionUnit fac = buildQueue[0];
            if (fac.spawnTimeCost > 0)
            {
                fac.spawnTimeCost--;
            }
            if (fac.spawnTimeCost <= 0 
                && consumePopulationAmount <= controllerOfThis.maxPopulation - controllerOfThis.population)
            { //spawn the unit
                //Debug.Log("spawn");

                BuildQueueSpawn(fac);
            }
        }
        if (controllerOfThis is RTSPlayer)
        {
            RTSPlayer rts = controllerOfThis as RTSPlayer;
            rts.UpdateBuildQueueGUI();
        } 
    }
    private void BuildQueueSpawn(FactionUnit unit)
    {
        buildQueue.RemoveAt(0);
        SpawnFromSpawner(this, rallyPoint, unit);
    }
    [HideInInspector] public SelectableEntity spawnerThatSpawnedThis;
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
        if (controllerOfThis is RTSPlayer)
        {
            RTSPlayer rts = controllerOfThis as RTSPlayer;
            rts.GenericSpawnMinion(pos, unit, this);
        }
        else if (controllerOfThis is AIPlayer)
        { 
            AIPlayer ai = controllerOfThis as AIPlayer;
            ai.SpawnMinion(pos, unit);
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
