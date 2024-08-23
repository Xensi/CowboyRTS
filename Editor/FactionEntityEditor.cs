using UnityEditor;
using UnityEngine;


[CustomEditor(typeof(FactionEntity), true)]
[CanEditMultipleObjects]
public class FactionEntityEditor : Editor
{
    // this are serialized variables in YourClass
    SerializedProperty attackType;
    SerializedProperty damage;
    SerializedProperty directionalAttack;
    SerializedProperty attackRange; 
    SerializedProperty attackDuration;
    SerializedProperty impactTime;
    SerializedProperty areaOfEffectRadius;
    SerializedProperty shouldAggressivelySeekEnemies;

    SerializedProperty isHarvester;
    SerializedProperty harvestCapacity;
    SerializedProperty depositRange;

    SerializedProperty spawnableUnits;
    SerializedProperty spawnableAtOnce;

    SerializedProperty expandGarrisonOptions;
    SerializedProperty passengersAreTargetable;
    SerializedProperty acceptsHeavy;

    private void OnEnable()
    {
        attackType = serializedObject.FindProperty("attackType");
        damage = serializedObject.FindProperty("damage");
        directionalAttack = serializedObject.FindProperty("directionalAttack");
        attackRange = serializedObject.FindProperty("attackRange");
        attackDuration = serializedObject.FindProperty("attackDuration");
        impactTime = serializedObject.FindProperty("impactTime");
        areaOfEffectRadius = serializedObject.FindProperty("areaOfEffectRadius");
        shouldAggressivelySeekEnemies = serializedObject.FindProperty("shouldAggressivelySeekEnemies");


        isHarvester = serializedObject.FindProperty("isHarvester");
        harvestCapacity = serializedObject.FindProperty("harvestCapacity");
        depositRange = serializedObject.FindProperty("depositRange");

        spawnableUnits = serializedObject.FindProperty("spawnableUnits");
        spawnableAtOnce = serializedObject.FindProperty("spawnableAtOnce");

        expandGarrisonOptions = serializedObject.FindProperty("expandGarrisonOptions");
        passengersAreTargetable = serializedObject.FindProperty("passengersAreTargetable");
        acceptsHeavy = serializedObject.FindProperty("acceptsHeavy");
    }

    public override void OnInspectorGUI()
    {
        // add this to render base
        base.OnInspectorGUI();


        serializedObject.Update();
        EditorGUILayout.PropertyField(attackType);
        if (attackType.intValue != 0)
        {
            EditorGUILayout.PropertyField(damage);
            EditorGUILayout.PropertyField(directionalAttack);
            EditorGUILayout.PropertyField(attackRange);
            EditorGUILayout.PropertyField(attackDuration);
            EditorGUILayout.PropertyField(impactTime);
            EditorGUILayout.PropertyField(shouldAggressivelySeekEnemies);
        }
        if (attackType.intValue == 2) //self destruct 
        { 
            EditorGUILayout.PropertyField(areaOfEffectRadius);
        }
        EditorGUILayout.PropertyField(isHarvester);
        if (isHarvester.boolValue == true)
        { 
            EditorGUILayout.PropertyField(harvestCapacity);
            EditorGUILayout.PropertyField(depositRange);
        }
        EditorGUILayout.PropertyField(spawnableUnits);
        if (spawnableUnits.arraySize > 0)
        {
            EditorGUILayout.PropertyField(spawnableAtOnce);
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