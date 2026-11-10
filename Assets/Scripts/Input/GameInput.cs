using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

/// <summary>
/// The game's input: a copy of the input actions per local player (PlayerControls; at most MaxPlayers), all with the
/// same bindings (saved per machine in PlayerPrefs). Player 1 alone uses any device and the labels follow the one used
/// last. When player 2 joins (Join) each player only gets their own devices: player 2 the one they joined with
/// (keyboard and mouse go together), player 1 the one they used last. The static members without a player (Actions,
/// Player, Skill, UsingGamepad, Label, ...) are player 1's. Each action has one binding per binding group:
/// Keyboard&amp;Mouse and Gamepad. Escape and the gamepad's Start button aren't actions: they always open and close menus
/// (PlayerMenus.EscapePressed). Also does the rebinding for the Controls window.
/// </summary>
public static class GameInput
{
    public const int MaxPlayers = 2;
    public const string KeyboardMouse = "Keyboard&Mouse";
    public const string Gamepad = "Gamepad";
    const string PrefsKey = "Controls.Bindings";
    const float GamepadSwitchMagnitude = 0.5f;  // How far a controller's stick, trigger or button must be pushed to count as using it
    const float PointerMoveSquared = 9f;        // The mouse counts as used once it moved more than 3 pixels

    /// <summary>One local player's input: their copy of the actions and the devices they use.</summary>
    public sealed class PlayerControls
    {
        public readonly int Index;
        public readonly InputSystem_Actions Actions;
        public readonly InputAction[] Skills;
        /// <summary>Their devices; empty for player 1 alone (any device).</summary>
        public readonly List<InputDevice> Devices = new List<InputDevice>();

        /// <summary>True when the device they used last is a gamepad.</summary>
        public bool UsingGamepad { get; internal set; }
        internal UnityEngine.InputSystem.Gamepad LastGamepad;
        internal Vector2 LastPointer;

        internal PlayerControls(int index)
        {
            Index = index;
            Actions = new InputSystem_Actions();
            Skills = new[] { Actions.Player.Skill1, Actions.Player.Skill2, Actions.Player.Skill3, Actions.Player.Skill4 };
            LoadBindings(Actions);
        }

        /// <summary>The binding group of the device they used last.</summary>
        public string Group => UsingGamepad ? GameInput.Gamepad : KeyboardMouse;

        /// <summary>Their gamepad: the one they joined with, or for player 1 alone the one used last (any, if none yet).</summary>
        public UnityEngine.InputSystem.Gamepad GamepadDevice
        {
            get
            {
                if (Devices.Count == 0) return LastGamepad != null && LastGamepad.added ? LastGamepad : UnityEngine.InputSystem.Gamepad.current;
                foreach (var device in Devices)
                    if (device is UnityEngine.InputSystem.Gamepad gamepad) return gamepad;
                return null;
            }
        }

        /// <summary>Their keyboard, or null when it's the other player's.</summary>
        public Keyboard KeyboardDevice => Devices.Count == 0 || Devices.Contains(Keyboard.current) ? Keyboard.current : null;

        /// <summary>The key label for the device they used last.</summary>
        public string Label(InputAction action) => GameInput.Label(action, Group);

        internal void UseDevices(IReadOnlyList<InputDevice> devices)
        {
            Devices.Clear();
            if (devices != null)
                foreach (var device in devices)
                    if (device != null) Devices.Add(device);
            Actions.devices = Devices.Count > 0 ? new ReadOnlyArray<InputDevice>(Devices.ToArray()) : null;
            UsingGamepad = Devices.Count > 0 ? Devices[0] is UnityEngine.InputSystem.Gamepad : UsingGamepad;
        }
    }

    static readonly List<PlayerControls> _players = new List<PlayerControls>();
    static bool _listening;

