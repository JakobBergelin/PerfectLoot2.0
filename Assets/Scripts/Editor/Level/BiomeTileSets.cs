using System.Collections.Generic;
using System.IO;
using System.Linq;
using LevelGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the biome tile sets: every corner combination of floor, wall and liquid made by BiomeTileGenerator (no
/// Blender models), with floor textures drawn here, PerfectLoot/Comic materials in each biome's palette (see
/// Assets/ArtStyleTest/CLAUDE.md), liquid, glow and chasm materials, and an atmosphere per biome: key light, ambient,
/// fog and a post-processing profile. Tools/Level/Biomes/Build All rebuilds everything under Assets/Data/Level/Biomes;
/// Apply To Test Levels points the test levels at one biome each. Safe to run again.
/// </summary>
public static class BiomeTileSets
{
    const string Root = "Assets/Data/Level/Biomes";
    const string ComicEnv = "Assets/ArtStyleTest/Materials/ComicCathedral/CT_Stone.mat";
    const string ComicFloor = "Assets/ArtStyleTest/Materials/ComicCathedral/Ground_CT_Stone.mat";
    const string ComicGlow = "Assets/ArtStyleTest/Materials/ComicCathedral/CT_Flame.mat";
    const string SampleProfile = "Assets/Settings/SampleSceneProfile.asset";

    const int WaterLayer = 4;   // Blocks units, not projectiles
    const int GroundLayer = 6;
    const int WallLayer = 7;

    public enum FloorPattern { Slabs, Plates, Mud }

    public class Biome
    {
        public string Name;
        public string TestScene;
        public WfcCornerRule Border = WfcCornerRule.Wall;
        public BiomeTileGenerator.Style Style;
        public float LiquidFactor = 0.5f;

        public FloorPattern Pattern;
        public string Floor, FloorAlt, FloorGap;
        public string WallSide, WallTop, Bank, Rock, Accent, AccentGlow;
        public float AccentGlowStrength = 2.2f;
        public string EnvShadow;

        public bool Lava;
        public string Deep, Shallow, Foam, Ripple;
        public Color LavaGlow;
        public string Crust;
        public string ChasmRim, ChasmFog;
        public Color ChasmMist;
        public Color Glow;

        public string Key; public float KeyIntensity; public Vector3 KeyAngles = new Vector3(50f, 330f, 0f);
        public string AmbientSky, AmbientEquator, AmbientGround, Fog; public float FogDensity = 0.012f;
        public float Bloom = 0.35f, Saturation = 5f, Contrast = 8f, Vignette = 0.28f;
        public string ShadowTint, HighlightTint;
    }

    public static readonly Biome[] All =
    {
        new Biome
        {
            Name = "EmberDepths", TestScene = "TestKillEnemies", LiquidFactor = 0.75f,
            Style = new BiomeTileGenerator.Style { WallHeight = 3.2f, WallTopJitter = 0.55f, WallBulge = 0.32f, BoundaryNoise = 0.2f, GlowReach = 1.3f, Accent = BiomeTileGenerator.Accent.MagmaSeam, RimRockChance = 0.3f },
            Pattern = FloorPattern.Slabs, Floor = "#3C2E3A", FloorAlt = "#382C38", FloorGap = "#221A24",
            WallSide = "#4E4A5E", WallTop = "#6E6880", Bank = "#2E2430", Rock = "#5E5A72", Accent = "#FF5418", AccentGlow = "#FF4A1C",
            EnvShadow = "#2E4252",
            Lava = true, Deep = "#6E1A12", LavaGlow = new Color(2.1f, 0.42f, 0.1f), Crust = "#1C1016",
            Glow = new Color(0.32f, 0.08f, 0.02f),
            Key = "#FFD8B0", KeyIntensity = 1.3f, AmbientSky = "#4E6274", AmbientEquator = "#2E3A48", AmbientGround = "#141418", Fog = "#1A2630", FogDensity = 0.012f,
            Bloom = 0.45f, ShadowTint = "#2E4252", HighlightTint = "#FFD8B0",
        },
        new Biome
        {
            Name = "DrownedMire", TestScene = "TestSurvive", LiquidFactor = 0.95f,
            Style = new BiomeTileGenerator.Style { WallHeight = 2.3f, WallTopJitter = 0.4f, WallBulge = 0.38f, WallRows = 2, BoundaryNoise = 0.24f, LiquidSurfaceY = -0.22f, GlowReach = 0.9f, Accent = BiomeTileGenerator.Accent.Mushrooms, AccentChance = 0.22f, RimRockChance = 0.25f },
            Pattern = FloorPattern.Mud, Floor = "#3E3436", FloorAlt = "#3A4438", FloorGap = "#2A2428",
            WallSide = "#3E2E38", WallTop = "#3F4C3E", Bank = "#2A2428", Rock = "#6C6A7C", Accent = "#C8447E", AccentGlow = "#FF5FA8", AccentGlowStrength = 0.9f,
            EnvShadow = "#2C4450",
            Deep = "#12303A", Shallow = "#1C4A54", Foam = "#6FB8AE", Ripple = "#2C6A6E",
            Glow = new Color(0.04f, 0.16f, 0.15f),
            Key = "#F2DDB0", KeyIntensity = 1.2f, AmbientSky = "#56707A", AmbientEquator = "#344A4C", AmbientGround = "#1A1E24", Fog = "#20363C", FogDensity = 0.016f,
            Bloom = 0.35f, ShadowTint = "#2C4450", HighlightTint = "#F2DDB0",
        },
        new Biome
        {
            Name = "FrozenHollow", TestScene = "TestKillBoss", LiquidFactor = 0.6f,
            Style = new BiomeTileGenerator.Style { WallHeight = 3f, WallTopJitter = 0.6f, WallBulge = 0.2f, BoundaryNoise = 0.12f, NoiseScale = 1.1f, Accent = BiomeTileGenerator.Accent.Crystals, AccentChance = 0.35f, CrownRocks = 4, RimRockChance = 0.2f },
            Pattern = FloorPattern.Plates, Floor = "#3A3F52", FloorAlt = "#363B4E", FloorGap = "#22263A",
            WallSide = "#6E7A94", WallTop = "#C6D2EA", Bank = "#2E3446", Rock = "#5C6680", Accent = "#3F74E0", AccentGlow = "#4A7CFF", AccentGlowStrength = 0.9f,
            EnvShadow = "#3A4468",
            Deep = "#0E1A33", Shallow = "#1A2E52", Foam = "#A8B8D8", Ripple = "#2A4470",
            Key = "#F0DCC8", KeyIntensity = 1.2f, AmbientSky = "#5A6684", AmbientEquator = "#323A54", AmbientGround = "#14161E", Fog = "#1A2034", FogDensity = 0.014f,
            Bloom = 0.35f, ShadowTint = "#3A4468", HighlightTint = "#F0DCC8",
        },
        new Biome
        {
            Name = "AbyssalRuins", TestScene = "TestReachExit", Border = WfcCornerRule.Water, LiquidFactor = 1.1f,
            Style = new BiomeTileGenerator.Style { WallHeight = 2.4f, WallTopJitter = 0.7f, WallBulge = 0.08f, BoundaryNoise = 0.1f, Chasm = true, ChasmDepth = 12f, GlowReach = 0.9f, RimRockChance = 0.2f, CrownRocks = 4 },
            Pattern = FloorPattern.Slabs, Floor = "#403C4A", FloorAlt = "#3C3846", FloorGap = "#262330",
            WallSide = "#6E6880", WallTop = "#7E7892", Bank = "#2A2834", Rock = "#5E5A72", Accent = "#3FC4B4", AccentGlow = "#4FD6C8",
            EnvShadow = "#2A4448",
            ChasmRim = "#2A2834", ChasmFog = "#123E44", ChasmMist = new Color(0.3f, 1.1f, 1.0f),
            Glow = new Color(0.03f, 0.15f, 0.14f),
            Key = "#FFD8B0", KeyIntensity = 1.25f, AmbientSky = "#4A6670", AmbientEquator = "#2A3E44", AmbientGround = "#101418", Fog = "#0E2226", FogDensity = 0.014f,
            Bloom = 0.4f, ShadowTint = "#2A4448", HighlightTint = "#FFD8B0",
        },
    };

    [MenuItem("Tools/Level/Biomes/Build All Biome Tile Sets")]
    public static void BuildAll()
    {
        foreach (var biome in All) Build(biome);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Level/Biomes/Apply Biomes To Test Levels")]
    public static void ApplyToTestLevels()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        foreach (var biome in All) ApplyToScene(biome, $"Assets/Scenes/{biome.TestScene}.unity", $"Assets/Scenes/{biome.TestScene}/Entities.unity");
    }

    // ---------- tile set ----------

    public static WfcTileSet Build(Biome biome)
    {
        string folder = $"{Root}/{biome.Name}";
        EnsureFolder(folder);
        EnsureFolder(folder + "/Tiles");
        EnsureFolder(folder + "/Materials");

        var materials = BuildMaterials(biome, folder);

        // All meshes of the biome live in one container asset, rebuilt from scratch each time
        string meshesPath = $"{folder}/{biome.Name}_Meshes.asset";
        AssetDatabase.DeleteAsset(meshesPath);
        var container = new Mesh { name = biome.Name + "_Meshes" };
        AssetDatabase.CreateAsset(container, meshesPath);

        string setPath = $"{folder}/{biome.Name}TileSet.asset";
        var tileSet = AssetDatabase.LoadAssetAtPath<WfcTileSet>(setPath);
        if (tileSet == null)
        {
            tileSet = ScriptableObject.CreateInstance<WfcTileSet>();
            AssetDatabase.CreateAsset(tileSet, setPath);
        }
        tileSet.Tiles.Clear();
        tileSet.Pieces.Clear();

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

            string letters = new string(corners.Select(WfcTerrains.Letter).ToArray());
            float weight = Weight(corners, biome.LiquidFactor);
            bool allFloor = letters == "FFFF", allWall = letters == "WWWW";
            if (allFloor)
            {
                AddTile(tileSet, biome, container, materials, folder, "Tile_FFFF", corners, weight * 0.45f, 1000, false);
                AddTile(tileSet, biome, container, materials, folder, "Tile_FFFF_Alt", corners, weight * 0.35f, 1001, true);
                AddTile(tileSet, biome, container, materials, folder, "Tile_FFFF_Var", corners, weight * 0.2f, 1002, false);
            }
            else if (allWall)
            {
                AddTile(tileSet, biome, container, materials, folder, "Tile_WWWW", corners, weight * 0.5f, 2000, false);
                AddTile(tileSet, biome, container, materials, folder, "Tile_WWWW_Var", corners, weight * 0.5f, 2001, false);
            }
            else AddTile(tileSet, biome, container, materials, folder, "Tile_" + letters, corners, weight, code + 17, false);
        }

        EditorUtility.SetDirty(container);
        EditorUtility.SetDirty(tileSet);
        AssetDatabase.SaveAssets();
        Debug.Log($"Biome {biome.Name}: {tileSet.Tiles.Count} tiles written to {setPath}.", tileSet);
        return tileSet;
    }

    // A tile weighs the product of its corners' factors: mostly floor, some wall, liquid as the biome likes
    static float Weight(WfcTerrain[] corners, float liquidFactor)
    {
        float weight = 1f;
        foreach (var c in corners) weight *= c == WfcTerrain.Walkable ? 4f : c == WfcTerrain.Wall ? 1f : liquidFactor;
        return weight;
    }

    static void AddTile(WfcTileSet set, Biome biome, Mesh container, Dictionary<BiomeTileGenerator.Part, Material> materials,
                        string folder, string name, WfcTerrain[] corners, float weight, int seed, bool floorAlt)
    {
        var generator = new BiomeTileGenerator(biome.Style, (WfcTerrain[])corners.Clone(), seed + StableHash(biome.Name));
        generator.Build(floorAlt);

        var root = new GameObject(name);
        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        visual.AddComponent<MeshFilter>().sharedMesh = Store(container, generator.Visual, name + "_Visual");
        visual.AddComponent<MeshRenderer>().sharedMaterials = generator.VisualParts.Select(p => materials[p]).ToArray();

        void Collider(Mesh mesh, string suffix, int layer, bool ground)
        {
            if (mesh == null) return;
            var go = new GameObject(suffix) { layer = layer };
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshCollider>().sharedMesh = Store(container, mesh, $"{name}_{suffix}");
            if (ground) go.AddComponent<GroundAuthoring>();
        }
        Collider(generator.Ground, "ColGround", GroundLayer, true);
        Collider(generator.Wall, "ColWall", WallLayer, false);
        Collider(generator.LiquidSurface, "ColLiquidSurface", WaterLayer, true);
        Collider(generator.LiquidBlocker, "ColLiquidBlocker", WaterLayer, false);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/Tiles/{name}.prefab");
        Object.DestroyImmediate(root);
        set.Tiles.Add(new WfcTile
        {
            Name = name, Prefab = prefab, Weight = weight, AllowRotations = true,
            SouthWest = corners[0], SouthEast = corners[1], NorthEast = corners[2], NorthWest = corners[3],
        });
    }

    static Mesh Store(Mesh container, Mesh mesh, string name)
    {
        mesh.name = name;
        AssetDatabase.AddObjectToAsset(mesh, container);
        return mesh;
    }

    // ---------- materials ----------

    static Dictionary<BiomeTileGenerator.Part, Material> BuildMaterials(Biome b, string folder)
    {
        string m = folder + "/Materials";
        var floorA = FloorTexture(b, folder, "FloorA", b.Floor, 11);
        var floorB = FloorTexture(b, folder, "FloorB", b.FloorAlt, 23);
        var shadow = Hex(b.EnvShadow);

        var result = new Dictionary<BiomeTileGenerator.Part, Material>
        {
            [BiomeTileGenerator.Part.Floor] = Comic(ComicFloor, $"{m}/{b.Name}_Floor.mat", Color.white, shadow, floorA),
            [BiomeTileGenerator.Part.FloorAlt] = Comic(ComicFloor, $"{m}/{b.Name}_FloorAlt.mat", Color.white, shadow, floorB),
            [BiomeTileGenerator.Part.WallSide] = Comic(ComicEnv, $"{m}/{b.Name}_WallSide.mat", Hex(b.WallSide), shadow),
            [BiomeTileGenerator.Part.WallTop] = Comic(ComicEnv, $"{m}/{b.Name}_WallTop.mat", Hex(b.WallTop), shadow),
            [BiomeTileGenerator.Part.Bank] = Comic(ComicEnv, $"{m}/{b.Name}_Bank.mat", Hex(b.Bank), shadow),
            [BiomeTileGenerator.Part.Rock] = Comic(ComicEnv, $"{m}/{b.Name}_Rock.mat", Hex(b.Rock), shadow),
            [BiomeTileGenerator.Part.Accent] = Accent(b, $"{m}/{b.Name}_Accent.mat"),
        };

        var liquid = Load($"{m}/{b.Name}_Liquid.mat", "PerfectLoot/Liquid");
        if (b.Lava)
        {
            liquid.SetFloat("_Mode", 1f);
            liquid.DisableKeyword("_MODE_WATER");
            liquid.EnableKeyword("_MODE_LAVA");
            liquid.SetColor("_DeepColor", Hex(b.Deep));
            liquid.SetColor("_GlowColor", b.LavaGlow);
            liquid.SetColor("_CrustColor", Hex(b.Crust));
            liquid.SetFloat("_CrustAmount", 0.42f);
            liquid.SetFloat("_FoamWidth", 0.22f);
            liquid.SetFloat("_FlowSpeed", 0.18f);
        }
        else if (b.Deep != null)
        {
            liquid.SetFloat("_Mode", 0f);
            liquid.EnableKeyword("_MODE_WATER");
            liquid.DisableKeyword("_MODE_LAVA");
            liquid.SetColor("_DeepColor", Hex(b.Deep));
            liquid.SetColor("_ShallowColor", Hex(b.Shallow));
            liquid.SetColor("_FoamColor", Hex(b.Foam));
            liquid.SetColor("_RippleColor", Hex(b.Ripple));
            liquid.SetFloat("_FlowSpeed", 0.2f);
        }
        result[BiomeTileGenerator.Part.Liquid] = liquid;

        var glow = Load($"{m}/{b.Name}_Glow.mat", "PerfectLoot/Glow");
        glow.SetColor("_Color", b.Glow);
        glow.SetFloat("_Reach", Mathf.Max(0.1f, b.Style.GlowReach));
        glow.SetFloat("_Flicker", b.Lava ? 0.3f : 0.12f);
        result[BiomeTileGenerator.Part.Glow] = glow;

        var chasm = Load($"{m}/{b.Name}_Chasm.mat", "PerfectLoot/Chasm");
        chasm.SetColor("_RimColor", Hex(b.ChasmRim ?? b.Bank));
        chasm.SetColor("_FogColor", Hex(b.ChasmFog ?? b.Fog));
        chasm.SetColor("_MistColor", b.ChasmMist);
        chasm.SetFloat("_Depth", Mathf.Max(2f, b.Style.ChasmDepth * 0.65f));
        result[BiomeTileGenerator.Part.Chasm] = chasm;

        foreach (var material in result.Values) EditorUtility.SetDirty(material);
        return result;
    }

    static Material Comic(string template, string path, Color color, Color shadow, Texture2D texture = null)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(AssetDatabase.LoadAssetAtPath<Material>(template));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetColor("_ShadowColor", shadow);
        material.SetTexture("_BaseMap", texture);
        return material;
    }

    static Material Accent(Biome b, string path)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(AssetDatabase.LoadAssetAtPath<Material>(ComicGlow));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", Hex(b.Accent));
        material.SetColor("_EmissionColor", Hex(b.AccentGlow) * b.AccentGlowStrength);
        material.SetTexture("_EmissionMap", Texture2D.whiteTexture);
        material.SetFloat("_EmissionPulse", 0.25f);
        material.SetFloat("_PatternStrength", 0f);
        material.SetFloat("_OutlineWidth", 2.5f);
        material.EnableKeyword("_EMISSION");
        return material;
    }

    static Material Load(string path, string shader)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(material, path);
        }
        return material;
    }

    // ---------- floor textures ----------

    /// <summary>
    /// A 512 px floor texture covering one tile. Slabs/Plates: irregular stones with dark seams, including a seam along
    /// every border so tiles meet cleanly in any rotation. Mud: patches of mud and moss split by soft cracks.
    /// </summary>
    static Texture2D FloorTexture(Biome b, string folder, string name, string baseHex, int seed)
    {
        const int size = 512;
        string path = $"{folder}/Materials/{b.Name}_{name}.png";
        var random = new System.Random(seed + b.Name.Length * 31);
        int cells = b.Pattern == FloorPattern.Plates ? 3 : b.Pattern == FloorPattern.Mud ? 5 : 4;
        float gap = b.Pattern == FloorPattern.Mud ? 5f : b.Pattern == FloorPattern.Plates ? 4f : 6f;
        var points = new List<Vector2>();
        var tones = new List<float>();
        var alt = new List<bool>();
        float cell = size / (float)cells;
        for (int y = 0; y < cells; y++)
            for (int x = 0; x < cells; x++)
            {
                points.Add(new Vector2((x + 0.2f + (float)random.NextDouble() * 0.6f) * cell, (y + 0.2f + (float)random.NextDouble() * 0.6f) * cell));
                tones.Add(0.9f + (float)random.NextDouble() * 0.16f);
                alt.Add(random.NextDouble() < 0.4);
            }

        Color baseColor = Hex(baseHex), gapColor = Hex(b.FloorGap), altColor = Hex(b.Pattern == FloorPattern.Mud ? b.FloorAlt : baseHex);
        var pixels = new Color32[size * size];
        for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                var p = new Vector2(px + 0.5f, py + 0.5f);
                float f1 = float.MaxValue, f2 = float.MaxValue; int nearest = 0;
                for (int i = 0; i < points.Count; i++)
                {
                    float d = Vector2.Distance(p, points[i]);
                    if (d < f1) { f2 = f1; f1 = d; nearest = i; }
                    else if (d < f2) f2 = d;
                }
                float edge = (f2 - f1) * 0.5f;
                float border = Mathf.Min(Mathf.Min(p.x, size - p.x), Mathf.Min(p.y, size - p.y));
                float seam = Mathf.Min(edge, border * 2f);
                float grain = Mathf.PerlinNoise(px * 0.06f + seed, py * 0.06f) * 0.06f - 0.03f;
                Color c = alt[nearest] ? altColor : baseColor;
                c *= (tones[nearest] + grain) * 0.85f;
                if (seam < gap) c = gapColor;
                else if (seam < gap * 2.2f && b.Pattern != FloorPattern.Mud) c *= 1.06f;    // a faint lit lip along each seam
                c.a = 1f;
                pixels[px + py * size] = c;
            }

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
        texture.SetPixels32(pixels);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 4;
        importer.maxTextureSize = 512;
        importer.sRGBTexture = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------- atmosphere ----------

    /// <summary>The biome's post-processing: a copy of the sample profile with its bloom, grading and vignette.</summary>
    static VolumeProfile BuildProfile(Biome b)
    {
        string path = $"{Root}/{b.Name}/{b.Name}_Volume.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            AssetDatabase.CopyAsset(SampleProfile, path);
            profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        }
        if (profile.TryGet<Bloom>(out var bloom))
        {
            bloom.intensity.Override(b.Bloom);
            bloom.threshold.Override(0.95f);
            bloom.scatter.Override(0.65f);
        }
        if (profile.TryGet<Vignette>(out var vignette)) vignette.intensity.Override(b.Vignette);
        var grading = Get<ColorAdjustments>(profile);
        grading.saturation.Override(b.Saturation);
        grading.contrast.Override(b.Contrast);
        // No split toning: the warm key light and cool fog already split the scene, and toning turned every biome brown
        if (profile.TryGet<SplitToning>(out var split)) split.active = false;
        if (profile.TryGet<MotionBlur>(out var blur)) blur.active = false;
        EditorUtility.SetDirty(profile);
        return profile;
    }

    static T Get<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var component)) return component;
        component = profile.Add<T>(true);
        component.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    /// <summary>Points the scene's level at the biome and gives it the biome's light, ambient, fog and post-processing.</summary>
    public static void ApplyToScene(Biome b, string scenePath, string subscenePath)
    {
        var tileSet = AssetDatabase.LoadAssetAtPath<WfcTileSet>($"{Root}/{b.Name}/{b.Name}TileSet.asset") ?? Build(b);
        var profile = BuildProfile(b);
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;
            light.color = Hex(b.Key);
            light.intensity = b.KeyIntensity;
            light.transform.rotation = Quaternion.Euler(b.KeyAngles);
            light.shadows = LightShadows.Soft;
            EditorUtility.SetDirty(light);
        }
        foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
        {
            if (!volume.isGlobal) continue;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
        }
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Hex(b.AmbientSky);
        RenderSettings.ambientEquatorColor = Hex(b.AmbientEquator);
        RenderSettings.ambientGroundColor = Hex(b.AmbientGround);
        RenderSettings.skybox = FogSky();
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Hex(b.Fog);
        RenderSettings.fogDensity = b.FogDensity;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        var subscene = EditorSceneManager.OpenScene(subscenePath, OpenSceneMode.Additive);
        foreach (var root in subscene.GetRootGameObjects())
            foreach (var layout in root.GetComponentsInChildren<LevelLayoutAuthoring>(true))
            {
                layout.TileSet = tileSet;
                layout.Border = b.Border;
                EditorUtility.SetDirty(layout);
            }
        EditorSceneManager.MarkSceneDirty(subscene);
        EditorSceneManager.SaveScene(subscene);
        EditorSceneManager.CloseScene(subscene, true);
        Debug.Log($"{scenePath} now uses the {b.Name} biome.");
    }

    /// <summary>The shared sky material that shows the scene's fog color (PerfectLoot/FogSky).</summary>
    static Material FogSky()
    {
        const string path = "Assets/ArtStyleTest/Materials/PL_FogSky.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("PerfectLoot/FogSky"));
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // ---------- helpers ----------

    static int StableHash(string text)
    {
        int hash = 17;
        foreach (char c in text) hash = hash * 31 + c;
        return hash % 997;
    }

    static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.magenta;

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
