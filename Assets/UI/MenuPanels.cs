using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// One local player's menus: which of their menu panels (character, skills, ...) is open (one at a time: opening one
/// closes their others), popups that block their gameplay input without closing panels, and their menu buttons (Esc,
/// Start, B, LB/RB, LT/RT, View) read from their own devices only. Gameplay reads IsAnyOpen to ignore their skill
/// input while a menu has it. MenuPanels.For(index) gives a player's.
/// </summary>
public sealed class PlayerMenus
{
    public readonly int PlayerIndex;

    object _open;
    readonly HashSet<object> _blockers = new HashSet<object>();
    int _changedFrame = -1;

    internal PlayerMenus(int playerIndex) => PlayerIndex = playerIndex;

    GameInput.PlayerControls Controls => GameInput.For(PlayerIndex);
    // Their gamepad and keyboard (any, while player 1 plays alone); none while they aren't joined
    Gamepad MenuGamepad => Controls?.GamepadDevice;
    Keyboard MenuKeyboard => Controls?.KeyboardDevice;

    /// <summary>True while they have the keyboard and a text field has it: their keys are for the text.</summary>
    public bool IsTyping => MenuPanels.IsTyping && MenuKeyboard != null;

    /// <summary>True while one of their menu panels is open, a popup blocks their input, or they're typing.</summary>
    public bool IsAnyOpen => _open != null || _blockers.Count > 0 || IsTyping;

    /// <summary>True while one of their menu panels is open (popups that only block input don't count).</summary>
    public bool IsPanelOpen => _open != null;

    /// <summary>Raised with their panel that is now open, or null when all are closed.</summary>
    public event Action<object> OpenChanged;

    public void SetOpen(object panel, bool open)
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

    /// <summary>Blocks their gameplay input while set, without counting as an open panel (other panels stay open).</summary>
    public void SetBlocking(object owner, bool blocking)
    {
        if (blocking ? _blockers.Add(owner) : _blockers.Remove(owner)) _changedFrame = Time.frameCount;
    }

    /// <summary>
    /// True on a frame where one of their panels opened or closed, a popup started or stopped blocking them, or their
    /// typing started or stopped. Esc closes one thing per press: what reacts to Esc after something else (the in-game
    /// menu) checks this first.
    /// </summary>
    public bool ChangedThisFrame => _changedFrame == Time.frameCount || (MenuPanels.TypingChangedThisFrame && MenuKeyboard != null);

    /// <summary>
    /// Esc, or Start on their gamepad, this frame: they always close what is open or open the in-game menu. Not the
    /// Start of a free controller where another player can join: that one joins (GameInput.JoinPressed).
    /// </summary>
    public bool EscapePressed =>
        (MenuKeyboard != null && MenuKeyboard.escapeKey.wasPressedThisFrame) ||
        (MenuGamepad != null && MenuGamepad.startButton.wasPressedThisFrame && GameInput.JoinPressed() != MenuGamepad);

    /// <summary>
    /// Esc, Start, or B on their gamepad, this frame: closes what is open, one thing per press. Unlike EscapePressed it
    /// never opens the in-game menu, so B stays free during play.
    /// </summary>
    public bool BackPressed => EscapePressed || (MenuGamepad != null && MenuGamepad.buttonEast.wasPressedThisFrame);

    /// <summary>
    /// A panel's own toggle (their Inventory, Skills, ... action) pressed this frame. Not while they type, and not from a
    /// controller while a menu has it: its D-pad (where players may bind panels) moves focus there, and B, View or LB/RB
    /// close or switch.
    /// </summary>
    public bool TogglePressed(InputAction action) =>
        action != null && action.WasPressedThisFrame() && !IsTyping &&
        !(UiNav.For(PlayerIndex).HasGamepadInput && action.activeControl?.device is Gamepad);

    /// <summary>-1 for LT, +1 for RT, else 0 this frame: turns the page of their open panel's list.</summary>
    public int PagePressed => MenuGamepad == null ? 0
        : MenuGamepad.leftTrigger.wasPressedThisFrame ? -1 : MenuGamepad.rightTrigger.wasPressedThisFrame ? 1 : 0;

    // ---------- Panel tabs ----------

    // The panels their Panels action (View) opens and LB/RB go through, in order
    struct Tab
    {
        public object Panel;
        public int Order;
        public Action<bool> SetOpen;
    }

