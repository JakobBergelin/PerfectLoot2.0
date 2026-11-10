using System;
using System.Collections.Generic;
using System.Linq;
using Equipment;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Character panel toggled with the Inventory action (I): the player's equipped items and their stats.
/// Clicking a slot opens a picker left of the panel with the found items that fit it (and Unequip); hovering one
/// previews the stats with it equipped (SwapPreview) and compares it with the equipped item, clicking equips it.
/// Equipment comes from InventoryManager, stats from the player entity while the panel is open.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class CharacterPanelHUD : MonoBehaviour
{
    const string HiddenClass = "character-panel-root--hidden";
    const string TooltipVisibleClass = "cp-tooltip--visible";
    const string PickerHiddenClass = "cp-picker--hidden";
    const string PreviewHiddenClass = "cp-preview--hidden";
    const string SlotPickingClass = "cp-slot--picking";
    const string ChipSelectedClass = "cp-chip--selected";
    const string RowChangedClass = "cp-row--changed";
    const string DeltaBetterClass = "cp-row__delta--better";
    const string DeltaWorseClass = "cp-row__delta--worse";
    const string NoValue = "–";
    const string PreviewIdleText = "Hover an item to see how your stats change";
    const string TownHint = "Click a slot to swap · Press I to close";
    const string LookOnlyHint = "Change equipment in town · Press I to close";
    const string GamepadTownHint = "A Swap · LT/RT Pages · LB/RB Panels · B Close";
    const string GamepadLookOnlyHint = "Change equipment in town · LT/RT Pages · LB/RB Panels · B Close";
    const float Gap = 8f;
    const float ScreenMargin = 16f;
    const int MaxOptionChanges = 3;
    const int OptionsPerPage = 8;
    const int StatRowsPerPage = 30;     // A section counts its rows and title

    static readonly Color EmptySlotBorder = new Color32(58, 63, 75, 255);

    class SlotView
    {
        public string Name;
        public Func<InventoryManager, InventorySlot> GetSlot;
        public VisualElement Element;
        public VisualElement Icon;
        public Label Label;
        public Item ShownItem;
    }

    class StatRow
    {
        public VisualElement Element;
        public Label Name, Value, Delta;
        public string FixedName;
        public Func<Snapshot, string> GetName;      // Names that depend on the player (resource type)
        public string ChipName;                     // Name in the picker's change summaries, when not the row's name
        public Func<Snapshot, float> Number;        // Compared by the swap preview; higher is always better
        public Func<float, string> Format;          // Number as text, without a sign
        public Func<Snapshot, string> Text;
        public string ShownName, ShownValue, ShownDelta;
    }

    // Everything the stat rows read, taken from the player entity once per frame
    struct Snapshot
    {
        public bool HasPlayer;
        public StatBlock Stats;
        public Health Health;
        public Resource Resource;
        public Armor Armor;
        public int Level;
        public ClassType Class;
        public ResourceType ResourceType;
        public bool HasCombatConfig;
        public CombatConfig Combat;
    }

    enum OptionState { Available, Equipped, Elsewhere, Unequip }

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    bool _open;

    VisualElement _root;
    VisualElement _panel;
    Label _header;
    Label _hint;
    VisualElement _tooltip;
    Label _tooltipTitle, _tooltipSubtitle;
    VisualElement _tooltipBody;
    float _tooltipTop;
    readonly List<SlotView> _slots = new List<SlotView>();
    readonly List<StatRow> _rows = new List<StatRow>();
    bool _slotsDirty = true;

    // Swap picker
    VisualElement _picker, _pickerFilters;
    Label _pickerTitle;
    VisualElement _pickerList, _statList;
    Pager _pickerPager, _statPager;
    VisualElement _previewHeader;
    Label _previewTitle;
    SlotView _pickerSlot;               // The slot the picker is open for; null while it's closed
    Rarity? _rarityFilter;
    float _pickerTop;
    bool _pickerDirty;

    // The hovered picker option: equipment stats as they are and with it equipped
    bool _previewing;
    StatBlock _previewCurrent, _previewChanged;

    InventoryManager _subscribedInventory;
    Snapshot _snapshot;

    InputAction _toggleAction;

    bool CanPreview => _snapshot.HasPlayer && _snapshot.HasCombatConfig;

    void Awake()
    {
        _toggleAction = GameInput.Actions.asset.FindAction("Player/Inventory");
        if (_toggleAction == null)
            Debug.LogWarning("CharacterPanelHUD: no Player/Inventory input action. Reimport InputSystem_Actions.inputactions.", this);
    }

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
        MenuPanels.OpenChanged += OnMenuOpenChanged;
        MenuPanels.AddTab(this, 0, SetOpen);
    }

    void OnDisable()
    {
        MenuPanels.RemoveTab(this);
        MenuPanels.OpenChanged -= OnMenuOpenChanged;
        if (_open) SetOpen(false);
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        Unsubscribe();
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
        _root = root.Q<VisualElement>("characterPanelRoot");
        _panel = root.Q<VisualElement>("panel");
        _header = root.Q<Label>("header");
        _hint = root.Q<Label>("hint");
        var slotGrid = root.Q<VisualElement>("slotGrid");
        _statList = root.Q<VisualElement>("statList");
        _tooltip = root.Q<VisualElement>("tooltip");
        _tooltipTitle = root.Q<Label>("tooltipTitle");
        _tooltipSubtitle = root.Q<Label>("tooltipSubtitle");
        _tooltipBody = root.Q<VisualElement>("tooltipBody");
        _picker = root.Q<VisualElement>("picker");
        _pickerTitle = root.Q<Label>("pickerTitle");
        _pickerFilters = root.Q<VisualElement>("pickerFilters");
        _pickerList = root.Q<VisualElement>("pickerList");
        _previewHeader = root.Q<VisualElement>("previewHeader");
        _previewTitle = root.Q<Label>("previewTitle");
        if (_root == null || _panel == null || _header == null || _hint == null || slotGrid == null || _statList == null || _tooltip == null ||
            _picker == null || _pickerTitle == null || _pickerFilters == null || _pickerList == null ||
            _previewHeader == null || _previewTitle == null)
        {
            Debug.LogError("CharacterPanelHUD: CharacterPanel.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }

        // Moved up when they'd reach past the bottom of the screen, once their height is known
        _picker.RegisterCallback<GeometryChangedEvent>(_ => ClampTop(_picker, _pickerTop));
        _tooltip.RegisterCallback<GeometryChangedEvent>(_ => ClampTop(_tooltip, _tooltipTop));

        UiNav.Register(_root);
        _pickerPager = Pager.After(_pickerList);
        _statPager = Pager.After(_statList);
        BuildSlots(slotGrid);
        BuildStats(_statList);
        _pickerSlot = null;
        ClosePicker();
        _slotsDirty = true;
        return true;
    }

    void Update()
    {
        if (MenuPanels.TogglePressed(_toggleAction))
            SetOpen(!_open);

        if (!_bound || !_open) return;

        // Esc or B closes the picker first, then the panel
        if (MenuPanels.BackPressed && !MenuPanels.IsTyping)
        {
            if (_pickerSlot != null) ClosePicker();
            else SetOpen(false);
            return;
        }
        // LT/RT: the picker's pages while it's open, else the stats'
        if (MenuPanels.PagePressed != 0) (_pickerSlot != null ? _pickerPager : _statPager).Turn(MenuPanels.PagePressed);
        // A click anywhere but the picker or a slot closes the picker (slots open their own or close it themselves)
        if (_pickerSlot != null && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !PointerOnPickerOrSlot())
            ClosePicker();
        // Equipment is only changed in town; elsewhere the panel is look-only
        bool town = GameArea.IsTown;
        bool gamepad = GameInput.UsingGamepad;
        _hint.text = town ? (gamepad ? GamepadTownHint : TownHint) : (gamepad ? GamepadLookOnlyHint : LookOnlyHint);
        if (!town && _pickerSlot != null) ClosePicker();

        Subscribe();
        if (_slotsDirty) RefreshSlots();
        RefreshStats();
        if (_pickerSlot != null && _pickerDirty) BuildPicker();
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
        if (!open)
        {
            ClosePicker();
            HideTooltip();
        }
        UiNav.SetScope(this, _panel, open);
        _slotsDirty = true;
    }

    // InventoryManager may not exist yet when this is enabled, so subscribe once it does
    void Subscribe()
    {
        var inventory = InventoryManager.Instance;
        if (inventory == _subscribedInventory) return;
        Unsubscribe();
        if (inventory == null) return;
        inventory.StatsChanged += OnInventoryChanged;
        _subscribedInventory = inventory;
        _slotsDirty = true;
        _pickerDirty = true;
    }

    void Unsubscribe()
    {
        if (_subscribedInventory != null) _subscribedInventory.StatsChanged -= OnInventoryChanged;
        _subscribedInventory = null;
    }

    void OnInventoryChanged(StatSummary summary)
    {
        _slotsDirty = true;
        _pickerDirty = true;
    }

    // ---------- Equipment ----------

    void BuildSlots(VisualElement grid)
    {
        grid.Clear();
        _slots.Clear();
        // 3 columns, read left to right: head to feet, then weapon, offhand and rings
        AddSlot(grid, "Head", m => m.HeadSlot);
        AddSlot(grid, "Shoulders", m => m.ShouldersSlot);
        AddSlot(grid, "Amulet", m => m.AmuletSlot);
        AddSlot(grid, "Chest", m => m.ChestSlot);
        AddSlot(grid, "Hands", m => m.HandsSlot);
        AddSlot(grid, "Belt", m => m.BeltSlot);
        AddSlot(grid, "Legs", m => m.LegsSlot);
        AddSlot(grid, "Boots", m => m.BootsSlot);
        AddSlot(grid, "Weapon", m => m.WeaponSlot);
        AddSlot(grid, "Offhand", m => m.OffhandSlot);
        AddSlot(grid, "Ring", m => m.Ring1Slot);
        AddSlot(grid, "Ring", m => m.Ring2Slot);
    }

    void AddSlot(VisualElement grid, string name, Func<InventoryManager, InventorySlot> getSlot)
    {
        var view = new SlotView { Name = name, GetSlot = getSlot };
        view.Element = new VisualElement();
        view.Element.AddToClassList("cp-slot");
        view.Icon = new VisualElement { pickingMode = PickingMode.Ignore };
        view.Icon.AddToClassList("cp-slot__icon");
        view.Label = new Label { pickingMode = PickingMode.Ignore };
        view.Label.AddToClassList("cp-slot__label");
        view.Element.Add(view.Icon);
        view.Element.Add(view.Label);

        UiNav.Hover(view.Element, () => ShowSlotTooltip(view), HideTooltip);
        Action click = () =>
        {
            if (_pickerSlot == view) ClosePicker();
            else if (GameArea.IsTown) OpenPicker(view);
        };
        view.Element.RegisterCallback<ClickEvent>(_ => click());
        UiNav.Clickable(view.Element, click);

        grid.Add(view.Element);
        _slots.Add(view);
    }

    void RefreshSlots()
    {
        _slotsDirty = false;
        var inventory = InventoryManager.Instance;
        foreach (var view in _slots)
        {
            var slot = inventory != null ? view.GetSlot(inventory) : null;
            var item = slot != null && !slot.IsEmpty ? slot.EquippedItem : null;
            view.ShownItem = item;

            var icon = item != null ? item.BaseItem.Icon : null;
            view.Icon.style.backgroundImage = icon != null ? new StyleBackground(icon) : new StyleBackground(StyleKeyword.None);
            // Empty: slot name. Equipped without an icon: item name.
            view.Label.text = item == null ? view.Name : icon == null ? item.BaseItem.Name : "";

            var border = item != null ? RarityColors.Get(item.Rarity) ?? EmptySlotBorder : EmptySlotBorder;
            view.Element.style.borderTopColor = border;
            view.Element.style.borderBottomColor = border;
            view.Element.style.borderLeftColor = border;
            view.Element.style.borderRightColor = border;
        }
    }

    // ---------- Tooltip ----------

    void ShowSlotTooltip(SlotView view)
    {
        if (view.ShownItem == null) { HideTooltip(); return; }

        // Right of the slot, in the root's coordinates
        var position = view.Element.ChangeCoordinatesTo(_root, new Vector2(view.Element.layout.width + Gap, 0f));
        ShowTooltip(view.ShownItem, null, position.x, position.y, leftOf: false);
    }

    // At x, or ending at x when leftOf; top at y unless that reaches past the bottom of the screen
    void ShowTooltip(Item item, Item compareTo, float x, float y, bool leftOf)
    {
        _tooltipTitle.text = item.BaseItem.Name;
        _tooltipTitle.style.color = ItemText.TitleColor(item);
        _tooltipSubtitle.text = ItemText.Subtitle(item);
        ItemStatRows.Fill(_tooltipBody, item, compareTo);

        if (leftOf)
        {
            _tooltip.style.left = StyleKeyword.Auto;
            _tooltip.style.right = _root.layout.width - x;
        }
        else
        {
            _tooltip.style.right = StyleKeyword.Auto;
            _tooltip.style.left = x;
        }
        _tooltipTop = y;
        _tooltip.style.top = y;
        _tooltip.AddToClassList(TooltipVisibleClass);
    }

    void HideTooltip() => _tooltip?.RemoveFromClassList(TooltipVisibleClass);

    void ClampTop(VisualElement element, float desiredTop)
    {
        float maxTop = _root.layout.height - ScreenMargin - element.layout.height;
        float top = Mathf.Max(ScreenMargin, Mathf.Min(desiredTop, maxTop));
        if (Mathf.Abs(top - element.resolvedStyle.top) > 0.5f) element.style.top = top;
    }

    // ---------- Swap picker ----------

    void OpenPicker(SlotView view)
    {
        HideTooltip();
        EndPreview();
        _pickerSlot = view;
        _rarityFilter = null;
        foreach (var slot in _slots) slot.Element.EnableInClassList(SlotPickingClass, slot == view);
        _pickerPager.Reset();
        BuildPicker();
        // A controller moves through the picker until it closes, starting on its first item
        UiNav.SetScope(_picker, _picker, true);
        UiNav.FocusFirstOf(_pickerList.Children());

        // Left of the panel, level with the slot
        var slotPosition = view.Element.ChangeCoordinatesTo(_root, Vector2.zero);
        var panelPosition = _panel.ChangeCoordinatesTo(_root, Vector2.zero);
        _picker.style.right = _root.layout.width - panelPosition.x + Gap;
        _picker.style.maxHeight = _root.layout.height - 2f * ScreenMargin;
        _pickerTop = slotPosition.y;
        _picker.style.top = _pickerTop;

        _picker.RemoveFromClassList(PickerHiddenClass);
        _previewHeader.RemoveFromClassList(PreviewHiddenClass);
    }

    void ClosePicker()
    {
        _pickerSlot = null;
        if (_picker != null) UiNav.SetScope(_picker, _picker, false);
        EndPreview();
        HideTooltip();
        _picker?.AddToClassList(PickerHiddenClass);
        _previewHeader?.AddToClassList(PreviewHiddenClass);
        foreach (var slot in _slots) slot.Element.RemoveFromClassList(SlotPickingClass);
    }

    bool PointerOnPickerOrSlot()
    {
        var panel = _root.panel;
        if (panel == null) return false;
        var mouse = Mouse.current.position.ReadValue();
        // Screen positions count from the bottom, panel positions from the top
        var picked = panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, new Vector2(mouse.x, Screen.height - mouse.y)));
        for (var element = picked; element != null; element = element.parent)
            if (element == _picker || _slots.Exists(view => view.Element == element)) return true;
        return false;
    }

    // Filter chips, Unequip, then the slot's found items in database order (slot type, name, rarity)
    void BuildPicker()
    {
        _pickerDirty = false;
        EndPreview();
        HideTooltip();
        _pickerFilters.Clear();
        _pickerList.Clear();
        _pickerTitle.text = $"Swap {_pickerSlot.Name}";

        var inventory = InventoryManager.Instance;
        var slot = inventory != null ? _pickerSlot.GetSlot(inventory) : null;
        var database = AccountManager.Instance != null ? AccountManager.Instance.Items : null;
        if (slot == null || database == null)
        {
            AddPickerMessage("No inventory to equip from");
            return;
        }

        TakeSnapshot();
        var found = database.KeysFor(slot.SlotType).Where(key => database.Get(key) != null).ToList();
        AddFilterChip("All", null);
        foreach (var rarity in found.Select(key => key.Rarity).Distinct().OrderBy(rarity => rarity))
            AddFilterChip(rarity.ToString(), rarity);

        if (!slot.IsEmpty) _pickerList.Add(CreateOption(slot, null, OptionState.Unequip));
        int count = 0;
        foreach (var key in found)
        {
            if (_rarityFilter != null && key.Rarity != _rarityFilter) continue;
            var holder = inventory.SlotWith(key);
            var state = holder == null ? OptionState.Available : holder == slot ? OptionState.Equipped : OptionState.Elsewhere;
            _pickerList.Add(CreateOption(slot, database.Get(key), state));
            count++;
        }
        if (count == 0)
            AddPickerMessage(found.Count == 0 ? "No items found for this slot yet" : $"No {_rarityFilter} items found for this slot");
        _pickerPager.Show(_pickerList.Children(), OptionsPerPage);
    }

    void AddFilterChip(string text, Rarity? rarity)
    {
        var chip = new Label(text);
        chip.AddToClassList("cp-chip");
        chip.EnableInClassList(ChipSelectedClass, rarity == _rarityFilter);
        Action pick = () =>
        {
            _rarityFilter = rarity;
            _pickerPager.Reset();
            BuildPicker();
            // The chips are new: keep a controller on the picked one
            foreach (var shown in _pickerFilters.Children())
                if (shown.ClassListContains(ChipSelectedClass)) UiNav.Focus(shown);
        };
        chip.RegisterCallback<ClickEvent>(_ => pick());
        UiNav.Clickable(chip, pick);
        _pickerFilters.Add(chip);
    }

    void AddPickerMessage(string text)
    {
        var message = new Label(text);
        message.AddToClassList("cp-picker__empty");
        _pickerList.Add(message);
    }

    // Icon, name with rarity, then what equipping it changes (or why it can't be picked)
    VisualElement CreateOption(InventorySlot slot, Item item, OptionState state)
    {
        var option = new VisualElement();
        option.AddToClassList("cp-option");
        if (state == OptionState.Equipped) option.AddToClassList("cp-option--current");
        if (state == OptionState.Elsewhere) option.AddToClassList("cp-option--disabled");

        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("cp-option__icon");
        var sprite = item != null ? item.BaseItem.Icon : null;
        if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
        option.Add(icon);

        var text = new VisualElement { pickingMode = PickingMode.Ignore };
        text.AddToClassList("cp-option__text");
        var name = new Label(item != null ? $"{item.BaseItem.Name}  <color={ItemText.RangeColor}>{item.Rarity}</color>" : "Unequip")
        {
            pickingMode = PickingMode.Ignore,
        };
        name.AddToClassList("cp-option__name");
        if (item != null) name.style.color = ItemText.TitleColor(item);
        text.Add(name);

        var detail = new Label(state switch
        {
            OptionState.Equipped => "Equipped",
            OptionState.Elsewhere => "Equipped in the other " + slot.SlotType + " slot",
            _ => ChangeSummary(slot, item),
        }) { pickingMode = PickingMode.Ignore };
        detail.AddToClassList("cp-option__detail");
        text.Add(detail);
        option.Add(text);

        UiNav.Hover(option, () => HoverOption(slot, item, state, option), () =>
        {
            EndPreview();
            HideTooltip();
        });
        if (state != OptionState.Elsewhere)
        {
            option.RegisterCallback<ClickEvent>(_ => PickOption(slot, item, state));
            UiNav.Clickable(option, () => PickOption(slot, item, state));
        }
        return option;
    }

    void HoverOption(InventorySlot slot, Item item, OptionState state, VisualElement option)
    {
        // Compared with the equipped item, left of the picker
        if (item != null)
        {
            float x = _picker.ChangeCoordinatesTo(_root, Vector2.zero).x - Gap;
            float y = option.ChangeCoordinatesTo(_root, Vector2.zero).y;
            ShowTooltip(item, state == OptionState.Equipped ? null : slot.EquippedItem, x, y, leftOf: true);
        }
        else HideTooltip();

        if (state != OptionState.Available && state != OptionState.Unequip)
        {
            EndPreview();
            return;
        }

        var inventory = InventoryManager.Instance;
        if (inventory == null) return;
        var before = SwapPreview.ItemsWith(inventory, slot, slot.EquippedItem).ToList();
        var after = SwapPreview.ItemsWith(inventory, slot, item).ToList();
        _previewCurrent = SwapPreview.EquipmentStats(before);
        _previewChanged = SwapPreview.EquipmentStats(after);
        _previewing = true;

        var powers = PowerChanges(before, after);
        _previewTitle.text = (item != null ? $"With {item.Rarity} {item.BaseItem.Name}" : $"With no {_pickerSlot.Name}") +
                             (powers.Count > 0 ? "   " + string.Join("  ", powers) : "");
    }

    void EndPreview()
    {
        _previewing = false;
        if (_previewTitle != null) _previewTitle.text = PreviewIdleText;
    }

    void PickOption(InventorySlot slot, Item item, OptionState state)
    {
        var inventory = InventoryManager.Instance;
        if (inventory != null)
        {
            if (state == OptionState.Unequip) inventory.Unequip(slot);
            else if (state == OptionState.Available) inventory.Equip(slot, ItemKey.Of(item));     // Warns when it can't
        }
        ClosePicker();
    }

    // The biggest stat changes (relative to the larger of the two values), then legendary powers gained and lost
    string ChangeSummary(InventorySlot slot, Item item)
    {
        var inventory = InventoryManager.Instance;
        if (inventory == null) return "";
        var before = SwapPreview.ItemsWith(inventory, slot, slot.EquippedItem).ToList();
        var after = SwapPreview.ItemsWith(inventory, slot, item).ToList();

        var parts = new List<string>();
        if (CanPreview)
        {
            var preview = Preview(_snapshot, SwapPreview.EquipmentStats(before), SwapPreview.EquipmentStats(after));
            var changes = new List<(float Score, string Text)>();
            foreach (var row in _rows)
            {
                float a = row.Number(_snapshot), b = row.Number(preview);
                if (row.Format(a) == row.Format(b)) continue;
                float score = Mathf.Abs(b - a) / Mathf.Max(Mathf.Abs(a), Mathf.Abs(b));
                changes.Add((score, Colored(b > a, $"{Delta(row, a, b)} {row.ChipName ?? RowName(row)}")));
            }
            // Stable: equal changes keep the stat column's order
            parts.AddRange(changes.OrderByDescending(change => change.Score).Take(MaxOptionChanges).Select(change => change.Text));
        }
        parts.AddRange(PowerChanges(before, after));
        return parts.Count > 0 ? string.Join("   ", parts) : "No stat changes";
    }

    static List<string> PowerChanges(List<Item> before, List<Item> after)
    {
        var had = SwapPreview.Powers(before);
        var has = SwapPreview.Powers(after);
        var changes = new List<string>();
        foreach (var power in has.Where(power => !had.Contains(power))) changes.Add(Colored(true, "+" + power.DisplayName));
        foreach (var power in had.Where(power => !has.Contains(power))) changes.Add(Colored(false, "-" + power.DisplayName));
        return changes;
    }

    // The snapshot with the equipment part of the stats changed; same math as FinalizeStatsSystem for the rest
    static Snapshot Preview(in Snapshot snapshot, in StatBlock current, in StatBlock changed)
    {
        var preview = snapshot;
        preview.Stats = SwapPreview.Swap(snapshot.Stats, current, changed, snapshot.Combat.UnarmedWeaponReach);
        preview.Armor.Value = preview.Stats.Armor;
        preview.Health.MaxValue = preview.Stats.Health * (1f + snapshot.Health.MaxHealthMultiplier);
        preview.Resource.MaxValue = preview.Stats.Resource;
        preview.Resource.Regeneration = preview.Stats.ResourceRegeneration * (1f + snapshot.Resource.RegenerationMultiplier);
        return preview;
    }

    static string Delta(StatRow row, float before, float after) =>
        (after > before ? "+" : "-") + row.Format(Mathf.Abs(after - before));

    static string Colored(bool better, string text) =>
        $"<color={(better ? ItemText.BetterColor : ItemText.WorseColor)}>{text}</color>";

    // ---------- Stats ----------

    void BuildStats(VisualElement list)
    {
        list.Clear();
        _rows.Clear();

        var section = AddSection(list, "Vitals", first: true);
        AddRow(section, Affix.Health, "Health", s => s.Health.MaxValue, Whole, s => $"{s.Health.CurrentValue:0} / {s.Health.MaxValue:0}");
        AddRow(section, Affix.HealthRegeneration, "Health Regen", s => s.Stats.HealthRegeneration, Flat, s => $"{Flat(s.Stats.HealthRegeneration)}/s");
        AddRow(section, Affix.Resource, "", s => s.Resource.MaxValue, Whole, s => $"{s.Resource.CurrentValue:0} / {s.Resource.MaxValue:0}",
               getName: s => ResourceName(s.ResourceType));
        AddRow(section, Affix.ResourceRegeneration, "", s => s.Resource.Regeneration, Flat, s => $"{Flat(s.Resource.Regeneration)}/s",
               getName: s => $"{ResourceName(s.ResourceType)} Regen");

        section = AddSection(list, "Attributes");
        AddRow(section, Affix.Strength, "Strength", s => s.Stats.Strength, Flat);
        AddRow(section, Affix.Agility, "Agility", s => s.Stats.Agility, Flat);
        AddRow(section, Affix.Intelligence, "Intelligence", s => s.Stats.Intelligence, Flat);

        section = AddSection(list, "Offense");
        AddRow(section, Affix.WeaponDamage, "Weapon Damage", s => s.Stats.WeaponDamage, Flat, s => s.Stats.WeaponDamageRange > 0f
            ? $"{Flat(s.Stats.WeaponDamage)} ± {Flat(s.Stats.WeaponDamageRange)}"
            : Flat(s.Stats.WeaponDamage));
        AddRow(section, Affix.Damage, "Damage", s => s.Stats.Damage, Pct, plus: true);
        AddRow(section, Affix.ElementalDamage, "Elemental Damage", s => s.Stats.ElementalDamage, Pct, plus: true);
        AddRow(section, Affix.FireDamage, "Fire Damage", s => s.Stats.FireDamage, Pct, plus: true);
        AddRow(section, Affix.ColdDamage, "Cold Damage", s => s.Stats.ColdDamage, Pct, plus: true);
        AddRow(section, Affix.LightningDamage, "Lightning Damage", s => s.Stats.LightningDamage, Pct, plus: true);
        AddRow(section, Affix.PoisonDamage, "Poison Damage", s => s.Stats.PoisonDamage, Pct, plus: true);
        AddRow(section, Affix.AttackSpeed, "Attack Speed", s => s.Stats.AttackSpeed, value => value.ToString("0.00"));
        AddRow(section, Affix.WeaponReach, "Weapon Reach", s => s.Stats.WeaponReach, Flat);
        AddRow(section, Affix.CriticalChance, "Critical Chance", s => s.Stats.CriticalChance, Pct);
        AddRow(section, Affix.CriticalDamage, "Critical Damage", s => s.Stats.CriticalDamage, Pct, plus: true);
        AddRow(section, Affix.ArmorPierce, "Armor Pierce", s => s.Stats.ArmorPierce, Flat);
        AddRow(section, Affix.AreaOfEffect, "Area of Effect", s => s.Stats.AreaOfEffect, Pct, plus: true);
        AddRow(section, Affix.CooldownReduction, "Cooldown Reduction", s => s.Stats.CooldownReduction, Pct);
        AddRow(section, Affix.ResourceCostReduction, "Resource Cost Reduction", s => s.Stats.ResourceCostReduction, Pct);
        AddRow(section, Affix.LifeOnHit, "Life on Hit", s => s.Stats.LifeOnHit, Flat);
        AddRow(section, Affix.ResourceOnHit, "Resource on Hit", s => s.Stats.ResourceOnHit, Flat);
        AddRow(section, Affix.ProjectilePierces, "Pierce", s => s.Stats.Pierce, Whole);
        AddRow(section, Affix.ProjectileBounces, "Bounces", s => s.Stats.Bounces, Whole);
        AddRow(section, Affix.ProjectileSpeed, "Projectile Speed", s => s.Stats.ProjectileSpeed, Pct, plus: true);

        section = AddSection(list, "Defense");
        AddRow(section, Affix.Armor, "Armor", s => s.Armor.Value, Flat, s => WithMitigation(s.Armor.Value, ArmorMitigation(s)));
        AddRow(section, Affix.BlockChance, "Block Chance", s => s.Stats.BlockChance, Pct);
        AddRow(section, Affix.DodgeChance, "Dodge Chance", s => s.Stats.DodgeChance, Pct);

        section = AddSection(list, "Resistances");
        AddRow(section, Affix.FireResistance, "Fire", s => s.Stats.FireResistance + s.Stats.AllResistance, Flat,
               s => Resistance(s, s.Stats.FireResistance), chipName: "Fire Resistance");
        AddRow(section, Affix.ColdResistance, "Cold", s => s.Stats.ColdResistance + s.Stats.AllResistance, Flat,
               s => Resistance(s, s.Stats.ColdResistance), chipName: "Cold Resistance");
        AddRow(section, Affix.LightningResistance, "Lightning", s => s.Stats.LightningResistance + s.Stats.AllResistance, Flat,
               s => Resistance(s, s.Stats.LightningResistance), chipName: "Lightning Resistance");
        AddRow(section, Affix.PoisonResistance, "Poison", s => s.Stats.PoisonResistance + s.Stats.AllResistance, Flat,
               s => Resistance(s, s.Stats.PoisonResistance), chipName: "Poison Resistance");
        AddRow(section, Affix.AllResistance, "All Resistance", s => s.Stats.AllResistance, Flat, plus: true);

        section = AddSection(list, "Other");
        AddRow(section, Affix.MovementSpeed, "Movement Speed", s => s.Stats.MovementSpeed, Pct, plus: true);
        AddRow(section, Affix.BuffDuration, "Buff Duration", s => s.Stats.BuffDuration, Pct, plus: true);

        // Pages of whole sections
        _statPager.Reset();
        _statPager.Show(list.Children(), StatRowsPerPage, shown => shown.childCount);
    }

    static VisualElement AddSection(VisualElement list, string title, bool first = false)
    {
        var section = new VisualElement();
        section.AddToClassList("cp-section");
        if (first) section.AddToClassList("cp-section--first");
        var label = new Label(title);
        label.AddToClassList("cp-section__title");
        section.Add(label);
        list.Add(section);
        return section;
    }

    // The affix's icon, the name, the value, then the change while a picker option is previewed. Without text, the
    // value shows as format(number), with a + when plus and it isn't negative.
    void AddRow(VisualElement section, Affix affix, string name, Func<Snapshot, float> number, Func<float, string> format,
                Func<Snapshot, string> text = null, bool plus = false, Func<Snapshot, string> getName = null, string chipName = null)
    {
        var row = new StatRow
        {
            FixedName = name,
            GetName = getName,
            ChipName = chipName,
            Number = number,
            Format = format,
            Text = text ?? (s =>
            {
                float value = number(s);
                return (plus && value >= 0f ? "+" : "") + format(value);
            }),
        };

        row.Element = new VisualElement();
        row.Element.AddToClassList("cp-row");
        var icon = new VisualElement();
        icon.AddToClassList("cp-row__icon");
        var sprite = AffixIcons.Get(affix);
        if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
        row.Element.Add(icon);
        row.Name = new Label(name);
        row.Name.AddToClassList("cp-row__name");
        row.Value = new Label { enableRichText = true };
        row.Value.AddToClassList("cp-row__value");
        row.Delta = new Label();
        row.Delta.AddToClassList("cp-row__delta");
        row.Element.Add(row.Name);
        row.Element.Add(row.Value);
        row.Element.Add(row.Delta);
        section.Add(row.Element);
        _rows.Add(row);
    }

    string RowName(StatRow row) => row.GetName != null ? row.GetName(_snapshot) : row.FixedName;

    void RefreshStats()
    {
        TakeSnapshot();
        _header.text = _snapshot.HasPlayer ? $"{_snapshot.Class} · Level {_snapshot.Level}" : "No character";

        bool previewing = _previewing && CanPreview;
        var preview = previewing ? Preview(_snapshot, _previewCurrent, _previewChanged) : default;
        foreach (var row in _rows)
        {
            string value = NoValue, delta = "";
            bool changed = false, better = false;
            if (_snapshot.HasPlayer)
            {
                value = row.Text(_snapshot);
                if (previewing)
                {
                    float before = row.Number(_snapshot), after = row.Number(preview);
                    string beforeText = row.Format(before), afterText = row.Format(after);
                    if (beforeText != afterText)
                    {
                        changed = true;
                        better = after > before;
                        value = $"{beforeText} → {Colored(better, afterText)}";
                        delta = Delta(row, before, after);
                    }
                    else if (row.Text(preview) != value)
                    {
                        // Only the rest of the text changed, e.g. the weapon damage range
                        changed = true;
                        value = $"{value} → {row.Text(preview)}";
                    }
                }
            }

            SetText(row.Name, ref row.ShownName, RowName(row));
            SetText(row.Value, ref row.ShownValue, value);
            SetText(row.Delta, ref row.ShownDelta, delta);
            row.Element.EnableInClassList(RowChangedClass, changed);
            row.Delta.EnableInClassList(DeltaBetterClass, changed && better);
            row.Delta.EnableInClassList(DeltaWorseClass, changed && !better);
        }
    }

    static void SetText(Label label, ref string shown, string text)
    {
        if (text == shown) return;
        label.text = text;
        shown = text;
    }

    void TakeSnapshot()
    {
        _snapshot.HasPlayer = false;
        var world = World.DefaultGameObjectInjectionWorld;
        var characterManager = CharacterManager.Instance;
        if (world == null || !world.IsCreated || characterManager == null) return;

        var entityManager = world.EntityManager;
        var player = characterManager.Player;
        if (player == Entity.Null || !entityManager.Exists(player) || !entityManager.HasComponent<CalculatedStats>(player)) return;

        _snapshot.HasPlayer = true;
        _snapshot.Stats = entityManager.GetComponentData<CalculatedStats>(player).Value;
        _snapshot.Health = Get<Health>(entityManager, player);
        _snapshot.Resource = Get<Resource>(entityManager, player);
        _snapshot.Armor = Get<Armor>(entityManager, player);
        _snapshot.Level = Get<Level>(entityManager, player).Value;
        _snapshot.Class = Get<UnitClass>(entityManager, player).Type;
        _snapshot.ResourceType = Get<UnitResource>(entityManager, player).Type;

        using var combatQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<CombatConfig>());
        _snapshot.HasCombatConfig = combatQuery.CalculateEntityCount() == 1;
        if (_snapshot.HasCombatConfig) _snapshot.Combat = combatQuery.GetSingleton<CombatConfig>();
    }

    static T Get<T>(EntityManager entityManager, Entity entity) where T : unmanaged, IComponentData =>
        entityManager.HasComponent<T>(entity) ? entityManager.GetComponentData<T>(entity) : default;

    // Same math as FinalizeStatsSystem / CalculateDamageSystem
    static float? ArmorMitigation(in Snapshot snapshot)
    {
        if (!snapshot.HasCombatConfig) return null;
        return CombatConfig.PhysicalResistance(snapshot.Armor.Value, snapshot.Armor.PhysicalResistanceMultiplier, 0f, snapshot.Combat.ArmorConstant);
    }

    static string Resistance(in Snapshot snapshot, float resistance)
    {
        float total = resistance + snapshot.Stats.AllResistance;
        float? mitigation = snapshot.HasCombatConfig
            ? CombatConfig.ElementalMitigation(total, snapshot.Combat.ElementalResistanceConstant)
            : null;
        return WithMitigation(total, mitigation);
    }

    static string WithMitigation(float value, float? mitigation) =>
        mitigation.HasValue ? $"{Flat(value)} ({Pct(mitigation.Value)})" : Flat(value);

    static string ResourceName(ResourceType type) => type == ResourceType.None ? "Resource" : type.ToString();

    static string Flat(float value) => value.ToString("0.#");

    static string Whole(float value) => value.ToString("0");

    static string Pct(float fraction) => (fraction * 100f).ToString("0.#") + "%";
}
