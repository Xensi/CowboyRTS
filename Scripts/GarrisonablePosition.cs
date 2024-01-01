using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GarrisonablePosition : MonoBehaviour
{
    public MinionController passenger;
    private void Update()
    {
        if (passenger != null)
        {
            passenger.transform.position = transform.position;
        }
    }
}
