using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// One local player moving through their menus with a controller. A menu that is open registers its element as a scope
/// (SetScope); their newest scope is the one their D-pad or left stick moves focus in, and A presses what has focus (a
/// NavigationSubmitEvent, which Buttons and Clickable elements answer). Closing a scope returns focus to where it was in
/// the one below. B, LB/RB and LT/RT are read by the menus themselves (PlayerMenus). Focus here is the player's own
/// (the nav--focused-p1 or -p2 classes), not UI Toolkit's, so both players can move through menus on the same panel
/// at once; it shows while they use a controller. UiNav.For(index) gives a player's.
/// </summary>
public sealed class PlayerNav
{
    const float StickPress = 0.5f;
    const float RepeatDelay = 0.35f;
    const float RepeatInterval = 0.1f;

    class Scope
    {
        public object Owner;
        public VisualElement Root;
        public VisualElement Remembered;    // Where focus was in it; it goes back there when it's on top again
    }

    public readonly int PlayerIndex;

    readonly List<Scope> _scopes = new List<Scope>();
    readonly HashSet<object> _suspended = new HashSet<object>();
    readonly List<VisualElement> _pendingFirstOf = new List<VisualElement>();
    int _pendingFirstFrame;
    VisualElement _pendingFocus;
    VisualElement _focused;
    bool _focusShown;
    Vector2 _heldDirection;
    float _nextRepeat;

    internal PlayerNav(int playerIndex) => PlayerIndex = playerIndex;

    GameInput.PlayerControls Controls => GameInput.For(PlayerIndex);

    /// <summary>True when they used a controller last.</summary>
    public bool UsingGamepad => Controls != null && Controls.UsingGamepad;

    /// <summary>True while one of their menus that a controller can move through is open: A, B and the sticks are for it.</summary>
    public bool IsActive => _scopes.Count > 0;

    /// <summary>True while they use a controller and one of their menus has its input (gameplay leaves A and the sticks alone).</summary>
    public bool HasGamepadInput => IsActive && UsingGamepad;

    /// <summary>The element their focus is on, or null.</summary>
    public VisualElement Focused => _focused;

    /// <summary>
    /// Gives the root the shared styles (focus outline, hints, pages), and the nav--gamepad class while this player uses
    /// a controller (for hints). A root has one player: registering it again moves it to this one.
    /// </summary>
    public void Register(VisualElement root) => UiNav.Register(root, PlayerIndex);

    /// <summary>
    /// Opens or closes a menu for their controller. The newest open one gets the input; preferred (or else the first
    /// thing that can take focus) gets focus when it opens. Opening one that is already open only moves its root.
    /// </summary>
    public void SetScope(object owner, VisualElement root, bool open, VisualElement preferred = null)
    {
        int index = _scopes.FindIndex(scope => ReferenceEquals(scope.Owner, owner));
        if (!open)
        {
            if (index < 0) return;
            bool wasTop = index == _scopes.Count - 1;
            _scopes.RemoveAt(index);
            if (wasTop && _scopes.Count > 0) _pendingFocus = _scopes[_scopes.Count - 1].Remembered;
            return;
        }

        if (index >= 0)
        {
            _scopes[index].Root = root;
            if (preferred != null) Focus(preferred);
            return;
        }
        _scopes.Add(new Scope { Owner = owner, Root = root });
        _pendingFocus = preferred;
    }

    /// <summary>
    /// Stops moving focus and pressing with A while set, e.g. while Options waits for a button to bind (that button
    /// mustn't also press the focused one).
    /// </summary>
    public void Suspend(object owner, bool suspended)
    {
        if (suspended) _suspended.Add(owner);
        else _suspended.Remove(owner);
    }

    /// <summary>Gives the element their focus once it can take it (after layout), when they use a controller.</summary>
    public void Focus(VisualElement element) => _pendingFocus = element;

    /// <summary>
    /// Focuses the first of these that can take focus (e.g. the first item of a new page), on the next frame once they
    /// are laid out.
    /// </summary>
    public void FocusFirstOf(IEnumerable<VisualElement> elements)
    {
        _pendingFirstOf.Clear();
        _pendingFirstOf.AddRange(elements);
        _pendingFirstFrame = Time.frameCount;
    }

