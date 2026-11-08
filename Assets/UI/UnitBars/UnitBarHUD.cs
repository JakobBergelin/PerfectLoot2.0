using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Each player's health and resource bars over their head, with the values on them, in screen space so the numbers stay
/// sharp. One group per player entity, so any number of players. Anchored where the player's world health bar stick is
/// (HealthBarUI), which HealthUISystem and ResourceUISystem hide for players. Enemies' and companions' bars are drawn
/// in the world in the same style (UnitBar.shader). Layout and colours are in UnitBars.uss.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class UnitBarHUD : MonoBehaviour
{
    const string GroupClass = "unit-bars", HiddenClass = "unit-bars--hidden";
    const float FallbackHeight = 1.3f;  // Meters over a player's root (its capsule's centre) when it has no world health bar

    class Bar
    {
        public VisualElement Fill;
        public VisualElement Barrier;       // Over the fill from the left: the barrier as a share of max (health bar only)
        public Label Value;
        public int Shown = int.MinValue;    // The numbers on it, so the text only changes when they do
        public int ShownBarrier;
    }

    class Group
    {
        public VisualElement Root;
        public Bar Health, Resource;
        public string ResourceClass;    // Colours the resource bar by its type, e.g. "unit-bar--mana"
        public bool Seen;
    }

    PanelRenderer _panelRenderer;
    VisualElement _layer;
    int _boundVersion = -1;
    readonly Dictionary<Entity, Group> _groups = new Dictionary<Entity, Group>();
    readonly List<Entity> _stale = new List<Entity>();
    World _queryWorld;
    EntityQuery _players;

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDisable()
    {
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        _groups.Clear();
        _layer = null;
        _boundVersion = -1;
    }

    void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        if (_layer != null && version == _boundVersion) return;

        // The Panel Renderer's own container isn't part of the UXML; make it fill the screen
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;

        // New tree: the old groups are gone with it
        _groups.Clear();
        _layer = root.Q<VisualElement>("unitBarsLayer");
        if (_layer == null)
            Debug.LogError("UnitBarHUD: no element named unitBarsLayer. Is UnitBars.uxml assigned as the Panel Renderer's Source Asset?", this);
        _boundVersion = version;
    }

    void LateUpdate()
    {
        var camera = Camera.main;
        var world = World.DefaultGameObjectInjectionWorld;
        if (_layer == null || _layer.panel == null || camera == null || world == null || !world.IsCreated) return;

        var entityManager = world.EntityManager;
        if (_queryWorld != world)
        {
            _players = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>(), ComponentType.ReadOnly<Health>(), ComponentType.ReadOnly<LocalToWorld>());
            _queryWorld = world;
            foreach (var group in _groups.Values) group.Root.RemoveFromHierarchy();
            _groups.Clear();
        }

        foreach (var group in _groups.Values) group.Seen = false;
        using (var players = _players.ToEntityArray(Allocator.Temp))
        {
            foreach (var player in players)
            {
                if (!_groups.TryGetValue(player, out var group)) _groups[player] = group = CreateGroup();
                group.Seen = true;

                var health = entityManager.GetComponentData<Health>(player);
                float barrier = entityManager.HasComponent<Barrier>(player) ? entityManager.GetComponentData<Barrier>(player).Value : 0f;
                SetBar(group.Health, health.CurrentValue, health.MaxValue, barrier);
                bool hasResource = entityManager.HasComponent<Resource>(player);
                group.Resource.Fill.parent.style.display = hasResource ? DisplayStyle.Flex : DisplayStyle.None;
                if (hasResource)
                {
                    var resource = entityManager.GetComponentData<Resource>(player);
                    SetBar(group.Resource, resource.CurrentValue, resource.MaxValue);
                    var type = entityManager.HasComponent<UnitResource>(player) ? entityManager.GetComponentData<UnitResource>(player).Type : ResourceType.None;
                    SetResourceClass(group, type);
                }

                // Where its world health bar would be, over its head (FallbackHeight over its root without one)
                var stick = entityManager.HasComponent<HealthBarUI>(player) ? entityManager.GetComponentData<HealthBarUI>(player).StickEntity : Entity.Null;
                Vector3 anchor = stick != Entity.Null && entityManager.HasComponent<LocalToWorld>(stick)
                    ? entityManager.GetComponentData<LocalToWorld>(stick).Position
                    : (Vector3)entityManager.GetComponentData<LocalToWorld>(player).Position + Vector3.up * FallbackHeight;
                bool behindCamera = camera.WorldToViewportPoint(anchor).z < 0f;
                group.Root.EnableInClassList(HiddenClass, behindCamera);
                if (behindCamera) continue;
                Vector2 panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(_layer.panel, anchor, camera);
                group.Root.style.translate = new Translate(panelPosition.x, panelPosition.y);
            }
        }

        // Players that are gone (died, left)
        _stale.Clear();
        foreach (var (player, group) in _groups)
            if (!group.Seen) { group.Root.RemoveFromHierarchy(); _stale.Add(player); }
        foreach (var player in _stale) _groups.Remove(player);
    }

    Group CreateGroup()
    {
        var root = new VisualElement { pickingMode = PickingMode.Ignore };
        root.AddToClassList(GroupClass);
        _layer.Add(root);
        var group = new Group { Root = root, Health = CreateBar(root, "unit-bar--health"), Resource = CreateBar(root, "unit-bar--resource") };

        // The barrier lies over the health fill, under the numbers
        group.Health.Barrier = new VisualElement { pickingMode = PickingMode.Ignore };
        group.Health.Barrier.AddToClassList("unit-bar__barrier");
        var healthBar = group.Health.Fill.parent;
        healthBar.Insert(healthBar.IndexOf(group.Health.Value), group.Health.Barrier);
        return group;
    }

    static Bar CreateBar(VisualElement parent, string kindClass)
    {
        var bar = new VisualElement { pickingMode = PickingMode.Ignore };
        bar.AddToClassList("unit-bar");
        bar.AddToClassList(kindClass);
        var fill = new VisualElement { pickingMode = PickingMode.Ignore };
        fill.AddToClassList("unit-bar__fill");
        var band = new VisualElement { pickingMode = PickingMode.Ignore };
        band.AddToClassList("unit-bar__band");
        fill.Add(band);
        bar.Add(fill);
        var value = new Label { pickingMode = PickingMode.Ignore };
        value.AddToClassList("unit-bar__value");
        bar.Add(value);
        parent.Add(bar);
        return new Bar { Fill = fill, Value = value };
    }

    // One class per resource type, swapped when the type changes (switching character)
    static void SetResourceClass(Group group, ResourceType type)
    {
        string resourceClass = "unit-bar--" + type.ToString().ToLowerInvariant();
        if (resourceClass == group.ResourceClass) return;
        var bar = group.Resource.Fill.parent;
        if (group.ResourceClass != null) bar.RemoveFromClassList(group.ResourceClass);
        bar.AddToClassList(resourceClass);
        group.ResourceClass = resourceClass;
    }

    // Rounded up, so a sliver of health left never reads 0. A barrier shows over the fill and as "+N" after the number.
    static void SetBar(Bar bar, float current, float max, float barrier = 0f)
    {
        float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        bar.Fill.style.width = Length.Percent(fraction * 100f);
        if (bar.Barrier != null) bar.Barrier.style.width = Length.Percent(max > 0f ? Mathf.Clamp01(barrier / max) * 100f : 0f);

        int shown = Mathf.CeilToInt(Mathf.Max(0f, current) - 0.001f);
        int shownBarrier = Mathf.CeilToInt(Mathf.Max(0f, barrier) - 0.001f);
        if (shown == bar.Shown && shownBarrier == bar.ShownBarrier) return;
        bar.Shown = shown;
        bar.ShownBarrier = shownBarrier;
        bar.Value.text = shownBarrier > 0 ? $"{shown} <color=#DDEEFF>+{shownBarrier}</color>" : shown.ToString();
    }
}
