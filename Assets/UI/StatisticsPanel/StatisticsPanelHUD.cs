using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Statistics panel toggled with the Statistics action (O): the account's or the played character's numbers, in tabs.
/// General lists the totals; the other tabs have one row per enemy, skill or dungeon that has a number, sortable by
/// clicking a heading. Read-only. Numbers come from AccountManager.Statistics (StatisticsRecorder counts them).
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class StatisticsPanelHUD : MonoBehaviour
{
    const string HiddenClass = "stats-root--hidden", SelectedClass = "stats-chip--selected";
    const float RefreshInterval = 0.5f;

    // One number column of a tab
    struct Column
    {
        public string Heading;
        public StatType Type;
        public Column(string heading, StatType type) { Heading = heading; Type = type; }
    }

    class Tab
    {
        public string Name;
        public string Subject;          // The name column's heading; null for General (one row per total)
        public Column[] Columns;
    }

    static readonly Tab[] Tabs =
    {
        new Tab
        {
            Name = "General",
            Columns = new[]
            {
                new Column("Time played", StatType.TimePlayed),
                new Column("Enemies killed", StatType.EnemiesKilled),
                new Column("Bosses killed", StatType.BossesKilled),
                new Column("Times died", StatType.Deaths),
                new Column("Dungeons cleared", StatType.DungeonsCleared),
                new Column("Experience earned", StatType.ExperienceEarned),
                new Column("Damage dealt", StatType.DamageDealt),
            },
        },
        new Tab { Name = "Enemies", Subject = "Enemy", Columns = new[] { new Column("Killed", StatType.EnemiesKilled), new Column("Killed you", StatType.Deaths) } },
        new Tab { Name = "Skills", Subject = "Skill or effect", Columns = new[] { new Column("Damage dealt", StatType.DamageDealt) } },
        new Tab { Name = "Dungeons", Subject = "Dungeon", Columns = new[] { new Column("Cleared", StatType.DungeonsCleared) } },
    };

    class Row
    {
        public string Name;
        public double[] Values;
    }

    PanelRenderer _panelRenderer;
    bool _bound, _open;
    int _boundVersion = -1;
    VisualElement _root, _scopes, _tabs, _headings;
    ScrollView _rows;
    Label _empty;
    readonly List<Label> _tabChips = new List<Label>();
    Label _accountChip, _characterChip;

    int _tab;
    bool _byCharacter;
    int _sortColumn = 1;        // 0: by name, otherwise that number column (1-based)
    bool _sortDescending = true;
    float _nextRefresh;
    string _shownHeadings;

    InputAction _toggleAction;

    void Awake()
    {
        _toggleAction = GameInput.Actions.asset.FindAction("Player/Statistics");
        if (_toggleAction == null)
            Debug.LogWarning("StatisticsPanelHUD: no Player/Statistics input action. Reimport InputSystem_Actions.inputactions.", this);
    }

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
        MenuPanels.OpenChanged += OnMenuOpenChanged;
    }

    void OnDisable()
    {
        MenuPanels.OpenChanged -= OnMenuOpenChanged;
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
        _root = root.Q<VisualElement>("statisticsRoot");
        _scopes = root.Q<VisualElement>("scopes");
        _tabs = root.Q<VisualElement>("tabs");
        _headings = root.Q<VisualElement>("headings");
        _rows = root.Q<ScrollView>("rows");
        _empty = root.Q<Label>("empty");
        if (_root == null || _scopes == null || _tabs == null || _headings == null || _rows == null || _empty == null)
        {
            Debug.LogError("StatisticsPanelHUD: StatisticsPanel.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }
        _rows.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        _rows.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

        _scopes.Clear();
        _accountChip = Chip(_scopes, "Account", () => SetScope(false));
        _characterChip = Chip(_scopes, "Character", () => SetScope(true));

        _tabs.Clear();
        _tabChips.Clear();
        _shownHeadings = null;
        for (int i = 0; i < Tabs.Length; i++)
        {
            int index = i;
            _tabChips.Add(Chip(_tabs, Tabs[i].Name, () => SetTab(index)));
        }
        return true;
    }

    static Label Chip(VisualElement parent, string text, Action onClick)
    {
        var chip = new Label(text);
        chip.AddToClassList("stats-chip");
        chip.RegisterCallback<ClickEvent>(_ => onClick());
        parent.Add(chip);
        return chip;
    }

    void Update()
    {
        if (_toggleAction != null && _toggleAction.WasPressedThisFrame() && !MenuPanels.IsTyping)
            SetOpen(!_open);
        if (!_bound || !_open) return;

        if (MenuPanels.EscapePressed)
        {
            SetOpen(false);
            return;
        }
        // Time played and the rest keep counting while it's open
        if (Time.unscaledTime >= _nextRefresh) Refresh();
    }

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
        if (open) Refresh();
    }

    void SetScope(bool byCharacter)
    {
        _byCharacter = byCharacter;
        Refresh();
    }

    void SetTab(int tab)
    {
        _tab = tab;
        _sortColumn = Tabs[tab].Subject != null ? 1 : 0;
        _sortDescending = true;
        _rows.scrollOffset = Vector2.zero;
        Refresh();
    }

    // Clicking the sorted column flips it; another column sorts by it, numbers biggest first and names A to Z
    void SortBy(int column)
    {
        if (column == _sortColumn) _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = column;
            _sortDescending = column > 0;
        }
        Refresh();
    }

    void Refresh()
    {
        _nextRefresh = Time.unscaledTime + RefreshInterval;
        var account = AccountManager.Instance;
        var character = account != null ? account.SelectedCharacter : null;
        if (character == null) _byCharacter = false;
        var set = account == null ? null : _byCharacter ? account.Statistics.Character(character.Guid) : account.Statistics.Account;

        _characterChip.text = character != null ? character.Name : "Character";
        _characterChip.style.display = character != null ? DisplayStyle.Flex : DisplayStyle.None;
        _accountChip.EnableInClassList(SelectedClass, !_byCharacter);
        _characterChip.EnableInClassList(SelectedClass, _byCharacter);
        for (int i = 0; i < _tabChips.Count; i++) _tabChips[i].EnableInClassList(SelectedClass, i == _tab);

        var tab = Tabs[_tab];
        var rows = tab.Subject != null ? SubjectRows(set, tab) : TotalRows(set, tab);
        ShowHeadings(tab);
        ShowRows(tab, rows);
    }

    // General: one row per total
    static List<Row> TotalRows(StatisticsSet set, Tab tab)
    {
        var rows = new List<Row>();
        foreach (var column in tab.Columns)
            rows.Add(new Row { Name = column.Heading, Values = new[] { set != null ? set.Get(column.Type) : 0.0 } });
        return rows;
    }

    // One row per subject that has a number in any of the tab's columns
    List<Row> SubjectRows(StatisticsSet set, Tab tab)
    {
        var bySubject = new Dictionary<string, Row>();
        if (set != null)
        {
            foreach (var entry in set.Entries)
            {
                if (string.IsNullOrEmpty(entry.Subject) || entry.Value == 0.0) continue;
                int column = Array.FindIndex(tab.Columns, c => c.Type == entry.Type);
                if (column < 0) continue;
                if (!bySubject.TryGetValue(entry.Subject, out var row))
                    bySubject[entry.Subject] = row = new Row { Name = entry.Name, Values = new double[tab.Columns.Length] };
                row.Values[column] = entry.Value;
            }
        }

        var rows = new List<Row>(bySubject.Values);
        rows.Sort((a, b) =>
        {
            int order = _sortColumn == 0
                ? string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase)
                : a.Values[_sortColumn - 1].CompareTo(b.Values[_sortColumn - 1]);
            if (_sortDescending) order = -order;
            return order != 0 ? order : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });
        return rows;
    }

    // Only rebuilt when the tab or sorting changed, so a click on a heading isn't lost to a refresh
    void ShowHeadings(Tab tab)
    {
        string shown = $"{_tab}/{_sortColumn}/{_sortDescending}";
        if (shown == _shownHeadings) return;
        _shownHeadings = shown;
        _headings.Clear();
        if (tab.Subject == null)
        {
            _headings.style.display = DisplayStyle.None;
            return;
        }
        _headings.style.display = DisplayStyle.Flex;
        AddHeading(tab.Subject, 0, true);
        for (int i = 0; i < tab.Columns.Length; i++) AddHeading(tab.Columns[i].Heading, i + 1, false);
    }

    void AddHeading(string text, int column, bool isName)
    {
        var heading = new Label(text + (column == _sortColumn ? (_sortDescending ? " ▼" : " ▲") : ""));
        heading.AddToClassList("stats-cell");
        heading.AddToClassList("stats-cell--heading");
        heading.AddToClassList("stats-cell--sortable");
        if (isName) heading.AddToClassList("stats-cell--name");
        heading.EnableInClassList("stats-cell--sorted", column == _sortColumn);
        heading.RegisterCallback<ClickEvent>(_ => SortBy(column));
        _headings.Add(heading);
    }

    void ShowRows(Tab tab, List<Row> rows)
    {
        _rows.Clear();
        _empty.style.display = rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        foreach (var row in rows)
        {
            var element = new VisualElement();
            element.AddToClassList("stats-row");
            element.Add(Cell(row.Name, "stats-cell--name"));
            for (int i = 0; i < row.Values.Length; i++)
            {
                // General's values are in one column, each row its own stat
                var type = tab.Subject == null ? tab.Columns[rows.IndexOf(row)].Type : tab.Columns[i].Type;
                element.Add(Cell(Format(type, row.Values[i]), row.Values[i] == 0.0 ? "stats-cell--zero" : null));
            }
            _rows.Add(element);
        }
    }

    static Label Cell(string text, string extraClass)
    {
        var cell = new Label(text);
        cell.AddToClassList("stats-cell");
        if (extraClass != null) cell.AddToClassList(extraClass);
        return cell;
    }

    static string Format(StatType type, double value)
    {
        if (type == StatType.TimePlayed)
        {
            var time = TimeSpan.FromSeconds(value);
            return time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes:00}m" : $"{time.Minutes}m {time.Seconds:00}s";
        }
        // Totals with a fraction (damage) are shown whole, never rounded up to look like more
        return Math.Floor(value).ToString("N0");
    }
}
