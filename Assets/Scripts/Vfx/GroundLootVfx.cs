using System.Collections.Generic;
using Equipment;
using UnityEngine;

/// <summary>
/// A beam of light over each item on the ground in its rarity's color (faint for Normal items), and a sparkle where one
/// is picked up; a small gold one over each gold pile. Follows GroundLootManager's list whenever its Version changes. Cosmetic only. On the rig, next to
/// GroundLootManager.
/// </summary>
public class GroundLootVfx : MonoBehaviour
{
    [Tooltip("How see-through Normal items' beams are (0-1), so they don't compete with the colored ones")]
    [Range(0f, 1f)] public float NormalAlpha = 0.4f;
    [Tooltip("Gold piles' beams and sparkles")]
    public Color GoldColor = new Color(0.95f, 0.76f, 0.31f);
    [Tooltip("Gold piles' beams are this size of an item's")]
    public float GoldBeamScale = 0.35f;

    readonly Dictionary<object, int> _beams = new Dictionary<object, int>();   // By GroundItem or GroundGold
    readonly HashSet<object> _onGround = new HashSet<object>();
    readonly List<object> _gone = new List<object>();
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
        foreach (var gold in _manager.Gold)
        {
            _onGround.Add(gold);
            if (_beams.ContainsKey(gold)) continue;
            int handle = vfx.Spawn(VfxType.LootBeam, gold.Position, Quaternion.identity);
            vfx.Tint(handle, GoldColor);
            vfx.ScaleUniform(handle, GoldBeamScale);
            _beams[gold] = handle;
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
        if (_manager != null)
        {
            _manager.ItemPickedUp -= OnPickedUp;
            _manager.GoldPickedUp -= OnGoldPickedUp;
        }
        _manager = manager;
        if (_manager != null)
        {
            _manager.ItemPickedUp += OnPickedUp;
            _manager.GoldPickedUp += OnGoldPickedUp;
        }
    }

    void OnGoldPickedUp(GroundLootManager.GroundGold gold)
    {
        var vfx = VfxManager.Instance;
        if (vfx == null) return;
        int handle = vfx.Burst(VfxType.LootPickup, gold.Position, Quaternion.identity);
        vfx.Tint(handle, GoldColor);
        vfx.ScaleUniform(handle, GoldBeamScale * 2f);
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
