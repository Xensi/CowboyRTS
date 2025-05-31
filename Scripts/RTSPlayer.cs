using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.EventSystems;// Required when using Event data.
using TMPro;
using System.Linq;
using FoW;
using UnityEngine.Rendering;
using System;
//using Unity.Burst.CompilerServices;
using System.Threading.Tasks;
//using static UnityEditor.PlayerSettings;
//using static UnityEditor.Progress;

public class RTSPlayer : Player
{
    public NetworkVariable<bool> inTheGame = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public List<SelectableEntity> keystoneUnits = new();
    [SerializeField] private Grid grid;
    private Vector3Int _gridPosition;
    public List<SelectableEntity> selectedEntities; //selected and we can control them  
    public List<StateMachineController> selectedBuilders;
    private Vector3 _mousePosition;
    private Vector3 _offset;
    public Vector3 cursorWorldPosition;
    bool _doubleSelect = false;
    public enum MouseState
    {
        Waiting,
        ReadyToPlace,
        PlacingAndRotating,
        ReadyToSetRallyPoint
    }
    public enum LinkedState
    {
        Waiting,
        PlacingStart,
        PlacingEnd
    }
    public enum Direction
    {
        Forward, Left, Right, Back
    }
    public LinkedState linkedState = LinkedState.Waiting;
    public MouseState mouseState = MouseState.Waiting;
    public LayerMask groundLayer;
    //public LayerMask entityLayer;
    private LayerMask blockingLayer;
    public SelectableEntity lastSpawnedEntity;
    public bool placementBlocked = false;
    private SelectableEntity buildingGhost;
    private MeshRenderer[] meshes;
    private bool oldPlacement = false;
    public Portal startPortal;
    public Portal endPortal;
    Vector2 StartMousePosition;
    public bool placingLinkedBuilding = false;
    private bool finishedSelection = false;
    private readonly float camSpeed = 10;
    private readonly float camScroll = 0.5f;
    public byte starterUnitID = 0;
    private Transform camParent;
    public LayerMask placementGhost;
    public LayerMask gameLayer;
    public Camera mainCam;
    private Camera[] cams;
    private bool active = true;
    private FactionBuilding buildingToPlace = null;
    public List<SelectableEntity> militaryList = new();
    private List<SelectableEntity> builderListSelection = new();
    public List<SelectableEntity> productionList = new();
    private Vector3 startWallPosition;
    public List<Vector3> predictedWallPositions = new();
    public List<bool> predictedWallPositionsShouldBePlaced = new();
    public List<GameObject> wallGhosts = new();
    private List<SelectableEntity> fakeSpawns = new();
    private List<ushort> fakeSpawnsReadyForReplacement = new();
    private List<FactionBuilding> availableConstructionOptions = new();
    [SerializeField] private List<FactionAbility> availableAbilities = new();
    private List<FactionUnit> availableUnitSpawns = new();
    private Vector3 buildOffset = Vector3.zero;
    public bool placingPortal = false;
    public List<byte> indices;
    public void LoseGame()
    {
        inTheGame.Value = false;
    }
    private void MoveCamToSpawn()
    {
        Vector3 spawn = playerSpawns.spawnsList[Convert.ToInt32(OwnerClientId)].position;
        camParent.position = new Vector3(spawn.x, camParent.position.y, spawn.z);
    }
    public void UpdateHPText()
    {
        if (selectedEntities.Count == 1)
        {
            Global.Instance.hpText.text = "HP: " + selectedEntities[0].currentHP.Value + "/" + selectedEntities[0].maxHP;
        }
    }
    private void OnDisable()
    {
        Global.Instance.uninitializedPlayers.Remove(this);
    }
    public override void Start()
    {
        base.Start();
        groundLayer = LayerMask.GetMask("Ground");
        blockingLayer = LayerMask.GetMask("Entity", "Obstacle"); 
        gameLayer = LayerMask.GetMask("Entity", "Obstacle", "Ground");
        placementGhost = LayerMask.GetMask("PlacementGhost");
        //_offset = new Vector3(0.5f, 0, .5f);
        _offset = new Vector3(0.25f, 0, .25f);
        //_offset = new Vector3(0, 0, 0); 
        cams = Global.Instance.cams;
        mainCam = cams[0];
        camParent = cams[0].transform.parent.transform;
        meshes = new MeshRenderer[1];
        if (IsOwner)
        {
            MoveCamToSpawn();
        }
    }
    private LevelInfo playerSpawns;
    private void RetrieveSpawnPositionsList()
    {
        playerSpawns = null;
        playerSpawns = FindFirstObjectByType<LevelInfo>();
    }
    public override void OnNetworkSpawn()
    {
        playerTeamID = System.Convert.ToInt32(OwnerClientId);
        Global.Instance.uninitializedPlayers.Add(this);
        RetrieveSpawnPositionsList();
        if (IsOwner) //spawn initial minions/buildings  
        {
            inTheGame.Value = true;
            Global.Instance.localPlayer = this;
            /*Vector3 spawnPosition;
            if (playerTeamID < playerSpawns.spawnsList.Count)
            {
                spawnPosition = playerSpawns.spawnsList[playerTeamID].position;
            }
            else
            {
                spawnPosition = new Vector3(UnityEngine.Random.Range(-9, 9), 0, UnityEngine.Random.Range(-9, 9));
            }*/

            //GenericSpawnMinion(spawnPosition, playerFaction.spawnableEntities[0], this); 

            VolumeProfile profile = Global.Instance.fogVolume.sharedProfile;
            if (profile != null && profile.TryGet(out FogOfWarURP fow))
            {
                fow.team.value = playerTeamID;
            }
        }
        else
        {
            active = false;
        }
        //playerFaction = Global.Instance.factions[teamID];
        allegianceTeamID = playerTeamID;
    }
    private bool MouseOverUI()
    {
        PointerEventData eventDataCurrentPosition = new PointerEventData(EventSystem.current);
        eventDataCurrentPosition.position = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventDataCurrentPosition, results);
        foreach (RaycastResult item in results)
        {
            if (item.gameObject.layer == LayerMask.NameToLayer("UI"))
            {
                return true;
            }
        }
        return false;

