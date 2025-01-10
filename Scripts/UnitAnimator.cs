using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles setting animations and names of unit animation states
/// </summary>
public class UnitAnimator : EntityAddon
{
    public static string IDLE = "Idle";
    public static string HARVEST = "Harvest";
    public static string ATTACK = "Attack";
    public static string SPAWN = "Spawn";
    public static string WALK = "Walk";
    public static string DIE = "Die";
    public static string BEGIN_ATTACK_WALK = "AttackWalkStart";
    public static string CONTINUE_ATTACK_WALK = "AttackWalk";
    public static string USE_ABILITY = "UseAbility";

    public static string ATTACK_SPEED = "attackSpeedMultiplier";
    public static string MOVE_SPEED = "moveSpeedMultiplier";
    private Animator animator;
    
    public void ResetMultiplier(string str)
    {
        animator.SetFloat(str, 1);
    }

    public override void Awake()
    {
        base.Awake();
        animator = GetComponentInChildren<Animator>();
    }
    public void Play(string str)
    {
        if (animator != null) animator.Play(str);
    }
    public bool InState(string str)
    {
        return animator.GetCurrentAnimatorStateInfo(0).IsName(str);
    }
    /// <summary>
    /// Is the animator currently in the middle of playing a nonlooping animation?
    /// Normalized time >= 1 means the anim has completed once
    /// </summary>
    /// <returns></returns>
    public bool InProgress()
    {
        return animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1;
    }
}
