using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Equipment;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Item database panel toggled with the ItemDatabase action (L): one row per item kind with a tile per rarity
/// (Normal/Magic/Rare side by side; Legendary/Unique have one), filter chips, pages, a hover tooltip and a detail
/// pane. Found items show their rolls, who uses them and where they drop; items not found yet only show where they
/// drop. Read-only: equipping happens in the inventory. PanelSettings has no theme, so all controls are plain
/// elements styled by ItemDatabasePanel.uss.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class ItemDatabasePanelHUD : MonoBehaviour
{
    const string HiddenClass = "idb-root--hidden";
    const string TooltipVisibleClass = "idb-tooltip--visible";
    const string AffixPopupVisibleClass = "idb-affix-popup--visible";
    const string ChipClass = "idb-chip";
    const string ChipSelectedClass = "idb-chip--selected";
    const string ChipDisabledClass = "idb-chip--disabled";
    const string MissingClass = "idb-tile--missing";
    const string SelectedClass = "idb-tile--selected";
    const float TooltipWidth = 290f;
    const int RowsPerPage = 10;

    static readonly Color EmptyBorder = new Color32(58, 63, 75, 255);
    static readonly Rarity[] FilterRarities = { Rarity.Normal, Rarity.Magic, Rarity.Rare, Rarity.Legendary, Rarity.Unique };
    static readonly ItemType[] FilterSlots = (ItemType[])Enum.GetValues(typeof(ItemType));
    static readonly Affix[] FilterAffixes = (Affix[])Enum.GetValues(typeof(Affix));

    enum FoundFilter { All, Found, NotFound }

    class TileView
    {
        public ItemDatabase.Entry Entry;
        public VisualElement Element;
        public VisualElement Icon;
        public Label Mystery;
        public Label Equipped;
    }

    // One item kind: its name and a tile per rarity
    class RowView
    {
        public VisualElement Element;
        public readonly List<TileView> Tiles = new List<TileView>();
    }

    // Chips where exactly one is selected (index 0 is "All")
    class ChipGroup
    {
        public readonly List<Label> Chips = new List<Label>();
        public int Selected;

        public void Select(int index)
        {
            Selected = index;
            for (int i = 0; i < Chips.Count; i++) Chips[i].EnableInClassList(ChipSelectedClass, i == index);
        }
    }

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    bool _open;
    bool _dirty = true;

    VisualElement _root;
    Label _count;
    VisualElement _rowContainer;
    Label _prevPage, _nextPage, _pageLabel;
    Label _detailTitle, _detailSubtitle, _detailBody;
    VisualElement _affixPopup;
    VisualElement _tooltip;
    Label _tooltipTitle, _tooltipSubtitle, _tooltipBody;
    readonly List<RowView> _rows = new List<RowView>();
    ItemDatabase _builtFor;
    TileView _selected;
    int _page;

    // Filters
    ChipGroup _slotChips, _rarityChips, _foundChips;
    Label _equippedChip, _affixChip;
    bool _equippedOnly;
    Affix? _affix;
    TextField _searchField;

    ItemDatabase _subscribedDatabase;
    InventoryManager _subscribedInventory;

    InputSystem_Actions _actions;
    InputAction _toggleAction;

    void Awake()
    {
        _actions = new InputSystem_Actions();
        _toggleAction = _actions.asset.FindAction("Player/ItemDatabase");
        if (_toggleAction == null)
            Debug.LogWarning("ItemDatabasePanelHUD: no Player/ItemDatabase input action. Reimport InputSystem_Actions.inputactions.", this);
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
        _root = root.Q<VisualElement>("itemDatabaseRoot");
        _count = root.Q<Label>("count");
        var filters = root.Q<VisualElement>("filters");
        _rowContainer = root.Q<VisualElement>("rows");
        _prevPage = root.Q<Label>("prevPage");
        _nextPage = root.Q<Label>("nextPage");
        _pageLabel = root.Q<Label>("pageLabel");
        _detailTitle = root.Q<Label>("detailTitle");
        _detailSubtitle = root.Q<Label>("detailSubtitle");
        _detailBody = root.Q<Label>("detailBody");
        _affixPopup = root.Q<VisualElement>("affixPopup");
        _tooltip = root.Q<VisualElement>("tooltip");
        _tooltipTitle = root.Q<Label>("tooltipTitle");
        _tooltipSubtitle = root.Q<Label>("tooltipSubtitle");
        _tooltipBody = root.Q<Label>("tooltipBody");
        if (_root == null || _count == null || filters == null || _rowContainer == null || _prevPage == null ||
            _nextPage == null || _pageLabel == null || _detailBody == null || _affixPopup == null || _tooltip == null)
        {
            Debug.LogError("ItemDatabasePanelHUD: ItemDatabasePanel.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }

        BuildFilters(filters);
        BuildAffixPopup();
        _prevPage.RegisterCallback<ClickEvent>(_ => SetPage(_page - 1));
        _nextPage.RegisterCallback<ClickEvent>(_ => SetPage(_page + 1));
        // The wheel flips pages
        _rowContainer.RegisterCallback<WheelEvent>(evt => SetPage(_page + (evt.delta.y > 0 ? 1 : -1)));

        _rows.Clear();
        _builtFor = null;
        _selected = null;
        ShowDetail(null);
        _dirty = true;
        return true;
    }

    void Update()
    {
        // Typing an "l" into the search field shouldn't close the panel
        if (_toggleAction != null && _toggleAction.WasPressedThisFrame() && !MenuPanels.IsTyping)
            SetOpen(!_open);

        if (!_bound || !_open) return;

        // Esc leaves the search field first, then closes the affix picker, then the panel
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (MenuPanels.IsTyping) _searchField.Blur();
            else if (_affixPopup.ClassListContains(AffixPopupVisibleClass)) ShowAffixPopup(false);
            else SetOpen(false);
            return;
        }

        Subscribe();
        if (_dirty) Refresh();
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
        // A hidden field keeps focus (and IsTyping) unless it's blurred
        if (!open) _searchField?.Blur();
        _root.EnableInClassList(HiddenClass, !open);
        ShowAffixPopup(false);
        HideTooltip();
        _dirty = true;
    }

    // The database and inventory may not exist yet when this is enabled (and the inventory changes with the scene)
    void Subscribe()
    {
        var database = AccountManager.Instance != null ? AccountManager.Instance.Items : null;
        if (database != _subscribedDatabase)
        {
            if (_subscribedDatabase != null) _subscribedDatabase.ItemChanged -= OnItemChanged;
            _subscribedDatabase = database;
            if (database != null) database.ItemChanged += OnItemChanged;
            _dirty = true;
        }

        var inventory = InventoryManager.Instance;
        if (inventory != _subscribedInventory)
        {
            if (_subscribedInventory != null) _subscribedInventory.StatsChanged -= OnInventoryChanged;
            _subscribedInventory = inventory;
            if (inventory != null) inventory.StatsChanged += OnInventoryChanged;
            _dirty = true;
        }
    }

    void Unsubscribe()
    {
        if (_subscribedDatabase != null) _subscribedDatabase.ItemChanged -= OnItemChanged;
        if (_subscribedInventory != null) _subscribedInventory.StatsChanged -= OnInventoryChanged;
        _subscribedDatabase = null;
        _subscribedInventory = null;
    }

    void OnItemChanged(ItemKey key) => _dirty = true;
    void OnInventoryChanged(StatSummary summary) => _dirty = true;

    // ---------- Filters ----------

    void BuildFilters(VisualElement container)
    {
        container.Clear();

        var row = AddFilterRow(container, "Slot");
        _slotChips = AddChipGroup(row, "All", FilterSlots.Select(slot => slot.ToString()));

        row = AddFilterRow(container, "Rarity");
        _rarityChips = AddChipGroup(row, "All", FilterRarities.Select(rarity => rarity.ToString()));
        for (int i = 0; i < FilterRarities.Length; i++)
        {
            var color = RarityColors.Get(FilterRarities[i]) ?? Color.white;
            _rarityChips.Chips[i + 1].style.borderBottomColor = color;
            _rarityChips.Chips[i + 1].style.borderBottomWidth = 3;
        }

        row = AddFilterRow(container, "Show");
        _foundChips = AddChipGroup(row, "All", new[] { "Found", "Not found" });

        AddSpacer(row);
        _equippedChip = AddChip(row, "Equipped only", () =>
        {
            _equippedOnly = !_equippedOnly;
            _equippedChip.EnableInClassList(ChipSelectedClass, _equippedOnly);
            FiltersChanged();
        });

        AddSpacer(row);
        _affixChip = AddChip(row, "", () => ShowAffixPopup(!_affixPopup.ClassListContains(AffixPopupVisibleClass)));
        UpdateAffixChip();

        AddSpacer(row);
        var searchTitle = new Label("Search");
        searchTitle.AddToClassList("idb-filter-title");
        row.Add(searchTitle);
        _searchField = new TextField();
        _searchField.AddToClassList("idb-search");
        _searchField.RegisterValueChangedCallback(_ => FiltersChanged());
        MenuPanels.TrackTyping(_searchField);
        row.Add(_searchField);
    }

    static VisualElement AddFilterRow(VisualElement container, string title)
    {
        var row = new VisualElement();
        row.AddToClassList("idb-filter-row");
        var label = new Label(title);
        label.AddToClassList("idb-filter-title");
        row.Add(label);
        container.Add(row);
        return row;
    }

    static void AddSpacer(VisualElement row)
    {
        var spacer = new VisualElement();
        spacer.AddToClassList("idb-filter-spacer");
        row.Add(spacer);
    }

    static Label AddChip(VisualElement row, string text, Action onClick)
    {
        var chip = new Label(text);
        chip.AddToClassList(ChipClass);
        chip.RegisterCallback<ClickEvent>(_ => onClick());
        row.Add(chip);
        return chip;
    }

    ChipGroup AddChipGroup(VisualElement row, string all, IEnumerable<string> options)
    {
        var group = new ChipGroup();
        int index = 0;
        foreach (var text in new[] { all }.Concat(options))
        {
            int chipIndex = index++;
            group.Chips.Add(AddChip(row, text, () => { group.Select(chipIndex); FiltersChanged(); }));
        }
        group.Select(0);
        return group;
    }

    void BuildAffixPopup()
    {
        _affixPopup.Clear();
        AddChip(_affixPopup, "Any affix", () => PickAffix(null));
        foreach (var affix in FilterAffixes)
        {
            var value = affix;
            AddChip(_affixPopup, ItemText.AffixName(affix), () => PickAffix(value));
        }
    }

    void ShowAffixPopup(bool show)
    {
        if (_affixPopup == null) return;
        if (show)
        {
            // Under the Affix chip, in the root's coordinates
            var position = _affixChip.ChangeCoordinatesTo(_root, new Vector2(0f, _affixChip.layout.height + 4f));
            _affixPopup.style.left = position.x;
            _affixPopup.style.top = position.y;
        }
        _affixPopup.EnableInClassList(AffixPopupVisibleClass, show);
        _affixChip?.EnableInClassList(ChipSelectedClass, show || _affix != null);
    }

    void PickAffix(Affix? affix)
    {
        _affix = affix;
        UpdateAffixChip();
        ShowAffixPopup(false);
        FiltersChanged();
    }

    void UpdateAffixChip()
    {
        _affixChip.text = $"Affix: {(_affix != null ? ItemText.AffixName(_affix.Value) : "Any")} ▾";
        _affixChip.EnableInClassList(ChipSelectedClass, _affix != null);
    }

    void FiltersChanged()
    {
        _page = 0;
        _dirty = true;
    }

    bool Matches(ItemDatabase.Entry entry)
    {
        var key = entry.Key;
        if (_slotChips.Selected > 0 && key.BaseItem.ItemType != FilterSlots[_slotChips.Selected - 1]) return false;
        if (_rarityChips.Selected > 0 && key.Rarity != FilterRarities[_rarityChips.Selected - 1]) return false;

        var found = (FoundFilter)_foundChips.Selected;
        if (found == FoundFilter.Found && entry.IsEmpty) return false;
        if (found == FoundFilter.NotFound && !entry.IsEmpty) return false;

        // Only found items match an affix: unfound ones don't give their possible affixes away
        if (_affix != null && (entry.IsEmpty || !ItemText.HasAffix(entry.Item, _affix.Value))) return false;
        if (_equippedOnly && (_subscribedInventory == null || !_subscribedInventory.IsEquipped(key))) return false;

        var search = _searchField.value?.Trim();
        if (!string.IsNullOrEmpty(search) && (key.BaseItem.Name ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }

    // ---------- Rows, tiles and pages ----------

    void SetPage(int page)
    {
        if (page == _page) return;
        _page = page;
        _dirty = true;
    }

    void Refresh()
    {
        _dirty = false;
        var database = _subscribedDatabase;
        if (database == null)
        {
            _count.text = "No AccountManager";
            return;
        }
        if (database != _builtFor || CountTiles() != database.Entries.Count) BuildRows(database);

        // Rows with at least one matching tile, paged; non-matching tiles keep their space so columns line up
        var shownRows = new List<RowView>();
        int found = 0, total = 0, shownTiles = 0;
        foreach (var row in _rows)
        {
            bool any = false;
            foreach (var tile in row.Tiles)
            {
                RefreshTile(tile);
                bool match = Matches(tile.Entry);
                tile.Element.style.visibility = match ? Visibility.Visible : Visibility.Hidden;
                any |= match;
                if (match) shownTiles++;
                if (!tile.Entry.IsEmpty) found++;
                total++;
            }
            row.Element.style.display = DisplayStyle.None;
            if (any) shownRows.Add(row);
        }

        int pages = Mathf.Max(1, Mathf.CeilToInt(shownRows.Count / (float)RowsPerPage));
        _page = Mathf.Clamp(_page, 0, pages - 1);
        for (int i = _page * RowsPerPage; i < Mathf.Min(shownRows.Count, (_page + 1) * RowsPerPage); i++)
            shownRows[i].Element.style.display = DisplayStyle.Flex;

        _pageLabel.text = $"Page {_page + 1} / {pages}";
        _prevPage.EnableInClassList(ChipDisabledClass, _page == 0);
        _nextPage.EnableInClassList(ChipDisabledClass, _page >= pages - 1);
        _count.text = $"{found} / {total} found   ·   {shownTiles} shown";

        if (_selected != null) ShowDetail(_selected);
    }

    int CountTiles() => _rows.Sum(row => row.Tiles.Count);

    void BuildRows(ItemDatabase database)
    {
        _rowContainer.Clear();
        _rows.Clear();
        _builtFor = database;
        _selected = null;
        ShowDetail(null);

        // Entries are sorted by slot, name and rarity, so each kind's entries are next to each other
        foreach (var group in database.Entries.GroupBy(entry => entry.Key.BaseItem))
        {
            var row = new RowView { Element = new VisualElement() };
            row.Element.AddToClassList("idb-row");
            var name = new Label($"{group.Key.Name}\n<size=11><color=#A8ADB8>{group.Key.ItemType}</color></size>")
                { pickingMode = PickingMode.Ignore };
            name.AddToClassList("idb-row__name");
            row.Element.Add(name);

            foreach (var entry in group)
            {
                var tile = BuildTile(entry);
                row.Element.Add(tile.Element);
                row.Tiles.Add(tile);
            }
            _rowContainer.Add(row.Element);
            _rows.Add(row);
        }
    }

    TileView BuildTile(ItemDatabase.Entry entry)
    {
        var tile = new TileView { Entry = entry, Element = new VisualElement() };
        tile.Element.AddToClassList("idb-tile");

        tile.Icon = new VisualElement { pickingMode = PickingMode.Ignore };
        tile.Icon.AddToClassList("idb-tile__icon");
        tile.Mystery = new Label("?") { pickingMode = PickingMode.Ignore };
        tile.Mystery.AddToClassList("idb-tile__mystery");
        tile.Icon.Add(tile.Mystery);

        var rarityColor = RarityColors.Get(entry.Key.Rarity) ?? Color.white;
        var rarity = new Label(entry.Key.Rarity.ToString()) { pickingMode = PickingMode.Ignore };
        rarity.AddToClassList("idb-tile__rarity");
        rarity.style.color = rarityColor;

        tile.Equipped = new Label("E") { pickingMode = PickingMode.Ignore };
        tile.Equipped.AddToClassList("idb-tile__equipped");

        tile.Element.Add(tile.Icon);
        tile.Element.Add(rarity);
        tile.Element.Add(tile.Equipped);

        var border = RarityColors.Get(entry.Key.Rarity) ?? EmptyBorder;
        tile.Element.style.borderTopColor = border;
        tile.Element.style.borderBottomColor = border;
        tile.Element.style.borderLeftColor = border;
        tile.Element.style.borderRightColor = border;

        tile.Element.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(tile));
        tile.Element.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
        tile.Element.RegisterCallback<ClickEvent>(_ => Select(tile));
        return tile;
    }

    void RefreshTile(TileView tile)
    {
        bool found = !tile.Entry.IsEmpty;
        tile.Element.EnableInClassList(MissingClass, !found);
        tile.Mystery.style.display = found ? DisplayStyle.None : DisplayStyle.Flex;

        var icon = found ? tile.Entry.Key.BaseItem.Icon : null;
        tile.Icon.style.backgroundImage = icon != null ? new StyleBackground(icon) : new StyleBackground(StyleKeyword.None);

        bool equipped = _subscribedInventory != null && _subscribedInventory.IsEquipped(tile.Entry.Key);
        tile.Equipped.style.display = equipped ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void Select(TileView tile)
    {
        _selected?.Element.RemoveFromClassList(SelectedClass);
        _selected = tile;
        _selected?.Element.AddToClassList(SelectedClass);
        ShowDetail(tile);
    }

    // ---------- Detail and tooltip ----------

    void ShowDetail(TileView tile)
    {
        if (_detailTitle == null) return;
        if (tile == null)
        {
            _detailTitle.text = "";
            _detailSubtitle.text = "";
            _detailBody.text = "Select an item to see its details.";
            return;
        }

        var entry = tile.Entry;
        _detailTitle.text = entry.Key.BaseItem.Name;
        _detailTitle.style.color = RarityColors.Get(entry.Key.Rarity) ?? Color.white;
        _detailSubtitle.text = $"{entry.Key.Rarity} {entry.Key.BaseItem.ItemType}";

        var sb = new StringBuilder();
        if (entry.IsEmpty)
        {
            // Not found yet: only where to find it
            sb.AppendLine("Not found yet.");
        }
        else
        {
            sb.AppendLine(ItemText.Body(entry.Item));
            sb.AppendLine();
            sb.AppendLine("<b>Used by</b>");
            sb.AppendLine(UsedBy(entry.Key));
        }
        sb.AppendLine();
        sb.AppendLine("<b>Drops from</b>");
        var sources = AccountManager.Instance != null ? AccountManager.Instance.SourcesOf(entry.Key.BaseItem) : null;
        sb.Append(sources != null && sources.Count > 0 ? string.Join("\n", sources) : "Unknown");
        _detailBody.text = sb.ToString();
    }

    // Only one character for now: the current one
    string UsedBy(ItemKey key)
    {
        var slot = _subscribedInventory != null ? _subscribedInventory.SlotWith(key) : null;
        return slot != null ? $"Current character ({SlotName(slot)})" : $"<color={ItemText.RangeColor}>No one</color>";
    }

    // "Ring1Slot" -> "Ring 1"
    static string SlotName(InventorySlot slot)
    {
        var name = slot.name.EndsWith("Slot") ? slot.name.Substring(0, slot.name.Length - 4) : slot.name;
        return name.Length > 0 && char.IsDigit(name[name.Length - 1]) ? $"{name.Substring(0, name.Length - 1)} {name[name.Length - 1]}" : name;
    }

    void ShowTooltip(TileView tile)
    {
        if (tile.Entry.IsEmpty) { HideTooltip(); return; }
        var item = tile.Entry.Item;
        _tooltipTitle.text = item.BaseItem.Name;
        _tooltipTitle.style.color = ItemText.TitleColor(item);
        _tooltipSubtitle.text = ItemText.Subtitle(item);
        _tooltipBody.text = ItemText.Body(item);

        // Right of the tile, or left of it when there's no room, in the root's coordinates
        var position = tile.Element.ChangeCoordinatesTo(_root, new Vector2(tile.Element.layout.width + 8f, 0f));
        if (position.x + TooltipWidth > _root.layout.width)
            position = tile.Element.ChangeCoordinatesTo(_root, new Vector2(-TooltipWidth - 8f, 0f));
        _tooltip.style.left = position.x;
        _tooltip.style.top = position.y;
        _tooltip.AddToClassList(TooltipVisibleClass);
    }

    void HideTooltip() => _tooltip?.RemoveFromClassList(TooltipVisibleClass);
}
