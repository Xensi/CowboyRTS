using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrailController : MonoBehaviour
{
    public Vector3 start;
    public Vector3 destination; 
    private readonly float speed = 100; 
    void Update()
    { 
        var step = speed * Time.deltaTime; // calculate distance to move
        transform.position = Vector3.MoveTowards(transform.position, destination, step);
    }
}
    