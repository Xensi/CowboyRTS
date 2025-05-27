using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public enum Condition
{
    AutoComplete,
    LevelEntitiesDestroyed,
    SelectedTypeOfFactionEntity,
    ControlsTypeOfFactionEntity,
    MouseInputDetected,
    None

}

[CreateAssetMenu(fileName = "NewMessage", menuName = "Message", order = 0)]
[System.Serializable]
public class Message : ScriptableObject
{
    [TextArea(3, 20)]
    public string messageContents = ""; //string to display as message 
    public Condition[] conditions;
    public int mouseInput = 0;
    public FactionEntity entityToCheck;
    public int numEntitiesToCheck = 1;
    public bool playerNeedsToClickToContinue = false;
}
