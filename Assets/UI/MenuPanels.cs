using System;
using UnityEngine;

/// <summary>
/// Tracks which menu panel (character, skills, ...) is open. Only one is open at a time: opening one closes the
/// others. Gameplay reads IsAnyOpen to ignore skill input while the mouse is used for the menu.
/// </summary>
public static class MenuPanels
{
    static object _open;

    public static bool IsAnyOpen => _open != null;

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

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _open = null;
        OpenChanged = null;
    }
}
