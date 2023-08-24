using UnityEngine;  
public class Projectile : MonoBehaviour
{
    public float speed = 1;
    public Vector3 target;
    public float arcHeight = 10;

    Vector3 _startPosition;
    float _stepScale;
    float _progress;

    public float explosionRadius = 2;
    public bool isLocal = true;

    void Start()
    {
        _startPosition = transform.position;

        float distance = Vector3.Distance(_startPosition, target);

        // This is one divided by the total flight duration, to help convert it to 0-1 progress.
        _stepScale = speed / distance;
    }

    void Update()
    {
        // Increment our progress from 0 at the start, to 1 when we arrive.
        _progress = Mathf.Min(_progress + Time.deltaTime * _stepScale, 1.0f);

        // Turn this 0-1 value into a parabola that goes from 0 to 1, then back to 0.
        float parabola = 1.0f - 4.0f * (_progress - 0.5f) * (_progress - 0.5f);

        // Travel in a straight line from our start position to the target.        
        Vector3 nextPos = Vector3.Lerp(_startPosition, target, _progress);

        // Then add a vertical arc in excess of this.
        nextPos.y += parabola * arcHeight;

        // Continue as before.
        transform.LookAt(nextPos, transform.forward);
        transform.position = nextPos;

        // I presume you disable/destroy the arrow in Arrived so it doesn't keep arriving.
        if (_progress == 1.0f)
            Arrived();
    }
    private void Arrived()
    {
        if (isLocal) //the player who fired the explosion will do this, for everyone else it is purely cosmetic.
        { // if other players did this, the damage would be multiplied erroneously
            Global.Instance.localPlayer.CreateExplosionAtPoint(transform.position, explosionRadius);
        }
        Global.Instance.localPlayer.SpawnExplosion(transform.position); //all players play cosmetic explosion locally
        Global.Instance.PlayClipAtPoint(Global.Instance.explosion, transform.position, 0.25f);
        Destroy(gameObject);
    }

}
