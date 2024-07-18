using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using UnityEngine.EventSystems;// Required when using Event data.
using TMPro;
using System.Linq;
using FoW;
using UnityEngine.Rendering;
using System;
/*using UnityEditor.Playables;
using UnityEditor.ShaderGraph.Internal;
using Unity.Burst.CompilerServices;
using static UnityEditor.Progress;*/

public class RTSPlayer : Player
{
    public NetworkVariable<bool> inTheGame = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public List<SelectableEntity> keystoneUnits = new();
    [SerializeField] private Grid grid;
    private Vector3Int _gridPosition;
    public List<SelectableEntity> selectedEntities; //selected and we can control them 
    public List<SelectableEntity> enemyEntities; 
    private Vector3 _mousePosition;
    private Vector3 _offset;
    public Vector3 cursorWorldPosition;
    bool _doubleSelect = false;
    public enum MouseState
    {
        Waiting,
        ReadyToPlace,
        ReadyToSetRallyPoint
    }
    public enum LinkedState
    {
        Waiting,
        PlacingStart,
        PlacingEnd
    }
    public LinkedState linkedState = LinkedState.Waiting;
    public MouseState mouseState = MouseState.Waiting;
    public LayerMask groundLayer;
    public LayerMask entityLayer;
    private LayerMask blockingLayer;
    public SelectableEntity lastSpawnedEntity;
    public bool placementBlocked = false;
    private GameObject followCursorObject; 
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
    public enum ActionType
    {
        Move, AttackTarget, Harvest, Deposit, Garrison, BuildTarget
    }
    private ActionType actionType = ActionType.Move;
    private FactionBuilding buildingToPlace = null;
    public List<SelectableEntity> militaryList = new();
    public List<SelectableEntity> builderList = new();
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
    public override void Start()
    {
        base.Start();
        groundLayer = LayerMask.GetMask("Ground");
        blockingLayer = LayerMask.GetMask("Entity", "Obstacle");
        entityLayer = LayerMask.GetMask("Entity");
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
    private void MoveCamToSpawn()
    {
        Vector3 spawn = Global.Instance.playerSpawn[System.Convert.ToInt32(OwnerClientId)].position;
        camParent.position = new Vector3(spawn.x, camParent.position.y, spawn.z);
    }
    public void UpdateHPText()
    {
        if (selectedEntities.Count == 1)
        {
            Global.Instance.hpText.text = "HP: " + selectedEntities[0].hitPoints.Value + "/" + selectedEntities[0].maxHP;
        }
    }
    private void OnDisable()
    {
        Global.Instance.uninitializedPlayers.Remove(this);
    }
    public override void OnNetworkSpawn()
    {
        teamID = System.Convert.ToInt32(OwnerClientId);
        Global.Instance.uninitializedPlayers.Add(this);
        if (IsOwner) //spawn initial minions/buildings  
        {
            inTheGame.Value = true;
            Global.Instance.localPlayer = this;
            Vector3 spawnPosition;
            if (teamID < Global.Instance.playerSpawn.Count)
            {
                spawnPosition = Global.Instance.playerSpawn[teamID].position;
            }
            else
            {
                spawnPosition = new Vector3(UnityEngine.Random.Range(-9, 9), 0, UnityEngine.Random.Range(-9, 9));
            }

            GenericSpawnMinion(spawnPosition, playerFaction.spawnableEntities[0], this);
            //GenericSpawnMinion(spawn, starterUnitID, this);

            VolumeProfile profile = Global.Instance.fogVolume.sharedProfile;
            if (profile != null && profile.TryGet(out FogOfWarURP fow))
            {
                fow.team.value = teamID;
            }
        }
        else
        {
            active = false;
        }
        //playerFaction = Global.Instance.factions[teamID];
    }
    private bool MouseOverUI()
    {
        return EventSystem.current.IsPointerOverGameObject();
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
        foreach (SelectableEntity item in selectedEntities)
        {
            if (item.minionController != null) //minion
            {
                print("attack moving");
                //item.minionController.ClearGivenMission();
                item.minionController.SetAttackMoveDestination();
            }
        }
    }
    private void SetSelectedDestination() //RTS entity right click behavior
    {
        //when player right clicks, get position on map
        //tell other clients that this happened
        Vector3 clickedPosition;
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity, Global.Instance.localPlayer.gameLayer))
        {
            clickedPosition = hit.point;

            FogOfWarTeam fow = FogOfWarTeam.GetTeam((int)OwnerClientId);
            SelectableEntity select = Global.Instance.FindEntityFromObject(hit.collider.gameObject);
            //SelectableEntity select = hit.collider.GetComponent<SelectableEntity>();
            if (select != null && fow.GetFogValue(select.transform.position) <= 0.51f * 255) //if exists and is explored at least
            {
                if (select.teamType == SelectableEntity.TeamBehavior.OwnerTeam)
                {
                    if (select.net.OwnerClientId == OwnerClientId) //same team
                    {
                        if ((select.depositType == SelectableEntity.DepositType.Gold || select.depositType == SelectableEntity.DepositType.All)
                            && select.fullyBuilt) //if deposit point
                        {
                            actionType = ActionType.Deposit;
                        }
                        else if (!select.fullyBuilt || select.IsDamaged()) //if buildable
                        {
                            Debug.Log("not full built");
                            actionType = ActionType.BuildTarget;
                        }
                        else if (select.fullyBuilt && select.HasEmptyGarrisonablePosition())
                        { //target can be garrisoned, and passenger cannot garrison, then enter
                            actionType = ActionType.Garrison;
                        }
                        else if (select.occupiedGarrison != null && select.occupiedGarrison.HasEmptyGarrisonablePosition())
                        { //target is passenger of garrison, then enter garrison
                            actionType = ActionType.Garrison;
                            select = select.occupiedGarrison;
                        }
                    }
                    else //enemy
                    { //try to target this enemy specifically
                        actionType = ActionType.AttackTarget;
                    }
                }
                else if (select.teamType == SelectableEntity.TeamBehavior.FriendlyNeutral)
                {
                    if (select.selfHarvestableType != SelectableEntity.ResourceType.None)
                    {
                        actionType = ActionType.Harvest;
                    }
                }
            }
            else
            {
                actionType = ActionType.Move;
            }
            for (int i = 0; i < selectedEntities.Count; i++)
            {
                SelectableEntity item = selectedEntities[i];
                /*Vector2 circle = UnityEngine.Random.insideUnitCircle * i * 0.5f;
                Vector3 vec = clickedPosition + new Vector3(circle.x, 0, circle.y);*/
                if (item.minionController != null) //minion
                {
                    //item.minionController.ClearGivenMission();
                    switch (actionType)
                    {
                        case ActionType.Move:
                            item.minionController.MoveTo(clickedPosition);
                            break;
                        case ActionType.AttackTarget:
                            item.minionController.AttackTarget(select);
                            break;
                        case ActionType.Harvest:
                            item.minionController.CommandHarvestTarget(select);
                            break;
                        case ActionType.Deposit: //try to deposit if we have stuff to deposit
                            if (item.HasResourcesToDeposit())
                            { 
                                item.minionController.DepositTo(select);
                            }
                            else if (select.IsDamaged() && item.CanConstruct()) //if its damaged, we can try to build it
                            { 
                                item.minionController.CommandBuildTarget(select);
                            }
                            break;
                        case ActionType.Garrison:
                            item.minionController.GarrisonInto(select);
                            break;
                        case ActionType.BuildTarget://try determining how many things need to be built in total, and grabbing closest ones
                            if (item.CanConstruct())
                            { 
                                item.minionController.CommandBuildTarget(select);
                            }
                            else
                            { 
                                item.minionController.GarrisonInto(select);
                            }
                            break;
                        default:
                            break;
                    }
                }
                else //building
                {
                    item.SetRally();
                }
            }
            /*if (IsServer)
            {
                Debug.Log("Server telling client about the order");
                ReportOrderClientRpc(actionType, clickedPosition);
            }*/
        }

