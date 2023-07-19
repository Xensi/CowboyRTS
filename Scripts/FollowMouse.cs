 using UnityEngine;

public class FollowMouse : MonoBehaviour
{
    public Camera cam;

    void Start()
    {
        cam = Camera.main;
    }
    void Update()
    { 
        if (Input.GetMouseButtonDown(1))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition); 
            RaycastHit hit; 

            if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity))
            {
                transform.position = hit.point;
            }
        }
    }
}
