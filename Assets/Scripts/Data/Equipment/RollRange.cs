using System;
using UnityEngine;

namespace Equipment
{
    [Serializable]
    public class RollRange
    {
        public float Min;
        public float Max;

        /// <summary>A value between the minLerp and maxLerp points of the range, drawn from random (one draw).</summary>
        public float Random(ref Unity.Mathematics.Random random, float minLerp, float maxLerp)
        {
            return random.NextFloat(Mathf.Lerp(Min, Max, minLerp), Mathf.Lerp(Min, Max, maxLerp));
        }
    }
}
