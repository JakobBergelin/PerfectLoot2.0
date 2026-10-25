using System.Collections.Generic;
using System.Linq;
using LevelGeneration;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tile set Inspector: each tile with a clickable 2x2 corner icon (click cycles the terrain), pieces with their corner
/// grid, and which corner combinations no tile covers (where the solver can get stuck).
/// </summary>
[CustomEditor(typeof(WfcTileSet))]
public class WfcTileSetEditor : Editor
{
    const float CornerSize = 20f;
    static readonly string[] CornerFields = { nameof(WfcTile.SouthWest), nameof(WfcTile.SouthEast), nameof(WfcTile.NorthEast), nameof(WfcTile.NorthWest) };

    bool _showMissing;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawTiles(serializedObject.FindProperty(nameof(WfcTileSet.Tiles)));
        EditorGUILayout.Space(10);
        DrawPieces(serializedObject.FindProperty(nameof(WfcTileSet.Pieces)));
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10);
        DrawCoverage((WfcTileSet)target);
    }

    void DrawTiles(SerializedProperty tiles)
    {
        EditorGUILayout.LabelField($"Tiles ({tiles.arraySize})", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Click a corner to change its terrain. Corners: top row is north.", EditorStyles.miniLabel);
        int remove = -1;
        for (int i = 0; i < tiles.arraySize; i++)
        {
            var tile = tiles.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                // 2x2 icon: NW NE on top, SW SE below
                var icon = GUILayoutUtility.GetRect(CornerSize * 2 + 2, CornerSize * 2 + 2, GUILayout.Width(CornerSize * 2 + 2));
                CornerButton(new Rect(icon.x, icon.y, CornerSize, CornerSize), tile.FindPropertyRelative(CornerFields[3]));
                CornerButton(new Rect(icon.x + CornerSize + 2, icon.y, CornerSize, CornerSize), tile.FindPropertyRelative(CornerFields[2]));
                CornerButton(new Rect(icon.x, icon.y + CornerSize + 2, CornerSize, CornerSize), tile.FindPropertyRelative(CornerFields[0]));
                CornerButton(new Rect(icon.x + CornerSize + 2, icon.y + CornerSize + 2, CornerSize, CornerSize), tile.FindPropertyRelative(CornerFields[1]));

                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(tile.FindPropertyRelative(nameof(WfcTile.Name)), GUIContent.none);
                        if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60))) remove = i;
                    }
                    EditorGUILayout.PropertyField(tile.FindPropertyRelative(nameof(WfcTile.Prefab)), GUIContent.none);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUIUtility.labelWidth = 50;
                        EditorGUILayout.PropertyField(tile.FindPropertyRelative(nameof(WfcTile.Weight)));
                        EditorGUIUtility.labelWidth = 70;
                        EditorGUILayout.PropertyField(tile.FindPropertyRelative(nameof(WfcTile.AllowRotations)), new GUIContent("Rotations"));
                        EditorGUIUtility.labelWidth = 0;
                    }
                }
            }
        }
        if (remove >= 0) tiles.DeleteArrayElementAtIndex(remove);
        if (GUILayout.Button("Add tile"))
        {
            tiles.arraySize++;
            var added = tiles.GetArrayElementAtIndex(tiles.arraySize - 1);
            added.FindPropertyRelative(nameof(WfcTile.Name)).stringValue = $"Tile {tiles.arraySize}";
            added.FindPropertyRelative(nameof(WfcTile.Weight)).floatValue = 1f;
            added.FindPropertyRelative(nameof(WfcTile.AllowRotations)).boolValue = true;
        }
    }

    void DrawPieces(SerializedProperty pieces)
    {
        EditorGUILayout.LabelField($"Pieces ({pieces.arraySize})", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Multi-cell pieces, placed by hand in the level layout. Top row is north.", EditorStyles.miniLabel);
        int remove = -1;
        for (int i = 0; i < pieces.arraySize; i++)
        {
            var piece = pieces.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(piece.FindPropertyRelative(nameof(WfcPiece.Name)), GUIContent.none);
                    if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60))) remove = i;
                }
                EditorGUILayout.PropertyField(piece.FindPropertyRelative(nameof(WfcPiece.Prefab)));
                var sizeProperty = piece.FindPropertyRelative(nameof(WfcPiece.Size));
                EditorGUILayout.PropertyField(sizeProperty);
                EditorGUILayout.PropertyField(piece.FindPropertyRelative(nameof(WfcPiece.AllowRotations)), new GUIContent("Rotations"));

                var size = sizeProperty.vector2IntValue;
                size = new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
                sizeProperty.vector2IntValue = size;
                var corners = piece.FindPropertyRelative(nameof(WfcPiece.Corners));
                int count = (size.x + 1) * (size.y + 1);
                if (corners.arraySize != count) corners.arraySize = count;

                // Corner grid, north row first
                for (int y = size.y; y >= 0; y--)
                {
                    var row = GUILayoutUtility.GetRect((size.x + 1) * (CornerSize + 2), CornerSize + 2, GUILayout.ExpandWidth(false));
                    for (int x = 0; x <= size.x; x++)
                        CornerButton(new Rect(row.x + x * (CornerSize + 2), row.y, CornerSize, CornerSize), corners.GetArrayElementAtIndex(x + y * (size.x + 1)));
                }
            }
        }
        if (remove >= 0) pieces.DeleteArrayElementAtIndex(remove);
        if (GUILayout.Button("Add piece"))
        {
            pieces.arraySize++;
            var added = pieces.GetArrayElementAtIndex(pieces.arraySize - 1);
            added.FindPropertyRelative(nameof(WfcPiece.Name)).stringValue = $"Piece {pieces.arraySize}";
            added.FindPropertyRelative(nameof(WfcPiece.Size)).vector2IntValue = new Vector2Int(2, 2);
            added.FindPropertyRelative(nameof(WfcPiece.AllowRotations)).boolValue = true;
        }
    }

    // A terrain swatch with its letter; clicking cycles Walkable -> Wall -> Water
    static void CornerButton(Rect rect, SerializedProperty terrain)
    {
        var value = (WfcTerrain)terrain.intValue;
        EditorGUI.DrawRect(rect, WfcTerrains.Color(value));
        var style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.black } };
        if (GUI.Button(rect, new GUIContent(WfcTerrains.Letter(value).ToString(), value.ToString()), style))
            terrain.intValue = ((int)value + 1) % WfcTerrains.Count;
    }

    void DrawCoverage(WfcTileSet tileSet)
    {
        var covered = new HashSet<int>(tileSet.BuildVariants().Select(v => WfcRotation.Code(new[] { v.SouthWest, v.SouthEast, v.NorthEast, v.NorthWest })));
        int total = (int)Mathf.Pow(WfcTerrains.Count, 4);
        int missing = total - covered.Count;
        if (missing == 0)
        {
            EditorGUILayout.HelpBox($"All {total} corner combinations have a tile (counting rotations).", MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox($"{missing} of {total} corner combinations have no tile. Wherever the rules or neighbors need one, " +
                                "the solver gets stuck and retries. Letters are SW SE NE NW: F floor, W wall, L water.", MessageType.Warning);
        _showMissing = EditorGUILayout.Foldout(_showMissing, "Missing combinations", true);
        if (!_showMissing) return;
        var corners = new WfcTerrain[4];
        var names = new List<string>();
        for (int code = 0; code < total; code++)
        {
            if (covered.Contains(code)) continue;
            WfcRotation.Decode(code, corners);
            names.Add(new string(corners.Select(WfcTerrains.Letter).ToArray()));
        }
        EditorGUILayout.LabelField(string.Join("  ", names), EditorStyles.wordWrappedMiniLabel);
    }
}
