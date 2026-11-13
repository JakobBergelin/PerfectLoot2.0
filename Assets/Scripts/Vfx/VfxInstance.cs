using UnityEngine;

/// <summary>An effect that spans two points and is moved by its ends (VfxManager.SetEnds), every frame it's shown.</summary>
public interface IVfxEnds
{
    void SetEnds(Vector3 from, Vector3 to);
}

/// <summary>
/// Root of a pooled effect prefab. On release its particles stop emitting and the HideOnRelease renderers (a fireball's
/// core, a tornado's funnel) disappear at once; after Linger seconds, when the last particles have faded, it goes back
/// to the pool.
/// </summary>
public class VfxInstance : MonoBehaviour
{
    [Min(0f)] public float Linger = 1f;
    public Renderer[] HideOnRelease = new Renderer[0];

    ParticleSystem[] _systems;

    public void Begin()
    {
        _systems ??= GetComponentsInChildren<ParticleSystem>(true);
        foreach (var r in HideOnRelease) if (r != null) r.enabled = true;
        foreach (var ps in _systems)
        {
            ps.Clear(true);
            ps.Play(true);
        }
    }

    public void End()
    {
        _systems ??= GetComponentsInChildren<ParticleSystem>(true);
        foreach (var r in HideOnRelease) if (r != null) r.enabled = false;
        foreach (var ps in _systems) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}
