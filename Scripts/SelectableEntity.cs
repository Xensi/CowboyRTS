using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode; 
public class SelectableEntity : NetworkBehaviour
{ 
    public MeshRenderer[] unbuiltRenderers;
    public MeshRenderer[] finishedRenderers;
    public MinionController controller;
    public NetworkVariable<sbyte> hitPoints = new NetworkVariable<sbyte>();
    //public byte hitPoints;

    public bool selected = false;
    [SerializeField] private GameObject selectIndicator;
    public GameObject targetIndicator;
    public NetworkObject net;
     
    public List<MeshRenderer> teamRenderers;
    private MeshRenderer[] allMeshes;
    
    public enum EntityTypes
    {
        Melee,
        Ranged,
        ProductionStructure,
        Builder,
        HarvestableStructure
    }
    public EntityTypes type = EntityTypes.Melee;
    public List<int> builderEntityIndices; //list of indices that can be built with this builder.    

    [SerializeField] private sbyte startingHP = 10;
    public byte maxHP = 10;
    public bool fullyBuilt = true;
    public Transform spawnPosition;

    public List<FactionEntityClass> buildQueue;
    private int delay = 50;
    private int count = 0;
    public AudioClip[] sounds; //0 spawn, 1 attack, 2 attackMove
    public Vector3 rallyPoint;
    [SerializeField] private GameObject rallyVisual;

