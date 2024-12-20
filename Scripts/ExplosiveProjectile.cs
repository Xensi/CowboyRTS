using UnityEngine;  
public class ExplosiveProjectile : Projectile
{  
    public float explosionRadius = 2;

    public override void Start()
    {
        base.Start();
    }

    public override void Update()
    {
        base.Update();
    }
    public override void ArrivalEffect()
    { 
        if (isLocal) //the player who fired the explosion will do this, for everyone else it is purely cosmetic.
        { // if other players did this, the damage would be multiplied erroneously
            Global.Instance.localPlayer.CreateExplosionAtPoint(transform.position, explosionRadius, (sbyte)damage);
        }
        Global.Instance.localPlayer.SpawnExplosion(transform.position); //all players play cosmetic explosion locally
        Global.Instance.PlayClipAtPoint(Global.Instance.explosion, transform.position, 0.25f);
        Destroy(gameObject);
    }

}
