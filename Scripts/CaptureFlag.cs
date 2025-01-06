using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static AIPlayer;

public class CaptureFlag : MonoBehaviour
{
    [SerializeField] private GameObject moveable;
    [SerializeField] private float highestPoint = 8.5f;
    [SerializeField] private float lowestPoint = 0;
    [SerializeField] private float moveablePosition = 8.5f;

    [SerializeField] private GameObject startFlag;

    [SerializeField] private GameObject endFlag;

    [SerializeField] private List<SelectableEntity> watchedEntities = new();
    private int initialWatchedEntities = 0;
    [SerializeField] private List<SelectableEntity> captureEntities = new();
    [SerializeField] private List<SelectableEntity> destroyEntities = new();
    private bool switched = false;
    [SerializeField] private int numAlive = 0;
    private void Start()
    {
        initialWatchedEntities = watchedEntities.Count;
    }
    private void Update()
    {
        SetFlagPosition();
        CheckIfFlagShouldSwitch();
    }
    private void SetFlagPosition()
    {
        if (!switched)
        {
            //get new flag position based on ratio of number of alive watched entities versus initial 
            numAlive = 0;
            foreach (SelectableEntity item in watchedEntities)
            {
                if (item != null && item.alive)
                {
                    numAlive++;
                }
            }

            float t = (float)numAlive / initialWatchedEntities;
            float newYPos = Mathf.Lerp(lowestPoint, highestPoint, t);
            moveablePosition = newYPos;
            Vector3 newVec = new Vector3(0, newYPos, 0);
            moveable.transform.SetLocalPositionAndRotation(newVec, Quaternion.identity);
        }
        else
        {
            moveablePosition = Mathf.Clamp(moveablePosition + Time.deltaTime * 3, lowestPoint, highestPoint);
            Vector3 newVec = new Vector3(0, moveablePosition, 0);
            moveable.transform.SetLocalPositionAndRotation(newVec, Quaternion.identity);
        }
    }
    private void CheckIfFlagShouldSwitch()
    {
        if (switched) return;
        bool shouldSwitch = true;
        foreach (SelectableEntity item in watchedEntities)
        {
            if (item != null && item.alive)
            {
                shouldSwitch = false;
                break;
            }
        }
        if (shouldSwitch)
        {
            Switch();
        }
    }
    private void Switch()
    {
        switched = true;
        if (endFlag != null) endFlag.SetActive(true);
        if (startFlag != null) startFlag.SetActive(false);
        EffectEntities();
    }
    private void EffectEntities()
    {
        foreach (SelectableEntity item in captureEntities)
        {
            if (item != null && item.controllerOfThis != Global.Instance.localPlayer)
            {
                item.CaptureForLocalPlayer();
            }
        }

        foreach (SelectableEntity item in destroyEntities)
        {
            if (item != null)
            {
                item.DestroyThis();
            }
        }
    }
}
