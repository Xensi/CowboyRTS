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
    public override void OnNetworkSpawn()
    {
        if (teamRenderer != null)
        { 
            teamRenderer.material = Global.Instance.colors[System.Convert.ToInt32(net.OwnerClientId)];
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
