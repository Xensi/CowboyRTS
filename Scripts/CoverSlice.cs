using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CoverSlice : MonoBehaviour
{
    [SerializeField] int divideBy = 6;

    [SerializeField] float lrWidth = 0.05f;
    [SerializeField] private int subDivs = 64; //how detailed the circle should be
    public float radius = 1;
    private LineRenderer lr;
    [SerializeField] Entity followTarget;
    void Start()
    {
        lr = GetComponent<LineRenderer>();
        UpdateLR();
    }
    private void Update()
    {
        return;
        UpdateLR();
        if (followTarget != null)
        {   //special code to look at something with an initial offset angle on the x rotation
            Vector3 targetDir = followTarget.transform.position - transform.position;
            float angle = Vector3.SignedAngle(targetDir, transform.up, Vector3.down);
            transform.eulerAngles += new Vector3(0, angle, 0);
        }
    }
    public void UpdateCoverToFollow(Entity newCover)
    {
        followTarget = newCover;
        bool enable = newCover != null;
        SetLREnable(enable);
    }
    public void SetLREnable(bool val)
    {
        lr.enabled = val;
    }
    public void UpdateLR()
    {
        if (lr != null)
        {
            lr.startWidth = lrWidth;

            Vector3 point = transform.localPosition + new Vector3(0, 0, -0.01f);
            int iterations = (subDivs / divideBy) + 1;
            Vector3[] positions = new Vector3[iterations];
            for (int i = 0; i < iterations; i++)
            {
                /* Distance around the circle */
                var radians = 2 * Mathf.PI / subDivs * i + Mathf.PI/3;//Mathf.PI/4;

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
                lr.positionCount = iterations;
                lr.SetPositions(positions);
            }
        }
    }
}
