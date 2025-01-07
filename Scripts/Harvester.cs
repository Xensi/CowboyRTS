using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public class Harvester : MonoBehaviour
{
    public class BagResource
    {
        public ResourceType type = ResourceType.Gold;
    }
    private List<BagResource> harvesterBag;
    [SerializeField] private MeshRenderer[] resourceCollectingMeshes; //Add to this array to show meshes that indicate collected resources
    private SelectableEntity entity;

    private void Awake()
    {
        entity = GetComponent<SelectableEntity>();
    }

    private void UpdateResourceCollectableMeshes()
    {
        if (entity == null) return;
        if (resourceCollectingMeshes.Length == 0) return;
        /*if (entity.isVisibleInFog)
        {
            //compare max resources against max resourceCollectingMeshes
            float frac = (float) harvestedResourceAmount / harvestCapacity;
            int numToActivate = Mathf.FloorToInt(frac * resourceCollectingMeshes.Length);
            for (int i = 0; i < resourceCollectingMeshes.Length; i++)
            {
                if (resourceCollectingMeshes[i] != null)
                {
                    resourceCollectingMeshes[i].enabled = i <= numToActivate - 1;
                }
            }
        }
        else
        {
            for (int i = 0; i < resourceCollectingMeshes.Length; i++)
            {
                if (resourceCollectingMeshes[i] != null)
                {
                    resourceCollectingMeshes[i].enabled = false;
                }
            }
        }*/
    }
} 
public enum ResourceType
{
    None,
    Gold,
    Wood,
    Cactus
}