using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SelectionCircle : MonoBehaviour
{
    [SerializeField] private float lrWidth = 0.05f;
    [SerializeField] private int subDivs = 64; //how detailed the circle should be
    [SerializeField] private float radius = 1;
    private LineRenderer lr;
    private Color color;
    void Awake()
    {
        lr = GetComponent<LineRenderer>(); 
    }
    private void Start()
    {
        UpdateSelectionCirclePositions();
    }
    public void UpdateVisibility(bool val)
    {
        lr.enabled = val;
    }
    public void SetColor(Color newColor)
    {
        color = newColor;
        if (lr != null)
        {
            lr.startColor = color;
            lr.endColor = color;
        }
    }
    public float GetRadius()
    {
        return radius;
    }
    public void UpdateRadius(float newRad)
    {
        radius = newRad;
        UpdateSelectionCirclePositions();
    }
    public void UpdateSelectionCirclePositions()
    {
        if (lr != null)
        {
            lr.startWidth = lrWidth;
            lr.startColor = color;
            lr.endColor = color;
        } 
        Vector3 point = transform.localPosition + new Vector3(0, 0, -0.01f);
        int numPoints = subDivs + 1;
        Vector3[] positions = new Vector3[numPoints];
        for (int i = 0; i < numPoints; i++)
        {
            /* Distance around the circle */
            var radians = 2 * Mathf.PI / subDivs * i;

            /* Get the vector direction */
            var vertical = Mathf.Sin(radians);
            var horizontal = Mathf.Cos(radians);

            var spawnDir = new Vector3(horizontal, vertical, 0);

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
