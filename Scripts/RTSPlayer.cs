using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using UnityEngine.EventSystems;// Required when using Event data.
using TMPro;

public class RTSPlayer : NetworkBehaviour
{
    public int gold = 0;
    private Camera _cam;   
    [SerializeField] private Grid grid;
    private Vector3Int _gridPosition;
    public List<SelectableEntity> ownedEntities; //must be serialized or public
    [SerializeField] private List<SelectableEntity> _selectedEntities;

    [SerializeField] private FactionScriptableObject _faction;
    [SerializeField] private int _entitiesIndex = 0; //used to pick a prefab from faction list
    private Vector3 _mousePosition;
    private Vector3 _offset;
    private Vector3 _worldPosition;
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
        _cam = Camera.main; 
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
            SimpleSpawnMinion(Global.Instance.playerSpawn[System.Convert.ToInt32(OwnerClientId)].position, 0);
        }
    }
    private bool MouseOverUI()
    {
        return EventSystem.current.IsPointerOverGameObject();
    }
    void Update()
    {
        if (!MouseOverUI())
        {
            GetMouseWorldPosition();
            if (Input.GetMouseButtonDown(2))
            {
                SimpleSpawnMinion(_worldPosition, _entitiesIndex);
            }
            if (Input.GetMouseButtonDown(0))
            {
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
                if (buildState == BuildStates.ReadyToPlace)
                {
                    StopPlacingBuilding();
                }
            }
        } 
    }
    private void PlaceBuilding(int id = 0)
    {
        Debug.Log(id); 
        SimpleSpawnMinion(_worldPosition, id); 
        StopPlacingBuilding();
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
                meshes[i].material = Global.Instance.blocked;
            }
        }
        else
        { 
            for (int i = 0; i < meshes.Length; i++)
            {
                meshes[i].material = Global.Instance.transparent;
            }
        }
    }
    public bool placementBlocked = false;
    private GameObject followCursorObject;
    private int buildingPlacingID = 0;
    private MeshRenderer[] meshes;
    private void GetMouseWorldPosition()
    {
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity, groundLayer))
        {
            _mousePosition = hit.point;
            _gridPosition = grid.WorldToCell(_mousePosition);
            _worldPosition = grid.CellToWorld(_gridPosition) + _offset;
            if (followCursorObject != null)
            {
                followCursorObject.transform.position = _worldPosition;
            }
        }
    }
    #region SpawnMinion
    private void SimpleSpawnMinion(Vector3 pos, int id)
    { 
        if (IsServer)
        {
            SpawnMinion(pos, id);
        }
        else
        {
            SpawnMinionServerRPC(pos, id);
        }
    }
    private void SpawnMinion(Vector3 pos, int id)
    {
        pos.y = 0;
        GameObject guy = Instantiate(_faction.entities[id].prefabToSpawn, pos, Quaternion.identity); //spawn locally
        SelectableEntity select = guy.GetComponent<SelectableEntity>();
        NetworkObject net = guy.GetComponent<NetworkObject>();
        ownedEntities.Add(select);

        net.SpawnWithOwnership(OwnerClientId); //spawn on network, syncing the game state for everyone  
    } 
    [ServerRpc] //client tells server to spawn the object
    private void SpawnMinionServerRPC(Vector3 pos, int id, ServerRpcParams serverRpcParams = default)
    { 
        pos.y = 0;
        GameObject guy = Instantiate(_faction.entities[id].prefabToSpawn, pos, Quaternion.identity);
        //SelectableEntity select = guy.GetComponent<SelectableEntity>();
        NetworkObject net = guy.GetComponent<NetworkObject>();

        var clientId = serverRpcParams.Receive.SenderClientId;
        net.SpawnWithOwnership(clientId); 
        //update team color for server
        //select.teamColor.material = ColorReference.Instance.colors[System.Convert.ToInt32(clientId)]; 
        //send this to client that requested it
        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { clientId }
            }
        };
        UpdateOwnedEntitiesClientRpc(guy, clientRpcParams);  
    } 
    [ClientRpc]
    private void UpdateOwnedEntitiesClientRpc(NetworkObjectReference guy, ClientRpcParams clientRpcParams)
    {
        GameObject obj = guy;
        if (obj != null)
        { 
            SelectableEntity select = obj.GetComponent<SelectableEntity>();
            ownedEntities.Add(select); 
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
            Invoke("DoNotDoubleSelect", .2f);
            SingleSelect();
        }
        else
        {
            _doubleSelect = false;
            DoubleSelectDetected();
        }
        UpdateGUIFromSelections();
    }
    private void UpdateGUIFromSelections()
    {
        indices.Clear();

        if (_selectedEntities.Count > 0) //at least one unit selected
        { 
            //show gui elements based on unit type selected
            foreach (SelectableEntity item in _selectedEntities)
            { 
                foreach (int num in item.builderEntityIndices)
                {
                    if (!indices.Contains(num))
                    {
                        indices.Add(num);
                    }
                }
            }
        } 
        int i = 0;
        //enable a button for each indices
        for (; i < indices.Count; i++)
        {
            Button button = Global.Instance.productionButtons[i];
            button.gameObject.SetActive(true);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>();
            text.text = _faction.entities[indices[i]].productionName; 
            int j = indices[i];
            Debug.Log(j);
            button.onClick.RemoveAllListeners(); 
            if (_faction.entities[indices[i]].needsConstructing)
            { 
                button.onClick.AddListener(delegate { HoverBuildWithID(j); });
            }
            else
            { 
                button.onClick.AddListener(delegate { SpawnFromBuilding(j); });
            }
        }
        for (; i < Global.Instance.productionButtons.Count; i++)
        { 
            Global.Instance.productionButtons[i].gameObject.SetActive(false);
        }
    }
    private void SpawnFromBuilding(int id = 0)
    {
        //first pick a building from those selected that is able to spawn
        SelectableEntity select = _selectedEntities[0];
        SpawnMinion(select.transform.position, id);
    }
    private void HoverBuildWithID(int id = 0)
    {
        Debug.Log(id);
        placementBlocked = false;
        buildState = BuildStates.ReadyToPlace;
        GameObject build = _faction.entities[id].prefabToSpawn;
        GameObject spawn = Instantiate(build, Vector3.zero, Quaternion.identity); //spawn locally 
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
    public List<int> indices;
    private void SingleSelect()
    { 
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
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
                _selectedEntities.Add(entity);
                entity.Select(!entity.selected);
            }
        }
    }
    private void DoubleSelectDetected()
    {
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
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
                _selectedEntities.Add(item);
            }
        }
    }
    private void DeselectAll()
    { 
        foreach (SelectableEntity item in _selectedEntities)
        {
            item.Select(false);
        }
        _selectedEntities.Clear();
    }
    #endregion
    private void OnDrawGizmos()
    {
        if (IsOwner)
        {
            Gizmos.DrawWireSphere(_mousePosition, .1f);
            Gizmos.DrawSphere(_worldPosition, .1f);
        }
    } 
}
