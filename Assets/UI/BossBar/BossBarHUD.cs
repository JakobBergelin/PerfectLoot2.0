using System.Collections.Generic;
using Effects;
using Passives;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// At the top of the screen while a dungeon's boss (DungeonBoss) is fought: its name, health (with the health just lost
/// draining behind it, and marks where its Health Spawns go off) and buffs and debuffs. From its intro (FocusIntroPlayed)
/// until it dies. One bar per boss, over the whole screen in co-op. Read-only: never changes ECS.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class BossBarHUD : MonoBehaviour
{
    const string ManyClass = "boss-bars--many", TileHiddenClass = "effect-tile--hidden";
    const float LostDelay = 0.5f;       // Seconds after a hit until the lost health drains
    const float LostSpeed = 0.35f;      // Share of the bar per second

    // In someone else's game the effects stay on the host; the statuses it sends are shown without their timers
    static readonly (byte Bit, EffectType Type)[] Statuses =
    {
        (NetUnitFeedback.Bleeding, EffectType.Bleeding), (NetUnitFeedback.Burning, EffectType.Burning),
        (NetUnitFeedback.Poisoned, EffectType.Poisoned), (NetUnitFeedback.Chilled, EffectType.Chilled),
        (NetUnitFeedback.Frozen, EffectType.Frozen),
    };

    class Bar
    {
        public VisualElement Element, Fill, Lost, Effects;
        public Label Value;
        public readonly Dictionary<EffectType, EffectTile> Tiles = new Dictionary<EffectType, EffectTile>();
        public float Share = 1f, LostShare = 1f, DrainAt;
        public bool Seen;
    }

    [Tooltip("The same library the game bakes; used for icons")]
    public EffectLibrary Library;

    /// <summary>How far down the screen the bars reach, in panel pixels; 0 without a boss. The unit panel goes below.</summary>
    public static float Bottom { get; private set; }

    /// <summary>True for a boss that has a bar: alive, and met by a player.</summary>
    public static bool IsShown(EntityManager entityManager, Entity unit) =>
        entityManager.HasComponent<DungeonBoss>(unit) && entityManager.HasComponent<FocusIntroPlayed>(unit);

    PanelRenderer _panelRenderer;
    VisualElement _bars;
    int _boundVersion = -1;
    readonly Dictionary<Entity, Bar> _shown = new Dictionary<Entity, Bar>();
    readonly List<Entity> _gone = new List<Entity>();
    World _world;
    EntityQuery _query;

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDisable()
    {
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        _bars = null;
        _boundVersion = -1;
        Bottom = 0f;
    }

    void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        if (_bars != null && version == _boundVersion) return;

        // The Panel Renderer's own container isn't part of the UXML; make it fill the screen without taking clicks
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        root.pickingMode = PickingMode.Ignore;

        _bars = root.Q<VisualElement>("bars");
        if (_bars == null) Debug.LogError("BossBarHUD: no bars. Is BossBar.uxml assigned as the Panel Renderer's Source Asset?", this);
        _shown.Clear();
        _boundVersion = version;
    }

    void LateUpdate()
    {
        if (_bars == null) return;
        foreach (var bar in _shown.Values) bar.Seen = false;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated)
        {
            var entityManager = world.EntityManager;
            if (world != _world)
            {
                _world = world;
                _query = entityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<DungeonBoss>(), ComponentType.ReadOnly<FocusIntroPlayed>(), ComponentType.ReadOnly<UnitInfo>(), ComponentType.ReadOnly<Health>() },
                    None = new[] { ComponentType.ReadOnly<RigidDeath>() },
                });
            }
            using var bosses = _query.ToEntityArray(Allocator.Temp);
            foreach (var boss in bosses)
            {
                var health = entityManager.GetComponentData<Health>(boss);
                if (health.CurrentValue <= 0f || health.MaxValue <= 0f) continue;
                if (!_shown.TryGetValue(boss, out var bar)) _shown[boss] = bar = NewBar(entityManager.GetComponentData<UnitInfo>(boss).Source.Value as IUnitDescription, health);
                bar.Seen = true;
                ShowHealth(bar, health);
                ShowEffects(bar, entityManager, boss);
            }
        }

        _gone.Clear();
        foreach (var pair in _shown)
            if (!pair.Value.Seen) _gone.Add(pair.Key);
        foreach (var boss in _gone)
        {
            _shown[boss].Element.RemoveFromHierarchy();
            _shown.Remove(boss);
        }
        _bars.EnableInClassList(ManyClass, _shown.Count > 1);
        Bottom = _shown.Count == 0 || float.IsNaN(_bars.layout.yMax) ? 0f : _bars.layout.yMax;
    }

    Bar NewBar(IUnitDescription unit, in Health health)
    {
        var bar = new Bar { Element = NewElement("boss-bar") };
        bar.Share = bar.LostShare = Mathf.Clamp01(health.CurrentValue / health.MaxValue);
        var name = new Label(unit != null ? unit.DisplayName : "") { pickingMode = PickingMode.Ignore };
        name.AddToClassList("boss-bar__name");
        var frame = NewElement("boss-bar__bar");
        frame.Add(bar.Lost = NewElement("boss-bar__lost"));
        frame.Add(bar.Fill = NewElement("boss-bar__fill"));
        bar.Fill.Add(NewElement("boss-bar__band"));
        if (unit != null)
            foreach (var passive in unit.Passives)
                if (passive is HealthSpawnsPassive spawns)
                    foreach (var threshold in spawns.Thresholds)
                    {
                        var mark = NewElement("boss-bar__mark");
                        mark.style.left = Length.Percent(100f * threshold.HealthBelow);
                        frame.Add(mark);
                    }
        bar.Value = new Label { pickingMode = PickingMode.Ignore };
        bar.Value.AddToClassList("boss-bar__value");
        frame.Add(bar.Value);
        bar.Element.Add(name);
        bar.Element.Add(frame);
        bar.Element.Add(bar.Effects = NewElement("boss-bar__effects"));
        _bars.Add(bar.Element);
        return bar;
    }

    static VisualElement NewElement(string styleClass)
    {
        var element = new VisualElement { pickingMode = PickingMode.Ignore };
        element.AddToClassList(styleClass);
        return element;
    }

    static void ShowHealth(Bar bar, in Health health)
    {
        float share = Mathf.Clamp01(health.CurrentValue / health.MaxValue);
        // The first hit of a burst starts the wait, so steady damage still drains
        if (share < bar.Share && bar.LostShare <= bar.Share + 1e-4f) bar.DrainAt = Time.time + LostDelay;
        bar.Share = share;
        if (Time.time >= bar.DrainAt) bar.LostShare -= LostSpeed * Time.deltaTime;
        bar.LostShare = Mathf.Max(bar.LostShare, share);

        bar.Fill.style.width = Length.Percent(100f * share);
        bar.Lost.style.width = Length.Percent(100f * bar.LostShare);
        bar.Value.text = $"{Mathf.CeilToInt(health.CurrentValue):N0} / {Mathf.CeilToInt(health.MaxValue):N0}";
    }

    void ShowEffects(Bar bar, EntityManager entityManager, Entity boss)
    {
        foreach (var tile in bar.Tiles.Values) tile.Active = false;
        if (entityManager.HasBuffer<EffectDisplay>(boss))
        {
            foreach (var effect in entityManager.GetBuffer<EffectDisplay>(boss, isReadOnly: true)) ShowEffect(bar, effect);
        }
        else if (entityManager.HasComponent<NetUnitFeedback>(boss))
        {
            byte statuses = entityManager.GetComponentData<NetUnitFeedback>(boss).Statuses;
            foreach (var (bit, type) in Statuses)
                if ((statuses & bit) != 0) ShowEffect(bar, new EffectDisplay { Type = type, Stacks = 1 });
        }
        foreach (var tile in bar.Tiles.Values) tile.Element.EnableInClassList(TileHiddenClass, !tile.Active);
    }

    void ShowEffect(Bar bar, EffectDisplay effect)
    {
        if (!bar.Tiles.TryGetValue(effect.Type, out var tile))
        {
            var definition = Library != null ? Library.Get(effect.Type) : null;
            if (definition == null) return;
            bar.Tiles[effect.Type] = tile = new EffectTile(definition);
            tile.Element.pickingMode = PickingMode.Ignore;
            bar.Effects.Add(tile.Element);
        }
        tile.Active = true;
        tile.Effect = effect;
        tile.Refresh();
    }

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Bottom = 0f;
}
