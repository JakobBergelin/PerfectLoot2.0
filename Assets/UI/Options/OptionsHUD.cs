using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The Options window in Options.uxml, opened from the main menu and the in-game menu (Open). One tab for now:
/// Controls, a row per action with its binding for each device (GameInput's binding groups) and a reset. Click a
/// binding (A with a controller), then press a key or button to bind it; Esc or Start cancels, and Backspace or View
/// clears it. While open, gameplay input is off (the Player map is disabled, which rebinding needs too) and Esc, Start or
/// B closes it. The rows are in pages (LT/RT). Lives all session (GameBootstrap's Services), above every other panel.
/// One for both players: whoever opens it has it (their buttons and focus) and only their gameplay stops; the bindings
/// are everyone's.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class OptionsHUD : MonoBehaviour
{
    const string HiddenClass = "op--hidden";
    const string UnboundClass = "op-binding--unbound";
    const string ListeningClass = "op-binding--listening";
    const string IdleHint = "Click a binding, then press a key or button. Esc cancels, Backspace clears.";
    const string KeyboardHint = "Press a key or mouse button. Esc cancels, Backspace clears.";
    const string GamepadHint = "Press a controller button. Start cancels, View clears.";
    const string GamepadIdleHint = "A Change · LT/RT Pages · B Close";
    const int RowsPerPage = 20;

    // A row of the Controls tab. Part is a composite part (Move's keys); a row without a column shows a dash there
    class Row
    {
        public string Section;
        public string Name;
        public Func<InputAction> Action;
        public string Part;
        public bool HasKeyboard = true;
        public bool HasGamepad = true;
        public Button KeyboardButton;
        public Button GamepadButton;
    }

    static OptionsHUD _instance;

    // The player who has it open (or had it last)
    int _owner;
    PlayerMenus Menus => MenuPanels.For(_owner);
    PlayerNav Nav => UiNav.For(_owner);
    GameInput.PlayerControls Controls => GameInput.For(_owner) ?? GameInput.Players[0];

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    bool _open;
    int _closedFrame = -1;
    VisualElement _root;
    VisualElement _list;
    VisualElement _panel;
    Pager _pager;
    Label _hint;
    readonly List<Row> _rows = MakeRows();

    InputActionRebindingExtensions.RebindingOperation _rebinding;
    Row _rebindingRow;
    string _rebindingGroup;
    Row _pendingRow;            // A controller rebind waits for A (that pressed the binding) to be let go first

    /// <summary>True while player 1 has the window open, and on the frame it closed: that Esc was the window's.</summary>
    public static bool HandlesEscape => HandlesEscapeFor(0);

    /// <summary>True while this player has the window open, and on the frame it closed: that Esc was the window's.</summary>
    public static bool HandlesEscapeFor(int playerIndex) =>
        _instance != null && _instance._owner == playerIndex && (_instance._open || _instance._closedFrame == Time.frameCount);

    /// <summary>Opens it for this player (player 1 by default), unless the other player has it open.</summary>
    public static void Open(int playerIndex = 0)
    {
        if (_instance == null)
        {
            Debug.LogWarning("OptionsHUD: there is no Options window. Is it in GameBootstrap's Services?");
            return;
        }
        if (_instance._open) return;
        _instance._owner = playerIndex;
        _instance.SetOpen(true);
    }

    static List<Row> MakeRows()
    {
        Func<InputAction> move = () => GameInput.Player.Move;
        return new List<Row>
        {
            new Row { Section = "Movement", Name = "Move up", Action = move, Part = "up", HasGamepad = false },
            new Row { Section = "Movement", Name = "Move down", Action = move, Part = "down", HasGamepad = false },
            new Row { Section = "Movement", Name = "Move left", Action = move, Part = "left", HasGamepad = false },
            new Row { Section = "Movement", Name = "Move right", Action = move, Part = "right", HasGamepad = false },
            new Row { Section = "Movement", Name = "Move (stick)", Action = move, HasKeyboard = false },
            new Row { Section = "Movement", Name = "Aim (stick)", Action = () => GameInput.Player.Aim, HasKeyboard = false },
            new Row { Section = "Skills", Name = "Skill 1", Action = () => GameInput.Skill(0) },
            new Row { Section = "Skills", Name = "Skill 2", Action = () => GameInput.Skill(1) },
            new Row { Section = "Skills", Name = "Skill 3", Action = () => GameInput.Skill(2) },
            new Row { Section = "Skills", Name = "Skill 4", Action = () => GameInput.Skill(3) },
            new Row { Section = "Interface", Name = "Interact (portals, loot)", Action = () => GameInput.Player.Interact },
            new Row { Section = "Interface", Name = "Inventory", Action = () => GameInput.Player.Inventory },
            new Row { Section = "Interface", Name = "Skills", Action = () => GameInput.Player.Skills },
            new Row { Section = "Interface", Name = "Item Database", Action = () => GameInput.Player.ItemDatabase },
            new Row { Section = "Interface", Name = "Statistics", Action = () => GameInput.Player.Statistics },
            new Row { Section = "Interface", Name = "Damage Meter", Action = () => GameInput.Player.DamageMeter },
            new Row { Section = "Interface", Name = "Open panels", Action = () => GameInput.Player.Panels, HasKeyboard = false },
        };
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("OptionsHUD: there is already an Options window, so this one is removed.", this);
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDisable()
    {
        SetOpen(false);
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        _bound = false;
        _boundVersion = -1;
    }

    void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        if (_bound && version == _boundVersion) return;

        // The Panel Renderer's own container isn't part of the UXML; make it fill the screen without taking clicks
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        root.pickingMode = PickingMode.Ignore;

        _rebinding?.Cancel();
        _bound = Bind(root);
        _boundVersion = version;
        if (_bound) SetOpen(_open);
    }

    bool Bind(VisualElement root)
    {
        _root = root.Q<VisualElement>("optionsRoot");
        _list = root.Q<VisualElement>("controlsList");
        _panel = root.Q<VisualElement>(className: "op-panel");
        _hint = root.Q<Label>("controlsHint");
        var resetAll = root.Q<Button>("resetAllButton");
        var close = root.Q<Button>("closeButton");
        if (_root == null || _list == null || _panel == null || _hint == null || resetAll == null || close == null)
        {
            Debug.LogError("OptionsHUD: Options.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }

        UiNav.Register(_root);
        _pager = Pager.After(_list);
        resetAll.clicked += ResetAll;
        close.clicked += () => SetOpen(false);
        // A mouse button pressed to bind it mustn't also click what is under the pointer
        _root.RegisterCallback<PointerDownEvent>(e =>
        {
            if (_rebinding != null && _rebindingGroup == GameInput.KeyboardMouse) e.StopImmediatePropagation();
        }, TrickleDown.TrickleDown);
        BuildRows();
        return true;
    }

    void Update()
    {
        if (!_bound || !_open) return;

        // A controller rebind starts once A, which chose the binding, is let go (else it would bind A)
        if (_pendingRow != null)
        {
            var gamepad = Controls.GamepadDevice;
            if (gamepad == null || !gamepad.buttonSouth.isPressed)
            {
                var row = _pendingRow;
                _pendingRow = null;
                BeginRebind(row, GameInput.Gamepad);
            }
            return;
        }

        // Esc, Start, Backspace and View never bind (GameInput.Rebind), so the window can use them
        if (_rebinding != null)
        {
            var keyboard = Controls.KeyboardDevice;
            var gamepad = Controls.GamepadDevice;
            if ((keyboard != null && keyboard.backspaceKey.wasPressedThisFrame) || (gamepad != null && gamepad.selectButton.wasPressedThisFrame))
            {
                var row = _rebindingRow;
                var group = _rebindingGroup;
                _rebinding.Cancel();
                Clear(row, group);
            }
            else if (Menus.EscapePressed) _rebinding.Cancel();
            return;
        }

        if (Menus.BackPressed) SetOpen(false);
        else if (Menus.PagePressed != 0) _pager.Turn(Menus.PagePressed);
        _hint.text = Controls.UsingGamepad ? GamepadIdleHint : IdleHint;
    }

    void SetOpen(bool open)
    {
        if (open != _open)
        {
            if (!open)
            {
                _rebinding?.Cancel();
                _pendingRow = null;
                Nav.Suspend(this, false);
                _closedFrame = Time.frameCount;
            }
            _open = open;
            // The owner's gameplay ignores input while it's open; their Player map is off so a key pressed to bind it does
            // nothing else
            Menus.SetBlocking(this, open);
            if (open) Controls.Actions.Player.Disable();
            else Controls.Actions.Player.Enable();
        }

        if (!_bound) return;
        if (open) Nav.Register(_root);
        _root.EnableInClassList(HiddenClass, !open);
        Nav.SetScope(this, _panel, open);
        if (!open) return;
        _hint.text = Controls.UsingGamepad ? GamepadIdleHint : IdleHint;
        RefreshRows();
    }

    // ---------- Controls ----------

    void BuildRows()
    {
        _list.Clear();
        string section = null;
        foreach (var row in _rows)
        {
            if (row.Section != section)
            {
                var heading = new Label(row.Section);
                heading.AddToClassList("op-section");
                if (section == null) heading.AddToClassList("op-section--first");
                _list.Add(heading);
                section = row.Section;
            }

            var element = new VisualElement();
            element.AddToClassList("op-row");
            var name = new Label(row.Name);
            name.AddToClassList("op-action");
            element.Add(name);
            row.KeyboardButton = AddCell(element, row, row.HasKeyboard, GameInput.KeyboardMouse);
            row.GamepadButton = AddCell(element, row, row.HasGamepad, GameInput.Gamepad);
            var reset = new Button(() => Reset(row)) { text = "Reset" };
            reset.AddToClassList("op-reset");
            element.Add(reset);
            _list.Add(element);
        }
        _pager.Reset();
        _pager.Show(_list.Children(), RowsPerPage);
    }

    Button AddCell(VisualElement element, Row row, bool has, string group)
    {
        if (!has)
        {
            var none = new Label("-");
            none.AddToClassList("op-binding--none");
            element.Add(none);
            return null;
        }
        var button = new Button(() => StartRebind(row, group));
        button.AddToClassList("op-binding");
        element.Add(button);
        return button;
    }

    void RefreshRows()
    {
        foreach (var row in _rows)
        {
            RefreshCell(row, row.KeyboardButton, GameInput.KeyboardMouse);
            RefreshCell(row, row.GamepadButton, GameInput.Gamepad);
        }
    }

    void RefreshCell(Row row, Button button, string group)
    {
        if (button == null) return;
        bool listening = _rebinding != null && _rebindingRow == row && _rebindingGroup == group;
        string label = GameInput.Label(row.Action(), group, row.Part);
        button.text = listening ? "Press..." : string.IsNullOrEmpty(label) ? "Unbound" : label;
        button.EnableInClassList(ListeningClass, listening);
        button.EnableInClassList(UnboundClass, !listening && string.IsNullOrEmpty(label));
    }

    void StartRebind(Row row, string group)
    {
        _rebinding?.Cancel();
        // Pressed with A: wait for it to be let go (Update)
        var gamepad = Controls.GamepadDevice;
        if (group == GameInput.Gamepad && gamepad != null && gamepad.buttonSouth.isPressed)
        {
            _pendingRow = row;
            Nav.Suspend(this, true);
            _hint.text = GamepadHint;
            return;
        }
        BeginRebind(row, group);
    }

    void BeginRebind(Row row, string group)
    {
        var action = row.Action();
        int index = GameInput.BindingIndex(action, group, row.Part);
        if (index < 0)
        {
            Nav.Suspend(this, false);
            return;
        }

        _rebindingRow = row;
        _rebindingGroup = group;
        // Menu navigation waits, so the button pressed to bind it doesn't also press the focused one
        Nav.Suspend(this, true);
        // Unfocused, so the key pressed to bind it doesn't click it again
        if (!Controls.UsingGamepad) (group == GameInput.Gamepad ? row.GamepadButton : row.KeyboardButton)?.Blur();
        _rebinding = GameInput.Rebind(action, index, group, () =>
        {
            _rebinding = null;
            _rebindingRow = null;
            _rebindingGroup = null;
            Nav.Suspend(this, false);
            if (!_bound) return;
            _hint.text = Controls.UsingGamepad ? GamepadIdleHint : IdleHint;
            RefreshRows();
        });
        _hint.text = group == GameInput.Gamepad ? GamepadHint : KeyboardHint;
        RefreshRows();
    }

    void Clear(Row row, string group)
    {
        if (row == null) return;
        var action = row.Action();
        int index = GameInput.BindingIndex(action, group, row.Part);
        if (index >= 0) GameInput.Clear(action, index);
        RefreshRows();
    }

    void Reset(Row row)
    {
        _rebinding?.Cancel();
        var action = row.Action();
        if (row.HasKeyboard) ResetBinding(action, GameInput.KeyboardMouse, row.Part);
        if (row.HasGamepad) ResetBinding(action, GameInput.Gamepad, row.Part);
        RefreshRows();
    }

    static void ResetBinding(InputAction action, string group, string part)
    {
        int index = GameInput.BindingIndex(action, group, part);
        if (index >= 0) GameInput.Reset(action, index);
    }

    void ResetAll()
    {
        _rebinding?.Cancel();
        GameInput.ResetAll();
        RefreshRows();
    }
}
