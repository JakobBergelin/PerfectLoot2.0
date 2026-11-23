using UnityEditor;
using UnityEngine;
using static VfxGraphKit;

/// <summary>
/// Builds the fireball's VFX graphs (art guide section 13: hard-edged stylized fire blobs in bands from a white-hot core
/// to yellow, orange and a red fringe, breaking into embers): Fireball.vfx flies with the orb (a ribbon trail, fire blobs
/// and rising embers) and FireballImpact.vfx bursts where it lands (fire blobs, sparks bouncing off the ground and dark
/// smoke puffs). Behavior in HLSL/Fire; VfxInstance sets Origin and GroundY. VfxBuilder puts them in the fireball's
/// prefabs with the core and the light.
/// </summary>
static class FireGraphBuilder
{
    public const string Fireball = Root + "/Fireball.vfx", Impact = Root + "/FireballImpact.vfx";
    const string Hlsl = Root + "/HLSL/Fire";

    [MenuItem("Tools/VFX/Build Fire Graphs")]
    public static void Build()
    {
        var puff = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/VFXG_Puff.png");
        var mote = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/VFXG_Mote.png");
        var dot = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/VFXG_Dot.png");

        // The orb in flight
        var (resource, graph) = New(Fireball);
        var origin = Exposed(graph, "Origin", Vector3.zero);
        var trail = Particles(graph, Hlsl, "TrailInit", "TrailUpdate", 24, 60f, dot, additive: true, strip: true);
        origin.outputSlots[0].Link(Slot(trail.InitBlock, "origin"));
        var blobs = Particles(graph, Hlsl, "BlobInit", "BlobUpdate", 64, 45f, puff, additive: false, alphaClip: true);
        origin.outputSlots[0].Link(Slot(blobs.InitBlock, "origin"));
        var embers = Particles(graph, Hlsl, "EmberInit", "EmberUpdate", 64, 35f, mote, additive: true);
        origin.outputSlots[0].Link(Slot(embers.InitBlock, "origin"));
        DeltaTime(graph, Slot(embers.UpdateBlock, "deltaTime"));
        Save(resource, Fireball);

        // Where it lands
        (resource, graph) = New(Impact);
        origin = Exposed(graph, "Origin", Vector3.zero);
        var groundY = Exposed(graph, "GroundY", 0f);
        var fire = Particles(graph, Hlsl, "ImpactBlobInit", "ImpactBlobUpdate", 32, 0f, puff, additive: false, alphaClip: true, burst: 20);
        origin.outputSlots[0].Link(Slot(fire.InitBlock, "origin"));
        DeltaTime(graph, Slot(fire.UpdateBlock, "deltaTime"));
        var sparks = Particles(graph, Hlsl, "SparkInit", "SparkUpdate", 48, 0f, mote, additive: true, burst: 28);
        origin.outputSlots[0].Link(Slot(sparks.InitBlock, "origin"));
        groundY.outputSlots[0].Link(Slot(sparks.UpdateBlock, "groundY"));
        DeltaTime(graph, Slot(sparks.UpdateBlock, "deltaTime"));
        var smoke = Particles(graph, Hlsl, "SmokeInit", "SmokeUpdate", 16, 0f, puff, additive: false, alphaClip: true, burst: 7);
        origin.outputSlots[0].Link(Slot(smoke.InitBlock, "origin"));
        Set(smoke.InitBlock, "color", Hex("#3A2E3A"));
        DeltaTime(graph, Slot(smoke.UpdateBlock, "deltaTime"));
        Save(resource, Impact);
        Debug.Log("Fire graphs built");
    }
}