    /// <summary>True when the element is inside one of their open menus.</summary>
    public bool Contains(VisualElement element) => _scopes.Exists(scope => UiNav.IsInside(element, scope.Root));

    // ---------- Each frame (UiNavigator) ----------

    internal void Update()
    {
        _scopes.RemoveAll(scope => scope.Root == null || scope.Root.panel == null);
        if (_scopes.Count == 0)
        {
            _pendingFocus = null;
            MoveFocus(null);
            return;
        }

        bool gamepad = UsingGamepad;
        var top = _scopes[_scopes.Count - 1];
        var focused = _focused;
        if (focused != null && UiNav.IsInside(focused, top.Root) && UiNav.IsShown(focused, top.Root)) top.Remembered = focused;
        ShowFocus(gamepad);
        if (!gamepad || _suspended.Count > 0) return;

        // Focus where it was asked for, else keep it inside the open menu
        ResolveFirstOf();
        if (_pendingFocus != null && UiNav.IsInside(_pendingFocus, top.Root) && UiNav.IsShown(_pendingFocus, top.Root) && _pendingFocus.focusable)
        {
            MoveFocus(_pendingFocus);
            _pendingFocus = null;
            focused = _focused;
        }
        var gamepadDevice = Controls.GamepadDevice;
        if (focused == null || !UiNav.IsInside(focused, top.Root) || !UiNav.IsShown(focused, top.Root))
        {
            var first = top.Remembered != null && UiNav.IsShown(top.Remembered, top.Root) && top.Remembered != focused
                ? top.Remembered : UiNav.FirstFocusable(top.Root);
            MoveFocus(first);
            // The press that brought focus here (e.g. the first one with a controller) only shows where it is
            _heldDirection = ReadDirection(gamepadDevice);
            _nextRepeat = Time.unscaledTime + RepeatDelay;
            return;
        }

        if (gamepadDevice == null) return;
        var direction = ReadDirection(gamepadDevice);
        if (direction != Vector2.zero && (direction != _heldDirection || Time.unscaledTime >= _nextRepeat))
        {
            _nextRepeat = Time.unscaledTime + (direction != _heldDirection ? RepeatDelay : RepeatInterval);
            var next = UiNav.FindNext(top.Root, focused, direction);
            if (next != null) MoveFocus(next);
        }
        _heldDirection = direction;

        if (gamepadDevice.buttonSouth.wasPressedThisFrame)
        {
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = focused;
            focused.SendEvent(submit);
        }
    }

    void ResolveFirstOf()
    {
        if (_pendingFirstOf.Count == 0 || Time.frameCount <= _pendingFirstFrame) return;
        foreach (var element in _pendingFirstOf)
        {
            if (!UiNav.IsShown(element, null)) continue;
            var first = element.focusable ? element : UiNav.FirstFocusable(element);
            if (first == null) continue;
            _pendingFocus = first;
            break;
        }
        _pendingFirstOf.Clear();
    }

    // Moves their focus: the outline and the hover callbacks (UiNav.Hover) follow it
    void MoveFocus(VisualElement element)
    {
        if (_focused == element) return;
        ShowFocus(false);
        _focused = element;
        ShowFocus(element != null && UsingGamepad);
    }

    // The outline and hover only show while they use a controller
    void ShowFocus(bool show)
    {
        if (_focused == null || _focusShown == show) return;
        _focusShown = show;
        // Each player's own classes, so both can be on one element (the drop popup's button)
        _focused.EnableInClassList(PlayerIndex > 0 ? "nav-focus-p2" : "nav-focus-p1", show);
        _focused.EnableInClassList(PlayerIndex > 0 ? "nav--focused-p2" : "nav--focused-p1", show);
        if (show) UiNav.Entered(_focused);
        else UiNav.Left(_focused);
    }

    // The D-pad, or the left stick pushed past halfway, along its main axis (panel space: down is +y)
    static Vector2 ReadDirection(Gamepad gamepad)
    {
        if (gamepad == null) return Vector2.zero;
        Vector2 value = gamepad.dpad.ReadValue();
        if (value.sqrMagnitude < 0.25f) value = gamepad.leftStick.ReadValue();
        if (value.magnitude < StickPress) return Vector2.zero;
        return Mathf.Abs(value.x) > Mathf.Abs(value.y) ? new Vector2(Mathf.Sign(value.x), 0f) : new Vector2(0f, -Mathf.Sign(value.y));
    }

