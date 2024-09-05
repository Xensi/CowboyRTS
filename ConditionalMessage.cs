using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class ConditionalMessage : MonoBehaviour
{
    [SerializeField] private List<GameObject> messages;
    [SerializeField] private GameObject bg;

    private void Start()
    {
        ShowMessage(0);
    }
    //select grave msg
    private bool trainUnitMsgShown = false; //train numbskull
    private bool selectUnitMsgShown = false; //select numbskull unit
    private bool movementMsgShown = false;
    private bool attackSpecificMsgShown = false;
    private void Update()
    {
        if (!trainUnitMsgShown && Global.Instance.localPlayer.selectedEntities.Count > 0)
        {
            trainUnitMsgShown = true;
            ShowMessage(1);
        }
        if (!selectUnitMsgShown && Global.Instance.localPlayer.ownedEntities.Count > 1)
        {
            selectUnitMsgShown = true;
            ShowMessage(2);
        }
        if (!movementMsgShown && Global.Instance.localPlayer.selectedEntities.Count > 0 && Global.Instance.localPlayer.selectedEntities[0].IsMinion())
        {
            movementMsgShown = true;
            ShowMessage(3);
        }
        if (!attackSpecificMsgShown && Input.GetMouseButtonDown(1)) //right clicked
        {
            attackSpecificMsgShown = true;
            ShowMessage(4);
        }
    }
    private void ShowMessage(int id = 0)
    {
        bg.SetActive(true);
        for (int i = 0; i < messages.Count; i++)
        {
            messages[i].SetActive(i == id);
        }
    }
    private void HideMessage()
    {
        bg.SetActive(false);
    }
}
