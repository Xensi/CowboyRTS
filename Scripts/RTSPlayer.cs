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

public class RTSPlayer : NetworkBehaviour
{
    public NetworkVariable<bool> inTheGame = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public List<SelectableEntity> keystoneUnits = new();
    public int gold = 100;
    public Camera cam;
    [SerializeField] private Grid grid;
    private Vector3Int _gridPosition;
    public List<SelectableEntity> ownedEntities; //must be serialized or public
    public List<SelectableEntity> selectedEntities;
    public List<SelectableEntity> enemyEntities;

    [SerializeField] private FactionScriptableObject _faction;
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
    public void LoseGame()
    {
        inTheGame.Value = false;
    }
    void Start()
    {
        groundLayer = LayerMask.GetMask("Ground");
        entityLayer = LayerMask.GetMask("Entity", "Obstacle");
        gameLayer = LayerMask.GetMask("Entity", "Obstacle", "Ground");
        placementGhost = LayerMask.GetMask("PlacementGhost");
        _offset = new Vector3(0.5f, 0, .5f);
        //_offset = new Vector3(0, 0, 0);
        cam = Camera.main;
        camParent = cam.transform.parent.transform;
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
    public override void OnNetworkSpawn()
    {
        Global.Instance.playerList.Add(this);
        if (IsOwner) //spawn initial minions/buildings  
        {
            inTheGame.Value = true;
            Global.Instance.localPlayer = this;
            //SimpleSpawnMinion(Vector3.zero);
            int id = System.Convert.ToInt32(OwnerClientId);
            Vector3 spawn;
            if (id < Global.Instance.playerSpawn.Count)
            {
                spawn = Global.Instance.playerSpawn[id].position;
            }
            else
            {
                spawn = new Vector3(Random.Range(-9, 9), 0, Random.Range(-9, 9));
            }

            GenericSpawnMinion(spawn, starterUnitID, this);

            VolumeProfile profile = Global.Instance.fogVolume.sharedProfile;
            if (profile != null && profile.TryGet(out FogOfWarURP fow))
            {
                fow.team.value = id;
            }
        }
        else
        {
            active = false;
        }
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
        cam.orthographicSize -= Input.mouseScrollDelta.y * camScroll;

        cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, 1, 10);
    }
    private void SelectedAttackMove()
    {
        foreach (SelectableEntity item in selectedEntities)
        {
            if (item.minionController != null) //minion
            {
                item.minionController.SetAttackMoveDestination();
            }
        }
    }
    public enum ActionType
    {
        Move, AttackTarget, Harvest, Deposit, Garrison, BuildTarget
    }
    private ActionType actionType = ActionType.Move;
    private void SelectedSetDestination()
    {
        //when player right clicks, get position on map
        //tell other clients that this happened
        Vector3 clickedPosition;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
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
            foreach (SelectableEntity item in selectedEntities)
            {
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
                            Debug.Log("Commanding harvest");
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


    void Update()
    {
        if (!active) return;
        /*FogOfWarTeam fow = FogOfWarTeam.GetTeam((int)OwnerClientId);
        Debug.Log(fow.GetFogValue(cursorWorldPosition));*/
        UpdatePlacement();
        if (Global.Instance.gridVisual != null)
        {
            Global.Instance.gridVisual.SetActive(mouseState == MouseState.ReadyToPlace);
        }
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
        CameraMove();
        if (!MouseOverUI())
        {
            GetMouseWorldPosition();

#if UNITY_EDITOR
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
#endif
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
                        PlaceBuilding(buildingPlacingID);
                        if (!placementBlocked)
                        {

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
                    if (UnitIsInSelectionBox(cam.WorldToScreenPoint(item.transform.position), bounds))
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


            /*foreach (SelectableEntity item in ownedEntities)
            {
                if (item != null)
                { 
                    if (UnitIsInSelectionBox(cam.WorldToScreenPoint(item.transform.position), bounds))
                    {
                        selectedEntities.Add(item);
                        item.Select(true);
                    }
                    else
                    {
                        item.Select(false);
                    }
                }
            }*/
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
    private void PlaceBuilding(byte id = 0)
    { 
        switch (linkedState)
        {
            case LinkedState.Waiting:
                //PlaceBuildingGuide(id);
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
        }
    } 
    private void NormalPlaceBuilding(byte id)
    {
        FactionEntityClass fac = _faction.entities[id];
        if (gold >= fac.goldCost)
        {
            gold -= fac.goldCost;
            Debug.Log("placing at " + cursorWorldPosition);
            GenericSpawnMinion(cursorWorldPosition, id, this); //followCursorObject.transform.position

            SelectableEntity last = Global.Instance.localPlayer.ownedEntities.Last();
            TellSelectedToBuild(last);
            if (Input.GetKey(KeyCode.LeftShift) && gold >= fac.goldCost)
            {
                //continue placing buildings
            }
            else if (!Input.GetKey(KeyCode.LeftShift) || gold < fac.goldCost) //if not holding shift or out of money for this building
            {
                if (fac.linkedID == -1) //if no linked building, stop after placing building
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
                    buildingPlacingID = (byte)fac.linkedID;
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
            }
        }
    }
    public List<Vector3> predictedWallPositions = new();
    public List<bool> predictedWallPositionsShouldBePlaced = new();
    public List<GameObject> wallGhosts = new();
    private int CalculateFillCost(Vector3 start, Vector3 end, byte id) //fill between start and end
    {
        FactionEntityClass fac = _faction.entities[id];
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
        if (distance > 0)
        {
            for (int i = 0; i <= distance; i++)
            {
                Vector3 spot = Vector3.Lerp(start, end, i / distance);
                Vector3 mod = new Vector3(AlignToGrid(spot.x), 0, AlignToGrid(spot.z));
                Debug.DrawLine(mod, mod + new Vector3(0, 1, 0));
                if (!predictedWallPositions.Any(i => i == mod)) // && mod != cursorWorldPosition
                {
                    if (Physics.CheckBox(mod, new Vector3(0.4f, 0.4f, 0.4f), Quaternion.Euler(0, 180, 0), entityLayer, QueryTriggerInteraction.Ignore) ||
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
            if (Physics.CheckBox(start, new Vector3(0.4f, 0.4f, 0.4f), Quaternion.Euler(0, 180, 0), entityLayer, QueryTriggerInteraction.Ignore) ||
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
        return realCost;
    }
    private GameObject PlaceWallGhost(Vector3 pos, byte id, bool blocked = false)
    {
        GameObject build = _faction.entities[id].prefabToSpawn.gameObject;
        GameObject spawn = Instantiate(build, pos, Quaternion.Euler(0, 180, 0)); //spawn locally  
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
    private void WallFill(byte id)
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
                GenericSpawnMinion(pos, id, this);
            }
        }
        predictedWallPositions.Clear();
        predictedWallPositionsShouldBePlaced.Clear();
    } 
    private float AlignToGrid(float input)
    {
        //ex 1.7
        float ceiling = Mathf.Ceil(input); //2
        float floor = Mathf.Floor(input); //1
        float middle = floor + 0.5f;

        //float maxDiff = 0.25f;
        return middle;
        /*
                if (Mathf.Abs(ceiling - input) <= maxDiff) //if diff between ceiling and input is small
                {
                    return ceiling;
                }
                else if (Mathf.Abs(floor - input) <= maxDiff)
                {
                    return floor;
                }
                else
                {
                    return middle;
                }*/

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
    private void OnDrawGizmos()
    {
        if (IsOwner)
        {
            Gizmos.color = new Color(1, 0, 0, 0.5f);
            Gizmos.DrawCube(cursorWorldPosition, new Vector3(.8f, .8f, .8f));
            /*Gizmos.DrawWireSphere(_mousePosition, .1f);
            Gizmos.DrawSphere(worldPosition, .1f);*/
        }
    }
    public void UpdatePlacement()
    {
        placementBlocked = Physics.CheckBox(cursorWorldPosition, new Vector3(0.4f, 0.4f, 0.4f), Quaternion.identity, entityLayer, QueryTriggerInteraction.Ignore);

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
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, Mathf.Infinity, groundLayer))
        {
            _mousePosition = hit.point;
            _gridPosition = grid.WorldToCell(_mousePosition);
            cursorWorldPosition = grid.CellToWorld(_gridPosition) + _offset;
            if (followCursorObject != null)
            {
                followCursorObject.transform.position = cursorWorldPosition;
            }
        }
    }
    #region SpawnMinion

    private List<SelectableEntity> fakeSpawns = new(); 
    private void FakeClientSideSpawn(Vector2 spawn, byte minionID)
    {
        Vector3 spawnPosition = new(spawn.x, 0, spawn.y); //get spawn position 
        FactionEntityClass fac = _faction.entities[minionID]; //get information about minion based on ID
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
        }
        //
        //minion.name = "FAKE SPAWN";

        //Get components
    }
    /// <summary>
    /// Tell the server to spawn in a minion at a position.
    /// </summary> 
    public void GenericSpawnMinion(Vector3 spawnPos, byte minionID, NetworkBehaviourReference spawner)
    {
        Vector2 spawn = new Vector2(spawnPos.x, spawnPos.z);
        if (IsServer)
        {
            ServerSpawnMinion(spawn, minionID, (byte)OwnerClientId, spawner);
        }
        else //clients ask server to spawn it
        {
            FakeClientSideSpawn(spawn, minionID);
            FactionEntityClass fac = _faction.entities[minionID]; //get information about minion based on ID
            Debug.Log("CLIENT: sending request to spawn " + fac.productionName);
            //SpawnMinionServerRpc(spawn, minionID);
            RequestSpawnMinionServerRpc(spawn, minionID, (byte)OwnerClientId, spawner);
        }
        UpdateButtons();
    }
    [ServerRpc]
    private void RequestSpawnMinionServerRpc(Vector2 spawn, byte minionID, byte clientID, NetworkBehaviourReference spawner) //ok to use byte because 0-244
    {
        FactionEntityClass fac = _faction.entities[minionID]; //get information about minion based on ID
        Debug.Log("SERVER: received request to spawn " + fac.productionName);
        ServerSpawnMinion(spawn, minionID, clientID, spawner);
    }
    private void ServerSpawnMinion(Vector2 spawn, byte minionID, byte clientID, NetworkBehaviourReference spawner)
    {
        if (!IsServer) return;

        Vector3 spawnPosition = new(spawn.x, 0, spawn.y); //get spawn position

        FactionEntityClass fac = _faction.entities[minionID]; //get information about minion based on ID
        if (fac != null && fac.prefabToSpawn != null)
        {
            Debug.Log("SERVER: spawning " + fac.productionName);
            GameObject minion = Instantiate(fac.prefabToSpawn.gameObject, spawnPosition, Quaternion.identity); //Quaternion.Euler(0, 180, 0)
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
    /*private void InternalSpawnMinion(Vector2 spawn, byte minionID, ServerRpcParams serverRpcParams = default)
    {
        *//*Vector3 spawnPosition = new(spawn.x, 0, spawn.y); //get spawn position

        FactionEntityClass fac = _faction.entities[minionID]; //get information about minion based on ID
        GameObject minion = Instantiate(fac.prefabToSpawn, spawnPosition, Quaternion.Euler(0, 180, 0)); //spawn locally
        //Get components
        SelectableEntity select = minion.GetComponent<SelectableEntity>();   

        //Grant ownership to client that called this
        var clientId = serverRpcParams.Receive.SenderClientId;
        if (NetworkManager.ConnectedClients.ContainsKey(clientId))
        {
            Debug.Log("Spawning" + minion.name);  
            select.net.SpawnWithOwnership(clientId);
            //use client rpc to send this ID to client
            ClientRpcParams clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { clientId }
                }
            };
            SendReferenceToSpawnedMinionClientRpc(select.NetworkObjectId, clientRpcParams);
        } 
        return select;*//*
    }*/


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
        for (int i = 0; i < indices.Count; i++)
        {
            Button button = Global.Instance.productionButtons[i];
            FactionEntityClass fac = _faction.entities[indices[i]];
            button.interactable = gold >= fac.goldCost;
        }
    }
    public void UpdateGUIFromSelections()
    {
        UpdateIndices();
        UpdateBasedOnSelectedUnitCount();
        UpdateButtonsFromSelectedUnits();
        UpdateBuildQueue();
    }
    private void UpdateIndices()
    {
        indices.Clear();

        if (selectedEntities.Count > 0) //at least one unit selected
        {
            //show gui elements based on unit type selected
            foreach (SelectableEntity item in selectedEntities)
            {
                if (!item.fullyBuilt || !item.net.IsSpawned) //if not built or spawned, skip
                {
                    continue;
                }
                foreach (byte num in item.builderEntityIndices)
                {
                    if (!indices.Contains(num))
                    {
                        indices.Add(num);
                    }
                }
            }
        }
    }

    private void UpdateBasedOnSelectedUnitCount()
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
    private void UpdateButtonsFromSelectedUnits()
    {
        byte i = 0;
        int cap = Mathf.Clamp(indices.Count, 0, Global.Instance.productionButtons.Count);
        //enable a button for each indices
        for (; i < cap; i++)
        {
            Button button = Global.Instance.productionButtons[i];
            button.gameObject.SetActive(true);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>();
            FactionEntityClass fac = _faction.entities[indices[i]];
            text.text = fac.productionName + ": " + fac.goldCost + "g";
            byte j = indices[i];
            //Debug.Log(j);
            button.onClick.RemoveAllListeners();
            if (_faction.entities[indices[i]].needsConstructing)
            {
                button.onClick.AddListener(delegate { HoverBuildWithID(j); });
            }
            else
            {
                button.onClick.AddListener(delegate { QueueBuildingSpawn(j); });
            }
            button.interactable = gold >= fac.goldCost;
        }
        for (; i < Global.Instance.productionButtons.Count; i++)
        {
            Global.Instance.productionButtons[i].gameObject.SetActive(false);
        }
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
        FactionEntityClass fac = select.buildQueue[i];
        text.text = fac.productionName + ": " + fac.timeCost + "s";
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(delegate { DequeueProductionOrder(i); });
    }
    public void DequeueProductionOrder(int index = 0) //on click remove thing from queue and refund gold
    { 
        if (selectedEntities.Count == 1)
        {
            SelectableEntity select = selectedEntities[0]; 
            FactionEntityClass fac = select.buildQueue[index]; 
            gold += fac.goldCost; 
            select.buildQueue.RemoveAt(index);
            UpdateGUIFromSelections();
        }
    }
    private void QueueBuildingSpawn(byte id = 0)
    {
        //necessary to create new so we don't accidentally affect the files
        FactionEntityClass newFac = new()
        {
            productionName = _faction.entities[id].prefabToSpawn.name,
            timeCost = _faction.entities[id].timeCost,
            prefabToSpawn = _faction.entities[id].prefabToSpawn,
            goldCost = _faction.entities[id].goldCost,
            buildID = id
        };

        int cost = newFac.goldCost;
        //try to spawn from all selected buildings if possible 
        foreach (SelectableEntity select in selectedEntities)
        { 
            if (gold < cost || !select.net.IsSpawned || !select.fullyBuilt || !select.builderEntityIndices.Contains(id)) break;
            //if requirements fulfilled
            gold -= cost;
            select.buildQueue.Add(newFac); 
        }
        UpdateBuildQueue();
    }
    private void HoverBuildWithID(byte id = 0)
    {
        mouseState = MouseState.ReadyToPlace;
        placementBlocked = false;
        buildingPlacingID = id;
        GameObject build = _faction.entities[id].prefabToSpawn.gameObject;
        GameObject spawn = Instantiate(build, Vector3.zero, Quaternion.Euler(0, 180, 0)); //spawn locally  
        SelectableEntity entity = spawn.GetComponent<SelectableEntity>();
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
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
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
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
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
