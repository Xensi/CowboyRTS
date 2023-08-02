using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrailController : MonoBehaviour
{
    public Vector3 start;
    public Vector3 destination; 
    private readonly float speed = 50;
    void Update()
    { 
        /*time += speed*Time.deltaTime;
        time = Mathf.Clamp(time, 0, 1);
        transform.position = Vector3.Lerp(start, destination, time);*/
        var step = speed * Time.deltaTime; // calculate distance to move
        transform.position = Vector3.MoveTowards(transform.position, destination, step);
    }
}
    