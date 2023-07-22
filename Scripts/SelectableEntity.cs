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
        ProductionStructure
    }
    public EntityTypes type = EntityTypes.Melee;
    public override void OnNetworkSpawn()
    {
        if (teamRenderer != null)
        { 
            teamRenderer.material = ColorReference.Instance.colors[System.Convert.ToInt32(net.OwnerClientId)];
        }
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
