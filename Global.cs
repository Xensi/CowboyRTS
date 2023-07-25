using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Global : MonoBehaviour
{
    public RectTransform selectionRect;
    public static Global Instance { get; private set; }
    public List<Material> colors;
    public List<Transform> playerSpawn;
    public List<Button> productionButtons;
    public Material transparent;
    public Material blocked;
    public RTSPlayer localPlayer;
    public TMP_Text goldText;
    private void Awake()
    {
        // If there is an instance, and it's not me, delete myself.

        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }

        foreach (Button item in productionButtons)
        {
            item.gameObject.SetActive(false);
        }
    }
}
