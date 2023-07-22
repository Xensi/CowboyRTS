using UnityEngine;

[CreateAssetMenu(fileName = "FactionName", menuName = "Faction/FactionScriptableObject", order = 1)]
public class FactionScriptableObject : ScriptableObject
{ 
    public FactionEntityClass[] entities;
}