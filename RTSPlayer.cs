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

    void Start()
    {
        cam = Camera.main;
    }
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) this.enabled = false;
    }
    void Update()
    {
        /*if (Input.GetMouseButtonDown(1))
        {
            SetDestination();
        }*/
        if (Input.GetMouseButtonDown(0))
        {
            TryToSpawnMinion();
        }
    }
    private void TryToSpawnMinion()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity))
        {
            if (IsServer)
            {
                SimpleSpawnMinion(hit.point);
            }
            else
            { //client sends information to server
                SpawnMinionServerRPC(hit.point);
            }
        }
    }
    private void SimpleSpawnMinion(Vector3 pos)
    { 
        GameObject guy = Instantiate(minion, pos, Quaternion.identity);
        guy.GetComponent<NetworkObject>().Spawn();  
    }

    [ServerRpc]
    private void SpawnMinionServerRPC(Vector3 pos, ServerRpcParams serverRpcParams = default)
    {
        var id = serverRpcParams.Receive.SenderClientId;
        GameObject guy = Instantiate(minion, pos, Quaternion.identity);
        guy.GetComponent<NetworkObject>().SpawnWithOwnership(id);
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
    private void OnDrawGizmos()
    {
        if (IsOwner) Gizmos.DrawSphere(destination, .1f);
    }
}
