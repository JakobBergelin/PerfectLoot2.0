using System.Collections.Generic;
using Effects;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The player's buffs (left) and debuffs (right) just above the skill bar: icon (or short name), time sweep, stacks,
/// and a tooltip on hover. Reads the player's EffectDisplay list; names and icons come from the EffectLibrary.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class EffectBarHUD : MonoBehaviour
{
    const string HiddenClass = "effect-tile--hidden";
    const string TooltipVisibleClass = "effect-tooltip--visible";

    [Tooltip("The same library the game bakes; used for names, icons and descriptions")]
    public EffectLibrary Library;

    class TileView
    {
        public EffectType Type;
        public EffectDefinition Definition;
        public VisualElement Element;
        public CooldownSweep Sweep;
        public Label Stacks;
        public int ShownStacks = -1;

        // This frame's state
        public bool Active;
        public EffectDisplay Effect;
        public int TotalStacks => Effect.Stacks;
        public float TimeRemaining => Effect.TimeRemaining;
        public float Duration => Effect.Duration;
    }

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;

    VisualElement _root, _buffs, _debuffs, _tooltip;
    Label _tooltipTitle, _tooltipBody, _tooltipFooter;
    readonly Dictionary<EffectType, TileView> _tiles = new Dictionary<EffectType, TileView>();
    readonly List<EffectType> _shownOrder = new List<EffectType>();
    readonly List<EffectType> _order = new List<EffectType>();
    TileView _hovered;

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDisable()
    {
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

        _root = root.Q<VisualElement>("effectBarRoot");
        _buffs = root.Q<VisualElement>("buffs");
        _debuffs = root.Q<VisualElement>("debuffs");
        _tooltip = root.Q<VisualElement>("tooltip");
        _tooltipTitle = root.Q<Label>("tooltipTitle");
        _tooltipBody = root.Q<Label>("tooltipBody");
        _tooltipFooter = root.Q<Label>("tooltipFooter");
        _bound = _root != null && _buffs != null && _debuffs != null && _tooltip != null;
        if (!_bound) Debug.LogError("EffectBarHUD: EffectBar.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);

        // New tree: the old tiles are gone
        _tiles.Clear();
        _shownOrder.Clear();
        _hovered = null;
        _boundVersion = version;
    }

    void LateUpdate()
    {
        if (!_bound) return;

        foreach (var tile in _tiles.Values) tile.Active = false;
        GatherEffects();

        // Buffs and debuffs in EffectType order, so tiles don't jump around when timers refresh
        _order.Clear();
        foreach (var tile in _tiles.Values)
            if (tile.Active) _order.Add(tile.Type);
        _order.Sort();
        if (!SameOrder()) Relayout();

        foreach (var tile in _tiles.Values)
        {
            if (tile.Active) UpdateTile(tile);
            else tile.Element.AddToClassList(HiddenClass);
        }

        if (_hovered != null)
        {
            if (_hovered.Active) UpdateTooltipText(_hovered);
            else HideTooltip();
        }
    }

    void GatherEffects()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var characterManager = CharacterManager.Instance;
        if (world == null || !world.IsCreated || characterManager == null || Library == null) return;

        var entityManager = world.EntityManager;
        var player = characterManager.Player;
        if (player == Entity.Null || !entityManager.Exists(player) || !entityManager.HasBuffer<EffectDisplay>(player)) return;

        // One entry per active effect, built by EffectDisplaySystem
        foreach (var effect in entityManager.GetBuffer<EffectDisplay>(player, isReadOnly: true))
        {
            var tile = GetTile(effect.Type);
            if (tile == null) continue;
            tile.Active = true;
            tile.Effect = effect;
        }
    }

    TileView GetTile(EffectType type)
    {
        if (_tiles.TryGetValue(type, out var tile)) return tile;
        var definition = Library.Get(type);
        if (definition == null) return null;

        tile = new TileView { Type = type, Definition = definition };
        tile.Element = new VisualElement();
        tile.Element.AddToClassList("effect-tile");
        tile.Element.AddToClassList(definition.Kind == EffectKind.Buff ? "effect-tile--buff" : "effect-tile--debuff");

        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("effect-tile__icon");
        var fallback = new Label { pickingMode = PickingMode.Ignore };
        fallback.AddToClassList("effect-tile__fallback");
        if (definition.Icon != null) icon.style.backgroundImage = new StyleBackground(definition.Icon);
        else fallback.text = ShortName(definition.DisplayName);

        tile.Sweep = new CooldownSweep { pickingMode = PickingMode.Ignore };
        tile.Sweep.AddToClassList("effect-tile__sweep");
        tile.Stacks = new Label { pickingMode = PickingMode.Ignore };
        tile.Stacks.AddToClassList("effect-tile__stacks");

        tile.Element.Add(icon);
        tile.Element.Add(fallback);
        tile.Element.Add(tile.Sweep);
        tile.Element.Add(tile.Stacks);
        tile.Element.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(tile));
        tile.Element.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());

        _tiles.Add(type, tile);
        return tile;
    }

    bool SameOrder()
    {
        if (_order.Count != _shownOrder.Count) return false;
        for (int i = 0; i < _order.Count; i++)
            if (_order[i] != _shownOrder[i]) return false;
        return true;
    }

    void Relayout()
    {
        _buffs.Clear();
        _debuffs.Clear();
        foreach (var type in _order)
        {
            var tile = _tiles[type];
            (tile.Definition.Kind == EffectKind.Buff ? _buffs : _debuffs).Add(tile.Element);
        }
        _shownOrder.Clear();
        _shownOrder.AddRange(_order);
    }

    static void UpdateTile(TileView tile)
    {
        tile.Element.RemoveFromClassList(HiddenClass);

        // Darkens as it runs out; untimed effects have no sweep
        tile.Sweep.Fraction = tile.Duration > 0f ? 1f - tile.TimeRemaining / tile.Duration : 0f;

        if (tile.TotalStacks != tile.ShownStacks)
        {
            tile.ShownStacks = tile.TotalStacks;
            tile.Stacks.text = tile.TotalStacks > 1 ? tile.TotalStacks.ToString() : "";
        }
    }

    void ShowTooltip(TileView tile)
    {
        _hovered = tile;
        bool buff = tile.Definition.Kind == EffectKind.Buff;
        _tooltipTitle.text = tile.Definition.DisplayName;
        _tooltipTitle.EnableInClassList("effect-tooltip__title--buff", buff);
        _tooltipTitle.EnableInClassList("effect-tooltip__title--debuff", !buff);
        UpdateTooltipText(tile);

        // Above the tile, in the root's coordinates (the bar sits low on the screen)
        _tooltip.AddToClassList(TooltipVisibleClass);
        var position = tile.Element.ChangeCoordinatesTo(_root, Vector2.zero);
        _tooltip.style.left = position.x;
        _tooltip.style.top = StyleKeyword.Auto;
        _tooltip.style.bottom = _root.layout.height - position.y + 8f;
    }

    // Description and footer follow the live effect (stacks, time, magnitude)
    void UpdateTooltipText(TileView tile)
    {
        _tooltipBody.text = tile.Definition.Describe(tile.Effect);
        string time = tile.Duration > 0f ? $"{tile.TimeRemaining:0.0}s left" : "Until removed";
        _tooltipFooter.text = tile.TotalStacks > 1 ? $"{time} · {tile.TotalStacks} stacks" : time;
    }

    void HideTooltip()
    {
        _hovered = null;
        _tooltip?.RemoveFromClassList(TooltipVisibleClass);
    }

    static string ShortName(string name) => name.Length <= 3 ? name : name.Substring(0, 3);
}
