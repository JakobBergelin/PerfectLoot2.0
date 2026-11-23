using UnityEditor;
using UnityEditor.VFX;
using UnityEngine;
using static VfxGraphKit;

/// <summary>
/// Builds Brightwater Hollow's ambient graph (LevelAmbientGraph): fireflies in the forest, pollen over the clearings,
/// leaves falling at the forest's edge, mist drifting over the water and splashes where the rivers pass bridges and
/// rocks. Behavior in HLSL/Ambient (one file per function); LevelAmbientGraphView sets the exposed level data. Colors follow the art guide:
/// fireflies warm candle (a free accent, kept pale so it isn't Summoner gold), leaves the forest kit's greens and a
/// yellow, mist the biome's mist color.
/// </summary>
static class AmbientGraphBuilder
{
    public const string Path = Root + "/BrightwaterHollow_Ambient.vfx";
    const string Hlsl = Root + "/HLSL/Ambient";

    [MenuItem("Tools/VFX/Build Ambient Graphs")]
    public static void Build()
    {
        var dot = Sprite("VFXG_Dot", p => p.magnitude - 0.35f, glow: 0.9f);
        var mote = Sprite("VFXG_Mote", p => p.magnitude - 0.8f);
        // A leaf: two circles' overlap, pointed at both ends, with a short stem
        var leaf = Sprite("VFXG_Leaf", p =>
        {
            float body = Mathf.Max((p - new Vector2(0.55f, 0f)).magnitude, (p + new Vector2(0.55f, 0f)).magnitude) - 1.15f;
            float stem = Mathf.Max(Mathf.Abs(p.x) - 0.05f, Mathf.Abs(p.y + 0.85f) - 0.15f);
            return Mathf.Min(body, stem);
        });
        // A cartoon cloud puff: a few overlapping circles with a flat-ish bottom
        var puff = Sprite("VFXG_Puff", p =>
        {
            float d = (p - new Vector2(0f, 0.05f)).magnitude - 0.48f;
            d = Mathf.Min(d, (p - new Vector2(-0.42f, -0.12f)).magnitude - 0.36f);
            d = Mathf.Min(d, (p - new Vector2(0.44f, -0.1f)).magnitude - 0.38f);
            d = Mathf.Min(d, (p - new Vector2(-0.12f, 0.38f)).magnitude - 0.3f);
            return Mathf.Max(d, -p.y - 0.42f);
        });

        var (resource, graph) = New(Path);
        var center = Exposed(graph, "BoundsCenter", Vector3.zero);
        var size = Exposed(graph, "BoundsSize", Vector3.one * 100f);
        var bounds = (center, size);
        var forest = Exposed<Texture2D>(graph, "Forest", Texture2D.blackTexture);
        var forestMatrix = Exposed(graph, "ForestMatrix", Matrix4x4.identity);
        var ground = Exposed<Texture2D>(graph, "Ground", Texture2D.blackTexture);
        var groundMatrix = Exposed(graph, "GroundMatrix", Matrix4x4.identity);
        var water = Exposed<GraphicsBuffer>(graph, "Water", null);
        var waterCount = Exposed(graph, "WaterCount", 0u);
        var splashes = Exposed<GraphicsBuffer>(graph, "Splashes", null);
        var splashCount = Exposed(graph, "SplashCount", 0u);

        void Level(VFXModel block)
        {
            var b = (IVFXSlotContainer)block;
            center.outputSlots[0].Link(Slot(b, "boundsCenter"));
            size.outputSlots[0].Link(Slot(b, "boundsSize"));
            forest.outputSlots[0].Link(Slot(b, "forest"));
            forestMatrix.outputSlots[0].Link(Slot(b, "forestMatrix"));
            ground.outputSlots[0].Link(Slot(b, "ground"));
            groundMatrix.outputSlots[0].Link(Slot(b, "groundMatrix"));
        }

        var fireflies = Particles(graph, Hlsl, "FireflyInit", "FireflyUpdate", 1200, 160f, dot, additive: true, bounds: bounds);
        Level(fireflies.InitBlock);
        Set(fireflies.InitBlock, "color", Hex("#FFE9B8") * 3f);

        var pollen = Particles(graph, Hlsl, "PollenInit", "PollenUpdate", 2500, 250f, mote, additive: false, bounds: bounds);
        Level(pollen.InitBlock);
        Set(pollen.InitBlock, "color", Hex("#FFF6DC"));

        var leaves = Particles(graph, Hlsl, "LeafInit", "LeafUpdate", 1400, 90f, leaf, additive: false, alphaClip: true, bounds: bounds);
        Level(leaves.InitBlock);
        Set(leaves.InitBlock, "colorA", Hex("#4F8F3A"));
        Set(leaves.InitBlock, "colorB", Hex("#8DB840"));
        Set(leaves.InitBlock, "colorC", Hex("#D9A93A"));
        ground.outputSlots[0].Link(Slot(leaves.UpdateBlock, "ground"));
        groundMatrix.outputSlots[0].Link(Slot(leaves.UpdateBlock, "groundMatrix"));
        DeltaTime(graph, Slot(leaves.UpdateBlock, "deltaTime"));

        var mist = Particles(graph, Hlsl, "MistInit", "MistUpdate", 300, 14f, puff, additive: false, bounds: bounds);
        water.outputSlots[0].Link(Slot(mist.InitBlock, "water"));
        waterCount.outputSlots[0].Link(Slot(mist.InitBlock, "waterCount"));
        Set(mist.InitBlock, "color", Hex("#D8EEE6"));
        DeltaTime(graph, Slot(mist.UpdateBlock, "deltaTime"));

        var splash = Particles(graph, Hlsl, "SplashInit", "SplashUpdate", 800, 160f, mote, additive: false, bounds: bounds);
        splashes.outputSlots[0].Link(Slot(splash.InitBlock, "splashes"));
        splashCount.outputSlots[0].Link(Slot(splash.InitBlock, "splashCount"));
        Set(splash.InitBlock, "color", Hex("#EAFBF6"));
        DeltaTime(graph, Slot(splash.UpdateBlock, "deltaTime"));

        Save(resource, Path);
        Debug.Log($"Ambient graph built: {Path}");
    }
}
