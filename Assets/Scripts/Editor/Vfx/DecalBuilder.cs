using System;
using System.Collections.Generic;
using LevelGeneration;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the ground decals (art guide section 16): comic sprites drawn in code (hard edges, flat colors, low contrast on
/// the floors), a URP decal material for each, Brightwater Hollow's floor decal set (moss and fallen leaves along the
/// forest's edge, mud by the water, flower patches in the clearings), and the marks effects leave (scorch where a
/// fireball lands, frost from Cold Nova, the slimes' trails). Safe to run again; VfxBuilder.Build runs AttachToEffects.
/// </summary>
public static class DecalBuilder
{
    const string Root = "Assets/Data/Vfx/Decals";
    public const string BrightwaterSet = "Assets/Data/Level/Biomes/BrightwaterHollow/BrightwaterHollow_Decals.asset";
    const string SlimePrefab = "Assets/Prefabs/Enemies/EN_Slime_Visuals.prefab";

    [MenuItem("Tools/VFX/Build Decals")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets/Data/Vfx", "Decals");
        var moss = Material("Moss", Moss);
        var leaves = Material("Leaves", Leaves);
        var mud = Material("Mud", Mud);
        var flowers = Material("Flowers", Flowers);
        Material("Scorch", Scorch);
        Material("Frost", Frost);
        var slime = Material("Slime", Slime);

        var set = AssetDatabase.LoadAssetAtPath<LevelDecalSet>(BrightwaterSet);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<LevelDecalSet>();
            AssetDatabase.CreateAsset(set, BrightwaterSet);
        }
        set.Decals = new List<LevelDecalSet.Decal>
        {
            new() { Material = moss, Spot = LevelDecalSet.Spot.ForestEdge, Count = 70, Size = new Vector2(1.6f, 3.2f) },
            new() { Material = leaves, Spot = LevelDecalSet.Spot.ForestEdge, Count = 55, Size = new Vector2(1.2f, 2.2f) },
            new() { Material = mud, Spot = LevelDecalSet.Spot.Shore, Count = 40, Size = new Vector2(1.6f, 3f) },
            new() { Material = flowers, Spot = LevelDecalSet.Spot.Open, Count = 30, Size = new Vector2(0.9f, 1.5f) },
        };
        EditorUtility.SetDirty(set);

        // The slimes' trail
        var root = PrefabUtility.LoadPrefabContents(SlimePrefab);
        var trail = root.GetComponent<SlimeTrailAuthoring>();
        if (trail == null) trail = root.AddComponent<SlimeTrailAuthoring>();     // Not ??: a missing component isn't null to C#
        trail.Material = slime;
        PrefabUtility.SaveAsPrefabAsset(root, SlimePrefab);
        PrefabUtility.UnloadPrefabContents(root);

        AttachToEffects();
        AssetDatabase.SaveAssets();
        Debug.Log("Decals built");
    }

    /// <summary>The marks effects leave on the ground (VfxInstance.GroundMark), on their prefabs.</summary>
    public static void AttachToEffects()
    {
        Mark("Assets/Data/Vfx/Prefabs/VFX_FireballImpact.prefab", $"{Root}/DEC_Scorch.mat", 2.6f, new Vector2(6f, 3f));
        Mark("Assets/Data/Vfx/Prefabs/VFX_ColdNova.prefab", $"{Root}/DEC_Frost.mat", 6f, new Vector2(4f, 3f));
    }

    static void Mark(string prefabPath, string materialPath, float size, Vector2 life)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null || AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null) return;
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        var instance = root.GetComponent<VfxInstance>();
        if (instance != null)
        {
            instance.GroundMark = material;
            instance.GroundMarkSize = size;
            instance.GroundMarkLife = life;
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        PrefabUtility.UnloadPrefabContents(root);
    }

    // ---------- materials ----------

    static Material Material(string name, Func<Vector2, System.Random, Color> paint)
    {
        var texture = Paint($"DEC_{name}", paint);
        string path = $"{Root}/DEC_{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("Shader Graphs/Decal");
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetTexture("Base_Map", texture);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    // Paints a sprite pixel by pixel (p from -1 to 1), the same random sequence every time
    static Texture2D Paint(string name, Func<Vector2, System.Random, Color> paint, int size = 256)
    {
        var shapes = new System.Random(name.GetHashCode() & 0x7fffffff);
        _shapes = null;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[x + y * size] = paint(new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f), shapes);
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        string path = $"{Root}/{name}.png";
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------- sprites: hard edges (a pixel of antialiasing), flat colors ----------

    // A few random circles inside the sprite, made once per sprite (the first pixel)
    static List<Vector3> _shapes;
    static List<Vector3> Circles(System.Random r, int count, float spread, Vector2 radius)
    {
        if (_shapes != null) return _shapes;
        _shapes = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float a = (float)r.NextDouble() * 6.283f, d = (float)Math.Sqrt(r.NextDouble()) * spread;
            _shapes.Add(new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d, Mathf.Lerp(radius.x, radius.y, (float)r.NextDouble())));
        }
        return _shapes;
    }

    // Distance to the nearest circle's edge (negative inside), and which circle
    static float Inside(Vector2 p, List<Vector3> circles, out int which)
    {
        float best = 9f;
        which = -1;
        for (int i = 0; i < circles.Count; i++)
        {
            float d = (p - (Vector2)circles[i]).magnitude - circles[i].z;
            if (d < best) { best = d; which = i; }
        }
        return best;
    }

    static float Edge(float distance) => Mathf.Clamp01(0.5f - distance * 128f);
    static Color Hex(string hex, float alpha)
    {
        ColorUtility.TryParseHtmlString(hex, out var c);
        c.a = alpha;
        return c;
    }
    static Color With(Color c, float alpha) { c.a *= alpha; return c; }
    static float Hash(int i) => Mathf.Abs(Mathf.Sin(i * 12.9898f) * 43758.5453f) % 1f;

    // Moss: a clump of round tufts, a shade darker than the floor, with lighter specks
    static Color Moss(Vector2 p, System.Random r)
    {
        var tufts = Circles(r, 11, 0.55f, new Vector2(0.14f, 0.3f));
        float d = Inside(p, tufts, out int which);
        if (d > 0.01f) return Color.clear;
        var color = Hash(which) < 0.5f ? Hex("#3A6E3C", 0.9f) : Hex("#407A40", 0.9f);
        float speck = Mathf.Sin(p.x * 61f) * Mathf.Sin(p.y * 57f);
        if (speck > 0.82f) color = Hex("#5E9A4E", 0.95f);
        return With(color, Edge(d));
    }

    // Fallen leaves: a handful of small pointed leaves in the forest kit's greens, a yellow and a brown
    static Color Leaves(Vector2 p, System.Random r)
    {
        var spots = Circles(r, 8, 0.7f, new Vector2(0f, 1f));
        string[] colors = { "#4F8F3A", "#8DB840", "#C9A23E", "#7A5A2E" };
        for (int i = 0; i < spots.Count; i++)
        {
            float angle = spots[i].z * 6.283f;
            var q = p - (Vector2)spots[i];
            q = new Vector2(q.x * Mathf.Cos(angle) + q.y * Mathf.Sin(angle), -q.x * Mathf.Sin(angle) + q.y * Mathf.Cos(angle)) / 0.16f;
            float body = Mathf.Max((q - new Vector2(0.55f, 0f)).magnitude, (q + new Vector2(0.55f, 0f)).magnitude) - 1.15f;
            if (body < 0.02f) return With(Hex(colors[i % colors.Length], 1f), Edge(body * 0.16f));
        }
        return Color.clear;
    }

    // Mud: a wet blob, darker in the middle
    static Color Mud(Vector2 p, System.Random r)
    {
        var blobs = Circles(r, 7, 0.45f, new Vector2(0.25f, 0.42f));
        float d = Inside(p, blobs, out _);
        if (d > 0.01f) return Color.clear;
        var color = d < -0.12f ? Hex("#3A2C22", 0.7f) : Hex("#4A3A2C", 0.62f);
        return With(color, Edge(d));
    }

    // Flowers: a few small five-petal flowers, rose (a free accent) and off-white, with muted centers
    static Color Flowers(Vector2 p, System.Random r)
    {
        var spots = Circles(r, 7, 0.65f, new Vector2(0f, 1f));
        for (int i = 0; i < spots.Count; i++)
        {
            var q = p - (Vector2)spots[i];
            float size = 0.09f + spots[i].z * 0.05f;
            if (q.magnitude > size * 2.2f) continue;
            if (q.magnitude < size * 0.45f) return Hex("#D9B25A", 1f);
            float angle = Mathf.Atan2(q.y, q.x) + spots[i].z * 6.283f;
            float petal = size * (1.1f + 0.75f * Mathf.Cos(angle * 5f));
            if (q.magnitude < petal) return With(Hash(i) < 0.6f ? Hex("#C8447E", 1f) : Hex("#F2E6DA", 1f), Edge(q.magnitude - petal));
        }
        return Color.clear;
    }

    // Scorch: a dark burst with ragged rays, a few dull embers in the middle
    static Color Scorch(Vector2 p, System.Random r)
    {
        var rays = Circles(r, 14, 0f, new Vector2(0.55f, 0.95f));
        float angle = Mathf.Atan2(p.y, p.x) / 6.283f + 0.5f;
        int ray = Mathf.FloorToInt(angle * rays.Count) % rays.Count;
        float t = angle * rays.Count - Mathf.Floor(angle * rays.Count);
        float reach = Mathf.Lerp(0.45f, rays[ray].z, 1f - Mathf.Abs(t - 0.5f) * 2f);
        float d = p.magnitude - reach;
        if (d > 0.01f) return Color.clear;
        float ember = Mathf.Sin(p.x * 40f + 1.3f) * Mathf.Sin(p.y * 37f);
        if (p.magnitude < 0.3f && ember > 0.93f) return Hex("#B8301A", 1f);
        var color = p.magnitude < 0.38f ? Hex("#140E10", 0.92f) : Hex("#2A2024", 0.8f);
        return With(color, Edge(d));
    }

    // Frost: a ring of ice shards pointing out, in the frost trait's pale cyan
    static Color Frost(Vector2 p, System.Random r)
    {
        var shards = Circles(r, 18, 0f, new Vector2(0.7f, 0.98f));
        float angle = Mathf.Atan2(p.y, p.x) / 6.283f + 0.5f;
        int shard = Mathf.FloorToInt(angle * shards.Count) % shards.Count;
        float t = angle * shards.Count - Mathf.Floor(angle * shards.Count);
        float reach = Mathf.Lerp(0.55f, shards[shard].z, 1f - Mathf.Abs(t - 0.5f) * 2f);
        float m = p.magnitude;
        if (m > reach || m < 0.42f) return Color.clear;
        var color = m < 0.52f ? Hex("#E9FBFF", 0.9f) : Hex("#BFF3FF", 0.75f);
        return With(color, Edge(Mathf.Max(m - reach, 0.42f - m)));
    }

    // Slime: a glistening patch in the slimes' green, see-through, with a light sheen
    static Color Slime(Vector2 p, System.Random r)
    {
        var blobs = Circles(r, 5, 0.3f, new Vector2(0.3f, 0.48f));
        float d = Inside(p, blobs, out _);
        if (d > 0.01f) return Color.clear;
        var sheen = new Vector2(p.x + 0.18f, p.y - 0.2f);
        var color = sheen.magnitude < 0.09f || (sheen + new Vector2(-0.16f, 0.05f)).magnitude < 0.05f ? Hex("#E6FFF0", 0.95f) : Hex("#3F8F66", 0.72f);
        return With(color, Edge(d));
    }
}
