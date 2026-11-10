using Unity.Entities;
using UnityEngine;

/// <summary>
/// A flat ring on the ground at the local player's aim point (InputsData.MousePosition) while a controller is used, so
/// the stick's aim can be seen; the mouse cursor shows it otherwise. Hidden without a player or while a menu panel is
/// open. Cosmetic only. On the rig, with a LineRenderer whose material it keeps.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class AimReticle : MonoBehaviour
{
    [Tooltip("Meters from the aim point to the ring")]
    public float Radius = 0.6f;
    [Tooltip("Meters wide the ring's line is")]
    public float Width = 0.08f;
    [Tooltip("Meters the aim point sits above the ground (InputsDataSystem's AimHeight); the ring is drawn this much lower")]
    public float AimHeight = 0.5f;
    public Color Color = new Color(1f, 1f, 1f, 0.75f);
    [Range(8, 64)] public int Segments = 32;

    LineRenderer _line;
    Vector3[] _points;

    void Awake()
    {
        _line = GetComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.loop = true;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;
        _line.enabled = false;
    }

    void LateUpdate()
    {
        Vector3 point = default;
        bool show = GameInput.UsingGamepad && !MenuPanels.IsPanelOpen && TryGetAimPoint(out point);
        _line.enabled = show;
        if (!show) return;

        if (_points == null || _points.Length != Segments) _points = new Vector3[Segments];
        var center = point + Vector3.up * (0.05f - AimHeight);
        for (int i = 0; i < Segments; i++)
        {
            float angle = i * Mathf.PI * 2f / Segments;
            _points[i] = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Radius;
        }
        _line.positionCount = Segments;
        _line.SetPositions(_points);
        _line.widthMultiplier = Width;
        _line.startColor = _line.endColor = Color;
    }

    static bool TryGetAimPoint(out Vector3 point)
    {
        point = default;
        var world = World.DefaultGameObjectInjectionWorld;
        var characters = CharacterManager.Instance;
        if (world == null || !world.IsCreated || characters == null) return false;
        var entityManager = world.EntityManager;
        var player = characters.Player;
        if (!entityManager.Exists(player) || !entityManager.HasComponent<InputsData>(player)) return false;
        point = entityManager.GetComponentData<InputsData>(player).MousePosition;
        return true;
    }
}
