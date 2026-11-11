using System;
using System.Collections.Generic;
using ActiveSkills;
using Passives;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Mathematics;
using UnityEngine.UIElements;

/// <summary>
/// Skills panel toggled with the Skills action (K): the equipped active and passive skills. Clicking an active slot
/// shows the skill wheel instead of the slots (ActiveSkillPicker: every skill by its requirements, the highlighted
/// one's details); picking one equips it, swapping with the slot that had it. Clicking a passive slot opens a list
/// of the passives that aren't in another slot. Skills are changed in town only.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class SkillsPanelHUD : MonoBehaviour, IPerPlayer
{
    /// <summary>Whose HUD this is: 0 is player 1 (the rig's own); player 2's copy is made by CoopManager.</summary>
    public int PlayerIndex { get; set; }

    PlayerMenus Menus => MenuPanels.For(PlayerIndex);
    PlayerNav Nav => UiNav.For(PlayerIndex);
    GameInput.PlayerControls Controls => GameInput.For(PlayerIndex);

    const string HiddenClass = "skills-panel-root--hidden";
    const string PickerHiddenClass = "sp-picker--hidden";
    const string TooltipVisibleClass = "sp-tooltip--visible";
    const string TownHint = "Click a slot to change it · Press K to close";
    const string LookOnlyHint = "Change skills in town · Press K to close";
    const string GamepadTownHint = "A Change · LB/RB Panels · B Close";
    const string GamepadLookOnlyHint = "Change skills in town · LB/RB Panels · B Close";
    const string WheelHint = "Click a skill to equip it · Esc to go back";
    const string GamepadWheelHint = "A Equip · LT/RT Spokes · B Back";
    const string WideClass = "skills-panel--wide";

    /// <summary>One slot in the panel; active and passive slots only differ in these delegates and their look.</summary>
    class SlotView
    {
        public VisualElement Element;
        public VisualElement Icon;
        public Label Name;
        public Label Key;                               // Active slots: the key that uses it
        public string SelectedClass;
        public VisualElement Picker;
        public Func<ScriptableObject> GetEquipped;
        public Action<ScriptableObject> Equip;
        public Func<IEnumerable<ScriptableObject>> GetPool;
        public Func<ScriptableObject, int> TypeOf;      // Skills of the same type can't be in two slots
        public List<SlotView> Group;                    // The slots sharing this picker (active or passive)
        public ScriptableObject Shown;
        public bool ShownOnce;
    }

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    bool _open;

    VisualElement _root, _panel;
    VisualElement _slotsPage, _wheelPage;
    Label _wheelTitle;
    ActiveSkillPicker _wheel;      // Made in Bind: UI elements can't be made while the component is constructed
    Label _hint;
    VisualElement _tooltip;
    Label _tooltipTitle;
    VisualElement _tooltipBody;
    readonly List<SlotView> _activeSlots = new List<SlotView>();
    readonly List<SlotView> _passiveSlots = new List<SlotView>();
    SlotView _classPassive;     // The class's own passive, beside the slots: always on, can't be changed
    SlotView _resource;         // The character's resource and how it works, beside the class passive
    SlotView _selected;

    InputAction _toggleAction;
    int _keyVersion = -1;           // GameInput.Version the key labels are from

    void Awake()
    {
        _toggleAction = Controls.Actions.asset.FindAction("Player/Skills");
        if (_toggleAction == null)
            Debug.LogWarning("SkillsPanelHUD: no Player/Skills input action. Reimport InputSystem_Actions.inputactions.", this);
    }

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
        Menus.OpenChanged += OnMenuOpenChanged;
        Menus.AddTab(this, 1, SetOpen);
    }

    void OnDisable()
    {
        Menus.RemoveTab(this);
        Menus.OpenChanged -= OnMenuOpenChanged;
        if (_open) SetOpen(false);
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        _bound = false;
        _boundVersion = -1;
    }

    void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        if (_bound && version == _boundVersion) return;

        // The Panel Renderer's own container isn't part of the UXML; make it fill the screen
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        // The player's half of the screen in co-op
        PlayerHud.Register(root, PlayerIndex);

        _bound = Bind(root);
        _boundVersion = version;
        if (_bound) SetOpen(_open);
    }

    bool Bind(VisualElement root)
    {
        _root = root.Q<VisualElement>("skillsPanelRoot");
        _panel = root.Q<VisualElement>("panel");
        var activeContainer = root.Q<VisualElement>("activeSlots");
        var passiveContainer = root.Q<VisualElement>("passiveSlots");
        var passivePicker = root.Q<VisualElement>("passivePicker");
        _slotsPage = root.Q<VisualElement>("slotsPage");
        _wheelPage = root.Q<VisualElement>("wheelPage");
        _wheelTitle = root.Q<Label>("wheelTitle");
        var wheelHolder = root.Q<VisualElement>("wheelHolder");
        _tooltip = root.Q<VisualElement>("tooltip");
        _tooltipTitle = root.Q<Label>("tooltipTitle");
        _tooltipBody = root.Q<VisualElement>("tooltipBody");
        _hint = root.Q<Label>("hint");
        if (_root == null || _panel == null || activeContainer == null || passiveContainer == null || passivePicker == null || _tooltip == null ||
            _hint == null || _slotsPage == null || _wheelPage == null || _wheelTitle == null || wheelHolder == null)
        {
            Debug.LogError("SkillsPanelHUD: SkillsPanel.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }

        Nav.Register(_root);
        _selected = null;
        if (_wheel == null) _wheel = new ActiveSkillPicker();
        wheelHolder.Clear();
        wheelHolder.Add(_wheel.Element);
        _wheel.Picked = PickActive;
        BuildActiveSlots(activeContainer, _wheelPage);
        BuildPassiveSlots(passiveContainer, passivePicker);
        return true;
    }

    void Update()
    {
        if (Menus.TogglePressed(_toggleAction))
            SetOpen(!_open);

        if (!_bound || !_open) return;

        // Esc or B closes the picker first, then the panel
        if (Menus.BackPressed && !Menus.IsTyping)
        {
            if (_selected != null) Select(null);
            else SetOpen(false);
            return;
        }
        // Skills are only changed in town; elsewhere the panel is look-only
        bool town = GameArea.IsTown;
        bool gamepad = Nav.UsingGamepad;
        bool wheel = _selected != null && _selected.Group == _activeSlots;
        _hint.text = wheel ? (gamepad ? GamepadWheelHint : WheelHint)
                   : town ? (gamepad ? GamepadTownHint : TownHint) : (gamepad ? GamepadLookOnlyHint : LookOnlyHint);
        if (!town && _selected != null) Select(null);
        // LT/RT: the wheel's next spoke
        if (wheel && Menus.PagePressed != 0)
        {
            var node = _wheel.TurnSpoke(Menus.PagePressed);
            if (node != null) Nav.Focus(node);
        }

        // After a rebind or a switch between keyboard and gamepad
        if (_keyVersion != GameInput.Version)
        {
            _keyVersion = GameInput.Version;
            for (int i = 0; i < _activeSlots.Count; i++) _activeSlots[i].Key.text = KeyLabel(i);
        }
        foreach (var view in _activeSlots) RefreshSlot(view);
        foreach (var view in _passiveSlots) RefreshSlot(view);
        RefreshSlot(_classPassive);
        _classPassive.Element.style.display = _classPassive.Shown != null ? DisplayStyle.Flex : DisplayStyle.None;
        RefreshSlot(_resource);
        _resource.Element.style.display = _resource.Shown != null ? DisplayStyle.Flex : DisplayStyle.None;
        // No icons for resources: a disc in its bar colour
        if (_resource.Shown is Characters.ResourceDefinition resource && resource.Icon == null)
            _resource.Icon.style.backgroundColor = ResourceColors.Of(resource.Type);
    }

    // Another menu panel opened: close this one
    void OnMenuOpenChanged(object openPanel)
    {
        if (_open && openPanel != null && !ReferenceEquals(openPanel, this)) SetOpen(false);
    }

    void SetOpen(bool open)
    {
        _open = open;
        Menus.SetOpen(this, open);
        if (!_bound) return;
        _root.EnableInClassList(HiddenClass, !open);
        Select(null);
        HideTooltip();
        Nav.SetScope(this, _panel, open);
    }

    // ---------- Slots ----------

    void BuildActiveSlots(VisualElement container, VisualElement picker)
    {
        container.Clear();
        _activeSlots.Clear();
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var view = new SlotView
            {
                SelectedClass = "sp-card--selected",
                Picker = picker,
                Group = _activeSlots,
                GetEquipped = () => ActiveSlot(index)?.EquippedSkill,
                Equip = skill => ActiveSlot(index)?.EquipSkill((ActiveSkill)skill),
                GetPool = () => ActiveSkillManager.For(PlayerIndex) != null ? ActiveSkillManager.For(PlayerIndex).AllSkills : null,
                TypeOf = skill => (int)((ActiveSkill)skill).Type,
            };

            view.Element = new VisualElement();
            view.Element.AddToClassList("sp-card");
            view.Icon = new VisualElement { pickingMode = PickingMode.Ignore };
            view.Icon.AddToClassList("sp-card__icon");
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("sp-card__text");
            view.Name = new Label { pickingMode = PickingMode.Ignore };
            view.Name.AddToClassList("sp-card__name");
            view.Key = new Label(KeyLabel(index)) { pickingMode = PickingMode.Ignore };
            view.Key.AddToClassList("sp-card__key");
            text.Add(view.Name);
            text.Add(view.Key);
            view.Element.Add(view.Icon);
            view.Element.Add(text);

            RegisterSlotCallbacks(view);
            container.Add(view.Element);
            _activeSlots.Add(view);
        }
    }

    void BuildPassiveSlots(VisualElement container, VisualElement picker)
    {
        container.Clear();
        _passiveSlots.Clear();
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            var view = new SlotView
            {
                SelectedClass = "sp-passive--selected",
                Picker = picker,
                Group = _passiveSlots,
                GetEquipped = () => PassiveSlot(index)?.EquippedPassive,
                Equip = passive => PassiveSlot(index)?.EquipPassive((PassiveSkill)passive),
                GetPool = () => PassiveSkillManager.For(PlayerIndex) != null ? PassiveSkillManager.For(PlayerIndex).SelectablePassives : null,
                TypeOf = passive => (int)((PassiveSkill)passive).Type,
            };

            view.Element = new VisualElement();
            view.Element.AddToClassList("sp-passive");
            view.Icon = new VisualElement { pickingMode = PickingMode.Ignore };
            view.Icon.AddToClassList("sp-passive__icon");
            view.Name = new Label { pickingMode = PickingMode.Ignore };
            view.Name.AddToClassList("sp-passive__name");
            view.Element.Add(view.Icon);
            view.Element.Add(view.Name);

            RegisterSlotCallbacks(view);
            container.Add(view.Element);
            _passiveSlots.Add(view);
        }

        // The class passive and the resource: shown like passives, tagged, with a tooltip but no picker
        _classPassive = InfoTile(container, "Class",
            () => CharacterManager.For(PlayerIndex) != null && CharacterManager.For(PlayerIndex).Class != null ? CharacterManager.For(PlayerIndex).Class.ClassPassive : null);
        _resource = InfoTile(container, "Resource", () => CharacterManager.For(PlayerIndex) != null ? CharacterManager.For(PlayerIndex).Resource : null);
    }

    SlotView InfoTile(VisualElement container, string tagText, Func<ScriptableObject> getShown)
    {
        var view = new SlotView { GetEquipped = getShown };
        view.Element = new VisualElement();
        view.Element.AddToClassList("sp-passive");
        view.Element.AddToClassList("sp-passive--class");
        view.Icon = new VisualElement { pickingMode = PickingMode.Ignore };
        view.Icon.AddToClassList("sp-passive__icon");
        view.Name = new Label { pickingMode = PickingMode.Ignore };
        view.Name.AddToClassList("sp-passive__name");
        var tag = new Label(tagText) { pickingMode = PickingMode.Ignore };
        tag.AddToClassList("sp-passive__class-tag");
        view.Element.Add(view.Icon);
        view.Element.Add(view.Name);
        view.Element.Add(tag);
        UiNav.Hover(view.Element, () => ShowTooltip(view.GetEquipped(), view.Element), HideTooltip);
        container.Add(view.Element);
        return view;
    }

    void RegisterSlotCallbacks(SlotView view)
    {
        Action click = () => Select(_selected == view || !GameArea.IsTown ? null : view);
        view.Element.RegisterCallback<ClickEvent>(_ => click());
        UiNav.Clickable(view.Element, click);
        UiNav.Hover(view.Element, () => ShowTooltip(view.GetEquipped(), view.Element), HideTooltip);
    }

    ActiveSkillSlot ActiveSlot(int index)
    {
        var manager = ActiveSkillManager.For(PlayerIndex);
        if (manager == null) return null;
        return index switch { 0 => manager.Slot1, 1 => manager.Slot2, 2 => manager.Slot3, _ => manager.Slot4 };
    }

    PassiveSkillSlot PassiveSlot(int index)
    {
        var manager = PassiveSkillManager.For(PlayerIndex);
        if (manager == null) return null;
        return index switch { 0 => manager.Slot1, 1 => manager.Slot2, _ => manager.Slot3 };
    }

    string KeyLabel(int index)
    {
        var label = Controls != null ? Controls.Label(Controls.Skills[index]) : "";
        return string.IsNullOrEmpty(label) ? "Unbound" : label;
    }

    void RefreshSlot(SlotView view)
    {
        var skill = view.GetEquipped();
        if (view.ShownOnce && skill == view.Shown) return;
        view.Shown = skill;
        view.ShownOnce = true;

        Describe(skill, out var name, out var icon, out _);
        view.Name.text = skill == null ? "Empty" : name;
        view.Icon.style.backgroundImage = icon != null ? new StyleBackground(icon) : new StyleBackground(StyleKeyword.None);

        // The equipped skill changed from elsewhere while its picker was open: rebuild the choices
        if (_selected == view) BuildPicker(view);
    }

    // ---------- Picker ----------

    void Select(SlotView view)
    {
        if (_selected != null)
        {
            _selected.Element.RemoveFromClassList(_selected.SelectedClass);
            _selected.Picker.AddToClassList(PickerHiddenClass);
            Nav.SetScope(_selected.Picker, _selected.Picker, false);
        }
        // An active slot's wheel takes the place of the slots, in a wider panel
        bool wheel = view != null && view.Group == _activeSlots;
        _slotsPage.EnableInClassList(PickerHiddenClass, wheel);
        _panel.EnableInClassList(WideClass, wheel);
        _selected = view;
        if (view == null) return;

        view.Element.AddToClassList(view.SelectedClass);
        BuildPicker(view);
        view.Picker.RemoveFromClassList(PickerHiddenClass);
        // A controller picks in the picker until it closes, starting on the equipped skill
        Nav.SetScope(view.Picker, view.Picker, true);
        if (wheel)
        {
            Nav.Focus(_wheel.StartNode);
            return;
        }
        var current = view.Picker.Q(className: "sp-option--current");
        Nav.FocusFirstOf(current != null ? new[] { current } : view.Picker.Children());
    }

    /// <summary>
    /// The wheel for an active slot; for a passive slot, the pool minus passives equipped in the other slots (the
    /// slot's own is kept and marked).
    /// </summary>
    void BuildPicker(SlotView view)
    {
        if (view.Group == _activeSlots)
        {
            BuildWheel(view);
            return;
        }
        var picker = view.Picker;
        picker.Clear();
        HideTooltip();

        var takenTypes = new HashSet<int>();
        foreach (var other in view.Group)
        {
            if (other == view) continue;
            var otherSkill = other.GetEquipped();
            if (otherSkill != null) takenTypes.Add(view.TypeOf(otherSkill));
        }

        var current = view.GetEquipped();
        var pool = view.GetPool();
        int count = 0;
        if (pool != null)
        {
            foreach (var skill in pool)
            {
                if (skill == null || takenTypes.Contains(view.TypeOf(skill))) continue;
                picker.Add(CreateOption(view, skill, skill == current));
                count++;
            }
        }

        if (count == 0)
        {
            var empty = new Label("No other skills available");
            empty.AddToClassList("sp-picker__empty");
            picker.Add(empty);
        }
    }

    // ---------- Skill wheel (active slots) ----------

    // Every skill, lit when the character's attributes meet its requirements now; the ones in other slots show their key
    void BuildWheel(SlotView view)
    {
        HideTooltip();
        int index = _activeSlots.IndexOf(view);
        _wheelTitle.text = $"Choose a skill for {KeyLabel(index)}";
        var manager = ActiveSkillManager.For(PlayerIndex);
        var characters = CharacterManager.For(PlayerIndex);
        var stats = SkillTooltip.CurrentStats(PlayerIndex);
        _wheel.Open(manager != null ? manager.AllSkills : null, ActiveSlot(index)?.EquippedSkill, skill => KeyOfOtherSlot(skill, index),
                    stats is StatBlock s ? new float3(s.Strength, s.Agility, s.Intelligence) : ClassAttributes(characters),
                    characters != null ? characters.Class : null, stats);
    }

    // Without a player entity (yet): what the class has at the character's level
    static float3 ClassAttributes(CharacterManager characters)
    {
        if (characters == null || characters.Class == null) return float3.zero;
        var characterClass = characters.Class;
        return new float3(characterClass.StrengthPerLevel, characterClass.AgilityPerLevel, characterClass.IntelligencePerLevel) * characters.StartingLevel;
    }

    string KeyOfOtherSlot(ActiveSkill skill, int index)
    {
        for (int i = 0; i < _activeSlots.Count; i++)
            if (i != index && ActiveSlot(i)?.EquippedSkill == skill) return KeyLabel(i);
        return null;
    }

    // Equips it in the chosen slot; a skill from another slot swaps places with the chosen slot's
    void PickActive(ActiveSkill skill)
    {
        if (_selected == null || _selected.Group != _activeSlots || !GameArea.IsTown) return;
        int index = _activeSlots.IndexOf(_selected);
        var slot = ActiveSlot(index);
        var current = slot != null ? slot.EquippedSkill : null;
        if (slot != null && skill != current)
        {
            ActiveSkillSlot other = null;
            for (int i = 0; i < _activeSlots.Count; i++)
                if (i != index && ActiveSlot(i)?.EquippedSkill == skill) other = ActiveSlot(i);
            if (other != null) other.UnequipSkill();
            slot.EquipSkill(skill);
            if (other != null && current != null) other.EquipSkill(current);
        }
        Select(null);
        HideTooltip();
    }

    VisualElement CreateOption(SlotView view, ScriptableObject skill, bool isCurrent)
    {
        Describe(skill, out var name, out var icon, out _);

        var option = new VisualElement();
        option.AddToClassList("sp-option");
        if (isCurrent) option.AddToClassList("sp-option--current");
        var iconElement = new VisualElement { pickingMode = PickingMode.Ignore };
        iconElement.AddToClassList("sp-option__icon");
        if (icon != null) iconElement.style.backgroundImage = new StyleBackground(icon);
        var label = new Label(name) { pickingMode = PickingMode.Ignore };
        label.AddToClassList("sp-option__name");
        option.Add(iconElement);
        option.Add(label);

        Action pick = () =>
        {
            if (!isCurrent) view.Equip(skill);
            Select(null);
            HideTooltip();
        };
        option.RegisterCallback<ClickEvent>(_ => pick());
        UiNav.Clickable(option, pick);
        UiNav.Hover(option, () => ShowTooltip(skill, option), HideTooltip);
        return option;
    }

    // ---------- Tooltip ----------

    void ShowTooltip(ScriptableObject skill, VisualElement anchor)
    {
        if (skill == null) { HideTooltip(); return; }

        Describe(skill, out var name, out _, out var description);
        _tooltipTitle.text = name;

        // Active skills: tags, cost, cooldown, damage and effects with the player's stats (SkillTooltip); passives: their description
        _tooltipBody.Clear();
        if (skill is ActiveSkill active)
            SkillTooltip.Fill(_tooltipBody, active, SkillTooltip.CurrentStats(PlayerIndex));
        else if (!string.IsNullOrEmpty(description))
            _tooltipBody.Add(new Label(description) { pickingMode = PickingMode.Ignore });
        _tooltipBody.style.display = _tooltipBody.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;

        // Right of the hovered element, in the root's coordinates
        var position = anchor.ChangeCoordinatesTo(_root, new Vector2(anchor.layout.width + 8f, 0f));
        _tooltip.style.left = position.x;
        _tooltip.style.top = position.y;
        _tooltip.AddToClassList(TooltipVisibleClass);
    }

    void HideTooltip() => _tooltip?.RemoveFromClassList(TooltipVisibleClass);

    static void Describe(ScriptableObject skill, out string name, out Sprite icon, out string description)
    {
        switch (skill)
        {
            case ActiveSkill active:
                name = string.IsNullOrEmpty(active.Name) ? active.name : active.Name;
                icon = active.Icon;
                description = active.Description;
                break;
            case PassiveSkill passive:
                name = string.IsNullOrEmpty(passive.Name) ? passive.name : passive.Name;
                icon = passive.Icon;
                description = passive.Description;
                break;
            case Characters.ResourceDefinition resource:
                name = string.IsNullOrEmpty(resource.Name) ? resource.name : resource.Name;
                icon = resource.Icon;
                description = string.IsNullOrWhiteSpace(resource.Description) ? resource.Explanation : resource.Description + "\n\n" + resource.Explanation;
                break;
            default:
                name = "";
                icon = null;
                description = "";
                break;
        }
    }
}
