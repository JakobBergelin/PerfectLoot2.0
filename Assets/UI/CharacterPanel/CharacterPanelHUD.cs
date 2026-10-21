using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Equipment;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Character panel toggled with the Inventory action (I): the player's equipped items and their stats.
/// Read-only: equipment comes from InventoryManager, stats from the player entity while the panel is open.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class CharacterPanelHUD : MonoBehaviour
{
    const string HiddenClass = "character-panel-root--hidden";
    const string TooltipVisibleClass = "cp-tooltip--visible";
    const string NoValue = "–";
    const string RangeColor = "#8A8F99";

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
        public Label Value;
        public Func<string> Get;
        public string Shown;
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

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    bool _open;

    VisualElement _root;
    Label _header;
    VisualElement _tooltip;
    Label _tooltipTitle, _tooltipSubtitle, _tooltipBody;
    readonly List<SlotView> _slots = new List<SlotView>();
    readonly List<StatRow> _rows = new List<StatRow>();
    bool _slotsDirty = true;

    InventoryManager _subscribedInventory;
    Snapshot _snapshot;

    InputSystem_Actions _actions;
    InputAction _toggleAction;

    void Awake()
    {
        _actions = new InputSystem_Actions();
        _toggleAction = _actions.asset.FindAction("Player/Inventory");
        if (_toggleAction == null)
            Debug.LogWarning("CharacterPanelHUD: no Player/Inventory input action. Reimport InputSystem_Actions.inputactions.", this);
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
        _header = root.Q<Label>("header");
        var slotGrid = root.Q<VisualElement>("slotGrid");
        var statList = root.Q<ScrollView>("statList");
        _tooltip = root.Q<VisualElement>("tooltip");
        _tooltipTitle = root.Q<Label>("tooltipTitle");
        _tooltipSubtitle = root.Q<Label>("tooltipSubtitle");
        _tooltipBody = root.Q<Label>("tooltipBody");
        if (_root == null || _header == null || slotGrid == null || statList == null || _tooltip == null)
        {
            Debug.LogError("CharacterPanelHUD: CharacterPanel.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }

        BuildSlots(slotGrid);
        BuildStats(statList);
        _slotsDirty = true;
        return true;
    }

    void Update()
    {
        if (_toggleAction != null && _toggleAction.WasPressedThisFrame())
            SetOpen(!_open);

        if (!_bound || !_open) return;

        Subscribe();
        if (_slotsDirty) RefreshSlots();
        RefreshStats();
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
        if (!open) HideTooltip();
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
    }

    void Unsubscribe()
    {
        if (_subscribedInventory != null) _subscribedInventory.StatsChanged -= OnInventoryChanged;
        _subscribedInventory = null;
    }

    void OnInventoryChanged(StatSummary summary) => _slotsDirty = true;

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

        view.Element.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(view));
        view.Element.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());

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

    void ShowTooltip(SlotView view)
    {
        var item = view.ShownItem;
        if (item == null) { HideTooltip(); return; }

        _tooltipTitle.text = item.BaseItem.Name;
        _tooltipTitle.style.color = RarityColors.Get(item.Rarity) ?? Color.white;
        _tooltipSubtitle.text = $"{item.Rarity} {item.ItemType}";

        var sb = new StringBuilder();
        // Fixed (not rolled) stats first, muted, e.g. a weapon's cooldown and reach
        foreach (var line in item.BaseItem.FixedStatLines)
            sb.AppendLine($"<color={RangeColor}>{line}</color>");
        var settings = LootGenerationManager.Instance != null ? LootGenerationManager.Instance.GenerationSettings : null;
        AppendAffixes(sb, item, item.BaseAffixes, isBaseAffix: true, settings);
        AppendAffixes(sb, item, item.RollableAffixes, isBaseAffix: false, settings);
        _tooltipBody.text = sb.Length > 0 ? sb.ToString().TrimEnd() : "No stats";

        // Right of the slot, in the root's coordinates
        var position = view.Element.ChangeCoordinatesTo(_root, new Vector2(view.Element.layout.width + 8f, 0f));
        _tooltip.style.left = position.x;
        _tooltip.style.top = position.y;
        _tooltip.AddToClassList(TooltipVisibleClass);
    }

    void HideTooltip() => _tooltip?.RemoveFromClassList(TooltipVisibleClass);

    // "+177 Strength  [150 – 180]": the range this affix could roll on this item at its rarity, muted
    static void AppendAffixes(StringBuilder sb, Item item, List<Rolled> affixes, bool isBaseAffix, GenerationSettings settings)
    {
        if (affixes == null) return;
        foreach (var rolled in affixes)
        {
            sb.Append($"+{FormatAffix(rolled.Affix, rolled.Value)} {SplitWords(rolled.Affix.ToString())}");
            if (item.BaseItem.TryGetRollRange(rolled.Affix, isBaseAffix, item.Rarity, settings, out float min, out float max))
                sb.Append($"  <color={RangeColor}>[{FormatAffix(rolled.Affix, min)} – {FormatAffix(rolled.Affix, max)}]</color>");
            sb.AppendLine();
        }
    }

    // Affixes rolled as fractions (0.1 = 10%); the rest are flat values
    static bool IsPercentAffix(Affix affix) => affix switch
    {
        Affix.MovementSpeed or Affix.BlockChance or Affix.CooldownReduction or Affix.ResourceCostReduction or
        Affix.DodgeChance or Affix.AttackSpeed or Affix.Lifesteal or Affix.Damage or Affix.AreaOfEffect or
        Affix.CriticalChance or Affix.CriticalDamage or Affix.ResourceSteal => true,
        _ => false
    };

    static string FormatAffix(Affix affix, float value) => IsPercentAffix(affix) ? Pct(value) : Flat(value);

    static string SplitWords(string name) => Regex.Replace(name, "(\\B[A-Z])", " $1");

    // ---------- Stats ----------

    void BuildStats(ScrollView list)
    {
        list.Clear();
        _rows.Clear();

        var section = AddSection(list, "Vitals", first: true);
        AddRow(section, "Health", () => $"{_snapshot.Health.CurrentValue:0} / {_snapshot.Health.MaxValue:0}");
        AddRow(section, "Health Regen", () => $"{Flat(_snapshot.Stats.HealthRegeneration)}/s");
        AddRow(section, () => ResourceName(_snapshot.ResourceType), () => $"{_snapshot.Resource.CurrentValue:0} / {_snapshot.Resource.MaxValue:0}");
        AddRow(section, () => $"{ResourceName(_snapshot.ResourceType)} Regen", () => $"{Flat(_snapshot.Resource.Regeneration)}/s");

        section = AddSection(list, "Attributes");
        AddRow(section, "Strength", () => Flat(_snapshot.Stats.Strength));
        AddRow(section, "Agility", () => Flat(_snapshot.Stats.Agility));
        AddRow(section, "Intelligence", () => Flat(_snapshot.Stats.Intelligence));

        section = AddSection(list, "Offense");
        AddRow(section, "Weapon Damage", () => _snapshot.Stats.WeaponDamageRange > 0f
            ? $"{Flat(_snapshot.Stats.WeaponDamage)} ± {Flat(_snapshot.Stats.WeaponDamageRange)}"
            : Flat(_snapshot.Stats.WeaponDamage));
        AddRow(section, "Damage", () => "+" + Pct(_snapshot.Stats.Damage));
        AddRow(section, "Attack Speed", () => $"{_snapshot.Stats.AttackSpeed:0.00}");
        AddRow(section, "Weapon Reach", () => Flat(_snapshot.Stats.WeaponReach));
        AddRow(section, "Critical Chance", () => Pct(_snapshot.Stats.CriticalChance));
        AddRow(section, "Critical Damage", () => "+" + Pct(_snapshot.Stats.CriticalDamage));
        AddRow(section, "Area of Effect", () => "+" + Pct(_snapshot.Stats.AreaOfEffect));
        AddRow(section, "Cooldown Reduction", () => Pct(_snapshot.Stats.CooldownReduction));
        AddRow(section, "Resource Cost Reduction", () => Pct(_snapshot.Stats.ResourceCostReduction));
        AddRow(section, "Lifesteal", () => Pct(_snapshot.Stats.Lifesteal));
        AddRow(section, "Resource Steal", () => Pct(_snapshot.Stats.ResourceSteal));
        AddRow(section, "Pierce", () => _snapshot.Stats.Pierce.ToString());
        AddRow(section, "Bounces", () => _snapshot.Stats.Bounces.ToString());

        section = AddSection(list, "Defense");
        AddRow(section, "Armor", () => WithMitigation(_snapshot.Armor.Value, ArmorMitigation()));
        AddRow(section, "Physical Resistance", () => Pct(_snapshot.Stats.PhysicalResistance));
        AddRow(section, "Block Chance", () => Pct(_snapshot.Stats.BlockChance));
        AddRow(section, "Dodge Chance", () => Pct(_snapshot.Stats.DodgeChance));

        section = AddSection(list, "Resistances");
        AddRow(section, "Fire", () => Resistance(_snapshot.Stats.FireResistance));
        AddRow(section, "Cold", () => Resistance(_snapshot.Stats.ColdResistance));
        AddRow(section, "Lightning", () => Resistance(_snapshot.Stats.LightningResistance));
        AddRow(section, "Poison", () => Resistance(_snapshot.Stats.PoisonResistance));
        AddRow(section, "All Resistance", () => "+" + Flat(_snapshot.Stats.AllResistance));

        section = AddSection(list, "Other");
        AddRow(section, "Movement Speed", () => "+" + Pct(_snapshot.Stats.MovementSpeed));
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

    void AddRow(VisualElement section, string name, Func<string> get) => AddRow(section, null, get, name);

    void AddRow(VisualElement section, Func<string> getName, Func<string> get) => AddRow(section, getName, get, "");

    void AddRow(VisualElement section, Func<string> getName, Func<string> get, string name)
    {
        var row = new VisualElement();
        row.AddToClassList("cp-row");
        var nameLabel = new Label(name);
        nameLabel.AddToClassList("cp-row__name");
        var valueLabel = new Label();
        valueLabel.AddToClassList("cp-row__value");
        row.Add(nameLabel);
        row.Add(valueLabel);
        section.Add(row);

        _rows.Add(new StatRow { Value = valueLabel, Get = get });
        // Names that depend on the player (resource type) are refreshed like values
        if (getName != null) _rows.Add(new StatRow { Value = nameLabel, Get = getName });
    }

    void RefreshStats()
    {
        TakeSnapshot();
        _header.text = _snapshot.HasPlayer ? $"{_snapshot.Class} · Level {_snapshot.Level}" : "No character";

        foreach (var row in _rows)
        {
            var text = _snapshot.HasPlayer ? row.Get() : NoValue;
            if (text == row.Shown) continue;
            row.Value.text = text;
            row.Shown = text;
        }
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
    float? ArmorMitigation()
    {
        if (!_snapshot.HasCombatConfig) return null;
        float effectiveArmor = Mathf.Max(0, _snapshot.Armor.Value) * (1f + _snapshot.Armor.PhysicalResistanceMultiplier);
        return CombatConfig.Mitigation(effectiveArmor, _snapshot.Combat.ArmorConstant);
    }

    string Resistance(float resistance)
    {
        float total = resistance + _snapshot.Stats.AllResistance;
        float? mitigation = _snapshot.HasCombatConfig
            ? CombatConfig.Mitigation(total, _snapshot.Combat.ElementalResistanceConstant)
            : null;
        return WithMitigation(total, mitigation);
    }

    static string WithMitigation(float value, float? mitigation) =>
        mitigation.HasValue ? $"{Flat(value)} ({Pct(mitigation.Value)})" : Flat(value);

    static string ResourceName(ResourceType type) => type == ResourceType.None ? "Resource" : type.ToString();

    static string Flat(float value) => value.ToString("0.#");

    static string Pct(float fraction) => (fraction * 100f).ToString("0.#") + "%";
}
