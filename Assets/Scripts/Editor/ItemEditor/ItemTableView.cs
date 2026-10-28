using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Equipment;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Item Editor's Table tab: one row per item, one column per stat that appears among the shown items. The slot's
/// intrinsic stats and fixed values come first, then every other affix. Affix cells show the range (several lines if
/// the item has the affix more than once) heat-colored by ItemOutliers. The header row and the name column stay in
/// place while the cells scroll.
/// </summary>
public class ItemTableView
{
    const float NameWidth = 210f;
    const float ColumnWidth = 92f;
    const float HeaderHeight = 34f;
    const float LineHeight = 16f;
    const float ScrollWheelSpeed = 20f;

    static readonly Color HeaderLine = new Color(0, 0, 0, 0.4f);
    static readonly Color SlotLine = new Color(0, 0, 0, 0.5f);
    static readonly Color AlternateRow = new Color(1, 1, 1, 0.03f);
    static readonly Color PinnedTint = new Color(1, 1, 1, 0.05f);

    /// <summary>A table column: an affix range, a fixed (not rolled) number field of the item class, or a computed value.</summary>
    class Column
    {
        public string Header;
        public string Tooltip;
        public Affix Affix;
        public string Field;    // Set for fixed-value columns
        public Func<RollableItem, string> Computed;     // Set for computed columns (e.g. weapon DPS); empty where it doesn't apply
        public bool Pinned;     // Intrinsic stats and fixed values, shown first with a tinted header
    }

    Vector2 _scroll;

    /// <summary>Draws the table; returns the item whose name was clicked, or null.</summary>
    public RollableItem Draw(List<RollableItem> items, ItemOutliers outliers)
    {
        if (items.Count == 0)
        {
            EditorGUILayout.HelpBox("No items match the filters.", MessageType.Info);
            return null;
        }

        EditorGUILayout.LabelField("Cells are colored by how far a range's midpoint is from other items of the same slot: " +
                                   "warm = higher, cool = lower. Hover a cell for details; click a name to edit the item.",
                                   EditorStyles.wordWrappedMiniLabel);

        var columns = BuildColumns(items);
        var rows = items.Select(item => item.AllRanges.ToList()).ToList();
        var heights = rows.Select(ranges => RowHeight(ranges, columns)).ToList();
        int pinnedCount = columns.Count(column => column.Pinned);
        var cellStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };

