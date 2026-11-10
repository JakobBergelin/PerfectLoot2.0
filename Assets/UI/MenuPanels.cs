using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Tracks which menu panel (character, skills, ...) is open. Only one is open at a time: opening one closes the
/// others. Popups that need the mouse (e.g. the drop popup) block gameplay input without closing panels.
/// Gameplay reads IsAnyOpen to ignore skill input while the mouse is used for the menu.
/// </summary>
public static class MenuPanels
{
    static object _open;
    static readonly HashSet<object> _blockers = new HashSet<object>();

    static readonly HashSet<object> _typing = new HashSet<object>();
    static int _changedFrame = -1;

    /// <summary>True while a menu panel is open, a popup blocks gameplay input, or a text field has keyboard focus.</summary>
    public static bool IsAnyOpen => _open != null || _blockers.Count > 0 || _typing.Count > 0;

    /// <summary>True while a UI text field has keyboard focus: hotkeys and movement keys are for the text.</summary>
    public static bool IsTyping => _typing.Count > 0;

    public static void SetTyping(object owner, bool typing)
    {
        if (typing ? _typing.Add(owner) : _typing.Remove(owner)) _changedFrame = Time.frameCount;
    }

    /// <summary>Sets IsTyping while this field has keyboard focus.</summary>
    public static void TrackTyping(TextField field)
    {
        field.RegisterCallback<FocusInEvent>(_ => SetTyping(field, true));
        field.RegisterCallback<FocusOutEvent>(_ => SetTyping(field, false));
        // A field removed from the panel (UI reload) never gets FocusOut
        field.RegisterCallback<DetachFromPanelEvent>(_ => SetTyping(field, false));
    }

    /// <summary>True while a menu panel is open (popups that only block input don't count).</summary>
    public static bool IsPanelOpen => _open != null;

    /// <summary>Raised with the panel that is now open, or null when all are closed.</summary>
    public static event Action<object> OpenChanged;

    public static void SetOpen(object panel, bool open)
    {
        if (open)
        {
            if (_open == panel) return;
            _open = panel;
            if (_tabs.Exists(tab => ReferenceEquals(tab.Panel, panel))) _lastTab = panel;
            _changedFrame = Time.frameCount;
            OpenChanged?.Invoke(_open);
        }
        else if (_open == panel)
        {
            _open = null;
            _changedFrame = Time.frameCount;
            OpenChanged?.Invoke(null);
        }
    }

    /// <summary>Blocks gameplay input while set, without counting as an open panel (other panels stay open).</summary>
    public static void SetBlocking(object owner, bool blocking)
    {
        if (blocking ? _blockers.Add(owner) : _blockers.Remove(owner)) _changedFrame = Time.frameCount;
    }

    /// <summary>
    /// True on a frame where a panel opened or closed, a popup started or stopped blocking, or typing started or stopped.
    /// Esc closes one thing per press: what reacts to Esc after something else (the in-game menu) checks this first.
    /// </summary>
    public static bool ChangedThisFrame => _changedFrame == Time.frameCount;

    // The menus are player 1's for now: their gamepad and keyboard (any, while they play alone)
    static Gamepad MenuGamepad => GameInput.Players[0].GamepadDevice;
    static Keyboard MenuKeyboard => GameInput.Players[0].KeyboardDevice;

    /// <summary>Esc, or Start on a gamepad, this frame: they always close what is open or open the in-game menu.</summary>
    public static bool EscapePressed =>
        (MenuKeyboard != null && MenuKeyboard.escapeKey.wasPressedThisFrame) ||
        (MenuGamepad != null && MenuGamepad.startButton.wasPressedThisFrame);

    /// <summary>
    /// Esc, Start, or B on a gamepad, this frame: closes what is open, one thing per press. Unlike EscapePressed it never
    /// opens the in-game menu, so B stays free during play.
    /// </summary>
    public static bool BackPressed =>
        EscapePressed || (MenuGamepad != null && MenuGamepad.buttonEast.wasPressedThisFrame);

    /// <summary>
    /// A panel's own toggle (Inventory, Skills, ...) pressed this frame. Not while typing, and not from a controller while
    /// a menu has it: its D-pad (where players may bind panels) moves focus there, and B, View or LB/RB close or switch.
    /// </summary>
    public static bool TogglePressed(InputAction action) =>
        action != null && action.WasPressedThisFrame() && !IsTyping && !(UiNav.HasGamepadInput && action.activeControl?.device is Gamepad);

    /// <summary>-1 for LT, +1 for RT, else 0 this frame: turns the page of the open panel's list.</summary>
    public static int PagePressed => MenuGamepad == null ? 0
        : MenuGamepad.leftTrigger.wasPressedThisFrame ? -1 : MenuGamepad.rightTrigger.wasPressedThisFrame ? 1 : 0;

    // ---------- Panel tabs ----------

    // The panels the Panels action (View) opens and LB/RB go through, in order
    struct Tab
    {
        public object Panel;
        public int Order;
        public Action<bool> SetOpen;
    }

    static readonly List<Tab> _tabs = new List<Tab>();
    static object _lastTab;

    /// <summary>Adds a panel to the ones View opens and LB/RB switch between, at its place in the order.</summary>
    public static void AddTab(object panel, int order, Action<bool> setOpen)
    {
        RemoveTab(panel);
        int index = _tabs.FindIndex(tab => tab.Order > order);
        var added = new Tab { Panel = panel, Order = order, SetOpen = setOpen };
        if (index < 0) _tabs.Add(added);
        else _tabs.Insert(index, added);
    }

    public static void RemoveTab(object panel)
    {
        _tabs.RemoveAll(tab => ReferenceEquals(tab.Panel, panel));
        if (ReferenceEquals(_lastTab, panel)) _lastTab = null;
    }

    /// <summary>
    /// Run once a frame (UiNavigator): View opens the panel used last (the first one at first) or closes the open one; LB
    /// and RB open the one before or after it.
    /// </summary>
    public static void UpdateTabs()
    {
        if (_tabs.Count == 0 || IsTyping) return;
        int open = _tabs.FindIndex(tab => ReferenceEquals(tab.Panel, _open));
        if (GameInput.Player.Panels.WasPressedThisFrame())
        {
            if (open >= 0) _tabs[open].SetOpen(false);
            else
            {
                int last = _tabs.FindIndex(tab => ReferenceEquals(tab.Panel, _lastTab));
                _tabs[Mathf.Max(0, last)].SetOpen(true);
            }
            return;
        }

        var gamepad = MenuGamepad;
        if (open < 0 || gamepad == null) return;
        int step = gamepad.leftShoulder.wasPressedThisFrame ? -1 : gamepad.rightShoulder.wasPressedThisFrame ? 1 : 0;
        if (step != 0) _tabs[(open + step + _tabs.Count) % _tabs.Count].SetOpen(true);
    }

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _open = null;
        _blockers.Clear();
        _typing.Clear();
        _changedFrame = -1;
        OpenChanged = null;
        _tabs.Clear();
        _lastTab = null;
    }
}
