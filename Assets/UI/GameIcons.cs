using System.Collections.Generic;
using Equipment;
using UnityEngine;

/// <summary>Icons for tooltip lines without an affix or damage type: Resources/StatIcons/{name}.png.</summary>
public enum StatIcon
{
    Cooldown,
    Duration,
    Distance,
    CastTime,
}

/// <summary>
/// UI icons from Resources (imported as sprites by AffixIconImporter): affixes, damage types and other stats.
/// Null when the file is missing, so a line just shows without its icon.
/// </summary>
public static class GameIcons
{
    static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

    public static Sprite Affix(Affix affix) => AffixIcons.Get(affix);

    public static Sprite DamageType(DamageType type) => Load("DamageTypeIcons/" + type);

    public static Sprite Stat(StatIcon icon) => Load("StatIcons/" + icon);

    static Sprite Load(string path)
    {
        if (!_cache.TryGetValue(path, out var sprite))
        {
            sprite = Resources.Load<Sprite>(path);
            _cache[path] = sprite;
        }
        return sprite;
    }
}
