using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Moving through menus with a controller. A menu that is open registers its element as a scope (SetScope); the newest
/// scope is the one the D-pad or left stick moves focus in, and A presses what has focus (a NavigationSubmitEvent, which
/// Buttons and Clickable elements answer). Closing a scope returns focus to where it was in the one below. B, LB/RB and
/// LT/RT are read by the menus themselves (MenuPanels). The focus outline and the controller-only hints show while a
/// controller was used last: roots passed to Register get a nav--gamepad class then. Run by UiNavigator.
/// UI Toolkit's own Navigate and Submit are switched off, so they never move focus outside the open menu.
/// </summary>
public static class UiNav
{
    const string GamepadClass = "nav--gamepad";
    const string StyleSheetPath = "UiNav";      // Resources/UiNav.uss
    const float StickPress = 0.5f;
    const float RepeatDelay = 0.35f;
    const float RepeatInterval = 0.1f;

    class Scope
    {
        public object Owner;
        public VisualElement Root;
        public VisualElement Remembered;    // Where focus was in it; it goes back there when it's on top again
    }

    static readonly List<Scope> _scopes = new List<Scope>();
    static readonly List<VisualElement> _roots = new List<VisualElement>();
    static readonly List<VisualElement> _candidates = new List<VisualElement>();
    static readonly HashSet<object> _suspended = new HashSet<object>();
    static readonly List<VisualElement> _pendingFirstOf = new List<VisualElement>();
    static int _pendingFirstFrame;
    static StyleSheet _styleSheet;
    static VisualElement _pendingFocus;
    static bool _shownGamepad;
    static Vector2 _heldDirection;
    static float _nextRepeat;

    /// <summary>True while a menu that a controller can move through is open: A, B and the sticks are for it.</summary>
    public static bool IsActive => _scopes.Count > 0;

    /// <summary>True while a controller is used and a menu has its input (gameplay leaves A and the sticks alone).</summary>
    public static bool HasGamepadInput => IsActive && GameInput.UsingGamepad;

    // ---------- Setup ----------

    /// <summary>
    /// Gives the root the shared styles (focus outline, hints, pages) and the nav--gamepad class while a controller is
    /// used. Call once per bound root.
    /// </summary>
    public static void Register(VisualElement root)
    {
        if (root == null) return;
        AddStyles(root);
        if (!_roots.Contains(root)) _roots.Add(root);
        root.EnableInClassList(GamepadClass, GameInput.UsingGamepad);
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
    /// Hover for both: the pointer entering or leaving, and (with a controller) focus arriving or leaving. The element
    /// becomes focusable, so it can be reached even when A does nothing on it.
    /// </summary>
    public static void Hover(VisualElement element, Action enter, Action leave)
    {
        element.focusable = true;
        element.RegisterCallback<PointerEnterEvent>(_ => enter());
        element.RegisterCallback<PointerLeaveEvent>(_ => leave());
        element.RegisterCallback<FocusInEvent>(_ => { if (GameInput.UsingGamepad) enter(); });
        element.RegisterCallback<FocusOutEvent>(_ => { if (GameInput.UsingGamepad) leave(); });
    }

    /// <summary>A line that only shows while a controller is used, e.g. "A Select · B Close".</summary>
    public static Label Hint(VisualElement parent, string text)
    {
        var hint = new Label(text) { pickingMode = PickingMode.Ignore };
        hint.AddToClassList("nav-hint");
        parent.Add(hint);
        return hint;
    }

    // ---------- Scopes ----------

    /// <summary>
    /// Opens or closes a menu for the controller. The newest open one gets the input; preferred (or else the first
    /// thing that can take focus) gets focus when it opens. Opening one that is already open only moves its root.
    /// </summary>
    public static void SetScope(object owner, VisualElement root, bool open, VisualElement preferred = null)
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
        // Focus left in the menu below would otherwise stay there
        if (_scopes.Count > 1 && root?.panel?.focusController?.focusedElement is VisualElement focused && !IsInside(focused, root))
            focused.Blur();
    }

    /// <summary>
    /// Stops moving focus and pressing with A while set, e.g. while Options waits for a button to bind (that button
    /// mustn't also press the focused one).
    /// </summary>
    public static void Suspend(object owner, bool suspended)
    {
        if (suspended) _suspended.Add(owner);
        else _suspended.Remove(owner);
    }

    /// <summary>Gives the element focus once it can take it (after layout), when a controller is used.</summary>
    public static void Focus(VisualElement element) => _pendingFocus = element;

    /// <summary>
    /// Focuses the first of these that can take focus (e.g. the first item of a new page), on the next frame once they
    /// are laid out.
    /// </summary>
    public static void FocusFirstOf(IEnumerable<VisualElement> elements)
    {
        _pendingFirstOf.Clear();
        _pendingFirstOf.AddRange(elements);
        _pendingFirstFrame = Time.frameCount;
    }

