using UnityEngine;
public class Projectile : MonoBehaviour
{
    public float speed = 1;
    [HideInInspector] public Vector3 groundTarget;
    public bool shouldHomeOnEntity = false;
    [HideInInspector] public SelectableEntity entityToHomeOnto;
    public float maxArcHeight = 10;
    [HideInInspector] public float actualArcHeight;
    [HideInInspector] public float firingUnitAttackRange = 4;

    Vector3 _startPosition;
    float _stepScale;
    float _progress; 
    public bool isLocal = true;
    [HideInInspector] public bool arrivedAtTarget = false;
    //private Vector3 actualTarget;
    public int damage = 1;
    public bool spinContinously = false;
    public Vector3 spinRotation;
    public AudioClip arrivalAudio;
    public GameObject arrivalParticlesPrefab;
    public virtual void Start()
    {
        //EvaluateActualTarget();
        _startPosition = transform.position;

        float distance = Vector3.Distance(_startPosition, groundTarget);
        float ratio = distance/firingUnitAttackRange;
        actualArcHeight = maxArcHeight * ratio - 0.1f/ratio; //linearizes the projectile if the ratio is very low

        // This is one divided by the total flight duration, to help convert it to 0-1 progress.
        _stepScale = speed / distance;

        transform.LookAt(groundTarget, transform.forward);
    }
    /*private void EvaluateActualTarget()
    { 
        shouldHomeOnEntity = entityToHomeOnto != null; 
        if (shouldHomeOnEntity)
        {
            actualTarget = entityToHomeOnto.transform.position;
        }
        else
        {
            actualTarget = groundTarget;
        }
    }*/
    public virtual void Update()
    {
        //EvaluateActualTarget();
        // Increment our progress from 0 at the start, to 1 when we arrive.
        _progress = Mathf.Min(_progress + Time.deltaTime * _stepScale, 1.0f);

        // Turn this 0-1 value into a parabola that goes from 0 to 1, then back to 0.
        float parabola = 1.0f - 4.0f * (_progress - 0.5f) * (_progress - 0.5f);

        // Travel in a straight line from our start position to the target.        
        Vector3 nextPos = Vector3.Lerp(_startPosition, groundTarget, _progress);

        // Then add a vertical arc in excess of this.
        nextPos.y += parabola * actualArcHeight;

        // Continue as before.
        if (spinContinously)
        {
            transform.Rotate(spinRotation * Time.deltaTime);
        }
        else
        { 
            transform.LookAt(nextPos, transform.forward);
        }
        transform.position = nextPos;

        // I presume you disable/destroy the arrow in Arrived so it doesn't keep arriving.
        if (_progress == 1.0f && !arrivedAtTarget)
        {
            arrivedAtTarget = true;
            ArrivalEffect();
        }
    }
    public virtual void ArrivalEffect()
    {
        //Debug.Log("Arrived!");
        if (isLocal)
        {
            if (entityToHomeOnto != null)
            {
                Global.Instance.localPlayer.DamageEntity((sbyte)damage, entityToHomeOnto);
            }
        }
        if (arrivalAudio != null) Global.Instance.PlayClipAtPoint(arrivalAudio, transform.position, 0.1f);
        /*if (isLocal) //the player who fired the explosion will do this, for everyone else it is purely cosmetic.
        { // if other players did this, the damage would be multiplied erroneously
            Global.Instance.localPlayer.CreateExplosionAtPoint(transform.position, explosionRadius);
        }
        Global.Instance.localPlayer.SpawnExplosion(transform.position); //all players play cosmetic explosion locally
        Global.Instance.PlayClipAtPoint(Global.Instance.explosion, transform.position, 0.25f);
        */
        Instantiate(arrivalParticlesPrefab, transform.position, Quaternion.identity);
        Destroy(gameObject);
    } 

}