        //return EventSystem.current.IsPointerOverGameObject();
    }
    private void CameraMove()
    {
        Vector3 motion = new(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
        camParent.transform.Translate(camSpeed * Time.deltaTime * motion); //reordered operands for better performance
        //from 0 to 10 
        for (int i = 0; i < cams.Length; i++)
        {
            cams[i].orthographicSize = Mathf.Clamp(cams[i].orthographicSize - Input.mouseScrollDelta.y * camScroll, 1, 10); ;
        }
    }
    private void SelectedAttackMove()
    {
        //Debug.Log("trying to attack move");
        Vector3 clickedPosition;
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity, Global.Instance.localPlayer.groundLayer))
        {
            clickedPosition = hit.point;
            EntitySearcher searcher = null;
            if (selectedEntities.Count > 0)
            {
                //create an entity searcher at the clicked position
                searcher = CreateEntitySearcherAtPosition(clickedPosition, 0);
            }

            UnitOrdersQueue.Clear();

            if (searcher == null) return;
            foreach (SelectableEntity item in selectedEntities)
            {
                if (item.sm != null && item.IsAttacker()) //minion
                {
                    //if this unit is already assigned to an entity searcher, unassign it
                    if (item.attacker.assignedEntitySearcher != null)
                    {
                        item.attacker.assignedEntitySearcher.UnassignUnit(item.sm);
                    }
                    //assign the entity searcher to selected units
                    item.attacker.assignedEntitySearcher = searcher;
                    //update the entity searcher's assigned units list
                    item.attacker.assignedEntitySearcher.AssignUnit(item.sm);
                    item.attacker.hasCalledEnemySearchAsyncTask = false; //tell the minion to run a new search
                    UnitOrder order = new();
                    order.unit = item.sm;
                    order.targetPosition = clickedPosition;
                    order.action = ActionType.AttackMove;
                    UnitOrdersQueue.Add(order);
                }
            } 
        }
    }
    private bool SameAllegiance(SelectableEntity foreign)
    {   //later update this so it works with allegiances
        //return foreign.controllerOfThis == this;
        return foreign.controllerOfThis.allegianceTeamID == allegianceTeamID;
        //return foreign.teamNumber.Value == (sbyte)playerTeamID;
        //foreign.controllerOfThis.allegianceTeamID == allegianceTeamID;
    }
    /// <summary>
    /// Behavior depends on what is right clicked and the type of unit responding
    /// </summary>
    private void ContextualQueueUnitOrders()
    {
        Vector3 clickedPosition = Vector3.zero;
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] raycastHits = new RaycastHit[5];
        int hits = Physics.RaycastNonAlloc(ray.origin, ray.direction, raycastHits, Mathf.Infinity, Global.Instance.gameLayer);

        SelectableEntity hitEntity = null;
        for (int i = 0; i < hits; i++)
        {
            SelectableEntity checkEntity = Global.Instance.FindEntityFromObject(raycastHits[i].collider.gameObject);
            if (hitEntity == null || checkEntity.controllerOfThis != null && checkEntity.controllerOfThis.allegianceTeamID != allegianceTeamID)
            {
                //enemy takes priority over allies 
                hitEntity = checkEntity;
            }

            bool groundWasHit = raycastHits[i].collider.gameObject.layer == LayerMask.NameToLayer("Ground");
            if (groundWasHit)
            {
                clickedPosition = raycastHits[i].point;
            }
        }
        //EntitySearcher searcher = CreateEntitySearcherAtPosition(clickedPosition);

        if (hits > 0) 
        {
            //determine action type
            if (hitEntity != null && PositionExplored(clickedPosition)) //if exists and is explored at least
            {
                if (hitEntity.teamType == SelectableEntity.TeamBehavior.OwnerTeam)
                {
                    if (SameAllegiance(hitEntity)) //same team
                    {
                        /*if ((hitEntity.depositType == SelectableEntity.DepositType.Gold || hitEntity.depositType == SelectableEntity.DepositType.All)
                            && hitEntity.fullyBuilt) //if deposit point
                        {
                            actionType = ActionType.Deposit;
                        }*/
                        if (!hitEntity.fullyBuilt || hitEntity.IsDamaged() && !hitEntity.IsMinion()) //if buildable
                        {
                            actionType = ActionType.BuildTarget;

                            AssignBuildersBasedOnDistance();

                        }
                        else if (hitEntity.fullyBuilt && hitEntity.HasEmptyGarrisonablePosition())
                        { //target can be garrisoned, and passenger cannot garrison, then enter
                            actionType = ActionType.Garrison;
                        }
                        else if (hitEntity.occupiedGarrison != null && hitEntity.occupiedGarrison.HasEmptyGarrisonablePosition())
                        { //target is passenger of garrison, then enter garrison
                            actionType = ActionType.Garrison;
                            hitEntity = hitEntity.occupiedGarrison;
                        }
                        else
                        {
                            actionType = ActionType.MoveToTarget;
                        }
                    }
                    else if (hitEntity.isAttackable) //enemy
                    { //try to target this enemy specifically
                        actionType = ActionType.AttackTarget;
                        //Debug.Log("Trying to attack " + hitEntity.name);
                    }
                    else
                    {
                        actionType = ActionType.Move;
                    }
                }
                else if (hitEntity.teamType == SelectableEntity.TeamBehavior.FriendlyNeutral) //for now resources are only neutral; this may change
                { 
                    if (hitEntity.IsOre())
                    {
                        //Debug.Log("trying to harvest");
                        actionType = ActionType.Harvest;
                    }
                }
            }
            else
            {
                actionType = ActionType.Move;
                //Debug.Log("Moving");
            }
            //finished determining action type 
            UnitOrdersQueue.Clear();
            foreach (SelectableEntity item in selectedEntities)
            {
                if (item.sm != null)
                {
                    UnitOrder order = new();
                    order.unit = item.sm;
                    order.targetPosition = clickedPosition;
                    order.action = actionType;
                    order.target = hitEntity;
                    UnitOrdersQueue.Add(order);
                }
            }
            //totalNumUnitOrders = UnitOrdersQueue.Count;
        }
    }
    //private int totalNumUnitOrders = 0;
    public void ReadySetRallyPoint()
    {
        mouseState = MouseState.ReadyToSetRallyPoint;
    }
    private void SetSelectedRallyPoint()
    {
        foreach (SelectableEntity item in selectedEntities)
        {
            item.SetRally();
        }
    }
    private bool IsEntityGarrrisoned(SelectableEntity entity)
    {
        return entity.occupiedGarrison != null;
    }
    private void SelectAllAttackers()
    {
        //Debug.Log("trying to select all attackers");
        DeselectAll();
        foreach (StateMachineController item in ownedMinions)
        {
            if (item != null && !IsEntityGarrrisoned(item.ent)
                && !item.ent.IsSpawner() && !item.ent.IsHarvester())
            {
                TrySelectEntity(item.ent);
            }
        }
    }
    private void SelectAllProduction()
    {
        DeselectAll();
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item != null && item.IsSpawner())
            {
                TrySelectEntity(item);
            }
        }
    }
    private void SelectAllIdleBuilders()
    {
        DeselectAll();
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item != null && item.sm != null && (item.IsBuilder())) //&& item.canBuild
            {
                switch (item.sm.GetState())
                {
                    case StateMachineController.EntityStates.Idle:
                    case StateMachineController.EntityStates.FindInteractable:
                        TrySelectEntity(item);
                        break;
                }
            }
        }
    }
    public bool IsTargetExplicitlyOnOurTeam(SelectableEntity target)
    {
        return target.teamType == SelectableEntity.TeamBehavior.OwnerTeam && ownedEntities.Contains(target);
    }
    private SelectableEntity infoSelectedEntity;
    /// <summary>
    /// Try to select an entity.
    /// </summary>
    /// <param name="entity"></param>
    private bool TrySelectEntity(SelectableEntity entity)
    {
        if (entity == null) return false;
        if (!PositionFullyVisible(entity.transform.position)) return false;
        if (!entity.alive) return false;
        bool val = false;
        if (IsTargetExplicitlyOnOurTeam(entity))
        {
            selectedEntities.Add(entity);

            /*if (entity.factionEntity.constructableBuildings.Length > 0)
            {
                selectedBuilders.Add(entity.stateMachineController);
            }*/

            entity.Select(true);
            val = true;
        }
        else
        {
            infoSelectedEntity = entity;
            entity.InfoSelect(true);
        }
        if (entity.IsStructure())
        {
            Global.Instance.PlayStructureSelectSound(entity);
        }
        UpdateGUIFromSelections();
        return val;
    }
    private void UpdateGridVisual()
    {
        if (Global.Instance.gridVisual != null)
        {
            Global.Instance.gridVisual.SetActive(mouseState == MouseState.ReadyToPlace);
        }
    }
    private void DetectHotkeys()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            //Debug.Log("Spacebar");
            SelectAllAttackers();
        }
        if (Input.GetKeyDown(KeyCode.E))
        {
            SelectAllProduction();
        }
        if (Input.GetKeyDown(KeyCode.B))
        {
            SelectAllIdleBuilders();
        }
        if (Input.GetKeyDown(KeyCode.Period))
        {
            GenericSpawnMinion(cursorWorldPosition, playerFaction.spawnableEntities[2], this);
        }
        if (Input.GetKeyDown(KeyCode.RightControl))
        {
            GenericSpawnMinion(cursorWorldPosition, playerFaction.spawnableEntities[3], this);
        }
#if UNITY_EDITOR //DEBUG COMMANDS
        if (Input.GetKeyDown(KeyCode.RightShift))
        {
            GenericSpawnMinion(cursorWorldPosition, playerFaction.spawnableEntities[1], this);
        }

        if (Input.GetKeyDown(KeyCode.RightAlt))
        {
            GenericSpawnMinion(cursorWorldPosition, playerFaction.spawnableEntities[4], this);
        }
