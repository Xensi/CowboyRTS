using UnityEditor;
using UnityEngine;

public enum Condition
{
    None,
    LevelEntitiesDestroyed,
    SelectedTypeOfFactionEntity,
    ControlsTypeOfFactionEntity,
    MouseInputDetected
}

[CreateAssetMenu(fileName = "NewMessage", menuName = "Message", order = 0)]
[System.Serializable]
public class Message : ScriptableObject
{
    [TextArea(3, 20)]
    public string messageContents = ""; //string to display as message 
    public Condition condition;
    public int mouseInput = 0;
    public FactionEntity entityToCheck;
    public int numEntitiesToCheck = 1;
    public bool playerNeedsToClickToContinue = false;
}

[CustomEditor(typeof(Message), true)]
[CanEditMultipleObjects]
public class MessageEditor : Editor
{
    // this are serialized variables in YourClass
    SerializedProperty messageContents;
    SerializedProperty condition;
    SerializedProperty mouseInput;
    SerializedProperty entityToCheck;
    SerializedProperty numEntitiesToCheck;
    SerializedProperty playerNeedsToClickToContinue;

    private void OnEnable()
    {
        messageContents = serializedObject.FindProperty("messageContents");
        condition = serializedObject.FindProperty("condition");
        mouseInput = serializedObject.FindProperty("mouseInput");
        entityToCheck = serializedObject.FindProperty("entityToCheck");
        numEntitiesToCheck = serializedObject.FindProperty(nameof(numEntitiesToCheck));
        playerNeedsToClickToContinue = serializedObject.FindProperty("playerNeedsToClickToContinue");
    } 
    public override void OnInspectorGUI()
    {
        // add this to render base
        //base.OnInspectorGUI(); 
        serializedObject.Update();

        EditorGUILayout.PropertyField(messageContents);
        EditorGUILayout.PropertyField(condition);
        switch (condition.enumNames[condition.enumValueIndex])
        {
            case nameof(Condition.MouseInputDetected):
                EditorGUILayout.PropertyField(mouseInput);
                break;
            case nameof(Condition.ControlsTypeOfFactionEntity):
            case nameof(Condition.SelectedTypeOfFactionEntity):
                EditorGUILayout.PropertyField(entityToCheck);
                EditorGUILayout.PropertyField(numEntitiesToCheck); 
                break;
            default:
                break;
        }

        EditorGUILayout.PropertyField(playerNeedsToClickToContinue);
        // must be on the end.
        serializedObject.ApplyModifiedProperties();
    }
}