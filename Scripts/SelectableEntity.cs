using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class SelectableEntity : NetworkBehaviour
{
    public MinionController controller;
    //public NetworkVariable<byte> hitPoints = new NetworkVariable<byte>();
    public byte hitPoints;

    public bool selected = false;
    [SerializeField] private GameObject indicator;
    public NetworkObject net;
     
    public MeshRenderer teamRenderer;
    
    public enum EntityTypes
    {
        Melee,
        Ranged,
        ProductionStructure,
        Builder
    }
    public EntityTypes type = EntityTypes.Melee;
    public List<int> builderEntityIndices; //list of indices that can be built with this builder.    

    [SerializeField] private byte startingHP = 10;
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
    private void FixedUpdate()
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
        AudioSource.PlayClipAtPoint(clip, transform.position, 0.25f);
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
            AudioSource.PlayClipAtPoint(clip, transform.position, 0.5f);
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
    public override void OnNetworkSpawn()
    {
        if (teamRenderer != null)
        {
            teamRenderer.material = Global.Instance.colors[System.Convert.ToInt32(net.OwnerClientId)];
        }

        hitPoints = startingHP;
        /*if (IsServer)
        {
            hitPoints.Value = startingHP;
        }*/
        rallyPoint = transform.position;
        SimplePlaySound(0);
        //AudioSource.PlayClipAtPoint(spawnSound, transform.position);
    }
    private bool damaged = false;
    [SerializeField] private MeshRenderer[] meshes;
    public void TakeDamage(byte damage)
    {
        hitPoints -= damage;
        //hitPoints.Value -= damage;
        if (hitPoints <= 0)
        {
            Global.Instance.localPlayer.ownedEntities.Remove(this);
            Destroy(gameObject);
        }
        if (hitPoints <= maxHP / 2 && !damaged)
        {
            damaged = true;

            for (int i = 0; i < meshes.Length; i++)
            {
                meshes[i].material = damagedState;
            } 
        }
    }
    public void BuildThis(byte delta)
    {
        hitPoints += delta;
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
