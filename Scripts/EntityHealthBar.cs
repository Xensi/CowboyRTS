using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI; 

public class EntityHealthBar : MonoBehaviour
{
    public Image bar;
    [SerializeField] private Gradient gradient;
    public Transform barParent;
    [HideInInspector] public SelectableEntity entity;

    private void Start()
    {
        if (barParent != null) barParent.SetParent(Global.Instance.gameCanvas.transform);
        if (bar != null) bar.color = gradient.Evaluate(1);
    }
    public void Delete()
    { 
        SetVisible(false);
        if (barParent != null) Destroy(barParent.gameObject);
        Destroy(gameObject);
    }
    public void SetVisible(bool val)
    {
        if (entity.currentHP.Value < entity.maxHP)
        { 
            barParent.gameObject.SetActive(val);
        }
        else
        {
            barParent.gameObject.SetActive(false);
        }
    }
    public void SetRatioBasedOnHP(int current, float max)
    {
        if (bar == null) return;
        float ratio = current / max;
        bar.fillAmount = ratio; 
        bar.color = gradient.Evaluate(ratio);
        if (entity.currentHP.Value < entity.maxHP)
        {
            SetVisible(true); 
        }
    }
}
