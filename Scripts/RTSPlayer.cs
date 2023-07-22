using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode; 

public class RTSPlayer : NetworkBehaviour
{
    private Camera cam;
    [SerializeField] private Vector3 destination;
    public float speed = 4;
    public float rotSpeed = 10;
    public GameObject minion;
    public GameObject building;
    [SerializeField] private Grid grid;
    public Vector3Int gridPosition;
    [SerializeField] private List<SelectableEntity> ownedEntities;
    [SerializeField] private List<SelectableEntity> selectedEntities;

    public FactionScriptableObject faction;
    public int entitiesIndex = 0;

    void Start()
    {
        cam = Camera.main; 
    }
    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
        }
        else
        { 
            SimpleSpawnMinion(ColorReference.Instance.playerSpawn[System.Convert.ToInt32(OwnerClientId)].position);
        }
    }
    void Update()
    {
        GetMouseWorldPosition();
        if (Input.GetMouseButtonDown(2))
        {
            SimpleSpawnMinion(worldPosition);
        }
        if (Input.GetMouseButtonDown(0))
        {
            TryToSelectOne();
        }
    }
    private void SimpleSpawnMinion(Vector3 pos)
    { 
        if (IsServer)
        {
            SpawnMinion(pos);
        }
        else
        {
            SpawnMinionServerRPC(pos);
        }
    }
    private void SpawnMinion(Vector3 pos)
    {
        pos.y = 0;
        GameObject guy = Instantiate(faction.entities[entitiesIndex].prefabToSpawn, pos, Quaternion.identity); //spawn locally
        SelectableEntity select = guy.GetComponent<SelectableEntity>(); 
        //select.teamColor.material = ColorReference.Instance.colors[System.Convert.ToInt32(OwnerClientId)];
        ownedEntities.Add(select);
        //now, tell the server about this
        NetworkObject net = guy.GetComponent<NetworkObject>();
        net.Spawn(); //spawn on network, syncing the game state for everyone 
        //UpdateColorClientRpc(guy, 0);
    } 
    [ServerRpc] //client tells server to spawn the object
    private void SpawnMinionServerRPC(Vector3 pos, ServerRpcParams serverRpcParams = default)
    { 
        pos.y = 0;
        GameObject guy = Instantiate(faction.entities[entitiesIndex].prefabToSpawn, pos, Quaternion.identity);
        NetworkObject net = guy.GetComponent<NetworkObject>();

        var clientId = serverRpcParams.Receive.SenderClientId;
        net.SpawnWithOwnership(clientId); 
        //update team color for server
        SelectableEntity select = guy.GetComponent<SelectableEntity>();
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
    bool doubleSelect = false;
    private void DoNotDoubleSelect()
    {
        doubleSelect = false;
    }
    private void TryToSelectOne()
    { 
        if (!doubleSelect)
        {
            doubleSelect = true;
            Invoke("DoNotDoubleSelect", .2f);
            SingleSelect();
        }
        else
        {
            doubleSelect = false;
            DoubleSelectDetected();
        }


    }
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
            if (entity != null && ownedEntities.Contains(entity))
            {
                selectedEntities.Add(entity);
                entity.Select(!entity.selected);
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
    }
    public Vector3 mousePosition;
    private Vector3 offset = new Vector3(0.5f, 0, .5f);
    private void GetMouseWorldPosition()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity))
        {
            mousePosition = hit.point;
            gridPosition = grid.WorldToCell(mousePosition);
            worldPosition = grid.CellToWorld(gridPosition) + offset;
        }
    }
    private Vector3 worldPosition;
    private void OnDrawGizmos()
    {
        if (IsOwner)
        {
            Gizmos.DrawWireSphere(mousePosition, .1f);
            Gizmos.DrawSphere(worldPosition, .1f);
        }
    }

    private void SetDestination()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity))
        {
            destination = hit.point;
        }
    }
    private void FixedUpdate()
    {
        HandleMovement();
    }
    private void HandleMovement()
    {
        transform.position = Vector3.MoveTowards(transform.position, destination, Time.deltaTime * speed);
        transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, destination - transform.position, Time.deltaTime * rotSpeed, 0));
        //transform.rotation = Quaternion.LookRotation(destination - transform.position);
    }
}
