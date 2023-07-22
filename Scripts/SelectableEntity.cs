using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class SelectableEntity : MonoBehaviour
{
    public bool selected = false;
    [SerializeField] private GameObject indicator;
    //public NetworkObject net;
    public enum EntityTypes
    {
        Melee,
        Ranged,
        ProductionStructure
    }
    public EntityTypes type = EntityTypes.Melee;
    public MeshRenderer teamColor;
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
