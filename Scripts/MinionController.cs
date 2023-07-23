using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding; 
public class MinionController : NetworkBehaviour
{
    private Camera cam;
    [SerializeField] private Vector3 destination;  
    [SerializeField] private Animator anim;
    bool animsEnabled = false;
    AIPath ai;
    [SerializeField] private SelectableEntity selector;
    private AnimStates state = AnimStates.Idle;
    private float change;
    private float walkAnimThreshold = 0.01f;
    private Vector3 oldPosition;

    [SerializeField] private float attackRange = 1;

    public SelectableEntity targetEnemy;
    private bool followingMoveOrder = false; 
    private void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
    LayerMask enemyMask;
    void Update()
    {
        if (Input.GetMouseButtonDown(1) && selector.selected)
        {
            SetDestination();
        }
    }
    private void CheckTargetEnemy()
    { 
        if (targetEnemy == null)
        {
            enemyInRange = false;
            //do a spherecast to get one
            Vector3 center = transform.position;
            float detectionRange = attackRange * 2;
            int maxColliders = Mathf.RoundToInt(20 * detectionRange);
            Collider[] hitColliders = new Collider[maxColliders];
            int numColliders = Physics.OverlapSphereNonAlloc(center, detectionRange, hitColliders, enemyMask);
            //get closest collider 
            Collider closest = null;
            float distance = Mathf.Infinity;
            for (int i = 0; i < numColliders; i++)
            {
                if (hitColliders[i].gameObject == gameObject || hitColliders[i].isTrigger) //skip self
                {
                    continue;
                }
                SelectableEntity select = hitColliders[i].GetComponent<SelectableEntity>();
                if (Global.Instance.localPlayer.ownedEntities.Contains(select))
                {
                    continue;
                }
                float newDist = Vector3.SqrMagnitude(transform.position - hitColliders[i].transform.position);
                if (newDist < distance)
                {
                    closest = hitColliders[i];
                }
            }
            if (closest != null )
            {
                float dist = Vector3.Distance(transform.position, closest.transform.position);
                if (dist <= attackRange)
                { 
                    targetEnemy = closest.GetComponent<SelectableEntity>();
                    enemyInRange = true;
                    chasingEnemy = false;
                }
                else
                { 
                    destination = closest.transform.position;
                    enemyInRange = false;
                    chasingEnemy = true;
                }
            }
            else
            {
                enemyInRange = false;
                chasingEnemy = false;
            }
        }
        else
        {
            float dist = Vector3.Distance(transform.position, targetEnemy.transform.position);
            if (dist > attackRange)
            {
                chasingEnemy = false;
                targetEnemy = null;
                enemyInRange = false;
                CancelAttack();
            }
            else
            {
                chasingEnemy = false;
                enemyInRange = true;
            }
        }
    }
    public bool enemyInRange = false;
    void OnEnable()
    {
        ai = GetComponent<AIPath>();
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
        enemyMask = LayerMask.GetMask("Entity", "Obstacle");
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
        if (!IsOwner) enabled = false;
    }
    private void FixedUpdate()
    { 
        change = GetActualPositionChange();
        //Debug.Log(change);
        if (ai != null) ai.destination = destination;
        //HandleMovement();
        DetectIfShouldStopFollowingMoveOrder();

        if (delay > 30)
        {
            delay = 0;
            if (!followingMoveOrder)
            { 
                CheckTargetEnemy();
            }
        }
        else
        {
            delay++;
        }
        if (animsEnabled) UpdateAnimations();
    }
    //60 fps, 
    private int delay = 0;
    private int basicallyIdleInstances = 0;
    private int idleThreshold = 30;
    private void DetectIfShouldStopFollowingMoveOrder()
    {
        if (change <= walkAnimThreshold && basicallyIdleInstances <= idleThreshold)
        {
            basicallyIdleInstances++;
        }
        else if (change > walkAnimThreshold)
        {
            basicallyIdleInstances = 0;
        }
        if (basicallyIdleInstances > idleThreshold || ai.reachedDestination)
        {
            followingMoveOrder = false;
        }
    }
    private enum AnimStates
    {
        Idle,
        Walk,
        Attack
    }
    public enum AttackType
    {
        Melee, Ranged
    }
    public AttackType attackType = AttackType.Melee;
    private bool chasingEnemy = false;
    private void UpdateAnimations()
    {
        switch (state)
        {
            case AnimStates.Idle:
                anim.Play("Idle"); 
                if (enemyInRange && !followingMoveOrder)
                {
                    StartAttack();
                }
                else if (followingMoveOrder || chasingEnemy)
                {
                    state = AnimStates.Walk;
                } 

                break;
            case AnimStates.Walk:
                anim.Play("Walk");
                if (enemyInRange && !followingMoveOrder)
                {
                    StartAttack();
                }
                else if (!followingMoveOrder && !chasingEnemy)
                {
                    state = AnimStates.Idle;
                }
                break;
            case AnimStates.Attack:
                anim.Play("Attack");
                transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, targetEnemy.transform.position - transform.position, Time.deltaTime * ai.rotationSpeed, 0));
                break;
            default:
                break;
        }
    }
    [SerializeField] private int damage = 1;
    private void DamageEnemy()
    {
        if (IsServer)
        { 
            targetEnemy.TakeDamage(damage);
        }
        else //client needs to tell server to do this
        {
            DamageEnemyServerRpc(damage, targetEnemy.gameObject);
        }
    }
    [ServerRpc]
    private void DamageEnemyServerRpc(int damage, NetworkObjectReference enemy)
    {
        GameObject actual = enemy;
        actual.GetComponent<SelectableEntity>().TakeDamage(damage); 
    }
    private void StartAttack()
    { 
        state = AnimStates.Attack;
        destination = transform.position;
        Invoke("DamageEnemy", impactTime);
        Invoke("ReturnState", attackDuration);
    }
    private void CancelAttack()
    { 
        state = AnimStates.Idle;
        CancelInvoke();
    }
    private float attackDuration = 1;
    private float impactTime = .5f;
    private void ReturnState()
    {
        state = AnimStates.Idle;
    }
    private float GetActualPositionChange()
    {
        float dist = Vector3.SqrMagnitude(transform.position - oldPosition);
        oldPosition = transform.position;
        return dist/Time.deltaTime;
    }
    private void SetDestination()
    {
        targetEnemy = null;
        enemyInRange = false;
        basicallyIdleInstances = 0;
        followingMoveOrder = true; 
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity))
        {
            destination = hit.point;
        }
        state = AnimStates.Walk;
        CancelInvoke();
    } 
    /*private void HandleMovement()
    {
        transform.position = Vector3.MoveTowards(transform.position, destination, Time.deltaTime * speed);
        transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, destination - transform.position, Time.deltaTime * rotSpeed, 0));
        //transform.rotation = Quaternion.LookRotation(destination - transform.position);
    } */
}