        var area = GUILayoutUtility.GetRect(0f, float.MaxValue, 0f, float.MaxValue, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("* intrinsic slot stat   ° rollable affix   (no mark: base affix)   Tinted columns: intrinsic stats and fixed values",
                                   EditorStyles.miniLabel);

        var content = new Rect(0, 0, columns.Count * ColumnWidth, heights.Sum());
        var body = new Rect(area.x + NameWidth, area.y + HeaderHeight, Mathf.Max(0f, area.width - NameWidth), Mathf.Max(0f, area.height - HeaderHeight));
        // The cells' horizontal scrollbar covers the bottom of the body; the name column stops above it
        bool horizontalBar = content.width > body.width;
        float visibleHeight = body.height - (horizontalBar ? GUI.skin.horizontalScrollbar.fixedHeight : 0f);

        // Scrolling over the name column or the header scrolls the cells too
        var current = Event.current;
        if (current.type == EventType.ScrollWheel && area.Contains(current.mousePosition) && !body.Contains(current.mousePosition))
        {
            _scroll += current.delta * ScrollWheelSpeed;
            current.Use();
        }

        // Cells
        _scroll = GUI.BeginScrollView(body, _scroll, content);
        {
            float y = 0f;
            for (int r = 0; r < items.Count; r++)
            {
                var rowRect = new Rect(0, y, content.width, heights[r]);
                y += heights[r];
                if (rowRect.yMax < _scroll.y || rowRect.y > _scroll.y + body.height) continue;

                DrawRowBackground(rowRect, items, r);
                for (int c = 0; c < columns.Count; c++)
                {
                    float x = c * ColumnWidth;
                    if (x + ColumnWidth < _scroll.x || x > _scroll.x + body.width) continue;
                    DrawCell(new Rect(x + 1, rowRect.y + 2, ColumnWidth - 2, rowRect.height - 4), items[r], rows[r], columns[c], outliers, cellStyle);
                }
            }
            if (pinnedCount > 0 && pinnedCount < columns.Count)
                EditorGUI.DrawRect(new Rect(pinnedCount * ColumnWidth - 1, 0, 1, content.height), HeaderLine);
        }
        GUI.EndScrollView();

        // Header: scrolls only sideways
        var corner = new Rect(area.x, area.y, NameWidth, HeaderHeight);
        GUI.Label(corner, "Item", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.LowerLeft });
        var header = new Rect(body.x, area.y, body.width, HeaderHeight);
        GUI.BeginClip(header);
        {
            var headerStyle = new GUIStyle(EditorStyles.miniBoldLabel) { wordWrap = true, alignment = TextAnchor.LowerCenter };
            for (int c = 0; c < columns.Count; c++)
            {
                var rect = new Rect(c * ColumnWidth - _scroll.x, 0, ColumnWidth, HeaderHeight);
                if (rect.xMax < 0f || rect.x > header.width) continue;
                if (columns[c].Pinned) EditorGUI.DrawRect(rect, PinnedTint);
                GUI.Label(rect, new GUIContent(columns[c].Header, columns[c].Tooltip), headerStyle);
            }
            if (pinnedCount > 0 && pinnedCount < columns.Count)
                EditorGUI.DrawRect(new Rect(pinnedCount * ColumnWidth - 1 - _scroll.x, 0, 1, HeaderHeight), HeaderLine);
        }
        GUI.EndClip();
        EditorGUI.DrawRect(new Rect(area.x, area.y + HeaderHeight - 1, area.width, 1), HeaderLine);

        // Name column: scrolls only up and down
        RollableItem clicked = null;
        var names = new Rect(area.x, body.y, NameWidth, Mathf.Max(0f, visibleHeight));
        GUI.BeginClip(names);
        {
            float y = -_scroll.y;
            for (int r = 0; r < items.Count; r++)
            {
                var rowRect = new Rect(0, y, NameWidth, heights[r]);
                y += heights[r];
                if (rowRect.yMax < 0f || rowRect.y > names.height) continue;

                DrawRowBackground(rowRect, items, r);
                var item = items[r];
                var rarity = ItemAssets.DisplayRarity(item);
                RarityGUI.Swatch(new Rect(rowRect.x + 2, rowRect.y + 5, 8, 8), rarity);
                var nameRect = new Rect(rowRect.x + 14, rowRect.y + 2, NameWidth - 16, LineHeight);
                if (GUI.Button(nameRect, new GUIContent(item.Name, $"{ItemAssets.TierOf(item)} {item.ItemType}. Click to edit."), RarityGUI.LabelStyle(rarity, bold: true)))
                    clicked = item;
                GUI.Label(new Rect(rowRect.x + 14, rowRect.y + 2 + LineHeight, NameWidth - 16, LineHeight), item.ItemType.ToString(), EditorStyles.miniLabel);
            }
        }
        GUI.EndClip();
        EditorGUI.DrawRect(new Rect(area.x + NameWidth - 1, area.y, 1, area.height), HeaderLine);

