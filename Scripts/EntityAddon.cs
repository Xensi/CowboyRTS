using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EntityAddon : MonoBehaviour
{
    [HideInInspector] public SelectableEntity ent;
    [HideInInspector] public StateMachineController sm;
    public virtual void Awake()
    {
        ent = GetComponent<SelectableEntity>();
        sm = GetComponent<StateMachineController>();
    }
    public SelectableEntity GetEntity()
    {
        return ent;
    }
}