    /// <summary>The local players' input, player 1 first (made on first use).</summary>
    public static IReadOnlyList<PlayerControls> Players
    {
        get
        {
            if (_players.Count == 0) _players.Add(new PlayerControls(0));
            return _players;
        }
    }

    /// <summary>A local player's input, or null when that player hasn't joined.</summary>
    public static PlayerControls For(int index) => index >= 0 && index < Players.Count ? Players[index] : null;

    public static int PlayerCount => Players.Count;

    /// <summary>The index of the player who has the mouse (the one who joined with the keyboard); 0 when nobody has it.</summary>
    public static int MouseOwner
    {
        get
        {
            foreach (var player in Players)
                if (player.Devices.Count == 0 || player.Devices.Contains(Mouse.current)) return player.Index;
            return 0;
        }
    }

    // ---------- Player 1 (the static members the single-player game reads) ----------

    public static InputSystem_Actions Actions => Players[0].Actions;
    public static InputSystem_Actions.PlayerActions Player => Actions.Player;

    /// <summary>Skill1 to Skill4, by slot index.</summary>
    public static InputAction Skill(int index) => Players[0].Skills[index];

    /// <summary>True when the device player 1 used last is a gamepad.</summary>
    public static bool UsingGamepad => Players[0].UsingGamepad;

    /// <summary>The binding group of the device player 1 used last.</summary>
    public static string Group => Players[0].Group;

    /// <summary>Goes up whenever a binding, a player's last device or the players change, so key labels know to refresh.</summary>
    public static int Version { get; private set; }

    // ---------- Joining ----------

    static readonly HashSet<object> _joinScreens = new HashSet<object>();

    /// <summary>True while a screen lets another player join (Character Select, the Hub) and there's room for one.</summary>
    public static bool CanJoin => _joinScreens.Count > 0 && Players.Count < MaxPlayers;

    /// <summary>Set by a screen while another player may join from it.</summary>
    public static void AllowJoining(object screen, bool allowed)
    {
        if (allowed) _joinScreens.Add(screen);
        else _joinScreens.Remove(screen);
    }

    /// <summary>
    /// The free device whose join button (Start, or Enter on a keyboard) went down this frame while joining is allowed,
    /// else null. The menus ignore that Start (PlayerMenus.EscapePressed).
    /// </summary>
    public static InputDevice JoinPressed()
    {
        if (!CanJoin) return null;
        foreach (var device in InputSystem.devices)
        {
            if (!IsFree(device)) continue;
            if (device is UnityEngine.InputSystem.Gamepad gamepad && gamepad.startButton.wasPressedThisFrame) return device;
            if (device is Keyboard keyboard && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) return device;
        }
        return null;
    }

    /// <summary>
    /// A device nobody uses that could join as another player (a gamepad, or the keyboard), or null. Player 1 alone
    /// counts as using the device they used last.
    /// </summary>
    public static InputDevice FreeDevice()
    {
        foreach (var device in InputSystem.devices)
            if (IsFree(device)) return device;
        return null;
    }

    /// <summary>True for a gamepad or keyboard nobody uses, while another player can still join.</summary>
    public static bool IsFree(InputDevice device)
    {
        if (device == null || !device.added || !(device is UnityEngine.InputSystem.Gamepad || device is Keyboard)) return false;
        if (Players.Count >= MaxPlayers) return false;
        var player1 = Players[0];
        return player1.UsingGamepad ? device != player1.GamepadDevice : device is not Keyboard;
    }

    /// <summary>
    /// Adds a player using this device (with the mouse, for the keyboard); player 1 keeps the device they used last.
    /// Returns the new player's input, or null when nobody more can join.
    /// </summary>
    public static PlayerControls Join(InputDevice device)
    {
        if (device == null || Players.Count >= MaxPlayers) return null;
        var player1 = Players[0];
        if (player1.Devices.Count == 0)
            player1.UseDevices(player1.UsingGamepad && player1.GamepadDevice != null
                ? new InputDevice[] { player1.GamepadDevice }
                : new InputDevice[] { Keyboard.current, Mouse.current });

        var joined = new PlayerControls(_players.Count);
        joined.UseDevices(device is Keyboard ? new InputDevice[] { device, Mouse.current } : new[] { device });
        _players.Add(joined);
        if (_listening) joined.Actions.Player.Enable();
        Version++;
        return joined;
    }

