using System.Collections.Generic;
using Equipment;
using UnityEngine;

/// <summary>
/// A beam of light over each item on the ground in its rarity's color (faint for Normal items), and a sparkle where one
/// is picked up. Follows GroundLootManager's list whenever its Version changes. Cosmetic only. On the rig, next to
/// GroundLootManager.
/// </summary>
public class GroundLootVfx : MonoBehaviour
{
    [Tooltip("How see-through Normal items' beams are (0-1), so they don't compete with the colored ones")]
    [Range(0f, 1f)] public float NormalAlpha = 0.4f;

    readonly Dictionary<GroundLootManager.GroundItem, int> _beams = new Dictionary<GroundLootManager.GroundItem, int>();
    readonly HashSet<GroundLootManager.GroundItem> _onGround = new HashSet<GroundLootManager.GroundItem>();
    readonly List<GroundLootManager.GroundItem> _gone = new List<GroundLootManager.GroundItem>();
    GroundLootManager _manager;
    int _version = -1;

    void OnDisable()
    {
        Watch(null);
        var vfx = VfxManager.Instance;
        if (vfx != null) foreach (var handle in _beams.Values) vfx.Release(handle);
        _beams.Clear();
        _version = -1;
    }

    void Update()
    {
        Watch(GroundLootManager.Instance);
        var vfx = VfxManager.Instance;
        if (_manager == null || vfx == null || _manager.Version == _version) return;
        _version = _manager.Version;

        _onGround.Clear();
        foreach (var item in _manager.Items)
        {
            _onGround.Add(item);
            if (_beams.ContainsKey(item)) continue;
            int handle = vfx.Spawn(VfxType.LootBeam, item.Position, Quaternion.identity);
            vfx.Tint(handle, ColorOf(item.Item));
            _beams[item] = handle;
        }

        // Picked up, or left behind in the last scene
        _gone.Clear();
        foreach (var (item, handle) in _beams)
        {
            if (_onGround.Contains(item)) continue;
            vfx.Release(handle);
            _gone.Add(item);
        }
        foreach (var item in _gone) _beams.Remove(item);
    }

    void Watch(GroundLootManager manager)
    {
        if (manager == _manager) return;
        if (_manager != null) _manager.ItemPickedUp -= OnPickedUp;
        _manager = manager;
        if (_manager != null) _manager.ItemPickedUp += OnPickedUp;
    }

    void OnPickedUp(GroundLootManager.GroundItem item)
    {
        var vfx = VfxManager.Instance;
        if (vfx == null) return;
        vfx.Tint(vfx.Burst(VfxType.LootPickup, item.Position, Quaternion.identity), ColorOf(item.Item));
    }

    Color ColorOf(Item item)
    {
        var color = ItemText.TitleColor(item);
        if (item.Rarity == Rarity.Normal) color.a = NormalAlpha;
        return color;
    }
}
