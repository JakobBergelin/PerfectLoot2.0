using Equipment;
using UnityEditor;
using UnityEngine;

/// <summary>Shows a rolled Item with its icon on a rarity-colored background above its fields (e.g. the account ItemDatabase entries).</summary>
[CustomPropertyDrawer(typeof(Item))]
public class ItemDrawer : PropertyDrawer
{
    const float HeaderHeight = 40f;
    const float Spacing = 4f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // Not EditorGUI.GetPropertyHeight(property): that would call back into this drawer
        float height = EditorGUIUtility.singleLineHeight;
        if (property.isExpanded)
            foreach (var child in Children(property))
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(child, true);

        return BaseItem(property) != null ? HeaderHeight + Spacing + height : height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var baseItem = BaseItem(property);
        if (baseItem != null)
        {
            var rarity = (Rarity)property.FindPropertyRelative(nameof(Item.Rarity)).intValue;
            DrawHeader(new Rect(position.x, position.y, position.width, HeaderHeight), baseItem, rarity);
            position.yMin += HeaderHeight + Spacing;
        }

        // Fields, drawn per child (not EditorGUI.PropertyField(property), which would recurse into this drawer)
        var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            float y = line.yMax;
            foreach (var child in Children(property))
            {
                y += EditorGUIUtility.standardVerticalSpacing;
                float height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(new Rect(position.x, y, position.width, height), child, true);
                y += height;
            }
            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    static void DrawHeader(Rect header, RollableItem baseItem, Rarity rarity)
    {
        var iconRect = new Rect(header.x, header.y, HeaderHeight, HeaderHeight);
        IconPreviewEditor.DrawIcon(iconRect, baseItem.Icon, AssetPreview.GetMiniThumbnail(baseItem), RarityColors.Get(rarity));

        float textX = iconRect.xMax + 8f;
        float lineHeight = EditorGUIUtility.singleLineHeight;
        var nameRect = new Rect(textX, header.y + HeaderHeight / 2 - lineHeight, header.xMax - textX, lineHeight);
        EditorGUI.LabelField(nameRect, string.IsNullOrEmpty(baseItem.Name) ? baseItem.name : baseItem.Name, RarityGUI.LabelStyle(rarity, bold: true));
        EditorGUI.LabelField(new Rect(textX, nameRect.yMax, nameRect.width, lineHeight), $"{rarity} {baseItem.ItemType}", RarityGUI.LabelStyle(rarity));
    }

    static System.Collections.Generic.IEnumerable<SerializedProperty> Children(SerializedProperty property)
    {
        var child = property.Copy();
        var end = property.GetEndProperty();
        bool enterChildren = true;
        while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
        {
            enterChildren = false;
            yield return child.Copy();
        }
    }

    static RollableItem BaseItem(SerializedProperty property) =>
        property.FindPropertyRelative(nameof(Item.BaseItem))?.objectReferenceValue as RollableItem;
}