    /// <summary>Removes the last player; player 1 alone uses any device again.</summary>
    public static void Leave(int index)
    {
        if (index <= 0 || index >= _players.Count) return;
        _players[index].Actions.Dispose();
        _players.RemoveAt(index);
        if (_players.Count == 1) _players[0].UseDevices(null);
        Version++;
    }

    // ---------- Setup ----------

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        if (_listening) InputSystem.onActionChange -= OnActionChange;
        _listening = false;
        foreach (var player in _players) player.Actions.Dispose();
        _players.Clear();
        Version = 0;
        _joinScreens.Clear();
    }

    // Not earlier: in a build the Input System resets every enabled action when the first scene loads
    // (InputActionState.InitializeGlobalActionState), so actions enabled before then read nothing
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnableAfterFirstScene()
    {
        foreach (var player in Players) player.Actions.Player.Enable();
        InputSystem.onActionChange += OnActionChange;
        _listening = true;
    }

    static void LoadBindings(InputSystem_Actions actions)
    {
        string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            actions.asset.LoadBindingOverridesFromJson(json);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"GameInput: the saved controls couldn't be read, so the defaults are used. {exception.Message}");
        }
    }

    // The device a player used last decides which keys their labels show. Only the game's own actions count (the UI's
    // Navigate reacts to an idle controller's stick noise), and a controller only once something is pushed at least
    // halfway, so a resting controller never takes over from the keyboard
    static void OnActionChange(object target, InputActionChange change)
    {
        if (change != InputActionChange.ActionPerformed || target is not InputAction action) return;
        PlayerControls player = null;
        foreach (var candidate in _players)
            if (action.actionMap?.asset == candidate.Actions.asset) player = candidate;
        if (player == null) return;

        var control = action.activeControl;
        var device = control?.device;
        if (device == null) return;
        bool gamepad = device is UnityEngine.InputSystem.Gamepad;
        if (!gamepad && device is not Keyboard && device is not Mouse) return;
        if (gamepad && control.EvaluateMagnitude() < GamepadSwitchMagnitude) return;
        // The pointer only counts when it moved: enabling the map again (Options closing) reports where it already is
        if (action == player.Actions.Player.Look)
        {
            var pointer = action.ReadValue<Vector2>();
            bool moved = (pointer - player.LastPointer).sqrMagnitude > PointerMoveSquared;
            player.LastPointer = pointer;
            if (!moved) return;
        }
        if (gamepad) player.LastGamepad = (UnityEngine.InputSystem.Gamepad)device;
        if (gamepad == player.UsingGamepad) return;
        player.UsingGamepad = gamepad;
        Version++;
    }

    // ---------- Bindings (on player 1's actions, then copied to the others) ----------

    /// <summary>
    /// The index of the action's binding in a binding group, or -1. Part names a composite's part (Move's "up"); without
    /// it, composite parts are skipped.
    /// </summary>
    public static int BindingIndex(InputAction action, string group, string part = null)
    {
        var mask = InputBinding.MaskByGroup(group);
        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            if (binding.isComposite) continue;
            if (part != null ? !binding.isPartOfComposite || binding.name != part : binding.isPartOfComposite) continue;
            if (mask.Matches(binding)) return i;
        }
        return -1;
    }

    /// <summary>Short name of what is bound ("LMB", "Q", "RT"), or empty when nothing is.</summary>
    public static string Label(InputAction action, string group, string part = null)
    {
        int index = BindingIndex(action, group, part);
        if (index < 0 || string.IsNullOrEmpty(action.bindings[index].effectivePath)) return string.Empty;
        return action.GetBindingDisplayString(index, InputBinding.DisplayStringOptions.DontIncludeInteractions);
    }

    /// <summary>The key label for the device player 1 used last.</summary>
    public static string Label(InputAction action) => Label(action, Group);

    public static void Clear(InputAction action, int index)
    {
        action.ApplyBindingOverride(index, string.Empty);
        Save();
    }

    public static void Reset(InputAction action, int index)
    {
        action.RemoveBindingOverride(index);
        ClearOthersUsing(action, index);
        Save();
    }

    public static void ResetAll()
    {
        Actions.asset.RemoveAllBindingOverrides();
        Save();
    }

    /// <summary>
    /// Waits for the next button of the binding's group and binds it (Controls window). Esc, Start, Backspace and View
    /// (Select) are left out so the window can use them to cancel and clear. The action (player 1's) is off meanwhile,
    /// as rebinding needs, and on again afterwards if it was.
    /// </summary>
    public static InputActionRebindingExtensions.RebindingOperation Rebind(InputAction action, int index, string group, System.Action done)
    {
        bool wasEnabled = action.enabled;
        action.Disable();
        var binding = action.bindings[index];
        bool stick = !binding.isPartOfComposite && action.expectedControlType == "Vector2";
        var operation = action.PerformInteractiveRebinding(index)
            .WithExpectedControlType(stick ? "Vector2" : "Button")
            .WithControlsExcluding("<Keyboard>/escape")
            .WithControlsExcluding("<Keyboard>/backspace")
            .WithControlsExcluding("<Gamepad>/start")
            .WithControlsExcluding("<Gamepad>/select")
            .OnMatchWaitForAnother(0.05f);
        if (group == Gamepad) operation.WithControlsHavingToMatchPath("<Gamepad>");
        else operation.WithControlsHavingToMatchPath("<Keyboard>").WithControlsHavingToMatchPath("<Mouse>");

        operation.OnComplete(op =>
        {
            op.Dispose();
            ClearOthersUsing(action, index);
            Save();
            if (wasEnabled) action.Enable();
            done?.Invoke();
        });
        operation.OnCancel(op =>
        {
            op.Dispose();
            if (wasEnabled) action.Enable();
            done?.Invoke();
        });
        return operation.Start();
    }

    // A control is bound to one action per group: binding it here unbinds it everywhere else in the Player map. Look
    // (the mouse pointer) can't be rebound, so it keeps its binding
    static void ClearOthersUsing(InputAction action, int index)
    {
        var bound = action.bindings[index];
        string path = bound.effectivePath;
        if (string.IsNullOrEmpty(path)) return;
        foreach (var other in Player.Get().actions)
        {
            if (other == Player.Look) continue;
            var bindings = other.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (other == action && i == index) continue;
                var binding = bindings[i];
                if (binding.isComposite || binding.effectivePath != path || !SameGroup(binding, bound)) continue;
                other.ApplyBindingOverride(i, string.Empty);
            }
        }
    }

    static bool SameGroup(InputBinding a, InputBinding b) =>
        InputBinding.MaskByGroup(KeyboardMouse).Matches(a) == InputBinding.MaskByGroup(KeyboardMouse).Matches(b) &&
        InputBinding.MaskByGroup(Gamepad).Matches(a) == InputBinding.MaskByGroup(Gamepad).Matches(b);

    // Saved from player 1's actions, then given to the other players so everyone has the same bindings
    static void Save()
    {
        string json = Actions.asset.SaveBindingOverridesAsJson();
        PlayerPrefs.SetString(PrefsKey, json);
        PlayerPrefs.Save();
        for (int i = 1; i < _players.Count; i++)
        {
            _players[i].Actions.asset.RemoveAllBindingOverrides();
            _players[i].Actions.asset.LoadBindingOverridesFromJson(json);
        }
        Version++;
    }
}
