using UnityEngine;

[CreateAssetMenu(fileName = "Level", menuName = "RTS/Level", order = 1)]
public class Level : ScriptableObject
{
    public new string name;
    public string goal;
    public bool hasIntro = false;
}