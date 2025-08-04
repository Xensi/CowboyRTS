using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI; 

public class EntityHealthBar : MonoBehaviour
{
    public Image bar;
    [SerializeField] private Gradient gradient;
    public Transform barParent;
    [HideInInspector] public Entity entity; 

    private void Start()
    {
        if (barParent != null) barParent.SetParent(HealthBarCanvas.instance.transform);
        if (bar != null) bar.color = gradient.Evaluate(1);
    }
    public void Delete()
    { 
        SetVisible(false);
        if (barParent != null) Destroy(barParent.gameObject);
        if (gameObject != null) Destroy(gameObject);
    }
    public void SetVisibleHPConditional(bool val)
    {
        if (entity.currentHP.Value < entity.maxHP)
        { 
            gameObject.SetActive(val);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
    public void SetVisible(bool val)
    {
        gameObject.SetActive(val);
    }
    public void SetRatioBasedOnHP(int current, float max)
    {
        if (bar == null) return;
        float ratio = current / max;
        bar.fillAmount = ratio; 
        bar.color = gradient.Evaluate(ratio);

        SetVisibleHPConditional(true); 
    }
    public void SetRatioBasedOnProduction(float current, float max)
    { 
        if (bar == null) return;
        float ratio = current / (max);
        bar.fillAmount = ratio;
        bar.color = gradient.Evaluate(ratio); 
    }
}
