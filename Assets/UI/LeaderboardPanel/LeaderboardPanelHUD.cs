using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A dungeon's leaderboards (Leaderboards): one tab per player count (1 to 4), each with the best finish of this player
/// and their Steam friends (without Steam: this account's own best), ranked by difficulty and then time, with the class
/// played and who they played with. Opened from a dungeon portal's card (PortalHUD) or once after a finish
/// (ShowAfterFinish, player 1's). Not one of the LB/RB menu panels. LT/RT switch tabs; B, Esc or Close close it.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class LeaderboardPanelHUD : MonoBehaviour, IPerPlayer
{
    /// <summary>Whose HUD this is: 0 is player 1 (the rig's own); player 2's copy is made by CoopManager.</summary>
    public int PlayerIndex { get; set; }

    const string HiddenClass = "lb-root--hidden", SelectedClass = "lb-chip--selected", MineClass = "lb-row--mine";
    const float FinishDelay = 2f;           // Seconds after a finish before the panel opens
    const float RefreshInterval = 1f;       // Names Steam is still looking up show once known
    const int MaxRows = 50;

    static readonly List<LeaderboardPanelHUD> _panels = new List<LeaderboardPanelHUD>();
    // A finish to show on player 1's panel, and when
    static (string DungeonId, string Name, int Players, bool NewBest)? _finish;
    static float _finishAt;

    PlayerMenus Menus => MenuPanels.For(PlayerIndex);
    PlayerNav Nav => UiNav.For(PlayerIndex);

    PanelRenderer _panelRenderer;
    bool _bound, _open;
    int _boundVersion = -1;
    VisualElement _root, _panel, _tabs, _rows;
    Label _title, _note, _empty, _source, _withHeading;
    readonly List<Label> _tabChips = new List<Label>();

    string _dungeonId, _dungeonName, _noteText;
    int _players = 1;
    int _request;                       // Counts reads, so an old answer for another tab is dropped
    List<LeaderboardRow> _shown;        // null while reading
    bool _failed;
    float _nextRefresh;

    /// <summary>Opens this player's panel on a dungeon's leaderboard for a player count (1 to 4), with an optional note on top.</summary>
    public static void Open(int playerIndex, string dungeonId, string dungeonName, int players, string note = null)
    {
        var panel = _panels.Find(candidate => candidate != null && candidate.PlayerIndex == playerIndex);
        if (panel != null) panel.Show(dungeonId, dungeonName, players, note);
    }

    /// <summary>A run just finished on this machine: player 1's panel opens on its leaderboard a moment later.</summary>
    public static void ShowAfterFinish(string dungeonId, string dungeonName, int players, bool newBest)
    {
        _finish = (dungeonId, dungeonName, players, newBest);
        _finishAt = Time.unscaledTime + FinishDelay;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _panels.Clear();
        _finish = null;
    }

    void OnEnable()
    {
        _panels.Add(this);
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
        Menus.OpenChanged += OnMenuOpenChanged;
    }

    void OnDisable()
    {
        _panels.Remove(this);
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
        _root = root.Q<VisualElement>("leaderboardRoot");
        _panel = root.Q<VisualElement>("panel");
        _tabs = root.Q<VisualElement>("tabs");
        _rows = root.Q<VisualElement>("rows");
        _title = root.Q<Label>("title");
        _note = root.Q<Label>("note");
        _empty = root.Q<Label>("empty");
        _source = root.Q<Label>("source");
        _withHeading = root.Q<Label>("withHeading");
        var close = root.Q<Label>("closeButton");
        if (_root == null || _panel == null || _tabs == null || _rows == null || _title == null || _note == null || _empty == null ||
            _source == null || _withHeading == null || close == null)
        {
            Debug.LogError("LeaderboardPanelHUD: LeaderboardPanel.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }
        Nav.Register(_root);
        if (_panel.Q(className: "nav-hint") == null) UiNav.Hint(_panel, "LT/RT Players · B Close");
        close.RegisterCallback<ClickEvent>(_ => SetOpen(false));
        UiNav.Clickable(close, () => SetOpen(false));

        _tabs.Clear();
        _tabChips.Clear();
        for (int i = 1; i <= Leaderboards.MaxPlayers; i++)
        {
            int players = i;
            var chip = new Label(i == 1 ? "1 player" : $"{i} players");
            chip.AddToClassList("lb-chip");
            chip.RegisterCallback<ClickEvent>(_ => SetTab(players));
            UiNav.Clickable(chip, () => SetTab(players));
            _tabs.Add(chip);
            _tabChips.Add(chip);
        }
        return true;
    }

    void Update()
    {
        // Player 1's panel shows a finish once its moment comes
        if (PlayerIndex == 0 && _finish is { } finish && Time.unscaledTime >= _finishAt)
        {
            _finish = null;
            Show(finish.DungeonId, finish.Name, finish.Players, finish.NewBest ? "New best!" : null);
        }
        if (!_bound || !_open) return;

        if (Menus.BackPressed)
        {
            SetOpen(false);
            return;
        }
        if (Menus.PagePressed != 0) SetTab((_players - 1 + Menus.PagePressed + Leaderboards.MaxPlayers) % Leaderboards.MaxPlayers + 1);
        if (Time.unscaledTime >= _nextRefresh) ShowRows();
    }

    void OnMenuOpenChanged(object openPanel)
    {
        if (_open && openPanel != null && !ReferenceEquals(openPanel, this)) SetOpen(false);
    }

    void Show(string dungeonId, string dungeonName, int players, string note)
    {
        if (string.IsNullOrEmpty(dungeonId)) return;
        _dungeonId = dungeonId;
        _dungeonName = dungeonName;
        _noteText = note;
        SetOpen(true);
        SetTab(Mathf.Clamp(players, 1, Leaderboards.MaxPlayers));
    }

    void SetOpen(bool open)
    {
        _open = open;
        Menus.SetOpen(this, open);
        if (!_bound) return;
        _root.EnableInClassList(HiddenClass, !open);
        Nav.SetScope(this, _panel, open);
    }

    // Reads the tab's leaderboard; the rows show when it answers
    void SetTab(int players)
    {
        _players = players;
        if (!_bound) return;
        for (int i = 0; i < _tabChips.Count; i++) _tabChips[i].EnableInClassList(SelectedClass, i == players - 1);
        _title.text = string.IsNullOrEmpty(_dungeonName) ? "Leaderboard" : $"{_dungeonName} · Leaderboard";
        _note.text = _noteText ?? "";
        _note.style.display = string.IsNullOrEmpty(_noteText) ? DisplayStyle.None : DisplayStyle.Flex;
        _source.text = Platform.Current.HasLeaderboards ? "You and your Steam friends" : "Your best (not on Steam)";
        _withHeading.style.visibility = players > 1 ? Visibility.Visible : Visibility.Hidden;

        _shown = null;
        _failed = false;
        ShowRows();
        int request = ++_request;
        Leaderboards.Read(_dungeonId, players, rows =>
        {
            if (this == null || request != _request) return;
            _failed = rows == null;
            _shown = rows ?? new List<LeaderboardRow>();
            ShowRows();
        });
    }

    void ShowRows()
    {
        _nextRefresh = Time.unscaledTime + RefreshInterval;
        _rows.Clear();
        string empty = _shown == null ? "Loading..." : _failed ? "Couldn't reach the leaderboard" : _shown.Count == 0 ? "No finishes yet" : "";
        _empty.text = empty;
        _empty.style.display = empty.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        if (_shown == null) return;

        for (int i = 0; i < _shown.Count && i < MaxRows; i++)
        {
            var row = _shown[i];
            var element = new VisualElement();
            element.AddToClassList("lb-row");
            element.EnableInClassList(MineClass, row.Mine);
            element.Add(Cell($"{row.Rank}", "lb-cell--rank"));
            element.Add(Cell(PlayerText(row), "lb-cell--name"));
            element.Add(Cell($"{row.Result.Difficulty}", "lb-cell--difficulty"));
            element.Add(Cell(Leaderboards.FormatTime(row.Result.Seconds), "lb-cell--time"));
            element.Add(Cell(_players > 1 ? WithText(row) : "", "lb-cell--with"));
            _rows.Add(element);
        }
    }

    // "Anna" over "Mage · couch: Rogue": the entry's owner and their machine's players
    static string PlayerText(LeaderboardRow row)
    {
        var own = row.Result.Members.FindAll(member => member.Owner);
        string name = row.OwnerId != 0 ? Platform.Current.NameOf(row.OwnerId) : null;
        if (string.IsNullOrEmpty(name)) name = own.Count > 0 && !string.IsNullOrEmpty(own[0].Name) ? own[0].Name : row.Mine ? Leaderboards.MachineName() : "Player";
        var classes = new List<string>();
        foreach (var member in own) classes.Add(member.LocalIndex > 0 ? $"couch: {ClassName(member.Class)}" : ClassName(member.Class));
        return classes.Count > 0 ? $"{name}\n<size=12><color=#A8ADB8>{string.Join(" · ", classes)}</color></size>" : name;
    }

    // "Bob (Rogue), Bob's couch player (Mage)": the rest of the group
    static string WithText(LeaderboardRow row)
    {
        var others = new List<string>();
        foreach (var member in row.Result.Members)
            if (!member.Owner) others.Add($"{Leaderboards.MemberName(member)} ({ClassName(member.Class)})");
        return string.Join(", ", others);
    }

    static string ClassName(ClassType type) => type == ClassType.None ? "no class" : type.ToString();

    static Label Cell(string text, string extraClass)
    {
        var cell = new Label(text);
        cell.AddToClassList("lb-cell");
        cell.AddToClassList(extraClass);
        return cell;
    }
}
