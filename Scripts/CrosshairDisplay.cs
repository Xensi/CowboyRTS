using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CrosshairDisplay : MonoBehaviour
{
    [SerializeField] private float lrWidth = 0.05f;
    [SerializeField] private int subDivs = 64; //how detailed the circle should be
    [SerializeField] private float radius = 1;
    [SerializeField] private LineRenderer lr;
    [SerializeField] private Color color;
    [SerializeField] private int numLines = 4;
    [SerializeField] private float rotateSpeed = 100f;
    [SerializeField] private float scaleSpeed = 100f;
    [SerializeField] private float scaleMax = 0.25f;
    [SerializeField] private DisplayRadius dr;
    private float offset = 0;
    private bool visible = true;
    public SelectableEntity assignedEntity;
    private bool shouldPulse = false;
    [SerializeField] private float idlePulseScale = 0.5f;
    private void Start()
    {
        UpdatePositions();
    }
    private void Update()
    {
        float scale = 0;
        if (shouldPulse)
        {
            transform.RotateAround(transform.position, Vector3.up, rotateSpeed * Time.deltaTime);
            scale = Mathf.Sin(offset + Time.time * scaleSpeed) * scaleMax + (1 - scaleMax);
        }
        else
        {
            transform.RotateAround(transform.position, Vector3.up, rotateSpeed * idlePulseScale * Time.deltaTime);
            scale = Mathf.Sin(offset + Time.time * scaleSpeed) * scaleMax * idlePulseScale + (1 - scaleMax * idlePulseScale);
        }
        transform.localScale = new Vector3(scale, 1, scale);
    }
    public void SetPulse(bool val)
    {
        shouldPulse = val;
    }
    public void UpdateOffset()
    {
        offset = Time.time;
    }
    public bool GetVisibile()
    {
        return visible;
    }
    public void UpdateVisibility(bool val)
    {
        if (visible != val)
        {
            visible = val;
            if (lr != null) lr.enabled = val;
            if (dr != null) dr.SetLREnable(val);
        }
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
    public void CheckIfShouldBeDestroyed(SelectableEntity asker)
    {
        if (assignedEntity == asker)
        {
            Destroy(gameObject);
        }
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
