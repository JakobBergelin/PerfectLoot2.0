using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The Options window in Options.uxml, opened from the main menu and the in-game menu (Open). One tab for now:
/// Controls, a row per action with its binding for each device (GameInput's binding groups) and a reset. Click a
/// binding, then press a key or button to bind it; Esc (or Start) cancels and Backspace clears it. While open, gameplay
/// input is off (the Player map is disabled, which rebinding needs too) and Esc closes it. Lives all session
/// (GameBootstrap's Services), above every other panel.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class OptionsHUD : MonoBehaviour
{
    const string HiddenClass = "op--hidden";
    const string UnboundClass = "op-binding--unbound";
    const string ListeningClass = "op-binding--listening";
    const string IdleHint = "Click a binding, then press a key or button. Esc cancels, Backspace clears.";
    const string KeyboardHint = "Press a key or mouse button. Esc cancels, Backspace clears.";
    const string GamepadHint = "Press a controller button. Esc or Start cancels, Backspace clears.";

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

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    bool _open;
    int _closedFrame = -1;
    VisualElement _root;
    ScrollView _list;
    Label _hint;
    readonly List<Row> _rows = MakeRows();

    InputActionRebindingExtensions.RebindingOperation _rebinding;
    Row _rebindingRow;
    string _rebindingGroup;

    /// <summary>True while the window is open, and on the frame it closed: that Esc was the window's.</summary>
    public static bool HandlesEscape => _instance != null && (_instance._open || _instance._closedFrame == Time.frameCount);

    public static void Open()
    {
        if (_instance == null)
        {
            Debug.LogWarning("OptionsHUD: there is no Options window. Is it in GameBootstrap's Services?");
            return;
        }
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
            new Row { Section = "Skills", Name = "Skill 1", Action = () => GameInput.Skill(0) },
            new Row { Section = "Skills", Name = "Skill 2", Action = () => GameInput.Skill(1) },
            new Row { Section = "Skills", Name = "Skill 3", Action = () => GameInput.Skill(2) },
            new Row { Section = "Skills", Name = "Skill 4", Action = () => GameInput.Skill(3) },
            new Row { Section = "Interface", Name = "Interact (enter portals)", Action = () => GameInput.Player.Interact },
            new Row { Section = "Interface", Name = "Inventory", Action = () => GameInput.Player.Inventory },
            new Row { Section = "Interface", Name = "Skills", Action = () => GameInput.Player.Skills },
            new Row { Section = "Interface", Name = "Item Database", Action = () => GameInput.Player.ItemDatabase },
            new Row { Section = "Interface", Name = "Statistics", Action = () => GameInput.Player.Statistics },
            new Row { Section = "Interface", Name = "Damage Meter", Action = () => GameInput.Player.DamageMeter },
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
        _list = root.Q<ScrollView>("controlsList");
        _hint = root.Q<Label>("controlsHint");
        var resetAll = root.Q<Button>("resetAllButton");
        var close = root.Q<Button>("closeButton");
        if (_root == null || _list == null || _hint == null || resetAll == null || close == null)
        {
            Debug.LogError("OptionsHUD: Options.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }

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

        // Esc, Start and Backspace never bind (GameInput.Rebind), so the window can use them
        if (_rebinding != null)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.backspaceKey.wasPressedThisFrame)
            {
                var row = _rebindingRow;
                var group = _rebindingGroup;
                _rebinding.Cancel();
                Clear(row, group);
            }
            else if (MenuPanels.EscapePressed) _rebinding.Cancel();
            return;
        }

        if (MenuPanels.EscapePressed) SetOpen(false);
    }

    void SetOpen(bool open)
    {
        if (open != _open)
        {
            if (!open)
            {
                _rebinding?.Cancel();
                _closedFrame = Time.frameCount;
            }
            _open = open;
            // Gameplay ignores input while it's open; the Player map is off so a key pressed to bind it does nothing else
            MenuPanels.SetBlocking(this, open);
            if (open) GameInput.Player.Disable();
            else GameInput.Player.Enable();
        }

        if (!_bound) return;
        _root.EnableInClassList(HiddenClass, !open);
        if (!open) return;
        _hint.text = IdleHint;
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
        var action = row.Action();
        int index = GameInput.BindingIndex(action, group, row.Part);
        if (index < 0) return;

        _rebindingRow = row;
        _rebindingGroup = group;
        // Unfocused, so the key pressed to bind it doesn't click it again
        (group == GameInput.Gamepad ? row.GamepadButton : row.KeyboardButton)?.Blur();
        _rebinding = GameInput.Rebind(action, index, group, () =>
        {
            _rebinding = null;
            _rebindingRow = null;
            _rebindingGroup = null;
            if (!_bound) return;
            _hint.text = IdleHint;
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
