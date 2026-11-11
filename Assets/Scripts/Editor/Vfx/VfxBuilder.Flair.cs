using System.Collections.Generic;
using UnityEditor;
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

    // Loot, souls, portals and spawning. The loot effects are white: GroundLootVfx tints them to the item's rarity.
    static IEnumerable<(VfxType, GameObject)> JuiceEffects()
    {
        var up = new Vector3(-90f, 0f, 0f);     // Turns a cone's Z up, and lays a circle flat

        var beam = Root_("VFX_LootBeam", 1.2f, out _);
        Ps(beam, "Column", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.9f, 1.3f), new ParticleSystem.MinMaxCurve(1.6f, 2.4f), new ParticleSystem.MinMaxCurve(0.25f, 0.4f), Color.white, rate: 10f);
            Shape(ps, ParticleSystemShapeType.Cone, 0.12f, up);
            var shape = ps.shape; shape.angle = 0f;
            Fade(ps, Hex("#FFFFFF", 0.7f), Hex("#FFFFFF", 0.5f), Hex("#FFFFFF", 0f));
            Stretch(ps, 4f);
        });
        Ps(beam, "Glow", _additive, ps =>
        {
            Main(ps, 0.4f, 0f, 0.9f, Color.white, local: true, rate: 6f);
            Shape(ps, ParticleSystemShapeType.Sphere, 0.01f, default, new Vector3(0f, 0.15f, 0f));
            Fade(ps, Hex("#FFFFFF", 0.35f), Hex("#FFFFFF", 0.35f), Hex("#FFFFFF", 0f));   // Alpha here: Tint sets the start color
        });
        Ps(beam, "Motes", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(1f, 1.6f), new ParticleSystem.MinMaxCurve(0.1f, 0.3f), new ParticleSystem.MinMaxCurve(0.08f, 0.14f), Color.white, rate: 6f);
            var m = ps.main; m.gravityModifier = -0.3f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.35f, default, new Vector3(0f, 0.3f, 0f));
            Fade(ps, Hex("#FFFFFF"), Hex("#FFFFFF", 0.7f), Hex("#FFFFFF", 0f));
            Shrink(ps);
        });
        yield return (VfxType.LootBeam, Save(beam));

        var pickup = Root_("VFX_LootPickup", 1f, out _);
        Ps(pickup, "Flash", _additive, ps =>
        {
            Main(ps, 0.2f, 0f, 1.4f, Color.white, burst: 1);
            Fade(ps, Hex("#FFFFFF", 0.5f), Hex("#FFFFFF", 0.4f), Hex("#FFFFFF", 0f));
        });
        Ps(pickup, "Sparkles", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.7f), new ParticleSystem.MinMaxCurve(2f, 4f), new ParticleSystem.MinMaxCurve(0.1f, 0.2f), Color.white, burst: 18);
            var m = ps.main; m.gravityModifier = -0.5f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.2f, default, new Vector3(0f, 0.3f, 0f));
            Fade(ps, Hex("#FFFFFF"), Hex("#FFFFFF", 0.8f), Hex("#FFFFFF", 0f));
            Shrink(ps);
        });
        yield return (VfxType.LootPickup, Save(pickup));

        // In the soul orb's colours
        var pop = Root_("VFX_SoulPop", 0.9f, out _);
        Ps(pop, "Flash", _additive, ps => Main(ps, 0.2f, 0f, 1.2f, Hex("#9FC4FF", 0.5f), burst: 1));
        Ps(pop, "Wisps", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.7f), new ParticleSystem.MinMaxCurve(1.5f, 3f), new ParticleSystem.MinMaxCurve(0.12f, 0.24f), Color.white, burst: 14);
            var m = ps.main; m.gravityModifier = -0.3f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.2f);
            Fade(ps, Hex("#E8F0F8"), Hex("#C8E0FF", 0.6f), Hex("#7FA0D0", 0f));
            Shrink(ps);
        });
        yield return (VfxType.SoulPop, Save(pop));

        // Motes circling into the portal's ring (upright, facing Z, its centre 1.32 m up). Local, so it turns with the portal.
        // From a thick ring rather than a line, so the portal's opaque surface hides only the motes behind it.
        var swirl = Root_("VFX_PortalSwirl", 1.6f, out _);
        Ps(swirl, "Swirl", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(1.2f, 1.6f), 0f, new ParticleSystem.MinMaxCurve(0.12f, 0.22f), Color.white, rate: 26f, local: true);
            Shape(ps, ParticleSystemShapeType.Donut, 1.05f, default, new Vector3(0f, 1.32f, 0f));
            var shape = ps.shape; shape.donutRadius = 0.3f;
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = velocity.y = velocity.z = new ParticleSystem.MinMaxCurve(0f);
            velocity.orbitalX = velocity.orbitalY = new ParticleSystem.MinMaxCurve(0f);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(1.2f);
            velocity.radial = new ParticleSystem.MinMaxCurve(-0.5f);
            Fade(ps, Hex("#E0C8FF", 0.9f), Hex("#8C4DFF", 0.7f), Hex("#5A2A9E", 0f));
        });
        yield return (VfxType.PortalSwirl, Save(swirl));

        // At the player's centre (1.05 m up): warm candle light rising from the feet, and a puff of dust
        var spawn = Root_("VFX_PlayerSpawn", 1.4f, out _);
        Ps(spawn, "Flash", _additive, ps => Main(ps, 0.25f, 0f, 2.2f, Hex("#FFC66B", 0.5f), burst: 1));
        Ps(spawn, "Light", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.5f, 0.8f), new ParticleSystem.MinMaxCurve(3f, 5f), new ParticleSystem.MinMaxCurve(0.2f, 0.35f), Color.white, burst: 24);
            Shape(ps, ParticleSystemShapeType.Cone, 0.5f, up, new Vector3(0f, -1f, 0f));
            var shape = ps.shape; shape.angle = 0f;
            Fade(ps, Hex("#FFF2C8"), Hex("#FFC66B", 0.7f), Hex("#E0A020", 0f));
            Stretch(ps, 3f);
        });
        Ps(spawn, "Dust", _alpha, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.5f, 0.8f), new ParticleSystem.MinMaxCurve(1.5f, 2.5f), new ParticleSystem.MinMaxCurve(0.3f, 0.5f), Hex("#B8A890", 0.6f), burst: 20);
            Shape(ps, ParticleSystemShapeType.Circle, 0.3f, up, new Vector3(0f, -0.95f, 0f));
            Grow(ps, 1.6f);
        });
        yield return (VfxType.PlayerSpawn, Save(spawn));
    }

    // Combat feel: ice shards, crits, dodges, blocks, footsteps, and the looks of a few powers
    static IEnumerable<(VfxType, GameObject)> CombatEffects()
    {
        var flat = new Vector3(-90f, 0f, 0f);   // Lays a circle flat

        // An inked ice crystal pointing the way it flies (VfxSystem turns it to face its movement), with a short frost trail
        var shard = Root_("VFX_IceShard", 0.4f, out var shardInstance);
        var crystal = MeshPart(shard, "Crystal", Gem("VFX_Gem"), ComicGlow("VFX_ShardCore", FrostPale, Frost, 1.8f, 0f), Vector3.zero, new Vector3(0.45f, 0.9f, 0.45f));
        crystal.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // The gem's long tip forward
        shardInstance.HideOnRelease = new Renderer[] { crystal };
        Ps(shard, "Trail", _additive, ps =>
        {
            Main(ps, 0.25f, 0f, new ParticleSystem.MinMaxCurve(0.14f, 0.22f), Color.white);
            var e = ps.emission; e.rateOverDistance = 14f;
            Fade(ps, Hex("#FFFFFF", 0.9f), Hex(Frost, 0.6f), Hex(Frost, 0f));
            Shrink(ps);
        });
        yield return (VfxType.IceShard, Save(shard));

        var shardBreak = Root_("VFX_IceShardImpact", 0.6f, out _);
        Ps(shardBreak, "Flash", _additive, ps => Main(ps, 0.12f, 0f, 1f, Hex(FrostPale, 0.6f), burst: 1));
        Ps(shardBreak, "Splinters", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.2f, 0.35f), new ParticleSystem.MinMaxCurve(3f, 6f), new ParticleSystem.MinMaxCurve(0.08f, 0.14f), Color.white, burst: 10);
            Shape(ps, ParticleSystemShapeType.Sphere, 0.15f);
            Fade(ps, Hex("#FFFFFF"), Hex(FrostPale), Hex(Frost, 0f));
            Stretch(ps, 2.5f);
        });
        yield return (VfxType.IceShardImpact, Save(shardBreak));

        // A sharp golden star over the unit, with the crit's combat text
        var crit = Root_("VFX_CritPop", 0.5f, out _);
        Ps(crit, "Flash", _additive, ps =>
        {
            Main(ps, 0.14f, 0f, 1.6f, Color.white, burst: 1);
            Fade(ps, Hex("#FFF6D0", 0.8f), Hex("#FFD24A", 0.6f), Hex("#FFB020", 0f));
            Grow(ps, 1.4f);
        });
        Ps(crit, "Rays", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.15f, 0.25f), new ParticleSystem.MinMaxCurve(6f, 9f), new ParticleSystem.MinMaxCurve(0.12f, 0.18f), Color.white, burst: 10);
            Shape(ps, ParticleSystemShapeType.Sphere, 0.1f);
            Fade(ps, Hex("#FFFFFF"), Hex("#FFE58A"), Hex("#FFB020", 0f));
            Stretch(ps, 3f);
        });
        yield return (VfxType.CritPop, Save(crit));

        // A pale whoosh to the sides, like the dash's afterimage
        var dodge = Root_("VFX_DodgeFlash", 0.6f, out _);
        Ps(dodge, "Afterimage", _additive, ps =>
        {
            Main(ps, 0.25f, 0f, 1.8f, Color.white, burst: 1);
            Fade(ps, Hex("#BFD6FF", 0.4f), Hex("#BFD6FF", 0.25f), Hex("#BFD6FF", 0f));
            Grow(ps, 1.3f);
        });
        Ps(dodge, "Whoosh", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.2f, 0.35f), new ParticleSystem.MinMaxCurve(2.5f, 4f), new ParticleSystem.MinMaxCurve(0.1f, 0.18f), Color.white, burst: 10);
            Shape(ps, ParticleSystemShapeType.Circle, 0.4f, flat);
            Fade(ps, Hex("#FFFFFF", 0.8f), Hex("#BFD6FF", 0.5f), Hex("#8AA8E0", 0f));
            Stretch(ps, 2f);
        });
        yield return (VfxType.DodgeFlash, Save(dodge));

        // Steel sparks off a blocked hit
        var block = Root_("VFX_BlockFlash", 0.5f, out _);
        Ps(block, "Flash", _additive, ps =>
        {
            Main(ps, 0.12f, 0f, 1.3f, Color.white, burst: 1);
            Fade(ps, Hex("#FFFFFF", 0.7f), Hex("#9FB4D0", 0.5f), Hex("#9FB4D0", 0f));
        });
        Ps(block, "Sparks", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.2f, 0.35f), new ParticleSystem.MinMaxCurve(4f, 7f), new ParticleSystem.MinMaxCurve(0.07f, 0.12f), Color.white, burst: 12);
            var m = ps.main; m.gravityModifier = 1f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.15f);
            Fade(ps, Hex("#FFFFFF"), Hex("#E2E8F0"), Hex("#6A7A90", 0f));
            Stretch(ps, 2.5f);
        });
        yield return (VfxType.BlockFlash, Save(block));

        // A small puff at the feet; VfxSystem plays it at floor height
        var step = Root_("VFX_FootstepDust", 0.8f, out _);
        Ps(step, "Dust", _alpha, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.6f), new ParticleSystem.MinMaxCurve(0.3f, 0.7f), new ParticleSystem.MinMaxCurve(0.25f, 0.4f), Color.white, burst: 3);
            Shape(ps, ParticleSystemShapeType.Circle, 0.15f, flat, new Vector3(0f, 0.05f, 0f));
            Fade(ps, Hex("#B8A890", 0.45f), Hex("#9A8C78", 0.3f), Hex("#9A8C78", 0f));
            Grow(ps, 1.6f);
        });
        yield return (VfxType.FootstepDust, Save(step));

        // Faint violet sparkles on a Mirror Image copy while it lasts, and a pop when it comes and goes
        var mirror = Root_("VFX_MirrorShimmer", 0.8f, out _);
        Ps(mirror, "Sparkles", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.5f, 0.8f), new ParticleSystem.MinMaxCurve(0.1f, 0.3f), new ParticleSystem.MinMaxCurve(0.08f, 0.16f), Color.white, rate: 12f);
            var m = ps.main; m.gravityModifier = -0.3f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.5f);
            Fade(ps, Hex("#F0E8FF"), Hex("#C8B0FF", 0.7f), Hex("#8C6DFF", 0f));
            Shrink(ps);
        });
        yield return (VfxType.MirrorShimmer, Save(mirror));

        var mirrorPop = Root_("VFX_MirrorPop", 0.9f, out _);
        Ps(mirrorPop, "Flash", _additive, ps =>
        {
            Main(ps, 0.2f, 0f, 2f, Color.white, burst: 1);
            Fade(ps, Hex("#E0D0FF", 0.5f), Hex("#C8B0FF", 0.35f), Hex("#8C6DFF", 0f));
        });
        Ps(mirrorPop, "Sparkles", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.7f), new ParticleSystem.MinMaxCurve(1.5f, 3f), new ParticleSystem.MinMaxCurve(0.1f, 0.2f), Color.white, burst: 18);
            var m = ps.main; m.gravityModifier = -0.3f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.4f);
            Fade(ps, Hex("#F0E8FF"), Hex("#C8B0FF", 0.7f), Hex("#8C6DFF", 0f));
            Shrink(ps);
        });
        yield return (VfxType.MirrorPop, Save(mirrorPop));

        // Water drawn inward at a pulled enemy's feet (VfxSystem plays it at floor height)
        var ripple = Root_("VFX_UndertowRipple", 0.6f, out _);
        Ps(ripple, "Ripple", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.3f, 0.4f), new ParticleSystem.MinMaxCurve(-3f, -2.4f), new ParticleSystem.MinMaxCurve(0.12f, 0.2f), Color.white, burst: 20);
            Shape(ps, ParticleSystemShapeType.Circle, 1.2f, flat, new Vector3(0f, 0.08f, 0f));
            var shape = ps.shape; shape.radiusThickness = 0f;   // From the edge only, flowing in
            Fade(ps, Hex("#BFF6FF", 0.9f), Hex("#5AC8E8", 0.7f), Hex("#2A7A9E", 0f));
            Stretch(ps, 2f);
        });
        yield return (VfxType.UndertowRipple, Save(ripple));

        // Where a shot splits
        var spark = Root_("VFX_SlingshotSpark", 0.5f, out _);
        Ps(spark, "Flash", _additive, ps => Main(ps, 0.1f, 0f, 1.1f, Hex("#FFE8B0", 0.7f), burst: 1));
        Ps(spark, "Sparks", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.15f, 0.3f), new ParticleSystem.MinMaxCurve(5f, 8f), new ParticleSystem.MinMaxCurve(0.07f, 0.12f), Color.white, burst: 10);
            Shape(ps, ParticleSystemShapeType.Sphere, 0.1f);
            Fade(ps, Hex("#FFFFFF"), Hex("#FFE08A"), Hex("#FF9A3A", 0f));
            Stretch(ps, 2.5f);
        });
        yield return (VfxType.SlingshotSpark, Save(spark));

        // A half ring of light in front of the wearer (played turned like the wearer, so +Z is its front)
        var bulwark = Root_("VFX_BulwarkShimmer", 0.6f, out _);
        Ps(bulwark, "Arc", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.25f, 0.4f), new ParticleSystem.MinMaxCurve(0.3f, 0.8f), new ParticleSystem.MinMaxCurve(0.2f, 0.32f), Color.white, burst: 26);
            Shape(ps, ParticleSystemShapeType.Circle, 1.1f, new Vector3(90f, 0f, 0f));   // Flat, its arc from +X round through +Z
            var shape = ps.shape; shape.radiusThickness = 0f; shape.arc = 180f;
            Fade(ps, Hex("#FFFFFF", 0.9f), Hex("#FFE08A", 0.6f), Hex("#C8A040", 0f));
            Shrink(ps);
        });
        yield return (VfxType.BulwarkShimmer, Save(bulwark));
    }

    // Charged's bolts between the units its discharge hits, and the block of ice around Frozen units
    static IEnumerable<(VfxType, GameObject)> ChargeAndFreezeEffects()
    {
        // Two lines with the same jagged points (LightningArcVfx): a wide blue glow and a thin white core
        var bolt = ParticleMaterial("VFX_Bolt", true, null, CullMode.Off);
        var arc = Root_("VFX_LightningArc", 0.25f, out _);
        arc.AddComponent<LightningArcVfx>();
        BoltLine(arc, "Glow", bolt, 0.45f, Hex(Sapphire, 0.5f), Hex("#9CC4FF", 0.35f));
        BoltLine(arc, "Core", bolt, 0.11f, Hex("#FFFFFF"), Hex("#DDEEFF"));
        yield return (VfxType.LightningArc, Save(arc));

        // Sized for a unit of scale 1 (about 2.1 m tall, its root at the middle); VfxSystem scales it to the unit and turns it.
        // See-through lit ice, so the unit shows inside and the faces catch the light; shards break up the box's outline.
        var ice = IceMaterial("VFX_Ice");
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var gem = Gem("VFX_Gem");
        var block = Root_("VFX_IceBlock", 0.05f, out var blockInstance);
        var parts = new List<Renderer>
        {
            MeshPart(block, "Block", cube, ice, new Vector3(0f, 0.05f, 0f), new Vector3(1.35f, 2.35f, 1.3f)),
            MeshPart(block, "Shard Top", gem, ice, new Vector3(0.3f, 1.2f, 0.1f), new Vector3(0.8f, 1.1f, 0.8f)),
            MeshPart(block, "Shard Side", gem, ice, new Vector3(-0.7f, 0.5f, 0.2f), new Vector3(0.6f, 0.9f, 0.6f)),
            MeshPart(block, "Shard Low", gem, ice, new Vector3(0.55f, -0.6f, -0.6f), new Vector3(0.55f, 0.8f, 0.55f)),
        };
        parts[0].transform.localRotation = Quaternion.Euler(0f, 0f, 4f);
        parts[1].transform.localRotation = Quaternion.Euler(0f, 0f, 160f);     // Gems point down: these point out
        parts[2].transform.localRotation = Quaternion.Euler(10f, 0f, -115f);
        parts[3].transform.localRotation = Quaternion.Euler(-30f, 40f, 125f);
        blockInstance.HideOnRelease = parts.ToArray();
        Ps(block, "Sparkle", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.5f, 0.9f), 0f, new ParticleSystem.MinMaxCurve(0.08f, 0.16f), Color.white, rate: 4f);
            Shape(ps, ParticleSystemShapeType.Box, 1f);
            var shape = ps.shape; shape.scale = new Vector3(1.3f, 2.2f, 1.3f);
            Fade(ps, Hex("#FFFFFF", 0f), Hex("#FFFFFF"), Hex(FrostPale, 0f));
        });
        yield return (VfxType.IceBlock, Save(block));
    }

    // Class passives: Iron Palm's healing orb and the burst where it lands (and Shadow Priest's dark ones), and Guardian's Consecration
    static IEnumerable<(VfxType, GameObject)> ClassPassiveEffects()
    {
        const string Life = "#9CF5B0", Gold = "#FFD86A";

        // A glowing gold-green orb with a soft trail, like the soul orb
        var orb = Root_("VFX_HealOrb", 0.6f, out var orbInstance);
        var core = MeshPart(orb, "Core", Ico("VFX_Ico"), ComicGlow("VFX_HealCore", Life, Gold, 1.8f, 0.5f), Vector3.zero, Vector3.one * 0.36f);
        orbInstance.HideOnRelease = new Renderer[] { core };
        Ps(orb, "Glow", _additive, ps => Main(ps, 0.3f, 0f, 1.1f, Hex(Life, 0.4f), local: true, rate: 10f));
        Ps(orb, "Trail", _additive, ps =>
        {
            Main(ps, 0.35f, 0f, new ParticleSystem.MinMaxCurve(0.12f, 0.2f), Color.white);
            var e = ps.emission; e.rateOverDistance = 12f;
            Fade(ps, Hex("#FFFFFF", 0.9f), Hex(Life, 0.6f), Hex(Gold, 0f));
            Shrink(ps);
        });
        yield return (VfxType.HealOrb, Save(orb));

        var burst = Root_("VFX_HealBurst", 0.8f, out _);
        Ps(burst, "Flash", _additive, ps => Main(ps, 0.15f, 0f, 1.4f, Hex(Life, 0.6f), burst: 1));
        Ps(burst, "Motes", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.5f, 0.8f), new ParticleSystem.MinMaxCurve(0.5f, 1.5f), new ParticleSystem.MinMaxCurve(0.1f, 0.18f), Color.white, burst: 14);
            var m = ps.main; m.gravityModifier = -0.5f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.4f);
            Fade(ps, Hex("#FFFFFF"), Hex(Life), Hex(Gold, 0f));
            Shrink(ps);
        });
        yield return (VfxType.HealBurst, Save(burst));

        // Shadow Priest's: the same orb and burst in shadow violet, with a darker trail
        const string Shadow = "#9A6BFF", Void = "#3A1466";
        var darkOrb = Root_("VFX_DarkHealOrb", 0.6f, out var darkOrbInstance);
        var darkCore = MeshPart(darkOrb, "Core", Ico("VFX_Ico"), ComicGlow("VFX_DarkHealCore", Shadow, Void, 1.6f, 0.5f), Vector3.zero, Vector3.one * 0.36f);
        darkOrbInstance.HideOnRelease = new Renderer[] { darkCore };
        Ps(darkOrb, "Glow", _additive, ps => Main(ps, 0.3f, 0f, 1.1f, Hex(Shadow, 0.35f), local: true, rate: 10f));
        Ps(darkOrb, "Trail", _alpha, ps =>
        {
            Main(ps, 0.4f, 0f, new ParticleSystem.MinMaxCurve(0.12f, 0.2f), Color.white);
            var e = ps.emission; e.rateOverDistance = 12f;
            Fade(ps, Hex(Shadow, 0.8f), Hex(Void, 0.6f), Hex(Void, 0f));
            Shrink(ps);
        });
        yield return (VfxType.DarkHealOrb, Save(darkOrb));

        var darkBurst = Root_("VFX_DarkHealBurst", 0.8f, out _);
        Ps(darkBurst, "Flash", _additive, ps => Main(ps, 0.15f, 0f, 1.4f, Hex(Shadow, 0.5f), burst: 1));
        Ps(darkBurst, "Motes", _alpha, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.5f, 0.8f), new ParticleSystem.MinMaxCurve(0.5f, 1.5f), new ParticleSystem.MinMaxCurve(0.1f, 0.18f), Color.white, burst: 14);
            var m = ps.main; m.gravityModifier = -0.5f;
            Shape(ps, ParticleSystemShapeType.Sphere, 0.4f);
            Fade(ps, Hex(Shadow), Hex(Void), Hex(Void, 0f));
            Shrink(ps);
        });
        yield return (VfxType.DarkHealBurst, Save(darkBurst));

        // Like Cold Snap's nova in gold: drawn at radius 1, VfxSystem scales it to the radius
        var nova = Root_("VFX_Consecration", 1f, out _);
        Ps(nova, "Flash", _additive, ps =>
        {
            Main(ps, 0.25f, 0f, 3f, Hex(Gold, 0.6f), burst: 1);
            var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape;
        });
        Ps(nova, "Ring", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.7f), new ParticleSystem.MinMaxCurve(0.3f, 1f), new ParticleSystem.MinMaxCurve(0.2f, 0.34f), Color.white, burst: 40);
            var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape;
            Shape(ps, ParticleSystemShapeType.Circle, 1f, new Vector3(-90f, 0f, 0f), new Vector3(0f, -0.6f, 0f));
            var shape = ps.shape; shape.radiusThickness = 0f;
            Fade(ps, Hex("#FFFFFF"), Hex(Gold), Hex("#C8902A", 0f));
            Shrink(ps);
        });
        Ps(nova, "Rays", _additive, ps =>
        {
            Main(ps, new ParticleSystem.MinMaxCurve(0.5f, 0.9f), new ParticleSystem.MinMaxCurve(1.5f, 3f), new ParticleSystem.MinMaxCurve(0.14f, 0.24f), Color.white, burst: 20);
            var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape; m.gravityModifier = -0.3f;
            Shape(ps, ParticleSystemShapeType.Circle, 1f, new Vector3(-90f, 0f, 0f), new Vector3(0f, -0.8f, 0f));
            Fade(ps, Hex("#FFFFFF", 0.8f), Hex(Life, 0.5f), Hex(Gold, 0f));
            Stretch(ps, 2f);
        });
        yield return (VfxType.Consecration, Save(nova));
    }

    static void BoltLine(GameObject parent, string name, Material material, float width, Color start, Color end)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.widthMultiplier = width;
        line.numCapVertices = 2;
        line.numCornerVertices = 1;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.startColor = start;
        line.endColor = end;
        line.positionCount = 2;
    }

    /// <summary>URP Lit, transparent: pale blue, see-through and glossy, glowing a little so it reads in the dark.</summary>
    static Material IceMaterial(string name)
    {
        string path = $"{Root}/Materials/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", Hex("#A8E8FF", 0.42f));
        material.SetFloat("_Smoothness", 0.92f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)CullMode.Back);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", Hex("#2A6A86"));
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
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
