using System.Collections;
using System.Collections.Generic;
using TMPro; 
using UnityEditor;
using UnityEngine; 
using UnityEngine.UI;
using static StateMachineController;

public class ConditionalMessage : MonoBehaviour
{
    private int linearMessageBookmark = -1;
    [SerializeField] private List<MessageWithCondition> linearMessages; //play these messages one after another  
    private MessageWithCondition currentMessageWithCondition;
    private TMP_Text modifiableMessageText;
    private Image bg;
    [SerializeField] private Button clickToContinue;
    private void OnEnable()
    {
        modifiableMessageText = GetComponentInChildren<TMP_Text>(true);
        bg = GetComponentInChildren<Image>(true);
        SetVisibilityOfConfirmationButton(false);
        linearMessageBookmark = -1;
        ShowNextMessage();
    }
    private void ShowMessage(int id = 0)
    {
        if (bg != null) bg.gameObject.SetActive(true);
        if (modifiableMessageText != null) modifiableMessageText.gameObject.SetActive(true);
        if (modifiableMessageText != null) modifiableMessageText.text = linearMessages[id].message.messageContents;
        currentMessageWithCondition = linearMessages[id];
        SetVisibilityOfConfirmationButton(false);
    }
    private bool linearMessageConditionMet = false;
    private void Update()
    {
        EvaluateIfLinearMessageConditionMet();
    } 
    private void EvaluateIfLinearMessageConditionMet()
    { 
        if (linearMessageConditionMet == false)
        { 
            if (currentMessageWithCondition != null)
            {
                int conditionsMet = 0;
                int required = currentMessageWithCondition.message.conditions.Length;
                foreach (Condition condition in currentMessageWithCondition.message.conditions)
                {
                    bool met = false;
                    switch (condition)
                    {
                        case Condition.AutoComplete:
                            met = true;
                            break;
                        case Condition.LevelEntitiesDestroyed:
                            met = CheckLevelEntitiesDestroyed();
                            break;
                        case Condition.SelectedTypeOfFactionEntity:
                            met = CheckSelectedTypeOfFactionEntity();
                            break;
                        case Condition.ControlsTypeOfFactionEntity:
                            met = CheckControlsTypeOfFactionEntity();
                            break;
                        case Condition.MouseInputDetected:
                            met = Input.GetMouseButtonDown(currentMessageWithCondition.message.mouseInput);
                            break;
                        default:
                            break;
                    }
                    if (met) conditionsMet++;
                }
                linearMessageConditionMet = conditionsMet >= required;
            }
        }
        else
        {
            if (currentMessageWithCondition.message.playerNeedsToClickToContinue)
            {
                SetVisibilityOfConfirmationButton(true);
            }
            else
            {
                ShowNextMessage();
            }
        }
    }
    private void ShowNextMessage()
    { 
        linearMessageBookmark++;
        linearMessageConditionMet = false;
        if (linearMessageBookmark < linearMessages.Count)
        { 
            ShowMessage(linearMessageBookmark);
        }
        else
        {
            HideMessages();
        }
    }
    private void HideMessages()
    {
        if (bg != null) bg.gameObject.SetActive(false);
        if (modifiableMessageText != null) modifiableMessageText.gameObject.SetActive(false);
        currentMessageWithCondition = null;
        SetVisibilityOfConfirmationButton(false);
    }
    private void SetVisibilityOfConfirmationButton(bool val)
    {
        clickToContinue.gameObject.SetActive(val);
    }
    public void ConfirmationReceived()
    {
        ShowNextMessage();
    }
    private bool CheckControlsTypeOfFactionEntity()
    {
        FactionEntity fac = currentMessageWithCondition.message.entityToCheck;
        int required = currentMessageWithCondition.message.numEntitiesToCheck;
        int matchedNum = 0;
        foreach (SelectableEntity item in Global.Instance.localPlayer.ownedEntities)
        {
            if (item != null)
            {
                if (item.factionEntity == fac) matchedNum++;
            }
        }
        return matchedNum >= required;
    }
    private bool CheckSelectedTypeOfFactionEntity()
    {
        FactionEntity fac = currentMessageWithCondition.message.entityToCheck;
        int required = currentMessageWithCondition.message.numEntitiesToCheck; 
        int matchedNum = 0;
        foreach (SelectableEntity item in Global.Instance.localPlayer.ownedEntities)
        {
            if (item != null)
            {
                if (item.selected && item.factionEntity == fac) matchedNum++;
            }
        }
        //Debug.Log("Matched" + matchedNum + "required" +required);
        return matchedNum >= required; 
    }
    private bool CheckLevelEntitiesDestroyed()
    {  
        foreach (SelectableEntity item in currentMessageWithCondition.levelEntities)
        {
            if (item != null && item.alive)
            {
                return false; 
            }
        }
        return true; //only if no items are alive
    }
}
[System.Serializable]
public class MessageWithCondition
{
    public Message message;
    public List<SelectableEntity> levelEntities;
} 