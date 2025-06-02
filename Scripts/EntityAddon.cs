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
    [HideInInspector] public Attacker at;

    public virtual void Awake()
    {
        ent = GetComponent<SelectableEntity>();
        sm = GetComponent<StateMachineController>();
    }
    private void Start()
    { 
        anim = ent.anim;
        pf = ent.pf;
        at = ent.attacker;
        InitAddon();
    }
    public virtual void InitAddon() { }
    public virtual void UpdateAddon() { }
    public SelectableEntity GetEntity()
    {
        return ent;
    } 
    /// <summary>
    /// Switches the state machine controller state.
    /// </summary>
    /// <param name="state"></param>
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
