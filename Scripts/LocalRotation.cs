using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LocalRotation : MonoBehaviour
{
    public float rotSpeed = 10;
    Vector3 oldPos;
    private void Start()
    {
        oldPos = transform.position;
    }
    private void Update()
    { 
        
        transform.rotation = Quaternion.LookRotation(Vector3.RotateTowards(transform.forward, transform.position - oldPos, Time.deltaTime * rotSpeed, 0));
        oldPos = transform.position;
    }
}
