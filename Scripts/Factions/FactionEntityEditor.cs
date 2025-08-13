using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;


#if UNITY_EDITOR
[CustomEditor(typeof(FactionEntity), true)]
[CanEditMultipleObjects]
public class FactionEntityEditor : Editor
{
    private void OnEnable()
    {

    }

    public override void OnInspectorGUI()
    {
        // add this to render base
        base.OnInspectorGUI();


        serializedObject.Update();
        // must be on the end.
        serializedObject.ApplyModifiedProperties();
    }
}
#endif