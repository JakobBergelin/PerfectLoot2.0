using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the skill effects as pooled prefabs for VfxManager: procedural meshes (fireball core, barrage bolt, tornado
/// funnel, whirlwind arc) on PerfectLoot/Comic or additive materials, URP particle systems for embers, sparks, smoke and
/// dust, a VfxLibrary with every prefab, and a VfxManager in the Haunted Cathedral scene. Colours follow the style
/// guide: fire and poison use their trait colours, barrage bolts teal and lightning sapphire (free accent hues).
/// Safe to run again; prefabs and assets keep their GUIDs.
/// </summary>
public static partial class VfxBuilder
{
    const string Root = "Assets/Data/Vfx";
    const string ScenePath = "Assets/Scenes/HauntedCathedral.unity";
    const string GlowSource = "Assets/ArtStyleTest/Materials/ComicCathedral/TR_Shield.mat";   // A glowing comic material to copy

    static Material _additive, _alpha, _arc;
    static Texture2D _dot;

    [MenuItem("Tools/VFX/Build Skill Effects")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        foreach (var folder in new[] { Root, $"{Root}/Textures", $"{Root}/Materials", $"{Root}/Meshes", $"{Root}/Prefabs" }) EnsureFolder(folder);

        _dot = SoftDot();
        _additive = ParticleMaterial("VFX_Additive", true, _dot, CullMode.Back);
        _alpha = ParticleMaterial("VFX_Alpha", false, _dot, CullMode.Back);
        _arc = ParticleMaterial("VFX_Arc", true, null, CullMode.Off);
        _arc.SetColor("_BaseColor", Hex("#E2E8F0", 0.55f));     // A steel blur, not a solid ring

        var prefabs = new Dictionary<VfxType, GameObject>
        {
            [VfxType.FireballOrb] = FireballOrb(),
            [VfxType.FireballImpact] = FireballImpact(),
            [VfxType.BarrageBolt] = BarrageBolt(),
            [VfxType.BarrageImpact] = BarrageImpact(),
            [VfxType.Tornado] = TornadoFunnel(),
            [VfxType.TornadoEnd] = TornadoEnd(),
            [VfxType.LightningHit] = LightningHit(),
            [VfxType.WhirlwindArc] = WhirlwindArc(),
            [VfxType.DashTrail] = DashTrail(),
            [VfxType.DashBurst] = DashBurst(),
        };
        foreach (var (type, prefab) in UnitEffects()) prefabs[type] = prefab;

        string libraryPath = $"{Root}/VfxLibrary.asset";
        var library = AssetDatabase.LoadAssetAtPath<VfxLibrary>(libraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<VfxLibrary>();
            AssetDatabase.CreateAsset(library, libraryPath);
        }
        library.Entries = prefabs.Select(p => new VfxLibrary.Entry { Type = p.Key, Prefab = p.Value, Prewarm = p.Key is VfxType.LightningHit or VfxType.BarrageBolt or VfxType.UnitHit ? 12 : 4 }).ToList();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var manager = scene.GetRootGameObjects().Select(r => r.GetComponent<VfxManager>()).FirstOrDefault(m => m != null);
        if (manager == null) manager = new GameObject("VfxManager").AddComponent<VfxManager>();
        manager.Library = AssetDatabase.LoadAssetAtPath<VfxLibrary>(libraryPath);
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Skill effects built: {prefabs.Count} prefabs in {libraryPath}.");
    }

    // ---------- skill effects ----------

    static GameObject FireballOrb()
    {
        var root = Root_("VFX_FireballOrb", 0.7f, out var instance);
        var core = MeshPart(root, "Core", Ico("VFX_Ico"), ComicGlow("VFX_FireCore", "#FF8A2A", "#FF7A1A", 2.2f, 0.3f), Vector3.zero, Vector3.one * 0.55f);
        instance.HideOnRelease = new Renderer[] { core };
        Ps(root, "Glow", _additive, ps =>
        {
            Main(ps, 0.25f, 0f, 1.4f, Hex("#FF7A1A", 0.5f), local: true, rate: 14f);
        });
        Ps(root, "Embers", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.35f, 0.6f), new ParticleSystem.MinMaxCurve(0.3f, 0.9f), new ParticleSystem.MinMaxCurve(0.12f, 0.3f), Color.white, rate: 24f);
            var e = ps.emission; e.rateOverDistance = 14f;
            var m = ps.main; m.gravityModifier = -0.3f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.2f);
            Fade(ps, Hex("#FFD24A"), Hex("#FF7A1A"), Hex("#B8301A"));
            Shrink(ps);
        });
        return Save(root);
    }

    static GameObject FireballImpact()
    {
        var root = Root_("VFX_FireballImpact", 1.4f, out _);
        Ps(root, "Flash", _additive, ps => { Main(ps, 0.18f, 0f, 3.2f, Hex("#FFB040", 0.8f), burst: 1); Fade(ps, Hex("#FFE08A"), Hex("#FF7A1A"), Hex("#FF4A1C")); });
        Ps(root, "Sparks", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.25f, 0.5f), new ParticleSystem.MinMaxCurve(5f, 9f), new ParticleSystem.MinMaxCurve(0.08f, 0.16f), Color.white, burst: 28);
            Shape(ps, ParticleSystemShapeType.Sphere, 0.2f);
            Fade(ps, Hex("#FFE08A"), Hex("#FF8A2A"), Hex("#B8301A"));
            Stretch(ps, 2.5f);
        });
        Ps(root, "Smoke", _alpha, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.8f, 1.2f), new ParticleSystem.MinMaxCurve(0.5f, 1.5f), new ParticleSystem.MinMaxCurve(0.8f, 1.6f), Color.white, burst: 8);
            var m = ps.main; m.gravityModifier = -0.15f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.3f);
            Fade(ps, Hex("#3A2E3A", 0.7f), Hex("#2A2230", 0.5f), Hex("#2A2230", 0f));
            Grow(ps, 1.8f);
        });
        return Save(root);
    }

    static GameObject BarrageBolt()
    {
        var root = Root_("VFX_BarrageBolt", 0.4f, out var instance);
        var core = MeshPart(root, "Core", Bolt("VFX_Bolt"), ComicGlow("VFX_BoltCore", "#4FD6C8", "#4FD6C8", 2.2f, 0f), Vector3.zero, Vector3.one);
        instance.HideOnRelease = new Renderer[] { core };
        Ps(root, "Trail", _additive, ps =>
        {
            Main(ps, 0.3f, 0f, 0.2f, Hex("#4FD6C8", 0.8f), rate: 0f);
            var e = ps.emission; e.rateOverDistance = 20f;
            Shrink(ps);
            Fade(ps, Hex("#BFFFF6"), Hex("#4FD6C8"), Hex("#1C4A54", 0f));
        });
        return Save(root);
    }

    static GameObject BarrageImpact()
    {
        var root = Root_("VFX_BarrageImpact", 0.6f, out _);
        Ps(root, "Flash", _additive, ps => { Main(ps, 0.12f, 0f, 1.2f, Hex("#4FD6C8", 0.8f), burst: 1); });
        Ps(root, "Sparks", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.2f, 0.35f), new ParticleSystem.MinMaxCurve(3f, 6f), new ParticleSystem.MinMaxCurve(0.06f, 0.12f), Color.white, burst: 12);
            Shape(ps, ParticleSystemShapeType.Sphere, 0.1f);
            Fade(ps, Hex("#BFFFF6"), Hex("#4FD6C8"), Hex("#1C4A54", 0f));
            Stretch(ps, 2f);
        });
        return Save(root);
    }

    static GameObject TornadoFunnel()
    {
        var root = Root_("VFX_Tornado", 1.2f, out var instance);
        var funnel = MeshPart(root, "Funnel", Funnel("VFX_Funnel"), ComicGlow("VFX_PoisonFunnel", "#9CFF4A", "#8CFF3A", 0.9f, 0.3f), new Vector3(0f, -0.9f, 0f), Vector3.one);
        funnel.gameObject.AddComponent<VfxSpin>().DegreesPerSecond = 540f;
        instance.HideOnRelease = new Renderer[] { funnel };
        Ps(root, "Dust", _alpha, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.8f, 1.2f), 0f, new ParticleSystem.MinMaxCurve(0.4f, 0.8f), Color.white, rate: 30f, local: true);
            Shape(ps, ParticleSystemShapeType.Circle, 0.9f, new Vector3(-90f, 0f, 0f), new Vector3(0f, -0.9f, 0f));
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
            v.orbitalY = 4f; v.radial = -0.3f; v.y = 1.5f;
            v.x = v.z = 0f; v.orbitalX = v.orbitalZ = 0f;
            Fade(ps, Hex("#6E7A5C", 0.5f), Hex("#4A5A3E", 0.4f), Hex("#3A4438", 0f));
            Grow(ps, 1.5f);
        });
        Ps(root, "Droplets", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.7f), new ParticleSystem.MinMaxCurve(0.5f, 1.5f), new ParticleSystem.MinMaxCurve(0.08f, 0.15f), Color.white, rate: 18f);
            Shape(ps, ParticleSystemShapeType.Circle, 0.6f, new Vector3(-90f, 0f, 0f), new Vector3(0f, -0.6f, 0f));
            var m = ps.main; m.gravityModifier = -0.4f;
            Fade(ps, Hex("#DFFFB0"), Hex("#9CFF4A"), Hex("#5E7C46", 0f));
        });
        return Save(root);
    }

    static GameObject TornadoEnd()
    {
        var root = Root_("VFX_TornadoEnd", 1.2f, out _);
        Ps(root, "Puff", _alpha, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.6f, 1f), new ParticleSystem.MinMaxCurve(0.5f, 2f), new ParticleSystem.MinMaxCurve(0.5f, 1f), Color.white, burst: 14);
            Shape(ps, ParticleSystemShapeType.Sphere, 0.6f, offset: new Vector3(0f, -0.4f, 0f));
            Fade(ps, Hex("#6E7A5C", 0.5f), Hex("#4A5A3E", 0.3f), Hex("#3A4438", 0f));
            Grow(ps, 1.6f);
        });
        return Save(root);
    }

    static GameObject LightningHit()
    {
        var root = Root_("VFX_LightningHit", 0.4f, out _);
        Ps(root, "Flash", _additive, ps => { Main(ps, 0.1f, 0f, 1.6f, Hex("#E6EEFF", 0.9f), burst: 1); });
        Ps(root, "Bolts", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.12f, 0.2f), new ParticleSystem.MinMaxCurve(6f, 10f), new ParticleSystem.MinMaxCurve(0.06f, 0.1f), Color.white, burst: 8);
            Shape(ps, ParticleSystemShapeType.Sphere, 0.15f);
            Fade(ps, Hex("#FFFFFF"), Hex("#4A7CFF"), Hex("#2A4A9E", 0f));
            Stretch(ps, 4f);
        });
        Ps(root, "Crackle", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.08f, 0.16f), new ParticleSystem.MinMaxCurve(1f, 3f), new ParticleSystem.MinMaxCurve(0.1f, 0.2f), Hex("#9EB8FF"), burst: 10);
            Shape(ps, ParticleSystemShapeType.Sphere, 0.5f);
        });
        return Save(root);
    }

    static GameObject WhirlwindArc()
    {
        var root = Root_("VFX_WhirlwindArc", 0.4f, out var instance);
        var arc = MeshPart(root, "Arc", Arc("VFX_ArcMesh"), _arc, new Vector3(0f, -0.1f, 0f), Vector3.one);
        arc.gameObject.AddComponent<VfxSpin>().DegreesPerSecond = 900f;
        instance.HideOnRelease = new Renderer[] { arc };
        Ps(root, "Sparks", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.2f, 0.35f), new ParticleSystem.MinMaxCurve(1f, 3f), new ParticleSystem.MinMaxCurve(0.05f, 0.1f), Color.white, rate: 40f);
            Shape(ps, ParticleSystemShapeType.Circle, 1.25f, new Vector3(-90f, 0f, 0f), new Vector3(0f, -0.1f, 0f));
            Fade(ps, Hex("#FFFFFF"), Hex("#E2E8F0"), Hex("#8A8F99", 0f));
            Stretch(ps, 2f);
        });
        return Save(root);
    }

    static GameObject DashTrail()
    {
        var root = Root_("VFX_DashTrail", 0.6f, out _);
        Ps(root, "Afterimage", _additive, ps =>
        {
            Main(ps, 0.3f, 0f, 1.6f, Hex("#BFD6FF", 0.3f), rate: 0f);
            var e = ps.emission; e.rateOverDistance = 6f;
            Shrink(ps);
        });
        Ps(root, "Dust", _alpha, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.7f), new ParticleSystem.MinMaxCurve(0.3f, 1f), new ParticleSystem.MinMaxCurve(0.3f, 0.6f), Color.white, rate: 0f);
            var e = ps.emission; e.rateOverDistance = 8f;
            Shape(ps, ParticleSystemShapeType.Circle, 0.3f, new Vector3(-90f, 0f, 0f), new Vector3(0f, -0.9f, 0f));
            Fade(ps, Hex("#6A6278", 0.5f), Hex("#4A4458", 0.3f), Hex("#2E2C42", 0f));
            Grow(ps, 1.5f);
        });
        return Save(root);
    }

    static GameObject DashBurst()
    {
        var root = Root_("VFX_DashBurst", 0.8f, out _);
        Ps(root, "Ring", _alpha, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.7f), new ParticleSystem.MinMaxCurve(2f, 4f), new ParticleSystem.MinMaxCurve(0.3f, 0.6f), Color.white, burst: 16);
            Shape(ps, ParticleSystemShapeType.Circle, 0.3f, new Vector3(-90f, 0f, 0f), new Vector3(0f, -0.9f, 0f));
            Fade(ps, Hex("#6A6278", 0.6f), Hex("#4A4458", 0.3f), Hex("#2E2C42", 0f));
            Grow(ps, 1.6f);
        });
        return Save(root);
    }

    // ---------- particle helpers ----------

    static GameObject Root_(string name, float linger, out VfxInstance instance)
    {
        var root = new GameObject(name);
        instance = root.AddComponent<VfxInstance>();
        instance.Linger = linger;
        return root;
    }

    static GameObject Save(GameObject root)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Root}/Prefabs/{root.name}.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static ParticleSystem Ps(GameObject parent, string name, Material material, Action<ParticleSystem> configure)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var shape = ps.shape; shape.enabled = false;
        configure(ps);
        return ps;
    }

    /// <summary>Core settings. Looping with rate, or a single burst (burst > 0) that isn't looping.</summary>
    static void Main(ParticleSystem ps, ParticleSystem.MinMaxCurve lifetime, ParticleSystem.MinMaxCurve speed, ParticleSystem.MinMaxCurve size,
                     Color color, float rate = 0f, int burst = 0, bool local = false)
    {
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = burst == 0;
        main.duration = burst == 0 ? 1f : 0.1f;
        main.startLifetime = lifetime;
        main.startSpeed = speed;
        main.startSize = size;
        main.startColor = color;
        main.simulationSpace = local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var emission = ps.emission;
        emission.rateOverTime = burst == 0 ? rate : 0f;
        emission.rateOverDistance = 0f;
        emission.SetBursts(burst > 0 ? new[] { new ParticleSystem.Burst(0f, (short)burst) } : Array.Empty<ParticleSystem.Burst>());
    }

    static void Shape(ParticleSystem ps, ParticleSystemShapeType type, float radius, Vector3 rotation = default, Vector3 offset = default)
    {
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = type;
        shape.radius = radius;
        shape.rotation = rotation;
        shape.position = offset;
    }

    static void Fade(ParticleSystem ps, Color start, Color middle, Color end)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(start, 0f), new GradientColorKey(middle, 0.45f), new GradientColorKey(end, 1f) },
            new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(middle.a, 0.45f), new GradientAlphaKey(0f, 1f) });
        col.color = gradient;
    }

    static void Shrink(ParticleSystem ps)
    {
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
    }

    static void Grow(ParticleSystem ps, float to)
    {
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, to));
    }

    static void Stretch(ParticleSystem ps, float length)
    {
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = length;
        renderer.velocityScale = 0.05f;
    }

    static MeshRenderer MeshPart(GameObject parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return renderer;
    }

    // ---------- materials and textures ----------

    static Color Hex(string hex, float alpha = 1f)
    {
        ColorUtility.TryParseHtmlString(hex, out var c);
        c.a = alpha;
        return c;
    }

    static Texture2D SoftDot()
    {
        string path = $"{Root}/Textures/VFX_SoftDot.png";
        if (!File.Exists(path))
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    // A firm core with a short soft edge: reads as a comic blob rather than a haze
                    a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(a * 2.2f));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>URP Particles/Unlit, transparent: additive (glows) or alpha blended (smoke, dust); colour from the particles.</summary>
    static Material ParticleMaterial(string name, bool additive, Texture2D texture, CullMode cull)
    {
        string path = $"{Root}/Materials/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", additive ? 2f : 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)cull);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>A glowing PerfectLoot/Comic copy: flat colour, no hatching, emission, ink outline.</summary>
    static Material ComicGlow(string name, string baseHex, string emissionHex, float emission, float pulse)
    {
        string path = $"{Root}/Materials/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(AssetDatabase.LoadAssetAtPath<Material>(GlowSource));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", Hex(baseHex));
        material.SetColor("_EmissionColor", Hex(emissionHex) * emission);
        material.SetFloat("_EmissionPulse", pulse);
        material.SetFloat("_PatternStrength", 0f);
        material.SetFloat("_OutlineWidth", 2.5f);
        material.SetFloat("_UseSmoothNormals", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }
}
