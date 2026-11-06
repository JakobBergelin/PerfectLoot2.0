using System.Collections.Generic;
using LevelGeneration;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds wall tiles with more interesting shapes to the Cathedral tile set: arch, pillar alcove, steps and broken corners.
/// Each is a copy of an existing tile (same corners, same colliders, so sockets and walkability don't change) with an
/// extra procedural mesh in front of or on top of its wall. Run it again to rebuild them; other tiles aren't touched.
/// In a Wall-Wall-Floor-Floor tile the wall's face is at z = WallFace, and floor is toward +z.
/// </summary>
public static class CathedralShapeTiles
{
    const string TileFolder = "Assets/Data/Level/Cathedral/Tiles";
    const string MeshFolder = "Assets/Data/Level/Cathedral/Models/Shapes";
    const string SetPath = "Assets/Data/Level/Cathedral/CathedralTileSet.asset";
    const string TrimMaterial = "Assets/ArtStyleTest/Materials/ComicCathedral/Ground_CT_Stone.mat";
    const string ShadowMaterial = "Assets/ArtStyleTest/Materials/ComicCathedral/Ground_CT_DarkStone.mat";
    const float WallFace = 0.27f;
    const float WallHeight = 3f;

    [MenuItem("Tools/Level/Add Cathedral Shape Tiles")]
    static void Create()
    {
        if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder("Assets/Data/Level/Cathedral/Models", "Shapes");
        var set = AssetDatabase.LoadAssetAtPath<WfcTileSet>(SetPath);

        AddTile(set, "Tile_WWFF_Arch", "Tile_WWFF", 3f, Arch(), Niche(2.1f, 1.9f, 1.05f));
        AddTile(set, "Tile_WWFF_Alcove", "Tile_WWFF", 3f, Alcove(), Niche(1.9f, 2.45f, 0f));
        AddTile(set, "Tile_WWFF_Steps", "Tile_WWFF", 2.5f, Steps(), null);
        AddTile(set, "Tile_WFFF_Broken", "Tile_WFFF", 24f, BrokenCorner(7), null);
        AddTile(set, "Tile_WWWF_Broken", "Tile_WWWF", 2f, BrokenCorner(19), null);

        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log("Added the Cathedral shape tiles.");
    }

    // The copy keeps the base tile's parts; trim and shadow are added as visual-only children
    static void AddTile(WfcTileSet set, string name, string baseTile, float weight, MeshBuilder trim, MeshBuilder shadow)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{TileFolder}/{baseTile}.prefab");
        var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = name;
        AddPart(root, name, "Trim", trim, TrimMaterial);
        if (shadow != null) AddPart(root, name, "Shadow", shadow, ShadowMaterial);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{TileFolder}/{name}.prefab");
        Object.DestroyImmediate(root);

