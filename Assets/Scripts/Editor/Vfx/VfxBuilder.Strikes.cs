using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Strike effects: the generic look of a player's area attack landing, so every skill that makes a shaped attack reads
/// without a dedicated effect. VfxSystems picks one by the attack's shape and tints it by damage type:
/// a slash arc for cones (sized to the cone's reach), a shockwave ring on the ground for big round attacks (sized to
/// the radius) and a thrust streak for rectangles (sized to their length). All are drawn at size 1 and white, so the tint
/// and VfxManager.Scale make each one fit.
/// </summary>
public static partial class VfxBuilder
{
    static IEnumerable<(VfxType, GameObject)> StrikeEffects()
    {
        var strike = ParticleMaterial("VFX_Strike", true, null, CullMode.Off);
        yield return (VfxType.StrikeSlash, StrikeSlash(strike));
        yield return (VfxType.StrikeShock, StrikeShock(strike));
        yield return (VfxType.StrikeThrust, StrikeThrust(strike));
    }

    // A crescent sweeping across the front, with sparks flying off its edge
    static GameObject StrikeSlash(Material strike)
    {
        var root = Root_("VFX_StrikeSlash", 0.5f, out _);
        var crescent = Crescent("VFX_StrikeCrescent", 0.62f, 1f, 150f);
        Ps(root, "Blade", strike, ps =>
        {
            Main(ps, 0.2f, 0f, 1f, Color.white, burst: 1, local: true);
            MeshParticle(ps, crescent);
            ps.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.75f, 1f, 1.08f));
            var spin = ps.rotationOverLifetime; spin.enabled = true; spin.separateAxes = true;
            spin.y = new ParticleSystem.MinMaxCurve(Mathf.Deg2Rad * 260f);
            Fade(ps, Hex("#FFFFFF", 0.95f), Hex("#FFFFFF", 0.7f), Hex("#FFFFFF", 0f));
        });
        Ps(root, "Edge", strike, ps =>
        {
            Main(ps, 0.14f, 0f, 1f, Color.white, burst: 1, local: true);
            MeshParticle(ps, Crescent("VFX_StrikeCrescentThin", 0.9f, 1.02f, 130f));
            ps.transform.localPosition = new Vector3(0f, -0.4f, 0f);
            Fade(ps, Hex("#FFFFFF", 1f), Hex("#FFFFFF", 0.8f), Hex("#FFFFFF", 0f));
        });
        Ps(root, "Sparks", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.15f, 0.3f), new ParticleSystem.MinMaxCurve(3f, 7f), new ParticleSystem.MinMaxCurve(0.04f, 0.08f), Color.white, burst: 12);
            Shape(ps, ParticleSystemShapeType.Circle, 0.95f, new Vector3(-90f, 0f, 0f), new Vector3(0f, -0.4f, 0f));
            var shape = ps.shape; shape.arc = 140f; shape.rotation = new Vector3(-90f, 0f, -20f);
            Fade(ps, Hex("#FFFFFF"), Hex("#FFFFFF", 0.8f), Hex("#FFFFFF", 0f));
            Stretch(ps, 2.5f);
        });
        return Save(root);
    }

    // A ring racing outward along the ground, a puff of dust at its edge and chips thrown up
    static GameObject StrikeShock(Material strike)
    {
        var root = Root_("VFX_StrikeShock", 0.9f, out _);
        var ring = Crescent("VFX_StrikeRing", 0.86f, 1f, 360f);
        Ps(root, "Ring", strike, ps =>
        {
            Main(ps, 0.32f, 0f, 1f, Color.white, burst: 1, local: true);
            MeshParticle(ps, ring);
            ps.transform.localPosition = new Vector3(0f, -0.95f, 0f);
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.15f, 0f, 4f), new Keyframe(1f, 1.05f)));
            Fade(ps, Hex("#FFFFFF", 0.9f), Hex("#FFFFFF", 0.55f), Hex("#FFFFFF", 0f));
        });
        Ps(root, "Flash", strike, ps =>
        {
            Main(ps, 0.12f, 0f, 1f, Color.white, burst: 1, local: true);
            MeshParticle(ps, Crescent("VFX_StrikeDisc", 0f, 0.45f, 360f));
            ps.transform.localPosition = new Vector3(0f, -0.93f, 0f);
            Fade(ps, Hex("#FFFFFF", 0.7f), Hex("#FFFFFF", 0.3f), Hex("#FFFFFF", 0f));
        });
        Ps(root, "Dust", _alpha, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.45f, 0.8f), new ParticleSystem.MinMaxCurve(0.5f, 1.5f), new ParticleSystem.MinMaxCurve(0.35f, 0.7f), Color.white, burst: 18);
            Shape(ps, ParticleSystemShapeType.Circle, 0.9f, new Vector3(-90f, 0f, 0f), new Vector3(0f, -0.85f, 0f));
            Fade(ps, Hex("#9A90A8", 0.55f), Hex("#6A6278", 0.3f), Hex("#2E2C42", 0f));
            Grow(ps, 1.8f);
        });
        Ps(root, "Chips", _alpha, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.65f), new ParticleSystem.MinMaxCurve(2f, 5f), new ParticleSystem.MinMaxCurve(0.07f, 0.14f), Hex("#4A4458"), burst: 10);
            Shape(ps, ParticleSystemShapeType.Cone, 0.5f, new Vector3(-90f, 0f, 0f), new Vector3(0f, -0.9f, 0f));
            var shape = ps.shape; shape.angle = 40f;
            var main = ps.main; main.gravityModifier = 2.2f;
            Fade(ps, Hex("#5A5468"), Hex("#4A4458"), Hex("#2E2C42", 0f));
        });
        return Save(root);
    }

    // A thin bright streak along the attack's length, with sparks shed along it
    static GameObject StrikeThrust(Material strike)
    {
        var root = Root_("VFX_StrikeThrust", 0.45f, out _);
        Ps(root, "Streak", strike, ps =>
        {
            Main(ps, 0.18f, 0f, 1f, Color.white, burst: 1, local: true);
            MeshParticle(ps, Streak("VFX_StrikeStreak"));
            ps.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.05f));
            Fade(ps, Hex("#FFFFFF", 1f), Hex("#FFFFFF", 0.7f), Hex("#FFFFFF", 0f));
        });
        Ps(root, "Sparks", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.15f, 0.3f), new ParticleSystem.MinMaxCurve(1.5f, 4f), new ParticleSystem.MinMaxCurve(0.04f, 0.08f), Color.white, burst: 10);
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.15f, 0.1f, 2f); shape.position = new Vector3(0f, -0.45f, 0f);
            Fade(ps, Hex("#FFFFFF"), Hex("#FFFFFF", 0.8f), Hex("#FFFFFF", 0f));
            Stretch(ps, 2f);
        });
        return Save(root);
    }

    /// <summary>A flat band in the ground plane between two radii over sweep degrees centred on +Z, tapered at both ends.</summary>
    static Mesh Crescent(string name, float inner, float outer, float sweep)
    {
        const int steps = 32;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        bool closed = sweep >= 359f;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float angle = Mathf.Deg2Rad * (90f + sweep * 0.5f - sweep * t);
            float taper = closed ? 1f : Mathf.Sin(t * Mathf.PI);
            float a = Mathf.Lerp(outer, inner, taper), b = outer;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices.Add(direction * a);
            vertices.Add(direction * b);
            if (i == 0) continue;
            int k = i * 2;
            triangles.AddRange(new[] { k - 2, k - 1, k + 1, k - 2, k + 1, k });
        }
        return SaveMesh(name, vertices, triangles, flat: false);
    }

    /// <summary>A long thin diamond along Z from -1 to 1.</summary>
    static Mesh Streak(string name)
    {
        var vertices = new List<Vector3> { new Vector3(0f, 0f, -1f), new Vector3(0.09f, 0f, -0.2f), new Vector3(0f, 0f, 1f), new Vector3(-0.09f, 0f, -0.2f) };
        var triangles = new List<int> { 0, 1, 2, 0, 2, 3 };
        return SaveMesh(name, vertices, triangles, flat: false);
    }

    /// <summary>Renders the system's particles as the mesh, turned with the effect (forward along +Z).</summary>
    static void MeshParticle(ParticleSystem ps, Mesh mesh)
    {
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
    }
}