#endif
    }
    Vector3 oldCursorWorldPosition;
    private int requiredAssignments = 0;

    private void AssignBuildersBasedOnDistance()
    {
        Debug.Log("Starting to assign");
        requiredAssignments = unbuiltStructures.Count;
    }
    private void AssignBuildersSelectedWorkhorse()
    {
        float maxAllowableDistance = 10;
        float distance = Mathf.Infinity;
        SelectableEntity currentBuilding = null;
        StateMachineController closestBuilder = null;
        //get building and builder that have the least distance
        foreach (SelectableEntity building in unbuiltStructures)
        {
            if (building == null) continue;
            if (building.workersInteracting.Count < building.allowedWorkers)
            {
                //get the closest available builder in builder list
                foreach (StateMachineController builder in selectedBuilders)
                {
                    if (!builder.IsCurrentlyBuilding())
                    {
                        float newDist = Vector3.SqrMagnitude(building.transform.position - builder.transform.position);
                        //float newDist = Vector3.Distance(building.transform.position, builder.transform.position);
                        if (newDist < distance)
                        {
                            currentBuilding = building;
                            closestBuilder = builder;
                            distance = newDist;
                        }
                    }
                }
            }
        }

        if (closestBuilder != null && currentBuilding != null && distance <= maxAllowableDistance)
        {
            closestBuilder.CommandBuildTarget(currentBuilding);
        }
    }
    public override void Update()
    {
        if (!active) return;
        base.Update();
        if (requiredAssignments > 0)
        {
            requiredAssignments--;

            AssignBuildersSelectedWorkhorse();
        }
        ProcessOrdersInBatches();
        UpdatePlacementBlockedStatus();
        UpdateGridVisual();
        DetectHotkeys();
        CameraMove();
        if (!MouseOverUI())
        {
            GetMouseWorldPosition();

            if (linkedState == LinkedState.PlacingEnd)
            {
                if (cursorWorldPosition != oldCursorWorldPosition)
                {
                    oldCursorWorldPosition = cursorWorldPosition;
                    CalculateFillCost(startWallPosition, cursorWorldPosition, buildingToPlace);
                }
            }
            if (Input.GetMouseButtonDown(0)) //left click pressed
            {
                switch (mouseState)
                {
                    case MouseState.Waiting:
                        TryToSelectOne();
                        break;
                    case MouseState.ReadyToPlace:
                        if (!placementBlocked)
                        {
                            if (buildingToPlace.rotatable)
                            {
                                mouseState = MouseState.PlacingAndRotating; //if the building can't rotate place it immediately
                            }
                            else
                            {
                                //Debug.Log("Attempting to place building");
                                PlaceBuilding(buildingToPlace, cursorWorldPosition);
                                if (buildingToPlace.extendable && buildingGhost != null)
                                {
                                    Destroy(buildingGhost.gameObject);
                                    buildingGhost = null;
                                }
                            }
                        }
                        else if (placementBlocked && buildingToPlace.rotatable) //if not placeable, we may be able to rotate it so that it is
                        {
                            mouseState = MouseState.PlacingAndRotating;
                        }
                        break;
                    case MouseState.ReadyToSetRallyPoint:
                        mouseState = MouseState.Waiting;
                        SetSelectedRallyPoint();
                        break;
                    case MouseState.PlacingAndRotating:
                        /*Vector3 alignedForward = SnapToNearestWorldAxis(buildingGhost.transform.forward);
                        Vector3 alignedUp = SnapToNearestWorldAxis(buildingGhost.transform.up);
                        Quaternion quaternion = Quaternion.LookRotation(alignedForward, alignedUp);*/
                        //PlaceBuildingWithRotation(buildingToPlace, buildingGhost.transform.position, quaternion);
                        break;
                    default:
                        break;
                }
            }
            switch (mouseState)
            {
                case MouseState.Waiting:
                    break;
                case MouseState.ReadyToPlace:
                    if (buildingGhost != null)
                    {
                        buildingGhost.transform.position = cursorWorldPosition;
                        //followCursorObject.transform.LookAt(cursorWorldPosition);
                    }
                    break;
                case MouseState.ReadyToSetRallyPoint:
                    break;
                case MouseState.PlacingAndRotating:
                    if (buildingGhost != null)
                    {
                        //followCursorObject.transform.position = cursorWorldPosition;
                        buildingGhost.transform.LookAt(cursorWorldPosition);
                        Vector3 alignedForward = SnapToNearestWorldAxis(buildingGhost.transform.forward);
                        Vector3 alignedUp = SnapToNearestWorldAxis(buildingGhost.transform.up);
                        buildingGhost.transform.rotation = Quaternion.LookRotation(alignedForward, alignedUp);
                    }
                    break;
            }
            if (Input.GetMouseButtonUp(0)) //left click released
            {
                switch (mouseState)
                {
                    case MouseState.Waiting:
                        break;
                    case MouseState.ReadyToPlace:
                        break;
                    case MouseState.ReadyToSetRallyPoint:
                        break;
                    case MouseState.PlacingAndRotating:
                        if (!placementBlocked)
                        {
                            FinishPlacingRotatedBuilding();
                        }
                        else
                        {
                            StopPlacingBuilding();
                        }
                        break;
                }
            }
            if (Input.GetMouseButtonDown(2) || Input.GetKeyDown(KeyCode.R)) //middle click
            {
                SelectedAttackMove();
            }
            if (Input.GetMouseButtonDown(1)) //right click
            { //used to cancel most commands, or move
                //Debug.Log("right clicked");
                switch (mouseState)
                {
                    case MouseState.Waiting:
                        ContextualQueueUnitOrders();
                        SetBuildingRallies();
                        break;
                    case MouseState.ReadyToPlace:
                        StopPlacingBuilding();
                        //if (!placingLinkedBuilding) 
                        break;
                    case MouseState.ReadyToSetRallyPoint:
                        mouseState = MouseState.Waiting;
                        break;
                    case MouseState.PlacingAndRotating:
                        StopPlacingBuilding();
                        break;
                    default:
                        break;
                }
            }
        }

        switch (mouseState)
        {
            case MouseState.Waiting:
                if (Input.GetMouseButtonDown(0) && !MouseOverUI())
                {
                    StartMousePosition = Input.mousePosition;
                    Global.Instance.selectionRect.gameObject.SetActive(true);
                }
                if (Global.Instance.selectionRect.gameObject.activeSelf)
                {
                    if (Input.GetMouseButton(0))
                    {
                        ResizeSelection();
                    }
                    if (Input.GetMouseButtonUp(0))
                    {
                        SelectWithinBounds();
                    }
                }
                break;
            case MouseState.ReadyToPlace:
                Global.Instance.selectionRect.gameObject.SetActive(false);
                break;
            case MouseState.ReadyToSetRallyPoint:
                Global.Instance.selectionRect.gameObject.SetActive(false);
                break;
            case MouseState.PlacingAndRotating:
                Global.Instance.selectionRect.gameObject.SetActive(false);
                break;
            default:
                break;
        }

        if (!Input.GetMouseButton(0) && finishedSelection)
        {
            Global.Instance.selectionRect.gameObject.SetActive(false);
        }
        Global.Instance.goldText.text = "Gold: " + gold;
        if (Global.Instance.popText != null)
        {
            Global.Instance.popText.text = "Army Size: " + population + "/" + maxPopulation;
        }
        //TryReplaceFakeSpawn();
        UpdateGUIFromSelections();// this might be expensive ...
    }
    private void FinishPlacingRotatedBuilding()
    {
        Debug.Log("Finishing placing rotated building");
        //PlaceBuilding(buildingToPlace, buildingGhost.transform.position); 
        Vector3 alignedForward = SnapToNearestWorldAxis(buildingGhost.transform.forward);
        Vector3 alignedUp = SnapToNearestWorldAxis(buildingGhost.transform.up);
        Quaternion quaternion = Quaternion.LookRotation(alignedForward, alignedUp);
        Direction dir = ConvertForwardToEnum(alignedForward);

        if (gold < buildingToPlace.goldCost) return;
        gold -= buildingToPlace.goldCost;
        SpawnBuildingWithRotation(buildingToPlace, buildingGhost.transform.position, dir, quaternion);
        SelectableEntity last = Global.Instance.localPlayer.ownedEntities.Last();
        TellSelectedToBuild(last);
        StopPlacingBuilding();
    }
    private Direction ConvertForwardToEnum(Vector3 alignedForward)
    {
        Direction dir = Direction.Forward;
        if (alignedForward == Vector3.forward)
        {
            dir = Direction.Forward;
        }
        else if (alignedForward == Vector3.back)
        {
            dir = Direction.Back;
        }
        else if (alignedForward == Vector3.left)
        {
            dir = Direction.Left;
        }
        else if (alignedForward == Vector3.right)
        {
            dir = Direction.Right;
        }
        return dir;
    }
    private Vector3 ConvertDirectionToVector(Direction dir)
    {
        Vector3 vec = Vector3.forward;
        switch (dir)
        {
            case Direction.Forward:
                vec = Vector3.forward;
                break;
            case Direction.Left:
                vec = Vector3.left;
                break;
            case Direction.Right:
                vec = Vector3.right;
                break;
            case Direction.Back:
                vec = Vector3.back;
                break;
            default:
                break;
        }
        return vec;
    }
    private static Vector3 SnapToNearestWorldAxis(Vector3 vec)
    {
        if (Mathf.Abs(vec.x) < Mathf.Abs(vec.y))
        {
            vec.x = 0;
            if (Mathf.Abs(vec.y) < Mathf.Abs(vec.z))
                vec.y = 0;
            else
                vec.z = 0;
        }
        else
        {
            vec.y = 0;
            if (Mathf.Abs(vec.x) < Mathf.Abs(vec.z))
                vec.x = 0;
            else
                vec.z = 0;
        }
        return vec;
    }
    private void SetBuildingRallies()
    {
        foreach (SelectableEntity item in selectedEntities)
        {
            if (!item.IsMinion())
            {
                item.SetRally();
            }
        }
    }
    private void SelectWithinBounds() //rectangle select, finish drag select
    {
        RectTransform SelectionBox = Global.Instance.selectionRect;
        Bounds bounds = new(SelectionBox.anchoredPosition, SelectionBox.sizeDelta);
        //Debug.Log(bounds.size.magnitude);
        if (bounds.size.magnitude > 20f) //make sure the selection box is sufficiently large
        {
            if (!Input.GetKey(KeyCode.LeftShift)) //deselect all if not pressing shift
            {
                DeselectAll();
            }
            //evaluate which should actually be selected based on priority
            //count types
            List<SelectableEntity> evaluation = new();
            foreach (SelectableEntity item in ownedEntities)
            {
                if (item != null)
                {
                    if (UnitIsInSelectionBox(mainCam.WorldToScreenPoint(item.transform.position), bounds))
                    {
                        evaluation.Add(item);
                    }
                }
            }
            militaryList = new();
            builderListSelection = new();
            productionList = new();
            foreach (SelectableEntity item in evaluation)
            {
                if (item.IsBuilder())
                {
                    builderListSelection.Add(item);
                }
                else if (item.sm != null)
                {
                    militaryList.Add(item);
                }
                else if (item.IsSpawner())
                {
                    productionList.Add(item);
                }
            }
            int mil = militaryList.Count;
            int build = builderListSelection.Count;
            int prod = productionList.Count;
            List<SelectableEntity> useList = new();
            if (mil > 0 || build > 0) //then ignore prod
            {
                if (mil >= build)
                {
                    useList = militaryList;
                }
                else
                {
                    useList = builderListSelection;
                }
            }
            else
            {
                useList = productionList;
            }

            foreach (SelectableEntity item in useList)
            {
                TrySelectEntity(item);
            }
            UpdateGUIFromSelections();
        }
        Global.Instance.selectionRect.gameObject.SetActive(false);
        finishedSelection = true;
    }
    private bool UnitIsInSelectionBox(Vector2 Position, Bounds Bounds)
    {
        return Position.x > Bounds.min.x && Position.x < Bounds.max.x
            && Position.y > Bounds.min.y && Position.y < Bounds.max.y;
    }
    private void ResizeSelection() //drag selection
    {
        finishedSelection = false;
        RectTransform SelectionBox = Global.Instance.selectionRect;
        float width = Input.mousePosition.x - StartMousePosition.x;
        float height = Input.mousePosition.y - StartMousePosition.y;

        SelectionBox.anchoredPosition = StartMousePosition + new Vector2(width / 2, height / 2);
        SelectionBox.sizeDelta = new Vector2(Mathf.Abs(width), Mathf.Abs(height));
    }
    private void TellSelectedToBuild(SelectableEntity building)
    {
        Debug.Log("Attempting selected build: " + building.name);
        if (selectedBuilders.Count == 1)
        {
            selectedBuilders[0].ForceBuildTarget(building);
        }
        else if (selectedBuilders.Count > 1)
        {
            //tell closest builder to go build it
            FindClosestBuilderToBuildStructure(building);
        }
    }

    private void FindClosestBuilderToBuildStructure(SelectableEntity building)
    {
        float distance = Mathf.Infinity;
        StateMachineController closestBuilder = null;

        //get the closest available builder in builder list
        foreach (StateMachineController builder in selectedBuilders)
        {
            float newDist = Vector3.SqrMagnitude(building.transform.position - builder.transform.position);
            if (newDist < distance)
            {
                closestBuilder = builder;
                distance = newDist;
            }
        }
        if (closestBuilder != null)
        {
            closestBuilder.CommandBuildTarget(building);
        }
    }
    private void PlaceBuilding(FactionBuilding building, Vector3 position)
    {
        switch (linkedState)
        {
            case LinkedState.Waiting:
                NormalPlaceBuilding(building, position);
                break;
            case LinkedState.PlacingStart:
                linkedState = LinkedState.PlacingEnd;
                startWallPosition = cursorWorldPosition;
                Destroy(buildingGhost);
                break;
            case LinkedState.PlacingEnd:
                int cost = CalculateFillCost(startWallPosition, cursorWorldPosition, building);
                if (gold >= cost)
                {
                    gold -= cost;
                    WallFill(building);

                    AssignBuildersBasedOnDistance();
                    StopPlacingBuilding();
                }
                break;
            default:
                break;
        }
    }
    private void NormalPlaceBuilding(FactionBuilding building, Vector3 position)
    {
        if (gold < building.goldCost) return;
        gold -= building.goldCost;
        Debug.Log("Trying to place" + building.name);
        GenericSpawnMinion(position, building, this);
        SelectableEntity spawnedBuilding = Global.Instance.localPlayer.ownedEntities.Last();
        TellSelectedToBuild(spawnedBuilding);

        StopPlacingBuilding(); //temporary: later re-implement two-part buildings and holding shift to continue placing 

        //is building a two-parter? 

        /*if (Input.GetKey(KeyCode.LeftShift) && gold >= building.goldCost)
        {
            //continue placing buildings
        }
        else if (!Input.GetKey(KeyCode.LeftShift) || gold < building.goldCost) //if not holding shift or out of money for this building
        {
            if (building.linkedID == -1) //if no linked building, stop after placing building
            {
                StopPlacingBuilding();
                if (placingPortal)
                {
                    foreach (SelectableEntity item in ownedEntities)
                    {
                            Portal portal = item.GetComponent<Portal>();
                            if (portal != startPortal)
                            {
                                endPortal = portal;

                                startPortal.destination = endPortal.transform.position;
                                endPortal.destination = startPortal.transform.position;
                                startPortal.hasLinkedPortal = true;
                                endPortal.hasLinkedPortal = true;
                                startPortal.linkedPortal = endPortal;
                                endPortal.linkedPortal = startPortal;

                                startPortal = null;
                                endPortal = null;
                                placingPortal = false;
                                break;
                            }
                    }
                }
            }
            else //if there's a linked building, we continue placing buildings so the player can place the next part of the building.
            {
                buildingPlacingID = (byte)building.linkedID;
                placingLinkedBuilding = true;

                if (placingPortal)
                {
                    foreach (SelectableEntity item in ownedEntities)
                    {
                            startPortal = item.GetComponent<Portal>();
                            break;
                    }
                }
            }
        }*/
    }
    private void ClearWallGhosts()
    {
        foreach (GameObject item in wallGhosts)
        {
            if (item != null)
            {
                Destroy(item);
            }
        }
        wallGhosts.Clear();
    }
    private int CalculateFillCost(Vector3 start, Vector3 end, FactionBuilding building) //fill between start and end byte id
    {
        predictedWallPositions.Clear();
        predictedWallPositionsShouldBePlaced.Clear();
        int cost = building.goldCost;
        float distance = Vector3.Distance(start, end); //greater distance means more walls

        ClearWallGhosts();
        //float halfExtents = 0.1f;
        if (distance > 0)
        {
            for (float i = 0; i <= distance + 0.5f; i += 0.5f)
            {
                Vector3 spot = Vector3.Lerp(start, end, i / distance);
                Vector3 mod = AlignToQuarterGrid(spot);
                //place on ground;
                Vector3 ground = Vector3.zero;
                if (Physics.Raycast(mod + (new Vector3(0, 100, 0)), Vector3.down, out RaycastHit hit, Mathf.Infinity, Global.Instance.localPlayer.groundLayer))
                {
                    ground = hit.point;
                }
                //Vector3 mod = AlignToQuarterGrid(spot);
                //Vector3 mod = AlignToQuarterGrid(ground);
                //Debug.DrawLine(spot, spot + new Vector3(0, 1, 0), Color.red);
                if (!predictedWallPositions.Any(i => i == ground)) // && mod != cursorWorldPosition
                {
                    bool placeable = !IsPositionBlocked(ground);
                    predictedWallPositionsShouldBePlaced.Add(placeable);
                    predictedWallPositions.Add(ground);
                    /*if (placeable)
                    {
                        Debug.DrawLine(ground, ground + new Vector3(0, 1, 0), Color.green);
                    }
                    else
                    {

                        Debug.DrawLine(ground, ground + new Vector3(0, 1, 0), Color.red);
                    }*/
                }
            }
        }
        else //if distance is 0 
        {
            Vector3 ground = Vector3.zero;
            if (Physics.Raycast(start + (new Vector3(0, 100, 0)), Vector3.down, out RaycastHit hit, Mathf.Infinity, Global.Instance.localPlayer.groundLayer))
            {
                ground = hit.point;
            }
            bool placeable = !IsPositionBlocked(ground);
            predictedWallPositionsShouldBePlaced.Add(placeable);
            predictedWallPositions.Add(ground);
        }
        //count how many should be placed
        int count = 0;
        foreach (bool item in predictedWallPositionsShouldBePlaced)
        {
            if (item)
            {
                count++;
            }
        }
        int realCost = cost * count;
        if (gold < realCost) //not enough money
        {
            for (int i = 0; i < predictedWallPositions.Count; i++)
            {
                Vector3 pos = predictedWallPositions[i];
                GameObject ghost = PlaceWallGhost(pos, building, true);
                wallGhosts.Add(ghost);
            }
        }
        else
        {
            for (int i = 0; i < predictedWallPositions.Count; i++)
            {
                Vector3 pos = predictedWallPositions[i];
                bool shouldBePlaced = predictedWallPositionsShouldBePlaced[i];
                GameObject ghost = PlaceWallGhost(pos, building, !shouldBePlaced);
                wallGhosts.Add(ghost);
            }
        }
        //Debug.Log(realCost);
        return realCost;
    }
    private GameObject PlaceWallGhost(Vector3 pos, FactionBuilding building, bool blocked = false)
    {
        GameObject build = building.prefabToSpawn.gameObject;
        GameObject spawn = Instantiate(build, pos, Quaternion.Euler(0, 180, 0)); //spawn locally  
        spawn.layer = 0; //don't count as entity
        SelectableEntity entity = spawn.GetComponent<SelectableEntity>();

        entity.isBuildIndicator = true;
        meshes = spawn.GetComponentsInChildren<MeshRenderer>();
        for (int i = 0; i < meshes.Length; i++)
        {
            if (meshes[i] != null)
            {
                if (blocked)
                {
                    meshes[i].material = Global.Instance.blocked;
                }
                else
                {
                    meshes[i].material = Global.Instance.transparent;
                }
            }
        }
        Collider[] colliders = spawn.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].isTrigger = true;
        }
        return spawn;
    }
    private void WallFill(FactionBuilding building)
    {
        foreach (GameObject item in wallGhosts)
        {
            if (item != null)
            {
                Destroy(item);
            }
        }
        wallGhosts.Clear();
        for (int i = 0; i < predictedWallPositions.Count; i++)
        {
            Vector3 pos = predictedWallPositions[i];
            bool shouldBePlaced = predictedWallPositionsShouldBePlaced[i];
            if (shouldBePlaced)
            {
                GenericSpawnMinion(pos, building, this);
            }
        }
        predictedWallPositions.Clear();
        predictedWallPositionsShouldBePlaced.Clear();
    }
    private float AlignToGrid(float input)
    {
        //ex 1.7
        //float ceiling = Mathf.Ceil(input); //2
        float floor = Mathf.Floor(input); //1
        float middle = floor + 0.5f;

        //float maxDiff = 0.25f;
        return middle;
    }
    private Vector3 AlignToQuarterGrid(Vector3 input) //avoid 0 and 0.5 endings. we want .75 and .25
    {
        Vector3Int gridPosition = grid.WorldToCell(input);
        //cursorWorldPosition = grid.CellToWorld(_gridPosition) + _offset;
        Vector3 pos = grid.CellToWorld(gridPosition) + buildOffset;
        return pos;
    }
    //private byte wallID = 0;
    private void StopPlacingBuilding()
    {
        //Debug.Log("Stopping building placement and destroying ghosts");
        if (buildingGhost != null)
        {
            Destroy(buildingGhost.gameObject);
            buildingGhost = null;
        }
        mouseState = MouseState.Waiting;
        linkedState = LinkedState.Waiting;
        placingLinkedBuilding = false;

        foreach (GameObject item in wallGhosts)
        {
            if (item != null)
            {
                Destroy(item);
            }
        }
        wallGhosts.Clear();
    }
    public void UpdatePlacementBlockedStatus()
    {
        if (buildingGhost != null && buildingGhost.gameObject.activeInHierarchy)
        {
            //placementBlocked = IsPositionBlocked(buildingGhost.transform.position);
            placementBlocked = IsPositionBlockedByEntity(buildingGhost);
            if (placementBlocked != oldPlacement)
            {
                oldPlacement = placementBlocked;
                UpdatePlacementMeshes();
            }
        }
    }
    private void UpdatePlacementMeshes()
    {
        if (placementBlocked)
        {
            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] != null)
                {
                    meshes[i].material = Global.Instance.blocked;
                }
            }
        }
        else
        {
            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] != null)
                {
                    meshes[i].material = Global.Instance.transparent;
                }
            }
        }
    }
    /// <summary>
    /// Updates cursor world position onto grid
    /// </summary>
    private void GetMouseWorldPosition()
    {
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity, groundLayer))
        {
            _mousePosition = hit.point;
            _gridPosition = grid.WorldToCell(_mousePosition);
            cursorWorldPosition = grid.CellToWorld(_gridPosition) + buildOffset;
            cursorWorldPosition = new Vector3(cursorWorldPosition.x, hit.point.y, cursorWorldPosition.z);
        }
    }
    #region SpawnMinion

    private void FakeClientSideSpawn(Vector3 spawn, byte minionID)
    {
        /*Vector3 spawnPosition = spawn;//new(spawn.x, 0, spawn.y); //get spawn position 
        FactionUnit fac = _faction.entities[minionID]; //get information about minion based on ID
        if (fac.prefabToSpawn != null) // && fac.prefabToSpawn.fakeSpawnObject != null
        {
            SelectableEntity select = Instantiate(fac.prefabToSpawn, spawnPosition, Quaternion.Euler(0, 180, 0)); //spawn locally  
            Animator anim = select.GetComponentInChildren<Animator>();
            MeshRenderer[] renderers = select.GetComponentsInChildren<MeshRenderer>();
            NetworkObject net = select.GetComponent<NetworkObject>();
            Destroy(net);
            select.fakeSpawn = true;
            //select.fakeSpawnNetID.
            foreach (MeshRenderer item in renderers)
            {
                if (item != null)
                {
                    item.material.color = Global.Instance.teamColors[(int)Global.Instance.localPlayer.OwnerClientId];
                }
            }
            foreach (MeshRenderer item in select.teamRenderers)
            {
                if (item != null)
                {
                    item.material.color = Global.Instance.teamColors[(int)Global.Instance.localPlayer.OwnerClientId];
                }
            }
            if (anim != null)
            {
                anim.SetBool("fakeSpawn", true);
            }
            fakeSpawns.Add(select);
        }  */
    }
    private void SpawnBuildingWithRotation(FactionBuilding building, Vector3 spawnPosition, Direction dir, Quaternion quat)
    {
        if (playerFaction == null)
        {
            Debug.LogError("Missing player faction");
            return;
        }
        byte ownerID = (byte)OwnerClientId;
        if (IsServer)
        {
            ServerSpawnBuilding(building, spawnPosition, ownerID, quat);
        }
        else //clients ask server to spawn it
        {
            byte id = 0;
            bool foundID = false;
            for (int i = 0; i < playerFaction.spawnableEntities.Count; i++)
            {
                if (playerFaction.spawnableEntities[i].productionName == building.productionName)
                {
                    id = (byte)i;
                    foundID = true;
                    break;
                }
            }
            if (foundID)
            {
                RequestSpawnBuildingServerRpc(spawnPosition, id, ownerID, dir);
            }
            else
            {
                Debug.LogError("Missing: " + building.productionName + "; Make sure it's added to the faction's spawnable entities.");
            }
        }
        //UpdateButtons();
    }

    private void ServerSpawnBuilding(FactionBuilding building, Vector3 spawnPosition, byte clientID, Quaternion rotation)
    {
        if (!IsServer) return;
        if (building != null && building.prefabToSpawn != null)
        {
            GameObject buildingObject = Instantiate(building.prefabToSpawn.gameObject, spawnPosition, rotation); //spawn the minion
            SelectableEntity select = null;
            if (buildingObject != null)
            {
                select = buildingObject.GetComponent<SelectableEntity>(); //get select
            }
            if (select != null)
            {
                //grant ownership 
                if (NetworkManager.ConnectedClients.ContainsKey(clientID))
                {
                    select.clientIDToSpawnUnder = clientID;
                    Debug.Log("Granting ownership of " + select.name + " to client " + clientID);
                    if (select.net == null) select.net = select.GetComponent<NetworkObject>();

                    select.net.SpawnWithOwnership(clientID);
                    ClientRpcParams clientRpcParams = new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams
                        {
                            TargetClientIds = new ulong[] { clientID }
                        }
                    };
                }
            }
        }
    }

    [ServerRpc]
    private void RequestSpawnBuildingServerRpc(Vector3 spawnPosition, byte unit, byte clientID, Direction dir)
    {
        if (playerFaction == null)
        {
            Debug.LogError("Missing player faction");
            return;
        }
        FactionBuilding building = playerFaction.spawnableEntities[unit] as FactionBuilding;
        Vector3 vec = ConvertDirectionToVector(dir);
        Quaternion quat = Quaternion.LookRotation(vec, Vector3.up);
        ServerSpawnBuilding(building, spawnPosition, clientID, quat);
    }

    /// <summary>
    /// Tell the server to spawn in a minion at a position.
    /// </summary> 
    public void GenericSpawnMinion(Vector3 spawnPosition, FactionEntity unit, NetworkBehaviourReference spawner)
    {
        if (playerFaction == null)
        {
            Debug.LogError("Missing player faction");
            return;
        }
        byte ownerID = (byte)OwnerClientId;
        if (IsServer)
        {
            ServerSpawnMinion(spawnPosition, unit, ownerID, spawner);
        }
        else //clients ask server to spawn it
        {
            //FactionUnit fac = _faction.entities[unit]; //get information about minion based on ID
            //Debug.Log("CLIENT: sending request to spawn " + fac.productionName); 
            //RequestSpawnMinionServerRpc(spawnPosition, unit, (byte)OwnerClientId, spawner);

            //FakeClientSideSpawn(spawnPosition, unit);
            //get ID of requested unit from our faction information
            Debug.Log("Trying to spawn " + unit.productionName);
            byte id = 0;
            bool foundID = false;
            for (int i = 0; i < playerFaction.spawnableEntities.Count; i++)
            {
                //Debug.Log(playerFaction.spawnableEntities[i].productionName + "checking against" + unit.productionName + " at index " + i);
                if (playerFaction.spawnableEntities[i].productionName == unit.productionName)
                {
                    //Debug.Log(playerFaction.spawnableEntities[i].productionName + "matches" + unit.productionName + " at index " + i);
                    id = (byte)i;
                    foundID = true;
                    break;
                }
            }
            //Debug.Log("ID: " + id);
            if (foundID)
            {
                RequestSpawnMinionServerRpc(spawnPosition, id, ownerID, spawner);
            }
            else
            {
                Debug.LogError("Missing: " + unit.productionName + "; Make sure it's added to the faction's spawnable entities.");
            }
        }
        //UpdateButtons();
    }
    [ServerRpc]
    private void RequestSpawnMinionServerRpc(Vector3 spawnPosition, byte unit, byte clientID, NetworkBehaviourReference spawner) //ok to use byte because 0-244
    {
        if (playerFaction == null)
        {
            Debug.LogError("Missing player faction");
            return;
        }
        //FactionUnit fac = _faction.entities[minionID]; //get information about minion based on ID
        //Debug.Log("SERVER: received request to spawn " + fac.productionName);
        //ServerSpawnMinion(spawnPosition, unit, clientID, spawner);

        //convert unit id to faction entity to spawn
        FactionEntity entity = playerFaction.spawnableEntities[unit];
        /*for (int i = 0; i < playerFaction.spawnableEntities.Count; i++)
        {
            entity = playerFaction.spawnableEntities[i];
        }*/
        ServerSpawnMinion(spawnPosition, entity, clientID, spawner);
    }
    private void ServerSpawnMinion(Vector3 spawnPosition, FactionEntity unit, byte clientID, NetworkBehaviourReference spawner)
    {
        if (!IsServer) return;

        //FactionUnit fac = _faction.entities[minionID]; //get information about minion based on ID
        if (unit != null && unit.prefabToSpawn != null)
        {
            //Debug.Log("SERVER: spawning " + unit.productionName);
            GameObject minion = Instantiate(unit.prefabToSpawn.gameObject, spawnPosition, Quaternion.identity); //spawn the minion
            SelectableEntity select = null;
            if (minion != null)
            {
                select = minion.GetComponent<SelectableEntity>(); //get select
            }
            if (select != null)
            {
                if (spawner.TryGet(out SelectableEntity spawnerEntity))
                {
                    select.spawnerThatSpawnedThis = spawnerEntity;
                    select.controllerOfThis = spawnerEntity.controllerOfThis;
                }
                //grant ownership 
                if (NetworkManager.ConnectedClients.ContainsKey(clientID))
                {
                    select.clientIDToSpawnUnder = clientID;
                    //Debug.Log("Granting ownership of " + select.name + " to client " + clientID); 
                    if (select.net == null) select.net = select.GetComponent<NetworkObject>(); 
                    select.net.SpawnWithOwnership(clientID);
                    //change fog of war unit to the correct team
                    //select.localTeamNumber = clientID;
                    //if (select.fogUnit != null) select.fogUnit.team = clientID;

                    //change teamrenderers to correct color

                    //use client rpc to send this ID to client
                    ClientRpcParams clientRpcParams = new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams
                        {
                            TargetClientIds = new ulong[] { clientID }
                        }
                    }; 
                    //SendReferenceToSpawnedMinionClientRpc((ushort)select.NetworkObjectId, spawner, clientRpcParams);
                }
            }
        }
    }
    /// <summary>
    /// Tell client that their fake spawn is ready to be replaced with the real thing
    /// </summary>
    /// <param name="netID"></param>
    /// <param name="clientRpcParams"></param>
    [ClientRpc]
    public void SendReferenceToSpawnedMinionClientRpc(ushort netID, NetworkBehaviourReference spawner, ClientRpcParams clientRpcParams = default)
    {
        if (!IsServer)
        {
            Debug.Log("CLIENT: received confirmation of thing being spawned: " + netID);
            fakeSpawnsReadyForReplacement.Add(netID);
            if (spawner.TryGet(out SelectableEntity spawnerEntity))
            {
                newSpawnsSpawnerList.Add(spawnerEntity);
            }
        }
    }
    private List<SelectableEntity> newSpawnsSpawnerList = new();
    /*private void TryReplaceFakeSpawn() //not being used yet
    {
        if (fakeSpawns.Count > 0 && fakeSpawnsReadyForReplacement.Count > 0) //fakeSpawnsReadyForReplacement.Count > 0 && 
        {
            ushort fakeSpawnNetID = fakeSpawnsReadyForReplacement[0];
            //find newly spawned using netIDs
            List<NetworkObject> spawnedList = NetworkManager.SpawnManager.GetClientOwnedObjects(OwnerClientId);
            foreach (NetworkObject item in spawnedList)
            {
                if (item.NetworkObjectId == fakeSpawnNetID)
                {
                    Debug.Log("Replacing fake spawn with real spawned object: " + item.name); //later do rally 
                    NetworkObject newSpawn = item;

                    SelectableEntity select = newSpawn.GetComponent<SelectableEntity>();
                    SelectableEntity fake = fakeSpawns[0].GetComponent<SelectableEntity>();
                    StateMachineController fakeController = fakeSpawns[0].GetComponent<StateMachineController>();
                    newSpawn.transform.SetPositionAndRotation(fakeSpawns[0].transform.position, fakeSpawns[0].transform.rotation);
                    if (select != null && select.sm != null && fake != null && fakeController != null)
                    {
                        select.sm.currentState = fakeController.GetState();
                        select.sm.currentState = StateMachineController.EntityStates.Idle;
                    }
                    if (select != null && fake != null)
                    {
                        select.selected = fake.selected;
                        //select.ChangeHitPointsServerRpc(fake.hitPoints.Value);
                    }
                    if (select != null && newSpawnsSpawnerList.Count > 0)
                    {
                        select.spawnerThatSpawnedThis = newSpawnsSpawnerList[0];
                        newSpawnsSpawnerList.RemoveAt(0);
                    }
                    Destroy(fakeSpawns[0].gameObject);
                    fakeSpawns.RemoveAt(0);
                    fakeSpawnsReadyForReplacement.RemoveAt(0);
                    break;
                }
            }
        }
    }*/
    #endregion
    #region Selection
    private void DoNotDoubleSelect()
    {
        _doubleSelect = false;
    }
    private void TryToSelectOne()
    {
        DeselectAll();
        if (!_doubleSelect)
        {
            _doubleSelect = true;
            Invoke(nameof(DoNotDoubleSelect), .2f);
            bool valid = SingleSelect();
        }
        else
        {
            _doubleSelect = false;
            DoubleSelectDetected();
        }
    }
    /// <summary>
    /// Update all buttons to be interactable or not based on their cost vs your gold.
    /// </summary>
    public void UpdateButtons() //TODO
    {
        /*for (int i = 0; i < indices.Count; i++)
        {
            Button button = Global.Instance.productionButtons[i];
            FactionUnit fac = _faction.entities[indices[i]];
            button.interactable = gold >= fac.goldCost;
        }*/
    }
    public void UpdateGUIFromSelections()
    {
        //UpdateIndices();
        UpdateGUIBasedOnSelectedUnitCount();
        UpdateButtonsFromSelectedUnits();
        UpdateBuildQueueGUI();
    }
    private void UpdateGUIBasedOnSelectedUnitCount()
    {
        if (Global.Instance.selectedParent != null && Global.Instance.resourcesParent != null && Global.Instance.resourceText != null
            && Global.Instance.nameText != null && Global.Instance.descText != null && Global.Instance.singleUnitInfoParent != null)
        {
            if (infoSelectedEntity != null && selectedEntities.Count == 0)
            {
                Global.Instance.selectedParent.SetActive(true);
                Global.Instance.singleUnitInfoParent.SetActive(true);
                Global.Instance.nameText.text = infoSelectedEntity.displayName;
                Global.Instance.descText.text = infoSelectedEntity.desc;
                Global.Instance.hpText.text = "HP: " + infoSelectedEntity.currentHP.Value + "/" + infoSelectedEntity.maxHP;
                if (infoSelectedEntity.IsHarvester())
                {
                    //Global.Instance.resourcesParent.SetActive(true);
                    //.Instance.resourceText.text = "Stored gold: " + infoSelectedEntity.harvestedResourceAmount + "/" + infoSelectedEntity.harvestCapacity;
                }
            }
            else if (selectedEntities.Count <= 0)
            {
                Global.Instance.selectedParent.SetActive(false);
                Global.Instance.resourcesParent.SetActive(false);
            }
            else if (selectedEntities.Count == 1 && selectedEntities[0] != null)
            {
                Global.Instance.selectedParent.SetActive(true);
                Global.Instance.singleUnitInfoParent.SetActive(true);
                Global.Instance.nameText.text = selectedEntities[0].displayName;
                Global.Instance.descText.text = selectedEntities[0].desc;
                Global.Instance.hpText.text = "HP: " + selectedEntities[0].currentHP.Value + "/" + selectedEntities[0].maxHP;
                if (selectedEntities[0].IsHarvester())
                {
                    //Global.Instance.resourcesParent.SetActive(true);
                    //Global.Instance.resourceText.text = "Stored gold: " + selectedEntities[0].harvestedResourceAmount + "/" + selectedEntities[0].harvestCapacity;
                }
            }
            else
            {
                Global.Instance.selectedParent.SetActive(true);
                Global.Instance.singleUnitInfoParent.SetActive(false);
                //Global.Instance.selectedParent.SetActive(false);
                //Global.Instance.resourcesParent.SetActive(false);
            }
        }
        else
        {
            Debug.LogError("a GUI element needs to be assigned.");
        }
    }
    /// <summary>
    /// Update button abilities displayed based on selected units.
    /// </summary>
    private void UpdateButtonsFromSelectedUnits()
    {
        availableConstructionOptions.Clear();
        availableAbilities.Clear();
        availableUnitSpawns.Clear();
        Global.Instance.ChangeRallyPointButton(false);
        if (selectedEntities.Count > 0) //at least one unit selected
        {
            //show gui elements based on unit type selected
            foreach (SelectableEntity entity in selectedEntities)
            {
                if (entity == null! || !entity.net.IsSpawned || entity.factionEntity == null) //if not built or spawned, skip
                {
                    continue;
                }
                //get abilities
                if (entity.HasAbilities())
                { 
                    foreach (FactionAbility abilityOption in entity.unitAbilities.GetAbilities())
                    {
                        if ((!abilityOption.usableOnlyWhenBuilt && !entity.fullyBuilt) || (entity.fullyBuilt && abilityOption.usableOnlyWhenBuilt))
                        {
                            if (!availableAbilities.Contains(abilityOption)) availableAbilities.Add(abilityOption);
                        }
                    }
                }

                if (!entity.fullyBuilt) continue;

                if (entity.IsSpawner())
                {
                    //get spawnable units
                    foreach (FactionUnit unitOption in entity.spawner.GetSpawnables())
                    {
                        if (!availableUnitSpawns.Contains(unitOption)) availableUnitSpawns.Add(unitOption);
                    }
                    Global.Instance.ChangeRallyPointButton(true);
                } 
                if (entity.IsBuilder())
                { 
                    //get constructable buildings
                    foreach (FactionBuilding buildingOption in entity.builder.GetBuildables())
                    {
                        if (!availableConstructionOptions.Contains(buildingOption)) availableConstructionOptions.Add(buildingOption);
                    }
                }
            }
        }
        //enable a button for each indices 
        for (byte i = 0; i < Global.Instance.productionButtons.Count; i++)
        {
            if (i >= Global.Instance.productionButtons.Count) break; //met limit
            UnityEngine.UI.Button button = Global.Instance.productionButtons[i];
            button.gameObject.SetActive(true);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>();
            button.onClick.RemoveAllListeners();

            if (i < availableAbilities.Count) //abilities
            {
                FactionAbility ability = availableAbilities[i];
                button.onClick.AddListener(delegate { UseAbility(ability); });
                //get lowest ability cooldown 
                float cooldown = 999;
                foreach (SelectableEntity entity in selectedEntities)
                {
                    //Debug.Log(entity.name);
                    if (entity.CanUseAbility(ability)) //if this entity can use the ability
                    {
                        //Debug.Log("can use ability" + ability.name);
                        bool foundAbility = false;
                        List<AbilityOnCooldown> usedAbilities = entity.unitAbilities.GetUsedAbilities();
                        for (int j = 0; j < usedAbilities.Count; j++) //find the ability and set the cooldown
                        {
                            if (usedAbilities[j].abilityName == ability.abilityName) //does the ability match?
                            {
                                foundAbility = true;
                                if (usedAbilities[j].cooldownTime < cooldown) //is the cooldown lower than the current cooldown?
                                {
                                    cooldown = usedAbilities[j].cooldownTime;
                                    //Debug.Log("found ability");
                                }
                                break;
                            }
                        }
                        if (foundAbility == false) cooldown = 0;

                        //Debug.Log("found result: " + foundAbility);
                    }
                }
                if (cooldown <= 0)
                {
                    text.text = ability.abilityName;
                }
                else
                {
                    text.text = ability.abilityName + ": " + Mathf.RoundToInt(cooldown);
                }
                button.interactable = cooldown <= 0;
            }
            else if (i < availableAbilities.Count + availableUnitSpawns.Count) //spawns
            {
                FactionUnit fac = availableUnitSpawns[i - availableAbilities.Count];
                button.interactable = gold >= fac.goldCost;
                text.text = fac.productionName + ": " + fac.goldCost + "g";
                button.onClick.AddListener(delegate { QueueBuildingSpawn(fac); });
            }
            else if (i < availableAbilities.Count + availableConstructionOptions.Count + availableUnitSpawns.Count) //buildings
            {
                FactionBuilding fac = availableConstructionOptions[i - availableAbilities.Count - availableUnitSpawns.Count];
                button.interactable = gold >= fac.goldCost;
                text.text = fac.productionName + ": " + fac.goldCost + "g";
                button.onClick.AddListener(delegate { HoverBuild(fac); });
            }
            else
            {
                Global.Instance.productionButtons[i].gameObject.SetActive(false);
            }
        }
    }
    /// <summary>
    /// All selected units that have this ability will attempt to use it
    /// </summary>
    /// <param name="ability"></param>
    private void UseAbility(FactionAbility ability)
    {
        if (selectedEntities.Count > 0) //at least one unit selected
        {
            foreach (SelectableEntity entity in selectedEntities)
            {
                //skip if not built, not spawned, n
                if (EntityCanUseAbility(entity, ability))
                {
                    if (entity.IsMinion())
                    {
                        entity.unitAbilities.StartUsingAbility(ability);
                    }
                    else
                    {
                        entity.unitAbilities.ActivateAbility(ability);
                    }
                }
            }
        }
    }
    private bool EntityCanUseAbility(SelectableEntity entity, FactionAbility ability)
    {
        return entity != null && ability != null && (entity.fullyBuilt || !ability.usableOnlyWhenBuilt) && entity.net.IsSpawned
            && entity.alive && entity.CanUseAbility(ability)
            && entity.unitAbilities.AbilityOffCooldown(ability)
            && (entity.IsBuilding() || entity.sm.GetState() != StateMachineController.EntityStates.UsingAbility);
    }
    public void UpdateBuildQueueGUI()
    {
        Global.Instance.queueParent.gameObject.SetActive(false);
        if (selectedEntities.Count != 1) return; //only works with a single unit for now

        SelectableEntity selectedProductionEntity = selectedEntities[0];
        //only works if is production structure, fully built, and spawned
        if (!selectedProductionEntity.IsSpawner() || !selectedProductionEntity.fullyBuilt || !selectedProductionEntity.net.IsSpawned) return;

        Global.Instance.queueParent.gameObject.SetActive(true);
        int num = Mathf.Clamp(selectedProductionEntity.buildQueue.Count, 0, Global.Instance.queueButtons.Count);

        //enable a button for each indices
        for (int i = 0; i < Global.Instance.queueButtons.Count; i++)
        {
            Global.Instance.queueButtons[i].gameObject.SetActive(false);
            if (i < num)
            {
                UpdateButton(selectedProductionEntity, i);
            }
        }
        //get the progress of the first unit;
        FactionUnit beingProduced = null;
        if (selectedProductionEntity.buildQueue.Count > 0)
        {
            beingProduced = selectedProductionEntity.buildQueue[0];
        }
        if (beingProduced != null && Global.Instance.structureProgressBar != null)
        {
            Global.Instance.structureProgressBar.SetRatio(beingProduced.spawnTimer, beingProduced.maxSpawnTimeCost-1);
            //Debug.Log(beingProduced.spawnTimer + " / " + beingProduced.maxSpawnTimeCost);
        }
        else
        {
            Global.Instance.structureProgressBar.SetRatio(0, 1);
        }
        if (selectedProductionEntity.productionBlocked)
        {
            Global.Instance.structureProgressBar.SetColor(Color.red);
        }
        else
        {
            Global.Instance.structureProgressBar.SetColor(Color.white);
        }
    }
    private void UpdateButton(SelectableEntity select, int i = 0)
    {
        UnityEngine.UI.Button button = Global.Instance.queueButtons[i];
        button.gameObject.SetActive(true);
        TMP_Text text = button.GetComponentInChildren<TMP_Text>();
        FactionUnit fac = select.buildQueue[i];
        text.text = fac.productionName;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(delegate { DequeueProductionOrder(i); });
    }
    public void AddGold(int count)
    {
        gold += count;
    }
    public void DequeueProductionOrder(int index = 0) //on click remove thing from queue and refund gold
    {
        if (selectedEntities.Count == 1)
        {
            SelectableEntity select = selectedEntities[0];
            FactionUnit fac = select.buildQueue[index];
            gold += fac.goldCost;
            select.buildQueue.RemoveAt(index);
            UpdateGUIFromSelections();
        }
    }
    private void QueueBuildingSpawn(FactionUnit unit)
    {
        //necessary to create new so we don't accidentally affect the files
        /*FactionUnit newUnit = new()
        {
            productionName = unit.prefabToSpawn.name,
            spawnTimeCost = unit.spawnTimeCost,
            prefabToSpawn = unit.prefabToSpawn,
            goldCost = unit.goldCost, 
        };*/
        //Debug.Log("Trying to spawn :" + unit.name);
        FactionUnit newUnit = FactionUnit.CreateInstance(unit.productionName, unit.maxSpawnTimeCost, unit.prefabToSpawn, unit.goldCost);

        int cost = newUnit.goldCost;
        //try to spawn from all selected buildings if possible 
        foreach (SelectableEntity select in selectedEntities)
        {
            if (gold < cost || !select.net.IsSpawned || !select.fullyBuilt || !TargetCanSpawnThisEntity(select, newUnit)
                || select.buildQueue.Count >= Global.Instance.maxUnitsInProductionQueue) break;
            //if requirements fulfilled
            gold -= cost;
            select.buildQueue.Add(newUnit);
            //Debug.Log("Added" + newUnit.name + " to queue");
        }
        UpdateBuildQueueGUI();
    }
    private bool TargetCanSpawnThisEntity(SelectableEntity target, FactionEntity entity)
    {
        for (int i = 0; i < target.spawner.GetSpawnables().Length; i++)
        {
            if (target.spawner.GetSpawnables()[i].productionName == entity.productionName) return true;
        }
        return false;
    }
    /// <summary>
    /// Create building ghost showing where building will be placed
    /// </summary>
    /// <param name="id"></param>
    private void HoverBuild(FactionBuilding building) //start building
    {
        mouseState = MouseState.ReadyToPlace;
        placementBlocked = false;
        buildingToPlace = building;
        GameObject build = building.prefabToSpawn;

        if (meshes.Length > 0)
        {
            for (int i = 0; i < meshes.Length; i++)
            {
                Destroy(meshes[i]);
            }
        }
        ClearWallGhosts();

        GameObject spawn = Instantiate(build, Vector3.zero, Quaternion.Euler(0, 180, 0)); //spawn ghost
        SelectableEntity entity = spawn.GetComponent<SelectableEntity>();
        buildOffset = entity.buildOffset;
        /*
            placingPortal = true;*/
        if (building.extendable)
        {
            linkedState = LinkedState.PlacingStart;
        }
        else
        {
            linkedState = LinkedState.Waiting;
        }
        entity.isBuildIndicator = true;
        buildingGhost = entity;
        meshes = spawn.GetComponentsInChildren<MeshRenderer>();
        for (int i = 0; i < meshes.Length; i++)
        {
            meshes[i].material = Global.Instance.transparent;
        }
        Collider[] colliders = spawn.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].isTrigger = true;
        }
    }
    private bool SingleSelect()
    {
        bool successful = false;
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        if (!Input.GetKey(KeyCode.LeftShift)) //deselect all if not pressing shift
        {
            DeselectAll();
        }
        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity, Global.Instance.allEntityLayer))
        {
            //SelectableEntity entity = hit.collider.GetComponent<SelectableEntity>();

            SelectableEntity entity = Global.Instance.FindEntityFromObject(hit.collider.gameObject);
            if (entity != null && PositionFullyVisible(entity.transform.position))
            {
                successful = TrySelectEntity(entity);
            }
        }
        return successful;
    }
    private void DoubleSelectDetected() //double click
    {
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        if (!Input.GetKey(KeyCode.LeftShift)) //deselect all if not pressing shift
        {
            DeselectAll();
        }
        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity, Global.Instance.allEntityLayer))
        {
            //SelectableEntity entity = hit.collider.GetComponent<SelectableEntity>();

            SelectableEntity entity = Global.Instance.FindEntityFromObject(hit.collider.gameObject);
            if (entity != null && ownedEntities.Contains(entity))
            {
                if (entity.occupiedGarrison != null)
                {
                    SelectAllInSameGarrison(entity.occupiedGarrison);
                }
                else
                {
                    SelectAllSameTypeExcludingInGarrisons(entity);
                }
            }
        }

        UpdateGUIFromSelections();
    }
    private void SelectAllInSameGarrison(SelectableEntity garrison)
    {
        /*foreach (GarrisonablePosition item in garrison.garrisonablePositions)
        {
            if (item.passenger != null)
            {
                if (item.passenger.ent != null)
                {
                    TrySelectEntity(item.passenger.ent);
                }
            }
        }*/
    }
    private void SelectAllSameTypeExcludingInGarrisons(SelectableEntity entity)
    {
        foreach (SelectableEntity potential in ownedEntities)
        {
            if (potential.occupiedGarrison == null)
            {
                if ((entity.IsBuilder() && potential.IsBuilder()) ||
                    (entity.IsHarvester() && potential.IsHarvester()) ||
                    (entity.IsFighter() && potential.IsFighter() && entity.IsMelee() && potential.IsMelee()) ||
                    (entity.IsFighter() && potential.IsFighter() && !entity.IsMelee() && !potential.IsMelee()) ||
                    (entity.CannotConstructHarvestProduce() && potential.CannotConstructHarvestProduce() && !entity.IsMinion() && !potential.IsMinion()) ||
                    (entity.IsSpawner() && potential.IsSpawner()))
                {
                    TrySelectEntity(potential);
                }
            }
        }
    }
    public void DeselectSpecific(SelectableEntity entity)
    {
        selectedEntities.Remove(entity);
        entity.Select(false);
        UpdateGUIFromSelections();
    }
    private void DeselectAll()
    {
        foreach (SelectableEntity item in selectedEntities)
        {
            item.Select(false);
        }
        selectedEntities.Clear();
        selectedBuilders.Clear();
        if (infoSelectedEntity != null) infoSelectedEntity.InfoSelect(false);
        infoSelectedEntity = null;
        UpdateGUIFromSelections();
    }
    #endregion




    /// <summary>
    /// Damages all in radius at point.
    /// </summary> 
    public void CreateExplosionAtPoint(Vector3 center, float explodeRadius, sbyte damage = 10)
    {
        Collider[] hitColliders = new Collider[40];
        int numColliders = Physics.OverlapSphereNonAlloc(center, explodeRadius, hitColliders, Global.Instance.allEntityLayer);
        for (int i = 0; i < numColliders; i++)
        {
            if (hitColliders[i] == null) continue;
            SelectableEntity select = hitColliders[i].GetComponent<SelectableEntity>();
            if (select == null) continue;
            if (Global.Instance.localPlayer.ownedEntities.Contains(select)) //skip teammates
            {
                continue;
            }
            if (select.teamType == SelectableEntity.TeamBehavior.FriendlyNeutral)
            {
                continue;
            }
            if (!select.isTargetable.Value)
            {
                continue;
            }
            Debug.Log("Explosion affecting: " + select.name);
            DamageEntity(damage, select);
        }
    }
    public void DamageEntity(sbyte damage, SelectableEntity enemy) //since hp is a network variable, changing it on the server will propagate changes to clients as well
    {
        if (enemy != null)
        {
            //fire locally 
            if (IsServer)
            {
                enemy.TakeDamage(damage);
            }
            else //client tell server to change the network variable
            {
                PredictAndRequestDamage(damage, enemy);
            }
        }
    }
    private void PredictAndRequestDamage(sbyte damage, SelectableEntity enemy)
    {
        //if we know that this attack will kill that unit, we can kill it client side
        if (damage >= enemy.currentHP.Value)
        {
            Debug.Log("can kill early" + enemy.currentHP.Value);
            enemy.PrepareForEntityDestruction();
        }
        Global.Instance.localPlayer.RequestDamageServerRpc(damage, enemy);
    }

    [ServerRpc]
    public void RequestDamageServerRpc(sbyte damage, NetworkBehaviourReference enemy)
    {
        //server must handle damage! 
        if (enemy.TryGet(out SelectableEntity select))
        {
            select.TakeDamage(damage);
        }
    }
    public void SpawnExplosion(Vector3 pos)
    {
        GameObject prefab = Global.Instance.explosionPrefab;
        _ = Instantiate(prefab, pos, Quaternion.identity);
    }
}
