using UnityEngine;

namespace Equipment
{
    /// <summary>Display color per rarity, for editor tooling and in-game UI.</summary>
    public static class RarityColors
    {
        public static readonly Color Normal = new Color32(154, 154, 154, 255);     // Gray
        public static readonly Color Magic = new Color32(77, 127, 255, 255);       // Blue
        public static readonly Color Rare = new Color32(255, 224, 77, 255);        // Yellow
        public static readonly Color Legendary = new Color32(224, 160, 32, 255);   // Gold, more orange than Rare
        public static readonly Color Unique = new Color32(154, 77, 217, 255);      // Purple

        /// <summary>The color for a rarity, or null for Unrolled.</summary>
        public static Color? Get(Rarity rarity) => rarity switch
        {
            Rarity.Normal => Normal,
            Rarity.Magic => Magic,
            Rarity.Rare => Rare,
            Rarity.Legendary => Legendary,
            Rarity.Unique => Unique,
            _ => null
        };
    }
}
