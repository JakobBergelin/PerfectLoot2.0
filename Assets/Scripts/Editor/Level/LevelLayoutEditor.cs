using System.Collections.Generic;
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
    static readonly Color CarvedColor = new Color(1f, 0.6f, 0.1f);
    static readonly Color FilledColor = new Color(0.5f, 0.05f, 0.05f);
    static readonly Color PlacedPropColor = new Color(0.2f, 0.85f, 0.9f);

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

    // ---------- Scene view visibility (remembered in EditorPrefs) ----------

    const string ViewPrefs = LevelLayoutView.Prefs;
    static bool _viewLoaded, _viewFoldout = true;

    // The checkboxes themselves load in LevelLayoutView; only the foldout state lives here
    static void LoadView()
    {
        if (_viewLoaded) return;
        _viewLoaded = true;
        _viewFoldout = EditorPrefs.GetBool(ViewPrefs + "Foldout", true);
    }

    void DrawViewGUI()
    {
        LoadView();
        bool foldout = EditorGUILayout.Foldout(_viewFoldout, "Scene View", true);
        if (foldout != _viewFoldout) EditorPrefs.SetBool(ViewPrefs + "Foldout", _viewFoldout = foldout);
        if (!_viewFoldout) return;

        EditorGUI.indentLevel++;
        LevelLayoutView.Grid = ViewToggle("Grid", "Grid lines", LevelLayoutView.Grid);
        LevelLayoutView.CornerRules = ViewToggle("CornerRules", "Corner rules", LevelLayoutView.CornerRules, "Painted corners (Any corners still show while painting)");
        LevelLayoutView.TilePreview = ViewToggle("TilePreview", "Tile preview", LevelLayoutView.TilePreview, "The previewed or saved layout as colored patches");
        LevelLayoutView.Props = ViewToggle("Props", "Placed props", LevelLayoutView.Props);
        LevelLayoutView.Spawners = ViewToggle("Spawners", "Spawners", LevelLayoutView.Spawners, "Hand-placed and placed enemy spawners");
        LevelLayoutView.TriggerRings = ViewToggle("TriggerRings", "Trigger rings", LevelLayoutView.TriggerRings);
        LevelLayoutView.Labels = ViewToggle("Labels", "Labels", LevelLayoutView.Labels, "Names on forced tiles, pieces, spawners and placements (slow with many)");
        LevelLayoutView.Repairs = ViewToggle("Repairs", "Repairs", LevelLayoutView.Repairs, "Corners the connectivity repair carved (orange) or filled (dark red)");
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(4);
    }

    static bool ViewToggle(string key, string label, bool value, string tooltip = null)
    {
        bool changed = EditorGUILayout.Toggle(new GUIContent(label, tooltip), value);
        if (changed == value) return value;
        EditorPrefs.SetBool(ViewPrefs + key, changed);
        SceneView.RepaintAll();
        return changed;
    }

    void DrawGenerationGUI()
    {
        var layout = Layout;
        DrawViewGUI();
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
                    layout.PreviewRepairs = null;
                    layout.PreviewPlacements = null;
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

        // A spawner reaching past the streamed area would spawn enemies over floor that isn't built yet
        float reach = 0f;
        foreach (var spawner in layout.EnemySpawners)
            if (spawner != null) reach = Mathf.Max(reach, spawner.TriggerRadius + spawner.SpawnRadius);
        if (reach > layout.LoadDistance)
            EditorGUILayout.HelpBox($"A spawner reaches {reach:0.#} m (trigger + spawn radius), beyond Load Distance ({layout.LoadDistance:0.#} m). " +
                                    "Its enemies could spawn where the floor isn't built yet.", MessageType.Warning);
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
            Connect = layout.ConnectWalkableAreas,
            StartCorner = compiled.StartCorner,
            FillPocketsSmallerThan = layout.FillPocketsSmallerThan,
            LockedCorners = new NativeArray<byte>(compiled.LockedCorners, Allocator.TempJob),
            Repairs = new NativeArray<byte>(compiled.CornerMasks.Length, Allocator.TempJob),
            UnconnectedPockets = new NativeReference<int>(Allocator.TempJob),
        };

        var timer = Stopwatch.StartNew();
        job.Schedule().Complete();
        timer.Stop();

        int attempts = job.Attempts.Value;
        int carved = 0, filled = 0, unconnected = job.UnconnectedPockets.Value;
        if (attempts > 0)
        {
            layout.PreviewCells = compiled.ToPackedCells(job.Result.ToArray());
            layout.PreviewRepairs = job.Repairs.ToArray();
            foreach (var repair in layout.PreviewRepairs)
            {
                if (repair == WfcSolver.RepairCarved) carved++;
                else if (repair == WfcSolver.RepairFilled) filled++;
            }
        }
        job.VariantCorners.Dispose();
        job.VariantWeights.Dispose();
        job.InitialCornerMasks.Dispose();
        job.InitialCells.Dispose();
        job.Result.Dispose();
        job.Attempts.Dispose();
        job.LockedCorners.Dispose();
        job.Repairs.Dispose();
        job.UnconnectedPockets.Dispose();

        string connected = !layout.ConnectWalkableAreas ? "" : $" Connected: {carved} corners carved, {filled} filled" +
                           (unconnected > 0 ? $"; {unconnected} pockets couldn't be reached without changing painted or forced corners." : ".");
        string warnings = compiled.Warnings.Count > 0 ? "\n" + string.Join("\n", compiled.Warnings) : "";
        string placed = attempts > 0 ? PreviewPlacement(layout, compiled) : "";
        if (attempts > 0)
            SetStatus($"Generated with seed {layout.Seed}{(attempts > 1 ? $" (attempt {attempts})" : "")} in {timer.ElapsedMilliseconds} ms.{connected}{placed}{warnings}",
                      compiled.Warnings.Count > 0 || unconnected > 0 ? MessageType.Warning : MessageType.Info);
        else
            SetStatus($"No solution after {layout.MaxAttempts} attempts with seed {layout.Seed}. The rules may conflict, or the tile set " +
                      $"lacks tiles for some corner combinations (see its coverage).{warnings}", MessageType.Error);
        SceneView.RepaintAll();
    }

    // Runs the placement rules on the preview, like the game does; returns a status line with counts per rule
    static string PreviewPlacement(LevelLayoutAuthoring layout, WfcCompiledLayout compiled)
    {
        layout.PreviewPlacements = null;
        var set = layout.Placements;
        if (set == null || set.Rules.Count == 0 || layout.PreviewCells == null || layout.TileSet == null) return "";

        var tileCorners = new byte[layout.TileSet.Tiles.Count * 4];
        for (int t = 0; t < layout.TileSet.Tiles.Count; t++)
        {
            var terrain = layout.TileSet.Tiles[t]?.Corners;
            for (int k = 0; k < 4; k++) tileCorners[t * 4 + k] = terrain != null ? (byte)terrain[k] : LevelPlacement.Unknown;
        }
        var cornerTerrain = LevelPlacement.CornerTerrain(layout.Width, layout.Height, layout.PreviewCells, tileCorners, compiled.CornerMasks);
        var occupied = compiled.InitialCells.Select(cell => cell == WfcSolver.Occupied).ToArray();
        var rules = set.Rules.Select(rule => rule == null ? new PlacementRuleData() : new PlacementRuleData
        {
            Kind = rule.Kind, Spacing = rule.Spacing, ClearRadius = rule.ClearRadius,
            MinDistanceFromStart = rule.MinDistanceFromStart, MaxSlope = rule.MaxSlope,
        }).ToArray();

        var job = new LevelPlacementJob
        {
            Width = layout.Width, Height = layout.Height, TileSize = layout.TileSize, Seed = layout.Seed,
            StartCorner = compiled.StartCorner, Start = new Unity.Mathematics.float2(layout.PlayerStart.x + 0.5f, layout.PlayerStart.y + 0.5f),
            CornerTerrain = new NativeArray<byte>(cornerTerrain, Allocator.TempJob),
            OccupiedCells = new NativeArray<bool>(occupied, Allocator.TempJob),
            BentCorners = new NativeArray<Unity.Mathematics.float3>(layout.BentCorners(), Allocator.TempJob),
            Rules = new NativeArray<PlacementRuleData>(rules, Allocator.TempJob),
            Points = new NativeList<PlacementPoint>(Allocator.TempJob),
        };
        job.Schedule().Complete();
        layout.PreviewPlacements = job.Points.AsArray().ToArray();
        job.CornerTerrain.Dispose();
        job.OccupiedCells.Dispose();
        job.BentCorners.Dispose();
        job.Rules.Dispose();
        job.Points.Dispose();

        var counts = set.Rules.Select((rule, i) => $"{(string.IsNullOrEmpty(rule?.Name) ? $"Rule {i}" : rule.Name)}: {layout.PreviewPlacements.Count(p => p.Rule == i)}");
        return "\nPlaced " + string.Join(", ", counts) + ".";
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

        LoadView();
        if (e.type == EventType.Repaint) DrawLayout(layout);
        if (_brush == Brush.Off) return;

        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(id);
            return;
        }
        if (e.alt) return;   // Alt+drag orbits the camera

        // Only mouse events move the hover; a move repaints only when it lands on a different cell or corner
        if (e.isMouse)
        {
            var previous = (_hover, _hoverValid);
            UpdateHover(layout, e.mousePosition);
            if (e.type == EventType.MouseMove && (_hover, _hoverValid) != previous) SceneView.RepaintAll();
        }

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

        }
    }

    // The corner (corner brush) or cell (other brushes) under the mouse, on the bent grid seen from above
    void UpdateHover(LevelLayoutAuthoring layout, Vector2 mousePosition)
    {
        var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        var plane = new Plane(Vector3.up, layout.transform.position);
        _hoverValid = false;
        if (!plane.Raycast(ray, out float distance)) return;

        var point = ray.GetPoint(distance);
        _hoverValid = _brush == Brush.Corners ? layout.TryPickCorner(point, out _hover) : layout.TryPickCell(point, out _hover);
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

    // Everything is behind its Scene View checkbox (LevelLayoutView); labels are the slowest, so they're off by default
    void DrawLayout(LevelLayoutAuthoring layout)
    {
        float size = layout.TileSize;
        var lift = Vector3.up * 0.05f;
        bool labels = LevelLayoutView.Labels;

        // Grid, one segment per cell edge so it follows the bend
        if (LevelLayoutView.Grid)
        {
            var lines = new Vector3[((layout.Width + 1) * layout.Height + (layout.Height + 1) * layout.Width) * 2];
            int n = 0;
            for (int y = 0; y <= layout.Height; y++)
                for (int x = 0; x <= layout.Width; x++)
                {
                    var corner = layout.CornerPosition(x, y) + lift;
                    if (x < layout.Width) { lines[n++] = corner; lines[n++] = layout.CornerPosition(x + 1, y) + lift; }
                    if (y < layout.Height) { lines[n++] = corner; lines[n++] = layout.CornerPosition(x, y + 1) + lift; }
                }
            Handles.color = GridColor;
            Handles.DrawLines(lines);
        }

        // Corner rules (and, while painting corners, the Any corners)
        if (LevelLayoutView.CornerRules || _brush == Brush.Corners)
            for (int y = 0; y <= layout.Height; y++)
                for (int x = 0; x <= layout.Width; x++)
                {
                    var rule = layout.GetCornerRule(x, y);
                    if (rule == WfcCornerRule.Any)
                    {
                        if (_brush != Brush.Corners) continue;
                        Handles.color = new Color(1f, 1f, 1f, 0.35f);
                        Handles.DrawWireDisc(layout.CornerPosition(x, y) + lift, Vector3.up, size * 0.08f);
                    }
                    else
                    {
                        Handles.color = WfcTerrains.Color((WfcTerrain)((int)rule - 1));
                        Handles.DrawSolidDisc(layout.CornerPosition(x, y) + lift, Vector3.up, size * 0.12f);
                    }
                }

        // Forced tiles and pieces
        foreach (var forced in layout.ForcedTiles)
        {
            DrawCellRect(layout, forced.Cell, Vector2Int.one, ForcedColor);
            if (!labels) continue;
            string name = layout.TileSet != null && forced.Tile >= 0 && forced.Tile < layout.TileSet.Tiles.Count ? layout.TileSet.Tiles[forced.Tile].Name : "?";
            Handles.Label(layout.CellCenter(forced.Cell.x, forced.Cell.y) + Vector3.up, $"{name} {forced.Rotation * 90}°", EditorStyles.miniBoldLabel);
        }
        foreach (var placed in layout.Pieces)
        {
            if (layout.TileSet == null || placed.Piece < 0 || placed.Piece >= layout.TileSet.Pieces.Count) continue;
            var piece = layout.TileSet.Pieces[placed.Piece];
            DrawCellRect(layout, placed.Cell, RotatedSize(piece, placed.Rotation), PieceColor);
            if (labels) Handles.Label(layout.CellCenter(placed.Cell.x, placed.Cell.y) + Vector3.up, $"{piece.Name} {placed.Rotation * 90}°", EditorStyles.miniBoldLabel);
        }

        // Player start (always: it's one cell) and hand-placed spawners
        DrawCellRect(layout, layout.PlayerStart, Vector2Int.one, StartColor);
        Handles.Label(layout.CellCenter(layout.PlayerStart.x, layout.PlayerStart.y) + Vector3.up * 1.5f, "Player Start", EditorStyles.boldLabel);
        if (LevelLayoutView.Spawners)
            foreach (var spawner in layout.EnemySpawners)
            {
                if (spawner == null) continue;
                var center = layout.CellCenter(spawner.Cell.x, spawner.Cell.y) + Vector3.up * 0.1f;
                Handles.color = SpawnerColor;
                Handles.DrawSolidDisc(center, Vector3.up, Mathf.Max(0.3f, spawner.SpawnRadius));
                if (LevelLayoutView.TriggerRings)
                {
                    Handles.color = new Color(SpawnerColor.r, SpawnerColor.g, SpawnerColor.b, 0.35f);
                    Handles.DrawWireDisc(center, Vector3.up, spawner.TriggerRadius);
                }
                if (!labels) continue;
                int count = 0;
                foreach (var entry in spawner.Group) if (entry?.Definition != null) count += entry.Count;
                Handles.Label(center + Vector3.up * 1.5f, $"Spawner {spawner.Cell}: {count} enemies", EditorStyles.miniBoldLabel);
            }

        // Procedural placements in the preview: props as a disc with a yaw line, spawners as a bigger disc with their trigger ring
        if (layout.PreviewPlacements != null && layout.Placements != null && (LevelLayoutView.Props || LevelLayoutView.Spawners))
            foreach (var placed in layout.PreviewPlacements)
            {
                if (placed.Rule < 0 || placed.Rule >= layout.Placements.Rules.Count || layout.Placements.Rules[placed.Rule] == null) continue;
                var rule = layout.Placements.Rules[placed.Rule];
                bool spawner = rule.Kind == PlacementKind.EnemySpawner;
                if (spawner ? !LevelLayoutView.Spawners : !LevelLayoutView.Props) continue;
                var position = layout.GridToWorld(placed.Grid.x, placed.Grid.y) + Vector3.up * 0.15f;
                Handles.color = spawner ? SpawnerColor : PlacedPropColor;
                Handles.DrawSolidDisc(position, Vector3.up, spawner ? 1.2f : 0.6f);
                var facing = layout.Yaw * Quaternion.Euler(0f, placed.Yaw * Mathf.Rad2Deg, 0f) * Vector3.forward;
                Handles.DrawLine(position, position + facing * (spawner ? 2f : 1.2f));
                if (spawner && LevelLayoutView.TriggerRings)
                {
                    Handles.color = new Color(SpawnerColor.r, SpawnerColor.g, SpawnerColor.b, 0.2f);
                    Handles.DrawWireDisc(position, Vector3.up, rule.TriggerRadius);
                }
                if (labels) Handles.Label(position + Vector3.up * 1.2f, rule.Name, EditorStyles.miniLabel);
            }

        // What the connectivity repair changed in the preview
        if (LevelLayoutView.Repairs && layout.PreviewRepairs != null && layout.PreviewRepairs.Length == (layout.Width + 1) * (layout.Height + 1))
            for (int y = 0; y <= layout.Height; y++)
                for (int x = 0; x <= layout.Width; x++)
                {
                    byte repair = layout.PreviewRepairs[x + y * (layout.Width + 1)];
                    if (repair == WfcSolver.RepairNone) continue;
                    Handles.color = repair == WfcSolver.RepairCarved ? CarvedColor : FilledColor;
                    Handles.DrawSolidDisc(layout.CornerPosition(x, y) + Vector3.up * 3.2f, Vector3.up, layout.TileSize * 0.18f);
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
                    Handles.DrawSolidRectangleWithOutline(GridRect(layout, min, max - min, 0.08f),
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
        Handles.DrawSolidRectangleWithOutline(GridRect(layout, cell, size, 0.08f), new Color(color.r, color.g, color.b, 0.12f), color);
    }

    // The 4 world corners of a grid rectangle (turned with the layout), a little above the grid
    static Vector3[] GridRect(LevelLayoutAuthoring layout, Vector2Int from, Vector2Int size, float lift)
    {
        var up = Vector3.up * lift;
        return new[]
        {
            layout.CornerPosition(from.x, from.y) + up,
            layout.CornerPosition(from.x + size.x, from.y) + up,
            layout.CornerPosition(from.x + size.x, from.y + size.y) + up,
            layout.CornerPosition(from.x, from.y + size.y) + up,
        };
    }
}
