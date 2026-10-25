using System.Diagnostics;
using System.Linq;
using LevelGeneration;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Level layout Inspector and Scene view tools. Brushes: paint corner rules (Walkable / Wall / Water / Any), force a
/// tile on a cell, or place a multi-cell piece. Generate Preview runs the same solver as the game and draws the
/// result as gizmo blocks; Save Layout keeps it so the game spawns exactly that level.
/// </summary>
[CustomEditor(typeof(LevelLayoutAuthoring))]
public class LevelLayoutEditor : Editor
{
    enum Brush { Off, Corners, Tile, Piece, Start, Spawner }

    static readonly string[] BrushNames = { "Off", "Corners", "Tile", "Piece", "Start", "Spawner" };
    static readonly string[] RuleNames = { "Any", "Walkable", "Wall", "Water" };
    static readonly string[] RotationNames = { "0°", "90°", "180°", "270°" };
    static readonly Color GridColor = new Color(1f, 1f, 1f, 0.2f);
    static readonly Color ForcedColor = new Color(1f, 0.85f, 0.2f);
    static readonly Color PieceColor = new Color(1f, 0.5f, 0.15f);
    static readonly Color StartColor = new Color(0.3f, 1f, 0.4f);
    static readonly Color SpawnerColor = new Color(1f, 0.25f, 0.25f);

    // Static so the brush survives reselecting the layout
    static Brush _brush;
    static WfcCornerRule _rule = WfcCornerRule.Wall;
    static int _tile, _piece, _rotation;

    string _status;
    MessageType _statusType;
    Vector2Int _hover;
    bool _hoverValid;
    Vector2Int? _rectStart;
    bool _erasing;

    LevelLayoutAuthoring Layout => (LevelLayoutAuthoring)target;

    void OnDisable()
    {
        Tools.hidden = false;
    }

