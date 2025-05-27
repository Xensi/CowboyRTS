using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

[CustomEditor(typeof(Message), true)]
[CanEditMultipleObjects]
public class MessageEditor : Editor
{
    // this are serialized variables in YourClass
    SerializedProperty messageContents;
    SerializedProperty conditions;
    SerializedProperty mouseInput;
    SerializedProperty entityToCheck;
    SerializedProperty numEntitiesToCheck;
    SerializedProperty playerNeedsToClickToContinue;

    private void OnEnable()
    {
        messageContents = serializedObject.FindProperty("messageContents");
        conditions = serializedObject.FindProperty(nameof(conditions));
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
        EditorGUILayout.PropertyField(conditions);
        for (int i = 0; i < conditions.arraySize; i++)
        {
            SerializedProperty cond = conditions.GetArrayElementAtIndex(i);
            switch (cond.enumNames[cond.enumValueIndex])
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
        }


        EditorGUILayout.PropertyField(playerNeedsToClickToContinue);
        // must be on the end.
        serializedObject.ApplyModifiedProperties();
    }
}