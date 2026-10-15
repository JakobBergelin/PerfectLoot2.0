using Equipment;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RollableItem), true)]
public class RollableItemEditor : IconPreviewEditor
{
    protected override Sprite Icon => ((RollableItem)target).Icon;
    protected override string PreviewTitle => ((RollableItem)target).Name;

    // Standard items roll their rarity on drop, so they show as Normal; Legendary/Unique have a fixed one
    protected override Color? BackgroundColor => RarityColors.Get(((RollableItem)target).ForcedRarity ?? Rarity.Normal);
}
