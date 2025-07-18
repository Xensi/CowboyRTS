using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;


[CustomEditor(typeof(FactionEntity), true)]
[CanEditMultipleObjects]
public class FactionEntityEditor : Editor
{
    SerializedProperty isHarvester;
    SerializedProperty harvestCapacity;
    SerializedProperty depositRange; 

    SerializedProperty expandGarrisonOptions;
    SerializedProperty passengersAreTargetable;
    SerializedProperty acceptsHeavy;

    private void OnEnable()
    {

        isHarvester = serializedObject.FindProperty("isHarvester");
        harvestCapacity = serializedObject.FindProperty("harvestCapacity");
        depositRange = serializedObject.FindProperty("depositRange"); 

        expandGarrisonOptions = serializedObject.FindProperty("expandGarrisonOptions");
        passengersAreTargetable = serializedObject.FindProperty("passengersAreTargetable");
        acceptsHeavy = serializedObject.FindProperty("acceptsHeavy");
    }

    public override void OnInspectorGUI()
    {
        // add this to render base
        base.OnInspectorGUI();


        serializedObject.Update();
        EditorGUILayout.PropertyField(isHarvester);
        if (isHarvester.boolValue == true)
        { 
            EditorGUILayout.PropertyField(harvestCapacity);
            EditorGUILayout.PropertyField(depositRange);
        } 
        EditorGUILayout.PropertyField(expandGarrisonOptions);
        if (expandGarrisonOptions.boolValue == true)
        { 
            EditorGUILayout.PropertyField(passengersAreTargetable);
            EditorGUILayout.PropertyField(acceptsHeavy);
        }

        // must be on the end.
        serializedObject.ApplyModifiedProperties();
    }
}