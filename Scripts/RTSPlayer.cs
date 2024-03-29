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
using UnityEditor.Playables;

public class RTSPlayer : NetworkBehaviour
{
    public NetworkVariable<bool> inTheGame = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public List<SelectableEntity> keystoneUnits = new();
    public int gold = 100;
    [SerializeField] private Grid grid;
    private Vector3Int _gridPosition;
    public List<SelectableEntity> ownedEntities; //must be serialized or public
    public List<SelectableEntity> selectedEntities;
    public List<SelectableEntity> enemyEntities;

    //[SerializeField] private FactionScriptableObject _faction; 
    public Faction playerFaction;

    [SerializeField] private int _entitiesIndex = 0; //used to pick a prefab from faction list
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
    public SelectableEntity lastSpawnedEntity;
    public bool placementBlocked = false;
    private GameObject followCursorObject;
    private byte buildingPlacingID = 0;
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
    public int population = 0;
    public int maxPopulation = 100;
    public LayerMask placementGhost;
    public LayerMask gameLayer;
    public Camera mainCam;
    public Camera lineCam;
    public void LoseGame()
    {
        inTheGame.Value = false;
    }
    void Start()
    {
        groundLayer = LayerMask.GetMask("Ground");
        entityLayer = LayerMask.GetMask("Entity");
        gameLayer = LayerMask.GetMask("Entity", "Obstacle", "Ground");
        placementGhost = LayerMask.GetMask("PlacementGhost");
        //_offset = new Vector3(0.5f, 0, .5f);
        _offset = new Vector3(0.25f, 0, .25f);
        //_offset = new Vector3(0, 0, 0);
        mainCam = Global.Instance.mainCam;
        lineCam = Global.Instance.lineCam;
        camParent = mainCam.transform.parent.transform;
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
        Global.Instance.playerList.Remove(this);
    }
    private bool active = true;
    public int teamID = 0;
    public override void OnNetworkSpawn()
    {
        teamID = System.Convert.ToInt32(OwnerClientId);
        Global.Instance.playerList.Add(this);
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
        playerFaction = Global.Instance.factions[teamID];
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
        mainCam.orthographicSize -= Input.mouseScrollDelta.y * camScroll;
        mainCam.orthographicSize = Mathf.Clamp(mainCam.orthographicSize, 1, 10);
        lineCam.orthographicSize = mainCam.orthographicSize;
    }
    private void SelectedAttackMove()
    {
        foreach (SelectableEntity item in selectedEntities)
        {
            if (item.minionController != null) //minion
            {
                print("attack moving");
                item.minionController.SetAttackMoveDestination();
            }
        }
    }
    public enum ActionType
    {
        Move, AttackTarget, Harvest, Deposit, Garrison, BuildTarget
    }
    private ActionType actionType = ActionType.Move;
    private void SelectedSetDestination() //RTS entity right click behavior
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
                if (select.teamBehavior == SelectableEntity.TeamBehavior.OwnerTeam)
                {
                    if (select.net.OwnerClientId == OwnerClientId) //same team
                    {
                        if ((select.depositType == SelectableEntity.DepositType.Gold || select.depositType == SelectableEntity.DepositType.All)
                            && select.fullyBuilt) //if deposit point
                        {
                            actionType = ActionType.Deposit;
                        }
                        else if (!select.fullyBuilt) //if buildable and this is a builder
                        {
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
                        /*else if (select.type == SelectableEntity.EntityTypes.Portal)
                        {
                        }*/
                    }
                    else //enemy
                    { //try to target this enemy specifically
                        actionType = ActionType.AttackTarget;
                    }
                }
                else if (select.teamBehavior == SelectableEntity.TeamBehavior.FriendlyNeutral)
                {
                    if (select.type == SelectableEntity.EntityTypes.HarvestableStructure)
                    { //harvest target
                        actionType = ActionType.Harvest;
                        Debug.Log("HARVEST");
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
                        case ActionType.Deposit:
                            item.minionController.DepositTo(select);
                            break;
                        case ActionType.Garrison:
                            item.minionController.GarrisonInto(select);
                            break;
                        case ActionType.BuildTarget://try determining how many things need to be built in total, and grabbing closest ones
                            item.minionController.CommandBuildTarget(select);
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
    /*[ClientRpc]
    private void ReportOrderClientRpc(ActionType action, Vector3 position)
    {
        if (!IsServer)
        {
            //tell opponent about our order. once we receive their acknowledgement,
            //then activate our order
            Debug.Log("Received server's order, sending client acknowledgement.");
            SendAcknowledgementServerRpc(action, position);
        }
    }
    [ServerRpc(RequireOwnership = false)]
    private void SendAcknowledgementServerRpc(ActionType action, Vector3 position)
    {
        Debug.Log("Received client's acknowledgement, activating order NOW.");
        ActivateOrder(action, position);
    }
    private void ActivateOrder(ActionType action, Vector3 position)
    {
        SelectableEntity select = null;
        if (Physics.Raycast(position + new Vector3(0, 100, 0), Vector3.down, out RaycastHit hit, Mathf.Infinity))
        {
            if (hit.collider != null)
            {
                select = hit.collider.GetComponent<SelectableEntity>();
            }
        }
        if (select == null && action != ActionType.Move)
        {
            Debug.Log("entity expected but missing ...");
            action = ActionType.Move; //if for some reason it's missing default
        }
        foreach (SelectableEntity item in selectedEntities)
        {
            if (item.minionController != null) //minion
            {
                switch (action)
                {
                    case ActionType.Move:
                        item.minionController.MoveTo(position);
                        break;
                    case ActionType.AttackTarget:
                        item.minionController.AttackTarget(select);
                        break;
                    case ActionType.Harvest:
                        item.minionController.CommandHarvestTarget(select);
                        break;
                    case ActionType.Deposit:
                        item.minionController.DepositTo(select);
                        break;
                    case ActionType.Garrison:
                        item.minionController.GarrisonInto(select);
                        break;
                    case ActionType.BuildTarget:
                        item.minionController.CommandBuildTarget(select);
                        break;
                    default:
                        break;
                }
            }
            else
            {
                item.SetRally();
            }
        }
    }*/
    /*[ServerRpc]
    private void ReportOrderServerRpc() //runs on server
    { 
        Debug.Log("server receives order and sends acknowledgement");
        AcknowledgeOrderClientRpc();
    }
    [ClientRpc]
    private void AcknowledgeOrderClientRpc() //runs on clients
    { 
        Debug.Log("clients receive server acknowledgement and original client can run order");
    } */


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
    private void SelectAllAttackers()
    {
        DeselectAll();
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item != null && (item.type == SelectableEntity.EntityTypes.Melee || item.type == SelectableEntity.EntityTypes.Ranged))
            {
                if (item.teamBehavior == SelectableEntity.TeamBehavior.OwnerTeam && item.occupiedGarrison == null) //only select ungarrisoned
                {
                    TrySelectEntity(item);
                }
            }
        }
    }
    private void SelectAllProduction()
    {
        DeselectAll();
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item != null && item.type == SelectableEntity.EntityTypes.ProductionStructure)
            {
                if (item.teamBehavior == SelectableEntity.TeamBehavior.OwnerTeam)
                {
                    TrySelectEntity(item);
                }
            }
        }
    }
    private void SelectAllIdleBuilders()
    {
        DeselectAll();
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item != null && item.type == SelectableEntity.EntityTypes.Builder && item.minionController != null)
            {
                if (item.teamBehavior == SelectableEntity.TeamBehavior.OwnerTeam)
                {
                    switch (item.minionController.state)
                    {
                        case MinionController.State.Idle:
                        case MinionController.State.FindInteractable:
                            TrySelectEntity(item);
                            break;
                    }
                }
            }
        }
    }
    public bool IsTargetExplicitlyOnOurTeam(SelectableEntity target)
    {
        return target.teamBehavior == SelectableEntity.TeamBehavior.OwnerTeam && ownedEntities.Contains(target);
    }
    private void TrySelectEntity(SelectableEntity entity)
    {
        if (IsTargetExplicitlyOnOurTeam(entity) || entity.teamBehavior == SelectableEntity.TeamBehavior.FriendlyNeutral)
        {
            selectedEntities.Add(entity);
            entity.Select(true);
        }
        UpdateGUIFromSelections();
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
        /*#if UNITY_EDITOR //DEBUG COMMANDS
                if (Input.GetKeyDown(KeyCode.RightShift))
                {
                    GenericSpawnMinion(cursorWorldPosition, 0, this);
                }
                if (Input.GetKeyDown(KeyCode.RightAlt))
                {
                    GenericSpawnMinion(cursorWorldPosition, 2, this);
                }
                if (Input.GetKeyDown(KeyCode.RightControl))
                {
                    GenericSpawnMinion(cursorWorldPosition, 3, this);
                }
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    GenericSpawnMinion(cursorWorldPosition, 11, this);
                }
        #endif*/
    }
    private FactionBuilding buildingToPlace = null;
    void Update()
    {
        /*FogOfWarTeam fow = FogOfWarTeam.GetTeam((int)OwnerClientId);
        Debug.Log(fow.GetFogValue(cursorWorldPosition));*/
        if (!active) return;
        UpdatePlacement();
        UpdateGridVisual();
        DetectHotkeys();
        CameraMove();
        if (!MouseOverUI())
        {
            GetMouseWorldPosition();

            if (linkedState == LinkedState.PlacingEnd)
            {
                CalculateFillCost(startWallPosition, cursorWorldPosition, wallID);
            }
            if (Input.GetMouseButtonDown(0)) //left click
            {
                StartMousePosition = Input.mousePosition;
                ResizeSelection();
                Global.Instance.selectionRect.gameObject.SetActive(true);
                switch (mouseState)
                {
                    case MouseState.Waiting:
                        TryToSelectOne();
                        break;
                    case MouseState.ReadyToPlace:
                        PlaceBuilding(buildingToPlace);
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
                        SelectedSetDestination();
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
        TryReplaceFakeSpawn();
    }
    private void SelectWithinBounds() //rectangle select, finish drag select
    {
        RectTransform SelectionBox = Global.Instance.selectionRect;
        Bounds bounds = new(SelectionBox.anchoredPosition, SelectionBox.sizeDelta);
        //Debug.Log(bounds.size.magnitude);
        if (bounds.size.magnitude > 20f)
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
            int military = 0;
            int production = 0;
            int builder = 0;
            foreach (SelectableEntity item in evaluation)
            {
                switch (item.type)
                {
                    case SelectableEntity.EntityTypes.Melee:
                    case SelectableEntity.EntityTypes.Ranged:
                    case SelectableEntity.EntityTypes.Transport:
                        military++;
                        break;
                    case SelectableEntity.EntityTypes.ProductionStructure:
                        production++;
                        break;
                    case SelectableEntity.EntityTypes.Builder:
                        builder++;
                        break;
                    case SelectableEntity.EntityTypes.HarvestableStructure:
                        break;
                    case SelectableEntity.EntityTypes.DefensiveGarrison:
                        break;
                    case SelectableEntity.EntityTypes.Portal:
                        break;
                    case SelectableEntity.EntityTypes.ExtendableWall:
                        break;
                    default:
                        break;
                }
            }
            SelectableEntity.EntityTypes privileged1 = SelectableEntity.EntityTypes.Melee;
            SelectableEntity.EntityTypes privileged2 = SelectableEntity.EntityTypes.Ranged;
            SelectableEntity.EntityTypes privileged3 = SelectableEntity.EntityTypes.Transport;
            if (military > 0 || builder > 0) //these take precedent over production always
            {
                if (military < builder)
                {
                    privileged1 = SelectableEntity.EntityTypes.Builder;
                    privileged2 = SelectableEntity.EntityTypes.Builder;
                    privileged3 = SelectableEntity.EntityTypes.Builder;
                }
            }
            else
            {
                privileged1 = SelectableEntity.EntityTypes.ProductionStructure;
                privileged2 = SelectableEntity.EntityTypes.ProductionStructure;
                privileged3 = SelectableEntity.EntityTypes.ProductionStructure;
            }
            foreach (SelectableEntity item in evaluation)
            {
                if ((item.type == privileged1 || item.type == privileged2 || item.type == privileged3) && item.occupiedGarrison == null)
                {
                    selectedEntities.Add(item);
                    item.Select(true);
                }
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
    private Vector3 startWallPosition;
    private void PlaceBuilding(FactionBuilding building)
    {
        NormalPlaceBuilding(building);
        /*switch (linkedState)
        {
            case LinkedState.Waiting: 
                NormalPlaceBuilding(id);
                break;
            case LinkedState.PlacingStart:
                linkedState = LinkedState.PlacingEnd;
                startWallPosition = cursorWorldPosition;
                wallID = id;
                Destroy(followCursorObject);
                break;
            case LinkedState.PlacingEnd:
                int cost = CalculateFillCost(startWallPosition, cursorWorldPosition, id);
                if (gold >= cost)
                {
                    gold -= cost;
                    //GenericSpawnMinion(cursorWorldPosition, id); //followCursorObject.transform.position
                    WallFill(id);
                    StopPlacingBuilding();
                }
                break;
            default:
                break;
        }*/
    }
    private void NormalPlaceBuilding(FactionBuilding building)
    {
        if (gold < building.goldCost) return;
        gold -= building.goldCost; 
        GenericSpawnMinion(cursorWorldPosition, building, this);
        SelectableEntity last = Global.Instance.localPlayer.ownedEntities.Last();
        TellSelectedToBuild(last);
        //temporary: later re-implement two-part buildings and holding shift to continue placing
        StopPlacingBuilding();
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
                        if (item.type == SelectableEntity.EntityTypes.Portal)
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
            }
            else //if there's a linked building, we continue placing buildings so the player can place the next part of the building.
            {
                buildingPlacingID = (byte)building.linkedID;
                placingLinkedBuilding = true;

                if (placingPortal)
                {
                    foreach (SelectableEntity item in ownedEntities)
                    {
                        if (item.type == SelectableEntity.EntityTypes.Portal)
                        {
                            startPortal = item.GetComponent<Portal>();
                            break;
                        }
                    }
                }
            }
        }*/
    }
    public List<Vector3> predictedWallPositions = new();
    public List<bool> predictedWallPositionsShouldBePlaced = new();
    public List<GameObject> wallGhosts = new();
    private int CalculateFillCost(Vector3 start, Vector3 end, byte id) //fill between start and end
    {
        /*FactionUnit fac = _faction.entities[id];
        int cost = fac.goldCost;

        float distance = Vector3.Distance(start, end);
        //greater distance means more walls
        predictedWallPositions.Clear();
        predictedWallPositionsShouldBePlaced.Clear();
        foreach (GameObject item in wallGhosts)
        {
            if (item != null)
            {
                Destroy(item);
            }
        }
        wallGhosts.Clear();
        FogOfWarTeam fow = FogOfWarTeam.GetTeam((int)OwnerClientId);
        float halfExtents = 0.1f;
        if (distance > 0)
        {
            for (float i = 0; i <= distance + 0.5f; i += 0.5f)
            {
                Vector3 spot = Vector3.Lerp(start, end, i / distance);
                Vector3 mod = AlignToQuarterGrid(spot);
                Debug.DrawLine(spot, spot + new Vector3(0, 1, 0), Color.red);
                Debug.DrawLine(mod, mod + new Vector3(0, 1, 0));
                if (!predictedWallPositions.Any(i => i == mod)) // && mod != cursorWorldPosition
                {
                    if (Physics.CheckBox(mod, new Vector3(halfExtents, halfExtents, halfExtents), Quaternion.Euler(0, 180, 0), entityLayer, QueryTriggerInteraction.Collide) ||
                        (fow.GetFogValue(mod) > 0.1f * 255)) //blocked
                    {
                        predictedWallPositionsShouldBePlaced.Add(false);
                    }
                    else
                    {
                        predictedWallPositionsShouldBePlaced.Add(true);
                    }
                    predictedWallPositions.Add(mod);
                }
            }
        }
        else
        {
            if (Physics.CheckBox(start, new Vector3(halfExtents, halfExtents, halfExtents), Quaternion.Euler(0, 180, 0), entityLayer, QueryTriggerInteraction.Collide) ||
                (fow.GetFogValue(start) > 0.1f * 255)) //blocked
            {
                predictedWallPositionsShouldBePlaced.Add(false);
            }
            else
            {
                predictedWallPositionsShouldBePlaced.Add(true);
            }
            predictedWallPositions.Add(start);
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
                GameObject ghost = PlaceWallGhost(pos, id, true);
                wallGhosts.Add(ghost);
            }
        }
        else
        {
            for (int i = 0; i < predictedWallPositions.Count; i++)
            {
                Vector3 pos = predictedWallPositions[i];
                bool shouldBePlaced = predictedWallPositionsShouldBePlaced[i];
                GameObject ghost = PlaceWallGhost(pos, id, !shouldBePlaced);
                wallGhosts.Add(ghost);
            }
        }
        Debug.Log(realCost);
        return realCost;*/
        return 0;
    }
    private GameObject PlaceWallGhost(Vector3 pos, byte id, bool blocked = false)
    {
        /*GameObject build = _faction.entities[id].prefabToSpawn.gameObject;
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
        return spawn;*/
        return null;
    }
    private void WallFill(byte id)
    {
        /*foreach (GameObject item in wallGhosts)
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
                GenericSpawnMinion(pos, id, this);
            }
        }
        predictedWallPositions.Clear();
        predictedWallPositionsShouldBePlaced.Clear();*/
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
    private byte wallID = 0;
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
    public void UpdatePlacement()
    {
        placementBlocked = Physics.CheckBox(cursorWorldPosition, buildOffset, Quaternion.identity, entityLayer, QueryTriggerInteraction.Ignore);

        FogOfWarTeam fow = FogOfWarTeam.GetTeam((int)OwnerClientId);
        if (fow.GetFogValue(cursorWorldPosition) > 0.1f * 255)
        {
            placementBlocked = true;
        }

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
            //cursorWorldPosition = grid.CellToWorld(_gridPosition) + _offset;
            cursorWorldPosition = grid.CellToWorld(_gridPosition) + buildOffset;
            Debug.DrawRay(hit.point, transform.up, Color.red, 1);
            Debug.DrawRay(cursorWorldPosition, transform.up, Color.green, 1);
            //print(hit.point + " " + cursorWorldPosition);
            if (followCursorObject != null)
            {
                followCursorObject.transform.position = new Vector3(cursorWorldPosition.x, hit.point.y, cursorWorldPosition.z);// + new Vector3(0, 5, 0);
            }
        }
    }
    #region SpawnMinion

    private List<SelectableEntity> fakeSpawns = new();
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
            byte id = 0;
            for (int i = 0; i < playerFaction.spawnableEntities.Count; i++)
            {
                if (playerFaction.spawnableEntities[i].productionName == unit.productionName)
                {
                    id = (byte)i;
                    break;
                }
            }
            Debug.Log("ID: " + id);
            RequestSpawnMinionServerRpc(spawnPosition, id, ownerID, spawner);
        }
        UpdateButtons();
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
            GameObject minion = Instantiate(unit.prefabToSpawn.gameObject, spawnPosition, Quaternion.identity); //Quaternion.Euler(0, 180, 0)
            SelectableEntity select = null;
            if (minion != null)
            {
                select = minion.GetComponent<SelectableEntity>();
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
                    //use client rpc to send this ID to client
                    ClientRpcParams clientRpcParams = new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams
                        {
                            TargetClientIds = new ulong[] { clientID }
                        }
                    };
                    SendReferenceToSpawnedMinionClientRpc((ushort)select.NetworkObjectId, spawner, clientRpcParams);
                }
            }
        }
    }
    private List<ushort> fakeSpawnsReadyForReplacement = new();
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
    private void TryReplaceFakeSpawn()
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
                        select.minionController.state = fakeController.state;
                        select.minionController.state = MinionController.State.Idle;
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
    public void UpdateButtons()
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
        UpdateBuildQueue();
    }
    private void UpdateGUIBasedOnSelectedUnitCount()
    {
        if (Global.Instance.selectedParent != null && Global.Instance.resourcesParent != null && Global.Instance.resourceText != null
            && Global.Instance.nameText != null && Global.Instance.descText != null && Global.Instance.singleUnitInfoParent != null)
        {
            if (selectedEntities.Count <= 0)
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
    private List<FactionBuilding> availableConstructionOptions = new();
    private List<FactionAbility> availableAbilities = new();
    private List<FactionUnit> availableUnitSpawns = new();
    private void UpdateButtonsFromSelectedUnits() //update button abilities
    { 
        availableConstructionOptions.Clear();
        availableAbilities.Clear();
        availableUnitSpawns.Clear();
        if (selectedEntities.Count > 0) //at least one unit selected
        {
            //show gui elements based on unit type selected
            foreach (SelectableEntity entity in selectedEntities)
            {
                if (!entity.fullyBuilt || !entity.net.IsSpawned || entity == null) //if not built or spawned, skip
                {
                    continue;
                }
                //get abilities
                foreach (FactionAbility abilityOption in entity.usableAbilities)
                {
                    if (!availableAbilities.Contains(abilityOption)) availableAbilities.Add(abilityOption);
                }
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
                button.interactable = true;
                text.text = ability.abilityName;// + ": " + ability.goldCost + "g"; 
                button.onClick.AddListener(delegate { UseAbility(ability); });
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
        return (entity != null && entity.fullyBuilt && entity.net.IsSpawned && entity.alive && entity.usableAbilities.Contains(ability)
            && entity.AbilityOffCooldown(ability));
    }
    public void UpdateBuildQueue()
    {
        Global.Instance.queueParent.gameObject.SetActive(false);
        if (selectedEntities.Count != 1) return; //only works with a single unit for now

        SelectableEntity select = selectedEntities[0];
        //only works if is production structure, fully built, and spawned
        if (select.type != SelectableEntity.EntityTypes.ProductionStructure || !select.fullyBuilt || !select.net.IsSpawned) return;

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
        text.text = fac.productionName + ": " + fac.timeCost + "s";
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(delegate { DequeueProductionOrder(i); });
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
        FactionUnit newUnit = new()
        {
            productionName = unit.prefabToSpawn.name,
            timeCost = unit.timeCost,
            prefabToSpawn = unit.prefabToSpawn,
            goldCost = unit.goldCost,
            //buildID = id
        };

        int cost = newUnit.goldCost;
        //try to spawn from all selected buildings if possible 
        foreach (SelectableEntity select in selectedEntities)
        {
            if (gold < cost || !select.net.IsSpawned || !select.fullyBuilt || !TargetCanSpawnThisEntity(select, newUnit)) break;
            //if requirements fulfilled
            gold -= cost;
            select.buildQueue.Add(newUnit);
        }
        UpdateBuildQueue();
    }
    private bool TargetCanSpawnThisEntity(SelectableEntity target, FactionEntity entity)
    {
        for (int i = 0; i < target.spawnableUnits.Length; i++)
        {
            if (target.spawnableUnits[i].productionName == entity.productionName) return true; //if ability is in the used abilities list, then we still need to wait  
        }
        return false;
    }
    private Vector3 buildOffset = Vector3.zero;
    /// <summary>
    /// Create building ghost showing where building will be placed
    /// </summary>
    /// <param name="id"></param>
    private void HoverBuild(FactionBuilding building)
    {
        mouseState = MouseState.ReadyToPlace;
        placementBlocked = false;
        //buildingPlacingID = id;
        buildingToPlace = building;
        //GameObject build = _faction.entities[id].prefabToSpawn.gameObject;
        GameObject build = building.prefabToSpawn;
        GameObject spawn = Instantiate(build, Vector3.zero, Quaternion.Euler(0, 180, 0)); //spawn ghost
        SelectableEntity entity = spawn.GetComponent<SelectableEntity>();
        buildOffset = entity.buildOffset;
        if (entity.type == SelectableEntity.EntityTypes.Portal)
        {
            placingPortal = true;
        }
        else if (entity.type == SelectableEntity.EntityTypes.ExtendableWall)
        {
            linkedState = LinkedState.PlacingStart;
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
    public bool placingPortal = false;
    public List<byte> indices;
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
                    SelectAllSameTypeExcludingInGarrisons(entity.type);
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
                if (item.passenger.selectableEntity != null)
                {
                    TrySelectEntity(item.passenger.selectableEntity);
                }
            }
        }
    }
    private void SelectAllSameTypeExcludingInGarrisons(SelectableEntity.EntityTypes type)
    {
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item.type == type && item.occupiedGarrison == null)
            {
                item.Select(true);
                selectedEntities.Add(item);
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
            if (select.teamBehavior == SelectableEntity.TeamBehavior.FriendlyNeutral)
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
