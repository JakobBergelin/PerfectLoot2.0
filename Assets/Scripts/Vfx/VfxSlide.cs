using UnityEngine;

/// <summary>
/// Slides an effect part in from From (a local offset, in meters) to where it sits in the prefab over Duration seconds,
/// each time the effect starts: Earth Bending's wall rising out of the ground, a meteor's rock falling in. EaseIn speeds
/// up toward the end (a fall) instead of slowing down (a rise). HideOnArrive hides its meshes once it's there and stops
/// its particles emitting, so their trails fade where they are (the rock hits).
/// </summary>
public class VfxSlide : MonoBehaviour
{
    public Vector3 From;
    [Min(0.01f)] public float Duration = 0.3f;
    public bool EaseIn;
    public bool HideOnArrive;

    Vector3 _rest;
    bool _hasRest, _arrived;
    float _start;
    MeshRenderer[] _meshes;
    ParticleSystem[] _systems;

    void OnEnable()
    {
        if (!_hasRest)
        {
            _rest = transform.localPosition;
            _hasRest = true;
        }
        _meshes ??= GetComponentsInChildren<MeshRenderer>(true);
        _systems ??= GetComponentsInChildren<ParticleSystem>(true);
        foreach (var mesh in _meshes) mesh.enabled = true;
        _start = Time.time;
        _arrived = false;
        Place(0f);
    }

    void Update()
    {
        if (_arrived) return;
        float t = Mathf.Clamp01((Time.time - _start) / Duration);
        Place(t);
        if (t < 1f) return;
        _arrived = true;
        if (!HideOnArrive) return;
        foreach (var mesh in _meshes) mesh.enabled = false;
        foreach (var ps in _systems) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }

    void Place(float t)
    {
        float eased = EaseIn ? t * t : 1f - (1f - t) * (1f - t);
        transform.localPosition = Vector3.LerpUnclamped(_rest + From, _rest, eased);
    }
}
