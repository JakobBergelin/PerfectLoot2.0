using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class VfxBuilder
{
    const string Frost = "#7FE6FF", FrostPale = "#BFF3FF", Poison = "#9CFF4A", Sapphire = "#4A7CFF", Ruby = "#E0364F";

    // Skills that had no look of their own, and the two that borrowed another skill's prefab
    static IEnumerable<(VfxType, GameObject)> SkillFlair()
    {
        var soul = Root_("VFX_SoulOrb", 0.8f, out var soulInstance);
        var soulCore = MeshPart(soul, "Core", Ico("VFX_Ico"), ComicGlow("VFX_SoulCore", "#C8E0FF", "#9FC4FF", 1.8f, 0.5f), Vector3.zero, Vector3.one * 0.42f);
        soulInstance.HideOnRelease = new Renderer[] { soulCore };
        Ps(soul, "Glow", _additive, ps => Main(ps, 0.3f, 0f, 1.2f, Hex("#9FC4FF", 0.4f), local: true, rate: 10f));
        Ps(soul, "Wisps", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.6f, 1f), new ParticleSystem.MinMaxCurve(0.1f, 0.4f), new ParticleSystem.MinMaxCurve(0.12f, 0.24f), Color.white, rate: 9f);
            var m = ps.main; m.gravityModifier = -0.25f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.25f);
            Fade(ps, Hex("#E8F0F8", 0.8f), Hex("#C8E0FF", 0.5f), Hex("#7FA0D0", 0f));
            Shrink(ps);
        });
        yield return (VfxType.SoulOrb, Save(soul));

        // A chunky ruby gem pointing down at the target, big enough to find at gameplay zoom
        var mark = Root_("VFX_QuarryMark", 0.3f, out var markInstance);
        var gem = MeshPart(mark, "Gem", Gem("VFX_Gem"), ComicGlow("VFX_QuarryGem", Ruby, Ruby, 1.8f, 0.4f), new Vector3(0f, 0.3f, 0f), Vector3.one);
        gem.gameObject.AddComponent<VfxSpin>().DegreesPerSecond = 120f;
        markInstance.HideOnRelease = new Renderer[] { gem };
        Ps(mark, "Glow", _additive, ps => Main(ps, 0.3f, 0f, 1.5f, Hex(Ruby, 0.35f), local: true, rate: 8f));
        yield return (VfxType.QuarryMark, Save(mark));

        var orb = Root_("VFX_FrozenOrb", 0.8f, out var orbInstance);
        var orbCore = MeshPart(orb, "Core", Ico("VFX_Ico"), ComicGlow("VFX_FrostCore", FrostPale, Frost, 1.6f, 0.1f), Vector3.zero, Vector3.one * 0.8f);
        orbCore.gameObject.AddComponent<VfxSpin>().DegreesPerSecond = 180f;
        orbInstance.HideOnRelease = new Renderer[] { orbCore };
        Ps(orb, "Flakes", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.7f), new ParticleSystem.MinMaxCurve(0.4f, 1.2f), new ParticleSystem.MinMaxCurve(0.1f, 0.2f), Color.white, rate: 20f);
            Shape(ps, ParticleSystemShapeType.Sphere, 0.45f);
            Fade(ps, Hex("#FFFFFF"), Hex(FrostPale), Hex(Frost, 0f));
            Shrink(ps);
        });
        yield return (VfxType.FrozenOrb, Save(orb));

        var dart = Root_("VFX_PoisonDart", 0.4f, out var dartInstance);
        var dartCore = MeshPart(dart, "Core", Bolt("VFX_Bolt"), ComicGlow("VFX_DartCore", Poison, "#8CFF3A", 1.8f, 0f), Vector3.zero, Vector3.one);
        dartInstance.HideOnRelease = new Renderer[] { dartCore };
        Ps(dart, "Trail", _additive, ps =>
        {
            Main(ps, 0.3f, 0f, 0.18f, Hex(Poison, 0.8f));
            var e = ps.emission; e.rateOverDistance = 12f;
            Shrink(ps);
        });
        yield return (VfxType.PoisonDart, Save(dart));

        // Flamethrower: many at once, so a few soft licks each
        var flame = Root_("VFX_Flame", 0.5f, out _);
        Ps(flame, "Lick", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.2f, 0.4f), new ParticleSystem.MinMaxCurve(0.2f, 0.7f), new ParticleSystem.MinMaxCurve(0.4f, 0.75f), Color.white, rate: 16f);
            var m = ps.main; m.gravityModifier = -0.3f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.2f);
            Fade(ps, Hex("#FFD24A", 0.8f), Hex("#FF7A1A", 0.6f), Hex("#B8301A", 0f));
            Shrink(ps);
        });
        yield return (VfxType.Flame, Save(flame));

        var shield = Root_("VFX_ShieldDisc", 0.3f, out var shieldInstance);
        var disc = MeshPart(shield, "Disc", Ico("VFX_Ico"), ComicGlow("VFX_ShieldDisc", "#E2E8F0", "#BFD6FF", 0.8f, 0f), Vector3.zero, new Vector3(1f, 0.2f, 1f));
        shieldInstance.HideOnRelease = new Renderer[] { disc };
        Ps(shield, "Trail", _additive, ps =>
        {
            Main(ps, 0.25f, 0f, 0.5f, Hex("#BFD6FF", 0.3f));
            var e = ps.emission; e.rateOverDistance = 5f;
            Shrink(ps);
        });
        yield return (VfxType.ShieldDisc, Save(shield));

        // Three small flame heads turning around one point
        var hydra = Root_("VFX_FireHydra", 0.8f, out var hydraInstance);
        var heads = new GameObject("Heads");
        heads.transform.SetParent(hydra.transform, false);
        heads.AddComponent<VfxSpin>().DegreesPerSecond = 90f;
        var hydraParts = new List<Renderer>();
        for (int i = 0; i < 3; i++)
        {
            float angle = i * Mathf.PI * 2f / 3f;
            hydraParts.Add(MeshPart(heads, $"Head{i}", Ico("VFX_Ico"), ComicGlow("VFX_FireCore", "#FF8A2A", "#FF7A1A", 2.2f, 0.3f),
                new Vector3(Mathf.Cos(angle) * 0.38f, 0.1f, Mathf.Sin(angle) * 0.38f), Vector3.one * 0.42f));
        }
        hydraInstance.HideOnRelease = hydraParts.ToArray();
        Ps(hydra, "Glow", _additive, ps => Main(ps, 0.3f, 0f, 2f, Hex("#FF7A1A", 0.3f), local: true, rate: 10f));
        Ps(hydra, "Embers", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.7f), new ParticleSystem.MinMaxCurve(0.3f, 0.8f), new ParticleSystem.MinMaxCurve(0.1f, 0.22f), Color.white, rate: 14f);
            var m = ps.main; m.gravityModifier = -0.3f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.45f);
            Fade(ps, Hex("#FFD24A"), Hex("#FF7A1A"), Hex("#B8301A"));
            Shrink(ps);
        });
        yield return (VfxType.FireHydra, Save(hydra));

        // Both fields sit at unit height (1.05), so -1 puts the ring just above the floor
        yield return (VfxType.TeslaField, Save(Field("VFX_TeslaField", "VFX_TeslaRing", Sapphire, -1f, 2f)));
        yield return (VfxType.TimezoneField, Save(Field("VFX_TimezoneField", "VFX_TimezoneRing", Frost, -1f, 0.6f)));

        // Drawn at radius 1: VfxSystem scales it to the attack's radius. Shape scaling, so only where the particles start
        // grows, not their size or speed. A shard ring marks the edge, frost fills the inside, low over the floor.
        var nova = Root_("VFX_ColdNova", 0.9f, out _);
        Ps(nova, "Flash", _additive, ps =>
        {
            Main(ps, 0.2f, 0f, 3f, Hex(FrostPale, 0.6f), burst: 1);
            var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape;
        });
        Ps(nova, "Shards", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.35f, 0.6f), new ParticleSystem.MinMaxCurve(0.5f, 1.5f), new ParticleSystem.MinMaxCurve(0.18f, 0.32f), Color.white, burst: 40);
            var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape;
            Shape(ps, ParticleSystemShapeType.Circle, 1f, new Vector3(-90f, 0f, 0f), new Vector3(0f, -0.6f, 0f));
            var shape = ps.shape; shape.radiusThickness = 0f;   // From the edge only
            Fade(ps, Hex("#FFFFFF"), Hex(FrostPale), Hex(Frost, 0f));
            Shrink(ps);
        });
        Ps(nova, "Frost", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.7f), new ParticleSystem.MinMaxCurve(0.1f, 0.4f), new ParticleSystem.MinMaxCurve(0.2f, 0.35f), Color.white, burst: 24);
            var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape;
            Shape(ps, ParticleSystemShapeType.Circle, 1f, new Vector3(-90f, 0f, 0f), new Vector3(0f, -0.8f, 0f));
            Fade(ps, Hex("#FFFFFF", 0.6f), Hex(FrostPale, 0.4f), Hex(Frost, 0f));
        });
        yield return (VfxType.ColdNova, Save(nova));
    }

    /// <summary>A ground ring of radius 1 with motes on it, height meters from its entity. VfxManager.Scale sizes it sideways: only the ring and the shape grow, not the motes.</summary>
    static GameObject Field(string name, string ringMaterial, string hex, float height, float speed)
    {
        var root = Root_(name, 0.7f, out var instance);
        var material = ParticleMaterial(ringMaterial, true, null, CullMode.Off);
        material.SetColor("_BaseColor", Hex(hex, 0.5f));
        var ring = MeshPart(root, "Ring", Ring("VFX_Ring"), material, new Vector3(0f, height, 0f), Vector3.one);
        instance.HideOnRelease = new Renderer[] { ring };
        Ps(root, "Motes", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.3f, 0.6f), new ParticleSystem.MinMaxCurve(speed * 0.5f, speed), new ParticleSystem.MinMaxCurve(0.1f, 0.2f), Color.white, rate: 22f);
            var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape;
            Shape(ps, ParticleSystemShapeType.Circle, 1f, new Vector3(-90f, 0f, 0f), new Vector3(0f, height, 0f));
            Fade(ps, Hex("#FFFFFF"), Hex(hex), Hex(hex, 0f));
            Shrink(ps);
        });
        return root;
    }

    // Status effects: tiny one-shots VfxSystem plays on afflicted units a few times a second, so nothing follows each unit
    static IEnumerable<(VfxType, GameObject)> StatusEffects()
    {
        var burn = Root_("VFX_StatusBurn", 0.9f, out _);
        Ps(burn, "Embers", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.5f, 0.8f), new ParticleSystem.MinMaxCurve(0.3f, 0.8f), new ParticleSystem.MinMaxCurve(0.16f, 0.29f), Color.white, burst: 4);
            var m = ps.main; m.gravityModifier = -0.4f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.35f);
            Fade(ps, Hex("#FFD24A"), Hex("#FF7A1A"), Hex("#B8301A"));
            Shrink(ps);
        });
        yield return (VfxType.StatusBurn, Save(burn));

        var poison = Root_("VFX_StatusPoison", 1f, out _);
        Ps(poison, "Bubbles", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.6f, 0.9f), new ParticleSystem.MinMaxCurve(0.2f, 0.5f), new ParticleSystem.MinMaxCurve(0.18f, 0.31f), Color.white, burst: 3);
            var m = ps.main; m.gravityModifier = -0.2f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.35f);
            Fade(ps, Hex("#DFFFB0"), Hex(Poison), Hex("#5E7C46", 0f));
        });
        yield return (VfxType.StatusPoison, Save(poison));

        var chill = Root_("VFX_StatusChill", 0.9f, out _);
        Ps(chill, "Frost", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.5f, 0.8f), new ParticleSystem.MinMaxCurve(0.1f, 0.4f), new ParticleSystem.MinMaxCurve(0.13f, 0.24f), Color.white, burst: 4);
            var m = ps.main; m.gravityModifier = 0.12f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.45f);
            Fade(ps, Hex("#FFFFFF"), Hex(FrostPale), Hex(Frost, 0f));
        });
        yield return (VfxType.StatusChill, Save(chill));

        var bleed = Root_("VFX_StatusBleed", 0.7f, out _);
        Ps(bleed, "Drips", _alpha, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.35f, 0.55f), new ParticleSystem.MinMaxCurve(0.2f, 0.6f), new ParticleSystem.MinMaxCurve(0.16f, 0.24f), Hex("#8E1B2E", 0.95f), burst: 3);
            var m = ps.main; m.gravityModifier = 1.4f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.3f);
            Shrink(ps);
        });
        yield return (VfxType.StatusBleed, Save(bleed));

        var charged = Root_("VFX_StatusCharged", 0.4f, out _);
        Ps(charged, "Sparks", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.12f, 0.22f), new ParticleSystem.MinMaxCurve(2f, 4f), new ParticleSystem.MinMaxCurve(0.08f, 0.13f), Color.white, burst: 4);
            Shape(ps, ParticleSystemShapeType.Sphere, 0.4f);
            Fade(ps, Hex("#FFFFFF"), Hex(Sapphire), Hex("#2A4A9E", 0f));
            Stretch(ps, 3f);
        });
        yield return (VfxType.StatusCharged, Save(charged));
    }

    /// <summary>A long diamond pointing down: 0.95 tall, 0.64 wide.</summary>
    static Mesh Gem(string name)
    {
        const float r = 0.32f;
        var v = new List<Vector3> { new(0, 0.35f, 0), new(0, -0.6f, 0), new(r, 0, 0), new(0, 0, r), new(-r, 0, 0), new(0, 0, -r) };
        var t = new List<int> { 0,3,2, 0,4,3, 0,5,4, 0,2,5, 1,2,3, 1,3,4, 1,4,5, 1,5,2 };
        return SaveMesh(name, v, t);
    }

    /// <summary>A flat ring, radius 1, thin enough to stay a line when scaled up.</summary>
    static Mesh Ring(string name)
    {
        const int steps = 48;
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i <= steps; i++)
        {
            float angle = i / (float)steps * Mathf.PI * 2f;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            v.Add(direction);
            v.Add(direction * 0.95f);
        }
        for (int i = 0; i < steps; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            t.AddRange(new[] { a, b, c, b, d, c });
        }
        return SaveMesh(name, v, t, flat: false);
    }
}
