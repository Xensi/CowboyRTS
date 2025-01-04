using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GenericProgressBar : MonoBehaviour
{ 
    public Image bar;  
    public void SetRatio(int current, float max)
    {
        if (bar == null) return;
        float ratio = current / max;
        bar.fillAmount = ratio;  
    }
    public void SetColor(Color color)
    {
        if (bar == null) return;
        bar.color = color;
    }
}
