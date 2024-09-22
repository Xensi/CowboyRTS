using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Reins : MonoBehaviour
{
    private LineRenderer lr;
    public Transform pos1;
    public Transform pos2;
    private void Start()
    {
        lr = GetComponent<LineRenderer>();
    }
    private void Update()
    { 
        Vector3[] positions = { pos1.position, pos2.position };
        lr.SetPositions(positions);
    }
}