        return clicked;
    }

    /// <summary>Intrinsic affixes, then fixed-value fields, then every other affix (each group in a stable order).</summary>
    static List<Column> BuildColumns(List<RollableItem> items)
    {
        var all = items.SelectMany(item => item.AllRanges).ToList();
        var intrinsic = all.Where(r => r.Source == AffixSource.Intrinsic).Select(r => r.Affix).Distinct().OrderBy(a => (int)a).ToList();
        var others = all.Select(r => r.Affix).Distinct().Where(a => !intrinsic.Contains(a)).OrderBy(a => (int)a);
        var fields = items.Select(item => item.GetType()).Distinct().SelectMany(FixedFields)
                          .GroupBy(field => field.Name).Select(group => group.First());

        var columns = new List<Column>();
        columns.AddRange(intrinsic.Select(affix => new Column { Header = ItemEditorFormat.AffixName(affix), Affix = affix, Pinned = true }));
        columns.AddRange(fields.Select(field => new Column
        {
            Header = ObjectNames.NicifyVariableName(field.Name),
            Tooltip = field.GetCustomAttribute<TooltipAttribute>()?.tooltip,
            Field = field.Name,
            Pinned = true,
        }));
        // Weapon DPS, right after the cooldown it's computed from
        int cooldown = columns.FindIndex(column => column.Field == nameof(RollableWeaponItem.WeaponCooldown));
        if (cooldown >= 0)
            columns.Insert(cooldown + 1, new Column
            {
                Header = "Damage Per Second",
                Tooltip = ItemEditorFormat.DpsTooltip,
                Computed = item => item is RollableWeaponItem weapon ? ItemEditorFormat.Dps(weapon) : "",
                Pinned = true,
            });
        columns.AddRange(others.Select(affix => new Column { Header = ItemEditorFormat.AffixName(affix), Affix = affix }));
        return columns;
    }

    /// <summary>Public number fields declared by item classes (not RollableItem itself), e.g. a weapon's reach.</summary>
    static IEnumerable<FieldInfo> FixedFields(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(field => field.DeclaringType != typeof(RollableItem) && typeof(RollableItem).IsAssignableFrom(field.DeclaringType) &&
                            (field.FieldType == typeof(float) || field.FieldType == typeof(int)));

    // At least two lines: the name and the slot under it
    static float RowHeight(List<(Affix Affix, RollRange Range, AffixSource Source)> ranges, List<Column> columns)
    {
        int lines = 2;
        foreach (var column in columns)
            if (column.Field == null && column.Computed == null) lines = Mathf.Max(lines, ranges.Count(r => r.Affix == column.Affix));
        return lines * LineHeight + 6;
    }

    static void DrawRowBackground(Rect rect, List<RollableItem> items, int row)
    {
        if (row % 2 == 1) EditorGUI.DrawRect(rect, AlternateRow);
        if (row > 0 && items[row].ItemType != items[row - 1].ItemType) EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), SlotLine);
    }

    static void DrawCell(Rect cell, RollableItem item, List<(Affix Affix, RollRange Range, AffixSource Source)> ranges, Column column, ItemOutliers outliers, GUIStyle centered)
    {
        if (column.Field != null)
        {
            var field = item.GetType().GetField(column.Field);
            if (field == null) return;
            GUI.Label(cell, new GUIContent(Convert.ToSingle(field.GetValue(item)).ToString("0.##"), column.Tooltip), centered);
            return;
        }
        if (column.Computed != null)
        {
            GUI.Label(cell, new GUIContent(column.Computed(item), column.Tooltip), centered);
            return;
        }

        var entries = ranges.Where(r => r.Affix == column.Affix).ToList();
        if (entries.Count == 0) return;

        // Strongest verdict colors the cell; every entry gets a line and a tooltip line
        var verdicts = entries.Select(e => outliers.Evaluate(item, e.Affix, e.Range)).ToList();
        var strongest = verdicts.OrderByDescending(v => v.HasData ? Mathf.Abs(v.Score) : -1f).First();
        var color = ItemOutliers.ScoreColor(strongest);
        if (color.a > 0f) GUI.DrawTexture(cell, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, 3);

        string text = string.Join("\n", entries.Select(e => ItemEditorFormat.Range(e.Affix, e.Range) + SourceMark(e.Source)));
        string tooltip = string.Join("\n\n", verdicts.Select(v => v.Tooltip));
        GUI.Label(cell, new GUIContent(text, tooltip), centered);
    }

    static string SourceMark(AffixSource source) => source switch
    {
        AffixSource.Intrinsic => "*",
        AffixSource.Rollable => "°",
        _ => "",
    };
}
