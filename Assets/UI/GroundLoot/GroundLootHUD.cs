using System.Collections.Generic;
using Equipment;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A small label above every item on the ground (GroundLootManager): its name in its rarity's color. Clicking it picks
/// the item up when the mouse's player is within reach; labels out of everyone's reach are dimmed. Each player's
/// Interact action picks up the item in reach nearest them, whose label is marked and shows their key (unless a portal
/// in reach takes their Interact, PortalHUD). Picked-up items are the account's, so both players share them. Labels that
/// would overlap on screen are stacked upward.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class GroundLootHUD : MonoBehaviour
{
    const string LabelClass = "gl-label";
    const string FarClass = "gl-label--far";
    const string HiddenClass = "gl-label--hidden";
    const string NextClass = "gl-label--next";
    const float StackGap = 2f;

    [Tooltip("Height above the item where the label's bottom edge sits, in meters")]
    public float LabelHeight = 0.3f;

    class View
    {
        public GroundLootManager.GroundItem Item;
        public string Name;
        public Label Label;
        public Rect Rect;           // Where it goes this frame, in panel space
        public bool Visible;
    }

    PanelRenderer _panelRenderer;
    int _boundVersion = -1;
    VisualElement _root;
    int _itemsVersion = -1;
    GroundLootManager _manager;
    readonly List<View> _views = new List<View>();
    readonly List<View> _placed = new List<View>();
    readonly HashSet<Label> _hovered = new HashSet<Label>();
    // Per player: the item their Interact picks up, and their Interact key
    readonly GroundLootManager.GroundItem[] _next = new GroundLootManager.GroundItem[GameInput.MaxPlayers];
    readonly string[] _keys = new string[GameInput.MaxPlayers];

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDisable()
    {
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        _boundVersion = -1;
        _root = null;
        _views.Clear();
        ClearHovered();
    }

    void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        if (_root != null && version == _boundVersion) return;

        // The Panel Renderer's own container isn't part of the UXML; make it fill the screen without taking clicks
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        root.pickingMode = PickingMode.Ignore;

        _root = root.Q<VisualElement>("groundLootRoot");
        _boundVersion = version;
        if (_root == null)
        {
            Debug.LogError("GroundLootHUD: GroundLootHUD.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return;
        }
        _views.Clear();
        _itemsVersion = -1;
        ClearHovered();
    }

    void LateUpdate()
    {
        if (_root == null || _root.panel == null) return;
        var manager = GroundLootManager.Instance;
        if (manager != _manager || (manager != null && manager.Version != _itemsVersion)) Rebuild(manager);
        if (manager == null || _views.Count == 0) return;

        // Each player's Interact takes the item in reach nearest them, unless they're at a portal (PortalHUD)
        for (int i = 0; i < _next.Length; i++)
        {
            var controls = GameInput.For(i);
            _next[i] = controls != null && !PortalHUD.AtPortal(i) ? manager.NearestInReach(i) : null;
            _keys[i] = controls != null ? controls.Label(controls.Actions.Player.Interact) : "";
        }
        foreach (var view in _views)
        {
            int player = System.Array.IndexOf(_next, view.Item);
            view.Label.EnableInClassList(NextClass, player >= 0);
            string text = player >= 0 && _keys[player].Length > 0 ? $"{view.Name} ({_keys[player]})" : view.Name;
            if (view.Label.text != text) view.Label.text = text;
        }
        for (int i = 0; i < _next.Length; i++)
        {
            var controls = GameInput.For(i);
            var menus = MenuPanels.For(i);
            if (_next[i] == null || menus.IsPanelOpen || menus.IsTyping || UiNav.For(i).HasGamepadInput) continue;
            if (controls.Actions.Player.Interact.WasPressedThisFrame() && manager.PickUp(_next[i], i)) return;
        }

        var camera = Camera.main;
        foreach (var view in _views)
        {
            var anchor = view.Item.Position + Vector3.up * LabelHeight;
            view.Visible = camera != null && camera.WorldToViewportPoint(anchor).z > 0f;
            view.Label.EnableInClassList(HiddenClass, !view.Visible);
            if (!view.Visible) continue;

            // Size from the last layout (0 on the label's first frame)
            var point = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, anchor, camera);
            float width = float.IsNaN(view.Label.resolvedStyle.width) ? 0f : view.Label.resolvedStyle.width;
            float height = float.IsNaN(view.Label.resolvedStyle.height) ? 0f : view.Label.resolvedStyle.height;
            view.Rect = new Rect(point.x - width * 0.5f, point.y - height, width, height);
            view.Label.EnableInClassList(FarClass, !manager.InReachOfAnyone(view.Item));
        }

        Stack();
        foreach (var view in _views)
        {
            if (!view.Visible) continue;
            view.Label.style.left = view.Rect.x;
            view.Label.style.top = view.Rect.y;
        }
    }

    // Lowest on screen first (nearest the camera); each later label moves up until it overlaps none placed before it
    void Stack()
    {
        _placed.Clear();
        foreach (var view in _views)
            if (view.Visible) _placed.Add(view);
        _placed.Sort((a, b) => b.Rect.y.CompareTo(a.Rect.y));
        for (int i = 1; i < _placed.Count; i++)
        {
            var rect = _placed[i].Rect;
            for (int pass = 0; pass < _placed.Count; pass++)
            {
                bool moved = false;
                for (int j = 0; j < i; j++)
                {
                    var other = _placed[j].Rect;
                    if (!rect.Overlaps(other)) continue;
                    rect.y = other.y - rect.height - StackGap;
                    moved = true;
                }
                if (!moved) break;
            }
            _placed[i].Rect = rect;
        }
    }

    void Rebuild(GroundLootManager manager)
    {
        _manager = manager;
        _itemsVersion = manager != null ? manager.Version : -1;
        _root.Clear();
        _views.Clear();
        ClearHovered();
        if (manager == null) return;

        foreach (var item in manager.Items)
        {
            string name = item.Item.BaseItem != null ? item.Item.BaseItem.Name : "Item";
            var label = new Label(name);
            label.AddToClassList(LabelClass);
            var color = ItemText.TitleColor(item.Item);
            label.style.color = color;
            label.style.borderTopColor = color;
            label.style.borderBottomColor = color;
            label.style.borderLeftColor = color;
            label.style.borderRightColor = color;

            var groundItem = item;
            label.RegisterCallback<ClickEvent>(_ => manager.PickUp(groundItem, GameInput.MouseOwner));
            // While over a label the click is for it, not a skill
            label.RegisterCallback<PointerEnterEvent>(_ => SetHovered(label, true));
            label.RegisterCallback<PointerLeaveEvent>(_ => SetHovered(label, false));

            _root.Add(label);
            _views.Add(new View { Item = item, Name = name, Label = label });
        }
    }

    void SetHovered(Label label, bool hovered)
    {
        if (hovered) _hovered.Add(label);
        else _hovered.Remove(label);
        MenuPanels.SetBlocking(this, _hovered.Count > 0);
    }

    // Removed labels never get PointerLeave
    void ClearHovered()
    {
        _hovered.Clear();
        MenuPanels.SetBlocking(this, false);
    }
}
