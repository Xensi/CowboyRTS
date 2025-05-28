using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding;
using Pathfinding.RVO;
using FoW;
using static Effect;
using static StateMachineController;
using static UnitAnimator;

/// <summary>
/// Handles core behavior, like selection, HP, destruction, etc.
/// </summary>
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
    #endregion
    #region NetworkVariables 
    [HideInInspector] public NetworkVariable<bool> isTargetable = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    #endregion

    //Hidden variables 
    [HideInInspector] public bool isVisibleInFog = false;
    [HideInInspector] public bool oldVisibleInFog = false;
    [HideInInspector] public int hideFogTeam = 0; //set equal to the team whose fog will hide this. in mp this should be set equal to the localplayer's team
    [HideInInspector] public bool shouldHideInFog = true; // gold should not be hidden 
    [HideInInspector] public NavmeshCut obstacle;
    [HideInInspector] public int localTeamNumber = 0;
    [HideInInspector] public sbyte desiredTeamNumber = 0; //Set when spawned by AI. Only matters if negative
    [HideInInspector] public byte clientIDToSpawnUnder = 0;
    [HideInInspector] public bool aiControlled = false;
    [HideInInspector] public bool productionBlocked = false;
    [HideInInspector] public Collider[] allPhysicalColliders;

    [Header("Must Be Manually Set")]
    public EntityHealthBar healthBar;
    public EntityHealthBar productionProgressBar;
    public FactionEntity factionEntity;
    [SerializeField] private GameObject finishedRendererParent;
    [SerializeField] private GameObject rallyVisual;
    //[SerializeField] private MeshRenderer[] damageableMeshes;
    public MeshRenderer[] attackEffects;
    public GameObject targetIndicator;
    public LineRenderer lineIndicator;
    public List<MeshRenderer> teamRenderers;

    //Add on components

    [HideInInspector] public bool selected = false;
    [HideInInspector] public Vector3 rallyPoint;
    [HideInInspector] public bool alive = true;

    [HideInInspector] public SelectableEntity occupiedGarrison;
    [HideInInspector] public bool isBuildIndicator = false;
    //[HideInInspector] public int harvestedResourceAmount = 0; //how much have we already collected

    [Header("Debug")]
    public SelectableEntity interactionTarget;

    [HideInInspector] public List<FactionUnit> buildQueue;
    [SerializeField] private MeshRenderer[] unbuiltRenderers;
    private AreaEffector[] areaEffectors;
    //when fog of war changes, check if we should hide or show attack effects
    private bool damaged = false;
    private readonly int delay = 50;
    private int count = 0;
    #region Variables
    [Header("Behavior Settings")]
    [HideInInspector] public string displayName = "name";
    //[TextArea(2, 4)]
    [HideInInspector]
    public string desc = "desc";
    public NetworkVariable<short> currentHP = new(); 
    private short startingHP = 10; //buildings usually don't start with full HP
    [HideInInspector] public short maxHP = 10; 
    [HideInInspector]
    public TeamBehavior teamType = TeamBehavior.OwnerTeam;
    [HideInInspector]
    public EntityTypes entityType = EntityTypes.Melee;
    [HideInInspector]
    public bool isHeavy = false; //heavy units can't garrison into pallbearers
    [HideInInspector] public bool fullyBuilt = true; //[HideInInspector] 
    [HideInInspector]
    public bool isKeystone = false;
    public int allowedWorkers = 1; //how many can build/repair/harvest at a time
    public int allowedInteractors = 10; //how many can interact (not build/repair)
    public List<SelectableEntity> workersInteracting;
    public List<SelectableEntity> othersInteracting;

    [Header("Building Only")]
    [HideInInspector]
    public Vector3 buildOffset = new Vector3(0.5f, 0, 0.5f);
    //public FactionAbility[] usableAbilities { get; set; } 

    [HideInInspector]
    public bool passengersAreTargetable = false;
    [HideInInspector]
    public bool acceptsHeavy = false;

    public Transform positionToSpawnMinions; //used for buildings

    [Header("Aesthetic Settings")]
    private Material damagedState;
    [HideInInspector] public AudioClip[] sounds; //0 spawn, 1 attack, 2 attackMove
    public SelectionCircle selectIndicator;
    [HideInInspector]
    public NetworkVariable<sbyte> teamNumber = new NetworkVariable<sbyte>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); //negative team numbers are AI controlled

    //set by AI player when spawned in dynamically
    #endregion
    #region NetworkSpawn 
    //private bool hasRegisteredRallyMission = false;
    private List<Material> savedMaterials = new();
    public Player controllerOfThis;

    private bool oneTimeForceUpdateFog = false;
    //[SerializeField]
    private float fogValue;
    //[SerializeField]
    private FogOfWarTeam fow;
    public bool isAttackable = true;
    #region Automatically Set
    //Automatically set
    [HideInInspector] public Rigidbody rigid; 
    private LootOnDestruction lootComponent;
    [HideInInspector] public UnitAbilities unitAbilities;
    [HideInInspector] public UnitAnimator anim; //Entities that can be deposited to.
    [HideInInspector] public Depot depot; //Entities that can be deposited to.
    [HideInInspector] public Ore ore; //Entities that are harvestable for resources
    [HideInInspector] public Harvester harvester; //Entities that can harvest resources
    [HideInInspector] public StateMachineController sm; //Entities that need to be able to switch states
    [HideInInspector] public Builder builder; //Entities that need to pathfind
    [HideInInspector] public Spawner spawner;
    public Pathfinder pf;
    [HideInInspector] public Garrison garrison;
    private MeshRenderer[] allMeshes;
    private MeshRenderer[] finishedMeshRenderers;
    [HideInInspector] public Collider physicalCollider; 
    [HideInInspector] public RallyMission rallyMission;
    [HideInInspector] public SelectableEntity rallyTarget;
    [HideInInspector]
    public int consumePopulationAmount = 1;
    [HideInInspector]
    public int raisePopulationLimitBy = 0;
    [HideInInspector] public NetworkObject net;
    [HideInInspector] public Attacker attacker;
    public CaptureFlag capFlag;
    #endregion
    #region Just Spawned Code
    #region Awake Code 
    private void Awake() //Awake should be used to initialize component automatically
    { //in scene placed: Awake, Start, NetworkSpawn //Dynamic: awake, networkspawn, start 
        //Debug.Log("Awake");
        Initialize();
        InitializeEntityInfo();
        initialized = true;
    }
    private void Initialize()
    {
        //Used by all entities
        allMeshes = GetComponentsInChildren<MeshRenderer>();
        if (finishedRendererParent != null) finishedMeshRenderers = finishedRendererParent.GetComponentsInChildren<MeshRenderer>();
        if (rigid == null) rigid = GetComponent<Rigidbody>();
        if (fogUnit == null) fogUnit = GetComponent<FogOfWarUnit>();
        if (net == null) net = GetComponent<NetworkObject>();
        if (obstacle == null) obstacle = GetComponentInChildren<NavmeshCut>();
        if (physicalCollider == null) physicalCollider = GetComponent<Collider>();
        selectIndicator = GetComponentInChildren<SelectionCircle>(); 
        //Modular entity add ons
        if (harvester == null) harvester = GetComponent<Harvester>();
        ore = GetComponent<Ore>();
        depot = GetComponent<Depot>();
        anim = GetComponent<UnitAnimator>();
        builder = GetComponent<Builder>();
        spawner = GetComponent<Spawner>();
        unitAbilities = GetComponent<UnitAbilities>();
        attacker = GetComponent<Attacker>();
        pf = GetComponent<Pathfinder>();
        garrison = GetComponent<Garrison>();
        //soft addons
        areaEffectors = GetComponentsInChildren<AreaEffector>();
        if (lootComponent == null) lootComponent = GetComponent<LootOnDestruction>();
        //Not used by all entities
        if (sm == null) sm = GetComponent<StateMachineController>();
        if (sm != null)
        {
            sm.pf = pf;
            sm.anim = anim;
            sm.attacker = attacker;
        }
        if (capFlag == null) capFlag = GetComponentInChildren<CaptureFlag>();
        if (capFlag != null) capFlag.ent = this;
    }  
    private void InitializeEntityInfo()
    {
        //add more initializations; for pathfinder, for depot, etc.
        if (factionEntity != null)
        { 
            //set faction entity information
            deathEffect = factionEntity.deathEffect;
            displayName = factionEntity.productionName;
            desc = factionEntity.description;
            maxHP = (short)factionEntity.maxHP;
            //allowedUnfinishedInteractors = factionEntity.allowedUnfinishedInteractors;
            //allowedFinishedInteractors = factionEntity.allowedFinishedInteractors;
            //in the future, number of interactors will be per addon; different number of allowed harvesters, depositers, etc.
            allowedInteractors = 100;

            passengersAreTargetable = factionEntity.passengersAreTargetable;
            acceptsHeavy = factionEntity.acceptsHeavy;

            consumePopulationAmount = factionEntity.consumePopulationAmount;
            raisePopulationLimitBy = factionEntity.raisePopulationLimitBy;
            //DIFFERENTIATE BETWEEN BUILDING AND UNIT TYPES 
            teamType = factionEntity.teamType;
            shouldHideInFog = factionEntity.shouldHideInFog;

            if (fogUnit != null) fogUnit.circleRadius = factionEntity.visionRange;

            if (factionEntity.soundProfile != null)
            {
                sounds = factionEntity.soundProfile.sounds;
            }
            else
            {
                sounds = new AudioClip[0];
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
            }
            hideModelOnDeath = factionEntity.hideModelOnDeath;
        }
    }
    #endregion 
    public override void OnNetworkSpawn() //Netcode related initialization ONLY
    {
        //Debug.Log("Network Spawn");

        if (IsOwner)
        {
            InitializeBasedOffController();

            //place effect dependent on "controller of this" being defined after this line!
            if (controllerOfThis != null)
            {
                StartGameAddToEnemyLists();
                if (fogUnit != null) fogUnit.team = controllerOfThis.playerTeamID;
            }
        }
        if (IsServer)
        {
            currentHP.Value = startingHP;
        }
        currentHP.OnValueChanged += OnHitPointsChanged;

        localTeamNumber = System.Convert.ToInt32(net.OwnerClientId);
    }
    #region Network Spawn Code
    private void InitializeBasedOffController()
    {
        if (controllerOfThis != null)
        {
            ControllerFound();
        }
        else //has no controller already (dynamically spawned, or neutral and placed in manually)
        {
            ControllerNull();
        }
    }
    private void ControllerFound() // placed in scene manually and AI controlled
    { 
        teamNumber.Value = (sbyte)controllerOfThis.playerTeamID;
        if (teamType == TeamBehavior.OwnerTeam)
        {
            AddToPlayerOwnedLists(controllerOfThis);
        }
    }
    private void ControllerNull()
    {
        if (desiredTeamNumber < 0) //AI controlled
        {
            DynamicallySpawnedUnderAIControl();
        }
        else //player controlled or friendly neutral
        {
            if (teamType == TeamBehavior.OwnerTeam)
            {
                SpawnedUnderPlayerControl();
            }
            else if (teamType == TeamBehavior.FriendlyNeutral)
            {
                PlacedInSceneAsNeutral();
            }
        }
    }
    private void SpawnedUnderPlayerControl()
    { 
        RTSPlayer playerController = Global.Instance.localPlayer;
        playerController.lastSpawnedEntity = this;
        controllerOfThis = playerController;
        AddToPlayerOwnedLists(controllerOfThis);
        if (!fullyBuilt)
        {
            RequestBuilders();
        }
        teamNumber.Value = (sbyte)OwnerClientId;
    }
    private void PlacedInSceneAsNeutral()
    {
        if (IsOre())
        { 
            foreach (Player player in Global.Instance.allPlayers)
            {
                AddToPlayerOreList(player);
            }
        } 
    }
    private void DynamicallySpawnedUnderAIControl()
    { 
        teamNumber.Value = desiredTeamNumber;
        if (teamType == TeamBehavior.OwnerTeam)
        {
            AIPlayer AIController = Global.Instance.aiPlayers[Mathf.Abs(desiredTeamNumber) - 1];
            controllerOfThis = AIController;
            AddToPlayerOwnedLists(controllerOfThis);
        }
    } 
    #endregion
    private void Start() //Non netcode related initialization
    { 
        if (currentHP.Value <= 0 && !constructionBegun) //buildings begun as untargetable (by enemies)
        {
            isTargetable.Value = false;
        }
        else
        {
            isTargetable.Value = true;
        }
        if (teamType == TeamBehavior.OwnerTeam) ChangePopulation(consumePopulationAmount);
        Select(false);
        rallyPoint = transform.position;
        ChangeSelectIndicatorStatus(selected);
        if (lineIndicator != null)
        {
            lineIndicator.enabled = false;
        }
        if (targetIndicator != null)
        {
            targetIndicator.transform.parent = Global.Instance.transform;
        }
        SetStartingSelectionRadius();
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

        InitializeBars();
        DetermineLayerBasedOnAllegiance();
        PlaySpawnSound();
    }
    #region Start() Code
    private void DetermineLayerBasedOnAllegiance()
    { 
        if (controllerOfThis != null && controllerOfThis.allegianceTeamID != Global.Instance.localPlayer.allegianceTeamID)
        {   //this should be counted as an enemy 
            gameObject.layer = LayerMask.NameToLayer("EnemyEntity");
        }
    }
    private void InitializeBars()
    { 
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
        RetainHealthBarPosition();
    }
    #endregion
    #endregion
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
        if (!IsHarvester()) return;
        /*if (harvestedResourceAmount != oldHarvestedResourceAmount)
        {
            oldHarvestedResourceAmount = harvestedResourceAmount;
            //UpdateResourceCollectableMeshes();
        }*/
    }
    public bool IsMinion() //Minions typically move around
    {
        return sm != null && pf != null;
    }
    public bool IsHarvester()
    {
        return harvester != null;
    }
    public bool IsAttacker()
    {
        return attacker != null;
    }
    public bool fullyBuiltInScene = false; //set to true to override needs constructing value 
    public bool IsDamaged()
    {
        return currentHP.Value < maxHP;
    }
    public bool IsOre()
    {
        return ore != null;
    }
    private Vector3 healthBarOffset;
    private void RetainHealthBarPosition()
    {
        healthBarOffset = healthBar.transform.position - transform.position;
    }


    [HideInInspector] public bool initialized = false;

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
        return sm != null;
    }
    public bool IsDepot()
    {
        return depot != null;
    }
    private void AddToPlayerOwnedLists(Player player)
    {
        //Debug.Log("Adding to player lists");
        player.ownedEntities.Add(this);
        if (IsMinion())
        {
            //Debug.Log("Adding to minion list");
            player.ownedMinions.Add(sm);
        }
        else
        {
            //Debug.Log("Not a minion");
        }
        if (IsNotYetBuilt()) player.unbuiltStructures.Add(this);
        if (IsHarvester()) player.ownedHarvesters.Add(harvester);
        if (IsDepot()) player.ownedDepots.Add(depot);
    }
    private void AddToPlayerOreList(Player player)
    { 
        if (IsOre()) player.friendlyOres.Add(ore);
    }
    private void ChangeSelectIndicatorStatus(bool val)
    {
        if (selectIndicator != null)
        {
            //selectIndicator.gameObject.SetActive(true);
            selectIndicator.UpdateVisibility(val);
            Color selectColor = Color.green;
            if (Global.Instance.localPlayer != controllerOfThis)
            {
                selectColor = Color.red;
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
    private void SetStartingSelectionRadius()
    {
        if (IsMinion())
        {
            selectIndicator.UpdateRadius(pf.ai.radius); //TODO ERROR
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
    public bool HasResourcesToDeposit()
    {
        return harvester != null && harvester.BagContainsResources();
    }
    public bool CannotConstructHarvestProduce()
    {
        return !IsBuilder() && !IsHarvester() && !IsSpawner();
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
        if (spawnerThatSpawnedThis != null && sm != null)
        {
            //Debug.Log("mission registered");
            RallyMission spawnerRallyMission = spawnerThatSpawnedThis.rallyMission;
            SelectableEntity rallyTarget = spawnerThatSpawnedThis.rallyTarget;
            Vector3 rallyPoint = spawnerThatSpawnedThis.rallyPoint;
            //assign mission to last
            switch (spawnerRallyMission)
            {
                case RallyMission.None:
                    if (pf != null) pf.SetDestination(transform.position);
                    sm.givenMission = spawnerRallyMission;
                    break;
                case RallyMission.Move:
                case RallyMission.Attack: 
                    if (pf != null)
                    {
                        pf.SetDestination(rallyPoint);
                        Debug.Log("set dest" + rallyPoint);
                    }
                    sm.givenMission = spawnerRallyMission;
                    break;
                case RallyMission.Harvest:
                    if (IsHarvester())
                    {
                        interactionTarget = rallyTarget;
                        sm.givenMission = spawnerRallyMission;
                    }
                    else
                    {
                        if (pf != null) pf.SetDestination(transform.position);
                    }
                    break;
                case RallyMission.Build:
                    interactionTarget = rallyTarget;
                    sm.givenMission = spawnerRallyMission;
                    break;
                case RallyMission.Garrison:
                    interactionTarget = rallyTarget;
                    sm.givenMission = spawnerRallyMission;
                    break;
            }
        }
    }
    public bool InRangeOfEntity(SelectableEntity target, float range)
    {
        if (target == null) return false;
        if (target.physicalCollider != null) //get closest point on collider; //this has an issue
        {
            Vector3 centerToMax = target.physicalCollider.bounds.center - target.physicalCollider.bounds.max;
            float boundsFakeRadius = centerToMax.magnitude;
            float discrepancyThreshold = boundsFakeRadius + .5f;
            Vector3 closest = target.physicalCollider.ClosestPoint(transform.position);
            float rawDist = Vector3.Distance(transform.position, target.transform.position);
            float closestDist = Vector3.Distance(transform.position, closest);
            if (Mathf.Abs(rawDist - closestDist) > discrepancyThreshold)
            {
                return rawDist <= range;
            }
            else
            {
                return closestDist <= range;
            }
        }
        else //check dist to center
        {
            return Vector3.Distance(transform.position, target.transform.position) <= range;
        }
    }
    public bool IsBuilding()
    {
        return sm == null;
    }
    private void UpdateRallyVariables()
    {
        if (rallyTarget != null) //update rally visual and rally point to rally target position
        {
            rallyVisual.transform.position = rallyTarget.transform.position;
            rallyPoint = rallyTarget.transform.position;
        }
    }
    [HideInInspector] public bool constructionBegun = false;
    private void DetectIfBuilt()
    {
        if (!fullyBuilt && currentHP.Value >= maxHP) //detect if built
        {
            BecomeFullyBuilt();
        }
    } 
    private void DetectIfShouldDie()
    {
        if (sm != null && !alive && !sm.InState(EntityStates.Die)) //force go to death state
        {
            sm.SwitchState(EntityStates.Die);
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
            if (AstarPath.active != null)
            {
                AstarPath.active.navmeshUpdates.ForceUpdate();
                // Block until the updates have finished
                AstarPath.active.FlushGraphUpdates();
                //obstacle.DoUpdateGraphs();
                //AstarPath.active.FlushGraphUpdates();
            }
        }
    }
    private bool HasStateMachine()
    {
        return sm != null;
    }
    private void Update()
    {
        if (IsSpawned)
        {
            DetectIfShouldDie();
            if (HasStateMachine() && sm.InState(EntityStates.Die)) return;
            UpdateVisibilityFromFogOfWar();
            DetectIfShouldUnghost();
            DetectIfBuilt();
            UpdateInteractors();
            //UpdateSelectionCirclePosition();
            if (fullyBuilt)
            {
                UpdateRallyVariables();
                UpdateTimers();
                UpdateAppliedEffects();
                UpdateUsedAbilities();
                DetectChangeHarvestedResourceAmount();
                //DetectIfDamaged();
                UpdateEntityAddons();
            }
        }
    }
    private void UpdateEntityAddons()
    {
        if (pf != null) pf.UpdateAddon();
    }
    private void UpdateUsedAbilities()
    {
        if (HasAbilities()) unitAbilities.UpdateUsedAbilities();
    }
    public bool HasAbilities()
    {
        return unitAbilities != null;
    }
    public bool CanUseAbility(FactionAbility ability)
    {
        return HasAbilities() && unitAbilities.CanUseAbility(ability);
    } 
    [HideInInspector] public List<Effect> appliedEffects = new();
    private void UpdateAppliedEffects() //handle expiration of these effects; this implementation may be somewhat slow
    {
        for (int i = appliedEffects.Count - 1; i >= 0; i--)
        {
            if (appliedEffects[i].repeatWhileLingering)
            {
                appliedEffects[i].internalTimer += Time.deltaTime;
                if (appliedEffects[i].internalTimer >= appliedEffects[i].repeatTime)
                {
                    appliedEffects[i].internalTimer = 0;
                    ApplyEffect(appliedEffects[i]);
                }
            }
            appliedEffects[i].expirationTime -= Time.deltaTime;
            if (appliedEffects[i].expirationTime <= 0)
            {
                ResetVariableFromStatusEffect(appliedEffects[i]);
                appliedEffects.RemoveAt(i);
            }
        }
    }
    public void ApplyEffect(Effect effect)
    {
        bool validApply = true;
        SelectableEntity target = this;

        float variableToChange = 0;
        float secondVariable = 0;
        switch (effect.status) //set variable to change;
        {
            case StatusEffect.MoveSpeed:
                /*if (target.sm != null && target.sm.ai != null)
                {
                    variableToChange = target.sm.ai.maxSpeed;
                }*/
                break;
            case StatusEffect.AttackDuration:
                if (target.sm != null)
                {
                    //variableToChange = target.sm.attackDuration;
                    //secondVariable = target.sm.impactTime;
                }
                break;
            case StatusEffect.HP:
                variableToChange = target.currentHP.Value;
                break;
        }
        float originalValue = variableToChange;
        float attackAnimMultiplier = 1;
        float moveSpeedMultiplier = 1;
        switch (effect.operation) //apply change to variable
        {
            case Effect.Operation.Set:
                variableToChange = effect.statusNumber;
                secondVariable = effect.statusNumber;
                break;
            case Effect.Operation.Add:
                variableToChange += effect.statusNumber;
                secondVariable += effect.statusNumber;
                break;
            case Effect.Operation.Multiply:
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
        //apply effect
        switch (effect.status) //set actual variable to new variable
        {
            case Effect.StatusEffect.MoveSpeed:
                /*if (target.sm != null && target.sm.ai != null)
                {
                    target.sm.ai.maxSpeed = variableToChange;
                    target.sm.animator.SetFloat("moveSpeedMultiplier", moveSpeedMultiplier); //if we are halving, double animation speed
                }*/
                break;
            case Effect.StatusEffect.AttackDuration:
                if (target.sm != null)
                {
                    //target.sm.attackDuration = variableToChange;
                    //target.sm.impactTime = secondVariable;
                    //target.sm.animator.SetFloat("attackMultiplier", attackAnimMultiplier); //if we are halving, double animation speed
                }
                break;
            case StatusEffect.HP:
                variableToChange = Mathf.Clamp(variableToChange, 0, target.maxHP);
                target.currentHP.Value = (short)variableToChange;
                //Debug.Log("setting hitpoints to: " + variableToChange);
                if (originalValue == variableToChange) validApply = false; //if no change, apply was not valid
                break;
            case StatusEffect.CancelInProgress:
                //if target is ghost, full refund
                //if construction in progress, half refund
                if (target.constructionBegun)
                {
                    Global.Instance.localPlayer.AddGold(target.factionEntity.goldCost / 2);
                }
                else
                {
                    Global.Instance.localPlayer.AddGold(target.factionEntity.goldCost);
                }
                target.DestroyThis();
                break;
            case StatusEffect.ToggleGate:
                target.ToggleGate();
                break;
        }

        if (validApply && effect.particles != null) Instantiate(effect.particles, transform);
    }
    private bool HasUnitAnimator()
    {
        return anim != null;
    }
    private void ResetVariableFromStatusEffect(Effect effect) //this will work for now but will not work if multiple buffs are stacked
    {
        switch (effect.status)
        {
            case Effect.StatusEffect.MoveSpeed:
                if (sm != null && pf.ai != null)
                {
                    pf.ai.maxSpeed = pf.defaultMoveSpeed; 
                    if (HasUnitAnimator()) anim.ResetMultiplier(MOVE_SPEED);
                }
                break;
            case Effect.StatusEffect.AttackDuration:
                if (IsAttacker())
                {
                    attacker.duration = attacker.defaultAttackDuration;
                    attacker.impactTime = attacker.defaultImpactTime;
                    if (HasUnitAnimator()) anim.ResetMultiplier(ATTACK_SPEED);
                } 
                break;
            default:
                break;
        }
    }
    private void UpdateSelectionCirclePosition()
    {
        if (VisuallySelected())
        {
            if (selectIndicator != null) selectIndicator.UpdateSelectionCirclePositions();
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
                if (sm != null)
                {
                    if (anim.InState(BEGIN_ATTACK_WALK)
                        || anim.InState(CONTINUE_ATTACK_WALK)
                        || anim.InState(WALK)
                        )
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
    public void LookAtTarget(Transform target)
    {
        if (target != null)
        {
            /*transform.rotation = Quaternion.LookRotation(
                Vector3.RotateTowards(transform.forward, target.position - transform.position, Time.deltaTime * rotationSpeed, 0));*/
            transform.LookAt(new Vector3(target.position.x, transform.position.y, target.position.z));
        }
    }
    public bool IsMelee()
    {
        if (!IsAttacker()) return false;
        float maxMeleeRange = 1.5f;
        if (attacker.range <= maxMeleeRange)
        {
            return true;
        }
        return false;
    }
    public bool IsRanged()
    {
        return !IsMelee();
    }
    [ServerRpc]
    public void ChangeHitPointsServerRpc(sbyte value)
    {
        currentHP.Value = value;
    }
    private void FixPopulationOnDeath()
    {
        Debug.Log("Adjusting pop " + consumePopulationAmount + name);
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
    private void RemoveFromPlayerLists(Player player)
    {
        if (player == null) return;
        player.ownedEntities.Add(this);
        if (IsMinion())
        {
            player.ownedMinions.Add(sm);
            player.ownedBuilders.Remove(sm);
        }
        if (IsNotYetBuilt()) player.unbuiltStructures.Add(this);
        if (IsHarvester()) player.ownedHarvesters.Add(harvester);
        if (IsOre()) player.friendlyOres.Add(ore);
        if (IsDepot()) player.ownedDepots.Add(depot);
    } 
    private bool IsLoot()
    {
        return lootComponent != null;
    }
    private new void OnDestroy()
    {
        PrepareForEntityDestruction();
    }
    bool deathEffectPlayed = false;
    bool entityDestructionPrepped = false;
    public void PrepareForEntityDestruction()
    {
        if (entityDestructionPrepped) return;
        entityDestructionPrepped = true;
        if (attacker != null) attacker.RemoveFromEntitySearcher();
        if (IsLoot()) lootComponent.LootForLocalPlayer();

        if (selectIndicator != null) Destroy(selectIndicator.gameObject);

        ClearObstacle();
        RemoveFromEnemyLists(); 
        if (healthBar != null) healthBar.Delete();
        if (productionProgressBar != null) productionProgressBar.Delete();
        Global.Instance.allEntities.Remove(this);

        RemoveFromPlayerLists(controllerOfThis); 

        if (IsOwner)
        {
            FixPopulationOnDeath();
        }
        else
        {
            if (HasUnitAnimator()) anim.Play(DIE); 
        }
        if (fogUnit != null)
        {
            fogUnit.enabled = false;
        }
        /*foreach (GarrisonablePosition item in garrisonablePositions)
        {
            if (item != null)
            {
                if (item.passenger != null)
                {
                    UnloadPassenger(item.passenger);
                }
            }
        }*/

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
        if (CanPathfind())
            pf.FreezeRigid(true, true);
        if (sm != null)
        {
            sm.PrepareForDeath();

            Invoke(nameof(Die), deathDuration);
        }
        else
        {
            //StructureCosmeticDestruction();
            Invoke(nameof(Die), deathDuration); //structures cannot be deleted immediately because we need some time
            //for values to updated. better method is to destroy cosmetically
        }
        if (deathEffect != null && !deathEffectPlayed)
        {
            deathEffectPlayed = true;
            Instantiate(deathEffect, transform.position, Quaternion.identity);
        }
        enabled = false;
    }
    private bool CanPathfind()
    {
        return pf != null;
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
    public void ReceivePassenger(StateMachineController newPassenger)
    {
        /*foreach (GarrisonablePosition item in garrisonablePositions)
        {
            if (item != null)
            {
                if (item.passenger == null)
                {
                    item.passenger = newPassenger;
                    //newPassenger.transform.parent = item.transform;
                    newPassenger.ent.occupiedGarrison = this;
                    if (IsOwner)
                    {
                        newPassenger.ent.isTargetable.Value = passengersAreTargetable;
                        if (newPassenger.minionNetwork != null)
                        {
                            newPassenger.minionNetwork.verticalPosition.Value = item.transform.position.y;
                        }
                    }
                    newPassenger.col.isTrigger = true;
                    Global.Instance.localPlayer.DeselectSpecific(newPassenger.ent);
                    //newPassenger.minionNetwork.positionDifferenceThreshold = .1f;
                    //newPassenger.minionNetwork.ForceUpdatePosition(); //update so that passengers are more in the correct y-position
                    break;
                }
            }
        }*/
    }
    public bool IsEnemyOfPlayer(Player player)
    {
        return controllerOfThis.allegianceTeamID != player.allegianceTeamID;
    }
    public bool IsEnemyOfTarget(SelectableEntity target)
    {
        if (target != null)
        {
            return target.controllerOfThis.allegianceTeamID != controllerOfThis.allegianceTeamID;
        }
        return false;
        //return target.teamNumber.Value != entity.teamNumber.Value;
    }
    public void UnloadPassenger(StateMachineController exiting)
    {
        /*foreach (GarrisonablePosition item in garrisonablePositions)
        {
            if (item.passenger == exiting)
            {
                item.passenger = null;
                exiting.ent.occupiedGarrison = null;
                if (IsOwner)
                {
                    exiting.ChangeRVOStatus(true);
                    exiting.ent.isTargetable.Value = true;
                    exiting.ent.PlaceOnGround();
                    if (IsServer)
                    {
                        exiting.ent.PlaceOnGroundClientRpc();
                    }
                    else
                    {
                        exiting.ent.PlaceOnGroundServerRpc();
                    }
                }
                exiting.col.isTrigger = false;
                break;
            }
        }*/
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
        /*if (garrisonablePositions.Count <= 0)
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
        }*/
        return false;
    }
    private void RequestBuilders()
    {
        //Debug.Log("request builders");
        RTSPlayer local = Global.Instance.localPlayer;
        foreach (SelectableEntity item in local.selectedEntities)
        {
            if (item.IsBuilder())
            {
                //Debug.Log("requesting builder");
                StateMachineController minion = item.GetComponent<StateMachineController>();
                minion.SetBuildDestination(transform.position, this);
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
    private int interactorIndex = 0;
    private int othersInteractorIndex = 0;
    public bool IsBusy()
    {
        return interactionTarget != null;
    }
    private bool gateOpenStatus = true;
    private GameObject toggleableObject;
    public void ToggleGate()
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
            if (controllerOfThis != null)
            {
                controllerOfThis.population = Mathf.Clamp(controllerOfThis.population + change, 0, 999);
            }
        }
    }
    private readonly float deathDuration = 10;
    private void Die()
    { 
        if (IsOwner) //only the owner does this
        {
            if (sm != null && pf.pathfindingTarget != null) Destroy(pf.pathfindingTarget.gameObject);
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
    public void SetRally() //later have this take in a vector3?
    {
        rallyMission = RallyMission.Move;
        rallyTarget = null;
        //determine if spawned units should be given a mission

        Ray ray = Global.Instance.localPlayer.mainCam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity, Global.Instance.gameLayer))
        {
            rallyPoint = hit.point;
            Debug.Log("Setting rally" + rallyPoint);
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
                    if (target.IsOre())
                    {
                        rallyMission = RallyMission.Harvest;
                        rallyTarget = target;
                    }
                }
            }
        }
    }

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
        return sm == null;
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
                            if (target.sm != null && target.controllerOfThis == controllerOfThis)
                            {
                                //tell blocker to get out of the way.
                                float randRadius = 1;
                                Vector2 randCircle = UnityEngine.Random.insideUnitCircle * randRadius;
                                Vector3 rand = target.transform.position + new Vector3(randCircle.x, 0, randCircle.y);
                                target.pf.MoveTo(rand);
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
    public int GetAllegiance()
    {
        return controllerOfThis.allegianceTeamID;
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
        if (!playerController.ownedEntities.Contains(this)) playerController.ownedEntities.Add(this);
        if (IsMinion() && !playerController.ownedMinions.Contains(sm)) playerController.ownedMinions.Add(sm);

        /*if (factionEntity.constructableBuildings.Length > 0)
        {
            playerController.ownedBuilders.Add(stateMachineController);
        }*/
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
    private GameObject deathEffect = null;
    public void UpdatePathIndicator(Vector3[] list)
    {
        lineIndicator.positionCount = list.Length;
        lineIndicator.SetPositions(list);
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
            if (IsSpawner())
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
    #region Bools
    public bool IsSpawner()
    {
        return spawner != null;
    }
    public bool IsBuilder()
    {
        return builder != null;
    }
    #endregion
}
