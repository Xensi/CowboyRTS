using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding; 
public class MinionController : NetworkBehaviour
{
    private Camera cam;
    [SerializeField] private Vector3 destination;
    public float speed = 4;
    public float rotSpeed = 10;

    [SerializeField] private Animator anim;
    bool animsEnabled = false;
    IAstarAI ai;
    [SerializeField] private SelectableEntity selector;
    public NetworkVariable<Material> teamColor;
    void OnEnable()
    {
        ai = GetComponent<IAstarAI>();
        // Update the destination right before searching for a path as well.
        // This is enough in theory, but this script will also update the destination every
        // frame as the destination is used for debugging and may be used for other things by other
        // scripts as well. So it makes sense that it is up to date every frame.
        if (ai != null) ai.onSearchPath += Update;
    }

    void OnDisable()
    {
        if (ai != null) ai.onSearchPath -= Update;
    }
     
    void Start()
    {
        destination = transform.position;
        oldPosition = transform.position;
        cam = Camera.main;

        if (anim != null)
        {
            animsEnabled = true;
        }
        else
        {

            anim = GetComponentInChildren<Animator>();
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) this.enabled = false;
    }
    void Update()
    { 
        if (Input.GetMouseButtonDown(1) && selector.selected)
        {
            SetDestination();
        }
        if (animsEnabled) UpdateAnimations();
    }
    private void FixedUpdate()
    { 
        change = GetActualPositionChange();
        //Debug.Log(change);
        if (ai != null) ai.destination = destination;
        //HandleMovement();
    }
    private enum AnimStates
    {
        Idle,
        Walk,
        Attack
    }
    private AnimStates state = AnimStates.Idle;
    private float change;
    private float walkAnimThreshold = 0.01f;
    private void UpdateAnimations()
    {
        switch (state)
        {
            case AnimStates.Idle:
                anim.Play("Idle");

                //dist = Vector3.SqrMagnitude(destination - transform.position);
                /*if (dist > walkAnimThreshold)
                {
                    state = AnimStates.Walk;
                }*/ 
                if (change > walkAnimThreshold)
                {
                    state = AnimStates.Walk;
                }
                break;
            case AnimStates.Walk:
                anim.Play("Walk");

                /*dist = Vector3.SqrMagnitude(destination - transform.position);
                if (dist <= walkAnimThreshold)
                {
                    state = AnimStates.Idle;
                }*/
                if (change <= walkAnimThreshold)
                {
                    state = AnimStates.Idle;
                }
                break;
            case AnimStates.Attack:
                anim.Play("Attack");
                break;
            default:
                break;
        }
    }
    private Vector3 oldPosition;
    private float GetActualPositionChange()
    {
        float dist = Vector3.SqrMagnitude(transform.position - oldPosition);
        oldPosition = transform.position;
        return dist/Time.deltaTime;
    }
    private void SetDestination()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity))
        {
            destination = hit.point;
        }
        state = AnimStates.Walk;
    } 
    /*private void HandleMovement()
    {
        transform.position = Vector3.MoveTowards(transform.position, destination, Time.deltaTime * speed);
        transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, destination - transform.position, Time.deltaTime * rotSpeed, 0));
        //transform.rotation = Quaternion.LookRotation(destination - transform.position);
    } */
}
