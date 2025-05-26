using System; 
using System.Drawing;
using UnityEngine;
using static StateMachineController;
public class AreaEffector : MonoBehaviour
{
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
    public ParticleSystem particleAura;
    private ParticleSystem[] particleSystems;
    private DisplayRadius dr;
    public float radius = 1;
    private void Start()
    { 
        particleAura = GetComponent<ParticleSystem>();
        particleSystems = GetComponentsInChildren<ParticleSystem>();
        dr = GetComponent<DisplayRadius>();
        if (dr != null)
        {
            dr.radius = radius;
            dr.SetColor(UnityEngine.Color.green);
        }
    }
    private void Update()
    {
        timer += Time.deltaTime;
        if (timer >= timeToApply)
        {
            timer = 0;
            ApplyEffect();
        }
        UpdateAura();
    }

    private int maxArraySize = 50;
    public void UpdateVisibility(bool val)
    { 
        for (int i = 0; i < particleSystems.Length; i++)
        {
            if (particleSystems[i] != null)
            {
                if (val)
                {
                    if (!particleSystems[i].isPlaying) particleSystems[i].Play();
                }
                else
                {
                    if (particleSystems[i].isPlaying)
                    {
                        particleSystems[i].Stop();
                        particleSystems[i].Clear();
                    }
                }
                /*var emission = particleSystems[i].emission;
                emission.enabled = val;*/
            } 
        }
        if (dr != null) dr.SetLREnable(val);
    }
    private void UpdateAura()
    {
        if (dr != null) dr.UpdateLR();

        if (particleAura != null)
        {
            var shape = particleAura.shape;
            shape.radius = radius;
        }
    }
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
                    if (select.sm != null && !select.sm.InState(EntityStates.Attacking))
                    {
                        select.RaiseHP((sbyte)effectNumber);
                    }
                    break;
                default:
                    break;
            }
        }
    }
}
