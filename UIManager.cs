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
}
