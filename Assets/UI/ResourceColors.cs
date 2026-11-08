using UnityEngine;

/// <summary>
/// Each resource type's colour, the same as its bar fill (UnitBars.uss, unit-bar--&lt;type&gt;): keep the two matched.
/// For UI that names a resource, such as the character screen and the Skills panel.
/// </summary>
public static class ResourceColors
{
    public static string Hex(ResourceType type) => type switch
    {
        ResourceType.Mana => "#1FB4FF",
        ResourceType.Rage => "#D8362E",
        ResourceType.Focus => "#FFC21F",
        ResourceType.Energy => "#A6F01E",
        ResourceType.Arcane => "#C93BC0",
        _ => "#3E8CFF",
    };

    public static Color Of(ResourceType type) => ColorUtility.TryParseHtmlString(Hex(type), out var color) ? color : Color.white;
}
