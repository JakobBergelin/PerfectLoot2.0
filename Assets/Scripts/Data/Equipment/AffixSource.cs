namespace Equipment
{
    /// <summary>Where a roll range on a base item comes from (RollableItem.AllRanges).</summary>
    public enum AffixSource
    {
        Intrinsic,  // The slot's own stats, e.g. weapon damage or a chest's armor
        Base,       // BaseAffixes: always on the item
        Rollable,   // RollableAffixes: some of them, depending on rarity
    }
}
