using Pathfinding;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static StateMachineController;
public class EntityAddon : NetworkBehaviour
{
    [HideInInspector] public Entity ent;
    [HideInInspector] public StateMachineController sm;
    [HideInInspector] public UnitAnimator anim;
    [HideInInspector] public Pathfinder pf;
    [HideInInspector] public Attacker at;

    public virtual void Awake()
    {
        ent = GetComponent<Entity>();
        sm = GetComponent<StateMachineController>();
        pf = GetComponent<Pathfinder>();
    }
    private void Start()
    { 
        anim = ent.anim;
        if (pf == null) pf = ent.pf;
        at = ent.attacker;
        InitAddon();
    }
    public virtual void InitAddon() { }
    public virtual void UpdateAddon() { }
    public Entity GetEntity()
    {
        return ent;
    } 
    /// <summary>
    /// Switches the state machine controller state.
    /// </summary>
    /// <param name="state"></param>
    public void SwitchState(EntityStates state, bool shouldOverride = false)
    {
        if (sm != null) sm.SwitchState(state, shouldOverride);
    }
    public EntityStates GetState()
    {
        if (sm == null) return EntityStates.Idle;
        return sm.GetState();
    }
    public bool InState(EntityStates state)
    {
        if (sm == null) return false;
        return sm.InState(state);
    }
    public void SetLastMajorState(EntityStates state)
    {
        if (sm != null) sm.SetLastMajorState(state);
    }
    public bool InRangeOfEntity(Entity target, float range)
    {
        return ent.InRangeOfEntity(target, range);
    }
}
