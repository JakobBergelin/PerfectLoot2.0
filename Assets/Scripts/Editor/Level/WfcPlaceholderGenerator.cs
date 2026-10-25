using System.Collections.Generic;
using LevelGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Placeholder art for level generation: one tile prefab per corner combination (24 once rotations are left out),
/// each built from four 2.5 m quarters showing its corner's terrain, a 2x2 arena piece, and a tile set using them.
/// Also adds a level layout to the open Entities subscene.
/// </summary>
public static class WfcPlaceholderGenerator
{
    const string Folder = "Assets/Data/Level/Placeholder";
    const string TilesFolder = Folder + "/Tiles";
    const string TileSetPath = Folder + "/PlaceholderTileSet.asset";
    const float TileSize = 5f;
    const float Quarter = TileSize * 0.25f;

    const int WaterLayer = 4;   // Blocks units, not projectiles (Tools/Setup Physics Layers)
    const int GroundLayer = 6;
    const int WallLayer = 7;
    const float WallHeight = 3f;

    [MenuItem("Tools/Level/Generate Placeholder Tiles")]
    public static WfcTileSet Generate()
    {
        EnsureFolder("Assets/Data", "Level");
        EnsureFolder("Assets/Data/Level", "Placeholder");
        EnsureFolder(Folder, "Tiles");

        var materials = new Dictionary<WfcTerrain, Material>
        {
            [WfcTerrain.Walkable] = LoadOrCreateMaterial("PlaceholderFloor", new Color(0.55f, 0.6f, 0.5f)),
            [WfcTerrain.Wall] = LoadOrCreateMaterial("PlaceholderWall", new Color(0.35f, 0.35f, 0.42f)),
            [WfcTerrain.Water] = LoadOrCreateMaterial("PlaceholderWater", new Color(0.2f, 0.45f, 0.9f)),
        };

        var tileSet = AssetDatabase.LoadAssetAtPath<WfcTileSet>(TileSetPath);
        if (tileSet == null)
        {
            tileSet = ScriptableObject.CreateInstance<WfcTileSet>();
            AssetDatabase.CreateAsset(tileSet, TileSetPath);
        }
        tileSet.Tiles.Clear();
        tileSet.Pieces.Clear();

        // One tile per combination that isn't a rotation of another (the tile set rotates them)
        var corners = new WfcTerrain[4];
        var rotated = new WfcTerrain[4];
        int combinations = (int)Mathf.Pow(WfcTerrains.Count, 4);
        for (int code = 0; code < combinations; code++)
        {
            WfcRotation.Decode(code, corners);
            bool canonical = true;
            for (int r = 1; r < 4 && canonical; r++)
            {
                WfcRotation.RotateCorners(corners, r, rotated);
                canonical = WfcRotation.Code(rotated) >= code;
            }
            if (!canonical) continue;

            string name = "Tile_" + new string(new[] { WfcTerrains.Letter(corners[0]), WfcTerrains.Letter(corners[1]), WfcTerrains.Letter(corners[2]), WfcTerrains.Letter(corners[3]) });
            tileSet.Tiles.Add(new WfcTile
            {
                Name = name,
                Prefab = SaveTilePrefab(name, corners, materials),
                SouthWest = corners[0], SouthEast = corners[1], NorthEast = corners[2], NorthWest = corners[3],
                Weight = TileWeight(corners),
                AllowRotations = true,
            });
        }

        var arena = new WfcPiece { Name = "Arena 2x2", Size = new Vector2Int(2, 2), AllowRotations = true };
        arena.EnsureCorners();
        arena.Prefab = SaveArenaPrefab(materials);
        tileSet.Pieces.Add(arena);

        EditorUtility.SetDirty(tileSet);
        AssetDatabase.SaveAssets();
        Debug.Log($"Placeholder tile set with {tileSet.Tiles.Count} tiles and {tileSet.Pieces.Count} piece written to {TileSetPath}.", tileSet);
        return tileSet;
    }

    // Plain floor is the most common, then solid wall; mixed tiles are the transitions
    static float TileWeight(WfcTerrain[] corners)
    {
        bool same = corners[0] == corners[1] && corners[1] == corners[2] && corners[2] == corners[3];
        if (!same) return 1f;
        return corners[0] switch
        {
            WfcTerrain.Walkable => 6f,
            WfcTerrain.Wall => 3f,
            _ => 1.5f,
        };
    }

    static GameObject SaveTilePrefab(string name, WfcTerrain[] corners, Dictionary<WfcTerrain, Material> materials)
    {
        var root = new GameObject(name);
        for (int k = 0; k < 4; k++)
        {
            // SW, SE, NE, NW quarters
            var offset = new Vector3(k == 1 || k == 2 ? Quarter : -Quarter, 0f, k >= 2 ? Quarter : -Quarter);
            AddQuarter(root.transform, corners[k], offset, TileSize * 0.5f, materials);
        }
        return SavePrefab(root, $"{TilesFolder}/{name}.prefab");
    }