    readonly List<Tab> _tabs = new List<Tab>();
    object _lastTab;

    /// <summary>Adds a panel to the ones View opens and LB/RB switch between, at its place in the order.</summary>
    public void AddTab(object panel, int order, Action<bool> setOpen)
    {
        RemoveTab(panel);
        int index = _tabs.FindIndex(tab => tab.Order > order);
        var added = new Tab { Panel = panel, Order = order, SetOpen = setOpen };
        if (index < 0) _tabs.Add(added);
        else _tabs.Insert(index, added);
    }

    public void RemoveTab(object panel)
    {
        _tabs.RemoveAll(tab => ReferenceEquals(tab.Panel, panel));
        if (ReferenceEquals(_lastTab, panel)) _lastTab = null;
    }

    /// <summary>
    /// Run once a frame (UiNavigator): View opens the panel used last (the first one at first) or closes the open one; LB
    /// and RB open the one before or after it.
    /// </summary>
    internal void UpdateTabs()
    {
        var controls = Controls;
        if (_tabs.Count == 0 || IsTyping || controls == null) return;
        int open = _tabs.FindIndex(tab => ReferenceEquals(tab.Panel, _open));
        if (controls.Actions.Player.Panels.WasPressedThisFrame())
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

    internal void Reset()
    {
        _open = null;
        _blockers.Clear();
        _changedFrame = -1;
        OpenChanged = null;
        _tabs.Clear();
        _lastTab = null;
    }
}

/// <summary>
/// The local players' menus (PlayerMenus, For(index)) and the keyboard's typing. The static members without a player
/// are player 1's (the main menu and what isn't per player yet), except SetBlocking: hovering a button with the mouse
/// blocks the mouse's player.
/// </summary>
public static class MenuPanels
{
    static readonly PlayerMenus[] _players = { new PlayerMenus(0), new PlayerMenus(1) };
    static readonly HashSet<object> _typing = new HashSet<object>();
    static int _typingFrame = -1;

    /// <summary>A local player's menus (0 is player 1).</summary>
    public static PlayerMenus For(int playerIndex) => _players[Mathf.Clamp(playerIndex, 0, _players.Length - 1)];

    /// <summary>The menus of the player who has the mouse (player 1 when nobody has it, both playing with controllers).</summary>
    public static PlayerMenus ForMouse => For(GameInput.MouseOwner);

    // ---------- Typing (one keyboard: its player's keys are for the text) ----------

    /// <summary>True while a UI text field has keyboard focus.</summary>
    public static bool IsTyping => _typing.Count > 0;

    internal static bool TypingChangedThisFrame => _typingFrame == Time.frameCount;

    public static void SetTyping(object owner, bool typing)
    {
        if (typing ? _typing.Add(owner) : _typing.Remove(owner)) _typingFrame = Time.frameCount;
    }

    /// <summary>Sets IsTyping while this field has keyboard focus.</summary>
    public static void TrackTyping(TextField field)
    {
        field.RegisterCallback<FocusInEvent>(_ => SetTyping(field, true));
        field.RegisterCallback<FocusOutEvent>(_ => SetTyping(field, false));
        // A field removed from the panel (UI reload) never gets FocusOut
        field.RegisterCallback<DetachFromPanelEvent>(_ => SetTyping(field, false));
    }

    // ---------- Player 1's ----------

    public static bool IsAnyOpen => For(0).IsAnyOpen;
    public static bool IsPanelOpen => For(0).IsPanelOpen;
    public static bool ChangedThisFrame => For(0).ChangedThisFrame;
    public static bool EscapePressed => For(0).EscapePressed;
    public static bool BackPressed => For(0).BackPressed;
    public static int PagePressed => For(0).PagePressed;

    /// <summary>Blocks the mouse's player (ForMouse) while set: for popups and buttons the pointer is over.</summary>
    public static void SetBlocking(object owner, bool blocking)
    {
        foreach (var player in _players) player.SetBlocking(owner, false);
        if (blocking) ForMouse.SetBlocking(owner, true);
    }

    /// <summary>Each player's View and LB/RB, once a frame (UiNavigator).</summary>
    internal static void UpdateTabs()
    {
        foreach (var player in _players) player.UpdateTabs();
    }

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        foreach (var player in _players) player.Reset();
        _typing.Clear();
        _typingFrame = -1;
    }
}
