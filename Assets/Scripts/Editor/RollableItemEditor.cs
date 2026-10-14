using Equipment;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RollableItem), true)]
public class RollableItemEditor : IconPreviewEditor
{
    protected override Sprite Icon => ((RollableItem)target).Icon;
    protected override string PreviewTitle => ((RollableItem)target).Name;
}
