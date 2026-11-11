using System;
using System.Collections.Generic;
using ActiveSkills;
using Passives;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Mathematics;
using UnityEngine.UIElements;

/// <summary>
/// Skills panel toggled with the Skills action (K), over the whole screen: the active and passive slots on the left,
/// the skill wheel (ActiveSkillPicker: every skill by its requirements, equipped ones with their slot's key) in the
/// middle and the highlighted skill's card on the right. In town, clicking a skill and then an active slot (or pressing
/// that slot's key) equips it there, swapping with the slot that had it; clicking a passive slot opens a list of the
/// passives that aren't in another slot. Elsewhere it's look-only. One for both players: whoever opens it has it (their
/// buttons, focus and skills) until they close it; the other player can't open it meanwhile.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class SkillsPanelHUD : MonoBehaviour
{
    // The player who has it open (or had it last)
    int _owner;
    readonly Action<object>[] _openChanged = new Action<object>[GameInput.MaxPlayers];

    PlayerMenus Menus => MenuPanels.For(_owner);
    PlayerNav Nav => UiNav.For(_owner);
    GameInput.PlayerControls Controls => GameInput.For(_owner);

    const string HiddenClass = "skills-panel-root--hidden";
    const string PickerHiddenClass = "sp-picker--hidden";
    const string TooltipVisibleClass = "sp-tooltip--visible";
    const string ChooseClass = "sp-card--choose";
    const string TownHint = "Click a skill, then a slot · Press K to close";
    const string ChooseHint = "Click a slot or press its key · Esc to cancel";
    const string LookOnlyHint = "Change skills in town · Press K to close";
    const string GamepadTownHint = "A Choose skill · LT/RT Spokes · LB/RB Panels · B Close";
    const string GamepadChooseHint = "A Equip in slot · B Cancel";
    const string GamepadLookOnlyHint = "Change skills in town · LT/RT Spokes · LB/RB Panels · B Close";
    static readonly string[] StanceClasses = { "sp-card--stance-str", "sp-card--stance-agi", "sp-card--stance-int" };

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
        public List<SlotView> Group;                    // The slots sharing this picker
        public ScriptableObject Shown;
        public bool ShownOnce;
    }

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    bool _open;

    VisualElement _root, _panel;
    ActiveSkillPicker _wheel;      // Made in Bind: UI elements can't be made while the component is constructed
    Label _hint;
    VisualElement _tooltip;
    Label _tooltipTitle;
    VisualElement _tooltipBody;
    readonly List<SlotView> _activeSlots = new List<SlotView>();
    readonly List<SlotView> _passiveSlots = new List<SlotView>();
    SlotView _classPassive;     // The class's own passive, beside the slots: always on, can't be changed
    SlotView _resource;         // The character's resource and how it works, beside the class passive
    SlotView _selected;         // The passive slot whose picker is open
    ActiveSkill _pending;       // The skill clicked on the wheel, waiting for a slot
    bool _wheelDirty = true;
    float3 _shownAttributes;

    int _keyVersion = -1;           // GameInput.Version the key labels are from

    void Awake()
    {
        if (GameInput.Actions.asset.FindAction("Player/Skills") == null)
            Debug.LogWarning("SkillsPanelHUD: no Player/Skills input action. Reimport InputSystem_Actions.inputactions.", this);
    }

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
        // In every player's tabs; another of the owner's panels opening closes it
        for (int i = 0; i < GameInput.MaxPlayers; i++)
        {
            int player = i;
            _openChanged[i] = openPanel => OnMenuOpenChanged(player, openPanel);
            MenuPanels.For(i).OpenChanged += _openChanged[i];
            MenuPanels.For(i).AddTab(this, 1, open => SetOpenBy(player, open));
        }
    }

    void OnDisable()
    {
        for (int i = 0; i < GameInput.MaxPlayers; i++)
        {
            MenuPanels.For(i).RemoveTab(this);
            MenuPanels.For(i).OpenChanged -= _openChanged[i];
        }
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
        var wheelHolder = root.Q<VisualElement>("wheelHolder");
        var cardHolder = root.Q<VisualElement>("cardHolder");
        _tooltip = root.Q<VisualElement>("tooltip");
        _tooltipTitle = root.Q<Label>("tooltipTitle");
        _tooltipBody = root.Q<VisualElement>("tooltipBody");
        _hint = root.Q<Label>("hint");
        if (_root == null || _panel == null || activeContainer == null || passiveContainer == null || passivePicker == null || _tooltip == null ||
            _hint == null || wheelHolder == null || cardHolder == null)
        {
            Debug.LogError("SkillsPanelHUD: SkillsPanel.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }

        UiNav.Register(_root);
        _selected = null;
        _pending = null;
        if (_wheel == null) _wheel = new ActiveSkillPicker();
        wheelHolder.Clear();
        wheelHolder.Add(_wheel.Wheel);
        cardHolder.Clear();
        cardHolder.Add(_wheel.Card);
        _wheel.Picked = PickOnWheel;
        BuildActiveSlots(activeContainer);
        BuildPassiveSlots(passiveContainer, passivePicker);
        _wheelDirty = true;
        return true;
    }

    void Update()
    {
        // Any player's toggle
        for (int i = 0; i < GameInput.PlayerCount; i++)
            if (MenuPanels.For(i).TogglePressed(GameInput.For(i).Actions.asset.FindAction("Player/Skills")))
                SetOpenBy(i, !_open);

        if (!_bound || !_open) return;

        // Esc or B: lets go of the chosen skill first, then closes the passive picker, then the panel
        if (Menus.BackPressed && !Menus.IsTyping)
        {
            if (_pending != null) SetPending(null);
            else if (_selected != null) SelectPassive(null);
            else SetOpen(false);
            return;
        }
        // Skills are only changed in town; elsewhere the panel is look-only
        bool town = GameArea.IsTown;
        bool gamepad = Nav.UsingGamepad;
        if (!town && (_pending != null || _selected != null))
        {
            SetPending(null);
            SelectPassive(null);
        }
        _hint.text = !town ? (gamepad ? GamepadLookOnlyHint : LookOnlyHint)
                   : _pending != null ? (gamepad ? GamepadChooseHint : ChooseHint)
                   : gamepad ? GamepadTownHint : TownHint;

        // LT/RT: the wheel's next spoke
        if (Menus.PagePressed != 0 && _pending == null)
        {
            var node = _wheel.TurnSpoke(Menus.PagePressed);
            if (node != null) Nav.Focus(node);
        }
        // A skill waiting for a slot: that slot's key puts it there (keyboard; a controller's A is the slot it's on)
        if (_pending != null && !gamepad && Controls != null)
            for (int i = 0; i < _activeSlots.Count; i++)
                if (Controls.Skills[i].WasPressedThisFrame())
                {
                    EquipPending(i);
                    break;
                }

        // After a rebind or a switch between keyboard and gamepad
        if (_keyVersion != GameInput.Version)
        {
            _keyVersion = GameInput.Version;
            for (int i = 0; i < _activeSlots.Count; i++) _activeSlots[i].Key.text = KeyLabel(i);
            _wheelDirty = true;
        }
        foreach (var view in _activeSlots) RefreshSlot(view);
        // Stances: its card's border in the current stance's attribute colour
        for (int i = 0; i < _activeSlots.Count; i++)
        {
            var slot = ActiveSlot(i);
            int stance = slot != null && !slot.IsEmpty && slot.EquippedSkill.Type == SkillType.Stances ? slot.Variant : -1;
            for (int s = 0; s < StanceClasses.Length; s++) _activeSlots[i].Element.EnableInClassList(StanceClasses[s], stance == s);
        }
        foreach (var view in _passiveSlots) RefreshSlot(view);
        RefreshSlot(_classPassive);
        _classPassive.Element.style.display = _classPassive.Shown != null ? DisplayStyle.Flex : DisplayStyle.None;
        RefreshSlot(_resource);
        _resource.Element.style.display = _resource.Shown != null ? DisplayStyle.Flex : DisplayStyle.None;
        // No icons for resources: a disc in its bar colour
        if (_resource.Shown is Characters.ResourceDefinition resource && resource.Icon == null)
            _resource.Icon.style.backgroundColor = ResourceColors.Of(resource.Type);

        // The wheel again when a slot changed or the attributes did
        if (math.any(CurrentAttributes() != _shownAttributes)) _wheelDirty = true;
        if (_wheelDirty) BuildWheel();
    }

    // Another of its owner's menu panels opened: close this one
    void OnMenuOpenChanged(int player, object openPanel)
    {
        if (_open && player == _owner && openPanel != null && !ReferenceEquals(openPanel, this)) SetOpen(false);
    }

    // A player opens or closes it; while one has it open, the other can't
    void SetOpenBy(int player, bool open)
    {
        if (_open && player != _owner) return;
        if (open && player != _owner)
        {
            _owner = player;
            // Their slots and skills now
            foreach (var view in _activeSlots) view.ShownOnce = false;
            foreach (var view in _passiveSlots) view.ShownOnce = false;
            if (_classPassive != null) _classPassive.ShownOnce = false;
            if (_resource != null) _resource.ShownOnce = false;
            _keyVersion = -1;
        }
        SetOpen(open);
    }

    void SetOpen(bool open)
    {
        _open = open;
        Menus.SetOpen(this, open);
        if (!_bound) return;
        _root.EnableInClassList(HiddenClass, !open);
        SelectPassive(null);
        SetPending(null);
        HideTooltip();
        if (open) Nav.Register(_root);
        Nav.SetScope(this, _panel, open);
        if (!open) return;
        _wheelDirty = true;
        BuildWheel();
        // A controller starts on the first slot's skill
        var first = ActiveSlot(0)?.EquippedSkill;
        if (first != null) _wheel.Highlight(first);
        var node = _wheel.NodeOf(first);
        if (node != null) Nav.Focus(node);
    }

    // ---------- Slots ----------

    void BuildActiveSlots(VisualElement container)
    {
        container.Clear();
        _activeSlots.Clear();
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var view = new SlotView
            {
                SelectedClass = "sp-card--selected",
                Group = _activeSlots,
                GetEquipped = () => ActiveSlot(index)?.EquippedSkill,
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

            // With a skill waiting: equip it here. Otherwise: show this slot's skill on the wheel.
            Action click = () =>
            {
                if (_pending != null) EquipPending(index);
                else ShowSlotSkill(index);
            };
            view.Element.RegisterCallback<ClickEvent>(_ => click());
            UiNav.Clickable(view.Element, click);
            UiNav.Hover(view.Element, () =>
            {
                var skill = ActiveSlot(index)?.EquippedSkill;
                if (skill != null && _pending == null) _wheel.Highlight(skill);
            }, () => { });
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
                GetPool = () => PassiveSkillManager.For(_owner) != null ? PassiveSkillManager.For(_owner).SelectablePassives : null,
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

            Action click = () => SelectPassive(_selected == view || !GameArea.IsTown ? null : view);
            view.Element.RegisterCallback<ClickEvent>(_ => click());
            UiNav.Clickable(view.Element, click);
            UiNav.Hover(view.Element, () => ShowTooltip(view.GetEquipped(), view.Element), HideTooltip);
            container.Add(view.Element);
            _passiveSlots.Add(view);
        }

        // The class passive and the resource: shown like passives, tagged, with a tooltip but no picker
        _classPassive = InfoTile(container, "Class",
            () => CharacterManager.For(_owner) != null && CharacterManager.For(_owner).Class != null ? CharacterManager.For(_owner).Class.ClassPassive : null);
        _resource = InfoTile(container, "Resource", () => CharacterManager.For(_owner) != null ? CharacterManager.For(_owner).Resource : null);
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

    ActiveSkillSlot ActiveSlot(int index)
    {
        var manager = ActiveSkillManager.For(_owner);
        if (manager == null) return null;
        return index switch { 0 => manager.Slot1, 1 => manager.Slot2, 2 => manager.Slot3, _ => manager.Slot4 };
    }

    PassiveSkillSlot PassiveSlot(int index)
    {
        var manager = PassiveSkillManager.For(_owner);
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

        // An active slot changed: the wheel's keys; a passive slot's picker that's open: its choices
        if (view.Group == _activeSlots) _wheelDirty = true;
        if (_selected == view) BuildPassivePicker(view);
    }

    // ---------- Skill wheel ----------

    // Every skill, lit when the character's attributes meet its requirements now; equipped ones show their slot's key
    void BuildWheel()
    {
        _wheelDirty = false;
        var manager = ActiveSkillManager.For(_owner);
        var characters = CharacterManager.For(_owner);
        _shownAttributes = CurrentAttributes();
        _wheel.Show(manager != null ? manager.AllSkills : null, KeyOfSlot, _shownAttributes,
                    characters != null ? characters.Class : null, SkillTooltip.CurrentStats(_owner));
        _wheel.Wheel.SetPending(_pending);
    }

    float3 CurrentAttributes()
    {
        var stats = SkillTooltip.CurrentStats(_owner);
        return stats is StatBlock s ? SkillRequirements.Attributes(s) : ClassAttributes(CharacterManager.For(_owner));
    }

    // Without a player entity (yet): what the class has at the character's level
    static float3 ClassAttributes(CharacterManager characters)
    {
        if (characters == null || characters.Class == null) return float3.zero;
        var characterClass = characters.Class;
        return new float3(characterClass.StrengthPerLevel, characterClass.AgilityPerLevel, characterClass.IntelligencePerLevel) * characters.StartingLevel;
    }

    string KeyOfSlot(ActiveSkill skill)
    {
        for (int i = 0; i < _activeSlots.Count; i++)
            if (ActiveSlot(i)?.EquippedSkill == skill) return KeyLabel(i);
        return null;
    }

    // A skill clicked on the wheel: in town it waits for a slot; elsewhere it's only shown
    void PickOnWheel(ActiveSkill skill)
    {
        _wheel.Highlight(skill);
        if (!GameArea.IsTown) return;
        SelectPassive(null);
        SetPending(_pending == skill ? null : skill);
        // A controller goes to the slots to pick one
        if (_pending != null && Nav.UsingGamepad && _activeSlots.Count > 0) Nav.Focus(_activeSlots[0].Element);
    }

    void SetPending(ActiveSkill skill)
    {
        _pending = skill;
        _wheel?.Wheel.SetPending(skill);
        foreach (var view in _activeSlots) view.Element.EnableInClassList(ChooseClass, skill != null);
        if (_wheel == null) return;
        _wheel.SetStatus(skill != null ? "Choose a slot for it" : "");
        if (skill != null) _wheel.Highlight(skill);
    }

    // Equips the waiting skill in a slot; a skill from another slot swaps places with that slot's
    void EquipPending(int index)
    {
        var skill = _pending;
        SetPending(null);
        if (skill == null || !GameArea.IsTown) return;
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
        BuildWheel();
        var node = _wheel.NodeOf(skill);
        if (node != null) Nav.Focus(node);
    }

    // A slot clicked with nothing waiting: its skill on the card, and focus on its node
    void ShowSlotSkill(int index)
    {
        var skill = ActiveSlot(index)?.EquippedSkill;
        if (skill == null) return;
        _wheel.Highlight(skill);
        var node = _wheel.NodeOf(skill);
        if (node != null && Nav.UsingGamepad) Nav.Focus(node);
    }

    // ---------- Passive picker ----------

    void SelectPassive(SlotView view)
    {
        if (_selected != null)
        {
            _selected.Element.RemoveFromClassList(_selected.SelectedClass);
            _selected.Picker.AddToClassList(PickerHiddenClass);
            Nav.SetScope(_selected.Picker, _selected.Picker, false);
        }
        _selected = view;
        if (view == null) return;

        SetPending(null);
        view.Element.AddToClassList(view.SelectedClass);
        BuildPassivePicker(view);
        view.Picker.RemoveFromClassList(PickerHiddenClass);
        // A controller picks in the picker until it closes, starting on the equipped passive
        Nav.SetScope(view.Picker, view.Picker, true);
        var current = view.Picker.Q(className: "sp-option--current");
        Nav.FocusFirstOf(current != null ? new[] { current } : view.Picker.Children());
    }

    /// <summary>The pool minus passives equipped in the other slots (the slot's own is kept and marked).</summary>
    void BuildPassivePicker(SlotView view)
    {
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
            SelectPassive(null);
            HideTooltip();
        };
        option.RegisterCallback<ClickEvent>(_ => pick());
        UiNav.Clickable(option, pick);
        UiNav.Hover(option, () => ShowTooltip(skill, option), HideTooltip);
        return option;
    }

    // ---------- Tooltip (passives, class passive and resource) ----------

    void ShowTooltip(ScriptableObject skill, VisualElement anchor)
    {
        if (skill == null) { HideTooltip(); return; }

        Describe(skill, out var name, out _, out var description);
        _tooltipTitle.text = name;
        _tooltipBody.Clear();
        if (!string.IsNullOrEmpty(description))
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
