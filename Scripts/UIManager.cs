using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UtilityMethods;
public class UIManager : MonoBehaviour
{
    public static UIManager instance { get; private set; }
    public TMP_Text popText;
    public TMP_Text goldText;
    public TMP_Text nameText;
    public TMP_Text descText;
    public Button[] queueButtons;
    public Button[] productionButtons;
    public GameObject selectedParent;
    public TMP_Text hpText;
    public GameObject setRallyPointButton;
    public GameObject popFullWarning;
    //public GameObject singleUnitInfoParent;
    public TMP_Text modifiableMessageText;
    public Image levelMessageBG;
    public Button clickToContinue;

    public Canvas lobbyCanvas;
    public Canvas gameCanvas;

    public GameObject mainCamParent;
    public GameObject SPButtonsParent;
    public TMP_Text levelObjective;
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
        }
        else
        {
            instance = this;
        }
        Util.SmartSetActive(selectedParent, false);

        foreach (Button item in productionButtons)
        {
            Util.SmartSetActive(item.gameObject, false);
        }
    }
    private void Start()
    {
        ChangeLobbyUIStatus(true);
        ChangeGameUIStatus(false);
    }
    public void HideAllUI()
    {
        ChangeLobbyUIStatus(false);
        ChangeGameUIStatus(false);
        ChangeCamStatus(false);
    }
    public void ChangeLobbyUIStatus(bool val)
    {
        lobbyCanvas.enabled = val;
        SPButtonsParent.SetActive(val);
    }
    public void ShowGameUI()
    {
        ChangeLobbyUIStatus(false);
        ChangeGameUIStatus(true);
        ChangeCamStatus(true);
    }
    public void ChangeGameUIStatus(bool val)
    {
        gameCanvas.enabled = val;
    }
    public void ChangeCamStatus(bool val)
    {
        mainCamParent.SetActive(val);
    }
    public void UpdateLevelObjective(string obj)
    {
        if (levelObjective != null) levelObjective.text = obj;
    }
}