    internal void Reset()
    {
        _scopes.Clear();
        _suspended.Clear();
        _pendingFirstOf.Clear();
        _pendingFocus = null;
        _focused = null;
        _focusShown = false;
        _heldDirection = Vector2.zero;
    }
}

/// <summary>
/// Moving through menus with a controller: each local player's navigation (PlayerNav, For(index)) and what menus
/// share: the styles, hints that only show with a controller, and elements a controller can press (Clickable) or
/// hover (Hover). The static members without a player are player 1's (the main menu and what isn't per player yet).
/// Run by UiNavigator. UI Toolkit's own Navigate and Submit are switched off, so they never move focus outside the
/// open menu.
/// </summary>
public static class UiNav
{
    const string GamepadClass = "nav--gamepad";
    const string StyleSheetPath = "UiNav";      // Resources/UiNav.uss

    sealed class HoverActions
    {
        public Action Enter, Leave;
    }

    struct Root
    {
        public VisualElement Element;
        public int PlayerIndex;
    }

    static readonly PlayerNav[] _players = { new PlayerNav(0), new PlayerNav(1) };
    static readonly List<Root> _roots = new List<Root>();
    static readonly List<VisualElement> _candidates = new List<VisualElement>();
    static readonly ConditionalWeakTable<VisualElement, HoverActions> _hovers = new ConditionalWeakTable<VisualElement, HoverActions>();
    static StyleSheet _styleSheet;

    /// <summary>A local player's navigation (0 is player 1).</summary>
    public static PlayerNav For(int playerIndex) => _players[Mathf.Clamp(playerIndex, 0, _players.Length - 1)];

    /// <summary>The player whose open menu the element is in, or null.</summary>
    public static PlayerNav Of(VisualElement element)
    {
        foreach (var player in _players)
            if (player.Contains(element)) return player;
        return null;
    }

    // ---------- Player 1's ----------

    public static bool IsActive => For(0).IsActive;
    public static bool HasGamepadInput => For(0).HasGamepadInput;
    public static void Register(VisualElement root) => Register(root, 0);
    public static void SetScope(object owner, VisualElement root, bool open, VisualElement preferred = null) =>
        For(0).SetScope(owner, root, open, preferred);
    public static void Suspend(object owner, bool suspended) => For(0).Suspend(owner, suspended);
    public static void Focus(VisualElement element) => For(0).Focus(element);
    public static void FocusFirstOf(IEnumerable<VisualElement> elements) => For(0).FocusFirstOf(elements);

    // ---------- Setup ----------

    internal static void Register(VisualElement root, int playerIndex)
    {
        if (root == null) return;
        AddStyles(root);
        _roots.RemoveAll(entry => entry.Element == root);
        _roots.Add(new Root { Element = root, PlayerIndex = playerIndex });
        root.EnableInClassList(GamepadClass, For(playerIndex).UsingGamepad);
    }

    /// <summary>Only the shared styles (for Pager outside a registered root).</summary>
    public static void AddStyles(VisualElement root)
    {
        if (_styleSheet == null) _styleSheet = Resources.Load<StyleSheet>(StyleSheetPath);
        if (_styleSheet != null && !root.styleSheets.Contains(_styleSheet)) root.styleSheets.Add(_styleSheet);
    }

    /// <summary>
    /// Makes the element reachable with a controller: A runs onSubmit. Clicks still go through the element's own
    /// ClickEvent callback, which usually runs the same thing.
    /// </summary>
    public static void Clickable(VisualElement element, Action onSubmit)
    {
        element.focusable = true;
        element.RegisterCallback<NavigationSubmitEvent>(evt =>
        {
            onSubmit();
            evt.StopPropagation();
        });
    }

    /// <summary>
    /// Hover for both: the pointer entering or leaving, and a player's controller focus arriving or leaving. The element
    /// becomes focusable, so it can be reached even when A does nothing on it.
    /// </summary>
    public static void Hover(VisualElement element, Action enter, Action leave)
    {
        element.focusable = true;
        element.RegisterCallback<PointerEnterEvent>(_ => enter());
        element.RegisterCallback<PointerLeaveEvent>(_ => leave());
        _hovers.AddOrUpdate(element, new HoverActions { Enter = enter, Leave = leave });
    }

