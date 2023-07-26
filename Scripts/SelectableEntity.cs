using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class SelectableEntity : NetworkBehaviour
{
    public MinionController controller;
    public NetworkVariable<sbyte> hitPoints = new NetworkVariable<sbyte>();
    //public byte hitPoints;

    public bool selected = false;
    [SerializeField] private GameObject indicator;
    public NetworkObject net;
     
    public MeshRenderer[] teamRenderers;
    
    public enum EntityTypes
    {
        Melee,
        Ranged,
        ProductionStructure,
        Builder
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
    public override void OnNetworkSpawn()
    {
        foreach (MeshRenderer item in teamRenderers)
        { 
            if (item != null)
            {
                item.material = Global.Instance.colors[System.Convert.ToInt32(net.OwnerClientId)];
            }
        }
        if (IsServer)
        {
            hitPoints.Value = startingHP;
        }
        damagedThreshold = (sbyte) (maxHP / 2);
        rallyPoint = transform.position;
        SimplePlaySound(0);
        //AudioSource.PlayClipAtPoint(spawnSound, transform.position);
    }
    private bool damaged = false;
    [SerializeField] private MeshRenderer[] meshes;
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
            for (int i = 0; i < meshes.Length; i++)
            {
                meshes[i].material = damagedState;
            }
        }
    }
    public void BuildThis(sbyte delta)
    {
        hitPoints.Value += delta;
    } 
    private void FixedUpdate()
    { 
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
    private int footstepCount = 0;
    private int footstepThreshold = 12;
    private void CheckIfBuilt()
    {
        if (hitPoints.Value >= maxHP)
        {
            fullyBuilt = true; 
            Global.Instance.localPlayer.UpdateGUIFromSelections();
        }
    }
    private void ProperDestroyMinion()
    {
        Global.Instance.localPlayer.ownedEntities.Remove(this);
        Global.Instance.localPlayer.selectedEntities.Remove(this);
        Destroy(gameObject);
    }
    public void SetRally()
    { 
        Ray ray = Global.Instance.localPlayer.cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity))
        {
            rallyPoint = hit.point;
            if (rallyVisual != null)
            {
                rallyVisual.transform.position = rallyPoint;
            }
        }
    }
     
    public void SimplePlaySound(byte id)
    {
        //fire locally 
        AudioClip clip = sounds[id];
        Global.Instance.PlayClipAtPoint(clip, transform.position, 0.25f, Random.Range(.9f, 1.1f));
        //request server to send to other clients
        RequestSoundServerRpc(id);
    }
    [ServerRpc]
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
            Global.Instance.PlayClipAtPoint(clip, transform.position, 0.25f, Random.Range(.9f, 1.1f));
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
    private void UpdateIndicator()
    {
        if (indicator != null) indicator.SetActive(selected);
        if (rallyVisual != null)
        {
            rallyVisual.transform.position = rallyPoint;
            rallyVisual.SetActive(selected);
        }
    }
    public void Select(bool val)
    {
        selected = val;
        UpdateIndicator();
    }
}
