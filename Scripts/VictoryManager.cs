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
        DestroyAllPlayerUnits
    } 

    [SerializeField] private VictoryCondition victoryCondition = VictoryCondition.DestroyAllPlayerUnits;

    private enum LevelAction
    {
        SendToLevel, //plays the level cutscene first
        RestartLevel,
        SendToMainMenu
    }
    [SerializeField] private LevelAction victoryAction = LevelAction.SendToLevel;

    [SerializeField] private int levelToSwitchTo = 0;

    public string levelGoal = "";
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
        if (victory) return;
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
        victory = true;
        //Debug.Log("YOU WON!");
        ShowVictoryScreen();
    }
    private void ShowVictoryScreen()
    {
        if (conditionalMessage != null)
        {
        }
    }
}
