using System.Collections.Generic;
using Equipment;
using UnityEngine;

/// <summary>
/// The items found this session that a local player hasn't looked at yet: they highlight the Character button (MenuBarHUD)
/// and the slots they fit (CharacterPanelHUD) until the player opens that slot's picker. Not saved.
/// </summary>
public static class NewItems
{
    static readonly HashSet<ItemKey>[] _unseen = new HashSet<ItemKey>[GameInput.MaxPlayers];

    /// <summary>Goes up whenever an item was found or seen.</summary>
    public static int Version { get; private set; }

    /// <summary>The account's database got its first item for this key: new to every local player.</summary>
    public static void Found(ItemKey key)
    {
        for (int i = 0; i < _unseen.Length; i++) (_unseen[i] ??= new HashSet<ItemKey>()).Add(key);
        Version++;
    }

    /// <summary>The player opened the picker of a slot for this type.</summary>
    public static void Seen(int playerIndex, ItemType type)
    {
        if (_unseen[playerIndex] != null && _unseen[playerIndex].RemoveWhere(key => key.BaseItem.ItemType == type) > 0) Version++;
    }

    public static bool Any(int playerIndex) => _unseen[playerIndex] != null && _unseen[playerIndex].Count > 0;

    public static bool Any(int playerIndex, ItemType type)
    {
        if (!Any(playerIndex)) return false;
        foreach (var key in _unseen[playerIndex])
            if (key.BaseItem.ItemType == type) return true;
        return false;
    }

    // Also on entering Play mode: statics survive it when domain reload is off
    /// <summary>Another account signed in.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Clear()
    {
        foreach (var items in _unseen) items?.Clear();
        Version++;
    }
}
