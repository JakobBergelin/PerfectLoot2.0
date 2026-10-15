using ActiveSkills;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shows ActiveSkillManager's four slots in SkillBar.uxml: icon, key, cooldown sweep and countdown,
/// pressed/active highlight and "not enough resource" tint. Read-only: never touches ECS.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
[DefaultExecutionOrder(100)]  // After ActiveSkillManager.LateUpdate has mirrored this frame's state into the slots
public class SkillBarHUD : MonoBehaviour
{
    const string EmptyClass = "skill-slot--empty";
    const string PressedClass = "skill-slot--pressed";
    const string ActiveClass = "skill-slot--active";
    const string CooldownClass = "skill-slot--cooldown";
    const string NoResourceClass = "skill-slot--no-resource";

    class SlotView
    {
        public VisualElement Slot;
        public VisualElement Icon;
        public Label Fallback;
        public Label CooldownText;
        public CooldownSweep Sweep;

        // Last shown values, so the UI is only touched when something changes
        public ActiveSkill ShownSkill;
        public Sprite ShownIcon;
        public int ShownCooldownTenths = -1;
    }

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    readonly SlotView[] _views = new SlotView[4];

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        // Called on initial setup and whenever the UI is rebuilt (e.g. the UXML is reimported)
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDisable()
    {
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        _bound = false;
        _boundVersion = -1;
    }

    void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        // Same UI as last time: already bound
        if (_bound && version == _boundVersion) return;

        // The container the Panel Renderer creates isn't part of SkillBar.uxml, so it can't be styled from USS.
        // Make it fill the screen so the bar (anchored to its bottom edge) ends up at the bottom of the screen.
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;

        _bound = Bind(root);
        _boundVersion = version;
    }

    void LateUpdate()
    {
        if (!_bound) return;

        var manager = ActiveSkillManager.Instance;
        UpdateSlot(_views[0], manager != null ? manager.Slot1 : null);
        UpdateSlot(_views[1], manager != null ? manager.Slot2 : null);
        UpdateSlot(_views[2], manager != null ? manager.Slot3 : null);
        UpdateSlot(_views[3], manager != null ? manager.Slot4 : null);
    }

    bool Bind(VisualElement root)
    {
        for (int i = 0; i < _views.Length; i++)
        {
            var slot = root.Q<VisualElement>($"slot{i + 1}");
            if (slot == null)
            {
                Debug.LogError($"SkillBarHUD: no element named slot{i + 1}. Is SkillBar.uxml assigned as the Panel Renderer's Source Asset?", this);
                return false;
            }
            _views[i] = new SlotView
            {
                Slot = slot,
                Icon = slot.Q<VisualElement>("icon"),
                Fallback = slot.Q<Label>("fallback"),
                CooldownText = slot.Q<Label>("cooldownText"),
                Sweep = slot.Q<CooldownSweep>("cooldownSweep"),
            };
        }
        return true;
    }

    static void UpdateSlot(SlotView view, ActiveSkillSlot slot)
    {
        var skill = slot != null ? slot.EquippedSkill : null;
        bool empty = skill == null;
        view.Slot.EnableInClassList(EmptyClass, empty);

        // Icon, or the skill's name while it has no icon
        var icon = skill != null ? skill.Icon : null;
        if (skill != view.ShownSkill || icon != view.ShownIcon)
        {
            // Per-slot image can only be set from code; all other styling is in SkillBar.uss
            view.Icon.style.backgroundImage = icon != null ? new StyleBackground(icon) : new StyleBackground(StyleKeyword.None);
            view.Fallback.text = skill != null && icon == null ? (string.IsNullOrEmpty(skill.Name) ? skill.name : skill.Name) : string.Empty;
            view.ShownSkill = skill;
            view.ShownIcon = icon;
        }

        bool onCooldown = !empty && slot.IsOnCooldown;
        view.Slot.EnableInClassList(PressedClass, !empty && slot.IsPressed);
        view.Slot.EnableInClassList(ActiveClass, !empty && slot.IsActive);
        view.Slot.EnableInClassList(NoResourceClass, !empty && !slot.HasEnoughResource);
        view.Slot.EnableInClassList(CooldownClass, onCooldown);

        view.Sweep.Fraction = onCooldown ? slot.CooldownFraction : 0f;

        // Whole seconds above 1s, tenths below; only rebuild the string when the shown value changes
        int tenths = onCooldown ? Mathf.CeilToInt(slot.CooldownRemaining * 10f) : 0;
        if (tenths != view.ShownCooldownTenths)
        {
            view.ShownCooldownTenths = tenths;
            view.CooldownText.text = !onCooldown ? string.Empty
                : tenths >= 10 ? Mathf.CeilToInt(tenths / 10f).ToString()
                : (tenths / 10f).ToString("0.0");
        }
    }
}
