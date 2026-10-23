using System;
using System.Collections.Generic;
using System.Text;
using ActiveSkills;
using Passives;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Skills panel toggled with the Skills action (K): the equipped active and passive skills. Clicking a slot opens
/// a picker with the skills from the manager's pool that aren't in another slot; picking one equips it.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class SkillsPanelHUD : MonoBehaviour
{
    const string HiddenClass = "skills-panel-root--hidden";
    const string PickerHiddenClass = "sp-picker--hidden";
    const string TooltipVisibleClass = "sp-tooltip--visible";

    /// <summary>One slot in the panel; active and passive slots only differ in these delegates and their look.</summary>
    class SlotView
    {
        public VisualElement Element;
        public VisualElement Icon;
        public Label Name;
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

    VisualElement _root;
    VisualElement _tooltip;
    Label _tooltipTitle, _tooltipBody;
    readonly List<SlotView> _activeSlots = new List<SlotView>();
    readonly List<SlotView> _passiveSlots = new List<SlotView>();
    SlotView _selected;

    InputSystem_Actions _actions;
    InputAction _toggleAction;
    InputAction[] _skillActions;

    void Awake()
    {
        _actions = new InputSystem_Actions();
        _toggleAction = _actions.asset.FindAction("Player/Skills");
        if (_toggleAction == null)
            Debug.LogWarning("SkillsPanelHUD: no Player/Skills input action. Reimport InputSystem_Actions.inputactions.", this);
        // Only used for the key labels
        _skillActions = new[] { _actions.Player.Skill1, _actions.Player.Skill2, _actions.Player.Skill3, _actions.Player.Skill4 };
    }

    void OnDestroy()
    {
        _actions?.Dispose();
    }

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
        _toggleAction?.Enable();
        MenuPanels.OpenChanged += OnMenuOpenChanged;
    }

    void OnDisable()
    {
        MenuPanels.OpenChanged -= OnMenuOpenChanged;
        if (_open) SetOpen(false);
        _toggleAction?.Disable();
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
        var activeContainer = root.Q<VisualElement>("activeSlots");
        var activePicker = root.Q<VisualElement>("activePicker");
        var passiveContainer = root.Q<VisualElement>("passiveSlots");
        var passivePicker = root.Q<VisualElement>("passivePicker");
        _tooltip = root.Q<VisualElement>("tooltip");
        _tooltipTitle = root.Q<Label>("tooltipTitle");
        _tooltipBody = root.Q<Label>("tooltipBody");
        if (_root == null || activeContainer == null || activePicker == null || passiveContainer == null || passivePicker == null || _tooltip == null)
        {
            Debug.LogError("SkillsPanelHUD: SkillsPanel.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }

        _selected = null;
        BuildActiveSlots(activeContainer, activePicker);
        BuildPassiveSlots(passiveContainer, passivePicker);
        return true;
    }

    void Update()
    {
        if (_toggleAction != null && _toggleAction.WasPressedThisFrame() && !MenuPanels.IsTyping)
            SetOpen(!_open);

        if (!_bound || !_open) return;

        // Esc closes the picker first, then the panel
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !MenuPanels.IsTyping)
        {
            if (_selected != null) Select(null);
            else SetOpen(false);
            return;
        }

        foreach (var view in _activeSlots) RefreshSlot(view);
        foreach (var view in _passiveSlots) RefreshSlot(view);
    }

    // Another menu panel opened: close this one
    void OnMenuOpenChanged(object openPanel)
    {
        if (_open && openPanel != null && !ReferenceEquals(openPanel, this)) SetOpen(false);
    }

    void SetOpen(bool open)
    {
        _open = open;
        MenuPanels.SetOpen(this, open);
        if (!_bound) return;
        _root.EnableInClassList(HiddenClass, !open);
        Select(null);
        HideTooltip();
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
                GetPool = () => ActiveSkillManager.Instance != null ? ActiveSkillManager.Instance.AllSkills : null,
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
            var key = new Label(KeyLabel(index)) { pickingMode = PickingMode.Ignore };
            key.AddToClassList("sp-card__key");
            text.Add(view.Name);
            text.Add(key);
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
                GetPool = () => PassiveSkillManager.Instance != null ? PassiveSkillManager.Instance.AllPassives : null,
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
    }

    void RegisterSlotCallbacks(SlotView view)
    {
        view.Element.RegisterCallback<ClickEvent>(_ => Select(_selected == view ? null : view));
        view.Element.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(view.GetEquipped(), view.Element));
        view.Element.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
    }

    static ActiveSkillSlot ActiveSlot(int index)
    {
        var manager = ActiveSkillManager.Instance;
        if (manager == null) return null;
        return index switch { 0 => manager.Slot1, 1 => manager.Slot2, 2 => manager.Slot3, _ => manager.Slot4 };
    }

    static PassiveSkillSlot PassiveSlot(int index)
    {
        var manager = PassiveSkillManager.Instance;
        if (manager == null) return null;
        return index switch { 0 => manager.Slot1, 1 => manager.Slot2, _ => manager.Slot3 };
    }

    string KeyLabel(int index)
    {
        var label = _skillActions[index].GetBindingDisplayString(InputBinding.MaskByGroup("Keyboard&Mouse"));
        return string.IsNullOrEmpty(label) ? (index + 1).ToString() : label;
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
        }
        _selected = view;
        if (view == null) return;

        view.Element.AddToClassList(view.SelectedClass);
        BuildPicker(view);
        view.Picker.RemoveFromClassList(PickerHiddenClass);
    }

    /// <summary>The pool minus skills equipped in the slot's siblings; the slot's own skill is kept and marked.</summary>
    void BuildPicker(SlotView view)
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

        option.RegisterCallback<ClickEvent>(_ =>
        {
            if (!isCurrent) view.Equip(skill);
            Select(null);
            HideTooltip();
        });
        option.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(skill, option));
        option.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
        return option;
    }

    // ---------- Tooltip ----------

    void ShowTooltip(ScriptableObject skill, VisualElement anchor)
    {
        if (skill == null) { HideTooltip(); return; }

        Describe(skill, out var name, out _, out var description);
        _tooltipTitle.text = name;

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(description)) sb.AppendLine(description);
        if (skill is ActiveSkill active)
        {
            if (active.Cooldown > 0f) sb.AppendLine($"Cooldown: {active.Cooldown:0.#}s");
            if (active.ResourceCost > 0f) sb.AppendLine($"Cost: {active.ResourceCost:0.#}");
            if (active.ResourceCostPerSecond > 0f) sb.AppendLine($"Cost: {active.ResourceCostPerSecond:0.#} per second");
        }
        _tooltipBody.text = sb.ToString().TrimEnd();
        _tooltipBody.style.display = _tooltipBody.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

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
            default:
                name = "";
                icon = null;
                description = "";
                break;
        }
    }
}
