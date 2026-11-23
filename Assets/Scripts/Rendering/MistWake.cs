using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The mist wake: a top-down map over the level (the same area as the forest mask) of how far the height fog's mist has
/// been pushed and how much has been cleared away. Skills, dashes and footsteps stir it (VfxMist, from the effects they
/// show); each frame it eases back to calm, so pushed mist slowly fills back in. One map for every camera, so all
/// split-screen views see the same wake; every machine stirs it from the effects it shows, so they match. Updated on the
/// GPU (PL_MistWake.compute); PL_HeightFog.shader reads it (_PL_MistWake).
/// </summary>
public static class MistWake
{
    const float TexelMeters = 0.75f;
    const float Refill = 4f;        // Seconds the mist takes to settle back
    const int MaxStirs = 48;        // Per frame

    static RenderTexture _wake;
    static ComputeShader _compute;
    static ComputeBuffer _stirs;
    static readonly List<Vector4> _pending = new List<Vector4>();
    static Matrix4x4 _uvToWorld;

    static readonly int WakeId = Shader.PropertyToID("_PL_MistWake"), MatrixId = Shader.PropertyToID("_PL_MistWakeMatrix"),
                        KernelWakeId = Shader.PropertyToID("_Wake"), StirsId = Shader.PropertyToID("_Stirs"),
                        StirCountId = Shader.PropertyToID("_StirCount"), DeltaTimeId = Shader.PropertyToID("_DeltaTime"),
                        RefillId = Shader.PropertyToID("_Refill"), UvToWorldId = Shader.PropertyToID("_UvToWorld"),
                        SizeId = Shader.PropertyToID("_Size");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init() => Clear();

    /// <summary>The level's area: worldToUv maps world positions to the map (its xz), sizeMeters its size.</summary>
    public static void SetArea(Matrix4x4 worldToUv, Vector2 sizeMeters)
    {
        Clear();
        int w = Mathf.Clamp(Mathf.CeilToInt(sizeMeters.x / TexelMeters), 1, 2048), h = Mathf.Clamp(Mathf.CeilToInt(sizeMeters.y / TexelMeters), 1, 2048);
        _wake = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            { name = "PL_MistWake", enableRandomWrite = true, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        _wake.Create();
        var active = RenderTexture.active;
        RenderTexture.active = _wake;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = active;
        _uvToWorld = worldToUv.inverse;
        Shader.SetGlobalTexture(WakeId, _wake);
        Shader.SetGlobalMatrix(MatrixId, worldToUv);
    }

    /// <summary>No level: no wake (the fog reads a black map).</summary>
    public static void Clear()
    {
        if (_wake != null) _wake.Release();
        _wake = null;
        _stirs?.Release();
        _stirs = null;
        _pending.Clear();
        Shader.SetGlobalTexture(WakeId, Texture2D.blackTexture);
    }

    /// <summary>
    /// Stirs the mist this frame around a point: push (m/s) moves it outward (negative pulls it in), swirl (m/s) turns it
    /// around, clear (per second, 0..1) thins it out.
    /// </summary>
    public static void Stir(Vector3 position, float radius, float push, float swirl, float clear)
    {
        if (_wake == null || _pending.Count >= MaxStirs * 2) return;
        _pending.Add(new Vector4(position.x, position.z, radius, push));
        _pending.Add(new Vector4(swirl, clear, 0f, 0f));
    }

    /// <summary>Eases the mist back and applies this frame's stirs. Once a frame (VfxManager).</summary>
    public static void Tick(float deltaTime)
    {
        if (_wake == null) return;
        if (_compute == null) _compute = Resources.Load<ComputeShader>("PL_MistWake");
        if (_compute == null) return;
        _stirs ??= new ComputeBuffer(MaxStirs * 2, 16);
        if (_pending.Count > 0) _stirs.SetData(_pending);
        _compute.SetTexture(0, KernelWakeId, _wake);
        _compute.SetBuffer(0, StirsId, _stirs);
        _compute.SetInt(StirCountId, _pending.Count / 2);
        _compute.SetFloat(DeltaTimeId, deltaTime);
        _compute.SetFloat(RefillId, Refill);
        _compute.SetMatrix(UvToWorldId, _uvToWorld);
        _compute.SetVector(SizeId, new Vector2(_wake.width, _wake.height));
        _compute.Dispatch(0, (_wake.width + 7) / 8, (_wake.height + 7) / 8, 1);
        _pending.Clear();
    }
}
