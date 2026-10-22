using System;
using System.Collections.Generic;
using Equipment;
using Loottables;
using UnityEditor;
using UnityEngine;

/// <summary>Item Editor's "New" dialog: name, slot and tier, and optionally a loot table to add the new item to.</summary>
public class NewItemDialog : EditorWindow
{
    string _name = "New Item";
    ItemType _slot;
    ItemAssets.Tier _tier;
    bool _addToTable = true;
    LootTable _table;
    float _weight = 10f;
    List<LootTable> _tables;
    Action<RollableItem> _onCreated;
    bool _focused;

    public static void Show(List<LootTable> tables, ItemType slot, Action<RollableItem> onCreated)
    {
        var dialog = CreateInstance<NewItemDialog>();
        dialog.titleContent = new GUIContent("New Item");
        dialog._tables = tables;
        dialog._slot = slot;
        dialog._table = ItemAssets.DefaultTableFor(slot, tables);
        dialog._onCreated = onCreated;
        dialog.minSize = dialog.maxSize = new Vector2(340, 170);
        dialog.ShowUtility();
    }

    void OnGUI()
    {
        GUI.SetNextControlName("Name");
        _name = EditorGUILayout.TextField("Name", _name);
        if (!_focused) { EditorGUI.FocusTextInControl("Name"); _focused = true; }

        EditorGUI.BeginChangeCheck();
        _slot = (ItemType)EditorGUILayout.EnumPopup("Slot", _slot);
        if (EditorGUI.EndChangeCheck()) _table = ItemAssets.DefaultTableFor(_slot, _tables);
        _tier = (ItemAssets.Tier)EditorGUILayout.EnumPopup("Tier", _tier);

        _addToTable = EditorGUILayout.Toggle("Add to loot table", _addToTable);
        using (new EditorGUI.DisabledScope(!_addToTable))
        {
            _table = (LootTable)EditorGUILayout.ObjectField("Loot table", _table, typeof(LootTable), false);
            _weight = EditorGUILayout.FloatField("Weight", _weight);
        }

        bool hasClass = ItemAssets.ItemClassFor(_slot, _tier) != null;
        if (!hasClass) EditorGUILayout.HelpBox($"There is no item class for a {_tier} {_slot}.", MessageType.Warning);

        GUILayout.FlexibleSpace();
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(80))) Close();
            using (new EditorGUI.DisabledScope(!hasClass || string.IsNullOrWhiteSpace(_name)))
            {
                bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                if (GUILayout.Button("Create", GUILayout.Width(80)) || (enter && hasClass)) Create();
            }
        }
    }

    void Create()
    {
        var item = ItemAssets.Create(_name.Trim(), _slot, _tier);
        if (item != null && _addToTable && _table != null)
        {
            ItemAssets.SetIncluded(_table, item, true, _weight);
            AssetDatabase.SaveAssets();
        }
        Close();
        if (item != null) _onCreated?.Invoke(item);
    }
}
