using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;


#if UNITY_EDITOR
[CustomEditor(typeof(FactionOre), true)]
[CanEditMultipleObjects]
public class FactionOreEditor : Editor
{
    override public void OnInspectorGUI()
    {
        var ore = target as FactionOre;

        EditorGUILayout.PrefixLabel("Name");
        ore.productionName = EditorGUILayout.TextField(ore.productionName);

        EditorGUILayout.PrefixLabel("Description");
        ore.description = EditorGUILayout.TextField(ore.description);

        EditorGUILayout.PrefixLabel("HP");
        ore.maxHP = EditorGUILayout.IntField(ore.maxHP);

        ore.deathEffect = (GameObject)EditorGUILayout.ObjectField("Death Effect", ore.deathEffect, typeof(GameObject), false);

        EditorGUILayout.PrefixLabel("Max Harvesters");
        ore.maxHarvesters = EditorGUILayout.IntField(ore.maxHarvesters);
    }
}
#endif