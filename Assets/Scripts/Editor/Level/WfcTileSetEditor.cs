using System.Collections.Generic;
using System.Linq;
using LevelGeneration;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tile set Inspector: the corner and edge connections, each tile with a clickable 3x3 icon (corners and edges; a click
/// moves to the next connection, a right-click to the previous one), pieces with their corner grid, and which corner
/// combinations no tile covers (where the solver can get stuck).
/// </summary>
[CustomEditor(typeof(WfcTileSet))]
public class WfcTileSetEditor : Editor
{
    const float CornerSize = 20f;
    const float EdgeSize = 10f;
    const int MaxCoverageCombinations = 50000;
    static readonly string[] CornerFields = { nameof(WfcTile.SouthWest), nameof(WfcTile.SouthEast), nameof(WfcTile.NorthEast), nameof(WfcTile.NorthWest) };
    static readonly string[] EdgeFields = { nameof(WfcTile.South), nameof(WfcTile.East), nameof(WfcTile.North), nameof(WfcTile.West) };

    bool _showMissing;

    /// <summary>
    /// Saves every tile set with its connections. Tile sets made before connections get Floor, Wall and Water with Ids 0,
    /// 1 and 2 (what their tiles already store) and a Plain edge when they load; this writes that into the assets.
    /// </summary>
    [MenuItem("Tools/Level/Save Tile Sets With Connections")]
    public static void SaveAllWithConnections()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(WfcTileSet)))
        {
            var tileSet = AssetDatabase.LoadAssetAtPath<WfcTileSet>(AssetDatabase.GUIDToAssetPath(guid));
            if (tileSet == null) continue;
            tileSet.EnsureConnections();
            EditorUtility.SetDirty(tileSet);
        }
        AssetDatabase.SaveAssets();
    }

    public override void OnInspectorGUI()
    {
        var tileSet = (WfcTileSet)target;
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WfcTileSet.CornerConnections)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WfcTileSet.EdgeConnections)));
        EditorGUILayout.Space(10);
        DrawTiles(tileSet, serializedObject.FindProperty(nameof(WfcTileSet.Tiles)));
        EditorGUILayout.Space(10);
        DrawPieces(tileSet, serializedObject.FindProperty(nameof(WfcTileSet.Pieces)));
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10);
        DrawCoverage(tileSet);
    }

    void DrawTiles(WfcTileSet tileSet, SerializedProperty tiles)
    {
        EditorGUILayout.LabelField($"Tiles ({tiles.arraySize})", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Click a corner or edge for the next connection, right-click for the previous. Top row is north.", EditorStyles.miniLabel);
        int remove = -1;
        for (int i = 0; i < tiles.arraySize; i++)
        {
            var tile = tiles.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                // 3x3 icon: NW N NE / W . E / SW S SE
                float side = CornerSize * 2 + EdgeSize;
                var icon = GUILayoutUtility.GetRect(side, side, GUILayout.Width(side));
                float far = CornerSize + EdgeSize;
                CornerButton(tileSet, new Rect(icon.x, icon.y, CornerSize, CornerSize), tile.FindPropertyRelative(CornerFields[3]));
                CornerButton(tileSet, new Rect(icon.x + far, icon.y, CornerSize, CornerSize), tile.FindPropertyRelative(CornerFields[2]));
                CornerButton(tileSet, new Rect(icon.x, icon.y + far, CornerSize, CornerSize), tile.FindPropertyRelative(CornerFields[0]));
                CornerButton(tileSet, new Rect(icon.x + far, icon.y + far, CornerSize, CornerSize), tile.FindPropertyRelative(CornerFields[1]));
                EdgeButton(tileSet, new Rect(icon.x + CornerSize, icon.y, EdgeSize, CornerSize), tile.FindPropertyRelative(EdgeFields[2]));
                EdgeButton(tileSet, new Rect(icon.x + CornerSize, icon.y + far, EdgeSize, CornerSize), tile.FindPropertyRelative(EdgeFields[0]));
                EdgeButton(tileSet, new Rect(icon.x, icon.y + CornerSize, CornerSize, EdgeSize), tile.FindPropertyRelative(EdgeFields[3]));
                EdgeButton(tileSet, new Rect(icon.x + far, icon.y + CornerSize, CornerSize, EdgeSize), tile.FindPropertyRelative(EdgeFields[1]));

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

    void DrawPieces(WfcTileSet tileSet, SerializedProperty pieces)
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
                var corners = piece.FindPropertyRelative(nameof(WfcPiece.CornerIds));
                int count = (size.x + 1) * (size.y + 1);
                if (corners.arraySize != count) corners.arraySize = count;

                // Corner grid, north row first
                for (int y = size.y; y >= 0; y--)
                {
                    var row = GUILayoutUtility.GetRect((size.x + 1) * (CornerSize + 2), CornerSize + 2, GUILayout.ExpandWidth(false));
                    for (int x = 0; x <= size.x; x++)
                        CornerButton(tileSet, new Rect(row.x + x * (CornerSize + 2), row.y, CornerSize, CornerSize), corners.GetArrayElementAtIndex(x + y * (size.x + 1)));
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

    // A corner connection swatch with its first letter; clicking moves to the next connection in the list
    static void CornerButton(WfcTileSet tileSet, Rect rect, SerializedProperty id)
    {
        var connection = tileSet.CornerConnection(id.intValue);
        var ids = tileSet.CornerConnections.Where(c => c != null).Select(c => c.Id).ToList();
        string name = connection != null ? connection.Name : $"Missing Id {id.intValue}";
        ConnectionButton(rect, id, ids, connection?.Color ?? Color.magenta, $"{name} ({connection?.Terrain})", Letter(name));
    }

    static void EdgeButton(WfcTileSet tileSet, Rect rect, SerializedProperty id)
    {
        var connection = tileSet.EdgeConnections.Find(c => c != null && c.Id == id.intValue);
        var ids = tileSet.EdgeConnections.Where(c => c != null).Select(c => c.Id).ToList();
        string name = connection != null ? connection.Name : $"Missing Id {id.intValue}";
        ConnectionButton(rect, id, ids, connection?.Color ?? Color.magenta, $"{name} edge" + (connection != null && !connection.Walkable ? " (blocks walking)" : ""), "");
    }

    static void ConnectionButton(Rect rect, SerializedProperty id, List<int> ids, Color color, string tooltip, string label)
    {
        EditorGUI.DrawRect(rect, color);
        var style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.black } };
        var e = Event.current;
        bool previous = e.type == EventType.MouseDown && e.button == 1 && rect.Contains(e.mousePosition);
        if (previous) e.Use();
        bool next = GUI.Button(rect, new GUIContent(label, tooltip), style);
        if (!(previous || next) || ids.Count == 0) return;
        int step = previous ? -1 : 1;
        int index = ids.IndexOf(id.intValue);
        id.intValue = ids[((index < 0 ? 0 : index + step) % ids.Count + ids.Count) % ids.Count];
    }

    static string Letter(string name) => string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1);

    void DrawCoverage(WfcTileSet tileSet)
    {
        int connections = Mathf.Min(tileSet.CornerConnections.Count, WfcConnections.Max);
        long total = (long)connections * connections * connections * connections;
        if (total > MaxCoverageCombinations)
        {
            EditorGUILayout.HelpBox($"{connections} corner connections make {total} corner combinations, too many to check here.", MessageType.Info);
            return;
        }

        // Corner indices as a base-connections number: SW + n SE + n² NE + n³ NW
        long Key(int a, int b, int c, int d) => a + connections * (b + (long)connections * (c + (long)connections * d));
        var covered = new HashSet<long>(tileSet.BuildVariants().Select(v => Key(v.Corners.x, v.Corners.y, v.Corners.z, v.Corners.w)));
        long missing = total - covered.Count;
        if (missing == 0)
        {
            EditorGUILayout.HelpBox($"All {total} corner combinations have a tile (counting rotations).", MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox($"{missing} of {total} corner combinations have no tile. Wherever the rules or neighbors need one, " +
                                "the solver gets stuck and retries. That's fine for connections that never meet. Listed SW SE NE NW.", MessageType.Warning);
        _showMissing = EditorGUILayout.Foldout(_showMissing, "Missing combinations", true);
        if (!_showMissing) return;
        string Name(int index) => tileSet.CornerConnections[index]?.Name ?? "?";
        var names = new List<string>();
        for (int d = 0; d < connections && names.Count < 200; d++)
            for (int c = 0; c < connections && names.Count < 200; c++)
                for (int b = 0; b < connections && names.Count < 200; b++)
                    for (int a = 0; a < connections && names.Count < 200; a++)
                        if (!covered.Contains(Key(a, b, c, d))) names.Add($"{Name(a)} {Name(b)} {Name(c)} {Name(d)}");
        EditorGUILayout.LabelField(string.Join("\n", names) + (missing > names.Count ? $"\n... and {missing - names.Count} more" : ""), EditorStyles.wordWrappedMiniLabel);
    }
}
