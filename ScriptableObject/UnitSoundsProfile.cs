using UnityEngine;
[CreateAssetMenu(fileName = "NewUnitSoundProfile", menuName = "Faction/SoundProfile", order = 0)]
[System.Serializable]
public class UnitSoundsProfile: ScriptableObject
{
    public AudioClip[] sounds;

    public AudioClip spawnSound;

    public AudioClip selectSound;

    public AudioClip hitSound;

    public AudioClip missSound;

    public AudioClip attackMoveSound;

    public AudioClip abilitySound;
}
public enum SoundTypes
{
    SpawnSound,
    SelectSound,
    HitSound,
    MissSound,
    AttackMoveSound,
    AbilitySound,
}