using UnityEngine;
[CreateAssetMenu(fileName = "NewBuilding", menuName = "Faction/Building", order = 0)]
[System.Serializable]
public class FactionBuilding : FactionEntity
{
    public bool needsConstructing = true;
    public Vector3 buildOffset;
    //public GameObject linkedPrefab;
}
