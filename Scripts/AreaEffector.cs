using System.Collections.Generic;
using UnityEngine;
using static StateMachineController;
public class AreaEffector : MonoBehaviour
{
    //public int effectNumber = 1;
    public float timeToApply = 1;
    private float timer = 0;
    public enum TeamToApplyEffectTo
    {
        AlliedTeams, EnemyTeams
    }

    public List<Effect> effects;
    public TeamToApplyEffectTo team = TeamToApplyEffectTo.AlliedTeams;
    
    public enum ApplyTypes
    {
        All, Minions, Structures
    }
    public ApplyTypes typesToApplyEffectTo = ApplyTypes.All;

    public ParticleSystem particleAura;
    private ParticleSystem[] particleSystems;
    private DisplayRadius dr;
    public float radius = 1;
    [SerializeField] private SelectableEntity ent;
    [SerializeField] private bool applyToSelf = false;
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
        LayerMask searchMask = Global.instance.friendlyEntityLayer;
        switch (team)
        {
            case TeamToApplyEffectTo.AlliedTeams:
                if (ent.GetAllegiance() == 0)
                {
                    searchMask = Global.instance.friendlyEntityLayer;
                }
                else
                {
                    searchMask = Global.instance.enemyLayer;
                }
                break;
            case TeamToApplyEffectTo.EnemyTeams:
                if (ent.GetAllegiance() == 0)
                {
                    searchMask = Global.instance.enemyLayer;
                }
                else
                {
                    searchMask = Global.instance.friendlyEntityLayer;
                }
                break;
            default:
                break;
        }
        int searchedCount = Physics.OverlapSphereNonAlloc(transform.position, radius, array, searchMask);

        for (int i = 0; i < searchedCount; i++)
        {
            if (array[i] == null) continue;
            SelectableEntity select = array[i].GetComponent<SelectableEntity>();
            if (select == null || !select.alive || !select.isTargetable.Value) continue;
            if (ent != null && !applyToSelf && select == ent)
            {
                continue;
            }
            switch (typesToApplyEffectTo)
            {
                case ApplyTypes.All:
                    break;
                case ApplyTypes.Minions:
                    if (!select.IsMinion()) continue;
                    break;
                case ApplyTypes.Structures:
                    if (!select.IsStructure()) continue;
                    break;
                default:
                    break;
            }
            foreach (Effect effect in effects)
            {
                Effect newEffect = new() //NECESSARY to prevent modifying original class
                {
                    status = effect.status,
                    expirationTime = effect.expirationTime,
                    operation = effect.operation,
                    statusNumber = effect.statusNumber,
                    repeatTime = effect.repeatTime,
                    repeatWhileLingering = effect.repeatWhileLingering,
                    particles = effect.particles,
                };
                select.ApplyEffect(newEffect);
            }
        }
    }
}
