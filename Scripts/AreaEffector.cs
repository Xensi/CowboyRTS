using System; 
using System.Drawing;
using UnityEngine; 
public class AreaEffector : MonoBehaviour
{
    private readonly float lrWidth = 0.05f;
    [SerializeField] private int subDivs = 64; //how detailed the circle should be
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
    public ParticleSystem particleAura;
    public LineRenderer lr;
    public UnityEngine.Color color;
    private ParticleSystem[] particleSystems;
    private void Start()
    { 
        particleAura = GetComponent<ParticleSystem>();
        particleSystems = GetComponentsInChildren<ParticleSystem>();
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
        lr.enabled = val;
    }
    private void UpdateAura()
    {
        if (lr != null)
        {
            lr.startWidth = lrWidth;
            lr.startColor = color;
            lr.endColor = color;
        }
        if (particleAura != null)
        {
            var shape = particleAura.shape;
            shape.radius = radius;
        }
        Vector3 point = transform.position + new Vector3(0, 0.01f, 0);
        int numPoints = subDivs + 1;
        Vector3[] positions = new Vector3[numPoints]; 
        for (int i = 0; i < numPoints; i++)
        { 
            /* Distance around the circle */
            var radians = 2 * MathF.PI / subDivs * i;

            /* Get the vector direction */
            var vertical = MathF.Sin(radians);
            var horizontal = MathF.Cos(radians);

            var spawnDir = new Vector3(horizontal, 0, vertical);

            /* Get the spawn position */
            var spawnPos = point + spawnDir * radius; // Radius is just the distance away from the point 
            positions[i] = spawnPos;
            Debug.DrawRay(spawnPos, Vector3.up, UnityEngine.Color.red, 1); 
        }  
        if (lr != null)
        {
            lr.positionCount = numPoints;
            lr.SetPositions(positions);
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
                    if (select.sm != null && select.sm.currentState != StateMachineController.EntityStates.Attacking)
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
