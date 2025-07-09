using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Start level on enable. Use to automatically start a level at the end of a cutscene.
/// </summary>
public class AutoStartLevel : MonoBehaviour
{
    [SerializeField] private int levelNum = 1;
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
                LobbyManager.Instance.StartSinglePlayerGame(levelNum);
                break;
            case Behavior.EndCutscene:
                CutsceneManager.Instance.EndCutscene(levelNum);
                break;
            default:
                break;
        }
    }
}
