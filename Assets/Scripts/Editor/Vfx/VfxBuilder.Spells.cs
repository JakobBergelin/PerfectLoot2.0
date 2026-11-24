using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Curve = UnityEngine.ParticleSystem.MinMaxCurve;

/// <summary>
/// Spell looks (second pass): Flamethrower's flames and their embers, Meteor (landing ring, falling rock, burning ground),
/// Plague Arrow, Immolation, Gale Ward, Ball Lightning and Sonic Ball bodies, Frozen Orb's end, Poison Dart's mark, Cursed
/// Hammer, Earth Bending's wall and its break, Captain's Shield hits, arrows, cast flashes, summon circles, companion
/// arrivals and Barrage's toggle. Colours by element (style guide section 4): fire white-hot to yellow, orange and a red
/// fringe; frost cyan; poison acid green; lightning sapphire; summons gold; physical steel; wind and Barrage teal.
/// Fire is hard-edged blobs (VFX_FireBlob, a lumpy two-tone disc), not smoke (section 13).
///
/// Placement every prefab is drawn for (VfxSystem wires them):
/// - Origin at the entity or unit root (a capsule centre, 1.05 m above the floor), +Z forward; ground parts at y = -1.0.
///   Earth Bending's wall is the exception: its entity stands on the floor, so its origin is its feet.
/// - "Radius 1": VfxManager.Scale(handle, r) sizes it sideways (meshes with the effect, particle emitters by Shape).
/// - Flame and FlameEmbers: drawn for radius 1 and sized every way (ScaleUniform) to the flame's growing radius.
/// - EarthWall and EarthWallBreak: drawn for the default 6 x 3 x 1 m wall, ScaleUniform to the wall's scale.
/// - CastFlash: all white, VfxManager.Tint gives it the element's colour.
/// </summary>
public static partial class VfxBuilder
{
    static IEnumerable<(VfxType, GameObject)> SpellEffects()
    {
        SpellLooks.Init();
        // About two players spraying: ~15 flames a second each, every one shown for its 0.7 s plus its Linger
        Prewarms[VfxType.Flame] = 32;
        Prewarms[VfxType.FlameEmbers] = 20;
        Prewarms[VfxType.Arrow] = 16;
        Prewarms[VfxType.PoisonMark] = 12;
        Prewarms[VfxType.PlagueArrow] = 6;
        Prewarms[VfxType.CursedHammer] = 6;
        Prewarms[VfxType.CastFlash] = 6;
        Prewarms[VfxType.ShieldHit] = 6;

        yield return (VfxType.Flame, SpellLooks.Flame());
        yield return (VfxType.FlameEmbers, SpellLooks.FlameEmbers());
        yield return (VfxType.MeteorWarning, SpellLooks.MeteorWarning());
        yield return (VfxType.MeteorRock, SpellLooks.MeteorRock());
        yield return (VfxType.BurningGround, SpellLooks.BurningGround());
        yield return (VfxType.PoisonPuddle, SpellLooks.PoisonPuddle());
        yield return (VfxType.PlagueArrow, SpellLooks.PlagueArrow());
        yield return (VfxType.ImmolationAura, SpellLooks.ImmolationAura());
        yield return (VfxType.GaleWardBand, SpellLooks.GaleWardBand());
        yield return (VfxType.BallLightningBody, SpellLooks.BallLightningBody());
        yield return (VfxType.SonicBallBody, SpellLooks.SonicBallBody());
        yield return (VfxType.FrozenOrbEnd, SpellLooks.FrozenOrbEnd());
        yield return (VfxType.PoisonMark, SpellLooks.PoisonMark());
        yield return (VfxType.CursedHammer, SpellLooks.CursedHammer());
        yield return (VfxType.EarthWall, SpellLooks.EarthWall());
        yield return (VfxType.EarthWallBreak, SpellLooks.EarthWallBreak());
        yield return (VfxType.ShieldHit, SpellLooks.ShieldHit());
        yield return (VfxType.Arrow, SpellLooks.Arrow());
        yield return (VfxType.CastFlash, SpellLooks.CastFlash());
        yield return (VfxType.SummonCircle, SpellLooks.SummonCircle());
        yield return (VfxType.CompanionSpawn, SpellLooks.CompanionSpawn());
        yield return (VfxType.BarrageAura, SpellLooks.BarrageAura());
    }

    static class SpellLooks
    {
        const float Ground = -1f;   // The floor under a root
        const string White = "#FFFFFF", HotWhite = "#FFF6DC", Yellow = "#FFD24A", Orange = "#FF7A1A", Red = "#B8301A", Ember = "#6E1A12";
        const string Acid = "#8CFF3A", AcidPale = "#DFFFB0", AcidDark = "#2E4A1C";
        const string Teal = "#4FD6C8", TealPale = "#BFFFF6";
        const string Gold = "#FFD24A", GoldWhite = "#FFF2C8", GoldDeep = "#C8902A";
        const string Steel = "#E2E8F0", SteelBlue = "#BFD6FF", Violet = "#B57BFF";
        const string StoneLight = "#8A7A6A", Stone = "#6E6052", StoneDark = "#4A3E34", Dust = "#9A8A76";
        static readonly Vector3 Flat = new Vector3(-90f, 0f, 0f);  // Lays a circle shape flat (and turns a cone's Z up)

        static Material _flame, _flameGlow, _flat, _solid;
        static Mesh _disc, _ringThick, _ringThin;

        public static void Init()
        {
            var blob = FireBlobTexture();
            _flame = ParticleMaterial("VFX_SpellFlame", false, blob, CullMode.Back);     // Hard-edged fire, alpha blended so the red fringe reads
            _flameGlow = ParticleMaterial("VFX_SpellFlameGlow", true, blob, CullMode.Back);  // The white-hot core and embers
            _flat = ParticleMaterial("VFX_SpellFlat", true, null, CullMode.Off);          // Glowing meshes, coloured per particle
            _solid = ParticleMaterial("VFX_SpellSolid", false, null, CullMode.Off);       // Stone chunks
            _disc = Crescent("VFX_SpellDisc", 0f, 1f, 360f);
            _ringThick = Crescent("VFX_SpellRingThick", 0.9f, 1f, 360f);
            _ringThin = Crescent("VFX_SpellRingThin", 0.95f, 1f, 360f);
        }

        // ---------- Flamethrower ----------

