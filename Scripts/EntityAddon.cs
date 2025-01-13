using Pathfinding;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static StateMachineController;
public class EntityAddon : NetworkBehaviour
{
    [HideInInspector] public SelectableEntity ent;
    [HideInInspector] public StateMachineController sm;
    [HideInInspector] public UnitAnimator anim;
    [HideInInspector] public Pathfinder pf; 

    public virtual void Awake()
    {
        ent = GetComponent<SelectableEntity>();
        sm = GetComponent<StateMachineController>();
    }
    private void Start()
    { 
        anim = ent.anim;
        pf = ent.pf;
        InitAddon();
    }
    public virtual void InitAddon() { }
    public SelectableEntity GetEntity()
    {
        return ent;
    } 
    public void SwitchState(EntityStates state)
    {
        if (sm != null) sm.SwitchState(state);
    }

    public void SetLastMajorState(EntityStates state)
    {
        if (sm != null) sm.SetLastMajorState(state);
    }
    public bool InRangeOfEntity(SelectableEntity target, float range)
    {
        return ent.InRangeOfEntity(target, range);
    }
}
