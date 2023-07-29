using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using UnityEngine.EventSystems;// Required when using Event data.
using TMPro;

public class RTSPlayer : NetworkBehaviour
{
    public int gold = 100;
    public Camera cam;   
    [SerializeField] private Grid grid;
    private Vector3Int _gridPosition;
    public List<SelectableEntity> ownedEntities; //must be serialized or public
    public List<SelectableEntity> selectedEntities;

    [SerializeField] private FactionScriptableObject _faction;
    [SerializeField] private int _entitiesIndex = 0; //used to pick a prefab from faction list
    private Vector3 _mousePosition;
    private Vector3 _offset;
    public Vector3 worldPosition;
    bool _doubleSelect = false; 
    public enum BuildStates
    {
        Waiting,
        ReadyToPlace
    }
    public BuildStates buildState = BuildStates.Waiting;
    LayerMask groundLayer;
    void Start()
    {
        groundLayer = LayerMask.GetMask("Ground");
        _offset = new Vector3(0.5f, 0, .5f);
        cam = Camera.main;
        camParent = cam.transform.parent.transform;
        Vector3 spawn = Global.Instance.playerSpawn[System.Convert.ToInt32(OwnerClientId)].position;
        camParent.position = new Vector3(spawn.x, camParent.position.y, spawn.z);
    }
    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        { 
            enabled = false;
        }
        else
        { //spawn initial minions/buildings 
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
                spawn = new Vector3(Random.Range(-9, 9),0, Random.Range(-9, 9));
            }

