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

    public EntityHealthBar healthBar;
    public EntityHealthBar productionProgressBar;
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
    [HideInInspector] public SelectableEntity interactionTarget;

    [HideInInspector] public List<FactionUnit> buildQueue;
    private MeshRenderer[] allMeshes;
    [SerializeField] private MeshRenderer[] unbuiltRenderers;
    [SerializeField] private GameObject finishedRendererParent;
    private MeshRenderer[] finishedMeshRenderers;

    private AreaEffector[] areaEffectors;
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
    public Collider physicalCollider;
    public Collider[] allPhysicalColliders;
    [Header("Behavior Settings")]
    [HideInInspector] public string displayName = "name";
    //[TextArea(2, 4)]
    [HideInInspector]
    public string desc = "desc";
    public NetworkVariable<short> currentHP = new();

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
    [SerializeField] private GameObject rallyVisual;
    private Material damagedState;
    [HideInInspector] public AudioClip[] sounds; //0 spawn, 1 attack, 2 attackMove
    public LineRenderer lineIndicator;
    [SerializeField] private MeshRenderer[] damageableMeshes;
    [SerializeField] private MeshRenderer[] resourceCollectingMeshes;
    private SelectionCircle selectIndicator;
    public GameObject targetIndicator;
    public List<MeshRenderer> teamRenderers;
    [HideInInspector] public NavmeshCut obstacle;
    [HideInInspector] public RVOController RVO;
    [HideInInspector]
    public NetworkVariable<sbyte> teamNumber = new NetworkVariable<sbyte>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); //negative team numbers are AI controlled

    [HideInInspector] public int localTeamNumber = 0;

    [HideInInspector] public sbyte desiredTeamNumber = 0; //only matters if negative
    #endregion
    #region NetworkSpawn 
    [HideInInspector] public byte clientIDToSpawnUnder = 0;
    [HideInInspector] public bool aiControlled = false;
    private Rigidbody rigid;
    //private bool hasRegisteredRallyMission = false;
    private List<Material> savedMaterials = new();
    public Player controllerOfThis;

    [HideInInspector] public bool isVisibleInFog = false;
    [HideInInspector] public bool oldVisibleInFog = false;
    [HideInInspector] public int hideFogTeam = 0; //set equal to the team whose fog will hide this. in mp this should be set equal to the localplayer's team
    [HideInInspector] public bool shouldHideInFog = true; // gold should not be hidden
    private bool oneTimeForceUpdateFog = false;
    //[SerializeField]
    private float fogValue;
    //[SerializeField]
    private FogOfWarTeam fow;
    [HideInInspector] public bool productionBlocked = false;
    public bool isAttackable = true;


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
    public bool IsMinion()
    {
        return minionController != null;
    }
    private void UpdateResourceCollectableMeshes()
    {
        if (resourceCollectingMeshes.Length == 0) return;
        if (isVisibleInFog)
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
    private void Initialize()
    {
        allMeshes = GetComponentsInChildren<MeshRenderer>();
        if (minionController == null) minionController = GetComponent<MinionController>();
        if (fogUnit == null) fogUnit = GetComponent<FogOfWarUnit>();
        if (net == null) net = GetComponent<NetworkObject>();
        if (obstacle == null) obstacle = GetComponentInChildren<NavmeshCut>();
        if (RVO == null) RVO = GetComponent<RVOController>();
        if (physicalCollider == null) physicalCollider = GetComponent<Collider>();
        if (rigid == null) rigid = GetComponent<Rigidbody>();
        if (lootOnDestructionComp == null) lootOnDestructionComp = GetComponent<LootOnDestruction>();
        if (finishedRendererParent != null) finishedMeshRenderers = finishedRendererParent.GetComponentsInChildren<MeshRenderer>();
        areaEffectors = GetComponentsInChildren<AreaEffector>();
        selectIndicator = GetComponentInChildren<SelectionCircle>();
    }
    private LootOnDestruction lootOnDestructionComp;
    private void InitializeEntityInfo()
    {
        if (factionEntity == null)
        {
            Debug.Log("Return");
            return;
        }
        //set faction entity information
        deathEffect = factionEntity.deathEffect;
        displayName = factionEntity.productionName;
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

        fogUnit.circleRadius = factionEntity.visionRange;

        if (factionEntity.soundProfile != null)
        {
            sounds = factionEntity.soundProfile.sounds;
        }
        else
        {
            sounds = new AudioClip[0];
        }
        //Debug.Log("Trying to initialize");
        if (IsMinion())
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
            minionController.shouldAggressivelySeekEnemies = factionEntity.shouldAggressivelySeekEnemies;
            minionController.attackProjectile = factionEntity.attackProjectilePrefab;
        }

        if (factionEntity is FactionBuilding)
        {
            FactionBuilding factionBuilding = factionEntity as FactionBuilding;
            buildOffset = factionBuilding.buildOffset;
            if (fullyBuiltInScene)
            {
                fullyBuilt = false;
                startingHP = maxHP;
            }
            else
            {
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
        }
        else if (factionEntity is FactionUnit)
        {
            FactionUnit factionUnit = factionEntity as FactionUnit;
            isHeavy = factionUnit.isHeavy;
            startingHP = maxHP;

            /*if (minionController != null)
            {
                minionController.canMoveWhileAttacking = factionUnit.canAttackWhileMoving; 
            }*/
        }
        hideModelOnDeath = factionEntity.hideModelOnDeath;
    }
    public bool fullyBuiltInScene = false; //set to true to override needs constructing value
    public bool IsDamaged()
    {
        return currentHP.Value < maxHP;
    }
    private void Awake() //awake, networkspawn, start; verified through testing
    {
        //Debug.Log("Awake");
        Initialize();
        InitializeEntityInfo();
        RetainHealthBarPosition();
        initialized = true; 
    } 
    private Vector3 healthBarOffset;
    private void RetainHealthBarPosition()
    {
        healthBarOffset = healthBar.transform.position - transform.position;
    }


    [HideInInspector] public bool initialized = false;
    public bool CanHarvest()
    {
        return isHarvester;
    }
    public bool IsNotYetBuilt()
    {
        return !fullyBuilt && !isBuildIndicator;
    }
    public bool IsFullyBuilt()
    {
        return fullyBuilt && !isBuildIndicator;
    }
    public bool IsUnit()
    {
        return minionController != null;
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
            if (controllerOfThis != null) // placed in scene manually
            {
                teamNumber.Value = (sbyte)controllerOfThis.playerTeamID;
                if (controllerOfThis is AIPlayer)
                {
                    if (teamType == TeamBehavior.OwnerTeam)
                    {
                        controllerOfThis.ownedEntities.Add(this);

                        if (IsMinion()) controllerOfThis.ownedMinions.Add(minionController);
                        if (IsNotYetBuilt())
                        {
                            controllerOfThis.unbuiltStructures.Add(this);
                        }
                    }
                }
            }
            else //has no controller already
            {
                if (desiredTeamNumber < 0) //AI controlled
                {
                    teamNumber.Value = desiredTeamNumber;
                    if (teamType == TeamBehavior.OwnerTeam)
                    {
                        AIPlayer AIController = Global.Instance.aiPlayers[Mathf.Abs(desiredTeamNumber) - 1];
                        controllerOfThis = AIController;
                        controllerOfThis.ownedEntities.Add(this);
                        if (IsMinion()) controllerOfThis.ownedMinions.Add(minionController);
                        if (IsNotYetBuilt())
                        {
                            controllerOfThis.unbuiltStructures.Add(this);
                        }
                    }
                    //fogUnit.enabled = false;
                }
                else //player controlled
                {
                    teamNumber.Value = (sbyte)OwnerClientId;
                    if (teamType == TeamBehavior.OwnerTeam)
                    {
                        RTSPlayer playerController = Global.Instance.localPlayer;
                        playerController.ownedEntities.Add(this);
                        if (IsMinion()) playerController.ownedMinions.Add(minionController);
                        playerController.lastSpawnedEntity = this;
                        controllerOfThis = playerController;

                        if (factionEntity.constructableBuildings.Length > 0)
                        {
                            playerController.ownedBuilders.Add(minionController);
                        }

                        if (IsNotYetBuilt())
                        {
                            playerController.unbuiltStructures.Add(this);
                        }
                        if (!fullyBuilt)
                        {
                            RequestBuilders();
                        }
                    }
                }
            }
            //place effect dependent on "controller of this" being defined after this line!
            if (controllerOfThis != null)
            {
                StartGameAddToEnemyLists();
                if (fogUnit != null) fogUnit.team = controllerOfThis.playerTeamID;
            }

            if (teamType == TeamBehavior.OwnerTeam)
            {
                ChangePopulation(consumePopulationAmount);
            }
            if (currentHP.Value <= 0 && !constructionBegun) //buildings begun as untargetable (by enemies)
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
            currentHP.Value = startingHP;
        }
        currentHP.OnValueChanged += OnHitPointsChanged;
        damagedThreshold = (sbyte)(maxHP / 2);
        rallyPoint = transform.position;

        if (targetIndicator != null)
        {
            targetIndicator.transform.parent = Global.Instance.transform;
        }
        ChangeSelectIndicatorStatus(selected);


        //allowedBuilders = allowedUnfinishedInteractors;
        /*if (IsOwner && !hasRegisteredRallyMission)
        {
            hasRegisteredRallyMission = true;
            TryToRegisterRallyMission();
        }*/

        localTeamNumber = System.Convert.ToInt32(net.OwnerClientId);
    }
    private void ChangeSelectIndicatorStatus(bool val)
    {
        if (selectIndicator != null)
        {
            //selectIndicator.gameObject.SetActive(true);
            selectIndicator.UpdateVisibility(val);
            UnityEngine.Color selectColor = Color.green;
            if (Global.Instance.localPlayer != controllerOfThis)
            {
                selectColor = UnityEngine.Color.red;
            }
            selectIndicator.SetColor(selectColor); 
        }
    }
    private void SetFinishedRenderersVisibility(bool val)
    {
        if (finishedRendererParent == null) return;
        for (int i = 0; i < finishedMeshRenderers.Length; i++)
        {
            if (finishedMeshRenderers[i] != null) finishedMeshRenderers[i].enabled = (val);
        }
        //if (finishedRendererParent != null) finishedRendererParent.SetActive(val);
    }
    private void SetUnfinishedRenderersVisibility(bool val)
    {
        for (int i = 0; i < unbuiltRenderers.Length; i++)
        {
            if (unbuiltRenderers[i] != null) unbuiltRenderers[i].enabled = val;
        }
    }
    private void Start() //call stuff here so that network variables are valid (isSpawned)
    {
        SetStartingSelectionRadius();
        Select(false);
        PlaySpawnSound();
        TryToRegisterRallyMission();
        SetInitialVisuals();
        aiControlled = desiredTeamNumber < 0 || controllerOfThis is AIPlayer;
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
        SetFinishedRenderersVisibility(false);
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
        if (rallyVisual != null) rallyVisual.SetActive(false);
        if (teamType == TeamBehavior.OwnerTeam) Global.Instance.allEntities.Add(this);

        if (healthBar != null)
        { 
            healthBar.entity = this;
            healthBar.SetVisibleHPConditional(false);
        }
        if (productionProgressBar != null)
        {
            productionProgressBar.entity = this;
            productionProgressBar.SetVisible(false);
        }

        if (whenTheseEntitiesKilledTriggerBehavior.Count > 0) shouldCheckTrigger = true;

        if (controllerOfThis != null && controllerOfThis.allegianceTeamID != Global.Instance.localPlayer.allegianceTeamID)
        {   //this should be counted as an enemy 
            gameObject.layer = LayerMask.NameToLayer("EnemyEntity");
        }
    }
    private void SetStartingSelectionRadius()
    {
        if (IsMinion())
        {
            selectIndicator.UpdateRadius(minionController.ai.radius);
        }
        else
        {
            float offset = 0.1f;
            if (obstacle.type == NavmeshCut.MeshType.Circle)
            {
                selectIndicator.UpdateRadius(obstacle.circleRadius+offset);
            }
            else
            {
                float size = 1;
                if (obstacle.rectangleSize.x < obstacle.rectangleSize.y)
                {
                    size = obstacle.rectangleSize.x;
                }
                else
                {
                    size = obstacle.rectangleSize.y;
                }
                selectIndicator.UpdateRadius(size + offset);
            }
        }
    }
    private void SetInitialVisuals()
    {
        //FixFogTeam();
        SetHideFogTeam();
        UpdateTeamRenderers();
    }
    /// <summary>
    /// One time event to set fog hide team
    /// </summary>
    private void SetHideFogTeam()
    {
        //if we don't own this, then set its' hide fog team equal to our team
        /*if (FogHideable())
        {
        }*/
        if (Global.Instance.localPlayer != null)
        {
            hideFogTeam = (int)Global.Instance.localPlayer.OwnerClientId;
        }
    }
    public bool CannotConstructHarvestProduce()
    {
        return !CanConstruct() && !CanHarvest() && !CanProduceUnits();
    }

    //private bool teamRenderersUpdated = false;
    private void UpdateTeamRenderers()
    {
        foreach (MeshRenderer item in teamRenderers)
        {
            if (item != null)
            {
                ///item.material = Global.Instance.colors[System.Convert.ToInt32(net.OwnerClientId)];
                if (item.material != null && controllerOfThis != null) item.material.color = controllerOfThis.playerColor;
            }
        }
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
            //assign mission to last
            switch (spawnerRallyMission)
            {
                case RallyMission.None:
                    minionController.SetDestination(transform.position);
                    minionController.givenMission = spawnerRallyMission;
                    break;
                case RallyMission.Move:
                    minionController.SetDestination(rallyPoint);
                    minionController.givenMission = spawnerRallyMission;
                    break;
                case RallyMission.Harvest:
                    if (isHarvester)
                    {
                        interactionTarget = rallyTarget;
                        minionController.givenMission = spawnerRallyMission;
                    }
                    else
                    {
                        minionController.SetDestination(transform.position);
                    }
                    break;
                case RallyMission.Build:
                    interactionTarget = rallyTarget;
                    minionController.givenMission = spawnerRallyMission;
                    break;
                case RallyMission.Garrison:
                    interactionTarget = rallyTarget;
                    minionController.givenMission = spawnerRallyMission;
                    break;
                case RallyMission.Attack:
                    minionController.targetEnemy = rallyTarget;
                    minionController.givenMission = spawnerRallyMission;
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
            Debug.Log("Checking ability:" + ability.abilityName + "against: " + usedAbilities[i].abilityName);
            if (usedAbilities[i].abilityName == ability.abilityName) return false; //if ability is in the used abilities list, then we still need to wait  
        }
        return true;
    }
    public bool IsBuilding()
    {
        return minionController == null;
    }
    public void StartUsingAbility(FactionAbility ability)
    {
        abilityToUse = ability;
        Global.Instance.PlayMinionAbilitySound(this);
        if (minionController != null)
        {
            minionController.SwitchState(MinionController.MinionStates.UsingAbility);
        }
    }
    [HideInInspector] public FactionAbility abilityToUse;
    public void ActivateAbility(FactionAbility ability)
    {
        Debug.Log("Activating ability: " + ability.name);
        List<SelectableEntity> targetedEntities = new();
        foreach (TargetedEffects effect in ability.effectsToApply)
        {
            switch (effect.targets)
            {
                case TargetedEffects.Targets.Self:
                    if (!targetedEntities.Contains(this))
                    {
                        targetedEntities.Add(this);
                    }
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
                    case StatusEffect.AttackDuration:
                        if (target.minionController != null)
                        {
                            variableToChange = target.minionController.attackDuration;
                            secondVariable = target.minionController.impactTime;
                        }
                        break;
                    case StatusEffect.HP:
                        variableToChange = target.currentHP.Value;
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
                    case TargetedEffects.StatusEffect.AttackDuration:
                        if (target.minionController != null)
                        {
                            target.minionController.attackDuration = variableToChange;
                            target.minionController.impactTime = secondVariable;
                            target.minionController.animator.SetFloat("attackMultiplier", attackAnimMultiplier); //if we are halving, double animation speed
                        }
                        break;
                    case StatusEffect.HP:
                        variableToChange = Mathf.Clamp(variableToChange, 0, maxHP);
                        currentHP.Value = (short)variableToChange;
                        Debug.Log("setting hitpoints to: " + variableToChange);
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
                    case StatusEffect.DestroyThis:
                        target.DestroyThis();
                        break;
                    case StatusEffect.ToggleGate:
                        ToggleGate();
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
                    bool foundMatch = false;
                    foreach (TargetedEffects item in appliedEffects) //extend matching effects
                    {
                        if (item != null && item.status == newEffect.status && item.operation == newEffect.operation
                            && item.statusNumber == effect.statusNumber && item.expirationTime < newEffect.expirationTime)
                        {
                            foundMatch = true;
                            item.expirationTime = newEffect.expirationTime;
                            break;
                        }
                    }
                    if (!foundMatch)
                    {
                        appliedEffects.Add(newEffect);
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
    [HideInInspector] public List<AbilityOnCooldown> usedAbilities = new();
    [HideInInspector] public List<TargetedEffects> appliedEffects = new();
    private void UpdateUsedAbilities()
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
    private void UpdateAppliedEffects() //handle expiration of these effects; this implementation may be somewhat slow
    {
        for (int i = appliedEffects.Count - 1; i >= 0; i--)
        {
            appliedEffects[i].expirationTime -= Time.deltaTime;
            if (appliedEffects[i].expirationTime <= 0)
            {
                ResetVariableFromStatusEffect(appliedEffects[i]);
                appliedEffects.RemoveAt(i);
                /*TargetedEffects effect = appliedEffects[i];
                bool foundAnother = false;
                foreach (TargetedEffects item in appliedEffects)
                {
                    if (item == null || item == appliedEffects[i]) continue;
                    if (item.status == effect.status)
                    {
                        foundAnother = true;
                        break;
                    }
                }
                //search for another iteration of the same effect. if it doesn't exist, reset the variable
                if (!foundAnother)
                { 
                    ResetVariableFromStatusEffect(effect);
                } 
                appliedEffects.RemoveAt(i);*/

            }
        }
    }
    private void ResetVariableFromStatusEffect(TargetedEffects effect) //this will work for now but will not work if multiple buffs are stacked
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
            case TargetedEffects.StatusEffect.AttackDuration:
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
        if (!fullyBuilt && currentHP.Value >= maxHP) //detect if built
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
        if (currentHP.Value <= 0 && constructionBegun && alive) //detect death if "present" in game world ie not ghost/corpse
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
        if (!isBuildIndicator && !constructionBegun && currentHP.Value > 0)
        {
            constructionBegun = true;
            Unghost();
        }
    }
    private void Unghost() //sets building to look solid and not transparent
    {
        if (IsOwner) isTargetable.Value = true;

        if (allPhysicalColliders.Length > 1)
        {
            for (int i = 0; i < allPhysicalColliders.Length; i++)
            {
                allPhysicalColliders[i].isTrigger = false;
            }
        }
        else
        {
            physicalCollider.isTrigger = false;
        }

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
            AstarPath.active.navmeshUpdates.ForceUpdate();
            // Block until the updates have finished
            AstarPath.active.FlushGraphUpdates();
            //obstacle.DoUpdateGraphs();
            //AstarPath.active.FlushGraphUpdates();
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
            UpdateSelectionCirclePosition();
            if (fullyBuilt)
            {
                UpdateRallyVariables();
                UpdateTimers();
                UpdateAppliedEffects();
                UpdateUsedAbilities();
                //DetectIfDamaged();
                DetectChangeHarvestedResourceAmount();
                CheckIfShouldBeCaptured();
            }
        }
    }
    private void UpdateSelectionCirclePosition()
    {
        if (VisuallySelected())
        {
            if (selectIndicator != null) selectIndicator.UpdateSelectionCirclePosition();
        }
    }
    public bool VisuallySelected()
    {
        return selected || infoSelected;
    }
    private Camera mainCam;
    private void UpdateHealthBarPosition()
    {
        if (mainCam == null) mainCam = Global.Instance.localPlayer.mainCam;
        if (mainCam != null)
        {
            if (healthBar != null && healthBar.isActiveAndEnabled || productionProgressBar != null && productionProgressBar.isActiveAndEnabled)
            {
                healthBar.barParent.transform.position = transform.position + healthBarOffset;
                healthBar.barParent.transform.LookAt(transform.position + mainCam.transform.rotation * Vector3.forward,
                    mainCam.transform.rotation * Vector3.up);
            } 
        }
    }

    void LateUpdate() //Orient the camera after all movement is completed this frame to avoid jittering
    {
        UpdateHealthBarPosition();
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
                //walking sounds. client side only
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
                            Global.Instance.PlayClipAtPoint(Global.Instance.footsteps[UnityEngine.Random.Range(0, Global.Instance.footsteps.Length)], transform.position, .01f);
                        }
                    }
                }
            }
        }
    }
    public bool IsMelee()
    {
        float maxMeleeRange = 1.5f;
        if (minionController != null && minionController.attackRange <= maxMeleeRange)
        {
            return true;
        }
        return false;
    }
    [ServerRpc]
    public void ChangeHitPointsServerRpc(sbyte value)
    {
        currentHP.Value = value;
    }
    private void FixPopulationOnDeath()
    {
        ChangePopulation(-consumePopulationAmount);
        ChangeMaxPopulation(-raisePopulationLimitBy);
    }
    private List<Player> checkedPlayers = new();
    public void StartGameAddToEnemyLists()
    {
        //Debug.Log("num players " + Global.Instance.allPlayers.Count);
        foreach (Player player in Global.Instance.allPlayers)
        {
            if (controllerOfThis == null) break;
            if (player == null) continue;
            //Debug.Log(controllerOfThis.name + "name, id" + controllerOfThis.allegianceTeamID + "versus name id " + player.name + player.allegianceTeamID);

            if (controllerOfThis.allegianceTeamID != player.allegianceTeamID)
            {
                if (!checkedPlayers.Contains(player))
                {
                    checkedPlayers.Add(player);
                    player.enemyEntities.Add(this);
                    //Debug.Log("Adding " + name + " to " + player.name + "enemy list");
                }
            }
        }
    }
    public void MidGameUpdateEnemyListsAfterCapture()
    {
        foreach (Player player in Global.Instance.allPlayers)
        {
            if (controllerOfThis == null) break;
            if (player == null) continue;
            if (controllerOfThis.allegianceTeamID != player.allegianceTeamID)
            {
                player.enemyEntities.Add(this);
            }
            else
            {
                player.enemyEntities.Remove(this);
                player.visibleEnemies.Remove(this);
            }
        }
    }
    private void RemoveFromEnemyLists()
    {
        foreach (Player player in Global.Instance.allPlayers)
        {
            if (controllerOfThis == null) break;
            if (player == null) continue;
            if (controllerOfThis.allegianceTeamID != player.allegianceTeamID)
            {
                player.enemyEntities.Remove(this);
                player.visibleEnemies.Remove(this);
                /*if (IsMinion())
                {
                    player.enemyMinions.Add(this);
                }*/
            }
        }
    }
    public bool IsFighter()
    {
        return CannotConstructHarvestProduce() && IsMinion();
    }
    private bool hideModelOnDeath = false;
    
    public void MakeObstacle()
    {
        if (obstacle != null) obstacle.enabled = true;
    }
    public void ClearObstacle()
    {
        if (obstacle != null) obstacle.enabled = false;
    }
    public void PrepareForEntityDestruction()
    {
        if (lootOnDestructionComp != null) lootOnDestructionComp.LootForLocalPlayer();
        ClearObstacle();
        RemoveFromEnemyLists(); 
        if (healthBar != null) healthBar.Delete();
        if (productionProgressBar != null) productionProgressBar.Delete();
        Global.Instance.allEntities.Remove(this);
        controllerOfThis.ownedEntities.Remove(this);
        if (IsMinion())
        {
            controllerOfThis.ownedMinions.Remove(minionController);
            controllerOfThis.ownedBuilders.Remove(minionController);
        }
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

        foreach (MeshRenderer item in allMeshes)
        {
            if (IsStructure() || hideModelOnDeath)
            {
                if (item != null)
                {
                    item.enabled = false;
                }
            }
            else
            {
                if (item != null && !teamRenderers.Contains(item))
                {
                    item.material.color = Color.gray;
                }
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
        if (deathEffect != null)
        {
            Instantiate(deathEffect, transform.position, Quaternion.identity);
        }
        enabled = false;
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
        if (healthBar != null) healthBar.SetRatioBasedOnHP(current, maxHP);
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
                exiting.entity.occupiedGarrison = null;
                if (IsOwner)
                {
                    exiting.ChangeRVOStatus(true);
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
                }
                exiting.col.isTrigger = false;
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
    private sbyte damagedThreshold;
    private void CheckIfDamaged()
    {
        if (currentHP.Value <= damagedThreshold)
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
    public void RaiseHP(sbyte delta)
    {
        currentHP.Value = (sbyte)Mathf.Clamp(currentHP.Value + delta, 0, maxHP);
    }
    public void TakeDamage(sbyte damage) //always managed by SERVER
    {
        //currentHP.Value -= damage;
        currentHP.Value = (sbyte)Mathf.Clamp(currentHP.Value - damage, 0, maxHP);
    }
    public void Harvest(sbyte amount) //always managed by SERVER
    {
        //currentHP.Value -= amount;
        currentHP.Value = (sbyte)Mathf.Clamp(currentHP.Value - amount, 0, maxHP);
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
    private bool gateOpenStatus = true;
    private GameObject toggleableObject;
    private void ToggleGate()
    {
        gateOpenStatus = !gateOpenStatus;
        toggleableObject.SetActive(gateOpenStatus);
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
    private void UpdateVisibilityFromFogOfWar() //hide in fog
    {
        if (fow == null) fow = FogOfWarTeam.GetTeam(hideFogTeam);
        /* because "hidefogteam" is the local player id, a player's units will always be
        visible to themselves. */
        fogValue = fow.GetFogValue(transform.position); //get the value of the fog at this position
        isVisibleInFog = fogValue < Global.Instance.minFogStrength * 255;
        if (FogHideable())
        {
            //Debug.Log("running fog visibility for" + gameObject);
            if (isVisibleInFog != oldVisibleInFog || oneTimeForceUpdateFog == false) //update if there is a change
            {
                oneTimeForceUpdateFog = true;
                oldVisibleInFog = isVisibleInFog;
                UpdateMeshVisibility(isVisibleInFog);
            }
            for (int i = 0; i < attackEffects.Length; i++)
            {
                if (attackEffects[i] != null) attackEffects[i].enabled = showAttackEffects && isVisibleInFog;
            }
        }
        UpdateAreaEffectorVisibility(isVisibleInFog);
    }
    private void UpdateMeshVisibility(bool val)
    {
        if (IsStructure())
        {
            if (healthBar != null) healthBar.SetVisibleHPConditional(val);
            if (fullyBuilt)
            {
                SetFinishedRenderersVisibility(val);
            }
            else
            {
                SetUnfinishedRenderersVisibility(val);
            }
        }
        else //Is minion
        {
            UpdateAllMeshesVisibility(val);
        }
    }
    private void UpdateAllMeshesVisibility(bool val)
    { 
        for (int i = 0; i < allMeshes.Length; i++)
        {
            if (allMeshes[i] != null) allMeshes[i].enabled = val;
        }
    }
    private void UpdateAreaEffectorVisibility(bool val)
    { 
        for (int i = 0; i < areaEffectors.Length; i++)
        {
            if (areaEffectors[i] != null) areaEffectors[i].UpdateVisibility(val);
        }
    }
    private bool FogHideable()
    {
        return shouldHideInFog && IsSpawned && (!IsOwner || aiControlled);
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

    //bool hideFogTeamSet = false;


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
        foreach (MeshRenderer item in unbuiltRenderers)
        {
            if (item != null)
            {
                item.enabled = false;
            }
        }

        SetFinishedRenderersVisibility(true);
        if (fogUnit != null) fogUnit.enabled = true;
        ChangeMaxPopulation(raisePopulationLimitBy);

        if (!isBuildIndicator && obstacle != null && !obstacle.enabled)
        {
            obstacle.enabled = true;
        }

        if (IsOwner)
        {
            if (controllerOfThis != null) controllerOfThis.unbuiltStructures.Remove(this);
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
            if (minionController != null && minionController.pathfindingTarget != null) Destroy(minionController.pathfindingTarget.gameObject);
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
    public void SetRally() //later have this take in a vector3?
    {
        Debug.Log("Setting rally");
        rallyMission = RallyMission.Move;
        rallyTarget = null;
        //determine if spawned units should be given a mission

        Ray ray = Global.Instance.localPlayer.mainCam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity, Global.Instance.gameLayer))
        {
            rallyPoint = hit.point;
            if (rallyVisual != null)
            {
                rallyVisual.transform.position = rallyPoint;

            } 
            if (lineIndicator != null)
            {
                Vector3 offset = new Vector3(0, 0.01f, 0);
                lineIndicator.SetPosition(0, transform.position + offset);
                lineIndicator.SetPosition(1, rallyPoint + offset);
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
    public bool IsStructure()
    {
        return minionController == null;
    }
    private void OwnerUpdateBuildQueue()
    {
        if (buildQueue.Count > 0)
        {
            // todo add ability to build multiple from one structure
            FactionUnit fac = buildQueue[0];
            fac.spawnTimer++; 
            if (fac.spawnTimer > fac.maxSpawnTimeCost - 1) //ready to spawn
            {   
                if (fac.consumePopulationAmount <= controllerOfThis.maxPopulation - controllerOfThis.population) //allowed to spawn
                { 
                    //spawn the unit 
                    //Debug.Log("Population check on spawn:" + fac.consumePopulationAmount + ", " + controllerOfThis.maxPopulation + ", " + controllerOfThis.population);
                    //first check if the position is blocked;
                    if (Physics.Raycast(positionToSpawnMinions.position + (new Vector3(0, 100, 0)), Vector3.down,
                        out RaycastHit hit, Mathf.Infinity, Global.Instance.gameLayer))
                    {
                        SelectableEntity target = Global.Instance.FindEntityFromObject(hit.collider.gameObject);
                        if (target != null) //something blocking
                        {
                            if (target.minionController != null && target.controllerOfThis == controllerOfThis)
                            {
                                //tell blocker to get out of the way.
                                float randRadius = 1;
                                Vector2 randCircle = UnityEngine.Random.insideUnitCircle * randRadius;
                                Vector3 rand = target.transform.position + new Vector3(randCircle.x, 0, randCircle.y);
                                target.minionController.MoveTo(rand);
                                //Debug.Log("trying to move blocking unit to: " + rand);
                                productionBlocked = true;
                            }
                        }
                        else
                        {
                            BuildQueueSpawn(fac);
                            productionBlocked = false;
                        }
                    } 
                }
                else
                {
                    productionBlocked = true;
                }
            }
        }
        if (controllerOfThis is RTSPlayer)
        {
            RTSPlayer rts = controllerOfThis as RTSPlayer;
            rts.UpdateBuildQueueGUI();
        }

        if (productionProgressBar != null)
        {
            if (buildQueue.Count > 0)
            {
                productionProgressBar.SetVisible(true);
                FactionUnit fac = buildQueue[0];
                if (fac != null)
                {
                    productionProgressBar.SetRatioBasedOnProduction(fac.spawnTimer, fac.maxSpawnTimeCost);
                }
            }
            else
            {
                productionProgressBar.SetVisible(false);
            }
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
    [HideInInspector] public List<SelectableEntity> whenTheseEntitiesKilledTriggerBehavior;
    public enum TriggerableBehaviors
    {
        CaptureThis,
    }
    public TriggerableBehaviors triggerBehavior = TriggerableBehaviors.CaptureThis;
    private bool shouldCheckTrigger = false;
    private void CheckIfShouldBeCaptured()
    {
        if (shouldCheckTrigger)
        {
            bool capture = false;
            if (whenTheseEntitiesKilledTriggerBehavior.Count <= 0) capture = true;

            if (capture)
            {
                TriggerBehavior();
            }
            else
            {
                capture = true;
                foreach (SelectableEntity item in whenTheseEntitiesKilledTriggerBehavior)
                {
                    if (item != null && item.alive)
                    {
                        capture = false;
                        break;
                    }
                }
                if (capture)
                {
                    TriggerBehavior();
                }
            }
        }
    }
    private void TriggerBehavior()
    {
        switch (triggerBehavior)
        {
            case TriggerableBehaviors.CaptureThis:

                CaptureForLocalPlayer();
                break;
            default:
                break;
        }
        shouldCheckTrigger = false;
    }
    public void CaptureForLocalPlayer() //switch team of entity
    {
        //Debug.Log("capturing");
        controllerOfThis = Global.Instance.localPlayer;
        if (controllerOfThis != null)
        {
            teamNumber.Value = (sbyte)controllerOfThis.playerTeamID;

            if (fogUnit != null) fogUnit.team = controllerOfThis.playerTeamID;
        }
        UpdateTeamRenderers();

        ChangePopulation(consumePopulationAmount);
        ChangeMaxPopulation(raisePopulationLimitBy);
        RTSPlayer playerController = Global.Instance.localPlayer;
        playerController.ownedEntities.Add(this);
        if (IsMinion()) playerController.ownedMinions.Add(minionController);

        if (factionEntity.constructableBuildings.Length > 0)
        {
            playerController.ownedBuilders.Add(minionController);
        }
        if (IsNotYetBuilt())
        {
            playerController.unbuiltStructures.Add(this);
        }
        //update layer
        gameObject.layer = LayerMask.NameToLayer(Global.Instance.FRIENDLY_ENTITY);
        MidGameUpdateEnemyListsAfterCapture();
        PlayCaptureEffect();
    }
    private void PlayCaptureEffect()
    {
        if (Global.Instance.defaultCaptureEffect != null)
        {
            Instantiate(Global.Instance.defaultCaptureEffect, transform.position, Quaternion.identity);
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
    private GameObject deathEffect = null;
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
    public void UpdateIndicator(bool val)
    {
        ChangeSelectIndicatorStatus(val);
        UpdateRallyVisual(val);
        //UpdateTargetIndicator();
    }
    private void UpdateRallyVisual(bool val)
    {
        if (rallyVisual != null)
        {
            if (CanProduceUnits())
            {
                rallyVisual.transform.position = rallyPoint;
                rallyVisual.SetActive(val);
            }
            else
            {
                rallyVisual.SetActive(false);
            }
        }
        if (lineIndicator != null)
        {
            lineIndicator.enabled = val;
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
        UpdateIndicator(selected);
    }
    [HideInInspector] public bool infoSelected = false;
    public void InfoSelect(bool val)
    {
        infoSelected = val;
        UpdateIndicator(val);
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
}
