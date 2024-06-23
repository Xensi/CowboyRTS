using UnityEngine;
[CreateAssetMenu(fileName = "NewUnitSoundProfile", menuName = "Faction/SoundProfile", order = 0)]
[System.Serializable]
public class UnitSoundsProfile: ScriptableObject
{
    public AudioClip[] sounds; 
}