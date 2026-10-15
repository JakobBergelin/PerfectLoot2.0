using Equipment;
using UnityEditor;
using UnityEngine;

/// <summary>Shared drawing helpers for showing rarity in the editor.</summary>
public static class RarityGUI
{
    /// <summary>Rarity color adjusted for text: darkened on the light editor skin so yellow/gold stay readable.</summary>
    public static Color TextColor(Rarity rarity)
    {
        var color = RarityColors.Get(rarity) ?? EditorStyles.label.normal.textColor;
        return EditorGUIUtility.isProSkin ? color : Color.Lerp(color, Color.black, 0.4f);
    }

    public static GUIStyle LabelStyle(Rarity rarity, bool bold = false)
    {
        var style = new GUIStyle(bold ? EditorStyles.boldLabel : EditorStyles.label);
        style.normal.textColor = TextColor(rarity);
        return style;
    }

    /// <summary>Small rounded color chip, e.g. in front of a list entry.</summary>
    public static void Swatch(Rect rect, Rarity rarity)
    {
        var color = RarityColors.Get(rarity);
        if (color == null) return;
        GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color.Value, 0, rect.height * 0.25f);
    }
}
