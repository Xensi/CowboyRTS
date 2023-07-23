using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class SelectableEntity : NetworkBehaviour
{
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

    public NetworkVariable<int> hitPoints = new NetworkVariable<int>();
    [SerializeField] private int startingHP = 10;
    public override void OnNetworkSpawn()
    {
        if (teamRenderer != null)
        {
            teamRenderer.material = Global.Instance.colors[System.Convert.ToInt32(net.OwnerClientId)];
        }

        if (IsServer)
        {
            hitPoints.Value = startingHP;
        }
    }
    public void TakeDamage(int damage)
    {
        hitPoints.Value -= damage;
        if (hitPoints.Value <= 0)
        {
            Global.Instance.localPlayer.ownedEntities.Remove(this);
            Destroy(gameObject);
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
    }
    public void Select(bool val)
    {
        selected = val;
        UpdateIndicator();
    }
}