        var corners = set.Tiles.Find(tile => tile.Name == baseTile);
        set.Tiles.RemoveAll(tile => tile.Name == name);
        set.Tiles.Add(new WfcTile
        {
            Name = name, Prefab = prefab, Weight = weight, AllowRotations = true,
            SouthWest = corners.SouthWest, SouthEast = corners.SouthEast, NorthEast = corners.NorthEast, NorthWest = corners.NorthWest,
        });
    }

    static void AddPart(GameObject root, string tileName, string partName, MeshBuilder builder, string materialPath)
    {
        var mesh = PortalPrefabCreator.SaveOrReplace(builder.ToMesh(), $"{MeshFolder}/{tileName}_{partName}.asset");
        mesh.name = $"{tileName}_{partName}";
        var part = new GameObject(partName);
        part.transform.SetParent(root.transform, false);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        part.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
    }

    // Two pilasters and a round arch standing out from the wall
    static MeshBuilder Arch()
    {
        const float depth = 0.32f, spring = 1.9f, inner = 1.05f, outer = 1.5f;
        var mesh = new MeshBuilder();
        float z = WallFace + depth * 0.5f;
        foreach (float side in new[] { -1f, 1f })
        {
            mesh.Box(new Vector3(side * (inner + outer) * 0.5f, spring * 0.5f, z), new Vector3(outer - inner, spring, depth));
            mesh.Box(new Vector3(side * (inner + outer) * 0.5f, 0.14f, z + 0.05f), new Vector3(outer - inner + 0.2f, 0.28f, depth + 0.1f));
        }
        mesh.HalfRing(new Vector3(0f, spring, WallFace), inner, outer, depth, 9);
        mesh.Box(new Vector3(0f, spring + outer + 0.02f, z + 0.03f), new Vector3(0.42f, 0.5f, depth + 0.06f));
        return mesh;
    }

    // A pillar in front of a dark recess, with a lintel over it
    static MeshBuilder Alcove()
    {
        const float depth = 0.26f;
        var mesh = new MeshBuilder();
        float z = WallFace + depth * 0.5f;
        foreach (float side in new[] { -1f, 1f })
            mesh.Box(new Vector3(side * 1.15f, 1.25f, z), new Vector3(0.36f, 2.5f, depth));
        mesh.Box(new Vector3(0f, 2.62f, z + 0.03f), new Vector3(2.8f, 0.34f, depth + 0.06f));
        mesh.Prism(new Vector3(0f, 0f, WallFace + 0.32f), 0.34f, 2.45f, 8);
        mesh.Box(new Vector3(0f, 0.12f, WallFace + 0.32f), new Vector3(0.92f, 0.24f, 0.86f));
        mesh.Box(new Vector3(0f, 2.36f, WallFace + 0.32f), new Vector3(0.86f, 0.2f, 0.8f));
        return mesh;
    }

    // A dark flat recess against the wall: a rectangle, with a half round top when arch radius is above 0
    static MeshBuilder Niche(float width, float height, float archRadius)
    {
        var mesh = new MeshBuilder();
        float z = WallFace + 0.02f;
        mesh.Box(new Vector3(0f, height * 0.5f, z), new Vector3(width, height, 0.02f));
        if (archRadius > 0f) mesh.HalfRing(new Vector3(0f, height, z - 0.01f), 0f, archRadius, 0.02f, 9);
        return mesh;
    }

    // A stepped plinth climbing the wall
    static MeshBuilder Steps()
    {
        var mesh = new MeshBuilder();
        for (int step = 0; step < 4; step++)
        {
            float height = 0.22f * (step + 1);
            float depth = 0.62f - 0.14f * step;
            float width = 3.6f - 0.8f * step;
            mesh.Box(new Vector3(0f, height * 0.5f, WallFace + depth * 0.5f), new Vector3(width, height, depth));
        }
        return mesh;
    }

    // The corner sits at the middle of the tile in every rotation: jagged stones on top of it, rubble fallen around it
    static MeshBuilder BrokenCorner(int seed)
    {
        var random = new System.Random(seed);
        float Next(float min, float max) => min + (float)random.NextDouble() * (max - min);
        var mesh = new MeshBuilder();
        for (int i = 0; i < 9; i++)
        {
            float angle = i / 9f * Mathf.PI * 2f;
            float reach = Next(0.2f, 0.75f);
            var size = new Vector3(Next(0.35f, 0.7f), Next(0.4f, 1.1f), Next(0.35f, 0.7f));
            var center = new Vector3(Mathf.Cos(angle) * reach, WallHeight + size.y * 0.25f, Mathf.Sin(angle) * reach);
            mesh.Box(center, size, Next(-0.35f, 0.35f), random);
        }
        for (int i = 0; i < 12; i++)
        {
            float angle = Next(0f, Mathf.PI * 2f);
            float reach = Next(0.5f, 1.5f);
            var size = new Vector3(Next(0.2f, 0.5f), Next(0.12f, 0.34f), Next(0.2f, 0.5f));
            mesh.Box(new Vector3(Mathf.Cos(angle) * reach, size.y * 0.5f, Mathf.Sin(angle) * reach), size, Next(-0.8f, 0.8f), random);
        }
        return mesh;
    }

    /// <summary>Collects flat-shaded convex shapes into one mesh.</summary>
    class MeshBuilder
    {
        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<int> _triangles = new List<int>();

        // Corners in clockwise order, seen from the outside
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = _vertices.Count;
            var normal = Vector3.Cross(b - a, c - a).normalized;
            foreach (var corner in new[] { a, b, c, d })
            {
                _vertices.Add(corner);
                _normals.Add(normal);
                _uvs.Add(new Vector2(corner.x + corner.z, corner.y));
            }
            _triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        /// <summary>A box, turned around y by yaw (radians). With random, its corners are pushed around a little: chunky stone.</summary>
        public void Box(Vector3 center, Vector3 size, float yaw = 0f, System.Random random = null)
        {
            var corners = new Vector3[8];
            var turn = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
            for (int i = 0; i < 8; i++)
            {
                var offset = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
                if (random != null) offset *= 0.75f + (float)random.NextDouble() * 0.5f;
                corners[i] = center + turn * Vector3.Scale(offset, size);
            }
            Quad(corners[0], corners[2], corners[3], corners[1]);   // South
            Quad(corners[5], corners[7], corners[6], corners[4]);   // North
            Quad(corners[4], corners[6], corners[2], corners[0]);   // West
            Quad(corners[1], corners[3], corners[7], corners[5]);   // East
            Quad(corners[2], corners[6], corners[7], corners[3]);   // Top
            Quad(corners[4], corners[0], corners[1], corners[5]);   // Bottom
        }

        /// <summary>An upright prism standing on base.</summary>
        public void Prism(Vector3 basePoint, float radius, float height, int sides)
        {
            for (int i = 0; i < sides; i++)
            {
                float from = i / (float)sides * Mathf.PI * 2f, to = (i + 1) / (float)sides * Mathf.PI * 2f;
                var a = basePoint + new Vector3(Mathf.Cos(from), 0f, Mathf.Sin(from)) * radius;
                var b = basePoint + new Vector3(Mathf.Cos(to), 0f, Mathf.Sin(to)) * radius;
                Quad(b, b + Vector3.up * height, a + Vector3.up * height, a);
            }
        }

        /// <summary>The upper half of a ring in the xy plane, from center's z toward +z by depth. Inner 0 makes a half disc.</summary>
        public void HalfRing(Vector3 center, float inner, float outer, float depth, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float from = i / (float)segments * Mathf.PI, to = (i + 1) / (float)segments * Mathf.PI;
                var directionA = new Vector3(Mathf.Cos(from), Mathf.Sin(from), 0f);
                var directionB = new Vector3(Mathf.Cos(to), Mathf.Sin(to), 0f);
                Vector3 innerA = center + directionA * inner, innerB = center + directionB * inner;
                Vector3 outerA = center + directionA * outer, outerB = center + directionB * outer;
                var front = Vector3.forward * depth;
                Quad(outerA + front, outerB + front, innerB + front, innerA + front);   // Toward the floor
                Quad(outerA, outerA + front, outerB + front, outerB);                   // Outer edge
                if (inner > 0f) Quad(innerB, innerB + front, innerA + front, innerA);   // Underside
            }
        }

        public Mesh ToMesh()
        {
            var mesh = new Mesh();
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
