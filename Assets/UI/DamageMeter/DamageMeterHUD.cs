using System.Collections.Generic;
using System.Text;
using ActiveSkills;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Shows the DamageMeter in DamageMeter.uxml: a stat per tab, by unit or by skill, each row as per second over the last 10
/// seconds and since the level started, highest level rate first. ToggleKey or the title bar's button minimizes it, and
/// gameplay input is ignored while the pointer is over it. Read-only: never changes ECS.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class DamageMeterHUD : MonoBehaviour
{
    const string SelectedTabClass = "damage-meter__tab--selected";
    const string HiddenBodyClass = "damage-meter__body--hidden";
    const float RefreshInterval = 0.25f;

    class RowView
    {
        public VisualElement Root, Bar;
        public Label Name, Recent, Level;
    }

    struct RowData
    {
        public string Name;
        public float Recent, Level;
    }

    public Key ToggleKey = Key.M;

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    VisualElement _panel, _body;
    Button _toggle, _view;
    ScrollView _rows;
    readonly Button[] _tabs = new Button[4];
    readonly List<RowView> _rowViews = new List<RowView>();
    readonly List<RowData> _data = new List<RowData>();
    readonly Dictionary<DamageLabel, string> _labels = new Dictionary<DamageLabel, string>();

    bool _minimized = true;
    bool _bySource;
    MeterStat _stat = MeterStat.DamageDone;
    float _nextRefresh;

    // Cached because the meter is read several times a second
    World _queryWorld;
    EntityQuery _meterQuery;

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        // Called on initial setup and whenever the UI is rebuilt (e.g. the UXML is reimported)
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDisable()
    {
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        MenuPanels.SetBlocking(this, false);
        _bound = false;
        _boundVersion = -1;
    }

    void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        // Same UI as last time: already bound
        if (_bound && version == _boundVersion) return;

        // The container the Panel Renderer creates isn't part of the UXML: make it fill the screen, so the meter
        // anchored to its corner ends up in the corner of the screen
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        root.pickingMode = PickingMode.Ignore;

        _panel = root.Q<VisualElement>("panel");
        _body = root.Q<VisualElement>("body");
        _toggle = root.Q<Button>("toggle");
        _view = root.Q<Button>("view");
        _rows = root.Q<ScrollView>("rows");
        _rowViews.Clear();

        _panel.RegisterCallback<PointerEnterEvent>(_ => MenuPanels.SetBlocking(this, true));
        _panel.RegisterCallback<PointerLeaveEvent>(_ => MenuPanels.SetBlocking(this, false));
        _toggle.clicked += () => SetMinimized(!_minimized);
        _view.clicked += () =>
        {
            _bySource = !_bySource;
            Refresh();
        };
        for (int i = 0; i < _tabs.Length; i++)
        {
            var stat = (MeterStat)i;
            _tabs[i] = root.Q<Button>("tab" + i);
            _tabs[i].clicked += () =>
            {
                _stat = stat;
                Refresh();
            };
        }

        _bound = true;
        _boundVersion = version;
        SetMinimized(_minimized);
    }

    void SetMinimized(bool minimized)
    {
        _minimized = minimized;
        _toggle.text = minimized ? "+" : "-";
        _body.EnableInClassList(HiddenBodyClass, minimized);
        if (!minimized) Refresh();
    }

    void Update()
    {
        if (!_bound) return;

        var keyboard = Keyboard.current;
        if (keyboard != null && !MenuPanels.IsTyping && keyboard[ToggleKey].wasPressedThisFrame) SetMinimized(!_minimized);

        if (_minimized || Time.unscaledTime < _nextRefresh) return;
        Refresh();
    }

    void Refresh()
    {
        _nextRefresh = Time.unscaledTime + RefreshInterval;
        for (int i = 0; i < _tabs.Length; i++)
            _tabs[i].EnableInClassList(SelectedTabClass, (MeterStat)i == _stat);
        _view.text = _bySource ? "By skill" : "By unit";

        ReadRows();
        ShowRows();
    }

    void ReadRows()
    {
        _data.Clear();
        if (!TryGetMeter(out var meter)) return;

        // At least a second, so the first hit of a level isn't a huge rate
        float recentSeconds = Mathf.Max(1f, meter.RecentSeconds);
        float levelSeconds = Mathf.Max(1f, meter.Elapsed);
        int second = meter.Second;
        foreach (var pair in meter.Rows)
        {
            if (pair.Key.Stat != _stat || pair.Key.BySource != _bySource) continue;
            _data.Add(new RowData
            {
                Name = NameOf(pair.Key.Name),
                Recent = pair.Value.Recent(second) / recentSeconds,
                Level = pair.Value.Total / levelSeconds,
            });
        }
        _data.Sort((a, b) => b.Level.CompareTo(a.Level));
    }

    bool TryGetMeter(out DamageMeter meter)
    {
        meter = default;
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;

        if (_queryWorld != world)
        {
            _meterQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<DamageMeter>());
            _queryWorld = world;
        }
        if (_meterQuery.IsEmpty) return false;

        meter = _meterQuery.GetSingleton<DamageMeter>();
        return meter.Rows.IsCreated && meter.StartTime >= 0.0;
    }

    void ShowRows()
    {
        while (_rowViews.Count < _data.Count) _rowViews.Add(CreateRow());

        float highest = _data.Count > 0 ? _data[0].Level : 0f;
        for (int i = 0; i < _rowViews.Count; i++)
        {
            var view = _rowViews[i];
            bool used = i < _data.Count;
            view.Root.style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
            if (!used) continue;

            var row = _data[i];
            view.Name.text = row.Name;
            view.Recent.text = Format(row.Recent);
            view.Level.text = Format(row.Level);
            view.Bar.style.width = Length.Percent(highest > 0f ? 100f * row.Level / highest : 0f);
        }
    }

    RowView CreateRow()
    {
        var view = new RowView
        {
            Root = new VisualElement(),
            Bar = new VisualElement(),
            Name = new Label(),
            Recent = new Label(),
            Level = new Label(),
        };
        view.Root.AddToClassList("damage-meter__row");
        view.Bar.AddToClassList("damage-meter__bar");
        view.Name.AddToClassList("damage-meter__name");
        view.Recent.AddToClassList("damage-meter__number");
        view.Level.AddToClassList("damage-meter__number");
        view.Root.Add(view.Bar);
        view.Root.Add(view.Name);
        view.Root.Add(view.Recent);
        view.Root.Add(view.Level);
        _rows.Add(view.Root);
        return view;
    }

    static string Format(float perSecond) => perSecond.ToString(perSecond >= 100f ? "0" : "0.0");

    string NameOf(in MeterName name)
    {
        if (name.Label != DamageLabel.None) return LabelText(name.Label);
        if (name.Player > 0) return "Player " + name.Player;

        var unit = name.Unit.Value;
        if (unit is IUnitDescription description) return description.DisplayName;
        if (unit is ActiveSkill skill && !string.IsNullOrEmpty(skill.Name)) return skill.Name;
        return unit != null ? unit.name : "Unknown";
    }

    // "FrozenOrb" as "Frozen Orb"
    string LabelText(DamageLabel label)
    {
        if (_labels.TryGetValue(label, out var text)) return text;

        var spaced = new StringBuilder();
        foreach (char c in label.ToString())
        {
            if (char.IsUpper(c) && spaced.Length > 0) spaced.Append(' ');
            spaced.Append(c);
        }
        return _labels[label] = spaced.ToString();
    }
}
