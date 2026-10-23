using System;
using System.Collections.Generic;
using UnityEngine;
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

    /// <summary>True while a menu panel is open, a popup blocks gameplay input, or a text field has keyboard focus.</summary>
    public static bool IsAnyOpen => _open != null || _blockers.Count > 0 || _typing.Count > 0;

    /// <summary>True while a UI text field has keyboard focus: hotkeys and movement keys are for the text.</summary>
    public static bool IsTyping => _typing.Count > 0;

    public static void SetTyping(object owner, bool typing)
    {
        if (typing) _typing.Add(owner);
        else _typing.Remove(owner);
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
            OpenChanged?.Invoke(_open);
        }
        else if (_open == panel)
        {
            _open = null;
            OpenChanged?.Invoke(null);
        }
    }

    /// <summary>Blocks gameplay input while set, without counting as an open panel (other panels stay open).</summary>
    public static void SetBlocking(object owner, bool blocking)
    {
        if (blocking) _blockers.Add(owner);
        else _blockers.Remove(owner);
    }

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _open = null;
        _blockers.Clear();
        _typing.Clear();
        OpenChanged = null;
    }
}