            SimpleSpawnMinion(spawn, 0);
        }
    }
    private Transform camParent;
    private bool MouseOverUI()
    {
        return EventSystem.current.IsPointerOverGameObject();
    }
    private float camSpeed = 10;
    private void CameraMove()
    {
        Vector3 motion = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
        camParent.transform.Translate(motion * camSpeed * Time.deltaTime);
    } 
    private void SelectedAttackMove()
    { 
        foreach (SelectableEntity item in selectedEntities)
        {
            if (item.controller != null) //minion
            {
                item.controller.SetDestinationRaycast(true); 
            } 
        }
    }
    private void SelectedSetDestination()
    {
        foreach (SelectableEntity item in selectedEntities)
        {
            if (item.controller != null) //minion
            {
                item.controller.SetDestinationRaycast();
            }
            else //structure
            {
                item.SetRally();
            }
        }
    }
    private void SelectAllAttackers()
    {
        DeselectAll();
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item != null && item.type != SelectableEntity.EntityTypes.ProductionStructure && item.type != SelectableEntity.EntityTypes.Builder)
            { 
                if (item.teamBehavior == SelectableEntity.TeamBehavior.OwnerTeam)
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
            if (item != null && item.type == SelectableEntity.EntityTypes.Builder && item.controller != null && item.controller.buildTarget == null)
            {
                if (item.teamBehavior == SelectableEntity.TeamBehavior.OwnerTeam)
                {
                    TrySelectEntity(item);
                }
            }
        }
    }
    private void TrySelectEntity(SelectableEntity entity)
    {
        if (entity.teamBehavior == SelectableEntity.TeamBehavior.OwnerTeam && (ownedEntities.Contains(entity)) || entity.teamBehavior == SelectableEntity.TeamBehavior.FriendlyNeutral)
        {
            selectedEntities.Add(entity);
            entity.Select(true);
        }
        UpdateGUIFromSelections();
    }


    void Update()
    {
        if (Global.Instance.gridVisual != null)
        { 
            Global.Instance.gridVisual.SetActive(buildState == BuildStates.ReadyToPlace);
        }
        if (Input.GetKey(KeyCode.Q))
        {
            SelectAllAttackers();
        }
        if (Input.GetKey(KeyCode.E))
        {
            SelectAllProduction();
        }
        if (Input.GetKey(KeyCode.B))
        {
            SelectAllIdleBuilders();
        }
        CameraMove();
        if (!MouseOverUI()) 
        {
            GetMouseWorldPosition();

#if UNITY_EDITOR
            if (Input.GetKey(KeyCode.RightShift))
            {
                SimpleSpawnMinion(worldPosition, 0);
            }
            if (Input.GetKey(KeyCode.RightAlt))
            {
                SimpleSpawnMinion(worldPosition, 2);
            }
#endif
            if (Input.GetMouseButtonDown(0))
            {
                StartMousePosition = Input.mousePosition;
                ResizeSelection();
                Global.Instance.selectionRect.gameObject.SetActive(true);

                if (buildState == BuildStates.ReadyToPlace && !placementBlocked)
                {
                    PlaceBuilding(buildingPlacingID);
                }
                else
                {
                    TryToSelectOne();
                }
            }
            if (Input.GetMouseButtonDown(1))
            {
                SelectedSetDestination();
            }
            if (Input.GetMouseButtonDown(2))
            {
                SelectedAttackMove();
            }


            if (Input.GetMouseButtonDown(1))
            {
                if (buildState == BuildStates.ReadyToPlace)
                {
                    StopPlacingBuilding();
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
    }
    private bool finishedSelection = false;
    private void SelectWithinBounds()
    {
        RectTransform SelectionBox = Global.Instance.selectionRect;
        Bounds bounds = new Bounds(SelectionBox.anchoredPosition, SelectionBox.sizeDelta);
        //Debug.Log(bounds.size.magnitude);
        if (bounds.size.magnitude > 20f)
        {
            if (!Input.GetKey(KeyCode.LeftShift)) //deselect all if not pressing shift
            {
                selectedEntities.Clear();
            }
            foreach (SelectableEntity item in ownedEntities)
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
    Vector2 StartMousePosition;
    private void ResizeSelection()
    {
        finishedSelection = false;
        RectTransform SelectionBox = Global.Instance.selectionRect;
        float width = Input.mousePosition.x - StartMousePosition.x;
        float height = Input.mousePosition.y - StartMousePosition.y;

        SelectionBox.anchoredPosition = StartMousePosition + new Vector2(width / 2, height / 2);
        SelectionBox.sizeDelta = new Vector2(Mathf.Abs(width), Mathf.Abs(height));
    } 
    private void PlaceBuilding(byte id = 0)
    {
        FactionEntityClass fac = _faction.entities[id];
        if (gold >= fac.goldCost)
        { 
            gold -= fac.goldCost;
            SimpleSpawnMinion(followCursorObject.transform.position, id);
            if (!Input.GetKey(KeyCode.LeftShift)) //if holding shift, continue placing buildings
            {

                StopPlacingBuilding();
            }
            //if this is a wall, we should connect it to any adjacent nodes

            //find adjacent nodes

            //place walls between this and those nodes
        }
    }  
    private void StopPlacingBuilding()
    {
        Destroy(followCursorObject);
        followCursorObject = null; 
        buildState = BuildStates.Waiting;

    }
    public void UpdatePlacement()
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
    public bool placementBlocked = false;
    private GameObject followCursorObject;
    private byte buildingPlacingID = 0;
    private MeshRenderer[] meshes;
    private void GetMouseWorldPosition()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity, groundLayer))
        {
            _mousePosition = hit.point;
            _gridPosition = grid.WorldToCell(_mousePosition);
            worldPosition = grid.CellToWorld(_gridPosition) + _offset;
            if (followCursorObject != null)
            {
                followCursorObject.transform.position = worldPosition;
            }
        }
    }
    public SelectableEntity lastSpawnedEntity;
    #region SpawnMinion
    /// <summary>
    /// Tell the server to spawn in a minion at a position.
    /// </summary> 
    public void SimpleSpawnMinion(Vector3 spawnPos, byte minionID = 0, bool useRally = false, Vector3 rallyPos = default(Vector3))
    {
        sbyte xSpawn = (sbyte)spawnPos.x;
        sbyte zSpawn = (sbyte)spawnPos.z;
        sbyte xRally = (sbyte)rallyPos.x;
        sbyte zRally = (sbyte)rallyPos.z;


        if (useRally) {
            SpawnMinionRallyServerRpc(xSpawn, zSpawn, minionID, xRally, zRally);
            /*if (lastSpawnedEntity.controller != null)
            {
                lastSpawnedEntity.controller.SetDestinationToPos(rallyPos);
            }*/
        }
        else
        { 
            SpawnMinionServerRpc(xSpawn, zSpawn, minionID);
        }

        //tell this minion to go to rally point if there is one

        if (useRally)
        {
        }
        //tell minion to build things

        UpdateButtons();
        //
        /*if (IsServer)
        {
            SpawnMinion(pos, id, rally); 
        }
        else
        {
            Vector2 pos2D = new Vector2(pos.x, pos.z);
            Vector2 rally2D = new Vector2(rally.x, rally.z);
            SpawnMinionServerRPC(pos2D, rally2D, id);
        }*/
    }

    private SelectableEntity InternalSpawnMinion(sbyte xSpawn, sbyte zSpawn, byte minionID, ServerRpcParams serverRpcParams = default)
    {
        FactionEntityClass fac = _faction.entities[minionID]; //get information about minion based on ID
        Vector3 spawnPosition = new Vector3(xSpawn, 0, zSpawn); //get spawn position
        spawnPosition += _offset;

        GameObject minion = Instantiate(fac.prefabToSpawn, spawnPosition, Quaternion.Euler(0, 180, 0)); //spawn locally
        //Get components
        SelectableEntity select = minion.GetComponent<SelectableEntity>();   

        //Grant ownership to client that called this
        var clientId = serverRpcParams.Receive.SenderClientId;
        if (NetworkManager.ConnectedClients.ContainsKey(clientId))
        {
            select.net.SpawnWithOwnership(clientId); 
        } 
        return select;
    }
    /// <summary>
      /// Any client (including host) tells server to spawn in a minion and grant ownership to the client.
      /// </summary> 
    [ServerRpc]
    private void SpawnMinionRallyServerRpc(sbyte xSpawn, sbyte zSpawn, byte minionID, sbyte xRally, sbyte zRally, ServerRpcParams serverRpcParams = default)
    {
        SelectableEntity minion = InternalSpawnMinion(xSpawn, zSpawn, minionID, serverRpcParams);

        var clientId = serverRpcParams.Receive.SenderClientId;
        if (NetworkManager.ConnectedClients.ContainsKey(clientId))
        {
            ClientRpcParams clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { clientId }
                }
            };
            MinionRallyClientRpc(minion, xRally, zRally, clientRpcParams);
        } 
    }
    [ClientRpc]
    private void MinionRallyClientRpc(NetworkBehaviourReference minion, sbyte xRally, sbyte zRally, ClientRpcParams clientRpcParams)
    { 
        if (minion.TryGet(out SelectableEntity select))
        {
            Vector3 rallyPos = new Vector3(xRally, 0, zRally); //get spawn position 
            select.controller.SetDestinationToPos(rallyPos);
        }  
    }
    /// <summary>
    /// Any client (including host) tells server to spawn in a minion and grant ownership to the client.
    /// </summary> 
    [ServerRpc] 
    private void SpawnMinionServerRpc(sbyte xSpawn, sbyte zSpawn, byte minionID, ServerRpcParams serverRpcParams = default)
    {
        SelectableEntity minion = InternalSpawnMinion(xSpawn, zSpawn, minionID, serverRpcParams); 
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
            Invoke("DoNotDoubleSelect", .2f);
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
        indices.Clear();

        if (selectedEntities.Count > 0) //at least one unit selected
        { 
            //show gui elements based on unit type selected
            foreach (SelectableEntity item in selectedEntities)
            { 
                if (!item.fullyBuilt)
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
        if (Global.Instance.selectedParent != null && Global.Instance.resourcesParent != null && Global.Instance.resourceText != null)
        { 
            if (Global.Instance.nameText != null && Global.Instance.descText != null && selectedEntities.Count == 1 && selectedEntities[0] != null)
            {
                Global.Instance.selectedParent.SetActive(true);
                Global.Instance.nameText.text = selectedEntities[0].displayName;
                Global.Instance.descText.text = selectedEntities[0].desc;

                if (selectedEntities[0].isHarvester)
                { 
                    Global.Instance.resourcesParent.SetActive(true);
                    Global.Instance.resourceText.text = "Stored gold: " + selectedEntities[0].harvestedResource + "/" + selectedEntities[0].harvestCapacity;
                }
            }
            else
            {
                Global.Instance.selectedParent.SetActive(false);
                Global.Instance.resourcesParent.SetActive(false);
            }
        }

        byte i = 0;
        int cap = Mathf.Clamp(indices.Count, 0, Global.Instance.productionButtons.Count);
        //enable a button for each indices
        for (; i < cap; i++)
        {
            Button button = Global.Instance.productionButtons[i];
            button.gameObject.SetActive(true);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>();
            FactionEntityClass fac = _faction.entities[indices[i]];
            text.text = fac.productionName + ": " + fac.goldCost +"g";
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
        UpdateBuildQueue();
    }
    public void UpdateBuildQueue()
    {
        Global.Instance.queueParent.gameObject.SetActive(false);
        if (selectedEntities.Count == 1)
        {
            SelectableEntity select = selectedEntities[0];
            if (select.type == SelectableEntity.EntityTypes.ProductionStructure && select.fullyBuilt)
            {
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
        FactionEntityClass fac = new FactionEntityClass();
        fac.productionName = _faction.entities[id].prefabToSpawn.name;
        fac.timeCost = _faction.entities[id].timeCost;
        fac.prefabToSpawn = _faction.entities[id].prefabToSpawn;
        fac.goldCost = _faction.entities[id].goldCost;
        fac.buildID = id;

        int cost = fac.goldCost;
        //try to spawn from all selected buildings if possible 
        foreach (SelectableEntity select in selectedEntities)
        {
            if (gold < cost)
            {
                break;
            }
            if (select.fullyBuilt && select.builderEntityIndices.Contains(id))
            { 
                gold -= cost;
                select.buildQueue.Add(fac);
            }
        }
        UpdateBuildQueue();
    }
    public void FromBuildingSpawn(SelectableEntity select, Vector3 rally, byte id = 0)
    { 
        Vector3 pos;
        if (select.spawnPosition != null)
        {
            pos = new Vector3(select.spawnPosition.position.x, 0, select.spawnPosition.position.z);
        }
        else
        {
            pos = select.transform.position;
        }
        SimpleSpawnMinion(pos, id, true, rally);
    }
    private void HoverBuildWithID(byte id = 0)
    {
        //Debug.Log(id);
        placementBlocked = false;
        buildState = BuildStates.ReadyToPlace;
        GameObject build = _faction.entities[id].prefabToSpawn;
        GameObject spawn = Instantiate(build, Vector3.zero, Quaternion.Euler(0, 180, 0)); //spawn locally  
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
        buildingPlacingID = id;
    }
    public List<byte> indices;
    private void SingleSelect()
    { 
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (!Input.GetKey(KeyCode.LeftShift)) //deselect all if not pressing shift
        {
            DeselectAll();
        }
        if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity))
        {
            SelectableEntity entity = hit.collider.GetComponent<SelectableEntity>();
            if (entity != null)
            {
                TrySelectEntity(entity); 
            }
        }

    }
    private void DoubleSelectDetected()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (!Input.GetKey(KeyCode.LeftShift)) //deselect all if not pressing shift
        {
            DeselectAll();
        }
        if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity))
        {
            SelectableEntity entity = hit.collider.GetComponent<SelectableEntity>();
            if (entity != null && ownedEntities.Contains(entity))
            { 
                SelectAllSameType(entity.type);
            }
        }
    }
    private void SelectAllSameType(SelectableEntity.EntityTypes type)
    {
        foreach (SelectableEntity item in ownedEntities)
        {
            if (item.type == type)
            { 
                item.Select(true);
                selectedEntities.Add(item);
            }
        }
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
    private void OnDrawGizmos()
    {
        if (IsOwner)
        {
            Gizmos.DrawWireSphere(_mousePosition, .1f);
            Gizmos.DrawSphere(worldPosition, .1f);
        }
    } 
}
