using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Portal : MonoBehaviour
{
    private SelectableEntity entity;
    // teleports minions to destination
    public Vector3 destination;
    public bool hasLinkedPortal = false;

    private void Start()
    {
        entity = GetComponent<SelectableEntity>();
    }
    private void OnTriggerEnter(Collider other)
    {
        if (hasLinkedPortal == true)
        {
            if (entity.isBuildIndicator == false)
            {
                if (other.gameObject != gameObject)
                {
                    if ((Global.Instance.localPlayer.entityLayer.value & (1 << other.gameObject.layer)) > 0)
                    {
                        SelectableEntity otherEntity = other.GetComponent<SelectableEntity>();
                        if (otherEntity.isBuildIndicator == false)
                        {
                            other.transform.position = destination;
                        }
                    }
                }
            }
        }
    }
}
