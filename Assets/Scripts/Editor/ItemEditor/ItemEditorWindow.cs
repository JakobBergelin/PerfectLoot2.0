using System;
using System.Collections.Generic;
using System.Linq;
using Equipment;
using Loottables;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Item Editor: all RollableItem assets in one place. Items tab: filterable list + editor for the selected
/// item, with outlier markers on its affix ranges and its loot tables. Table tab: every item's affix ranges side by
/// side, heat-colored by how far they are from other items of the same slot (ItemOutliers).
/// </summary>
public class ItemEditorWindow : EditorWindow
{
    const float ListWidth = 300f;
    const float RowHeight = 42f;
    static readonly string[] Tabs = { "Items", "Table" };

    enum TierFilter { All, Standard, Legendary, Unique }

    [SerializeField] int _tab;
    [SerializeField] string _search = "";
    [SerializeField] int _slotFilter = -1;                  // -1 = all slots, otherwise (int)ItemType
    [SerializeField] TierFilter _tierFilter = TierFilter.All;
    [SerializeField] string _selectedGuid;
    Vector2 _listScroll, _detailScroll;

    List<RollableItem> _items = new List<RollableItem>();
    List<LootTable> _lootTables = new List<LootTable>();
    readonly ItemOutliers _outliers = new ItemOutliers();
    readonly ItemTableView _table = new ItemTableView();
    RollableItem _selected;
    SerializedObject _serialized;
    string _nameEdit;

    [MenuItem("Tools/Item Editor")]
    static void Open() => GetWindow<ItemEditorWindow>("Item Editor").minSize = new Vector2(760, 420);

