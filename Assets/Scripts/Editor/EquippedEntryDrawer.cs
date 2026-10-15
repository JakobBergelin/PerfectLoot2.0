using Equipment;
using UnityEditor;
using UnityEngine;

/// <summary>One line per InventoryManager.Equipped entry: a rarity swatch and "Slot: Item (Rarity)" in the rarity color.</summary>
[CustomPropertyDrawer(typeof(InventoryManager.EquippedEntry))]
public class EquippedEntryDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var slot = property.FindPropertyRelative(nameof(InventoryManager.EquippedEntry.Slot)).stringValue;
        var item = property.FindPropertyRelative(nameof(InventoryManager.EquippedEntry.Item)).stringValue;
        var rarity = (Rarity)property.FindPropertyRelative(nameof(InventoryManager.EquippedEntry.Rarity)).intValue;

        float swatchSize = position.height - 4f;
        RarityGUI.Swatch(new Rect(position.x, position.y + 2f, swatchSize, swatchSize), rarity);

        var textRect = new Rect(position.x + swatchSize + 6f, position.y, position.width - swatchSize - 6f, position.height);
        EditorGUI.LabelField(textRect, $"{slot}: {item} ({rarity})", RarityGUI.LabelStyle(rarity));
    }
}
