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
            Vector3 offset = start - end;
            return Vector3.SqrMagnitude(offset) <= threshold * threshold;
        }
    }
}
