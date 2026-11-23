using System;
using System.Linq;
using UnityEditor;
using UnityEditor.VFX;
using UnityEditor.VFX.Block;
using UnityEditor.VFX.UI;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Builds VFX Graph assets from code, so effects are made like the rest of the project's art (VfxBuilder) and stay
/// rebuildable: contexts, Custom HLSL blocks (the behavior lives in HLSL files beside the graphs) and exposed
/// parameters that game code sets. The graphs open in the VFX Graph editor like any other, for tweaking by hand.
/// This assembly is named so it can reach the VFX Graph package's editor internals (its InternalsVisibleTo list);
/// it's editor-only tooling and never ships.
/// </summary>
public static class VfxGraphKit
{
    public const string Root = "Assets/Data/Vfx/Graphs";

    /// <summary>A new, empty graph at the path (an existing one is replaced, keeping its GUID).</summary>
    internal static (VisualEffectResource Resource, VFXGraph Graph) New(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path) == null) VisualEffectAssetEditorUtility.CreateNewAsset(path);
        var resource = VisualEffectResource.GetResourceAtPath(path);
        var graph = resource.GetGraph() ?? resource.CreateGraph();
        foreach (var child in graph.children.ToList()) graph.RemoveChild(child);
        return (resource, graph);
    }

    /// <summary>Writes the graph to its asset and compiles it.</summary>
    internal static void Save(VisualEffectResource resource, string path)
    {
        resource.WriteAssetWithSubAssets();
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    internal static T Context<T>(VFXGraph graph) where T : VFXContext
    {
        var context = ScriptableObject.CreateInstance<T>();
        graph.AddChild(context);
        return context;
    }

    internal static T Block<T>(VFXContext context) where T : VFXBlock
    {
        var block = ScriptableObject.CreateInstance<T>();
        context.AddChild(block);
        return block;
    }

    /// <summary>A Custom HLSL block calling the function of an HLSL file (one per file: a block compiles its whole file).</summary>
    internal static CustomHLSL Hlsl(VFXContext context, string hlslPath, string function)
    {
        var block = Block<CustomHLSL>(context);
        block.SetSettingValue("m_ShaderFile", AssetDatabase.LoadAssetAtPath<ShaderInclude>(hlslPath));
        var choice = new MultipleValuesChoice<string> { values = new System.Collections.Generic.List<string> { function } };
        choice.SetSelection(function);
        block.SetSettingValue("m_AvailableFunction", choice);
        block.SetSettingValue("m_BlockName", ObjectNames.NicifyVariableName(function));
        return block;
    }

    /// <summary>The model's input slot by name (or a nested one, "parent/child").</summary>
    internal static VFXSlot Slot(IVFXSlotContainer model, string name)
    {
        var parts = name.Split('/');
        var slot = model.inputSlots.FirstOrDefault(s => s.name == parts[0] || s.name == "_" + parts[0])
                   ?? throw new ArgumentException($"{model} has no input {parts[0]} ({string.Join(", ", model.inputSlots.Select(s => s.name))})");
        foreach (var part in parts.Skip(1)) slot = slot.children.First(c => c.name == part);
        return slot;
    }

    internal static void Set(IVFXSlotContainer model, string name, object value) => Slot(model, name).value = value;

    /// <summary>An exposed parameter game code sets by name, linked to the inputs.</summary>
    internal static VFXParameter Exposed<T>(VFXGraph graph, string name, T value, params VFXSlot[] inputs)
    {
        var parameter = ScriptableObject.CreateInstance<VFXParameter>();
        parameter.Init(typeof(T));
        graph.AddChild(parameter);
        parameter.SetSettingValue("m_ExposedName", name);
        parameter.SetSettingValue("m_Exposed", true);
        parameter.value = value;
        foreach (var input in inputs) parameter.outputSlots[0].Link(input);
        return parameter;
    }

    internal static void Capacity(VFXContext initialize, uint capacity) => initialize.GetData().SetSettingValue("capacity", capacity);

    /// <summary>The graph's delta time, linked to the inputs.</summary>
    internal static void DeltaTime(VFXGraph graph, params VFXSlot[] inputs)
    {
        var time = ScriptableObject.CreateInstance<VFXDynamicBuiltInParameter>();
        graph.AddChild(time);
        foreach (var input in inputs) time.outputSlots[0].Link(input);
    }

    /// <summary>
    /// One particle system: a spawner (a constant rate, or a single burst of count particles), an Initialize and an Update
    /// context each running one HLSL file of the folder (named after its function: a block compiles its whole file), and a
    /// camera-facing quad output with the texture, or a ribbon (strip) through the particles in spawn order. World space;
    /// its culling bounds are the exposed BoundsCenter and BoundsSize, or computed (an effect that moves).
    /// </summary>
    internal static (VFXBasicSpawner Spawn, VFXBasicInitialize Init, CustomHLSL InitBlock, VFXBasicUpdate Update, CustomHLSL UpdateBlock, VFXContext Output)
        Particles(VFXGraph graph, string hlsl, string init, string update, uint capacity, float rate, Texture2D texture, bool additive,
                  bool alphaClip = false, (VFXParameter Center, VFXParameter Size)? bounds = null, float burst = 0f, bool strip = false)
    {
        var spawn = Context<VFXBasicSpawner>(graph);
        if (rate > 0f) Set(Block<VFXSpawnerConstantRate>(spawn), "Rate", rate);
        if (burst > 0f) Set(Block<VFXSpawnerBurst>(spawn), "Count", burst);
        var initialize = Context<VFXBasicInitialize>(graph);
        spawn.LinkTo(initialize);
        var data = (VFXDataParticle)initialize.GetData();
        if (strip)
        {
            data.SetSettingValue("dataType", VFXDataParticle.DataType.ParticleStrip);
            data.SetSettingValue("stripCapacity", 1u);
            data.SetSettingValue("particlePerStripCount", capacity);
        }
        else Capacity(initialize, capacity);
        data.space = VFXSpace.World;
        if (bounds is { } b)
        {
            data.SetSettingValue("boundsMode", BoundsSettingMode.Manual);
            b.Center.outputSlots[0].Link(Slot(initialize, "bounds/center"));
            b.Size.outputSlots[0].Link(Slot(initialize, "bounds/size"));
        }
        else data.SetSettingValue("boundsMode", BoundsSettingMode.Automatic);
        var initBlock = Hlsl(initialize, $"{hlsl}/{init}.hlsl", init);
        var updateContext = Context<VFXBasicUpdate>(graph);
        initialize.LinkTo(updateContext);
        var updateBlock = update != null ? Hlsl(updateContext, $"{hlsl}/{update}.hlsl", update) : null;
        VFXContext output = strip ? Context<VFXQuadStripOutput>(graph) : Context<VFXPlanarPrimitiveOutput>(graph);
        updateContext.LinkTo(output);
        output.SetSettingValue("blendMode", additive ? VFXAbstractRenderedOutput.BlendMode.Additive : VFXAbstractRenderedOutput.BlendMode.Alpha);
        if (alphaClip) output.SetSettingValue("useAlphaClipping", true);
        if (texture != null) Set(output, "mainTexture", texture);
        // Quads face the camera; a ribbon turns to face it along its length (without this it stands on edge)
        var orient = Block<Orient>(output);
        if (strip) orient.SetSettingValue("mode", Orient.Mode.FaceCameraPosition);
        return (spawn, initialize, initBlock, updateContext, updateBlock, output);
    }

    /// <summary>
    /// A comic sprite drawn from a signed distance (negative inside), white with a hard, slightly smoothed edge, saved as
    /// a PNG beside the graphs. glow: a soft halo outside the shape (fireflies).
    /// </summary>
    internal static Texture2D Sprite(string name, Func<Vector2, float> distance, float glow = 0f, int size = 128)
    {
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                float d = distance(p);
                float a = Mathf.Clamp01(0.5f - d * size * 0.5f);
                if (glow > 0f) a = Mathf.Max(a, Mathf.Clamp01(1f - d / glow) * Mathf.Clamp01(1f - d / glow) * 0.45f);
                pixels[x + y * size] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        string folder = $"{Root}/Textures";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Root, "Textures");
        string path = $"{folder}/{name}.png";
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    internal static Vector3 Hex(string hex)
    {
        // Graphs work in linear color
        ColorUtility.TryParseHtmlString(hex, out var c);
        c = c.linear;
        return new Vector3(c.r, c.g, c.b);
    }
}
