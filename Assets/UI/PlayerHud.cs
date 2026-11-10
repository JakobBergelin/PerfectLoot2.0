using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Where a player's own HUD goes: the whole screen alone, and in co-op their half (player 1 left, player 2 right), so
/// bars and panels centred on the screen centre in their half instead. Kept up to date by UiNavigator.
/// </summary>
public static class PlayerHud
{
    struct Entry
    {
        public VisualElement Root;
        public int PlayerIndex;
    }

    static readonly List<Entry> _roots = new List<Entry>();
    static bool _shownCoop;

    /// <summary>A HUD's full-screen root (stretched to the screen's edges), for this player.</summary>
    public static void Register(VisualElement root, int playerIndex)
    {
        _roots.RemoveAll(entry => entry.Root == root);
        _roots.Add(new Entry { Root = root, PlayerIndex = playerIndex });
        Place(root, playerIndex, GameInput.PlayerCount > 1);
    }

    /// <summary>True when the player has half the screen (co-op), so a wide panel needs its narrow layout.</summary>
    public static bool IsHalf => GameInput.PlayerCount > 1;

    internal static void Update()
    {
        bool coop = GameInput.PlayerCount > 1;
        if (_roots.RemoveAll(entry => entry.Root.panel == null) == 0 && coop == _shownCoop) return;
        _shownCoop = coop;
        foreach (var entry in _roots) Place(entry.Root, entry.PlayerIndex, coop);
    }

    static void Place(VisualElement root, int playerIndex, bool coop)
    {
        root.style.left = coop && playerIndex > 0 ? Length.Percent(50f) : new Length(0f);
        root.style.right = coop && playerIndex == 0 ? Length.Percent(50f) : new Length(0f);
    }

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _roots.Clear();
        _shownCoop = false;
    }
}
