using LevelGeneration;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// The main menu's camera: low and close, framing the characters head to toe, looking at the clearing's stage (the level's player start, LevelReady) from
/// the south, where the characters stand (MenuCharacters). It sways slowly around that view so the scene feels alive.
/// Shows the sky until the level is built.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MenuSceneCamera : MonoBehaviour
{
    [Tooltip("Meters south of the stage")]
    public float Distance = 5.5f;
    [Tooltip("Meters above the stage")]
    public float Height = 1.9f;
    [Tooltip("Meters above the stage the camera looks at")]
    public float LookHeight = 1.25f;
    [Tooltip("Meters the look point is moved sideways, so the characters stand between the menu's side panels")]
    public float LookOffset;

    [Header("Drift")]
    [Tooltip("Degrees the camera swings around the stage at most")]
    public float SwayAngle = 4f;
    [Tooltip("Seconds for one full swing")]
    public float SwayPeriod = 22f;
    [Tooltip("Meters the camera bobs up and down at most")]
    public float Bob = 0.15f;

    /// <summary>The stage the characters stand on, once the clearing is built.</summary>
    public static bool TryGetStage(out float3 stage)
    {
        stage = default;
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;
        using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<LevelReady>());
        if (query.IsEmpty) return false;
        stage = query.GetSingleton<LevelReady>().PlayerStart;
        return true;
    }

    void LateUpdate()
    {
        if (!TryGetStage(out var stage)) return;
        float t = Time.unscaledTime;
        float yaw = Mathf.Sin(t * 2f * Mathf.PI / Mathf.Max(1f, SwayPeriod)) * SwayAngle;
        float bob = Mathf.Sin(t * 2f * Mathf.PI / Mathf.Max(1f, SwayPeriod * 0.63f)) * Bob;

        var look = (Vector3)stage + new Vector3(LookOffset, LookHeight, 0f);
        var offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, Height - LookHeight + bob, -Distance);
        transform.position = look + offset;
        transform.rotation = Quaternion.LookRotation(look - transform.position, Vector3.up);
    }
}
