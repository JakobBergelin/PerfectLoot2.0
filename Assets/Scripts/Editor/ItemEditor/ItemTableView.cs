using System.Collections.Generic;
using System.Linq;
using Equipment;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Item Editor's Table tab: one row per item, one column per affix that appears among the shown items. Each cell
/// shows the range (several lines if the item has the affix more than once) heat-colored by ItemOutliers.
/// </summary>
public class ItemTableView
{
    const float NameWidth = 210f;
    const float ColumnWidth = 92f;
    const float HeaderHeight = 34f;
    const float LineHeight = 16f;

    Vector2 _scroll;

    /// <summary>Draws the table; returns the item whose name was clicked, or null.</summary>
    public RollableItem Draw(List<RollableItem> items, ItemOutliers outliers)
    {
        if (items.Count == 0)
        {
            EditorGUILayout.HelpBox("No items match the filters.", MessageType.Info);
            return null;
        }

        var affixes = items.SelectMany(item => item.AllRanges.Select(r => r.Affix)).Distinct().OrderBy(a => (int)a).ToList();
        RollableItem clicked = null;

        EditorGUILayout.LabelField("Cells are colored by how far a range's midpoint is from other items of the same slot: " +
                                   "warm = higher, cool = lower. Hover a cell for details; click a name to edit the item.",
                                   EditorStyles.wordWrappedMiniLabel);

        float width = NameWidth + affixes.Count * ColumnWidth;
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        // Header
        var header = GUILayoutUtility.GetRect(width, HeaderHeight, GUILayout.Width(width));
        var headerStyle = new GUIStyle(EditorStyles.miniBoldLabel) { wordWrap = true, alignment = TextAnchor.LowerCenter };
        GUI.Label(new Rect(header.x, header.y, NameWidth, HeaderHeight), "Item", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.LowerLeft });
        for (int c = 0; c < affixes.Count; c++)
            GUI.Label(new Rect(header.x + NameWidth + c * ColumnWidth, header.y, ColumnWidth, HeaderHeight), ItemEditorFormat.AffixName(affixes[c]), headerStyle);
        EditorGUI.DrawRect(new Rect(header.x, header.yMax - 1, width, 1), new Color(0, 0, 0, 0.4f));

        var cellStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
        ItemType? currentSlot = null;
        int row = 0;
        foreach (var item in items)
        {
            var ranges = item.AllRanges.ToList();
            // At least two lines: the name and the slot under it
            int lines = Mathf.Max(2, affixes.Max(a => ranges.Count(r => r.Affix == a)));
            var rect = GUILayoutUtility.GetRect(width, lines * LineHeight + 6, GUILayout.Width(width));

            // Slot separator
            if (item.ItemType != currentSlot)
            {
                if (currentSlot != null) EditorGUI.DrawRect(new Rect(rect.x, rect.y, width, 2), new Color(0, 0, 0, 0.5f));
                currentSlot = item.ItemType;
            }
            if (row++ % 2 == 1) EditorGUI.DrawRect(rect, new Color(1, 1, 1, 0.03f));

            // Name in the rarity color, with a swatch
            var rarity = ItemAssets.DisplayRarity(item);
            RarityGUI.Swatch(new Rect(rect.x + 2, rect.y + 5, 8, 8), rarity);
            var nameRect = new Rect(rect.x + 14, rect.y + 2, NameWidth - 16, LineHeight);
            if (GUI.Button(nameRect, new GUIContent(item.Name, $"{ItemAssets.TierOf(item)} {item.ItemType}. Click to edit."), RarityGUI.LabelStyle(rarity, bold: true)))
                clicked = item;
            GUI.Label(new Rect(rect.x + 14, rect.y + 2 + LineHeight, NameWidth - 16, LineHeight), item.ItemType.ToString(), EditorStyles.miniLabel);

            // Cells
            for (int c = 0; c < affixes.Count; c++)
            {
                var cell = new Rect(rect.x + NameWidth + c * ColumnWidth + 1, rect.y + 2, ColumnWidth - 2, rect.height - 4);
                var entries = ranges.Where(r => r.Affix == affixes[c]).ToList();
                if (entries.Count == 0) continue;

                // Strongest verdict colors the cell; every entry gets a line and a tooltip line
                var verdicts = entries.Select(e => outliers.Evaluate(item, e.Affix, e.Range)).ToList();
                var strongest = verdicts.OrderByDescending(v => v.HasData ? Mathf.Abs(v.Score) : -1f).First();
                var color = ItemOutliers.ScoreColor(strongest);
                if (color.a > 0f) GUI.DrawTexture(cell, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, 3);

                string text = string.Join("\n", entries.Select(e => ItemEditorFormat.Range(e.Affix, e.Range) + SourceMark(e.Source)));
                string tooltip = string.Join("\n\n", verdicts.Select(v => v.Tooltip));
                GUI.Label(cell, new GUIContent(text, tooltip), cellStyle);
            }
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.LabelField("* intrinsic slot stat   ° rollable affix   (no mark: base affix)", EditorStyles.miniLabel);
        return clicked;
    }

    static string SourceMark(AffixSource source) => source switch
    {
        AffixSource.Intrinsic => "*",
        AffixSource.Rollable => "°",
        _ => "",
    };
}
