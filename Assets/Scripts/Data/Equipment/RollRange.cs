using System;
using UnityEngine;

namespace Equipment
{
    [Serializable]
    public class RollRange
    {
        public float Min;
        public float Max;

        public float Random(float minLerp, float maxLerp)
        {
            return UnityEngine.Random.Range(Mathf.Lerp(Min, Max, minLerp), Mathf.Lerp(Min, Max, maxLerp));
        }
    }
}
