using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Start level on enable. Use to automatically start a level at the end of a cutscene.
/// </summary>
public class AutoStartLevel : MonoBehaviour
{
    [SerializeField] private Level level;
    public enum Behavior
    {
        LoadLevel,
        EndCutscene,
        LoadNextCutscene
    }
    [SerializeField] private Behavior behavior = Behavior.LoadLevel;
    void OnEnable()
    {
        switch (behavior)
        {
            case Behavior.LoadLevel:
                LevelManager.instance.LoadLevelDuringCutscene(level);
                break;
            case Behavior.EndCutscene:
                CutsceneManager.Instance.EndCutscene(level);
                break;
            default:
                break;
        }
    }
}
