using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class MinionController : NetworkBehaviour
{
    private Camera cam;
    [SerializeField] private Vector3 destination;
    public float speed = 4;
    public float rotSpeed = 10;

    [SerializeField] private Animator anim;
    bool animsEnabled = false;
    void Start()
    {
        cam = Camera.main;

        anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            animsEnabled = true;
        }
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
        if (animsEnabled) UpdateAnimations();
    }
    private float walkAnimThreshold = 0.1f;
    private enum AnimStates
    {
        Idle,
        Walk,
        Attack
    }
    private AnimStates state = AnimStates.Idle;
    private void UpdateAnimations()
    {
        float dist = 0;
        switch (state)
        {
            case AnimStates.Idle:
                anim.Play("Idle");

                dist = Vector3.SqrMagnitude(destination - transform.position);
                if (dist > walkAnimThreshold)
                {
                    state = AnimStates.Walk;
                }
                break;
            case AnimStates.Walk:
                anim.Play("Walk");

                dist = Vector3.SqrMagnitude(destination - transform.position);
                if (dist <= walkAnimThreshold)
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
