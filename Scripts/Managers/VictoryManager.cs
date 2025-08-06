using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VictoryManager : MonoBehaviour
{
    public static VictoryManager Instance { get; private set; }

    [SerializeField] private List<Player> watchedPlayers = new(); //players to be watched
    private ConditionalMessage conditionalMessage;

    private enum VictoryCondition
    {
        None,
        DestroyAllPlayerUnits,
    } 

    [SerializeField] private VictoryCondition victoryCondition = VictoryCondition.DestroyAllPlayerUnits;

    private enum LevelAction
    {
        None,
        SendToLevel, //plays the level cutscene first
        RestartLevel,
        SendToMainMenu
    }
    [SerializeField] private LevelAction victoryAction = LevelAction.SendToLevel;

    [SerializeField] private Level levelToSwitchTo;
    private bool victory = false;
    private void Awake()
    { 
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
        conditionalMessage = GetComponent<ConditionalMessage>();
    }
    private void Update()
    {
        EvaluateVictoryConditions();
    }
    private void EvaluateVictoryConditions()
    {
        if (victory || !LevelManager.instance.LevelStarted()) return;
        bool victoryAchieved = true;
        foreach (Player player in watchedPlayers)
        {
            if (player != null)
            {
                switch (victoryCondition)
                {
                    case VictoryCondition.DestroyAllPlayerUnits:
                        foreach (StateMachineController minion in player.ownedMinions)
                        {
                            if (minion != null)
                            {
                                if (minion.IsAlive())
                                {
                                    victoryAchieved = false;
                                    break;
                                }
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
        }
        if (victoryAchieved) VictoryAchieved();
    }
    private void VictoryAchieved()
    {
        //Debug.Log("YOU WON!");
        //ShowVictoryScreen();
        VictoryAction();
    }
    private void ShowVictoryScreen()
    {
        if (conditionalMessage != null)
        {
        }
    }
    private void VictoryAction()
    {
        if (victory) return;
        victory = true;
        Debug.Log("Victory action");
        switch (victoryAction)
        {
            case LevelAction.SendToLevel:
                LevelManager.instance.GeneralLoadLevel(levelToSwitchTo);
                break;
            case LevelAction.RestartLevel:
                break;
            case LevelAction.SendToMainMenu:
                break;
            default:
                break;
        }
    }
}
