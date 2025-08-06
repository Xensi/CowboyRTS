using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DisplayRadius : MonoBehaviour
{
    private readonly float lrWidth = 0.05f;
    [SerializeField] private int subDivs = 64; //how detailed the circle should be
    public float radius = 1;
    public LineRenderer lr;

    [SerializeField] private UnityEngine.Color drColor;
    void Start()
    {
        UpdateLR();
    }
    private void Update()
    {

        UpdateLR();
    }
    public void SetLREnable(bool val)
    {
        lr.enabled = val;
    }
    public void SetColor(Color color)
    {
        drColor = color;
    }
    public void UpdateLR()
    {
        if (lr != null)
        {
            lr.startWidth = lrWidth;
            lr.startColor = drColor;
            lr.endColor = drColor;

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
                //Debug.DrawRay(spawnPos, Vector3.up, UnityEngine.Color.red, 1);
            }
            if (lr != null)
            {
                lr.positionCount = numPoints;
                lr.SetPositions(positions);
            }
        }
    }
    private void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