    internal static void Entered(VisualElement element)
    {
        if (_hovers.TryGetValue(element, out var actions)) actions.Enter?.Invoke();
    }

    internal static void Left(VisualElement element)
    {
        if (_hovers.TryGetValue(element, out var actions)) actions.Leave?.Invoke();
    }

    /// <summary>A line that only shows while a controller is used, e.g. "A Select · B Close".</summary>
    public static Label Hint(VisualElement parent, string text)
    {
        var hint = new Label(text) { pickingMode = PickingMode.Ignore };
        hint.AddToClassList("nav-hint");
        parent.Add(hint);
        return hint;
    }

    // ---------- Each frame (UiNavigator) ----------

    internal static void DisableBuiltInNavigation()
    {
        var map = InputSystem.actions != null ? InputSystem.actions.FindActionMap("UI") : null;
        if (map == null) return;
        foreach (var name in new[] { "Navigate", "Submit", "Cancel" })
            map.FindAction(name)?.Disable();
    }

    internal static void Update()
    {
        _roots.RemoveAll(entry => entry.Element.panel == null);
        foreach (var entry in _roots) entry.Element.EnableInClassList(GamepadClass, For(entry.PlayerIndex).UsingGamepad);
        foreach (var player in _players) player.Update();
    }

    // ---------- Finding elements ----------

    // The nearest element that can take focus in that direction, preferring ones in line with the current one. Only
    // elements wholly on that side count (a little overlap allowed: the focused one is scaled up), so a wide element
    // below two columns isn't "left" of the right one.
    internal static VisualElement FindNext(VisualElement root, VisualElement from, Vector2 direction)
    {
        const float Overlap = 10f;
        var fromRect = from.worldBound;
        VisualElement best = null;
        float bestScore = float.MaxValue;
        CollectFocusable(root, root);
        foreach (var candidate in _candidates)
        {
            if (candidate == from) continue;
            var rect = candidate.worldBound;
            float along = direction.x < 0f ? fromRect.xMin - rect.xMax
                        : direction.x > 0f ? rect.xMin - fromRect.xMax
                        : direction.y < 0f ? fromRect.yMin - rect.yMax
                        : rect.yMin - fromRect.yMax;
            if (along < -Overlap) continue;
            along = Mathf.Max(0f, along);
            // Gap between the two along the other axis (0 when they overlap there)
            float gap = direction.x != 0f
                ? Mathf.Max(0f, Mathf.Max(rect.yMin, fromRect.yMin) - Mathf.Min(rect.yMax, fromRect.yMax))
                : Mathf.Max(0f, Mathf.Max(rect.xMin, fromRect.xMin) - Mathf.Min(rect.xMax, fromRect.xMax));
            float score = along + gap * 3f;
            if (score >= bestScore) continue;
            bestScore = score;
            best = candidate;
        }
        _candidates.Clear();
        return best;
    }

    internal static VisualElement FirstFocusable(VisualElement root)
    {
        CollectFocusable(root, root);
        var first = _candidates.Count > 0 ? _candidates[0] : null;
        _candidates.Clear();
        return first;
    }

    // Text fields are typed into with the keyboard, not reached with a controller
    static void CollectFocusable(VisualElement element, VisualElement root)
    {
        if (!IsShownSelf(element) || element is TextField) return;
        if (element != root && element.focusable && element.enabledInHierarchy && element.worldBound.width > 0f) _candidates.Add(element);
        foreach (var child in element.Children()) CollectFocusable(child, root);
    }

    internal static bool IsInside(VisualElement element, VisualElement root)
    {
        for (var current = element; current != null; current = current.parent)
            if (current == root) return true;
        return false;
    }

    static bool IsShownSelf(VisualElement element) =>
        element.resolvedStyle.display != DisplayStyle.None && element.resolvedStyle.visibility != Visibility.Hidden;

    // Shown when it and everything up to root (or the panel) is
    internal static bool IsShown(VisualElement element, VisualElement root)
    {
        if (element.panel == null) return false;
        for (var current = element; current != null; current = current.parent)
        {
            if (!IsShownSelf(current)) return false;
            if (current == root) break;
        }
        return element.enabledInHierarchy && element.worldBound.width > 0f;
    }

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        foreach (var player in _players) player.Reset();
        _roots.Clear();
        _candidates.Clear();
    }
}
