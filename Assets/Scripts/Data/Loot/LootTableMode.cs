namespace Loottables
{
    public enum LootTableMode
    {
        And, // each entry rolls independently; Value = chance (0-1)
        Or   // pick exactly one entry; Value = weight
    }
}
