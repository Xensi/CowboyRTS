using System.Collections;
using System.Collections.Generic;
using UnityEngine;
/// <summary>
/// Used to define entity addons that require the entity to swing at something (attacking, harvesting, building)
/// </summary>
public class SwingEntityAddon : EntityAddon
{ 
    public bool ready = true;
    [HideInInspector] public float readyTimer = 0;
    [HideInInspector] public float range = 1;
    [HideInInspector] public float impactTime = 1;
    [HideInInspector] public sbyte swingDelta = 1; //How much to change variables by
    [HideInInspector] public float duration = 1;
    /// <summary>
    /// Updates attack readiness during the time between impact and the attack duration.
    /// </summary>
    public void UpdateReadiness()
    {
        if (!ready)
        {
            if (readyTimer < Mathf.Clamp(duration - impactTime, 0, 999))
            {
                readyTimer += Time.deltaTime;
            }
            else
            {
                ready = true;
                readyTimer = 0;
            }
        }
    }
}
