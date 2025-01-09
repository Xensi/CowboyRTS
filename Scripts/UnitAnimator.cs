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
    private Animator animator;

    public override void Awake()
    {
        base.Awake();
        animator = GetComponentInChildren<Animator>();
    }
    public void PlayAnimation(string str)
    {
        if (animator != null) animator.Play(str);
    }
    public bool AnimInProgress()
    {
        return animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1;
    }
}
