using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using UnityEngine;

public class SelectionCircle : MonoBehaviour
{
    [SerializeField] private float lrWidth = 0.05f;
    [SerializeField] private int subDivs = 64; //how detailed the circle should be
    [SerializeField] private float radius = 1;
    private LineRenderer lr;
    private UnityEngine.Color color;
    void Awake()
    {
        lr = GetComponent<LineRenderer>(); 
    } 
    public void UpdateVisibility(bool val)
    {
        lr.enabled = val;
    }
    public void SetColor(UnityEngine.Color newColor)
    {
        color = newColor;
    }
    public void UpdateRadius(float newRad)
    {
        radius = newRad;
    }
    public void UpdateSelectionCirclePosition()
    {
        if (lr != null)
        {
            lr.startWidth = lrWidth;
            lr.startColor = color;
            lr.endColor = color;
        } 
        Vector3 point = transform.position + new Vector3(0, 0.01f, 0);
        int numPoints = subDivs + 1;
        Vector3[] positions = new Vector3[numPoints];
        for (int i = 0; i < numPoints; i++)
        {
            /* Distance around the circle */
            var radians = 2 * MathF.PI / subDivs * i;

            /* Get the vector direction */
            var vertical = MathF.Sin(radians);
            var horizontal = MathF.Cos(radians);

            var spawnDir = new Vector3(horizontal, 0, vertical);

            /* Get the spawn position */
            var spawnPos = point + spawnDir * radius; // Radius is just the distance away from the point 
            positions[i] = spawnPos; 
        }
        if (lr != null)
        {
            lr.positionCount = numPoints;
            lr.SetPositions(positions);
        }
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(transform.position, radius);
        Gizmos.DrawWireSphere(transform.position, radius+lrWidth);
    }
}
