using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CrosshairDisplay : MonoBehaviour
{
    [SerializeField] private float lrWidth = 0.05f;
    [SerializeField] private int subDivs = 64; //how detailed the circle should be
    [SerializeField] private float radius = 1;
    private LineRenderer lr;
    [SerializeField] private Color color;
    [SerializeField] private int numLines = 4;
    void Awake()
    {
        lr = GetComponentInChildren<LineRenderer>();
    }
    private void Start()
    {
        UpdatePositions();
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
    public void UpdateRadius(float newRad)
    {
        radius = newRad;
    }
    public void UpdatePositions()
    {
        if (lr != null)
        {
            lr.startWidth = lrWidth;
            lr.startColor = color;
            lr.endColor = color;
        }
        //create 4 lines from center
        Vector3 center = transform.localPosition + new Vector3(0, 0, -0.01f);
        Vector3[] positions = new Vector3[numLines*2];
        int posHead = 0;
        for (int i = 0; i < numLines; i++)
        {
            var radians = 2 * Mathf.PI / numLines * i;
            var vertical = Mathf.Sin(radians);
            var horizontal = Mathf.Cos(radians);
            var spawnDir = new Vector3(horizontal, vertical, 0);
            Vector3 start = center;
            Vector3 end = center + spawnDir * radius; // Radius is just the distance away from the point
            positions[posHead] = end;
            posHead++;
            positions[posHead] = start;
            posHead++;
        }
        if (lr != null)
        {
            lr.positionCount = posHead;
            lr.SetPositions(positions);
        }
    }
}