    static void AddQuarter(Transform parent, WfcTerrain terrain, Vector3 offset, float size, Dictionary<WfcTerrain, Material> materials)
    {
        switch (terrain)
        {
            case WfcTerrain.Walkable:
                var floor = AddBlock(parent, "Floor", offset + new Vector3(0f, -0.1f, 0f), new Vector3(size, 0.2f, size), GroundLayer, materials[terrain]);
                floor.AddComponent<GroundAuthoring>();
                break;
            case WfcTerrain.Wall:
                AddBlock(parent, "Wall", offset + new Vector3(0f, WallHeight * 0.5f, 0f), new Vector3(size, WallHeight, size), WallLayer, materials[terrain]);
                break;
            case WfcTerrain.Water:
                // The surface is aimable (Ground) but sits below the floor; an invisible box above it blocks units
                var surface = AddBlock(parent, "WaterSurface", offset + new Vector3(0f, -0.35f, 0f), new Vector3(size, 0.1f, size), WaterLayer, materials[terrain]);
                surface.AddComponent<GroundAuthoring>();
                var blocker = new GameObject("WaterBlocker") { layer = WaterLayer };
                blocker.transform.SetParent(parent, false);
                blocker.transform.localPosition = offset + new Vector3(0f, 1f, 0f);
                blocker.AddComponent<BoxCollider>().size = new Vector3(size, 2f, size);
                break;
        }
    }

    static GameObject AddBlock(Transform parent, string name, Vector3 position, Vector3 scale, int layer, Material material)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.layer = layer;
        block.transform.SetParent(parent, false);
        block.transform.localPosition = position;
        block.transform.localScale = scale;
        block.GetComponent<MeshRenderer>().sharedMaterial = material;
        return block;
    }

    static GameObject SaveArenaPrefab(Dictionary<WfcTerrain, Material> materials)
    {
        var root = new GameObject("Arena_2x2");
        var floor = AddBlock(root.transform, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(TileSize * 2f, 0.2f, TileSize * 2f), GroundLayer, materials[WfcTerrain.Walkable]);
        floor.AddComponent<GroundAuthoring>();
        foreach (var x in new[] { -1, 1 })
            foreach (var z in new[] { -1, 1 })
                AddBlock(root.transform, "Pillar", new Vector3(x * TileSize * 0.5f, WallHeight * 0.5f, z * TileSize * 0.5f),
                         new Vector3(1f, WallHeight, 1f), WallLayer, materials[WfcTerrain.Wall]);
        return SavePrefab(root, $"{Folder}/Arena_2x2.prefab");
    }

    static GameObject SavePrefab(GameObject root, string path)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static Material LoadOrCreateMaterial(string name, Color color)
    {
        string path = $"{Folder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        material = new Material(shader);
        material.SetColor("_BaseColor", color);
        material.color = color;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
    }

    // ---------- Level layout ----------

    [MenuItem("Tools/Level/Add Level Layout to Subscene")]
    static void AddLevelLayout()
    {
        Scene subscene = default;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isSubScene && scene.isLoaded) subscene = scene;
        }
        if (!subscene.IsValid())
        {
            EditorUtility.DisplayDialog("Add Level Layout", "Open the Entities subscene for editing first (tick its checkbox in the Hierarchy).", "OK");
            return;
        }

        var tileSet = AssetDatabase.LoadAssetAtPath<WfcTileSet>(TileSetPath);
        if (tileSet == null) tileSet = Generate();

        var go = new GameObject("LevelLayout");
        SceneManager.MoveGameObjectToScene(go, subscene);
        var layout = go.AddComponent<LevelLayoutAuthoring>();
        layout.TileSet = tileSet;
        layout.TileSize = TileSize;
        // Centered on the world origin, floor top at the old ground's height
        go.transform.position = new Vector3(-layout.Width * TileSize * 0.5f, -0.5f, -layout.Height * TileSize * 0.5f);

        // The player starts in the middle (its corners are always floor)
        layout.EnsureCornerRules();
        layout.PlayerStart = new Vector2Int(layout.Width / 2, layout.Height / 2);

        Undo.RegisterCreatedObjectUndo(go, "Add Level Layout");
        EditorSceneManager.MarkSceneDirty(subscene);
        Selection.activeGameObject = go;
        Debug.Log($"Added a level layout to {subscene.name}. Paint rules in the Scene view, then Generate Preview.", go);
    }
}
