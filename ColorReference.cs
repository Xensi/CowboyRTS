using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ColorReference : MonoBehaviour
{
    public static ColorReference Instance { get; private set; }
    public List<Material> colors;
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
    }
}
