using ActiveSkills;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shows its player's four skill slots (ActiveSkillManager) in SkillBar.uxml: icon, key (for their device used last), cooldown sweep and countdown,
/// pressed/active highlight and "not enough resource" tint. Hovering a slot shows its skill's tooltip (SkillTooltip,
/// which reads the player's stats). Read-only: never changes ECS.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
[DefaultExecutionOrder(100)]  // After ActiveSkillManager.LateUpdate has mirrored this frame's state into the slots
public class SkillBarHUD : MonoBehaviour, IPerPlayer
{
    /// <summary>Whose HUD this is: 0 is player 1 (the rig's own); player 2's copy is made by CoopManager.</summary>
    public int PlayerIndex { get; set; }

    const string EmptyClass = "skill-slot--empty";
    const string PressedClass = "skill-slot--pressed";
    const string ActiveClass = "skill-slot--active";
    const string CooldownClass = "skill-slot--cooldown";
    const string NoResourceClass = "skill-slot--no-resource";
    const string LockedClass = "skill-slot--locked";

    class SlotView
    {
        public VisualElement Slot;
        public VisualElement Icon;
        public Label Fallback;
        public Label CooldownText;
        public CooldownSweep Sweep;
        public Label Key;

        // Last shown values, so the UI is only touched when something changes
        public ActiveSkill ShownSkill;
        public Sprite ShownIcon;
        public int ShownCooldownTenths = -1;
    }

    const string TooltipVisibleClass = "skill-tooltip--visible";

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    VisualElement _barRoot, _tooltip, _tooltipBody;
    Label _tooltipTitle;
    int _hovered = -1;              // Slot index under the pointer, -1 for none
    ActiveSkill _tooltipSkill;      // The skill the tooltip shows
    readonly SlotView[] _views = new SlotView[4];
    int _labelVersion = -1;         // GameInput.Version the key labels are from

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
        // The player's half of the screen in co-op
        PlayerHud.Register(root, PlayerIndex);

        _bound = Bind(root);
        _boundVersion = version;
    }

    void LateUpdate()
    {
        if (!_bound) return;

        // The keys of the device used last, after a rebind or a switch between keyboard and gamepad
        if (_labelVersion != GameInput.Version)
        {
            _labelVersion = GameInput.Version;
            for (int i = 0; i < _views.Length; i++) UpdateKey(_views[i], i);
        }

        for (int i = 0; i < _views.Length; i++)
            UpdateSlot(_views[i], SlotAt(i));

        // The hovered slot's skill changed (or it was emptied): refresh or hide its tooltip
        var hoveredSkill = _hovered >= 0 ? SlotAt(_hovered)?.EquippedSkill : null;
        if (hoveredSkill != _tooltipSkill) ShowTooltip(_hovered);
    }

    ActiveSkillSlot SlotAt(int index)
    {
        var manager = ActiveSkillManager.For(PlayerIndex);
        if (manager == null) return null;
        return index switch { 0 => manager.Slot1, 1 => manager.Slot2, 2 => manager.Slot3, 3 => manager.Slot4, _ => null };
    }

    // ---------- Tooltip ----------

    // Above the slot, left-aligned with it: name, then SkillTooltip's body with the player's current stats
    void ShowTooltip(int index)
    {
        var skill = index >= 0 ? SlotAt(index)?.EquippedSkill : null;
        _tooltipSkill = skill;
        if (skill == null || _tooltip == null)
        {
            _tooltip?.RemoveFromClassList(TooltipVisibleClass);
            return;
        }

        _tooltipTitle.text = string.IsNullOrEmpty(skill.Name) ? skill.name : skill.Name;
        SkillTooltip.Fill(_tooltipBody, skill, SkillTooltip.CurrentStats(PlayerIndex));

        var slot = _views[index].Slot;
        var position = slot.ChangeCoordinatesTo(_barRoot, Vector2.zero);
        _tooltip.style.left = position.x;
        _tooltip.style.bottom = _barRoot.layout.height - position.y + 8f;
        _tooltip.AddToClassList(TooltipVisibleClass);
    }

    void HideTooltip(int index)
    {
        if (_hovered != index) return;
        _hovered = -1;
        ShowTooltip(-1);
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
                Key = slot.parent.Q<Label>("key"),
            };
            int index = i;
            slot.RegisterCallback<PointerEnterEvent>(_ => { _hovered = index; ShowTooltip(index); });
            slot.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip(index));
        }

        _barRoot = root.Q<VisualElement>("skillBarRoot");
        _tooltip = root.Q<VisualElement>("tooltip");
        _tooltipTitle = root.Q<Label>("tooltipTitle");
        _tooltipBody = root.Q<VisualElement>("tooltipBody");
        if (_barRoot == null || _tooltip == null || _tooltipTitle == null || _tooltipBody == null)
        {
            Debug.LogWarning("SkillBarHUD: SkillBar.uxml has no tooltip elements, so hovering a slot shows nothing.", this);
            _tooltip = null;
        }
        _hovered = -1;
        _tooltipSkill = null;
        _labelVersion = -1;
        return true;
    }

    void UpdateKey(SlotView view, int index)
    {
        if (view.Key == null) return;
        var controls = GameInput.For(PlayerIndex);
        string key = controls != null ? controls.Label(controls.Skills[index]) : "";
        view.Key.text = key;
        view.Key.style.visibility = string.IsNullOrEmpty(key) ? Visibility.Hidden : Visibility.Visible;
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
        // Requirements not met: greyed out (still usable outside dungeons)
        view.Slot.EnableInClassList(LockedClass, !empty && !slot.MeetsRequirements);
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
