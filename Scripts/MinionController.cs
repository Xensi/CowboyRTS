using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding;

[RequireComponent(typeof(LocalRotation))]
public class MinionController : NetworkBehaviour
{
    private Camera cam;
    public Vector3 destination;  
    [SerializeField] private Animator anim;
    bool animsEnabled = false;
    AIPath ai;
    [SerializeField] private SelectableEntity selector;
    private AnimStates state = AnimStates.Spawn;
    private enum AnimStates
    {
        Idle,
        Walk,
        Attack,
        Build,
        Spawn
    }
    public enum AttackType
    {
        Melee, Ranged
    }
    public AttackType attackType = AttackType.Melee;
    private bool chasingEnemy = false;
    private float change;
    private float walkAnimThreshold = 0.01f;
    private Vector3 oldPosition;

    [SerializeField] private float attackRange = 1;

    public SelectableEntity targetEnemy;
    public bool followingMoveOrder = false;
    [SerializeField] private LocalRotation localRotate;

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
    private void Update()
    {
        
    }
    private void Awake()
    {
        enemyMask = LayerMask.GetMask("Entity", "Obstacle");
        cam = Camera.main;

        if (anim != null)
        {
            animsEnabled = true;
        }
        else
        {
            anim = GetComponentInChildren<Animator>();
        }
        if (anim != null)
        {
            anim.Play("Spawn");
        } 
        Invoke("FinishSpawning", spawnDuration);
        //rigid.isKinematic = true;
    } 
    public override void OnNetworkSpawn()
    {
        //localRotate.rotSpeed = ai.rotationSpeed;
        //localRotate.enabled = !IsOwner; //if isOwner, localRotate is disabled
        enabled = IsOwner;
        destination = transform.position;
        oldPosition = transform.position;
    }
    private void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
    LayerMask enemyMask; 
    public void SetBuildDestination(Vector3 pos, SelectableEntity ent)
    { 
        destination = pos;
        buildTarget = ent; 
    } 
    private void CheckTargetEnemy() //use spherecast to get an enemy. if in attack range they become our target
    { 
        if (targetEnemy == null)
        {
            enemyInRange = false;
            //do a spherecast to get one
            Vector3 center = transform.position;
            float detectionRange = attackRange;
            if (attackMoving)
            {
                detectionRange = attackRange * 1.25f;
            }
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
    [SerializeField] private Rigidbody rigid;
    private float spawnDuration = .5f;
    private void FinishSpawning()
    {
        //rigid.isKinematic = false;
        state = AnimStates.Idle; 
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
            if ((!followingMoveOrder || attackMoving) && state != AnimStates.Build)
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
    private void UpdateAnimations()
    {
        switch (state)
        {
            case AnimStates.Spawn:
                break;
            case AnimStates.Idle:
                anim.Play("Idle"); 
                if (enemyInRange && !followingMoveOrder)
                {
                    StartAttack();
                }
                else if (buildTarget != null && Vector3.Distance(transform.position, buildTarget.transform.position) <= attackRange)
                {
                    StartBuild();
                }
                else if (buildTarget != null && Vector3.Distance(transform.position, buildTarget.transform.position) > attackRange)
                {
                    state = AnimStates.Walk;
                }
                else if (followingMoveOrder || chasingEnemy)
                {
                    state = AnimStates.Walk;
                }

                break;
            case AnimStates.Walk:
                anim.Play("Walk");
                if (enemyInRange && (!followingMoveOrder || attackMoving))
                {
                    StartAttack();
                }
                else if (buildTarget != null && Vector3.Distance(transform.position, buildTarget.transform.position) <= attackRange)
                {
                    StartBuild();
                } 
                else if (!followingMoveOrder && !chasingEnemy && (buildTarget == null || buildTarget.fullyBuilt))
                {
                    state = AnimStates.Idle;
                }
                break;
            case AnimStates.Attack:
                anim.Play("Attack");
                transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, targetEnemy.transform.position - transform.position, Time.deltaTime * ai.rotationSpeed, 0));
                break;
            case AnimStates.Build:
                anim.Play("Attack");
                transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, buildTarget.transform.position - transform.position, Time.deltaTime * ai.rotationSpeed, 0));
                //
                if (buildTarget.fullyBuilt)
                {
                    CancelAttack();
                }
                else if (buildTarget != null && Vector3.Distance(transform.position, buildTarget.transform.position) > attackRange)
                {
                    state = AnimStates.Walk;
                }
                
                break;
            default:
                break;
        }
    }
    private void CancelAttack()
    {
        targetEnemy = null;
        buildTarget = null;
        state = AnimStates.Idle;
        CancelInvoke();
    }
    private byte buildDelta = 1;
    private void StartBuild()
    {
        state = AnimStates.Build;
        destination = transform.position;
        Invoke("BuildTarget", impactTime);
        Invoke("ReturnState", attackDuration);
    }
    private void StartAttack()
    {
        state = AnimStates.Attack;
        destination = transform.position;
        Invoke("DamageEnemy", impactTime);
        Invoke("ReturnState", attackDuration);
    }
    public SelectableEntity buildTarget;
    [SerializeField] private byte damage = 1;
    private void DamageEnemy()
    {
        selector.SimplePlaySound(1);
        //AudioSource.PlayClipAtPoint(selector.attackSound, transform.position);
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
    private void DamageEnemyServerRpc(byte damage, NetworkObjectReference enemy)
    {
        GameObject actual = enemy;
        actual.GetComponent<SelectableEntity>().TakeDamage(damage); 
    }

    private void BuildTarget()
    {
        selector.SimplePlaySound(1);
        //AudioSource.PlayClipAtPoint(selector.attackSound, transform.position);
        if (IsServer)
        {
            BuildFromServer();
        }
        else
        {
            BuildFromClientServerRpc(buildTarget.gameObject);
            if (buildTarget.hitPoints.Value >= buildTarget.maxHP - 1)
            {
                buildTarget.fullyBuilt = true;
                buildTarget = null;
                Global.Instance.localPlayer.UpdateGUIFromSelections();
            }
        }
    }
    private void BuildFromServer()
    {
        buildTarget.hitPoints.Value += buildDelta;
        if (buildTarget.hitPoints.Value >= buildTarget.maxHP)
        {
            buildTarget.fullyBuilt = true;
            buildTarget = null;
            Global.Instance.localPlayer.UpdateGUIFromSelections();
        }
    }
    [ServerRpc]
    private void BuildFromClientServerRpc(NetworkObjectReference obj)
    {
        GameObject act = obj;
        SelectableEntity buildTarget = act.GetComponent<SelectableEntity>();
        buildTarget.hitPoints.Value += buildDelta;
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

    public bool attackMoving = false;
    public void SetDestination(bool attackMove = false)
    {
        if (state != AnimStates.Spawn)
        {
            targetEnemy = null;
            buildTarget = null;
            enemyInRange = false;
            basicallyIdleInstances = 0;
            followingMoveOrder = true;
            attackMoving = attackMove;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity))
            {
                SelectableEntity select = hit.collider.GetComponent<SelectableEntity>();
                if (select != null)
                {
                    if (select.net.OwnerClientId == selector.net.OwnerClientId && !select.fullyBuilt) //same team
                    {
                        if (selector.type == SelectableEntity.EntityTypes.Builder)
                        {
                            buildTarget = select;
                        }
                    }
                }

                destination = hit.point;
            }
            state = AnimStates.Walk;
            CancelInvoke();
        } 
    } 
    /*private void HandleMovement()
    {
        transform.position = Vector3.MoveTowards(transform.position, destination, Time.deltaTime * speed);
        transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, destination - transform.position, Time.deltaTime * rotSpeed, 0));
        //transform.rotation = Quaternion.LookRotation(destination - transform.position);
    } */
}
