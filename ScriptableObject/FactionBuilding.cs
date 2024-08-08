using UnityEngine;
[CreateAssetMenu(fileName = "NewBuilding", menuName = "Faction/Building", order = 0)]
[System.Serializable]
public class FactionBuilding : FactionEntity
{
    [Header ("Building Properties")]
    public bool needsConstructing = true;
    public Vector3 buildOffset = new Vector3(0.5f, 0, 0.5f);
    public bool extendable = false; //should this building be placed in a line?
    public bool rotatable = false;
    //public GameObject linkedPrefab;

    public enum BuildingTypes
    {
        Generic, //default  
    }
}
