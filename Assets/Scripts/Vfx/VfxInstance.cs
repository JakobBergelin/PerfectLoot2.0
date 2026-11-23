using UnityEngine;
using UnityEngine.VFX;

/// <summary>An effect that spans two points and is moved by its ends (VfxManager.SetEnds), every frame it's shown.</summary>
public interface IVfxEnds
{
    void SetEnds(Vector3 from, Vector3 to);
}

/// <summary>
/// Root of a pooled effect prefab. On release its particles stop emitting (particle systems and VFX graphs) and the
/// HideOnRelease renderers (a fireball's core, a tornado's funnel) disappear at once; after Linger seconds, when the last
/// particles have faded, it goes back to the pool. Its lights (a fireball's glow) flare up as it starts and fade out
/// over LightFade once it's released or its flash is over; VfxManager turns them off when too many are lit.
/// </summary>
public class VfxInstance : MonoBehaviour
{
    [Min(0f)] public float Linger = 1f;
    public Renderer[] HideOnRelease = new Renderer[0];
    [Tooltip("Lights that light up what's around the effect (flat comic pools in PerfectLoot/Comic)")]
    public Light[] Lights = new Light[0];
    [Tooltip("Seconds the lights take to fade out once the effect is released")]
    [Min(0.01f)] public float LightFade = 0.25f;
    [Tooltip("Above 0: the lights only flash for this many seconds (an impact), then fade")]
    [Min(0f)] public float LightFlash;
    [Tooltip("A mark it leaves on the ground as it starts (scorch, frost): a decal material (GroundDecals)")]
    public Material GroundMark;
    [Min(0f)] public float GroundMarkSize = 3f;
    [Tooltip("Seconds the mark stays, then the seconds it takes to fade")]
    public Vector2 GroundMarkLife = new Vector2(6f, 3f);

    ParticleSystem[] _systems;
    VisualEffect[] _graphs;
    float[] _intensities;
    float _startedAt, _fadeFrom = -1f;
    bool _lightsAllowed = true;

    public bool HasLights => Lights.Length > 0;

    void Find()
    {
        _systems ??= GetComponentsInChildren<ParticleSystem>(true);
        _graphs ??= GetComponentsInChildren<VisualEffect>(true);
        if (_intensities == null)
        {
            _intensities = new float[Lights.Length];
            for (int i = 0; i < Lights.Length; i++) _intensities[i] = Lights[i].intensity;
        }
    }

    public void Begin()
    {
        Find();
        foreach (var r in HideOnRelease) if (r != null) r.enabled = true;
        foreach (var ps in _systems)
        {
            ps.Clear(true);
            ps.Play(true);
        }
        LateUpdate();
        foreach (var graph in _graphs)
        {
            graph.Reinit();
            graph.Play();
        }
        _startedAt = Time.time;
        _fadeFrom = -1f;
        SetLights(1f);
        if (GroundMark != null)
            GroundDecals.Place(GroundMark, transform.position, GroundMarkSize * transform.localScale.x, Random.Range(0f, 360f),
                               GroundMarkLife.x, GroundMarkLife.y);
    }

    public void End()
    {
        Find();
        foreach (var r in HideOnRelease) if (r != null) r.enabled = false;
        foreach (var ps in _systems) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        foreach (var graph in _graphs) graph.Stop();
        if (_fadeFrom < 0f) _fadeFrom = Time.time;
    }

    /// <summary>Turns the lights off for good (VfxManager: too many lit), or lets them shine again.</summary>
    public void AllowLights(bool allowed) => _lightsAllowed = allowed;

    static readonly int OriginId = Shader.PropertyToID("Origin"), GroundYId = Shader.PropertyToID("GroundY"),
                        LevelGroundId = Shader.PropertyToID("_PL_GroundY");

    // Graphs simulate in world space: they're told where the effect is (Origin) and where the level's ground is (GroundY)
    void LateUpdate()
    {
        if (_graphs == null) return;
        foreach (var graph in _graphs)
        {
            if (graph.HasVector3(OriginId)) graph.SetVector3(OriginId, graph.transform.position);
            if (graph.HasFloat(GroundYId)) graph.SetFloat(GroundYId, Shader.GetGlobalFloat(LevelGroundId));
        }
    }

    void Update()
    {
        if (Lights.Length == 0) return;
        if (LightFlash > 0f && _fadeFrom < 0f && Time.time >= _startedAt + LightFlash) _fadeFrom = Time.time;
        // A quick flare as it starts, then steady until it fades out
        float on = Mathf.Clamp01((Time.time - _startedAt) / 0.06f);
        float fade = _fadeFrom < 0f ? 1f : 1f - Mathf.Clamp01((Time.time - _fadeFrom) / LightFade);
        SetLights(on * fade);
    }

    void SetLights(float amount)
    {
        for (int i = 0; i < Lights.Length; i++)
        {
            Lights[i].intensity = _intensities[i] * amount;
            Lights[i].enabled = _lightsAllowed && amount > 0f;
        }
    }
}
