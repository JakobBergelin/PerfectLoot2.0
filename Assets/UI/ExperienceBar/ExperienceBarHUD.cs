using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The player's experience bar and level between the skill bar and the buff row (ExperienceBar.uxml), with the numbers
/// while the pointer is over the bar and a "Level up!" flash. Shows CharacterManager's player. Read-only: never changes ECS.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class ExperienceBarHUD : MonoBehaviour
{
    const string HoveredClass = "experience-bar--hovered", LevelUpClass = "experience-row--level-up";
    const float LevelUpSeconds = 1.8f;  // How long "Level up!" shows

    PanelRenderer _panelRenderer;
    int _boundVersion = -1;
    VisualElement _row, _fill;
    Label _level, _value;

    // Last shown values, so the UI is only touched when something changes
    Entity _shownPlayer;
    int _shownLevel, _shownExperience = -1;
    float _levelUpEnd;

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDisable()
    {
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        _row = null;
        _boundVersion = -1;
    }

    void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        if (_row != null && version == _boundVersion) return;

        // The Panel Renderer's own container isn't part of the UXML; make it fill the screen
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;

        _row = root.Q<VisualElement>("experienceRow");
        var bar = root.Q<VisualElement>("bar");
        _fill = root.Q<VisualElement>("fill");
        _level = root.Q<Label>("level");
        _value = root.Q<Label>("value");
        if (_row == null || bar == null || _fill == null || _level == null || _value == null)
        {
            Debug.LogError("ExperienceBarHUD: elements missing. Is ExperienceBar.uxml assigned as the Panel Renderer's Source Asset?", this);
            _row = null;
            return;
        }
        bar.RegisterCallback<PointerEnterEvent>(_ => bar.AddToClassList(HoveredClass));
        bar.RegisterCallback<PointerLeaveEvent>(_ => bar.RemoveFromClassList(HoveredClass));
        _shownPlayer = Entity.Null;
        _shownExperience = -1;
        _boundVersion = version;
    }

    void LateUpdate()
    {
        if (_row == null) return;
        var world = World.DefaultGameObjectInjectionWorld;
        var player = CharacterManager.Instance != null ? CharacterManager.Instance.Player : Entity.Null;
        bool hasPlayer = world != null && world.IsCreated && player != Entity.Null && world.EntityManager.HasComponent<Level>(player);
        _row.style.display = hasPlayer ? DisplayStyle.Flex : DisplayStyle.None;
        if (!hasPlayer) return;

        var level = world.EntityManager.GetComponentData<Level>(player);
        // A new player entity (respawn, new scene) shows its level without a flash
        if (player != _shownPlayer)
        {
            _shownPlayer = player;
            _shownLevel = 0;
        }
        if (level.Value != _shownLevel || level.Experience != _shownExperience) Show(level);
        _row.EnableInClassList(LevelUpClass, Time.unscaledTime < _levelUpEnd);
    }

    // The fill (full at the max level), the numbers and the badge; a flash when the level went up
    void Show(Level level)
    {
        int toNext = Experience.ToNextLevel(level.Value);
        float fraction = toNext > 0 ? Mathf.Clamp01((float)level.Experience / toNext) : 1f;
        _fill.style.width = Length.Percent(fraction * 100f);
        _value.text = toNext > 0 ? $"{level.Experience:N0} / {toNext:N0}" : "Max level";

        if (_shownLevel > 0 && level.Value > _shownLevel) _levelUpEnd = Time.unscaledTime + LevelUpSeconds;
        if (level.Value != _shownLevel) _level.text = level.Value.ToString();
        _shownLevel = level.Value;
        _shownExperience = level.Experience;
    }
}
