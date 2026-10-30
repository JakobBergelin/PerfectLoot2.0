using System.Collections.Generic;
using Equipment;
using UnityEngine;

/// <summary>
/// Each affix's icon: Resources/AffixIcons/{Affix name}.png (imported as sprites by AffixIconImporter). A new affix
/// only needs a PNG with its enum name. Null when there is none.
/// </summary>
public static class AffixIcons
{
    const string Folder = "AffixIcons/";

    static readonly Dictionary<Affix, Sprite> _cache = new Dictionary<Affix, Sprite>();

    public static Sprite Get(Affix affix)
    {
        if (!_cache.TryGetValue(affix, out var sprite))
        {
            sprite = Resources.Load<Sprite>(Folder + affix);
            _cache[affix] = sprite;
        }
        return sprite;
    }
}