    static void ResolveFirstOf()
    {
        if (_pendingFirstOf.Count == 0 || Time.frameCount <= _pendingFirstFrame) return;
        foreach (var element in _pendingFirstOf)
        {
            var first = element.focusable && IsShown(element, null) ? element : IsShown(element, null) ? FirstFocusable(element) : null;
            if (first == null) continue;
            _pendingFocus = first;
            break;
        }
        _pendingFirstOf.Clear();
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
        bool gamepad = GameInput.UsingGamepad;
        if (gamepad != _shownGamepad || _roots.RemoveAll(root => root.panel == null) > 0)
        {
            _shownGamepad = gamepad;
            foreach (var root in _roots) root.EnableInClassList(GamepadClass, gamepad);
        }

        _scopes.RemoveAll(scope => scope.Root == null || scope.Root.panel == null);
        if (_scopes.Count == 0)
        {
            _pendingFocus = null;
            // Nothing is open: a button left focused would otherwise be pressed by A (Submit) during play
            BlurAll();
            return;
        }

        var top = _scopes[_scopes.Count - 1];
        var focusController = top.Root.panel.focusController;
        var focused = focusController.focusedElement as VisualElement;
        if (focused != null && IsInside(focused, top.Root) && IsShown(focused, top.Root)) top.Remembered = focused;
        if (!gamepad || _suspended.Count > 0) return;

        // Focus where it was asked for, else keep it inside the open menu
        ResolveFirstOf();
        if (_pendingFocus != null && IsInside(_pendingFocus, top.Root) && IsShown(_pendingFocus, top.Root) && _pendingFocus.canGrabFocus)
        {
            _pendingFocus.Focus();
            _pendingFocus = null;
            focused = focusController.focusedElement as VisualElement;
        }
        if (focused == null || !IsInside(focused, top.Root) || !IsShown(focused, top.Root) || IsTextField(focused))
        {
            var first = top.Remembered != null && IsShown(top.Remembered, top.Root) && top.Remembered != focused ? top.Remembered : FirstFocusable(top.Root);
            first?.Focus();
            // The press that brought focus here (e.g. the first one with a controller) only shows where it is
            _heldDirection = ReadDirection(GameInput.Players[0].GamepadDevice);
            _nextRepeat = Time.unscaledTime + RepeatDelay;
            return;
        }

        var gamepadDevice = GameInput.Players[0].GamepadDevice;
        if (gamepadDevice == null) return;
        var direction = ReadDirection(gamepadDevice);
        if (direction != Vector2.zero && (direction != _heldDirection || Time.unscaledTime >= _nextRepeat))
        {
            _nextRepeat = Time.unscaledTime + (direction != _heldDirection ? RepeatDelay : RepeatInterval);
            var next = FindNext(top.Root, focused, direction);
            next?.Focus();
        }
        _heldDirection = direction;

        if (gamepadDevice.buttonSouth.wasPressedThisFrame)
        {
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = focused;
            focused.SendEvent(submit);
        }
    }

    static void BlurAll()
    {
        foreach (var root in _roots)
        {
            if (root.panel?.focusController?.focusedElement is VisualElement focused && !IsTextField(focused))
            {
                focused.Blur();
                return;
            }
        }
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

    // The nearest element that can take focus in that direction, preferring ones in line with the current one. Only
    // elements wholly on that side count (a little overlap allowed: the focused one is scaled up), so a wide element
    // below two columns isn't "left" of the right one.
    static VisualElement FindNext(VisualElement root, VisualElement from, Vector2 direction)
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

    static VisualElement FirstFocusable(VisualElement root)
    {
        CollectFocusable(root, root);
        var first = _candidates.Count > 0 ? _candidates[0] : null;
        _candidates.Clear();
        return first;
    }

    static void CollectFocusable(VisualElement element, VisualElement root)
    {
        if (!IsShownSelf(element) || IsTextField(element)) return;
        if (element != root && element.focusable && element.enabledInHierarchy && element.worldBound.width > 0f) _candidates.Add(element);
        foreach (var child in element.Children()) CollectFocusable(child, root);
    }

    static bool IsTextField(VisualElement element) => element is TextField || element.GetFirstAncestorOfType<TextField>() != null;

    static bool IsInside(VisualElement element, VisualElement root)
    {
        for (var current = element; current != null; current = current.parent)
            if (current == root) return true;
        return false;
    }

    static bool IsShownSelf(VisualElement element) =>
        element.resolvedStyle.display != DisplayStyle.None && element.resolvedStyle.visibility != Visibility.Hidden;

    // Shown when it and everything up to root (or the panel) is
    static bool IsShown(VisualElement element, VisualElement root)
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
        _scopes.Clear();
        _roots.Clear();
        _candidates.Clear();
        _suspended.Clear();
        _pendingFirstOf.Clear();
        _pendingFocus = null;
        _shownGamepad = false;
        _heldDirection = Vector2.zero;
    }
}

