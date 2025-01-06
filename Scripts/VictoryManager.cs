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
        EliminateAllMinionsControlledByWatchedPlayer
    } 

    [SerializeField] private VictoryCondition victoryCondition = VictoryCondition.EliminateAllMinionsControlledByWatchedPlayer;
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
                    case VictoryCondition.EliminateAllMinionsControlledByWatchedPlayer:
                        foreach (MinionController minion in player.ownedMinions)
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
        if (conditionalMessage != null)
        {

        }
    }

}
