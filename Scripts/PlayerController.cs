 using UnityEngine;
using Unity.Netcode;
public class PlayerController : NetworkBehaviour
{
    private Camera cam;
    [SerializeField] private Vector3 destination;
    public float speed = 4;
    public float rotSpeed = 10;

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
        if (Input.GetMouseButtonDown(1))
        {
            SetDestination();
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
    private void OnDrawGizmos()
    {
        if (IsOwner) Gizmos.DrawSphere(destination, .1f);
    }
}
