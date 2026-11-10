using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The game's input actions: one shared copy, so a rebind reaches everything that reads them. Keeps the player's
/// bindings (saved per machine in PlayerPrefs), which device was used last (for key labels), and does the rebinding
/// for the Controls window. Each action has one binding per binding group: Keyboard&amp;Mouse and Gamepad.
/// Escape and the gamepad's Start button aren't actions: they always open and close menus (MenuPanels.EscapePressed).
/// </summary>
public static class GameInput
{
    public const string KeyboardMouse = "Keyboard&Mouse";
    public const string Gamepad = "Gamepad";
    const string PrefsKey = "Controls.Bindings";

    static InputSystem_Actions _actions;
    static InputAction[] _skills;
    static bool _usingGamepad;
    static bool _listening;

    public static InputSystem_Actions Actions
    {
        get
        {
            if (_actions == null) Create();
            return _actions;
        }
    }

    public static InputSystem_Actions.PlayerActions Player => Actions.Player;

    /// <summary>Skill1 to Skill4, by slot index.</summary>
    public static InputAction Skill(int index)
    {
        if (_actions == null) Create();
        return _skills[index];
    }

    /// <summary>True when the device used last is a gamepad.</summary>
    public static bool UsingGamepad => _usingGamepad;

    /// <summary>The binding group of the device used last.</summary>
    public static string Group => _usingGamepad ? Gamepad : KeyboardMouse;

    /// <summary>Goes up whenever a binding or the device used last changes, so key labels know to refresh.</summary>
    public static int Version { get; private set; }

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        if (_listening) InputSystem.onActionChange -= OnActionChange;
        _listening = false;
        _actions?.Dispose();
        _actions = null;
        _skills = null;
        _usingGamepad = false;
        Version = 0;
    }

    // Not earlier: in a build the Input System resets every enabled action when the first scene loads
    // (InputActionState.InitializeGlobalActionState), so actions enabled before then read nothing
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnableAfterFirstScene()
    {
        Player.Enable();
        InputSystem.onActionChange += OnActionChange;
        _listening = true;
    }

    static void Create()
    {
        _actions = new InputSystem_Actions();
        _skills = new[] { _actions.Player.Skill1, _actions.Player.Skill2, _actions.Player.Skill3, _actions.Player.Skill4 };
        string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            _actions.asset.LoadBindingOverridesFromJson(json);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"GameInput: the saved controls couldn't be read, so the defaults are used. {exception.Message}");
        }
    }

    // The device used last decides which keys the labels show
    static void OnActionChange(object target, InputActionChange change)
    {
        if (change != InputActionChange.ActionPerformed || target is not InputAction action) return;
        var device = action.activeControl?.device;
        if (device == null) return;
        bool gamepad = device is UnityEngine.InputSystem.Gamepad;
        if (!gamepad && device is not Keyboard && device is not Mouse) return;
        if (gamepad == _usingGamepad) return;
        _usingGamepad = gamepad;
        Version++;
    }

    // ---------- Bindings ----------

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

    /// <summary>The key label for the device used last.</summary>
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
    /// Waits for the next button of the binding's group and binds it (Controls window). Esc and Backspace are left out
    /// so the window can use them to cancel and clear. The action must be disabled (OptionsHUD disables the Player map).
    /// </summary>
    public static InputActionRebindingExtensions.RebindingOperation Rebind(InputAction action, int index, string group, System.Action done)
    {
        var binding = action.bindings[index];
        bool stick = !binding.isPartOfComposite && action.expectedControlType == "Vector2";
        var operation = action.PerformInteractiveRebinding(index)
            .WithExpectedControlType(stick ? "Vector2" : "Button")
            .WithControlsExcluding("<Keyboard>/escape")
            .WithControlsExcluding("<Keyboard>/backspace")
            .WithControlsExcluding("<Gamepad>/start")
            .OnMatchWaitForAnother(0.05f);
        if (group == Gamepad) operation.WithControlsHavingToMatchPath("<Gamepad>");
        else operation.WithControlsHavingToMatchPath("<Keyboard>").WithControlsHavingToMatchPath("<Mouse>");

        operation.OnComplete(op =>
        {
            op.Dispose();
            ClearOthersUsing(action, index);
            Save();
            done?.Invoke();
        });
        operation.OnCancel(op =>
        {
            op.Dispose();
            done?.Invoke();
        });
        return operation.Start();
    }

    // A control is bound to one action per group: binding it here unbinds it everywhere else in the Player map. Look
    // (the pointer, a stick) can't be rebound, so it keeps its bindings
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

    static void Save()
    {
        PlayerPrefs.SetString(PrefsKey, Actions.asset.SaveBindingOverridesAsJson());
        PlayerPrefs.Save();
        Version++;
    }
}
