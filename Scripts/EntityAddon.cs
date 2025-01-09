using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static StateMachineController;

public class EntityAddon : NetworkBehaviour
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
    public void SwitchState(EntityStates state)
    {
        if (sm != null) sm.SwitchState(state);
    }
}
