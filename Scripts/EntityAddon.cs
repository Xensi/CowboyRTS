using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static StateMachineController;
public class EntityAddon : NetworkBehaviour
{
    [HideInInspector] public SelectableEntity ent;
    [HideInInspector] public StateMachineController sm;

    [HideInInspector] public bool ready = true;
    [HideInInspector] public float readyTimer = 0;
    [HideInInspector] public float range = 1;
    [HideInInspector] public float impactTime = 1;
    [HideInInspector] public sbyte delta = 1; //How much to change variables by
    [HideInInspector] public float duration = 1;
    [HideInInspector] public UnitAnimator anim;
    [HideInInspector] public Pathfinder pf;

    public virtual void Awake()
    {
        ent = GetComponent<SelectableEntity>();
        sm = GetComponent<StateMachineController>();
        anim = ent.unitAnimator;
        pf = ent.pf;
    }
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
