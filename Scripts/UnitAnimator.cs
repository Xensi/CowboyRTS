using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StateMachineController;

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
    string currentAnim = "";
    public void ResetMultiplier(string str)
    {
        animator.SetFloat(str, 1);
    }

    public override void Awake()
    {
        base.Awake();
        animator = GetComponentInChildren<Animator>();
    }
    public void Play(string str) //, float fadeLength = 3f
    {
        if (animator != null)
        {
            if (currentAnim != str)
            {
                //animator.CrossFadeInFixedTime(str, fadeLength);
                animator.Play(str);
                currentAnim = str;
            }
        }
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
    public void IdleOrWalkContextuallyAnimationOnly()
    {
        /*if (pf == null) return;
        float limit = pf.changeThreshold;
        if (pf.change < limit * limit && pf.walkStartTimer <= 0 || pf.ai.reachedDestination) //basicallyIdleInstances > idleThreshold
        {
            Play(IDLE); 
        }
        else
        {
            Play(WALK); 
        }*/
        Play(WALK);
    }
    /// <summary>
    /// Handles code for playing animations for certain states that loop a single animation.
    /// </summary>
    public void StateBasedAnimations()
    {
        switch (sm.currentState)
        {
            case EntityStates.Idle:
            case EntityStates.FindInteractable:
                Play(IDLE);
                break;
            case EntityStates.Walk:
            case EntityStates.WalkToSpecificEnemy:
            case EntityStates.WalkToInteractable:
            case EntityStates.WalkToRally:
            case EntityStates.WalkToTarget:
                Play(WALK);
                //IdleOrWalkContextuallyAnimationOnly();
                break;  
            case EntityStates.Spawn:
                Play(SPAWN);
                break;
            case EntityStates.Die:
                Play(DIE);
                break;     
            case EntityStates.UsingAbility:
                Play(USE_ABILITY);
                break;
            default:
                break;
        }
    }
}
