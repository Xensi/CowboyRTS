using TMPro;
using UnityEngine;

namespace UtilityMethods
{
    public static class Util
    {
        /// <summary>
        /// Returns true if the distance between start and end is less than threshold.
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="threshold"></param>
        /// <returns></returns>
        public static bool FastDistanceCheck(Vector3 start, Vector3 end, float threshold)
        {
            float mag = GetSqrDist(start, end);
            return SqrDistCheck(mag, threshold);
        }
        public static float GetSqrDist(Vector3 start, Vector3 end)
        {
            Vector3 offset = start - end;
            return Vector3.SqrMagnitude(offset);
        }
        public static bool SqrDistCheck(float sqrDist, float threshold)
        {
            return sqrDist < threshold * threshold;
        }
        public static void SetText(TMP_Text text, string words)
        {
            if (text != null) text.SetText(words);
        }

        public static void SmartSetActive(GameObject obj, bool val)
        {
            if (obj != null && obj.activeInHierarchy != val) obj.SetActive(val);
        }
    }
}
