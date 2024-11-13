using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AreaEffector : MonoBehaviour
{
    public float radius = 1;
    public int effectNumber = 1;

    public float timeToApply = 1;
    private float timer = 0;
    public enum EffectToApply
    {
        None, HealNonAttackers
    }

    public enum TeamToApplyEffectTo
    {
        AlliedTeams, EnemyTeams
    }

    public EffectToApply effect = EffectToApply.None;
    public TeamToApplyEffectTo team = TeamToApplyEffectTo.AlliedTeams;

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer >= timeToApply)
        {
            timer = 0;
            ApplyEffect();
        }
    }

    private int maxArraySize = 50;
    private void ApplyEffect()
    {
        Collider[] array = new Collider[maxArraySize];
        LayerMask mask = Global.Instance.friendlyEntityLayer;
        switch (team)
        {
            case TeamToApplyEffectTo.AlliedTeams: 
                if (gameObject.layer == LayerMask.NameToLayer("Entity"))
                { 
                    mask = Global.Instance.friendlyEntityLayer;
                }
                else
                { 
                    mask = Global.Instance.enemyLayer;
                }
                break;
            case TeamToApplyEffectTo.EnemyTeams:
                if (gameObject.layer == LayerMask.NameToLayer("Entity"))
                {
                    mask = Global.Instance.enemyLayer;
                }
                else
                {
                    mask = Global.Instance.friendlyEntityLayer;
                }
                break;
            default:
                break;
        }
        int searchedCount = Physics.OverlapSphereNonAlloc(transform.position, radius, array, mask);

        for (int i = 0; i < searchedCount; i++)
        {
            if (array[i] == null) continue;
            SelectableEntity select = array[i].GetComponent<SelectableEntity>();
            if (select == null) continue;
            if (!select.alive || !select.isTargetable.Value)
            {
                continue;
            }
            switch (effect)
            {
                case EffectToApply.None:
                    break;
                case EffectToApply.HealNonAttackers:
                    if (select.minionController != null && select.minionController.minionState != MinionController.MinionStates.Attacking)
                    {
                        select.RaiseHP((sbyte)effectNumber);
                    }
                    break;
                default:
                    break;
            }
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(transform.position, radius); 
    }
}