    // ---------- Inspector ----------

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10);
        DrawBrushGUI();
        EditorGUILayout.Space(10);
        DrawGenerationGUI();
    }

    void DrawBrushGUI()
    {
        var layout = Layout;
        EditorGUILayout.LabelField("Scene View Brush", EditorStyles.boldLabel);
        var brush = (Brush)GUILayout.Toolbar((int)_brush, BrushNames);
        if (brush != _brush)
        {
            _brush = brush;
            SceneView.RepaintAll();
        }
        // The move/rotate gizmo would steal clicks while painting
        Tools.hidden = _brush != Brush.Off;

        switch (_brush)
        {
            case Brush.Corners:
                _rule = (WfcCornerRule)GUILayout.Toolbar((int)_rule, RuleNames);
                EditorGUILayout.HelpBox("Click or drag to paint corners. Shift+drag fills a rectangle. Right-click sets Any.", MessageType.None);
                break;
            case Brush.Tile:
                if (!HasTiles(layout)) break;
                _tile = EditorGUILayout.Popup("Tile", Mathf.Clamp(_tile, 0, layout.TileSet.Tiles.Count - 1),
                                              layout.TileSet.Tiles.Select((t, i) => $"{i}: {t.Name}").ToArray());
                _rotation = GUILayout.Toolbar(_rotation, RotationNames);
                EditorGUILayout.HelpBox("Click a cell to force this tile there, right-click to remove. R rotates.", MessageType.None);
                break;
            case Brush.Piece:
                if (layout.TileSet == null || layout.TileSet.Pieces.Count == 0)
                {
                    EditorGUILayout.HelpBox("The tile set has no pieces.", MessageType.Info);
                    break;
                }
                _piece = EditorGUILayout.Popup("Piece", Mathf.Clamp(_piece, 0, layout.TileSet.Pieces.Count - 1),
                                               layout.TileSet.Pieces.Select((p, i) => $"{i}: {p.Name}").ToArray());
                _rotation = GUILayout.Toolbar(_rotation, RotationNames);
                EditorGUILayout.HelpBox("Click a cell to place the piece with its south-west corner there, right-click a piece to remove it. R rotates.", MessageType.None);
                break;
            case Brush.Start:
                EditorGUILayout.HelpBox("Click a cell to make it the player start. Its corners are always Walkable.", MessageType.None);
                break;
            case Brush.Spawner:
                EditorGUILayout.HelpBox("Click a cell to add an enemy spawner there, right-click to remove it. Set each spawner's enemies " +
                                        "in the Enemy Spawners list above.", MessageType.None);
                break;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Clear corner rules") && EditorUtility.DisplayDialog("Clear corner rules", "Set every corner back to Any?", "Clear", "Cancel"))
                Modify("Clear corner rules", l => l.ClearCornerRules());
            if (GUILayout.Button("Clear forced tiles")) Modify("Clear forced tiles", l => l.ForcedTiles.Clear());
            if (GUILayout.Button("Clear pieces")) Modify("Clear pieces", l => l.Pieces.Clear());
            if (GUILayout.Button("Clear spawners")) Modify("Clear enemy spawners", l => l.EnemySpawners.Clear());
        }
    }

    static bool HasTiles(LevelLayoutAuthoring layout)
    {
        if (layout.TileSet != null && layout.TileSet.Tiles.Count > 0) return true;
        EditorGUILayout.HelpBox("Assign a tile set with tiles.", MessageType.Info);
        return false;
    }

    void DrawGenerationGUI()
    {
        var layout = Layout;
        EditorGUILayout.LabelField("Generation", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Preview")) GeneratePreview(layout);
            if (GUILayout.Button("Reroll"))
            {
                Modify("Reroll level seed", l => l.Seed++);
                GeneratePreview(layout);
            }
            using (new EditorGUI.DisabledScope(layout.PreviewCells == null))
                if (GUILayout.Button("Clear Preview"))
                {
                    layout.PreviewCells = null;
                    _status = null;
                    SceneView.RepaintAll();
                }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(layout.PreviewCells == null))
                if (GUILayout.Button("Save Layout"))
                    Modify("Save level layout", l =>
                    {
                        l.SetSavedCells((int[])l.PreviewCells.Clone());
                        l.Mode = LevelGenerationMode.UseSavedLayout;
                    });
            using (new EditorGUI.DisabledScope(!layout.HasSavedLayout))
                if (GUILayout.Button("Clear Saved Layout"))
                    Modify("Clear saved level layout", l =>
                    {
                        l.SetSavedCells(null);
                        l.Mode = LevelGenerationMode.GenerateAtRuntime;
                    });
        }

        string saved = layout.HasSavedLayout ? "A layout is saved" : "No saved layout";
        EditorGUILayout.LabelField($"{saved}. Mode: {ObjectNames.NicifyVariableName(layout.Mode.ToString())}.", EditorStyles.miniLabel);
        if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, _statusType);
    }

    void Modify(string undoName, System.Action<LevelLayoutAuthoring> change)
    {
        Undo.RecordObject(Layout, undoName);
        change(Layout);
        EditorUtility.SetDirty(Layout);
        SceneView.RepaintAll();
    }

    void GeneratePreview(LevelLayoutAuthoring layout)
    {
        var compiled = WfcLayoutCompiler.Compile(layout);
        if (compiled.Variants.Count == 0)
        {
            SetStatus("The tile set has no tiles.", MessageType.Error);
            return;
        }

        int cells = layout.Width * layout.Height;
        var job = new WfcSolveJob
        {
            Width = layout.Width,
            Height = layout.Height,
            VariantCorners = new NativeArray<byte>(compiled.Variants.SelectMany(v => new[] { (byte)v.SouthWest, (byte)v.SouthEast, (byte)v.NorthEast, (byte)v.NorthWest }).ToArray(), Allocator.TempJob),
            VariantWeights = new NativeArray<float>(compiled.Variants.Select(v => v.Weight).ToArray(), Allocator.TempJob),
            InitialCornerMasks = new NativeArray<byte>(compiled.CornerMasks, Allocator.TempJob),
            InitialCells = new NativeArray<int>(compiled.InitialCells, Allocator.TempJob),
            Seed = layout.Seed,
            MaxAttempts = layout.MaxAttempts,
            Result = new NativeArray<int>(cells, Allocator.TempJob),
            Attempts = new NativeReference<int>(Allocator.TempJob),
        };

        var timer = Stopwatch.StartNew();
        job.Schedule().Complete();
        timer.Stop();

        int attempts = job.Attempts.Value;
        if (attempts > 0) layout.PreviewCells = compiled.ToPackedCells(job.Result.ToArray());
        job.VariantCorners.Dispose();
        job.VariantWeights.Dispose();
        job.InitialCornerMasks.Dispose();
        job.InitialCells.Dispose();
        job.Result.Dispose();
        job.Attempts.Dispose();

        string warnings = compiled.Warnings.Count > 0 ? "\n" + string.Join("\n", compiled.Warnings) : "";
        if (attempts > 0)
            SetStatus($"Generated with seed {layout.Seed}{(attempts > 1 ? $" (attempt {attempts})" : "")} in {timer.ElapsedMilliseconds} ms.{warnings}",
                      compiled.Warnings.Count > 0 ? MessageType.Warning : MessageType.Info);
        else
            SetStatus($"No solution after {layout.MaxAttempts} attempts with seed {layout.Seed}. The rules may conflict, or the tile set " +
                      $"lacks tiles for some corner combinations (see its coverage).{warnings}", MessageType.Error);
        SceneView.RepaintAll();
    }

    void SetStatus(string text, MessageType type)
    {
        _status = text;
        _statusType = type;
        Repaint();
    }

    // ---------- Scene view ----------

    void OnSceneGUI()
    {
        var layout = Layout;
        var e = Event.current;

        if (e.type == EventType.Repaint) DrawLayout(layout);
        if (_brush == Brush.Off) return;

        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(id);
            return;
        }
        if (e.alt) return;   // Alt+drag orbits the camera

        UpdateHover(layout, e.mousePosition);

        switch (e.type)
        {
            case EventType.KeyDown when e.keyCode == KeyCode.R && (_brush == Brush.Tile || _brush == Brush.Piece):
                _rotation = (_rotation + 1) % 4;
                e.Use();
                Repaint();
                break;

            case EventType.MouseDown when (e.button == 0 || e.button == 1) && _hoverValid:
                GUIUtility.hotControl = id;
                _erasing = e.button == 1;
                if (_brush == Brush.Corners && e.shift) _rectStart = _hover;
                else Apply(layout, _hover);
                e.Use();
                break;

            case EventType.MouseDrag when GUIUtility.hotControl == id:
                if (_brush == Brush.Corners && _rectStart == null && _hoverValid) Apply(layout, _hover);
                e.Use();
                break;

            case EventType.MouseUp when GUIUtility.hotControl == id:
                if (_rectStart != null && _hoverValid) FillCorners(layout, _rectStart.Value, _hover);
                _rectStart = null;
                GUIUtility.hotControl = 0;
                e.Use();
                break;

            case EventType.MouseMove:
                SceneView.RepaintAll();
                break;
        }
    }

    // The corner (corner brush) or cell (tile/piece brush) under the mouse
    void UpdateHover(LevelLayoutAuthoring layout, Vector2 mousePosition)
    {
        var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        var plane = new Plane(Vector3.up, layout.transform.position);
        _hoverValid = false;
        if (!plane.Raycast(ray, out float distance)) return;

        var local = (ray.GetPoint(distance) - layout.transform.position) / layout.TileSize;
        if (_brush == Brush.Corners)
        {
            _hover = new Vector2Int(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.z));
            _hoverValid = _hover.x >= 0 && _hover.y >= 0 && _hover.x <= layout.Width && _hover.y <= layout.Height;
        }
        else
        {
            _hover = new Vector2Int(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.z));
            _hoverValid = _hover.x >= 0 && _hover.y >= 0 && _hover.x < layout.Width && _hover.y < layout.Height;
        }
    }

    void Apply(LevelLayoutAuthoring layout, Vector2Int at)
    {
        switch (_brush)
        {
            case Brush.Corners:
                var rule = _erasing ? WfcCornerRule.Any : _rule;
                if (layout.GetCornerRule(at.x, at.y) == rule) return;
                Modify("Paint corner", l => l.SetCornerRule(at.x, at.y, rule));
                break;

            case Brush.Tile:
                if (layout.TileSet == null || layout.TileSet.Tiles.Count == 0) return;
                Modify(_erasing ? "Remove forced tile" : "Force tile", l =>
                {
                    l.ForcedTiles.RemoveAll(f => f.Cell == at);
                    if (!_erasing) l.ForcedTiles.Add(new LevelLayoutAuthoring.ForcedTile { Cell = at, Tile = _tile, Rotation = _rotation });
                });
                break;

            case Brush.Piece:
                if (layout.TileSet == null || layout.TileSet.Pieces.Count == 0) return;
                if (_erasing)
                {
                    Modify("Remove piece", l => l.Pieces.RemoveAll(p => Covers(l, p, at)));
                    break;
                }
                var size = RotatedSize(layout.TileSet.Pieces[_piece], _rotation);
                Modify("Place piece", l =>
                {
                    // Replace pieces the new one would overlap
                    l.Pieces.RemoveAll(p => Overlaps(l, p, at, size));
                    l.Pieces.Add(new LevelLayoutAuthoring.PlacedPiece { Cell = at, Piece = _piece, Rotation = _rotation });
                });
                break;

            case Brush.Start:
                if (_erasing || layout.PlayerStart == at) return;
                Modify("Move player start", l => l.PlayerStart = at);
                break;

            case Brush.Spawner:
                if (_erasing)
                {
                    Modify("Remove enemy spawner", l => l.EnemySpawners.RemoveAll(s => s != null && s.Cell == at));
                    break;
                }
                if (layout.EnemySpawners.Exists(s => s != null && s.Cell == at)) return;
                Modify("Add enemy spawner", l => l.EnemySpawners.Add(new LevelLayoutAuthoring.SpawnerMarker { Cell = at }));
                break;
        }
    }

    void FillCorners(LevelLayoutAuthoring layout, Vector2Int from, Vector2Int to)
    {
        var rule = _erasing ? WfcCornerRule.Any : _rule;
        Modify("Fill corners", l =>
        {
            for (int y = Mathf.Min(from.y, to.y); y <= Mathf.Max(from.y, to.y); y++)
                for (int x = Mathf.Min(from.x, to.x); x <= Mathf.Max(from.x, to.x); x++)
                    l.SetCornerRule(x, y, rule);
        });
    }

    static Vector2Int RotatedSize(WfcPiece piece, int rotation) =>
        rotation % 2 == 0 ? piece.Size : new Vector2Int(piece.Size.y, piece.Size.x);

    static bool Covers(LevelLayoutAuthoring layout, LevelLayoutAuthoring.PlacedPiece placed, Vector2Int cell) =>
        Overlaps(layout, placed, cell, Vector2Int.one);

    static bool Overlaps(LevelLayoutAuthoring layout, LevelLayoutAuthoring.PlacedPiece placed, Vector2Int cell, Vector2Int size)
    {
        if (layout.TileSet == null || placed.Piece < 0 || placed.Piece >= layout.TileSet.Pieces.Count) return false;
        var placedSize = RotatedSize(layout.TileSet.Pieces[placed.Piece], placed.Rotation);
        return placed.Cell.x < cell.x + size.x && cell.x < placed.Cell.x + placedSize.x &&
               placed.Cell.y < cell.y + size.y && cell.y < placed.Cell.y + placedSize.y;
    }

    void DrawLayout(LevelLayoutAuthoring layout)
    {
        float size = layout.TileSize;
        var lift = Vector3.up * 0.05f;

        // Grid
        var lines = new Vector3[(layout.Width + layout.Height + 2) * 2];
        int n = 0;
        for (int x = 0; x <= layout.Width; x++) { lines[n++] = layout.CornerPosition(x, 0) + lift; lines[n++] = layout.CornerPosition(x, layout.Height) + lift; }
        for (int y = 0; y <= layout.Height; y++) { lines[n++] = layout.CornerPosition(0, y) + lift; lines[n++] = layout.CornerPosition(layout.Width, y) + lift; }
        Handles.color = GridColor;
        Handles.DrawLines(lines);

        // Corner rules
        for (int y = 0; y <= layout.Height; y++)
            for (int x = 0; x <= layout.Width; x++)
            {
                var rule = layout.GetCornerRule(x, y);
                var position = layout.CornerPosition(x, y) + lift;
                if (rule == WfcCornerRule.Any)
                {
                    if (_brush != Brush.Corners) continue;
                    Handles.color = new Color(1f, 1f, 1f, 0.35f);
                    Handles.DrawWireDisc(position, Vector3.up, size * 0.08f);
                }
                else
                {
                    Handles.color = WfcTerrains.Color((WfcTerrain)((int)rule - 1));
                    Handles.DrawSolidDisc(position, Vector3.up, size * 0.12f);
                }
            }

        // Forced tiles and pieces
        foreach (var forced in layout.ForcedTiles)
        {
            DrawCellRect(layout, forced.Cell, Vector2Int.one, ForcedColor);
            string name = layout.TileSet != null && forced.Tile >= 0 && forced.Tile < layout.TileSet.Tiles.Count ? layout.TileSet.Tiles[forced.Tile].Name : "?";
            Handles.Label(layout.CellCenter(forced.Cell.x, forced.Cell.y) + Vector3.up, $"{name} {forced.Rotation * 90}°", EditorStyles.miniBoldLabel);
        }
        foreach (var placed in layout.Pieces)
        {
            if (layout.TileSet == null || placed.Piece < 0 || placed.Piece >= layout.TileSet.Pieces.Count) continue;
            var piece = layout.TileSet.Pieces[placed.Piece];
            DrawCellRect(layout, placed.Cell, RotatedSize(piece, placed.Rotation), PieceColor);
            Handles.Label(layout.CellCenter(placed.Cell.x, placed.Cell.y) + Vector3.up, $"{piece.Name} {placed.Rotation * 90}°", EditorStyles.miniBoldLabel);
        }

        // Player start and enemy spawners (trigger radius as a ring)
        DrawCellRect(layout, layout.PlayerStart, Vector2Int.one, StartColor);
        Handles.Label(layout.CellCenter(layout.PlayerStart.x, layout.PlayerStart.y) + Vector3.up * 1.5f, "Player Start", EditorStyles.boldLabel);
        foreach (var spawner in layout.EnemySpawners)
        {
            if (spawner == null) continue;
            var center = layout.CellCenter(spawner.Cell.x, spawner.Cell.y) + Vector3.up * 0.1f;
            Handles.color = SpawnerColor;
            Handles.DrawSolidDisc(center, Vector3.up, Mathf.Max(0.3f, spawner.SpawnRadius));
            Handles.color = new Color(SpawnerColor.r, SpawnerColor.g, SpawnerColor.b, 0.35f);
            Handles.DrawWireDisc(center, Vector3.up, spawner.TriggerRadius);
            int count = 0;
            foreach (var entry in spawner.Group) if (entry?.Definition != null) count += entry.Count;
            Handles.Label(center + Vector3.up * 1.5f, $"Spawner {spawner.Cell}: {count} enemies", EditorStyles.miniBoldLabel);
        }

        DrawHover(layout);
    }

    void DrawHover(LevelLayoutAuthoring layout)
    {
        if (_brush == Brush.Off || !_hoverValid) return;
        float size = layout.TileSize;
        switch (_brush)
        {
            case Brush.Corners:
                var color = _rule == WfcCornerRule.Any ? Color.white : WfcTerrains.Color((WfcTerrain)((int)_rule - 1));
                if (_rectStart != null)
                {
                    var from = _rectStart.Value;
                    var min = new Vector2Int(Mathf.Min(from.x, _hover.x), Mathf.Min(from.y, _hover.y));
                    var max = new Vector2Int(Mathf.Max(from.x, _hover.x), Mathf.Max(from.y, _hover.y));
                    var a = layout.CornerPosition(min.x, min.y);
                    var b = layout.CornerPosition(max.x, max.y);
                    Handles.DrawSolidRectangleWithOutline(new[] { a, new Vector3(b.x, a.y, a.z), b, new Vector3(a.x, a.y, b.z) },
                                                          new Color(color.r, color.g, color.b, 0.15f), color);
                }
                Handles.color = color;
                Handles.DrawWireDisc(layout.CornerPosition(_hover.x, _hover.y) + Vector3.up * 0.1f, Vector3.up, size * 0.16f);
                break;
            case Brush.Tile:
            case Brush.Start:
            case Brush.Spawner:
                DrawCellRect(layout, _hover, Vector2Int.one, Color.white);
                break;
            case Brush.Piece:
                if (layout.TileSet == null || layout.TileSet.Pieces.Count == 0) break;
                DrawCellRect(layout, _hover, RotatedSize(layout.TileSet.Pieces[Mathf.Clamp(_piece, 0, layout.TileSet.Pieces.Count - 1)], _rotation), Color.white);
                break;
        }
    }

    static void DrawCellRect(LevelLayoutAuthoring layout, Vector2Int cell, Vector2Int size, Color color)
    {
        var a = layout.CornerPosition(cell.x, cell.y) + Vector3.up * 0.08f;
        var b = layout.CornerPosition(cell.x + size.x, cell.y + size.y) + Vector3.up * 0.08f;
        Handles.DrawSolidRectangleWithOutline(new[] { a, new Vector3(b.x, a.y, a.z), b, new Vector3(a.x, a.y, b.z) },
                                              new Color(color.r, color.g, color.b, 0.12f), color);
    }
}
