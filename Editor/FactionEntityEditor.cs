using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;


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

    SerializedProperty expandGarrisonOptions;
    SerializedProperty passengersAreTargetable;
    SerializedProperty acceptsHeavy;
    SerializedProperty attackProjectilePrefab;

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

        expandGarrisonOptions = serializedObject.FindProperty("expandGarrisonOptions");
        passengersAreTargetable = serializedObject.FindProperty("passengersAreTargetable");
        acceptsHeavy = serializedObject.FindProperty("acceptsHeavy");
        attackProjectilePrefab = serializedObject.FindProperty("attackProjectilePrefab");
    }

    public override void OnInspectorGUI()
    {
        // add this to render base
        base.OnInspectorGUI();


        serializedObject.Update();
        EditorGUILayout.PropertyField(attackType);
        if (attackType.intValue != 0) //not none
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
        else if (attackType.intValue == 3) //projectile
        { 
            EditorGUILayout.PropertyField(attackProjectilePrefab);
        }
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