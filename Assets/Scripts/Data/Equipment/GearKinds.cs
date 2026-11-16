using System.Collections.Generic;

namespace Equipment
{
    /// <summary>
    /// What a weapon or offhand is (RollableWeaponItem.Kind, RollableOffhandItem.Kind). Skills that need one name the kinds
    /// that work, any one of them will do (ActiveSkill.RequiredGear). Stored in assets: add new values at the end.
    /// </summary>
    [System.Flags]
    public enum GearKinds
    {
        None = 0,
        Bow = 1 << 0,
        Crossbow = 1 << 1,
        Dagger = 1 << 2,
        Sword = 1 << 3,
        Staff = 1 << 4,
        Wand = 1 << 5,
        Shield = 1 << 6,
        Focus = 1 << 7,
        Quiver = 1 << 8,
    }

    public static class Gear
    {
        /// <summary>True when nothing is required, or one of the required kinds is equipped.</summary>
        public static bool Meets(GearKinds required, GearKinds equipped) => required == GearKinds.None || (required & equipped) != 0;

        /// <summary>The kinds of these equipped items together.</summary>
        public static GearKinds Of(IEnumerable<Item> items)
        {
            var kinds = GearKinds.None;
            foreach (var item in items)
                if (item?.BaseItem != null) kinds |= item.BaseItem.Gear;
            return kinds;
        }

        /// <summary>"Bow or Crossbow", or "" for none.</summary>
        public static string Text(GearKinds kinds)
        {
            var names = new List<string>();
            foreach (GearKinds kind in System.Enum.GetValues(typeof(GearKinds)))
                if (kind != GearKinds.None && (kinds & kind) != 0) names.Add(kind.ToString());
            if (names.Count <= 1) return names.Count == 1 ? names[0] : "";
            return string.Join(", ", names.GetRange(0, names.Count - 1)) + " or " + names[names.Count - 1];
        }
    }
}
