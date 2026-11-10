using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// One player's own buttons for the small co-op menus (player 2's character picker), read from their devices only, so
/// the other player's presses never reach it: up and down (D-pad, left stick, arrow keys or W/S, repeating while held),
/// confirm (A, Enter or Space), back (B, Esc or Backspace) and Start (Start or Esc).
/// </summary>
public sealed class PlayerMenuInput
{
    const float StickPress = 0.5f;
    const float RepeatDelay = 0.35f;
    const float RepeatInterval = 0.1f;

    readonly int _playerIndex;
    int _heldStep;
    float _nextRepeat;

    public PlayerMenuInput(int playerIndex) => _playerIndex = playerIndex;

    GameInput.PlayerControls Controls => GameInput.For(_playerIndex);
    Gamepad Pad => Controls?.GamepadDevice;
    Keyboard Keys => Controls?.KeyboardDevice;

    /// <summary>True when they play with a controller (else the keyboard and mouse).</summary>
    public bool UsesGamepad => Pad != null;

    /// <summary>True when the mouse is theirs (they joined with the keyboard).</summary>
    public bool OwnsMouse => Controls != null && Mouse.current != null && Controls.Devices.Contains(Mouse.current);

    public bool ConfirmPressed =>
        (Pad != null && Pad.buttonSouth.wasPressedThisFrame) ||
        (Keys != null && (Keys.enterKey.wasPressedThisFrame || Keys.numpadEnterKey.wasPressedThisFrame || Keys.spaceKey.wasPressedThisFrame));

    public bool BackPressed =>
        (Pad != null && Pad.buttonEast.wasPressedThisFrame) ||
        (Keys != null && (Keys.escapeKey.wasPressedThisFrame || Keys.backspaceKey.wasPressedThisFrame));

    public bool StartPressed =>
        (Pad != null && Pad.startButton.wasPressedThisFrame) || (Keys != null && Keys.escapeKey.wasPressedThisFrame);

    /// <summary>"A" or "Enter", and "B" or "Esc", for hints.</summary>
    public string ConfirmKey => UsesGamepad ? "A" : "Enter";
    public string BackKey => UsesGamepad ? "B" : "Esc";

    /// <summary>-1 up, +1 down, else 0: once when pressed, then repeating while held. Call once a frame.</summary>
    public int ReadStep()
    {
        int step = HeldStep();
        if (step == 0 || step != _heldStep)
        {
            _heldStep = step;
            _nextRepeat = Time.unscaledTime + RepeatDelay;
            return step;
        }
        if (Time.unscaledTime < _nextRepeat) return 0;
        _nextRepeat = Time.unscaledTime + RepeatInterval;
        return step;
    }

    int HeldStep()
    {
        var pad = Pad;
        if (pad != null)
        {
            Vector2 value = pad.dpad.ReadValue();
            if (value.sqrMagnitude < 0.25f) value = pad.leftStick.ReadValue();
            if (Mathf.Abs(value.y) >= StickPress && Mathf.Abs(value.y) >= Mathf.Abs(value.x)) return value.y > 0f ? -1 : 1;
        }
        var keys = Keys;
        if (keys != null)
        {
            if (keys.upArrowKey.isPressed || keys.wKey.isPressed) return -1;
            if (keys.downArrowKey.isPressed || keys.sKey.isPressed) return 1;
        }
        return 0;
    }
}
