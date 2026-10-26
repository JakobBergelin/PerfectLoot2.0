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

    // The default inspector, with Id read-only (it's the asset's GUID)
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var property = serializedObject.GetIterator();
        for (bool enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
        {
            bool readOnly = property.propertyPath == "m_Script" || property.propertyPath == nameof(RollableItem.Id);
            using (new EditorGUI.DisabledScope(readOnly))
                EditorGUILayout.PropertyField(property, true);
        }
        serializedObject.ApplyModifiedProperties();
    }
}
