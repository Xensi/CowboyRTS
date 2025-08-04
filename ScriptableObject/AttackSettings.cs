using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static GameConstants;

[CreateAssetMenu(fileName = "AttackSettings", menuName = "Faction/Attack Settings", order = 0)]

public class AttackSettings : ScriptableObject
{
    //public bool directionalAttack = false;
    //public bool shouldAggressivelySeekEnemies = false;
    public AttackType attackType = AttackType.Instant;
    public sbyte damage = 1;
    public float range = 1;
    public float attackDuration = 1; //How long does it take until the attack is ready again?
    public float impactTime = 0.5f; //When does the attack deal damage/shoot a projectile?
    public float areaOfEffectRadius = 0;
    public Projectile attackProjectilePrefab;
    public float coverToIgnore = 0; //subtract from cover value. Melee should usually fully ignore cover.
                                    //Certain ranged attacks may ignore some cover.
    public bool ShouldIgnoreCover()
    {
        return coverToIgnore >= FullCoverVal;
    }
}

public enum AttackType
{
    None,
    Instant, SelfDestruct,
    Projectile,
    //Gatling, //for gatling gun
}