        // One flame of the jet, drawn for radius 1 (VfxSystem sizes it to the flame's radius every frame), +Z the way it
        // flies. Its blobs are strung out along the flight, living exactly as long as the flame (0.7 s), so the
        // colour follows the flame's own age: white-hot and yellow at the nozzle, orange, then a red fringe that stays
        // solid out to the tip. Licks and sparks shed behind tie the flames into one stream; embers burst where it dies.
        public static GameObject Flame()
        {
            const float life = 0.7f;    // Flamethrower's Lifetime
            var root = Root_("VFX_Flame", 0.4f, out var instance);
            var fringe = Ps(root, "Fringe", _flame, ps =>
            {
                Main(ps, life, new Curve(0.2f, 0.5f), new Curve(1.5f, 1.9f), Color.white, burst: 4, local: true);
                Tongue(ps, 0.6f, 1.2f);
                Colors(ps, (0f, Hex(Yellow, 0.95f)), (0.25f, Hex(Orange, 0.95f)), (0.6f, Hex(Red, 0.95f)), (0.88f, Hex(Red, 0.85f)), (1f, Hex(Ember, 0f)));
                Sizes(ps, (0f, 0.8f), (1f, 1.05f));
                Order(ps, 30f);
            });
            var body = Ps(root, "Body", _flame, ps =>
            {
                Main(ps, life, new Curve(0.3f, 0.6f), new Curve(1f, 1.3f), Color.white, burst: 5, local: true);
                Tongue(ps, 0.5f, 1f);
                Colors(ps, (0f, Hex(HotWhite, 0.95f)), (0.2f, Hex(Yellow, 0.95f)), (0.5f, Hex(Orange, 0.95f)), (0.85f, Hex(Red, 0.85f)), (1f, Hex(Red, 0f)));
                Sizes(ps, (0f, 0.85f), (1f, 1f));
                Order(ps, 15f);
            });
            // The white-hot heart, only while the flame is young (near the nozzle)
            var core = Ps(root, "Core", _flameGlow, ps =>
            {
                Main(ps, life, new Curve(0.3f, 0.5f), new Curve(0.5f, 0.7f), Color.white, burst: 2, local: true);
                Tongue(ps, 0.3f, 0.6f);
                Colors(ps, (0f, Hex(White, 0.9f)), (0.2f, Hex(HotWhite, 0.75f)), (0.45f, Hex(Yellow, 0f)), (1f, Hex(Yellow, 0f)));
                Order(ps, 0f);
            });
            Ps(root, "Licks", _flame, ps =>
            {
                Main(ps, new Curve(0.2f, 0.35f), new Curve(0.3f, 0.8f), new Curve(0.3f, 0.55f), Color.white, rate: 30f);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.55f);
                var m = ps.main; m.gravityModifier = -2.5f;
                Colors(ps, (0f, Hex(Yellow, 0.95f)), (0.35f, Hex(Orange, 0.95f)), (0.8f, Hex(Red, 0.8f)), (1f, Hex(Red, 0f)));
                Sizes(ps, (0f, 1f), (1f, 0.5f));
                Stretch(ps, 1.4f);
                Order(ps, 20f);
            });
            Ps(root, "Sparks", _additive, ps =>
            {
                Main(ps, new Curve(0.3f, 0.5f), new Curve(1f, 2.5f), new Curve(0.06f, 0.1f), Color.white, rate: 10f);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.6f);
                var m = ps.main; m.gravityModifier = -0.6f;
                Fade(ps, Hex(White), Hex(Yellow), Hex(Orange, 0f));
                Stretch(ps, 2.5f);
            });
            instance.HideOnRelease = new Renderer[] { Renderer(fringe), Renderer(body), Renderer(core) };
            return Save(root);
        }

        // Burst blobs strung out along +Z, the way the flame flies (a box of width x width x length, drifting forward), so each
        // flame reads as a tongue of the jet. Round billboards turned at random: stretched billboards in local space came out
        // sideways.
        static void Tongue(ParticleSystem ps, float width, float length)
        {
            Shape(ps, ParticleSystemShapeType.Box, 1f);
            var shape = ps.shape; shape.scale = new Vector3(width, width, length);
            Spinning(ps, 50f);
        }

        // Where a flame dies: it breaks up into a last puff, ember sparks and chunky embers. Radius 1, sized like the flame.
        public static GameObject FlameEmbers()
        {
            var root = Root_("VFX_FlameEmbers", 0.9f, out _);
            Ps(root, "Puff", _flame, ps =>
            {
                Main(ps, new Curve(0.25f, 0.4f), new Curve(0.5f, 1.2f), new Curve(0.8f, 1.2f), Color.white, burst: 3);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.35f);
                var m = ps.main; m.gravityModifier = -1f;
                Spinning(ps, 90f);
                Colors(ps, (0f, Hex(Orange, 0.9f)), (0.5f, Hex(Red, 0.8f)), (1f, Hex(Ember, 0f)));
                Sizes(ps, (0f, 1f), (1f, 0.3f));
            });
            Ps(root, "Sparks", _flameGlow, ps =>
            {
                Main(ps, new Curve(0.4f, 0.8f), new Curve(1.5f, 3.5f), new Curve(0.08f, 0.15f), Color.white, burst: 14);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.5f);
                var m = ps.main; m.gravityModifier = -0.5f;
                Colors(ps, (0f, Hex(White)), (0.3f, Hex(Yellow)), (0.7f, Hex(Orange, 0.9f)), (1f, Hex(Red, 0f)));
                Stretch(ps, 1.8f);
            });
            Ps(root, "Embers", _flame, ps =>
            {
                Main(ps, new Curve(0.5f, 0.7f), new Curve(1f, 2.2f), new Curve(0.15f, 0.26f), Color.white, burst: 6);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.4f);
                var m = ps.main; m.gravityModifier = 0.8f;
                Spinning(ps, 180f);
                Colors(ps, (0f, Hex("#FFB040")), (0.6f, Hex(Red)), (1f, Hex(Ember, 0f)));
            });
            return Save(root);
        }

        // ---------- Meteor ----------

        // Where a meteor will land, radius 1: a hard ring on the floor, a dark red fill, rings closing in, heat rising
        public static GameObject MeteorWarning()
        {
            var root = Root_("VFX_MeteorWarning", 0.25f, out var instance);
            var ring = MeshPart(root, "Ring", _ringThick, Glow("VFX_MeteorRing", Orange, 0.75f), new Vector3(0f, Ground + 0.02f, 0f), Vector3.one);
            var fill = MeshPart(root, "Fill", _disc, Glow("VFX_MeteorFill", Red, 0.16f), new Vector3(0f, Ground + 0.01f, 0f), Vector3.one);
            instance.HideOnRelease = new Renderer[] { ring, fill };
            Ps(root, "Closing", _flat, ps =>
            {
                Main(ps, 0.75f, 0f, 1f, Color.white, rate: 1.4f, local: true);
                MeshParticle(ps, _ringThin);
                ps.transform.localPosition = new Vector3(0f, Ground + 0.03f, 0f);
                var m = ps.main; m.maxParticles = 4;
                Colors(ps, (0f, Hex(Yellow, 0f)), (0.3f, Hex(Yellow, 0.8f)), (1f, Hex(Orange, 0f)));
                Sizes(ps, (0f, 1f), (1f, 0.1f));
            });
            Ps(root, "Heat", _additive, ps =>
            {
                Main(ps, new Curve(0.5f, 0.9f), new Curve(0.4f, 1.2f), new Curve(0.08f, 0.15f), Color.white, rate: 16f);
                var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape; m.gravityModifier = -0.3f;
                Shape(ps, ParticleSystemShapeType.Circle, 1f, Flat, new Vector3(0f, Ground + 0.05f, 0f));
                Fade(ps, Hex(Yellow), Hex(Orange), Hex(Red, 0f));
            });
            return Save(root);
        }

        // The rock, falling in from high up for VfxSystem.MeteorFallTime (sent down that long before the impact), trailing
        // fire; it hides as it lands, where the impact takes over
        public static GameObject MeteorRock()
        {
            var root = Root_("VFX_MeteorRock", VfxSystem.MeteorFallTime + 0.6f, out _);
            var rock = new GameObject("Rock");
            rock.transform.SetParent(root.transform, false);
            rock.transform.localPosition = new Vector3(0f, Ground + 0.55f, 0f);
            var slide = rock.AddComponent<VfxSlide>();
            slide.From = new Vector3(3f, 13f, 4.5f);   // From up and behind the impact, so it reads at the 50 degree camera
            slide.Duration = VfxSystem.MeteorFallTime;
            slide.EaseIn = true;
            slide.HideOnArrive = true;
            var stone = MeshPart(rock, "Stone", Ico("VFX_Ico"), ComicGlow("VFX_MeteorStone", "#4A2C24", Orange, 1.1f, 0.4f), Vector3.zero, Vector3.one * 1.3f);
            stone.gameObject.AddComponent<VfxSpin>().DegreesPerSecond = 240f;
            Ps(rock, "Halo", _additive, ps => Main(ps, 0.15f, 0f, 2.6f, Hex(Orange, 0.5f), rate: 20f, local: true));
            Ps(rock, "Trail", _flame, ps =>
            {
                Main(ps, new Curve(0.35f, 0.55f), 0f, new Curve(0.9f, 1.4f), Color.white);
                var e = ps.emission; e.rateOverDistance = 1.4f;
                Shape(ps, ParticleSystemShapeType.Sphere, 0.3f);
                Spinning(ps, 90f);
                Colors(ps, (0f, Hex(HotWhite, 0.95f)), (0.25f, Hex(Yellow, 0.95f)), (0.6f, Hex(Orange, 0.9f)), (1f, Hex(Red, 0f)));
                Sizes(ps, (0f, 1f), (1f, 0.4f));
            });
            Ps(rock, "Smoke", _alpha, ps =>
            {
                Main(ps, new Curve(0.6f, 0.9f), 0f, new Curve(0.8f, 1.2f), Color.white);
                var e = ps.emission; e.rateOverDistance = 0.6f;
                Fade(ps, Hex("#3A2E3A", 0.6f), Hex("#2A2230", 0.4f), Hex("#2A2230", 0f));
                Grow(ps, 1.6f);
            });
            Ps(rock, "Sparks", _additive, ps =>
            {
                Main(ps, new Curve(0.25f, 0.45f), new Curve(1f, 3f), new Curve(0.06f, 0.11f), Color.white);
                var e = ps.emission; e.rateOverDistance = 2f;
                Shape(ps, ParticleSystemShapeType.Sphere, 0.5f);
                Fade(ps, Hex(White), Hex(Yellow), Hex(Orange, 0f));
                Stretch(ps, 2.5f);
            });
            return Save(root);
        }

        // The burning ground a meteor leaves, radius 1: a dark red glow with a hot rim, hard flame licks all over it, embers
        public static GameObject BurningGround()
        {
            var root = Root_("VFX_BurningGround", 0.6f, out var instance);
            var glow = MeshPart(root, "Glow", _disc, Glow("VFX_BurnGlow", Red, 0.22f), new Vector3(0f, Ground + 0.01f, 0f), Vector3.one);
            var rim = MeshPart(root, "Rim", _ringThick, Glow("VFX_BurnRim", Orange, 0.5f), new Vector3(0f, Ground + 0.02f, 0f), Vector3.one);
            instance.HideOnRelease = new Renderer[] { glow, rim };
            Ps(root, "Flames", _flame, ps =>
            {
                Main(ps, new Curve(0.35f, 0.6f), new Curve(0.2f, 0.6f), new Curve(0.45f, 0.85f), Color.white, rate: 60f);
                var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape; m.gravityModifier = -1.2f;
                Shape(ps, ParticleSystemShapeType.Circle, 1f, Flat, new Vector3(0f, Ground + 0.1f, 0f));
                Spinning(ps, 60f);
                Colors(ps, (0f, Hex(Yellow, 0.95f)), (0.4f, Hex(Orange, 0.9f)), (1f, Hex(Red, 0f)));
                Sizes(ps, (0f, 0.6f), (0.3f, 1f), (1f, 0.3f));
            });
            Ps(root, "Embers", _additive, ps =>
            {
                Main(ps, new Curve(0.5f, 0.9f), new Curve(0.5f, 1.5f), new Curve(0.06f, 0.11f), Color.white, rate: 10f);
                var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape; m.gravityModifier = -0.4f;
                Shape(ps, ParticleSystemShapeType.Circle, 1f, Flat, new Vector3(0f, Ground + 0.1f, 0f));
                Fade(ps, Hex(White), Hex(Yellow), Hex(Orange, 0f));
                Stretch(ps, 2f);
            });
            return Save(root);
        }

        // The poison puddle an enemy leaves, radius 1: a dark green pool with an acid rim, hard bubbles swelling and popping
        public static GameObject PoisonPuddle()
        {
            var root = Root_("VFX_PoisonPuddle", 0.6f, out var instance);
            var pool = MeshPart(root, "Pool", _disc, Glow("VFX_PuddlePool", AcidDark, 0.55f), new Vector3(0f, Ground + 0.01f, 0f), Vector3.one);
            var rim = MeshPart(root, "Rim", _ringThick, Glow("VFX_PuddleRim", Acid, 0.45f), new Vector3(0f, Ground + 0.02f, 0f), Vector3.one);
            instance.HideOnRelease = new Renderer[] { pool, rim };
            Ps(root, "Bubbles", _flame, ps =>
            {
                Main(ps, new Curve(0.5f, 0.9f), new Curve(0f, 0.1f), new Curve(0.15f, 0.3f), Color.white, rate: 18f);
                var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape; m.gravityModifier = -0.1f;
                Shape(ps, ParticleSystemShapeType.Circle, 0.9f, Flat, new Vector3(0f, Ground + 0.05f, 0f));
                Colors(ps, (0f, Hex(AcidPale, 0.9f)), (0.6f, Hex(Acid, 0.85f)), (1f, Hex(Acid, 0f)));
                Sizes(ps, (0f, 0.3f), (0.8f, 1f), (1f, 1.2f));
            });
            Ps(root, "Fumes", _additive, ps =>
            {
                Main(ps, new Curve(0.8f, 1.3f), new Curve(0.2f, 0.5f), new Curve(0.06f, 0.1f), Color.white, rate: 8f);
                var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape; m.gravityModifier = -0.2f;
                Shape(ps, ParticleSystemShapeType.Circle, 1f, Flat, new Vector3(0f, Ground + 0.1f, 0f));
                Fade(ps, Hex(AcidPale), Hex(Acid), Hex(AcidDark, 0f));
            });
            return Save(root);
        }

        // ---------- Plague Arrow, Poison Dart ----------

        // A glowing green arrow dragging a cloud of hard-edged poison blobs (its 1.5 m poison area) and dripping
        public static GameObject PlagueArrow()
        {
            var root = Root_("VFX_PlagueArrow", 1.3f, out var instance);
            var arrow = MeshPart(root, "Arrow", ArrowMesh("VFX_SpellArrow"), ComicGlow("VFX_PlagueArrowCore", "#7ACC3A", Acid, 2.2f, 0.4f), Vector3.zero, Vector3.one * 1.6f);
            instance.HideOnRelease = new Renderer[] { arrow };
            Ps(root, "Glow", _additive, ps => Main(ps, 0.25f, 0f, 1.8f, Hex(Acid, 0.4f), rate: 10f, local: true));
            Ps(root, "Cloud", _flame, ps =>
            {
                Main(ps, new Curve(0.9f, 1.3f), new Curve(0.1f, 0.3f), new Curve(1.1f, 1.7f), Color.white);
                var e = ps.emission; e.rateOverDistance = 5f;
                Shape(ps, ParticleSystemShapeType.Sphere, 0.6f);
                Spinning(ps, 30f);
                Colors(ps, (0f, Hex("#D8FF9A", 0.75f)), (0.4f, Hex(Acid, 0.6f)), (1f, Hex(AcidDark, 0f)));
                Sizes(ps, (0f, 0.6f), (1f, 1.3f));
                Order(ps, 10f);
            });
            Ps(root, "Bubbles", _additive, ps =>
            {
                Main(ps, new Curve(0.5f, 0.8f), new Curve(0.1f, 0.4f), new Curve(0.1f, 0.18f), Color.white, rate: 10f);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.5f);
                var m = ps.main; m.gravityModifier = -0.3f;
                Fade(ps, Hex(AcidPale), Hex(Acid), Hex("#5E7C46", 0f));
            });
            Ps(root, "Drips", _alpha, ps =>
            {
                Main(ps, 0.4f, 0f, new Curve(0.1f, 0.15f), Hex(Acid, 0.9f));
                var e = ps.emission; e.rateOverDistance = 2f;
                var m = ps.main; m.gravityModifier = 1.4f;
            });
            return Save(root);
        }

        // Over a marked unit's head: a green drop pointing down, dripping
        public static GameObject PoisonMark()
        {
            var root = Root_("VFX_PoisonMark", 0.4f, out var instance);
            var drop = MeshPart(root, "Drop", Gem("VFX_Gem"), ComicGlow("VFX_PoisonMarkDrop", "#9CFF4A", Acid, 1.8f, 0.5f), new Vector3(0f, 0.25f, 0f), new Vector3(0.6f, 0.75f, 0.6f));
            drop.gameObject.AddComponent<VfxSpin>().DegreesPerSecond = 90f;
            instance.HideOnRelease = new Renderer[] { drop };
            Ps(root, "Glow", _additive, ps => Main(ps, 0.3f, 0f, 1.1f, Hex(Acid, 0.35f), rate: 8f, local: true));
            Ps(root, "Drips", _alpha, ps =>
            {
                Main(ps, 0.45f, 0f, new Curve(0.1f, 0.15f), Hex(Acid, 0.95f), rate: 3f);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.08f);
                var m = ps.main; m.gravityModifier = 1.2f;
            });
            return Save(root);
        }

        // ---------- Auras and bodies (tracked at the player's root) ----------

        // Flames licking up the body and a burning ring at Immolation's 4 m radius (the default; an area bonus isn't shown)
        public static GameObject ImmolationAura()
        {
            var root = Root_("VFX_ImmolationAura", 0.6f, out var instance);
            var ring = MeshPart(root, "Ring", Crescent("VFX_ImmolationRing", 3.8f, 4f, 360f), Glow("VFX_ImmolationRing", Orange, 0.45f), new Vector3(0f, Ground + 0.02f, 0f), Vector3.one);
            instance.HideOnRelease = new Renderer[] { ring };
            Ps(root, "Body", _flame, ps =>
            {
                Main(ps, new Curve(0.35f, 0.55f), new Curve(0.2f, 0.5f), new Curve(0.45f, 0.8f), Color.white, rate: 40f);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.45f);
                var shape = ps.shape; shape.scale = new Vector3(1f, 2f, 1f);
                var m = ps.main; m.gravityModifier = -1.6f;
                Spinning(ps, 60f);
                Colors(ps, (0f, Hex(Yellow, 0.95f)), (0.35f, Hex(Orange, 0.9f)), (1f, Hex(Red, 0f)));
                Sizes(ps, (0f, 0.7f), (0.3f, 1f), (1f, 0.3f));
            });
            Ps(root, "Heart", _additive, ps => Main(ps, 0.3f, 0f, 1.8f, Hex(Orange, 0.22f), rate: 8f, local: true));
            Ps(root, "RingFlames", _flame, ps =>
            {
                Main(ps, new Curve(0.3f, 0.5f), new Curve(0.1f, 0.3f), new Curve(0.4f, 0.7f), Color.white, rate: 70f);
                Shape(ps, ParticleSystemShapeType.Circle, 4f, Flat, new Vector3(0f, Ground + 0.1f, 0f));
                var shape = ps.shape; shape.radiusThickness = 0f;
                var m = ps.main; m.gravityModifier = -1.5f;
                Spinning(ps, 60f);
                Colors(ps, (0f, Hex(Yellow, 0.9f)), (0.4f, Hex(Orange, 0.85f)), (1f, Hex(Red, 0f)));
            });
            return Save(root);
        }

        // Gale Ward's band, 6 to 9 m out (the defaults): faint teal ground with bright edges and gust arcs. Local, so it
        // turns as VfxSystem turns it (either way); an area bonus isn't shown.
        public static GameObject GaleWardBand()
        {
            var root = Root_("VFX_GaleWardBand", 0.8f, out var instance);
            var edge = Glow("VFX_GaleEdge", TealPale, 0.4f);
            instance.HideOnRelease = new Renderer[]
            {
                MeshPart(root, "Band", Crescent("VFX_GaleBand", 6f, 9f, 360f), Glow("VFX_GaleBand", Teal, 0.07f), new Vector3(0f, Ground + 0.01f, 0f), Vector3.one),
                MeshPart(root, "Outer", Crescent("VFX_GaleEdgeOuter", 8.85f, 9f, 360f), edge, new Vector3(0f, Ground + 0.02f, 0f), Vector3.one),
                MeshPart(root, "Inner", Crescent("VFX_GaleEdgeInner", 6f, 6.12f, 360f), edge, new Vector3(0f, Ground + 0.02f, 0f), Vector3.one),
            };
            var gust = Crescent("VFX_GustArc", 0.97f, 1f, 50f);
            foreach (var (name, height, rate) in new[] { ("Gusts", Ground + 0.4f, 12f), ("Gusts High", Ground + 1.4f, 6f) })
                Ps(root, name, _flat, ps =>
                {
                    Main(ps, new Curve(0.5f, 0.8f), 0f, new Curve(6.4f, 8.6f), Color.white, rate: rate, local: true);
                    MeshParticle(ps, gust);
                    ps.transform.localPosition = new Vector3(0f, height, 0f);
                    var m = ps.main; m.startRotation3D = true; m.startRotationX = 0f; m.startRotationZ = 0f; m.startRotationY = new Curve(0f, Mathf.PI * 2f);
                    Colors(ps, (0f, Hex(White, 0f)), (0.3f, Hex(TealPale, 0.7f)), (1f, Hex(Teal, 0f)));
                });
            Ps(root, "Motes", _additive, ps =>
            {
                Main(ps, new Curve(0.6f, 1f), 0f, new Curve(0.12f, 0.22f), Color.white, rate: 30f, local: true);
                Shape(ps, ParticleSystemShapeType.Circle, 9f, Flat, new Vector3(0f, Ground + 0.6f, 0f));
                var shape = ps.shape; shape.radiusThickness = 1f - 6f / 9f;
                Fade(ps, Hex(White, 0.8f), Hex(TealPale, 0.6f), Hex(Teal, 0f));
            });
            return Save(root);
        }

        // Ball Lightning: the player is a crackling sapphire ball while it flies
        public static GameObject BallLightningBody()
        {
            var root = Root_("VFX_BallLightningBody", 0.4f, out var instance);
            var core = MeshPart(root, "Core", Ico("VFX_Ico"), ComicGlow("VFX_BallLightningCore", "#7FA8FF", Sapphire, 2.6f, 0.6f), Vector3.zero, Vector3.one * 1.1f);
            core.gameObject.AddComponent<VfxSpin>().DegreesPerSecond = 300f;
            instance.HideOnRelease = new Renderer[] { core };
            Ps(root, "Halo", _additive, ps => Main(ps, 0.25f, 0f, 2.8f, Hex(Sapphire, 0.45f), rate: 16f, local: true));
            Ps(root, "Crackle", _additive, ps =>
            {
                Main(ps, new Curve(0.08f, 0.18f), new Curve(3f, 6f), new Curve(0.05f, 0.09f), Color.white, rate: 36f);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.55f);
                Fade(ps, Hex(White), Hex("#9CC4FF"), Hex(Sapphire, 0f));
                Stretch(ps, 4f);
            });
            Ps(root, "Trail", _additive, ps =>
            {
                Main(ps, 0.3f, 0f, new Curve(0.5f, 0.8f), Color.white);
                var e = ps.emission; e.rateOverDistance = 5f;
                Fade(ps, Hex("#9CC4FF", 0.5f), Hex(Sapphire, 0.3f), Hex(Sapphire, 0f));
                Shrink(ps);
            });
            return Save(root);
        }

        // Sonic Ball: a teal ball with a bright band, rolling forward (end over end about +X), kicking up dust
        public static GameObject SonicBallBody()
        {
            var root = Root_("VFX_SonicBallBody", 0.5f, out var instance);
            var roll = new GameObject("Roll");
            roll.transform.SetParent(root.transform, false);
            roll.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);    // VfxSpin turns about its up: here the root's +X
            roll.AddComponent<VfxSpin>().DegreesPerSecond = 540f;
            var ball = MeshPart(roll, "Ball", Ico("VFX_Ico"), ComicGlow("VFX_SonicBall", "#8FEFE4", Teal, 1f, 0.2f), Vector3.zero, Vector3.one * 1.5f);
            var band = MeshPart(roll, "Band", Crescent("VFX_SonicBand", 0.78f, 0.86f, 360f), Glow("VFX_SonicBand", "#E8FFFC", 0.8f), Vector3.zero, Vector3.one);
            instance.HideOnRelease = new Renderer[] { ball, band };
            Ps(root, "Wake", _additive, ps =>
            {
                Main(ps, 0.35f, 0f, new Curve(1.2f, 1.6f), Color.white);
                var e = ps.emission; e.rateOverDistance = 3f;
                Fade(ps, Hex(White, 0.35f), Hex(Teal, 0.2f), Hex(Teal, 0f));
                Grow(ps, 1.5f);
            });
            Ps(root, "Streaks", _additive, ps =>
            {
                Main(ps, new Curve(0.15f, 0.3f), new Curve(1f, 3f), new Curve(0.05f, 0.09f), Color.white, rate: 20f);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.8f);
                Fade(ps, Hex(White), Hex(TealPale), Hex(Teal, 0f));
                Stretch(ps, 3f);
            });
            Ps(root, "Dust", _alpha, ps =>
            {
                Main(ps, new Curve(0.4f, 0.7f), new Curve(0.3f, 1f), new Curve(0.3f, 0.6f), Color.white);
                var e = ps.emission; e.rateOverDistance = 6f;
                Shape(ps, ParticleSystemShapeType.Circle, 0.4f, Flat, new Vector3(0f, Ground + 0.1f, 0f));
                Fade(ps, Hex("#6A6278", 0.5f), Hex("#4A4458", 0.3f), Hex("#2E2C42", 0f));
                Grow(ps, 1.6f);
            });
            return Save(root);
        }

        // Barrage switched on: three teal bolts circling the player and a faint teal ring at its feet
        public static GameObject BarrageAura()
        {
            var root = Root_("VFX_BarrageAura", 0.4f, out var instance);
            var orbit = new GameObject("Orbit");
            orbit.transform.SetParent(root.transform, false);
            orbit.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            orbit.AddComponent<VfxSpin>().DegreesPerSecond = 240f;
            var parts = new List<Renderer>();
            for (int i = 0; i < 3; i++)
            {
                float angle = i * Mathf.PI * 2f / 3f;
                var bolt = MeshPart(orbit, $"Bolt{i}", Bolt("VFX_Bolt"), ComicGlow("VFX_BoltCore", Teal, Teal, 2.2f, 0f),
                    new Vector3(Mathf.Cos(angle) * 1f, 0f, Mathf.Sin(angle) * 1f), Vector3.one * 1.3f);
                bolt.transform.localRotation = Quaternion.LookRotation(new Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle)));   // Along the way it circles
                parts.Add(bolt);
            }
            parts.Add(MeshPart(root, "Ring", Crescent("VFX_BarrageRing", 0.85f, 0.95f, 360f), Glow("VFX_BarrageRing", Teal, 0.35f), new Vector3(0f, Ground + 0.02f, 0f), Vector3.one));
            instance.HideOnRelease = parts.ToArray();
            Ps(root, "Motes", _additive, ps =>
            {
                Main(ps, new Curve(0.4f, 0.6f), 0f, new Curve(0.08f, 0.14f), Color.white, rate: 14f, local: true);
                Shape(ps, ParticleSystemShapeType.Circle, 0.8f, Flat, new Vector3(0f, 0.2f, 0f));
                var shape = ps.shape; shape.radiusThickness = 0f;
                Fade(ps, Hex(TealPale), Hex(Teal), Hex("#1C4A54", 0f));
            });
            return Save(root);
        }

        // ---------- Frost, shield, arrows, hammer ----------

        // A Frozen Orb's end: a frost flash, crystals bursting out, a ring racing over the floor and a puff of mist
        public static GameObject FrozenOrbEnd()
        {
            var root = Root_("VFX_FrozenOrbEnd", 1f, out _);
            Ps(root, "Flash", _additive, ps => { Main(ps, 0.2f, 0f, 3.2f, Color.white, burst: 1); Fade(ps, Hex(FrostPale, 0.8f), Hex(Frost, 0.5f), Hex(Frost, 0f)); });
            Ps(root, "Crystals", _flat, ps =>
            {
                Main(ps, new Curve(0.5f, 0.8f), new Curve(3f, 6f), new Curve(0.35f, 0.6f), Color.white, burst: 12);
                MeshParticle(ps, Gem("VFX_Gem"));
                Shape(ps, ParticleSystemShapeType.Sphere, 0.3f);
                var m = ps.main; m.gravityModifier = 1.2f;
                RandomTumble(ps);
                Colors(ps, (0f, Hex(White)), (0.5f, Hex(FrostPale, 0.9f)), (1f, Hex(Frost, 0f)));
            });
            Ps(root, "Splinters", _additive, ps =>
            {
                Main(ps, new Curve(0.2f, 0.4f), new Curve(4f, 8f), new Curve(0.07f, 0.12f), Color.white, burst: 20);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.2f);
                Fade(ps, Hex(White), Hex(FrostPale), Hex(Frost, 0f));
                Stretch(ps, 2.5f);
            });
            Ps(root, "Ring", _flat, ps =>
            {
                Main(ps, 0.35f, 0f, 2.8f, Color.white, burst: 1, local: true);
                MeshParticle(ps, _ringThick);
                ps.transform.localPosition = new Vector3(0f, Ground + 0.03f, 0f);
                Sizes(ps, (0f, 0.12f), (1f, 1f));
                Colors(ps, (0f, Hex(White, 0.9f)), (1f, Hex(Frost, 0f)));
            });
            Ps(root, "Mist", _alpha, ps =>
            {
                Main(ps, new Curve(0.6f, 0.9f), new Curve(0.5f, 1.5f), new Curve(0.8f, 1.4f), Color.white, burst: 8);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.4f);
                Fade(ps, Hex("#E8F8FF", 0.45f), Hex(FrostPale, 0.3f), Hex(FrostPale, 0f));
                Grow(ps, 1.8f);
            });
            return Save(root);
        }

        // Captain's Shield striking: a white flash, a steel ring with a violet edge, steel sparks
        public static GameObject ShieldHit()
        {
            var root = Root_("VFX_ShieldHit", 0.5f, out _);
            Ps(root, "Flash", _additive, ps => { Main(ps, 0.12f, 0f, 1.6f, Color.white, burst: 1); Fade(ps, Hex(White, 0.85f), Hex(SteelBlue, 0.5f), Hex(SteelBlue, 0f)); });
            Ps(root, "Ring", _flat, ps =>
            {
                Main(ps, 0.22f, 0f, 1.4f, Color.white, burst: 1, local: true);
                MeshParticle(ps, _ringThick);
                Sizes(ps, (0f, 0.2f), (1f, 1f));
                Colors(ps, (0f, Hex(White)), (0.5f, Hex(Steel, 0.8f)), (1f, Hex(Violet, 0f)));
            });
            Ps(root, "Sparks", _additive, ps =>
            {
                Main(ps, new Curve(0.2f, 0.35f), new Curve(5f, 9f), new Curve(0.06f, 0.11f), Color.white, burst: 14);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.2f);
                var m = ps.main; m.gravityModifier = 1f;
                Fade(ps, Hex(White), Hex(Steel), Hex("#8A8F99", 0f));
                Stretch(ps, 2.5f);
            });
            return Save(root);
        }

        // A steel-tipped arrow with a short white streak behind it
        public static GameObject Arrow()
        {
            var root = Root_("VFX_Arrow", 0.3f, out var instance);
            var arrow = MeshPart(root, "Arrow", ArrowMesh("VFX_SpellArrow"), ComicGlow("VFX_ArrowSteel", "#C8D0DC", Steel, 0.7f, 0f), Vector3.zero, Vector3.one);
            instance.HideOnRelease = new Renderer[] { arrow };
            Ps(root, "Trail", _additive, ps =>
            {
                Main(ps, 0.15f, 0f, new Curve(0.08f, 0.12f), Color.white);
                var e = ps.emission; e.rateOverDistance = 10f;
                Fade(ps, Hex(White, 0.7f), Hex(Steel, 0.4f), Hex(Steel, 0f));
                Shrink(ps);
            });
            return Save(root);
        }

        // Cursed Hammer: a dark iron hammer with sapphire runes, tumbling end over end (about +X), shedding sparks and
        // dark wisps
        public static GameObject CursedHammer()
        {
            var root = Root_("VFX_CursedHammer", 0.45f, out var instance);
            var tumble = new GameObject("Tumble");
            tumble.transform.SetParent(root.transform, false);
            tumble.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);  // VfxSpin turns about its up: here the root's +X
            tumble.AddComponent<VfxSpin>().DegreesPerSecond = 900f;
            var hammer = MeshPart(tumble, "Hammer", HammerMesh("VFX_SpellHammer"), ComicGlow("VFX_CursedHammer", "#5A5480", Sapphire, 2.2f, 0.5f), Vector3.zero, Vector3.one * 2f);
            hammer.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);    // Back upright in the root's frame
            instance.HideOnRelease = new Renderer[] { hammer };
            Ps(root, "Glow", _additive, ps => Main(ps, 0.25f, 0f, 1.8f, Hex(Sapphire, 0.25f), rate: 10f, local: true));
            Ps(root, "Sparks", _additive, ps =>
            {
                Main(ps, new Curve(0.2f, 0.35f), new Curve(0.5f, 2f), new Curve(0.05f, 0.09f), Color.white);
                var e = ps.emission; e.rateOverDistance = 6f;
                Shape(ps, ParticleSystemShapeType.Sphere, 0.4f);
                Fade(ps, Hex(White), Hex("#9CC4FF"), Hex(Sapphire, 0f));
                Stretch(ps, 2.5f);
            });
            Ps(root, "Wisps", _flame, ps =>
            {
                Main(ps, new Curve(0.35f, 0.55f), 0f, new Curve(0.35f, 0.55f), Color.white);
                var e = ps.emission; e.rateOverDistance = 2f;
                Spinning(ps, 60f);
                Colors(ps, (0f, Hex("#4A2E7A", 0.7f)), (0.5f, Hex("#2E1A4A", 0.5f)), (1f, Hex("#160C26", 0f)));
                Sizes(ps, (0f, 1f), (1f, 0.4f));
            });
            return Save(root);
        }

        // ---------- Earth Bending ----------

        // The default 6 x 3 x 1 m wall (origin at its feet, facing +Z): rough stone blocks rising out of the floor with dust
        // and rubble thrown up along its foot. ScaleUniform to the wall's scale; the mesh hides when the wall goes.
        public static GameObject EarthWall()
        {
            var root = Root_("VFX_EarthWall", 0.05f, out var instance);
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var slide = body.AddComponent<VfxSlide>();
            slide.From = new Vector3(0f, -3.3f, 0f);
            slide.Duration = 0.3f;
            var wall = MeshPart(body, "Stone", StoneWallMesh("VFX_SpellStoneWall"), ComicGlow("VFX_EarthWallStone", "#7A6A5A", "#000000", 0f, 0f), Vector3.zero, Vector3.one);
            instance.HideOnRelease = new Renderer[] { wall };
            Ps(root, "Dust", _alpha, ps =>
            {
                Main(ps, new Curve(0.6f, 1f), new Curve(1f, 2.5f), new Curve(0.5f, 1f), Color.white, burst: 26);
                Shape(ps, ParticleSystemShapeType.Box, 1f, Flat, new Vector3(0f, 0.1f, 0f));
                var shape = ps.shape; shape.scale = new Vector3(6f, 1.4f, 0.1f); shape.randomDirectionAmount = 0.4f;
                Fade(ps, Hex(Dust, 0.6f), Hex(Stone, 0.4f), Hex(Stone, 0f));
                Grow(ps, 1.8f);
            });
            Ps(root, "Rubble", _solid, ps =>
            {
                Main(ps, new Curve(0.6f, 0.9f), new Curve(3f, 6f), new Curve(0.2f, 0.4f), Color.white, burst: 14);
                MeshParticle(ps, ChunkMesh("VFX_SpellChunk"));
                Shape(ps, ParticleSystemShapeType.Box, 1f, Flat, new Vector3(0f, 0.1f, 0f));
                var shape = ps.shape; shape.scale = new Vector3(6f, 1.4f, 0.1f); shape.randomDirectionAmount = 0.3f;
                var m = ps.main; m.gravityModifier = 2.2f;
                RandomTumble(ps);
                Colors(ps, (0f, Hex(StoneLight)), (0.8f, Hex(Stone)), (1f, Hex(StoneDark, 0f)));
            });
            return Save(root);
        }

        // The wall breaking: stone chunks flying out of its whole body and a cloud of dust
        public static GameObject EarthWallBreak()
        {
            var root = Root_("VFX_EarthWallBreak", 1.4f, out _);
            Ps(root, "Chunks", _solid, ps =>
            {
                Main(ps, new Curve(0.8f, 1.2f), new Curve(2f, 5f), new Curve(0.3f, 0.7f), Color.white, burst: 30);
                MeshParticle(ps, ChunkMesh("VFX_SpellChunk"));
                Shape(ps, ParticleSystemShapeType.Box, 1f, default, new Vector3(0f, 1.5f, 0f));
                var shape = ps.shape; shape.scale = new Vector3(6f, 3f, 1f); shape.randomDirectionAmount = 1f;
                var m = ps.main; m.gravityModifier = 2.2f;
                RandomTumble(ps);
                Colors(ps, (0f, Hex(StoneLight)), (0.7f, Hex(Stone)), (1f, Hex(StoneDark, 0f)));
            });
            Ps(root, "Dust", _alpha, ps =>
            {
                Main(ps, new Curve(0.8f, 1.2f), new Curve(0.5f, 1.5f), new Curve(0.9f, 1.6f), Color.white, burst: 24);
                Shape(ps, ParticleSystemShapeType.Box, 1f, default, new Vector3(0f, 1.2f, 0f));
                var shape = ps.shape; shape.scale = new Vector3(6f, 2.4f, 1f); shape.randomDirectionAmount = 1f;
                Fade(ps, Hex(Dust, 0.65f), Hex(Stone, 0.4f), Hex(Stone, 0f));
                Grow(ps, 1.8f);
            });
            Ps(root, "Crumbs", _alpha, ps =>
            {
                Main(ps, new Curve(0.5f, 0.8f), new Curve(2f, 4f), new Curve(0.1f, 0.18f), Hex(StoneDark), burst: 20);
                Shape(ps, ParticleSystemShapeType.Box, 1f, default, new Vector3(0f, 1.5f, 0f));
                var shape = ps.shape; shape.scale = new Vector3(6f, 3f, 1f); shape.randomDirectionAmount = 1f;
                var m = ps.main; m.gravityModifier = 2f;
            });
            return Save(root);
        }

        // ---------- Casts and summons ----------

        // A spell cast: a flash at the hands, sparks, a ring over the floor and motes rising. All white: VfxSystem tints it
        // to the spell's element (VfxManager.Tint sets every system's start colour).
        public static GameObject CastFlash()
        {
            var root = Root_("VFX_CastFlash", 0.8f, out _);
            var hands = new Vector3(0f, 0.25f, 0.45f);
            Ps(root, "Flash", _additive, ps =>
            {
                Main(ps, 0.18f, 0f, 2f, Color.white, burst: 1);
                ps.transform.localPosition = hands;
                Fade(ps, Hex(White, 0.8f), Hex(White, 0.5f), Hex(White, 0f));
            });
            Ps(root, "Sparks", _additive, ps =>
            {
                Main(ps, new Curve(0.2f, 0.35f), new Curve(3f, 5f), new Curve(0.06f, 0.1f), Color.white, burst: 12);
                ps.transform.localPosition = hands;
                Shape(ps, ParticleSystemShapeType.Sphere, 0.15f);
                Fade(ps, Hex(White), Hex(White, 0.8f), Hex(White, 0f));
                Stretch(ps, 2f);
            });
            Ps(root, "Ring", _flat, ps =>
            {
                Main(ps, 0.35f, 0f, 1.8f, Color.white, burst: 1, local: true);
                MeshParticle(ps, _ringThick);
                ps.transform.localPosition = new Vector3(0f, Ground + 0.03f, 0f);
                Sizes(ps, (0f, 0.2f), (1f, 1f));
                Colors(ps, (0f, Hex(White, 0.85f)), (1f, Hex(White, 0f)));
            });
            Ps(root, "Motes", _additive, ps =>
            {
                Main(ps, new Curve(0.5f, 0.8f), new Curve(0.2f, 0.6f), new Curve(0.1f, 0.18f), Color.white, burst: 16);
                Shape(ps, ParticleSystemShapeType.Circle, 0.6f, Flat, new Vector3(0f, Ground + 0.1f, 0f));
                var m = ps.main; m.gravityModifier = -1.5f;
                Fade(ps, Hex(White), Hex(White, 0.7f), Hex(White, 0f));
            });
            return Save(root);
        }

        // A summon cast: a gold circle with a turning ring of runes on the floor, light rising off its edge
        public static GameObject SummonCircle()
        {
            var root = Root_("VFX_SummonCircle", 1.4f, out _);
            Ps(root, "Circle", _flat, ps =>
            {
                Main(ps, 1.2f, 0f, 1.6f, Color.white, burst: 1, local: true);
                MeshParticle(ps, _ringThick);
                ps.transform.localPosition = new Vector3(0f, Ground + 0.03f, 0f);
                Sizes(ps, (0f, 0.5f), (0.15f, 1f), (1f, 1f));
                Colors(ps, (0f, Hex(GoldWhite, 0f)), (0.12f, Hex(Gold, 0.9f)), (0.7f, Hex(Gold, 0.7f)), (1f, Hex(Gold, 0f)));
            });
            Ps(root, "Runes", _flat, ps =>
            {
                Main(ps, 1.2f, 0f, 1.6f, Color.white, burst: 1, local: true);
                MeshParticle(ps, RuneRingMesh("VFX_SummonRunes"));
                ps.transform.localPosition = new Vector3(0f, Ground + 0.04f, 0f);
                var spin = ps.rotationOverLifetime; spin.enabled = true; spin.separateAxes = true;
                spin.x = 0f; spin.z = 0f; spin.y = new Curve(1.5f);
                Sizes(ps, (0f, 0.6f), (0.2f, 1f), (1f, 1f));
                Colors(ps, (0f, Hex(GoldWhite, 0f)), (0.2f, Hex(GoldWhite, 0.9f)), (0.75f, Hex(Gold, 0.6f)), (1f, Hex(Gold, 0f)));
            });
            Ps(root, "Fill", _flat, ps =>
            {
                Main(ps, 0.5f, 0f, 1.45f, Color.white, burst: 1, local: true);
                MeshParticle(ps, _disc);
                ps.transform.localPosition = new Vector3(0f, Ground + 0.02f, 0f);
                Colors(ps, (0f, Hex(Gold, 0.3f)), (1f, Hex(Gold, 0f)));
            });
            Ps(root, "Light", _additive, ps =>
            {
                Main(ps, new Curve(0.6f, 1f), 0f, new Curve(0.1f, 0.18f), Color.white, burst: 26);
                Shape(ps, ParticleSystemShapeType.Circle, 1.5f, Flat, new Vector3(0f, Ground + 0.05f, 0f));
                var shape = ps.shape; shape.radiusThickness = 0f;
                var m = ps.main; m.gravityModifier = -2f;
                Fade(ps, Hex(GoldWhite), Hex(Gold), Hex(GoldDeep, 0f));
                Stretch(ps, 2.5f);
            });
            Ps(root, "Flash", _additive, ps =>
            {
                Main(ps, 0.3f, 0f, 2.6f, Color.white, burst: 1);
                ps.transform.localPosition = new Vector3(0f, Ground + 0.4f, 0f);
                Fade(ps, Hex(GoldWhite, 0.5f), Hex(Gold, 0.3f), Hex(Gold, 0f));
            });
            return Save(root);
        }

        // Where a companion appears (its root): a gold flash, a column of light out of the floor, a ring and dust
        public static GameObject CompanionSpawn()
        {
            var root = Root_("VFX_CompanionSpawn", 1.2f, out _);
            Ps(root, "Flash", _additive, ps =>
            {
                Main(ps, 0.25f, 0f, 3.2f, Color.white, burst: 1);
                ps.transform.localPosition = new Vector3(0f, -0.3f, 0f);
                Fade(ps, Hex(GoldWhite, 0.85f), Hex(Gold, 0.4f), Hex(Gold, 0f));
            });
            Ps(root, "Column", _additive, ps =>
            {
                Main(ps, new Curve(0.4f, 0.7f), 0f, new Curve(0.15f, 0.25f), Color.white, burst: 40);
                Shape(ps, ParticleSystemShapeType.Circle, 0.6f, Flat, new Vector3(0f, Ground + 0.05f, 0f));
                var m = ps.main; m.gravityModifier = -3f;
                Fade(ps, Hex(GoldWhite), Hex(Gold), Hex(GoldDeep, 0f));
                Stretch(ps, 3f);
            });
            Ps(root, "Ring", _flat, ps =>
            {
                Main(ps, 0.4f, 0f, 3f, Color.white, burst: 1, local: true);
                MeshParticle(ps, _ringThick);
                ps.transform.localPosition = new Vector3(0f, Ground + 0.03f, 0f);
                Sizes(ps, (0f, 0.15f), (1f, 1f));
                Colors(ps, (0f, Hex(GoldWhite, 0.9f)), (1f, Hex(Gold, 0f)));
            });
            Ps(root, "Dust", _alpha, ps =>
            {
                Main(ps, new Curve(0.5f, 0.8f), new Curve(1.5f, 2.5f), new Curve(0.3f, 0.5f), Hex("#B8A890", 0.55f), burst: 16);
                Shape(ps, ParticleSystemShapeType.Circle, 0.4f, Flat, new Vector3(0f, Ground + 0.05f, 0f));
                Grow(ps, 1.6f);
            });
            return Save(root);
        }

        // ---------- particle helpers ----------

        static ParticleSystemRenderer Renderer(ParticleSystem ps) => ps.GetComponent<ParticleSystemRenderer>();

        /// <summary>Colour over lifetime through these keys (alpha from each colour).</summary>
        static void Colors(ParticleSystem ps, params (float Time, Color Color)[] keys)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(keys.Select(k => new GradientColorKey(k.Color, k.Time)).ToArray(), keys.Select(k => new GradientAlphaKey(k.Color.a, k.Time)).ToArray());
            col.color = gradient;
        }

        static void Sizes(ParticleSystem ps, params (float Time, float Size)[] keys)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new Curve(1f, new AnimationCurve(keys.Select(k => new Keyframe(k.Time, k.Size)).ToArray()));
        }

        /// <summary>Starts at a random angle and turns up to this many degrees a second either way: blobs that don't repeat.</summary>
        static void Spinning(ParticleSystem ps, float degrees)
        {
            var main = ps.main;
            main.startRotation = new Curve(0f, Mathf.PI * 2f);
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new Curve(-degrees * Mathf.Deg2Rad, degrees * Mathf.Deg2Rad);
        }

        /// <summary>Mesh particles turned every way at random and tumbling.</summary>
        static void RandomTumble(ParticleSystem ps)
        {
            var main = ps.main;
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new Curve(0f, Mathf.PI * 2f);
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = spin.y = spin.z = new Curve(-6f, 6f);
        }

        /// <summary>Higher draws further back among the transparent effects (the flame's fringe behind its core).</summary>
        static void Order(ParticleSystem ps, float fudge) => Renderer(ps).sortingFudge = fudge;

        /// <summary>An additive flat colour for ground rings and discs (meshes, not particles).</summary>
        static Material Glow(string name, string hex, float alpha)
        {
            var material = ParticleMaterial(name, true, null, CullMode.Off);
            material.SetColor("_BaseColor", Hex(hex, alpha));
            return material;
        }

        /// <summary>
        /// A lumpy disc with a hard edge and a darker rim band, white (the particle colours it): stylized fire as cel-shaded
        /// blobs, a hot middle and a cooler fringe in one particle. Particles turn it, so no two read the same.
        /// </summary>
        static Texture2D FireBlobTexture()
        {
            string path = $"{Root}/Textures/VFX_FireBlob.png";
            if (!File.Exists(path))
            {
                const int size = 128;
                const float edge = 1.5f / (size / 2f);  // Antialiased over about a pixel and a half
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        var p = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f) / (size / 2f);
                        float d = p.magnitude, angle = Mathf.Atan2(p.y, p.x);
                        float r = 0.78f + 0.07f * Mathf.Sin(3f * angle + 0.6f) + 0.05f * Mathf.Sin(5f * angle + 2.1f) + 0.03f * Mathf.Sin(8f * angle);
                        float a = Mathf.Clamp01((r - d) / edge + 0.5f);
                        float shade = Mathf.Lerp(0.72f, 1f, Mathf.Clamp01((r * 0.64f - d) / edge + 0.5f));
                        tex.SetPixel(x, y, new Color(shade, shade, shade, a));
                    }
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------- meshes ----------

        /// <summary>Adds a quad, wound to face away from inside (flat shading splits it later).</summary>
        static void Quad(List<Vector3> v, List<int> t, Vector3 inside, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = v.Count;
            v.AddRange(new[] { a, b, c, d });
            // Unity's front faces: cross(b - a, c - a) points out
            bool outward = Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c + d) * 0.25f - inside) > 0f;
            t.AddRange(outward ? new[] { i, i + 1, i + 2, i, i + 2, i + 3 } : new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        static void Tri(List<Vector3> v, List<int> t, Vector3 inside, Vector3 a, Vector3 b, Vector3 c)
        {
            int i = v.Count;
            v.AddRange(new[] { a, b, c });
            bool outward = Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - inside) > 0f;
            t.AddRange(outward ? new[] { i, i + 1, i + 2 } : new[] { i, i + 2, i + 1 });
        }

        /// <summary>A box of size around center, turned by rotation; corners can be pushed (jitter) for rough stone.</summary>
        static void Box(List<Vector3> v, List<int> t, Vector3 center, Vector3 size, Quaternion rotation, System.Func<Vector3, Vector3> jitter = null)
        {
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
                corner = Vector3.Scale(corner, size);
                if (jitter != null) corner += jitter(corner);
                c[i] = center + rotation * corner;
            }
            Quad(v, t, center, c[0], c[1], c[3], c[2]);    // -Z
            Quad(v, t, center, c[4], c[5], c[7], c[6]);    // +Z
            Quad(v, t, center, c[0], c[1], c[5], c[4]);    // -Y
            Quad(v, t, center, c[2], c[3], c[7], c[6]);    // +Y
            Quad(v, t, center, c[0], c[2], c[6], c[4]);    // -X
            Quad(v, t, center, c[1], c[3], c[7], c[5]);    // +X
        }

        /// <summary>An arrow along +Z, about 0.95 m: a shaft, a four-sided head and crossed fletching.</summary>
        static Mesh ArrowMesh(string name)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            Box(v, t, new Vector3(0f, 0f, -0.05f), new Vector3(0.045f, 0.045f, 0.75f), Quaternion.identity);
            var tip = new Vector3(0f, 0f, 0.52f);
            var headCenter = new Vector3(0f, 0f, 0.38f);
            var b = new[] { new Vector3(0.08f, 0f, 0.3f), new Vector3(0f, 0.08f, 0.3f), new Vector3(-0.08f, 0f, 0.3f), new Vector3(0f, -0.08f, 0.3f) };
            for (int i = 0; i < 4; i++) Tri(v, t, headCenter, tip, b[i], b[(i + 1) % 4]);
            Quad(v, t, headCenter + Vector3.forward, b[0], b[1], b[2], b[3]);
            Box(v, t, new Vector3(0f, 0f, -0.36f), new Vector3(0.2f, 0.012f, 0.16f), Quaternion.identity);
            Box(v, t, new Vector3(0f, 0f, -0.36f), new Vector3(0.012f, 0.2f, 0.16f), Quaternion.identity);
            return SaveMesh(name, v, t);
        }

        /// <summary>A chunky war hammer, its handle along Y and its head at the top, across Z.</summary>
        static Mesh HammerMesh(string name)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            Box(v, t, new Vector3(0f, -0.2f, 0f), new Vector3(0.09f, 0.75f, 0.09f), Quaternion.identity);
            Box(v, t, new Vector3(0f, 0.3f, 0f), new Vector3(0.3f, 0.3f, 0.62f), Quaternion.identity);
            Box(v, t, new Vector3(0f, 0.3f, 0.36f), new Vector3(0.22f, 0.22f, 0.1f), Quaternion.identity);      // Striking faces
            Box(v, t, new Vector3(0f, 0.3f, -0.36f), new Vector3(0.22f, 0.22f, 0.1f), Quaternion.identity);
            Box(v, t, new Vector3(0f, -0.6f, 0f), new Vector3(0.14f, 0.1f, 0.14f), Quaternion.identity);        // Pommel
            return SaveMesh(name, v, t);
        }

        /// <summary>Earth Bending's wall at its default size: six rough stone blocks across 6 m, about 3 m tall and 1 m deep, standing on y = 0.</summary>
        static Mesh StoneWallMesh(string name)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            var random = new System.Random(7);
            float R(float min, float max) => min + (float)random.NextDouble() * (max - min);
            for (int i = 0; i < 6; i++)
            {
                float height = R(2.6f, 3.25f);
                var rotation = Quaternion.Euler(R(-3f, 3f), R(-5f, 5f), R(-3f, 3f));
                Box(v, t, new Vector3(-2.5f + i + R(-0.04f, 0.04f), height * 0.5f - 0.1f, R(-0.06f, 0.06f)), new Vector3(R(1f, 1.1f), height, R(0.9f, 1.1f)), rotation,
                    corner => corner.y > 0f ? new Vector3(R(-0.08f, 0.08f), R(-0.25f, 0.1f), R(-0.08f, 0.08f)) : Vector3.zero);   // Broken tops
            }
            return SaveMesh(name, v, t);
        }

        /// <summary>A rough stone chunk about 1 across.</summary>
        static Mesh ChunkMesh(string name)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            var random = new System.Random(3);
            Box(v, t, Vector3.zero, new Vector3(1f, 0.8f, 0.9f), Quaternion.identity,
                corner => new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 0.35f);
            return SaveMesh(name, v, t);
        }

        /// <summary>A flat ring of twelve rune dashes between radius 0.62 and 0.74, for the summon circle.</summary>
        static Mesh RuneRingMesh(string name)
        {
            const int dashes = 12;
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < dashes; i++)
            {
                float a0 = i * Mathf.PI * 2f / dashes, a1 = a0 + Mathf.PI * 2f / dashes * 0.55f;
                Vector3 At(float angle, float radius) => new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                int k = v.Count;
                v.AddRange(new[] { At(a0, 0.62f), At(a0, 0.74f), At(a1, 0.74f), At(a1, 0.62f) });
                t.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
            }
            return SaveMesh(name, v, t, flat: false);
        }
    }
}
