using UnityEngine;

/// <summary>Spins an effect part around its local up axis (tornado funnel, whirlwind arc).</summary>
public class VfxSpin : MonoBehaviour
{
    public float DegreesPerSecond = 720f;

    void Update() => transform.Rotate(0f, DegreesPerSecond * Time.deltaTime, 0f, Space.Self);
}
