using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using static StateMachineController;

public class Pathfinder : EntityAddon
{

    public bool pathStatusValid = false; //when this is true, the current path result is valid
    private bool pathfindingValidationTimerActive = false;
    private float pathfindingValidationTimerDuration = 0.5f;
    /// <summary>
    /// This is a timer that runs for 100 ms if the path status is invalid. The path status can become invalid by the destination
    /// changing. After the timer elapses, the path status will become valid, meaning that the game has had enough time to do path
    /// calculations. This timer is set up in a way so that it can be safely cancelled. It will be cancelled if the attack moving
    /// state is exited.
    /// </summary>
    public async void ValidatePathStatus()
    {
        if (!pathStatusValid && !pathfindingValidationTimerActive) //path status becomes invalid if the destination changes, since we need to recalculate and ensure the
        { //blocked status is correct 
            pathStatusTimerCancellationToken = new CancellationTokenSource();
            pathfindingValidationTimerActive = true;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(pathfindingValidationTimerDuration), pathStatusTimerCancellationToken.Token);
            }
            catch
            {
                //Debug.Log("Timer1 was cancelled!");
                return;
            }
            finally
            {
                pathStatusTimerCancellationToken?.Dispose();
                pathStatusTimerCancellationToken = null;
                pathStatusValid = true;
                pathfindingValidationTimerActive = false;
            }
        }
    }
    public bool PathBlocked()
    {
        return pathStatusValid && pathReachesDestination == PathStatus.Blocked;
    }
    public bool PathReaches()
    {
        return pathStatusValid && pathReachesDestination == PathStatus.Reaches;
    }
    public void SetTargetEnemyAsDestination()
    {
        if (targetEnemy == null) return;
        if (targetEnemy.IsStructure()) //if target is a structure, first move the destination closer to us until it no longer hits obstacle
        {
            SetDestinationIfHighDiff(nudgedTargetEnemyStructurePosition);
        }
        else
        {
            SetDestinationIfHighDiff(targetEnemy.transform.position);
        }
    }

    /// <summary>
    /// Only set destination if there's a significant difference
    /// </summary>
    /// <param name="target"></param>
    public void SetDestinationIfHighDiff(Vector3 target, float threshold = 0.1f)
    {
        Vector3 offset = target - destination;
        if (Vector3.SqrMagnitude(offset) > threshold * threshold)
        {
            //Debug.Log("Setting destination bc diff");
            SetDestination(target);
        }
    }
    /// <summary>
    /// Tells server this minion's destination so it can pathfind there on other clients
    /// </summary>
    /// <param name="position"></param>
    public void SetDestination(Vector3 position)
    {
        //print("setting destination");
        destination = position; //tell server where we're going
        //Debug.Log("Setting destination to " + destination);
        UpdateSetterTargetPosition(); //move pathfinding target
        pathStatusValid = false;
        //ai.SearchPath();
    }
}
