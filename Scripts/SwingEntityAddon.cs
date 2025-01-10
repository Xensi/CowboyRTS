using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SwingEntityAddon : EntityAddon
{ 
    [HideInInspector] public bool ready = true;
    [HideInInspector] public float readyTimer = 0;
    [HideInInspector] public float range = 1;
    [HideInInspector] public float impactTime = 1;
    [HideInInspector] public sbyte delta = 1; //How much to change variables by
    [HideInInspector] public float duration = 1;
}
