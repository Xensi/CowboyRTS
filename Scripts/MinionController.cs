using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Pathfinding;
 
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
        if (!IsOwner)
        {
            rigid.isKinematic = true;
        }
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
            float detectionRange = attackRange * 1.25f;
            if (attackMoving)
            {
                detectionRange = attackRange;
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
            if (closest != null)
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

        if (delay >= 24) //0 to 24 is half of 50
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
    //50 fps fixed update
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
            if (attackMoving)
            {
                destination = orderedDestination;
            }
        }
    }
    bool playedAttackMoveSound = false;
    private void UpdateAnimations()
    {
        switch (state)
        {
            case AnimStates.Spawn:
                break;
            case AnimStates.Idle:
                if (anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk"))
                { 
                    anim.SetTrigger("Idle");
                }
                else
                { 
                    anim.Play("Idle");
                }
                if (enemyInRange && !followingMoveOrder)
                {
                    anim.ResetTrigger("Idle");
                    StartAttack();
                }
                else if (buildTarget != null && Vector3.Distance(transform.position, buildTarget.transform.position) <= attackRange)
                {
                    anim.ResetTrigger("Idle");
                    StartBuild();
                }
                else if (buildTarget != null && Vector3.Distance(transform.position, buildTarget.transform.position) > attackRange)
                {
                    anim.ResetTrigger("Idle");
                    state = AnimStates.Walk;
                }
                else if (followingMoveOrder || chasingEnemy)
                {
                    anim.ResetTrigger("Idle");
                    state = AnimStates.Walk;
                }

                break;
            case AnimStates.Walk:
                if (attackMoving)
                {
                    if (!anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk"))
                    { 
                        if (!playedAttackMoveSound)
                        {
                            playedAttackMoveSound = true;
                            selector.SimplePlaySound(2);
                        }
                        anim.Play("AttackWalkStart"); 
                    }
                }
                else
                {
                    if (anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk") || anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalkStart"))
                    {
                        playedAttackMoveSound = false;
                        anim.SetTrigger("Walk");
                    }
                    else
                    { 
                        anim.Play("Walk");
                    }
                }
                if (enemyInRange && (!followingMoveOrder || attackMoving))
                {
                    playedAttackMoveSound = false;
                    anim.ResetTrigger("Walk");
                    StartAttack();
                }
                else if (buildTarget != null && Vector3.Distance(transform.position, buildTarget.transform.position) <= attackRange)
                {
                    anim.ResetTrigger("Walk");
                    playedAttackMoveSound = false;
                    StartBuild();
                } 
                else if (!followingMoveOrder && !chasingEnemy && (buildTarget == null || buildTarget.fullyBuilt))
                {
                    anim.ResetTrigger("Walk");
                    playedAttackMoveSound = false;
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
        Invoke("SimpleBuildTarget", impactTime);
        Invoke("ReturnState", attackDuration);
    }
    private void StartAttack()
    {
        state = AnimStates.Attack;
        destination = transform.position;
        Invoke("SimpleDamageEnemy", impactTime);
        Invoke("ReturnState", attackDuration);
    }
    public SelectableEntity buildTarget;
    [SerializeField] private byte damage = 1; 
    public void SimpleDamageEnemy() //since hp is a network variable, changing it on the server will propagate changes to clients as well
    {
        //fire locally
        selector.SimplePlaySound(1); 

        if (IsServer)
        {
            targetEnemy.TakeDamage(damage);
        }
        else //client tell server to change the network variable
        {  
            RequestDamageServerRpc(damage, targetEnemy);
        }
    }
    [ServerRpc]
    private void RequestDamageServerRpc(byte damage, NetworkBehaviourReference enemy)
    {
        //server must handle damage! 
        if (enemy.TryGet(out SelectableEntity select))
        {
            select.TakeDamage(damage);
        } 
        //DamageClientRpc(damage, enemy); //send to all clients?
    }
    /*[ClientRpc]
    private void DamageClientRpc(byte damage, NetworkObjectReference enemy)
    { 
        if (!IsOwner)
        {
            selector.SimplePlaySound(1);
        }
    } */
    public void SimpleBuildTarget()
    {
        selector.SimplePlaySound(1);

        RequestBuildServerRpc(buildDelta, buildTarget.gameObject);
        
    }
    [ServerRpc]
    private void RequestBuildServerRpc(byte delta, NetworkObjectReference structure)
    {
        BuildClientRpc(delta, structure);
    }
    [ClientRpc]
    private void BuildClientRpc(byte delta, NetworkObjectReference structure)
    {
        /*if (!IsOwner)
        {
            selector.SimplePlaySound(1);
        }
        //server must handle delta!
        GameObject actual = structure;
        actual.GetComponent<SelectableEntity>().BuildThis(delta);

        if (IsOwner) //only the owner really cares if it's fully built 
        { 
            if (buildTarget.hitPoints >= buildTarget.maxHP)
            {
                buildTarget.fullyBuilt = true;
                buildTarget = null;
                Global.Instance.localPlayer.UpdateGUIFromSelections();
            }
        }*/
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
    public Vector3 orderedDestination;
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
                    if (select.net.OwnerClientId == selector.net.OwnerClientId) //same team
                    {
                        if (selector.type == SelectableEntity.EntityTypes.Builder && !select.fullyBuilt)
                        {
                            buildTarget = select;
                        }
                    }
                    else
                    {
                        targetEnemy = select;
                        attackMoving = true;
                    }
                }

                destination = hit.point;
                orderedDestination = destination;
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