    [SerializeField] private Material damagedState;
    private void RequestBuilders()
    {
        RTSPlayer local = Global.Instance.localPlayer;
        foreach (SelectableEntity item in local.selectedEntities)
        {
            if (item.type == EntityTypes.Builder)
            {
                MinionController minion = item.GetComponent<MinionController>();
                minion.SetBuildDestination(transform.position, this);
            }
        }
    }
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            Global.Instance.localPlayer.ownedEntities.Add(this);
            Global.Instance.localPlayer.lastSpawnedEntity = this;
            RequestBuilders();
        }
        allMeshes = GetComponentsInChildren<MeshRenderer>();
        if (IsServer)
        {
            hitPoints.Value = startingHP;
        }
        damagedThreshold = (sbyte) (maxHP / 2);
        rallyPoint = transform.position;
        SimplePlaySound(0);
        //AudioSource.PlayClipAtPoint(spawnSound, transform.position);
        UpdateTeamRenderers();

        targetIndicator.transform.parent = null;
    }
    public override void OnNetworkDespawn()
    {
        Destroy(targetIndicator);
    }
    private bool teamRenderersUpdated = false;
    private void UpdateTeamRenderers()
    {
        foreach (MeshRenderer item in teamRenderers)
        {
            if (item != null)
            {
                item.material = Global.Instance.colors[System.Convert.ToInt32(net.OwnerClientId)];
            }
        }
        teamRenderersUpdated = true;
    }
    private bool damaged = false;
    [SerializeField] private MeshRenderer[] damageableMeshes;
    public void TakeDamage(sbyte damage) //always managed by SERVER
    {
        hitPoints.Value -= damage; 
    }  
    private sbyte damagedThreshold;
    private void CheckIfDamaged()
    { 
        if (hitPoints.Value <= damagedThreshold)
        {
            damaged = true;
            for (int i = 0; i < damageableMeshes.Length; i++)
            {
                damageableMeshes[i].material = damagedState;
            }
        }
    }
    public void BuildThis(sbyte delta)
    {
        hitPoints.Value += delta;
    }
    public void OnTriggerEnter(Collider other)
    {
        Global.Instance.localPlayer.placementBlocked = true;
        Global.Instance.localPlayer.UpdatePlacement();
    }
    public void OnTriggerExit(Collider other)
    {
        Global.Instance.localPlayer.placementBlocked = false;
        Global.Instance.localPlayer.UpdatePlacement();
    } 
    private void FixedUpdate()
    {
        //Alert for issue where units are not "spawned"
        /*if (!IsSpawned)
        {
            Debug.LogError("Minion not spawned correctly ...");
            //Global.Instance.localPlayer.SimpleSpawnMinion()
        }*/
        if (!teamRenderersUpdated)
        {
            UpdateTeamRenderers();
        }

        if (!alive)
        {
            if (controller != null)
            {
                controller.state = MinionController.AnimStates.Die;
            }
            return;
        }

        if (!fullyBuilt)
        {
            CheckIfBuilt();
        }
        else
        { 
            if (count < delay)
            {
                count++;
            }
            else
            {
                count = 0;
                UpdateBuildQueue();
            } 
            if (!damaged)
            {
                CheckIfDamaged();
            }
            if (hitPoints.Value <= 0)
            {
                ProperDestroyMinion();
            }
            //walking sounds
            if (controller != null)
            {
                if (controller.anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalkStart") || controller.anim.GetCurrentAnimatorStateInfo(0).IsName("AttackWalk") || controller.anim.GetCurrentAnimatorStateInfo(0).IsName("Walk"))
                {
                    if (footstepCount < footstepThreshold)
                    {
                        footstepCount++;
                    }
                    else
                    {
                        footstepCount = 0;
                        Global.Instance.PlayClipAtPoint(Global.Instance.footsteps[Random.Range(0, Global.Instance.footsteps.Length)], transform.position, .05f); 
                    }
                }
            }
        }
    }
    public bool alive = true;
    private void ProperDestroyMinion()
    {
        targetIndicator.SetActive(false);
        targetIndicator.transform.parent = transform;
        alive = false;
        foreach (MeshRenderer item in allMeshes)
        { 
            if (!teamRenderers.Contains(item))
            { 
                item.material.color = Color.gray;
            }
        }
        if (type != EntityTypes.ProductionStructure)
        {
            if (controller != null)
            {
                controller.PrepareForDeath();
            }
            Invoke("Die", deathDuration);
        }
        else
        {
            Die();
        }
    }
    private int footstepCount = 0;
    private int footstepThreshold = 12;
    private void CheckIfBuilt()
    {
        if (hitPoints.Value >= maxHP)
        {
            fullyBuilt = true; 
            Global.Instance.localPlayer.UpdateGUIFromSelections();
            foreach (MeshRenderer item in finishedRenderers)
            {
                item.enabled = true;
            }
            foreach (MeshRenderer item in unbuiltRenderers)
            {
                item.enabled = false;
            }

        }
    }
    private float deathDuration = 10;
    private void Die()
    { 
        if (IsOwner)
        { 
            Global.Instance.localPlayer.ownedEntities.Remove(this);
            Global.Instance.localPlayer.selectedEntities.Remove(this);
            if (IsServer) //only the server may destroy networkobjects
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyObjectServerRpc(gameObject);
            }
        }
    }
    [ServerRpc]
    private void DestroyObjectServerRpc(NetworkObjectReference obj)
    {
        GameObject game = obj;
        Destroy(game);
    }
    public void SetRally()
    {
        /*Ray ray = Global.Instance.localPlayer.cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity))
        {
            rallyPoint = hit.point;
            if (rallyVisual != null)
            {
                rallyVisual.transform.position = rallyPoint;
            }
        }*/
        rallyPoint = Global.Instance.localPlayer.worldPosition;
        if (rallyVisual != null)
        {
            rallyVisual.transform.position = rallyPoint;
        }
    }
    public MeshRenderer[] attackEffects;

    private float attackEffectDuration = 0.1f;
    public void DisplayAttackEffects()
    {
        //fire locally
        for (int i = 0; i < attackEffects.Length; i++)
        {
            attackEffects[i].enabled = true;
        }
        Invoke("HideAttackEffects", attackEffectDuration);
        //request server to send to other clients
        RequestEffectServerRpc();
    }
    [ServerRpc]
    private void RequestEffectServerRpc()
    {
        PlayAttackEffectClientRpc();
    }
    [ClientRpc]
    private void PlayAttackEffectClientRpc()
    {
        if (!IsOwner)
        {
            for (int i = 0; i < attackEffects.Length; i++)
            {
                attackEffects[i].enabled = true;
            }
            Invoke("HideAttackEffects", attackEffectDuration);
        }
    }
    private void HideAttackEffects()
    { 
        for (int i = 0; i < attackEffects.Length; i++)
        {
            attackEffects[i].enabled = false;
        }
    }
    public void SimplePlaySound(byte id)
    {
        //fire locally 
        AudioClip clip = sounds[id];
        Global.Instance.PlayClipAtPoint(clip, transform.position, 0.25f);
        //request server to send to other clients
        RequestSoundServerRpc(id);
    }
    [ServerRpc(RequireOwnership = false)]
    private void RequestSoundServerRpc(byte id)
    {
        PlaySoundClientRpc(id);
    }
    [ClientRpc]
    private void PlaySoundClientRpc(byte id)
    {
        if (!IsOwner)
        {
            AudioClip clip = sounds[id];
            Global.Instance.PlayClipAtPoint(clip, transform.position, 0.25f);
        }
    }
    private void UpdateBuildQueue()
    {
        if (buildQueue.Count > 0)
        {
            FactionEntityClass fac = buildQueue[0];
            fac.timeCost--;
            if (fac.timeCost <= 0)
            {
                Debug.Log("spawn");
                buildQueue.RemoveAt(0);
                Global.Instance.localPlayer.FromBuildingSpawn(this, rallyPoint, fac.buildID);
            }
        }
        Global.Instance.localPlayer.UpdateBuildQueue();
    } 
    public void UpdateTargetIndicator()
    { 
        if (targetIndicator != null)
        {
            if (alive)
            { 
                if (controller != null && controller.targetEnemy != null)
                {
                    targetIndicator.SetActive(selected);
                }
                else
                {
                    targetIndicator.SetActive(false);
                }
            }
            else
            {
                targetIndicator.SetActive(false);
            }
        }
    }
    public void UpdateIndicator()
    {
        if (selectIndicator != null) selectIndicator.SetActive(selected);
        UpdateTargetIndicator();
        if (rallyVisual != null)
        {
            rallyVisual.transform.position = rallyPoint;
            rallyVisual.SetActive(selected);
        }
    }
    public void Select(bool val)
    {
        if (alive)
        { 
            selected = val;
        }
        else
        {
            selected = false;
        }
        UpdateIndicator();
    }
}
