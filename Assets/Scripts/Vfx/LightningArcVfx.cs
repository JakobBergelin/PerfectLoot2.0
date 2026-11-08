using UnityEngine;

/// <summary>
/// A lightning bolt between two points (VfxManager.Arc): a jagged line that is re-jagged a few times while it lives and
/// thins out as it fades. Every LineRenderer under it (a bright core and a wider glow) draws the same points, with its
/// own width from the prefab. Lives for its VfxInstance's Linger.
/// </summary>
[RequireComponent(typeof(VfxInstance))]
public class LightningArcVfx : MonoBehaviour
{
    [Tooltip("Meters per kink of the bolt")]
    public float SegmentLength = 0.6f;
    [Tooltip("How far a kink can be pushed sideways, in meters (most in the middle, none at the ends)")]
    public float Jitter = 0.35f;
    [Tooltip("Seconds between new kinks")]
    public float Flicker = 0.035f;

    LineRenderer[] _lines;
    float[] _widths;
    Vector3[] _points = new Vector3[16];
    Vector3 _from, _to;
    float _start, _nextFlicker, _life;

    /// <summary>Starts the bolt from one point to the other.</summary>
    public void SetEnds(Vector3 from, Vector3 to)
    {
        if (_lines == null)
        {
            _lines = GetComponentsInChildren<LineRenderer>(true);
            _widths = new float[_lines.Length];
            for (int i = 0; i < _lines.Length; i++) _widths[i] = _lines[i].widthMultiplier;
        }
        _from = from;
        _to = to;
        _start = Time.time;
        _life = Mathf.Max(0.05f, GetComponent<VfxInstance>().Linger);
        _nextFlicker = 0f;
        Redraw();
    }

    void Update()
    {
        if (_lines == null) return;
        float fade = 1f - Mathf.Clamp01((Time.time - _start) / _life);
        for (int i = 0; i < _lines.Length; i++) _lines[i].widthMultiplier = _widths[i] * fade;
        if (Time.time >= _nextFlicker) Redraw();
    }

    void Redraw()
    {
        _nextFlicker = Time.time + Flicker;
        var along = _to - _from;
        int count = Mathf.Clamp(Mathf.CeilToInt(along.magnitude / SegmentLength) + 1, 3, _points.Length);

        // Sideways: across the bolt, and a little up and down so it isn't flat from the top-down camera
        var side = Vector3.Cross(along, Vector3.up);
        side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;
        for (int i = 0; i < count; i++)
        {
            float t = i / (count - 1f);
            float reach = Jitter * Mathf.Sin(t * Mathf.PI);
            _points[i] = _from + along * t + side * Random.Range(-reach, reach) + Vector3.up * Random.Range(-reach, reach) * 0.5f;
        }
        foreach (var line in _lines)
        {
            line.positionCount = count;
            line.SetPositions(_points);
        }
    }
}