    void OnEnable()
    {
        Refresh();
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;

    void OnProjectChange() => Refresh();

    void OnUndoRedo()
    {
        _serialized?.Update();
        _outliers.Rebuild(_items);
        Repaint();
    }

    void Refresh()
    {
        _items = ItemAssets.FindAllItems()
            .OrderBy(item => item.ItemType)
            .ThenBy(item => ItemAssets.TierOf(item))
            .ThenBy(item => item.Name)
            .ToList();
        _lootTables = ItemAssets.FindAllLootTables().OrderBy(table => table.name).ToList();
        _outliers.Rebuild(_items);

        var selected = string.IsNullOrEmpty(_selectedGuid) ? null
            : AssetDatabase.LoadAssetAtPath<RollableItem>(AssetDatabase.GUIDToAssetPath(_selectedGuid));
        Select(selected != null ? selected : _items.FirstOrDefault());
        Repaint();
    }

    void Select(RollableItem item)
    {
        _selected = item;
        _serialized = item != null ? new SerializedObject(item) : null;
        _nameEdit = item != null ? item.Name : "";
        _selectedGuid = item != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item)) : null;
        GUI.FocusControl(null);
    }

    IEnumerable<RollableItem> Filtered() => _items.Where(item =>
        item != null &&
        (_slotFilter < 0 || (int)item.ItemType == _slotFilter) &&
        (_tierFilter == TierFilter.All || (int)ItemAssets.TierOf(item) == (int)_tierFilter - 1) &&
        (string.IsNullOrEmpty(_search) || (item.Name ?? item.name).IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0));

    // ---------- Layout ----------

    void OnGUI()
    {
        DrawToolbar();
        if (_tab == 0)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(ListWidth))) DrawList();
                DrawVerticalLine();
                using (new EditorGUILayout.VerticalScope()) DrawDetail();
            }
        }
        else
        {
            var clicked = _table.Draw(Filtered().ToList(), _outliers);
            if (clicked != null)
            {
                Select(clicked);
                _tab = 0;
            }
        }
    }

    void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            _tab = GUILayout.Toolbar(_tab, Tabs, EditorStyles.toolbarButton, GUILayout.Width(140));
            GUILayout.Space(8);
            _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(170));

            var slotNames = new[] { "All slots" }.Concat(Enum.GetNames(typeof(ItemType))).ToArray();
            _slotFilter = EditorGUILayout.Popup(_slotFilter + 1, slotNames, EditorStyles.toolbarPopup, GUILayout.Width(95)) - 1;
            _tierFilter = (TierFilter)EditorGUILayout.EnumPopup(_tierFilter, EditorStyles.toolbarPopup, GUILayout.Width(85));

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(45)))
                NewItemDialog.Show(_lootTables, _slotFilter >= 0 ? (ItemType)_slotFilter : ItemType.Weapon, OnItemCreated);
            using (new EditorGUI.DisabledScope(_selected == null))
            {
                if (GUILayout.Button("Duplicate", EditorStyles.toolbarButton, GUILayout.Width(70))) DuplicateSelected();
                if (GUILayout.Button("Delete", EditorStyles.toolbarButton, GUILayout.Width(55))) DeleteSelected();
            }
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60))) Refresh();
        }
    }

    static void DrawVerticalLine()
    {
        var rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandHeight(true), GUILayout.Width(1));
        EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.35f));
    }

    // ---------- List ----------

    void DrawList()
    {
        _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
        ItemType? currentSlot = null;
        foreach (var item in Filtered())
        {
            if (_slotFilter < 0 && item.ItemType != currentSlot)
            {
                currentSlot = item.ItemType;
                EditorGUILayout.LabelField(currentSlot.ToString(), EditorStyles.miniBoldLabel);
            }
            DrawListRow(item);
        }
        EditorGUILayout.EndScrollView();
    }

    void DrawListRow(RollableItem item)
    {
        var rect = GUILayoutUtility.GetRect(0, RowHeight, GUILayout.ExpandWidth(true));
        bool selected = item == _selected;
        if (selected) EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.9f, 0.35f));
        else if (rect.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(rect, new Color(1, 1, 1, 0.05f));

        var rarity = ItemAssets.DisplayRarity(item);
        IconPreviewEditor.DrawIcon(new Rect(rect.x + 4, rect.y + 3, RowHeight - 6, RowHeight - 6), item.Icon,
            AssetPreview.GetMiniThumbnail(item), RarityColors.Get(rarity));

        float textX = rect.x + RowHeight + 4;
        GUI.Label(new Rect(textX, rect.y + 4, rect.width - RowHeight - 70, 18), item.Name, RarityGUI.LabelStyle(rarity, bold: true));
        GUI.Label(new Rect(textX, rect.y + 21, rect.width - RowHeight - 70, 16), $"{item.ItemType} · {ItemAssets.TierOf(item)}", EditorStyles.miniLabel);

        // Outlier badge: how many ranges are above / below typical for the slot
        var (high, low) = _outliers.Count(item);
        var badge = new Rect(rect.xMax - 62, rect.y + 12, 58, 18);
        if (high > 0) DrawBadge(new Rect(badge.x, badge.y, 28, 18), $"▲{high}", new Color(1f, 0.55f, 0.15f, 0.8f), "Ranges higher than typical for the slot");
        if (low > 0) DrawBadge(new Rect(badge.x + 30, badge.y, 28, 18), $"▼{low}", new Color(0.3f, 0.55f, 1f, 0.8f), "Ranges lower than typical for the slot");

        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            Select(item);
            Event.current.Use();
        }
    }

    static void DrawBadge(Rect rect, string text, Color color, string tooltip)
    {
        GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, 4);
        var style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        GUI.Label(rect, new GUIContent(text, tooltip), style);
    }

    // ---------- Detail ----------

    void DrawDetail()
    {
        if (_selected == null || _serialized == null || _serialized.targetObject == null)
        {
            EditorGUILayout.HelpBox("Select an item on the left, or create one with New.", MessageType.Info);
            return;
        }

        _serialized.Update();
        _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
        EditorGUI.BeginChangeCheck();

        DrawHeader();
        EditorGUILayout.Space(6);
        DrawNameAndIcon();
        DrawOtherFields();
        EditorGUILayout.Space(6);
        DrawAffixList("Base affixes (always on the item)", _serialized.FindProperty(nameof(RollableItem.BaseAffixes)));
        DrawAffixList("Rollable affixes (some, depending on rarity)", _serialized.FindProperty(nameof(RollableItem.RollableAffixes)));
        EditorGUILayout.Space(6);
        DrawLootTables();

        if (EditorGUI.EndChangeCheck())
        {
            _serialized.ApplyModifiedProperties();
            _outliers.Rebuild(_items);
        }
        EditorGUILayout.EndScrollView();
    }

    void DrawHeader()
    {
        var rarity = ItemAssets.DisplayRarity(_selected);
        var rect = GUILayoutUtility.GetRect(0, 84, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), RarityColors.Get(rarity) ?? Color.gray);
        IconPreviewEditor.DrawIcon(new Rect(rect.x + 6, rect.y + 4, 76, 76), _selected.Icon, AssetPreview.GetMiniThumbnail(_selected), RarityColors.Get(rarity));

        var title = new GUIStyle(RarityGUI.LabelStyle(rarity, bold: true)) { fontSize = 18 };
        GUI.Label(new Rect(rect.x + 92, rect.y + 8, rect.width - 100, 26), _selected.Name, title);
        GUI.Label(new Rect(rect.x + 92, rect.y + 36, 70, 18), _selected.ItemType.ToString());
        DrawRarityPopup(new Rect(rect.x + 160, rect.y + 36, 200, 18));
        string path = AssetDatabase.GetAssetPath(_selected);
        GUI.Label(new Rect(rect.x + 92, rect.y + 56, rect.width - 160, 18), path, EditorStyles.miniLabel);
        if (GUI.Button(new Rect(rect.xMax - 56, rect.y + 54, 50, 18), "Ping", EditorStyles.miniButton))
            EditorGUIUtility.PingObject(_selected);
    }

    void DrawNameAndIcon()
    {
        // Delayed: the asset is renamed when you finish editing (focus out / Enter), not on every key
        var newName = EditorGUILayout.DelayedTextField("Name", _nameEdit);
        if (newName != _nameEdit)
        {
            _nameEdit = newName;
            _serialized.FindProperty(nameof(RollableItem.Name)).stringValue = newName;
            _serialized.ApplyModifiedProperties();
            string error = ItemAssets.RenameAssetToMatch(_selected);
            if (error != null) Debug.LogWarning($"Item Editor: couldn't rename the asset: {error}");
        }
        DrawFieldRow(_serialized.FindProperty(nameof(RollableItem.Icon)));
    }

    // Slot-specific fields (weapon stats, intrinsic ranges, legendary power, ...); intrinsic ranges get outlier markers
    void DrawOtherFields()
    {
        var skip = new HashSet<string> { "m_Script", nameof(RollableItem.Name), nameof(RollableItem.Icon),
            nameof(RollableItem.BaseAffixes), nameof(RollableItem.RollableAffixes) };
        var intrinsic = _selected.AllRanges.Where(r => r.Source == AffixSource.Intrinsic).ToList();

        var property = _serialized.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (skip.Contains(property.name)) continue;

            var field = _selected.GetType().GetField(property.name);
            var range = field?.GetValue(_selected) as RollRange;
            var match = intrinsic.FirstOrDefault(r => ReferenceEquals(r.Range, range));
            string path = property.propertyPath;
            if (range != null && match.Range != null)
                DrawRangeRow(property.displayName, null, property.FindPropertyRelative(nameof(RollRange.Min)),
                    property.FindPropertyRelative(nameof(RollRange.Max)), _outliers.Evaluate(_selected, match.Affix, range), null,
                    () => ShowFieldCopyMenu(path));
            else
                DrawFieldRow(property);
        }
    }

    void DrawAffixList(string title, SerializedProperty list)
    {
        if (list == null) return;
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        var ranges = list.name == nameof(RollableItem.BaseAffixes) ? _selected.BaseAffixes : _selected.RollableAffixes;
        string listName = list.name;

        int remove = -1;
        for (int i = 0; i < list.arraySize; i++)
        {
            var element = list.GetArrayElementAtIndex(i);
            var affixProperty = element.FindPropertyRelative(nameof(Rollable.Affix));
            var rangeProperty = element.FindPropertyRelative(nameof(Rollable.RollRange));
            var rollable = ranges != null && i < ranges.Count ? ranges[i] : null;
            var verdict = rollable != null ? _outliers.Evaluate(_selected, rollable.Affix, rollable.RollRange) : default;

            if (DrawRangeRow(null, affixProperty, rangeProperty.FindPropertyRelative(nameof(RollRange.Min)),
                    rangeProperty.FindPropertyRelative(nameof(RollRange.Max)), verdict, "Remove", () => ShowAffixCopyMenu(listName, rollable)))
                remove = i;
        }
        if (remove >= 0) list.DeleteArrayElementAtIndex(remove);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Add affix", EditorStyles.miniButton, GUILayout.Width(90)))
            {
                list.arraySize++;
                var added = list.GetArrayElementAtIndex(list.arraySize - 1).FindPropertyRelative(nameof(Rollable.RollRange));
                added.FindPropertyRelative(nameof(RollRange.Min)).floatValue = 0f;
                added.FindPropertyRelative(nameof(RollRange.Max)).floatValue = 0f;
            }
        }
    }

    /// <summary>One range row: label or affix dropdown, Min, Max, outlier marker, optional button. Returns true when the button was clicked.</summary>
    static bool DrawRangeRow(string label, SerializedProperty affix, SerializedProperty min, SerializedProperty max,
        ItemOutliers.Verdict verdict, string button, Action copyMenu)
    {
        bool clicked = false;
        using (new EditorGUILayout.HorizontalScope())
        {
            if (affix != null) EditorGUILayout.PropertyField(affix, GUIContent.none, GUILayout.Width(170));
            else EditorGUILayout.LabelField(label, GUILayout.Width(170));

            EditorGUIUtility.labelWidth = 28;
            EditorGUILayout.PropertyField(min, new GUIContent("Min"), GUILayout.MinWidth(70));
            EditorGUILayout.PropertyField(max, new GUIContent("Max"), GUILayout.MinWidth(70));
            EditorGUIUtility.labelWidth = 0;

            var marker = GUILayoutUtility.GetRect(34, 18, GUILayout.Width(34));
            var color = ItemOutliers.ScoreColor(verdict);
            if (color.a > 0f) GUI.DrawTexture(marker, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, 4);
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(marker, new GUIContent(verdict.IsOutlier ? ItemOutliers.ScoreArrow(verdict) : "·", verdict.Tooltip), style);

            if (copyMenu != null && CopyButton()) copyMenu();

            if (button != null && GUILayout.Button(new GUIContent("✕", button), EditorStyles.miniButton, GUILayout.Width(22)))
                clicked = true;
        }
        if (copyMenu != null && IsContextClick()) copyMenu();
        return clicked;
    }

    // ---------- Copying to other items ----------

    /// <summary>A plain field row with a copy button (and right-click) for copying the field to other items.</summary>
    void DrawFieldRow(SerializedProperty property)
    {
        if (property == null) return;
        string path = property.propertyPath;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(property, true);
            if (CopyButton()) ShowFieldCopyMenu(path);
        }
        if (IsContextClick()) ShowFieldCopyMenu(path);
    }

    static bool CopyButton() =>
        GUILayout.Button(new GUIContent("⋯", "Copy to other items"), EditorStyles.miniButton, GUILayout.Width(22));

    static bool IsContextClick()
    {
        var current = Event.current;
        if (current.type != EventType.ContextClick || !GUILayoutUtility.GetLastRect().Contains(current.mousePosition)) return false;
        current.Use();
        return true;
    }

    void ShowFieldCopyMenu(string propertyPath)
    {
        var source = _selected;
        var property = _serialized.FindProperty(propertyPath);
        if (source == null || property == null) return;
        // Only items with the same field can receive it (e.g. weapon reach only on weapons)
        string fieldName = propertyPath.Split('.')[0];
        var targets = ItemAssets.FieldTargets(source, fieldName, _items);
        ShowCopyMenu(property.displayName, targets, chosen =>
        {
            _serialized.ApplyModifiedProperties();
            ItemAssets.CopyField(source, propertyPath, chosen);
            Debug.Log($"Item Editor: copied {property.displayName} from {source.Name} to {string.Join(", ", chosen.Select(i => i.Name))}.");
        });
    }

    void ShowAffixCopyMenu(string listName, Rollable rollable)
    {
        var source = _selected;
        if (source == null || rollable?.RollRange == null) return;
        // Snapshot the values now, so the copy is what you see even if the row is edited before picking a target
        var affix = rollable.Affix;
        float min = rollable.RollRange.Min, max = rollable.RollRange.Max;
        string where = listName == nameof(RollableItem.BaseAffixes) ? "base" : "rollable";
        ShowCopyMenu($"{ItemEditorFormat.AffixName(affix)} ({where})", _items.Where(i => i != null && i != source).ToList(), chosen =>
        {
            ItemAssets.CopyAffix(listName, affix, min, max, chosen);
            Debug.Log($"Item Editor: copied {where} affix {affix} {min}–{max} from {source.Name} to {string.Join(", ", chosen.Select(i => i.Name))}.");
        });
    }

    /// <summary>"Copy to all [slot] items" plus every target item grouped by slot.</summary>
    void ShowCopyMenu(string what, List<RollableItem> targets, Action<List<RollableItem>> copy)
    {
        var menu = new GenericMenu();
        void Run(List<RollableItem> chosen)
        {
            copy(chosen);
            _serialized?.Update();
            _outliers.Rebuild(_items);
            Repaint();
        }

        if (targets.Count == 0)
        {
            menu.AddDisabledItem(new GUIContent($"No other item has {what}"));
        }
        else
        {
            var sameSlot = targets.Where(t => t.ItemType == _selected.ItemType).ToList();
            if (sameSlot.Count > 0)
                menu.AddItem(new GUIContent($"Copy {what} to all {_selected.ItemType} items ({sameSlot.Count})"), false, () => Run(sameSlot));
            menu.AddSeparator("");
            foreach (var target in targets)
            {
                // '/' would create a submenu level
                string label = $"Copy {what} to/{target.ItemType}/{(target.Name ?? target.name).Replace('/', '∕')}";
                var single = new List<RollableItem> { target };
                menu.AddItem(new GUIContent(label), false, () => Run(single));
            }
        }
        menu.ShowAsContext();
    }

    // ---------- Rarity ----------

    readonly Dictionary<(ItemType, ItemAssets.Tier), bool> _tierExists = new Dictionary<(ItemType, ItemAssets.Tier), bool>();

    bool TierExists(ItemType slot, ItemAssets.Tier tier)
    {
        if (!_tierExists.TryGetValue((slot, tier), out bool exists))
            _tierExists[(slot, tier)] = exists = ItemAssets.ItemClassFor(slot, tier) != null;
        return exists;
    }

    void DrawRarityPopup(Rect rect)
    {
        var current = ItemAssets.TierOf(_selected);
        var tiers = ((ItemAssets.Tier[])Enum.GetValues(typeof(ItemAssets.Tier))).Where(t => TierExists(_selected.ItemType, t)).ToList();
        var labels = tiers.Select(t => t == ItemAssets.Tier.Standard ? "Standard (Normal/Magic/Rare)" : t.ToString()).ToArray();
        int index = EditorGUI.Popup(rect, tiers.IndexOf(current), labels);
        if (index < 0 || tiers[index] == current) return;

        var tier = tiers[index];
        bool losesPower = tier == ItemAssets.Tier.Standard && _selected is ILegendaryItem legendary && legendary.Power != null;
        string message = $"Change {_selected.Name} from {current} to {tier}?\n\n" +
                         "The asset keeps its file and references (loot tables), name, icon, affixes and slot stats." +
                         (losesPower ? $"\n\nIts legendary power ({((ILegendaryItem)_selected).Power.DisplayName}) will be removed: standard items have none." : "") +
                         "\n\nThis can't be undone.";
        if (!EditorUtility.DisplayDialog("Change rarity", message, "Change", "Cancel")) return;

        var changed = ItemAssets.ChangeTier(_selected, tier);
        Refresh();
        Select(changed);
        GUIUtility.ExitGUI();
    }

    void DrawLootTables()
    {
        EditorGUILayout.LabelField("Loot tables", EditorStyles.boldLabel);
        if (_lootTables.Count == 0)
        {
            EditorGUILayout.LabelField("No loot tables in the project.", EditorStyles.miniLabel);
            return;
        }

        foreach (var table in _lootTables)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                int index = ItemAssets.IndexOf(table, _selected);
                bool included = index >= 0;
                bool newIncluded = EditorGUILayout.ToggleLeft(table.name, included, GUILayout.Width(200));
                if (newIncluded != included) ItemAssets.SetIncluded(table, _selected, newIncluded, 10f);

                if (included)
                {
                    EditorGUIUtility.labelWidth = 50;
                    float weight = EditorGUILayout.FloatField(new GUIContent("Weight", "Or mode: relative weight. And mode: chance 0-1."),
                        table.Entries[index].Value, GUILayout.Width(120));
                    EditorGUIUtility.labelWidth = 0;
                    if (!Mathf.Approximately(weight, table.Entries[index].Value)) ItemAssets.SetWeight(table, _selected, weight);
                }
            }
        }
    }

    // ---------- Actions ----------

    void OnItemCreated(RollableItem item)
    {
        Refresh();
        Select(item);
        _tab = 0;
    }

    void DuplicateSelected()
    {
        var copy = ItemAssets.Duplicate(_selected);
        if (copy == null) return;
        Refresh();
        Select(copy);
    }

    void DeleteSelected()
    {
        var tables = _lootTables.Where(table => ItemAssets.Contains(table, _selected)).Select(table => table.name).ToList();
        string message = $"Delete {_selected.Name} ({AssetDatabase.GetAssetPath(_selected)})?" +
                         (tables.Count > 0 ? $"\n\nIt will also be removed from: {string.Join(", ", tables)}." : "") +
                         "\n\nThis can't be undone.";
        if (!EditorUtility.DisplayDialog("Delete item", message, "Delete", "Cancel")) return;

        int index = _items.IndexOf(_selected);
        ItemAssets.Delete(_selected, _lootTables);
        _selectedGuid = null;
        Refresh();
        Select(_items.Count > 0 ? _items[Mathf.Clamp(index, 0, _items.Count - 1)] : null);
    }
}
