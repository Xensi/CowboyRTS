using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VictoryManager : MonoBehaviour
{
    public static VictoryManager Instance { get; private set; }

    [SerializeField] private List<Player> watchedPlayers = new(); //players to be watched

    private enum VictoryCondition
    {
        EliminateAllMinionsControlledByWatchedPlayer
    } 

    [SerializeField] private VictoryCondition victoryCondition = VictoryCondition.EliminateAllMinionsControlledByWatchedPlayer;
    public string levelGoal = "";
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
    }
    private void Update()
    {
        EvaluateVictoryConditions();
    }
    private void EvaluateVictoryConditions()
    {
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
        Debug.Log("YOU WON!");
    }

}