        /*else
        { 
            Debug.Log("client tells server about order but doesn't execute it yet");
            ReportOrderServerRpc();
        }*/
    }

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
        return entity.occupiedGarrison == null;
    }
    private void SelectAllAttackers()
    {
        DeselectAll();
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item != null && item.minionController != null &&
                item.minionController.attackType != MinionController.AttackType.None && !IsEntityGarrrisoned(item))
            {
                TrySelectEntity(item);
            }
        }
    }
    private void SelectAllProduction()
    {
        DeselectAll();
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item != null && item.CanProduceUnits())
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
            if (item != null && item.minionController != null && (item.CanConstruct())) //&& item.canBuild
            {
                switch (item.minionController.minionState)
                {
                    case MinionController.MinionStates.Idle:
                    case MinionController.MinionStates.FindInteractable:
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
    /// Try to select an entity. This will only succeed if they're on our team or neutral.
    /// </summary>
    /// <param name="entity"></param>
    private bool TrySelectEntity(SelectableEntity entity) //later make this able to info select
    {
        bool val = false;
        if (IsTargetExplicitlyOnOurTeam(entity))
        {
            selectedEntities.Add(entity);
            entity.Select(true);
            val = true;
        }
        else
        {
            infoSelectedEntity = entity;
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
        if (Input.GetKeyDown(KeyCode.Q))
        {
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
#if UNITY_EDITOR //DEBUG COMMANDS
        if (Input.GetKeyDown(KeyCode.RightShift))
        {
            GenericSpawnMinion(cursorWorldPosition, playerFaction.spawnableEntities[1], this);
        }
        /*if (Input.GetKeyDown(KeyCode.RightAlt))
        {
            GenericSpawnMinion(cursorWorldPosition, 2, this);
        }*/
        if (Input.GetKeyDown(KeyCode.RightControl))
        {
            GenericSpawnMinion(cursorWorldPosition, playerFaction.spawnableEntities[3], this);
        }
        /*if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            GenericSpawnMinion(cursorWorldPosition, 11, this);
        }*/
#endif
    }
    void Update()
    {
        /*FogOfWarTeam fow = FogOfWarTeam.GetTeam((int)OwnerClientId);
        Debug.Log(fow.GetFogValue(cursorWorldPosition));*/
        if (!active) return;
        UpdatePlacementBlockedStatus();
        UpdateGridVisual();
        DetectHotkeys();
        CameraMove();
        if (!MouseOverUI())
        {
            GetMouseWorldPosition();

            if (linkedState == LinkedState.PlacingEnd)
            {
                CalculateFillCost(startWallPosition, cursorWorldPosition, buildingToPlace);
            }
            if (Input.GetMouseButtonDown(0)) //left click
            {
                infoSelectedEntity = null;
                StartMousePosition = Input.mousePosition;
                ResizeSelection();
                Global.Instance.selectionRect.gameObject.SetActive(true);
                switch (mouseState)
                {
                    case MouseState.Waiting:
                        TryToSelectOne();
                        break;
                    case MouseState.ReadyToPlace:
                        if (!placementBlocked)
                        {
                            PlaceBuilding(buildingToPlace);
                        }
                        break;
                    case MouseState.ReadyToSetRallyPoint:
                        mouseState = MouseState.Waiting;
                        SetSelectedRallyPoint();
                        break;
                    default:
                        break;
                }
            }
            if (Input.GetMouseButtonDown(2) || Input.GetKeyDown(KeyCode.R)) //middle click
            {
                SelectedAttackMove();
            }
            if (Input.GetMouseButtonDown(1)) //right click
            { //used to cancel most commands, or move
                switch (mouseState)
                {
                    case MouseState.Waiting:
                        SetSelectedDestination();
                        break;
                    case MouseState.ReadyToPlace:
                        if (!placingLinkedBuilding)
                        {
                            StopPlacingBuilding();
                        }
                        break;
                    case MouseState.ReadyToSetRallyPoint:
                        mouseState = MouseState.Waiting;
                        break;
                    default:
                        break;
                }
            }
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
        if (!Input.GetMouseButton(0) && finishedSelection)
        {
            Global.Instance.selectionRect.gameObject.SetActive(false);
        }
        Global.Instance.goldText.text = "Gold: " + gold;
        if (Global.Instance.popText != null)
        {
            Global.Instance.popText.text = population + "/" + maxPopulation + " Population";
        }
        //TryReplaceFakeSpawn();
        UpdateGUIFromSelections();// this might be expensive ...
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
            builderList = new();
            productionList = new();
            foreach (SelectableEntity item in evaluation)
            {
                if (item.CanConstruct())
                {
                    builderList.Add(item);
                }
                else if (item.minionController != null && item.minionController.attackType != MinionController.AttackType.None)
                {
                    militaryList.Add(item);
                }
                else if (item.CanProduceUnits())
                {
                    productionList.Add(item);
                }
            }
            int mil = militaryList.Count;
            int build = builderList.Count;
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
                    useList = builderList;
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
    private void ResizeSelection()
    {
        finishedSelection = false;
        RectTransform SelectionBox = Global.Instance.selectionRect;
        float width = Input.mousePosition.x - StartMousePosition.x;
        float height = Input.mousePosition.y - StartMousePosition.y;

        SelectionBox.anchoredPosition = StartMousePosition + new Vector2(width / 2, height / 2);
        SelectionBox.sizeDelta = new Vector2(Mathf.Abs(width), Mathf.Abs(height));
    }
    private void TellSelectedToBuild(SelectableEntity last)
    {
        foreach (SelectableEntity item in selectedEntities)
        {
            if (item.minionController != null)
            {
                item.minionController.ForceBuildTarget(item);
            }
        }
    }
    private void PlaceBuilding(FactionBuilding building)
    {
        switch (linkedState)
        {
            case LinkedState.Waiting:
                NormalPlaceBuilding(building);
                break;
            case LinkedState.PlacingStart:
                linkedState = LinkedState.PlacingEnd;
                startWallPosition = cursorWorldPosition;
                Destroy(followCursorObject);
                break;
            case LinkedState.PlacingEnd:
                int cost = CalculateFillCost(startWallPosition, cursorWorldPosition, building);
                if (gold >= cost)
                {
                    gold -= cost;
                    WallFill(building);
                    StopPlacingBuilding();
                }
                break;
            default:
                break;
        }
    }
    private void NormalPlaceBuilding(FactionBuilding building)
    {
        if (gold < building.goldCost) return;
        gold -= building.goldCost;
        Debug.Log("Trying to place" + building.name);
        GenericSpawnMinion(cursorWorldPosition, building, this);
        SelectableEntity last = Global.Instance.localPlayer.ownedEntities.Last();
        TellSelectedToBuild(last);
        //temporary: later re-implement two-part buildings and holding shift to continue placing
        StopPlacingBuilding();


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
        FogOfWarTeam fow = FogOfWarTeam.GetTeam((int)OwnerClientId);
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
        Destroy(followCursorObject);
        followCursorObject = null;
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
        placementBlocked = IsPositionBlocked(cursorWorldPosition);
        if (placementBlocked != oldPlacement)
        {
            oldPlacement = placementBlocked;
            UpdatePlacementMeshes();
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
    private void GetMouseWorldPosition()
    {
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity, groundLayer))
        {
            _mousePosition = hit.point;
            _gridPosition = grid.WorldToCell(_mousePosition);
            cursorWorldPosition = grid.CellToWorld(_gridPosition) + buildOffset;
            cursorWorldPosition = new Vector3(cursorWorldPosition.x, hit.point.y, cursorWorldPosition.z);
            if (followCursorObject != null)
            {
                followCursorObject.transform.position = cursorWorldPosition;//new Vector3(cursorWorldPosition.x, hit.point.y, cursorWorldPosition.z);// + new Vector3(0, 5, 0);
            }
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
            Debug.Log("SERVER: spawning " + unit.productionName);
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
                }
                //grant ownership 
                if (NetworkManager.ConnectedClients.ContainsKey(clientID))
                {
                    select.clientIDToSpawnUnder = clientID;
                    Debug.Log("Granting ownership of " + select.name + " to client " + clientID);
                    //select.net.ChangeOwnership(clientID);
                    //select.net.Spawn(); 
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
    private void TryReplaceFakeSpawn() //not being used yet
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
                    MinionController fakeController = fakeSpawns[0].GetComponent<MinionController>();
                    newSpawn.transform.SetPositionAndRotation(fakeSpawns[0].transform.position, fakeSpawns[0].transform.rotation);
                    if (select != null && select.minionController != null && fake != null && fakeController != null)
                    {
                        select.minionController.minionState = fakeController.minionState;
                        select.minionController.minionState = MinionController.MinionStates.Idle;
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
    }
    #endregion
    #region Selection
    private void DoNotDoubleSelect()
    {
        _doubleSelect = false;
    }
    private void TryToSelectOne()
    {
        if (!_doubleSelect)
        {
            _doubleSelect = true;
            Invoke(nameof(DoNotDoubleSelect), .2f);
            SingleSelect();
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
                Global.Instance.hpText.text = "HP: " + infoSelectedEntity.hitPoints.Value + "/" + infoSelectedEntity.maxHP;
                if (infoSelectedEntity.isHarvester)
                {
                    Global.Instance.resourcesParent.SetActive(true);
                    Global.Instance.resourceText.text = "Stored gold: " + infoSelectedEntity.harvestedResourceAmount + "/" + infoSelectedEntity.harvestCapacity;
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
                Global.Instance.hpText.text = "HP: " + selectedEntities[0].hitPoints.Value + "/" + selectedEntities[0].maxHP;
                if (selectedEntities[0].isHarvester)
                {
                    Global.Instance.resourcesParent.SetActive(true);
                    Global.Instance.resourceText.text = "Stored gold: " + selectedEntities[0].harvestedResourceAmount + "/" + selectedEntities[0].harvestCapacity;
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
                foreach (FactionAbility abilityOption in entity.usableAbilities)
                {
                    if (!abilityOption.usableOnlyWhenBuilt || entity.fullyBuilt)
                    {
                        if (!availableAbilities.Contains(abilityOption)) availableAbilities.Add(abilityOption);
                    }
                }
                if (!entity.fullyBuilt) continue;
                //get spawnable units
                foreach (FactionUnit unitOption in entity.spawnableUnits)
                {
                    if (!availableUnitSpawns.Contains(unitOption)) availableUnitSpawns.Add(unitOption);
                }
                //get constructable buildings
                foreach (FactionBuilding buildingOption in entity.constructableBuildings)
                {
                    if (!availableConstructionOptions.Contains(buildingOption)) availableConstructionOptions.Add(buildingOption);
                }
            }
        }
        //enable a button for each indices 
        for (byte i = 0; i < Global.Instance.productionButtons.Count; i++)
        {
            if (i >= Global.Instance.productionButtons.Count) break; //met limit
            Button button = Global.Instance.productionButtons[i];
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
                        for (int j = 0; j < entity.usedAbilities.Count; j++) //find the ability and set the cooldown
                        {
                            if (entity.usedAbilities[j].abilityName == ability.abilityName) //does the ability match?
                            {
                                foundAbility = true;
                                if (entity.usedAbilities[j].cooldownTime < cooldown) //is the cooldown lower than the current cooldown?
                                {
                                    cooldown = entity.usedAbilities[j].cooldownTime;
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
                    entity.UseAbility(ability);
                }
            }
        }
    }
    private bool EntityCanUseAbility(SelectableEntity entity, FactionAbility ability)
    {
        return (entity != null && (entity.fullyBuilt || !ability.usableOnlyWhenBuilt) && entity.net.IsSpawned
            && entity.alive && entity.usableAbilities.Contains(ability)
            && entity.AbilityOffCooldown(ability));
    }
    public void UpdateBuildQueueGUI()
    {
        Global.Instance.queueParent.gameObject.SetActive(false);
        if (selectedEntities.Count != 1) return; //only works with a single unit for now

        SelectableEntity select = selectedEntities[0];
        //only works if is production structure, fully built, and spawned
        if (!select.CanProduceUnits() || !select.fullyBuilt || !select.net.IsSpawned) return;

        Global.Instance.queueParent.gameObject.SetActive(true);
        int num = Mathf.Clamp(select.buildQueue.Count, 0, Global.Instance.queueButtons.Count);
        //enable a button for each indices

        for (int i = 0; i < Global.Instance.queueButtons.Count; i++)
        {
            Global.Instance.queueButtons[i].gameObject.SetActive(false);
            if (i < num)
            {
                UpdateButton(select, i);
            }
        }
    }
    private void UpdateButton(SelectableEntity select, int i = 0)
    {
        Button button = Global.Instance.queueButtons[i];
        button.gameObject.SetActive(true);
        TMP_Text text = button.GetComponentInChildren<TMP_Text>();
        FactionUnit fac = select.buildQueue[i];
        text.text = fac.productionName + ": " + fac.spawnTimeCost + "s";
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
        FactionUnit newUnit = FactionUnit.CreateInstance(unit.prefabToSpawn.name, unit.spawnTimeCost, unit.prefabToSpawn, unit.goldCost);

        int cost = newUnit.goldCost;
        //try to spawn from all selected buildings if possible 
        foreach (SelectableEntity select in selectedEntities)
        {
            if (gold < cost || !select.net.IsSpawned || !select.fullyBuilt || !TargetCanSpawnThisEntity(select, newUnit)) break;
            //if requirements fulfilled
            gold -= cost;
            select.buildQueue.Add(newUnit);
        }
        UpdateBuildQueueGUI();
    }
    private bool TargetCanSpawnThisEntity(SelectableEntity target, FactionEntity entity)
    {
        for (int i = 0; i < target.spawnableUnits.Length; i++)
        {
            if (target.spawnableUnits[i].productionName == entity.productionName) return true; //if ability is in the used abilities list, then we still need to wait  
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
        followCursorObject = spawn;
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
    private void SingleSelect()
    {
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        if (!Input.GetKey(KeyCode.LeftShift)) //deselect all if not pressing shift
        {
            DeselectAll();
        }
        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity, entityLayer))
        {
            //SelectableEntity entity = hit.collider.GetComponent<SelectableEntity>();

            SelectableEntity entity = Global.Instance.FindEntityFromObject(hit.collider.gameObject);
            if (entity != null)
            {
                TrySelectEntity(entity);
            }
        }
    }
    private void DoubleSelectDetected()
    {
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        if (!Input.GetKey(KeyCode.LeftShift)) //deselect all if not pressing shift
        {
            DeselectAll();
        }
        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity, entityLayer))
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
        foreach (GarrisonablePosition item in garrison.garrisonablePositions)
        {
            if (item.passenger != null)
            {
                if (item.passenger.entity != null)
                {
                    TrySelectEntity(item.passenger.entity);
                }
            }
        }
    }
    private void SelectAllSameTypeExcludingInGarrisons(SelectableEntity entity)
    {
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item.occupiedGarrison == null)
            {
                if ((entity.CanConstruct() && item.CanConstruct()) ||
                    (entity.minionController != null && entity.minionController.attackType != MinionController.AttackType.None
                    && item.minionController != null && item.minionController.attackType != MinionController.AttackType.None) ||
                    (entity.CanProduceUnits() && item.CanProduceUnits()))
                {
                    TrySelectEntity(item);
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
        UpdateGUIFromSelections();
    }
    #endregion

    /// <summary>
    /// Damages all in radius at point.
    /// </summary> 
    public void CreateExplosionAtPoint(Vector3 center, float explodeRadius, sbyte damage = 10)
    {
        Collider[] hitColliders = new Collider[40];
        int numColliders = Physics.OverlapSphereNonAlloc(center, explodeRadius, hitColliders, entityLayer);
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
        if (damage >= enemy.hitPoints.Value)
        {
            Debug.Log("can kill early" + enemy.hitPoints.Value);
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
