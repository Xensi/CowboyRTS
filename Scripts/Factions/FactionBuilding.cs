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

    public float coverVal = 0;

    [Header("AI Only")]
    public bool isSpawner = false;
    public bool isPopAdder = false;

    public bool IsPartialCover()
    {
        return coverVal < 1;
    }
    public bool IsNotCover()
    {
        return coverVal <= 0;
    }
}
