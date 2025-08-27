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
    public static string COVER = "cover";
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
                //animator.Play(str);
                animator.CrossFade(str, 0.25f);
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
    private float walkChangeAnimThreshold = 0.001f;
    public void IdleOrWalkContextuallyAnimationOnly()
    {
        if (pf == null) return;
        float limit = walkChangeAnimThreshold;
        if (pf.sqrDistChange < limit * limit && pf.walkStartTimer <= 0 || pf.ai.reachedDestination)
        {
            Play(IDLE);
        }
        else
        {
            Play(WALK);
        }
        //Play(WALK);
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
            case EntityStates.PushableIdle:
                IdleOrWalkContextuallyAnimationOnly();
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
    private float coverVal = 0;
    private bool currentCoverBool = false;
    private float t = 0;
    private float transitionToCoverSpeed = 1.25f;
    public void UpdateCoverVal(bool inCover)
    {
        if (inCover != currentCoverBool)
        {
            currentCoverBool = inCover;
            t = 0;
        }
        if (t < 1) t += Time.deltaTime * transitionToCoverSpeed;
        float newVal = inCover ? 1 : 0;

        coverVal = Mathf.Lerp(coverVal, newVal, t);

        animator.SetFloat(COVER, coverVal);
    }
}
