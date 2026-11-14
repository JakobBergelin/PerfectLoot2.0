using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Melee and buff looks (second pass): Swipe, Upheaval, Phantom Edge, Hookshot Slash, Riposte, Shield Wall, vanish and
/// appear, Invulnerability, Stances, Blood Rite and the Empowered / Shadowed buff. Colours: physical steel #E2E8F0, Upheaval
/// earth and stone with pale bone-white crack glow (not orange: fire owns it), Phantom Edge and the puffs ink and dark
/// violet with a bright lilac edge, Invulnerability gold-white, Blood Rite ruby. Domes and shields follow the style guide
/// (section 13): bright rim, a glowing ring where they cut the ground, a faint pattern, a see-through middle.
///
/// Placement every prefab is drawn for (VfxSystems wires them):
/// - Origin at the unit's root (its capsule centre, 1.05 m above its feet), +Z its facing; ground parts at y = -1.0.
/// - "Radius 1": drawn so VfxManager.Scale(handle, r) sizes it sideways. Meshes on the ground and sweeping blades scale with
///   the effect (Hierarchy); particles only move their emitters (Shape), so blobs, rocks and spikes stay in meters.
/// - Everything else is in meters and isn't meant to be scaled.
/// Per type (B: the wiring):
/// - SwipeArc: burst at the swinger's root, its facing, Scale = the swing's reach (2 x weapon reach). Drawn at radius 1, the
///   blade band 0.55 to 1.35 over 170 degrees (the +/-70 degree sweep plus the hit circle). Direction-neutral, so it reads
///   for both swing directions. Linger 0.55.
/// - UpheavalSlam: burst at the slam, Scale = slam radius (1.5). Pass the caster's facing if handy (the cracks turn with
///   it); identity is fine. Linger 1.4.
/// - UpheavalWarning: tracked on the DelayedAttack, Scale = its quake radius (5), released when it fires. Its sweep ring
///   reaches the edge in 0.7 s (Upheaval's QuakeDelay). Spawn it and UpheavalQuake with the SAME rotation (identity), so the
///   cracks that grow during the warning are the ones the quake breaks open. Linger 0.15.
/// - UpheavalQuake: burst where the quake fires, Scale = quake radius. Linger 1.7.
/// - PhantomSlash: burst at each strike (the enemy's centre), the striker's rotation after the blink (it faces the
///   enemy); two crossing slashes across that facing. In meters (about 1.7 m long). Linger 0.45.
/// - PhantomAfterimage: burst at the striker's root, its rotation: an ink ghost with a violet glow. Linger 0.6.
/// - HookHead: tracked at HookPoint, rotation LookRotation(HookPoint - player, up), so the tip points away. Linger 0.3.
/// - HookChain: tracked; SetEnds(hand, HookPoint) every frame (HookChainVfx). SetEnds puts the root itself, so don't Move
///   it after SetEnds (Move before it is harmless). Linger 0.05: links vanish at once on release.
/// - RiposteFlash: burst at the root, facing forward. Drawn for weapon reach 1.1 m (default); Scale(reach / 1.1) is
///   optional. Linger 0.5.
/// - DeflectSpark: burst where the projectile is turned or stopped, any rotation. Linger 0.45.
/// - VanishPuff / AppearPuff: bursts at the root. Linger 1.0 each.
/// - InvulnerableBubble: tracked at the root (any rotation). A 1.45 m sphere centred 0.55 m below the root, cut by the
///   floor in a 1.38 m ring. Linger 0.6.
/// - ShieldWallArc: tracked at the root with its rotation. In meters for Shield Wall's 2.5 m reach and +/-60 degree cone;
///   with an area bonus, Scale(Reach * AreaMultiplier / 2.5). Linger 0.4.
/// - StanceAura: tracked at the root; Tint right after Spawn (Str ruby, Agi teal, Int sapphire). Every part is particles,
///   so Tint colours all of it. Linger 0.8.
/// - BloodBurst: burst at the root. Linger 1.1.
/// - EmpoweredAura: tracked at the root while Empowered or Shadowed (one look for both: ruby with dark ink). Linger 0.9.
/// </summary>
public static partial class VfxBuilder
{
    static IEnumerable<(VfxType, GameObject)> MeleeEffects()
    {
        MeleeLooks.Init();
        Prewarms[VfxType.PhantomSlash] = 8;
        Prewarms[VfxType.PhantomAfterimage] = 8;
        Prewarms[VfxType.DeflectSpark] = 8;
        Prewarms[VfxType.UpheavalSlam] = 6;
        Prewarms[VfxType.UpheavalQuake] = 6;
        Prewarms[VfxType.UpheavalWarning] = 6;

        yield return (VfxType.SwipeArc, MeleeLooks.SwipeArc());
        yield return (VfxType.UpheavalSlam, MeleeLooks.UpheavalSlam());
        yield return (VfxType.UpheavalWarning, MeleeLooks.UpheavalWarning());
        yield return (VfxType.UpheavalQuake, MeleeLooks.UpheavalQuake());
        yield return (VfxType.PhantomSlash, MeleeLooks.PhantomSlash());
        yield return (VfxType.PhantomAfterimage, MeleeLooks.PhantomAfterimage());
        yield return (VfxType.HookHead, MeleeLooks.HookHead());
        yield return (VfxType.HookChain, MeleeLooks.HookChain());
        yield return (VfxType.RiposteFlash, MeleeLooks.RiposteFlash());
        yield return (VfxType.DeflectSpark, MeleeLooks.DeflectSpark());
        yield return (VfxType.VanishPuff, MeleeLooks.VanishPuff());
        yield return (VfxType.AppearPuff, MeleeLooks.AppearPuff());
        yield return (VfxType.InvulnerableBubble, MeleeLooks.InvulnerableBubble());
        yield return (VfxType.ShieldWallArc, MeleeLooks.ShieldWallArc());
        yield return (VfxType.StanceAura, MeleeLooks.StanceAura());
        yield return (VfxType.BloodBurst, MeleeLooks.BloodBurst());
        yield return (VfxType.EmpoweredAura, MeleeLooks.EmpoweredAura());
    }

    static class MeleeLooks
    {
        const float Ground = -1f;   // The floor under a unit's root
        const string Steel = "#E2E8F0", SteelBlue = "#BFD6FF", SteelDark = "#8A8F99";
        const string Stone = "#6E6052", StoneDark = "#3A3029", Dust = "#9A8A76", DustDark = "#5E5246", CrackGlow = "#F6E7C8";
        const string Ink = "#160C26", InkViolet = "#2E1A4A", Violet = "#7A4ADB", Lilac = "#C9B0FF", Edge = "#F2EAFF", SmokeViolet = "#4A2E86";
        const string GoldWhite = "#FFF4D6", Gold = "#FFD98A", GoldDeep = "#E8B04A";
        const string RubyDark = "#8E1B2E", RubyHot = "#FF8A9A", RubyInk = "#2A0A14";

        // A circle shape lies flat with this rotation, its arc running from +X round through +Z
        static readonly Vector3 FlatArc = new Vector3(90f, 0f, 0f);

        static Material _add, _ink, _rim, _steel, _rock;

        public static void Init()
        {
            _add = ParticleMaterial("VFX_MeleeAdd", true, null, CullMode.Off);       // Glowing meshes, coloured per particle
            _ink = ParticleMaterial("VFX_MeleeInk", false, null, CullMode.Off);      // Ink, dust and blood meshes
            _rim = ParticleMaterial("VFX_MeleeRim", true, RimTexture(), CullMode.Back);
            _steel = ComicGlow("VFX_HookSteel", "#C8D0DC", SteelBlue, 0.6f, 0f);
            _rock = ComicGlow("VFX_QuakeRock", "#9A8A76", "#5A4A3A", 0.45f, 0f);   // Lighter than the floor so it reads
        }

        // ---------- Swipe ----------

        // A heavy steel crescent over the whole swept arc, a ghost of it a beat later, a white cutting edge, sparks off the
        // edge and dust kicked up under it
        public static GameObject SwipeArc()
        {
            var root = Root_("VFX_SwipeArc", 0.55f, out _);
            var blade = Crescent("VFX_SwipeBlade", 0.55f, 1.35f, 170f);
            var main = MeshBurst(root, "Blade", _add, blade, -0.45f, 0.22f, Hex(Steel));
            Size(main, (0f, 0.8f), (0.35f, 1f), (1f, 1.06f));
            Alpha(main, (0f, 0.95f), (0.4f, 0.7f), (1f, 0f));
            var ghost = MeshBurst(root, "Ghost", _add, blade, -0.5f, 0.28f, Hex(SteelBlue), delay: 0.05f);
            Size(ghost, (0f, 0.95f), (1f, 1.12f));
            Alpha(ghost, (0f, 0.4f), (1f, 0f));
            var edge = MeshBurst(root, "Edge", _add, Crescent("VFX_SwipeEdge", 1.22f, 1.38f, 160f), -0.42f, 0.15f, Color.white);
            Alpha(edge, (0f, 1f), (0.5f, 0.85f), (1f, 0f));
            Ps(root, "Sparks", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.15f, 0.3f), new ParticleSystem.MinMaxCurve(3f, 6.5f), new ParticleSystem.MinMaxCurve(0.07f, 0.12f), Color.white, burst: 18);
                Arc(ps, 1.3f, 170f, -0.45f);
                Fade(ps, Hex("#FFFFFF"), Hex(Steel), Hex(SteelDark, 0f));
                Stretch(ps, 2.5f);
            });
            Ps(root, "Dust", _alpha, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.7f), new ParticleSystem.MinMaxCurve(0.6f, 1.6f), new ParticleSystem.MinMaxCurve(0.3f, 0.55f), Color.white, burst: 10);
                Arc(ps, 1f, 170f, Ground + 0.08f);
                Fade(ps, Hex("#A89C8C", 0.5f), Hex("#7A6E62", 0.3f), Hex("#4A4440", 0f));
                Grow(ps, 1.7f);
            });
            return Save(root);
        }

        // ---------- Upheaval ----------

        // The weapon hits the ground: a flash, a shock ring, cracks that stay a moment, dust and stones thrown up
        public static GameObject UpheavalSlam()
        {
            var root = Root_("VFX_UpheavalSlam", 1.4f, out _);
            var dark = MeshBurst(root, "Cracks", _ink, MeleeMeshes.Cracks("VFX_SlamCracksDark", 11, 6, 0.15f), Ground, 1.3f, Hex(StoneDark));
            Size(dark, (0f, 0.55f), (0.1f, 1f), (1f, 1f));
            Alpha(dark, (0f, 0.85f), (0.6f, 0.7f), (1f, 0f));
            var glow = MeshBurst(root, "Crack Glow", _add, MeleeMeshes.Cracks("VFX_SlamCracksGlow", 11, 6, 0.065f), Ground + 0.01f, 1.1f, Hex(CrackGlow));
            Size(glow, (0f, 0.55f), (0.1f, 1f), (1f, 1f));
            Alpha(glow, (0f, 1f), (0.3f, 0.75f), (1f, 0f));
            var flash = MeshBurst(root, "Flash", _add, Crescent("VFX_MeleeDisc", 0f, 0.55f, 360f), Ground + 0.02f, 0.12f, Hex(CrackGlow));
            Size(flash, (0f, 0.6f), (1f, 1.3f));
            Alpha(flash, (0f, 0.8f), (1f, 0f));
            var shock = MeshBurst(root, "Shock", _add, Crescent("VFX_MeleeShock", 0.93f, 1f, 360f), Ground + 0.02f, 0.3f, Hex("#FFF6E6"));
            Size(shock, (0f, 0.2f), (0.4f, 0.85f), (1f, 1.1f));
            Alpha(shock, (0f, 0.9f), (0.5f, 0.5f), (1f, 0f));
            Ps(root, "Dust", _alpha, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.6f, 1f), new ParticleSystem.MinMaxCurve(1f, 2.5f), new ParticleSystem.MinMaxCurve(0.45f, 0.8f), Color.white, burst: 20);
                Disc(ps, 0.85f, Ground + 0.15f, edgeOnly: true);
                Fade(ps, Hex(Dust, 0.7f), Hex(DustDark, 0.45f), Hex(DustDark, 0f));
                Grow(ps, 1.9f);
            });
            Ps(root, "Stones", _rock, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.6f, 0.85f), new ParticleSystem.MinMaxCurve(1.5f, 3f), new ParticleSystem.MinMaxCurve(0.1f, 0.22f), Color.white, burst: 10);
                MeshParticle(ps, MeleeMeshes.Rock("VFX_QuakeRockMesh"));
                Disc(ps, 0.5f, Ground + 0.1f);
                Rise(ps, 3f, 5f);
                Tumble(ps);
                var m = ps.main; m.gravityModifier = 1.8f;
                Size(ps, (0f, 1f), (0.75f, 1f), (1f, 0f));
            });
            Ps(root, "Sparks", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.2f, 0.35f), new ParticleSystem.MinMaxCurve(4f, 7f), new ParticleSystem.MinMaxCurve(0.06f, 0.11f), Color.white, burst: 10);
                Shape(ps, ParticleSystemShapeType.Cone, 0.3f, new Vector3(-90f, 0f, 0f), new Vector3(0f, Ground + 0.1f, 0f));
                var shape = ps.shape; shape.angle = 50f;
                var m = ps.main; m.gravityModifier = 1.5f; m.scalingMode = ParticleSystemScalingMode.Shape;
                Fade(ps, Hex("#FFFFFF"), Hex(CrackGlow), Hex(Dust, 0f));
                Stretch(ps, 2f);
            });
            return Save(root);
        }

        // Over the quake's delay: its edge marked on the ground, a ring sweeping out to it as the time runs out, cracks
        // spreading from the slam, and pebbles hopping as the ground rumbles
        public static GameObject UpheavalWarning()
        {
            var root = Root_("VFX_UpheavalWarning", 0.15f, out var instance);
            var edge = MeshBurst(root, "Edge", _add, Crescent("VFX_UpheavalEdge", 0.965f, 1f, 360f), Ground + 0.02f, 3f, Hex(CrackGlow));
            Alpha(edge, (0f, 0f), (0.04f, 0.55f), (1f, 0.55f));
            var sweep = MeshBurst(root, "Sweep", _add, Crescent("VFX_UpheavalSweep", 0.93f, 1f, 360f), Ground + 0.02f, 0.7f, Hex(CrackGlow));
            Size(sweep, (0f, 0.08f), (1f, 1f));
            Alpha(sweep, (0f, 0.25f), (0.8f, 0.7f), (1f, 0.9f));
            // Grown out over 0.7 s of a 3 s life, then held until the quake
            var dark = MeshBurst(root, "Cracks", _ink, MeleeMeshes.Cracks("VFX_QuakeCracksDark", 23, 9, 0.08f), Ground, 3f, Hex(StoneDark));
            Size(dark, (0f, 0.1f), (0.23f, 1f), (1f, 1f));
            Alpha(dark, (0f, 0.7f), (1f, 0.7f));
            var glow = MeshBurst(root, "Crack Glow", _add, MeleeMeshes.Cracks("VFX_QuakeCracksGlow", 23, 9, 0.034f), Ground + 0.01f, 3f, Hex(CrackGlow));
            Size(glow, (0f, 0.1f), (0.23f, 1f), (1f, 1f));
            Alpha(glow, (0f, 0.2f), (0.23f, 0.75f), (1f, 0.75f));
            instance.HideOnRelease = new Renderer[] { RendererOf(edge), RendererOf(sweep), RendererOf(dark), RendererOf(glow) };
            Ps(root, "Pebbles", _alpha, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.25f, 0.4f), 0f, new ParticleSystem.MinMaxCurve(0.07f, 0.14f), Hex(StoneDark), rate: 45f);
                Disc(ps, 1f, Ground + 0.05f);
                Rise(ps, 2f, 3.5f);
                var m = ps.main; m.gravityModifier = 2f;
            });
            Ps(root, "Haze", _alpha, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.6f, 0.9f), new ParticleSystem.MinMaxCurve(0.2f, 0.5f), new ParticleSystem.MinMaxCurve(0.5f, 0.9f), Color.white, rate: 14f);
                Disc(ps, 1f, Ground + 0.1f);
                Fade(ps, Hex(Dust, 0.35f), Hex(DustDark, 0.2f), Hex(DustDark, 0f));
                Grow(ps, 1.5f);
            });
            return Save(root);
        }

        // The ground heaves: the warning's cracks break open, stone spikes jut up and sink, rocks fly and dust rolls out
        public static GameObject UpheavalQuake()
        {
            var root = Root_("VFX_UpheavalQuake", 1.7f, out _);
            var dark = MeshBurst(root, "Cracks", _ink, MeleeMeshes.Cracks("VFX_QuakeCracksDark", 23, 9, 0.08f), Ground, 1.6f, Hex(StoneDark));
            Size(dark, (0f, 0.95f), (0.08f, 1.03f), (1f, 1f));
            Alpha(dark, (0f, 0.85f), (0.6f, 0.7f), (1f, 0f));
            var glow = MeshBurst(root, "Crack Glow", _add, MeleeMeshes.Cracks("VFX_QuakeCracksGlow", 23, 9, 0.034f), Ground + 0.01f, 1.3f, Hex(CrackGlow));
            Size(glow, (0f, 0.95f), (0.08f, 1.03f), (1f, 1f));
            Alpha(glow, (0f, 1f), (0.3f, 0.8f), (1f, 0f));
            var flash = MeshBurst(root, "Flash", _add, Crescent("VFX_MeleeDisc", 0f, 0.55f, 360f), Ground + 0.02f, 0.15f, Hex(CrackGlow));
            Size(flash, (0f, 0.4f), (1f, 1f));
            Alpha(flash, (0f, 0.7f), (1f, 0f));
            var shock = MeshBurst(root, "Shock", _add, Crescent("VFX_MeleeShock", 0.93f, 1f, 360f), Ground + 0.02f, 0.4f, Hex("#FFF6E6"));
            Size(shock, (0f, 0.1f), (0.35f, 0.8f), (1f, 1.05f));
            Alpha(shock, (0f, 0.9f), (0.5f, 0.5f), (1f, 0f));
            Ps(root, "Spikes", _rock, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.8f, 1.1f), 0f, new ParticleSystem.MinMaxCurve(0.75f, 1.5f), Color.white, burst: 18);
                MeshParticle(ps, MeleeMeshes.Spike("VFX_QuakeSpike"));
                Disc(ps, 0.92f, Ground - 0.04f);
                var m = ps.main;
                m.startRotation3D = true;
                m.startRotationX = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                m.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                m.startRotationZ = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                Size(ps, (0f, 0f), (0.1f, 1.05f), (0.18f, 1f), (0.7f, 1f), (1f, 0f));   // Jut up, hold, sink
            });
            Ps(root, "Rocks", _rock, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(1f, 1.3f), new ParticleSystem.MinMaxCurve(0.5f, 2f), new ParticleSystem.MinMaxCurve(0.25f, 0.5f), Color.white, burst: 20);
                MeshParticle(ps, MeleeMeshes.Rock("VFX_QuakeRockMesh"));
                Disc(ps, 0.9f, Ground + 0.1f);
                Rise(ps, 6f, 9f);
                Tumble(ps);
                var m = ps.main; m.gravityModifier = 1.5f;
                Size(ps, (0f, 1f), (0.8f, 1f), (1f, 0f));
            });
            Ps(root, "Dust", _alpha, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.8f, 1.3f), new ParticleSystem.MinMaxCurve(0.5f, 2f), new ParticleSystem.MinMaxCurve(0.7f, 1.3f), Color.white, burst: 22);
                Disc(ps, 1f, Ground + 0.2f);
                Fade(ps, Hex("#B8A890", 0.6f), Hex(DustDark, 0.45f), Hex(DustDark, 0f));
                Grow(ps, 1.8f);
            });
            Ps(root, "Dust Edge", _alpha, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.6f, 1f), new ParticleSystem.MinMaxCurve(1f, 2.5f), new ParticleSystem.MinMaxCurve(0.6f, 1f), Color.white, burst: 30);
                Disc(ps, 1f, Ground + 0.2f, edgeOnly: true);
                Fade(ps, Hex(Dust, 0.6f), Hex(DustDark, 0.35f), Hex(DustDark, 0f));
                Grow(ps, 1.7f);
            });
            Ps(root, "Glints", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.3f, 0.5f), 0f, new ParticleSystem.MinMaxCurve(0.07f, 0.12f), Color.white, burst: 20);
                Disc(ps, 0.9f, Ground + 0.05f);
                Rise(ps, 3f, 6f);
                var m = ps.main; m.gravityModifier = 1f;
                Fade(ps, Hex("#FFFFFF"), Hex(CrackGlow), Hex(Dust, 0f));
                Stretch(ps, 2f);
            });
            return Save(root);
        }

        // ---------- Phantom Edge ----------

        // Two big slashes crossing over the enemy, the second a beat later: an ink stroke, a violet glow around it so it
        // reads on dark floors and dark enemies, and a bright lilac cutting edge; a hard violet ring pings at the hit
        public static GameObject PhantomSlash()
        {
            var root = Root_("VFX_PhantomSlash", 0.5f, out _);
            var inkBlade = MeleeMeshes.SlashBlade("VFX_PhantomBladeInk", 0.3f);
            var glowBlade = MeleeMeshes.SlashBlade("VFX_PhantomBladeGlow", 0.42f);
            var edgeBlade = MeleeMeshes.SlashBlade("VFX_PhantomBladeEdge", 0.16f);
            Slash(root, "A", inkBlade, glowBlade, edgeBlade, 35f, 10f, 0f);
            Slash(root, "B", inkBlade, glowBlade, edgeBlade, -40f, -12f, 0.08f);
            Ps(root, "Ping", _rim, ps =>
            {
                Main(ps, 0.2f, 0f, 1f, Hex(Lilac), burst: 1);
                Size(ps, (0f, 0.3f), (1f, 1.6f));
                Alpha(ps, (0f, 0.9f), (1f, 0f));
            });
            Ps(root, "Shards", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.15f, 0.3f), new ParticleSystem.MinMaxCurve(5f, 9f), new ParticleSystem.MinMaxCurve(0.08f, 0.13f), Color.white, burst: 14);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.2f);
                Fade(ps, Hex(Edge), Hex(Lilac), Hex(Violet, 0f));
                Stretch(ps, 2.5f);
            });
            Ps(root, "Ink", _alpha, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.3f, 0.45f), new ParticleSystem.MinMaxCurve(2f, 4f), new ParticleSystem.MinMaxCurve(0.12f, 0.22f), Hex(Ink, 0.9f), burst: 6);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.2f);
                var m = ps.main; m.gravityModifier = 1.2f;
                Shrink(ps);
            });
            return Save(root);
        }

        static void Slash(GameObject root, string name, Mesh inkBlade, Mesh glowBlade, Mesh edgeBlade, float yaw, float tilt, float delay)
        {
            var glow = MeshBurst(root, $"Slash {name} Glow", _add, glowBlade, -0.02f, 0.3f, Hex("#9A6AF0"), delay);
            var ink = MeshBurst(root, $"Slash {name}", _ink, inkBlade, 0f, 0.34f, Hex(Ink), delay);
            var edge = MeshBurst(root, $"Slash {name} Edge", _add, edgeBlade, 0.02f, 0.28f, Hex(Edge), delay);
            foreach (var ps in new[] { glow, ink, edge })
            {
                var m = ps.main;
                m.startSize = 1.3f;     // About 2.6 m long: reads over a whole enemy at gameplay zoom
                m.startRotation3D = true;
                m.startRotationX = 0f;
                m.startRotationY = Mathf.Deg2Rad * yaw;
                m.startRotationZ = Mathf.Deg2Rad * tilt;
                Size(ps, (0f, 0.5f), (0.25f, 1f), (1f, 1.05f));
            }
            Alpha(glow, (0f, 0.7f), (0.5f, 0.45f), (1f, 0f));
            Alpha(ink, (0f, 0.85f), (0.5f, 0.6f), (1f, 0f));
            Alpha(edge, (0f, 1f), (0.5f, 0.85f), (1f, 0f));
        }

        // A dark ink ghost of the striker (cloak, head and a blade held forward) in a violet glow, fading where it stood
        public static GameObject PhantomAfterimage()
        {
            var root = Root_("VFX_PhantomAfterimage", 0.6f, out _);
            var ghost = MeleeMeshes.Ghost("VFX_PhantomGhost");
            var glow = MeshBurst(root, "Glow", _add, ghost, 0f, 0.5f, Hex("#9A6AF0"));
            Size(glow, (0f, 1.15f), (1f, 1.25f));
            Alpha(glow, (0f, 0.85f), (0.4f, 0.6f), (1f, 0f));
            var body = MeshBurst(root, "Body", _ink, ghost, 0f, 0.45f, Hex(Ink));
            Size(body, (0f, 1f), (1f, 1.04f));
            Alpha(body, (0f, 0.75f), (0.4f, 0.55f), (1f, 0f));
            Ps(root, "Wisps", _alpha, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.6f), new ParticleSystem.MinMaxCurve(0.3f, 0.8f), new ParticleSystem.MinMaxCurve(0.35f, 0.6f), Color.white, burst: 8);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.5f, offset: new Vector3(0f, -0.2f, 0f));
                var m = ps.main; m.gravityModifier = -0.3f;
                Fade(ps, Hex(Ink, 0.7f), Hex(InkViolet, 0.45f), Hex(InkViolet, 0f));
                Grow(ps, 1.5f);
            });
            return Save(root);
        }

        // ---------- Hookshot Slash ----------

        // A chunky steel grappling hook, tip along +Z, glinting, with a short spark trail
        public static GameObject HookHead()
        {
            var root = Root_("VFX_HookHead", 0.3f, out var instance);
            var hook = MeshPart(root, "Hook", MeleeMeshes.Hook("VFX_HookMesh"), _steel, Vector3.zero, Vector3.one);
            instance.HideOnRelease = new Renderer[] { hook };
            Ps(root, "Glint", _additive, ps => Main(ps, 0.25f, 0f, 0.8f, Hex(SteelBlue, 0.35f), local: true, rate: 8f));
            Ps(root, "Trail", _additive, ps =>
            {
                Main(ps, 0.2f, 0f, new ParticleSystem.MinMaxCurve(0.12f, 0.2f), Color.white);
                var e = ps.emission; e.rateOverDistance = 10f;
                Fade(ps, Hex("#FFFFFF", 0.9f), Hex(SteelBlue, 0.6f), Hex(SteelBlue, 0f));
                Shrink(ps);
            });
            return Save(root);
        }

        // Interlocking steel links laid by HookChainVfx between its ends, over a faint glint line
        public static GameObject HookChain()
        {
            var root = Root_("VFX_HookChain", 0.05f, out _);
            var link = MeshPart(root, "Link", MeleeMeshes.ChainLink("VFX_ChainLink"), _steel, Vector3.zero, Vector3.one);
            link.gameObject.SetActive(false);
            var glint = ParticleMaterial("VFX_HookGlint", true, null, CullMode.Off);
            BoltLine(root, "Glint", glint, 0.16f, Hex(SteelBlue, 0.3f), Hex(SteelBlue, 0.15f));
            var chain = root.AddComponent<HookChainVfx>();
            chain.LinkTemplate = link.transform;
            return Save(root);
        }

        // ---------- Riposte and blocks ----------

        // A white half-moon guard flashing up at weapon reach, a hard ring and rays from the parry point, a fan over the cone
        // on the floor with a bright edge, sparks. Big and bright: it's on screen for a quarter second
        public static GameObject RiposteFlash()
        {
            var root = Root_("VFX_RiposteFlash", 0.5f, out _);
            // Low and in front: from the 50 degree camera anything above chest height ahead of the player sits under its
            // health bars
            var parry = new Vector3(0f, -0.55f, 0.85f);
            var guard = MeshBurst(root, "Guard", _add, Crescent("VFX_RiposteGuard", 0.62f, 0.95f, 160f), 0f, 0.34f, Color.white);
            guard.transform.localPosition = new Vector3(0f, Ground, 0.85f);
            guard.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);    // Stood up on the floor, its middle on top, facing forward
            Size(guard, (0f, 0.6f), (0.25f, 1f), (1f, 1.1f));
            Alpha(guard, (0f, 1f), (0.5f, 0.9f), (1f, 0f));
            var fan = MeshBurst(root, "Fan", _add, Crescent("VFX_RiposteFan", 0.2f, 1.15f, 120f), Ground + 0.03f, 0.32f, Hex(SteelBlue));
            Size(fan, (0f, 0.5f), (0.3f, 1f), (1f, 1.05f));
            Alpha(fan, (0f, 0.85f), (0.5f, 0.6f), (1f, 0f));
            var fanEdge = MeshBurst(root, "Fan Edge", _add, Crescent("VFX_RiposteFanEdge", 1.02f, 1.22f, 120f), Ground + 0.04f, 0.3f, Color.white);
            Size(fanEdge, (0f, 0.6f), (0.3f, 1f), (1f, 1.05f));
            Alpha(fanEdge, (0f, 1f), (0.5f, 0.8f), (1f, 0f));
            Ps(root, "Ping", _rim, ps =>
            {
                Main(ps, 0.26f, 0f, 1f, Hex(Steel), burst: 1);
                ps.transform.localPosition = parry;
                Size(ps, (0f, 0.3f), (1f, 1.8f));
                Alpha(ps, (0f, 1f), (1f, 0f));
            });
            Ps(root, "Flash", _additive, ps =>
            {
                Main(ps, 0.12f, 0f, 2f, Hex("#FFFFFF", 0.85f), burst: 1);
                ps.transform.localPosition = parry;
            });
            Ps(root, "Rays", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.1f, 0.18f), new ParticleSystem.MinMaxCurve(9f, 13f), new ParticleSystem.MinMaxCurve(0.18f, 0.26f), Color.white, burst: 12);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.05f, offset: parry);
                Fade(ps, Hex("#FFFFFF"), Hex(Steel), Hex(SteelBlue, 0f));
                Stretch(ps, 3f);
            });
            Ps(root, "Sparks", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.2f, 0.35f), new ParticleSystem.MinMaxCurve(4f, 8f), new ParticleSystem.MinMaxCurve(0.07f, 0.12f), Color.white, burst: 20);
                Shape(ps, ParticleSystemShapeType.Cone, 0.1f, offset: parry);    // Along +Z
                var shape = ps.shape; shape.angle = 55f;
                var m = ps.main; m.gravityModifier = 1f;
                Fade(ps, Hex("#FFFFFF"), Hex(Steel), Hex(SteelDark, 0f));
                Stretch(ps, 2.5f);
            });
            return Save(root);
        }

        // A projectile turned or stopped: a white flash, a steel ring pinging out and sparks
        public static GameObject DeflectSpark()
        {
            var root = Root_("VFX_DeflectSpark", 0.45f, out _);
            Ps(root, "Ping", _rim, ps =>
            {
                Main(ps, 0.22f, 0f, 1f, Hex(Steel), burst: 1);
                Size(ps, (0f, 0.25f), (1f, 1.3f));
                Alpha(ps, (0f, 1f), (1f, 0f));
            });
            Ps(root, "Flash", _additive, ps => Main(ps, 0.1f, 0f, 1.1f, Hex("#FFFFFF", 0.85f), burst: 1));
            Ps(root, "Sparks", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.15f, 0.3f), new ParticleSystem.MinMaxCurve(5f, 9f), new ParticleSystem.MinMaxCurve(0.06f, 0.11f), Color.white, burst: 14);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.1f);
                var m = ps.main; m.gravityModifier = 0.8f;
                Fade(ps, Hex("#FFFFFF"), Hex(SteelBlue), Hex(SteelDark, 0f));
                Stretch(ps, 2.5f);
            });
            return Save(root);
        }

        // ---------- Vanish and appear ----------

        // Ink pulls into the body over lilac streaks and a violet ring shrinking on the floor, then a hard lilac ring pops,
        // with violet smoke and sparks rising where it was. Ink alone vanishes on the dark floors, so the light parts lead.
        public static GameObject VanishPuff()
        {
            var root = Root_("VFX_VanishPuff", 1f, out _);
            const float pop = 0.2f;     // When the ink has collapsed
            var ringMesh = Crescent("VFX_PuffRing", 0.78f, 1f, 360f);
            Ps(root, "Implode", _alpha, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.26f, 0.32f), new ParticleSystem.MinMaxCurve(-3.2f, -2.4f), new ParticleSystem.MinMaxCurve(0.35f, 0.55f), Color.white, burst: 16);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.9f, offset: new Vector3(0f, -0.2f, 0f));
                var shape = ps.shape; shape.radiusThickness = 0f;
                Fade(ps, Hex(Ink, 0.9f), Hex(SmokeViolet, 0.8f), Hex(SmokeViolet, 0f));
            });
            Ps(root, "Streaks", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.24f, 0.3f), new ParticleSystem.MinMaxCurve(-4.6f, -3.8f), new ParticleSystem.MinMaxCurve(0.1f, 0.16f), Color.white, burst: 14);
                Shape(ps, ParticleSystemShapeType.Sphere, 1.2f, offset: new Vector3(0f, -0.2f, 0f));
                var shape = ps.shape; shape.radiusThickness = 0f;
                Fade(ps, Hex(Lilac, 0.9f), Hex(Edge), Hex(Edge, 0f));
                Stretch(ps, 2.5f);
            });
            var shadow = MeshBurst(root, "Ring Ink", _ink, ringMesh, Ground, 0.3f, Hex(Ink));
            Size(shadow, (0f, 1.2f), (1f, 0.2f));
            Alpha(shadow, (0f, 0f), (0.2f, 0.7f), (1f, 0.3f));
            var ring = MeshBurst(root, "Ring", _add, ringMesh, Ground + 0.01f, 0.25f, Hex(Lilac));
            Size(ring, (0f, 1.25f), (1f, 0.25f));
            Alpha(ring, (0f, 0.2f), (0.3f, 0.9f), (1f, 0.6f));
            Ps(root, "Ping", _rim, ps =>
            {
                Main(ps, 0.22f, 0f, 1f, Hex(Lilac), burst: 1);
                var m = ps.main; m.startDelay = pop;
                ps.transform.localPosition = new Vector3(0f, -0.2f, 0f);
                Size(ps, (0f, 0.4f), (1f, 2.2f));
                Alpha(ps, (0f, 1f), (1f, 0f));
            });
            Ps(root, "Flash", _additive, ps =>
            {
                Main(ps, 0.14f, 0f, 1.3f, Hex(Edge, 0.8f), burst: 1);
                var m = ps.main; m.startDelay = pop;
            });
            Ps(root, "Smoke", _alpha, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.6f, 0.9f), new ParticleSystem.MinMaxCurve(0.3f, 1.2f), new ParticleSystem.MinMaxCurve(0.6f, 1f), Color.white, burst: 12);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.45f, offset: new Vector3(0f, -0.2f, 0f));
                var m = ps.main; m.startDelay = pop; m.gravityModifier = -0.15f;
                Fade(ps, Hex(SmokeViolet, 0.85f), Hex(InkViolet, 0.5f), Hex(InkViolet, 0f));
                Grow(ps, 1.7f);
            });
            Ps(root, "Sparks", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.35f, 0.55f), new ParticleSystem.MinMaxCurve(2.5f, 4.5f), new ParticleSystem.MinMaxCurve(0.1f, 0.16f), Color.white, burst: 18);
                Shape(ps, ParticleSystemShapeType.Cone, 0.4f, new Vector3(-90f, 0f, 0f), new Vector3(0f, -0.6f, 0f));
                var shape = ps.shape; shape.angle = 30f;
                var m = ps.main; m.startDelay = pop; m.gravityModifier = -0.5f;
                Fade(ps, Hex(Edge), Hex(Lilac), Hex(Violet, 0f));
                Stretch(ps, 2f);
            });
            return Save(root);
        }

        // A hard lilac ring popping out, a white-lilac flash, violet smoke bursting outward, rings racing over the floor, sparks
        public static GameObject AppearPuff()
        {
            var root = Root_("VFX_AppearPuff", 1f, out _);
            var ringMesh = Crescent("VFX_PuffRing", 0.78f, 1f, 360f);
            Ps(root, "Ping", _rim, ps =>
            {
                Main(ps, 0.25f, 0f, 1f, Hex(Lilac), burst: 1);
                ps.transform.localPosition = new Vector3(0f, -0.2f, 0f);
                Size(ps, (0f, 0.4f), (1f, 2.6f));
                Alpha(ps, (0f, 1f), (1f, 0f));
            });
            Ps(root, "Flash", _additive, ps => Main(ps, 0.16f, 0f, 1.6f, Hex(Edge, 0.85f), burst: 1));
            Ps(root, "Burst", _alpha, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.4f, 0.65f), new ParticleSystem.MinMaxCurve(2f, 4f), new ParticleSystem.MinMaxCurve(0.45f, 0.8f), Color.white, burst: 16);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.3f, offset: new Vector3(0f, -0.3f, 0f));
                Fade(ps, Hex(SmokeViolet, 0.85f), Hex(InkViolet, 0.5f), Hex(InkViolet, 0f));
                Grow(ps, 1.6f);
            });
            var shadow = MeshBurst(root, "Ring Ink", _ink, ringMesh, Ground, 0.45f, Hex(Ink));
            Size(shadow, (0f, 0.3f), (0.5f, 1.2f), (1f, 1.5f));
            Alpha(shadow, (0f, 0.6f), (1f, 0f));
            var ring = MeshBurst(root, "Ring", _add, ringMesh, Ground + 0.01f, 0.4f, Hex(Lilac));
            Size(ring, (0f, 0.2f), (0.5f, 1.2f), (1f, 1.6f));
            Alpha(ring, (0f, 1f), (1f, 0f));
            var inner = MeshBurst(root, "Ring Inner", _add, ringMesh, Ground + 0.015f, 0.3f, Hex(Violet), delay: 0.06f);
            Size(inner, (0f, 0.15f), (1f, 1f));
            Alpha(inner, (0f, 0.9f), (1f, 0f));
            Ps(root, "Sparks", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.35f, 0.55f), new ParticleSystem.MinMaxCurve(3f, 5.5f), new ParticleSystem.MinMaxCurve(0.1f, 0.16f), Color.white, burst: 22);
                Disc(ps, 0.3f, Ground + 0.1f);
                var m = ps.main; m.gravityModifier = -0.6f;
                Fade(ps, Hex(Edge), Hex(Lilac), Hex(Violet, 0f));
                Stretch(ps, 2f);
            });
            return Save(root);
        }

        // ---------- Buffs ----------

        // A gold-white dome: a camera-facing rim (a billboard of the sphere's outline, so it reads like a fresnel edge), a
        // bright ring where it meets the floor with pulses running out to it, a faint turning lattice and an almost clear
        // shell
        public static GameObject InvulnerableBubble()
        {
            const float radius = 1.45f, centre = -0.55f;
            float cut = Mathf.Sqrt(radius * radius - (Ground - centre) * (Ground - centre));    // 1.38 m
            var root = Root_("VFX_InvulnerableBubble", 0.6f, out var instance);
            var shell = MeshPart(root, "Shell", MeleeMeshes.DomeShell("VFX_BubbleShell", radius, centre, Ground, 10, 32),
                Tinted("VFX_BubbleFill", GoldWhite, 0.06f), Vector3.zero, Vector3.one);
            var lattice = MeshPart(root, "Lattice", MeleeMeshes.DomeLattice("VFX_BubbleLattice", radius * 1.003f, centre, Ground, 0.045f),
                Tinted("VFX_BubbleLattice", Gold, 0.28f), Vector3.zero, Vector3.one);
            lattice.gameObject.AddComponent<VfxSpin>().DegreesPerSecond = 12f;
            var ring = MeshPart(root, "Ground Ring", Crescent("VFX_BubbleRing", cut - 0.12f, cut + 0.04f, 360f),
                Tinted("VFX_BubbleRing", GoldWhite, 0.95f), new Vector3(0f, Ground + 0.02f, 0f), Vector3.one);
            var ringGlow = MeshPart(root, "Ground Glow", Crescent("VFX_BubbleRingGlow", cut - 0.45f, cut + 0.12f, 360f),
                Tinted("VFX_BubbleRingGlow", Gold, 0.3f), new Vector3(0f, Ground + 0.01f, 0f), Vector3.one);
            var rim = Ps(root, "Rim", _rim, ps =>
            {
                Main(ps, 1000f, 0f, radius * 2f / 0.95f, Hex(GoldWhite, 0.9f), burst: 1, local: true);   // The texture's rim is at 0.95
                var m = ps.main; m.maxParticles = 1;
                ps.transform.localPosition = new Vector3(0f, centre, 0f);
            });
            instance.HideOnRelease = new Renderer[] { shell, lattice, ring, ringGlow, RendererOf(rim) };
            var pulse = Ps(root, "Pulse", _add, ps =>
            {
                Main(ps, 1.1f, 0f, 1f, Hex(GoldWhite), local: true);
                MeshParticle(ps, Crescent("VFX_BubblePulse", 0.9f, 1f, 360f));
                ps.transform.localPosition = new Vector3(0f, Ground + 0.03f, 0f);
                Every(ps, 1.1f);
                Size(ps, (0f, 0.15f), (1f, cut));
                Alpha(ps, (0f, 0f), (0.2f, 0.6f), (1f, 0f));
            });
            Ps(root, "Sparkles", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.5f, 0.8f), new ParticleSystem.MinMaxCurve(0.1f, 0.3f), new ParticleSystem.MinMaxCurve(0.08f, 0.15f), Color.white, rate: 14f, local: true);
                Shape(ps, ParticleSystemShapeType.Sphere, radius, offset: new Vector3(0f, centre, 0f));
                var shape = ps.shape; shape.radiusThickness = 0f;
                var m = ps.main; m.gravityModifier = -0.2f;
                Fade(ps, Hex("#FFFFFF"), Hex(GoldWhite), Hex(GoldDeep, 0f));
                Shrink(ps);
            });
            return Save(root);
        }

        // A curved, slightly leaning wall of steel light at the block cone's edge (2.5 m, +/-60 degrees): bright top rim, ends
        // and ground line, a faint hex pattern, an almost clear middle, a faint floor over the cone and motes rising off it
        public static GameObject ShieldWallArc()
        {
            var root = Root_("VFX_ShieldWallArc", 0.4f, out var instance);
            var floor = MeshPart(root, "Floor", MeleeMeshes.WallFloor("VFX_WallFloor"), Tinted("VFX_WallFloor", SteelBlue, 0.05f), Vector3.zero, Vector3.one);
            var body = MeshPart(root, "Body", MeleeMeshes.WallBand("VFX_WallBand"), Tinted("VFX_WallBody", Steel, 0.07f), Vector3.zero, Vector3.one);
            var pattern = MeshPart(root, "Pattern", MeleeMeshes.WallHex("VFX_WallHex"), Tinted("VFX_WallPattern", SteelBlue, 0.25f), Vector3.zero, Vector3.one);
            var rim = MeshPart(root, "Rim", MeleeMeshes.WallRim("VFX_WallRim"), Tinted("VFX_WallRim", "#F4F8FF", 0.9f), Vector3.zero, Vector3.one);
            instance.HideOnRelease = new Renderer[] { floor, body, pattern, rim };
            Ps(root, "Motes", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.5f, 0.9f), 0f, new ParticleSystem.MinMaxCurve(0.08f, 0.15f), Color.white, rate: 18f, local: true);
                Arc(ps, MeleeMeshes.WallRadius, MeleeMeshes.WallHalfAngle * 2f, Ground + 0.05f);
                var m = ps.main; m.gravityModifier = -0.6f; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
                Fade(ps, Hex("#FFFFFF"), Hex(Steel), Hex(SteelBlue, 0f));
            });
            return Save(root);
        }

        // Tinted per stance (VfxManager.Tint), so all white here: a steady ring at the feet, pulses closing on it, three
        // gems circling and motes rising
        public static GameObject StanceAura()
        {
            var root = Root_("VFX_StanceAura", 0.8f, out var instance);
            var ring = Ps(root, "Ring", _add, ps =>
            {
                Main(ps, 1000f, 0f, 1f, Color.white, burst: 1, local: true);
                var m = ps.main; m.maxParticles = 1;
                MeshParticle(ps, Crescent("VFX_StanceRing", 0.6f, 0.72f, 360f));
                ps.transform.localPosition = new Vector3(0f, Ground + 0.02f, 0f);
                Alpha(ps, (0f, 0.65f), (1f, 0.65f));
            });
            var gems = Ps(root, "Gems", _add, ps =>
            {
                Main(ps, 1000f, 0f, 0.42f, Color.white, burst: 3, local: true);
                var m = ps.main; m.maxParticles = 3;
                MeshParticle(ps, Gem("VFX_Gem"));
                Shape(ps, ParticleSystemShapeType.Circle, 0.8f, FlatArc, new Vector3(0f, -0.35f, 0f));
                var shape = ps.shape; shape.radiusThickness = 0f; shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
                Orbit(ps, 2f);
                Alpha(ps, (0f, 0.9f), (1f, 0.9f));
            });
            instance.HideOnRelease = new Renderer[] { RendererOf(ring), RendererOf(gems) };
            Ps(root, "Pulse", _add, ps =>
            {
                Main(ps, 0.8f, 0f, 1f, Color.white, local: true);
                MeshParticle(ps, Crescent("VFX_StancePulse", 0.9f, 1f, 360f));
                ps.transform.localPosition = new Vector3(0f, Ground + 0.03f, 0f);
                Every(ps, 0.8f);
                Size(ps, (0f, 1.05f), (1f, 0.7f));
                Alpha(ps, (0f, 0f), (0.3f, 0.7f), (1f, 0f));
            });
            Ps(root, "Motes", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.6f, 1f), 0f, new ParticleSystem.MinMaxCurve(0.1f, 0.18f), Color.white, rate: 12f);
                Disc(ps, 0.55f, Ground + 0.05f);
                var m = ps.main; m.gravityModifier = -0.5f; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
                Alpha(ps, (0f, 1f), (0.45f, 0.7f), (1f, 0f));
                Shrink(ps);
            });
            return Save(root);
        }

        // Blood Rite: a ruby flash, glowing droplets thrown out and falling, a splash ring on the floor and sparks rising
        public static GameObject BloodBurst()
        {
            var root = Root_("VFX_BloodBurst", 1.1f, out _);
            Ps(root, "Flash", _additive, ps => Main(ps, 0.18f, 0f, 2.2f, Hex(Ruby, 0.75f), burst: 1));
            Ps(root, "Ping", _rim, ps =>
            {
                Main(ps, 0.22f, 0f, 1f, Hex(RubyHot), burst: 1);
                ps.transform.localPosition = new Vector3(0f, -0.2f, 0f);
                Size(ps, (0f, 0.4f), (1f, 2.4f));
                Alpha(ps, (0f, 1f), (1f, 0f));
            });
            Ps(root, "Drops", _alpha, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.45f, 0.75f), new ParticleSystem.MinMaxCurve(4f, 7.5f), new ParticleSystem.MinMaxCurve(0.16f, 0.3f), Color.white, burst: 26);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.35f);
                var m = ps.main; m.gravityModifier = 1.8f;
                Fade(ps, Hex(Ruby), Hex("#B01E3A"), Hex(RubyDark, 0f));
                Shrink(ps);
            });
            Ps(root, "Glow Drops", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.35f, 0.6f), new ParticleSystem.MinMaxCurve(3.5f, 6.5f), new ParticleSystem.MinMaxCurve(0.12f, 0.2f), Color.white, burst: 18);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.3f);
                var m = ps.main; m.gravityModifier = 1.2f;
                Fade(ps, Hex(RubyHot), Hex(Ruby), Hex(RubyDark, 0f));
                Stretch(ps, 1.8f);
            });
            var stain = MeshBurst(root, "Stain", _ink, Crescent("VFX_MeleeDisc", 0f, 0.55f, 360f), Ground, 0.9f, Hex(RubyDark));
            Size(stain, (0f, 0.4f), (0.2f, 1.3f), (1f, 1.4f));
            Alpha(stain, (0f, 0.7f), (0.6f, 0.5f), (1f, 0f));
            var ring = MeshBurst(root, "Ring", _add, Crescent("VFX_BloodRing", 0.8f, 1f, 360f), Ground + 0.01f, 0.45f, Hex(Ruby));
            Size(ring, (0f, 0.3f), (0.4f, 1.4f), (1f, 1.9f));
            Alpha(ring, (0f, 0.9f), (1f, 0f));
            Ps(root, "Rising", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.6f, 0.9f), 0f, new ParticleSystem.MinMaxCurve(0.12f, 0.2f), Color.white, burst: 16);
                Disc(ps, 0.6f, Ground + 0.05f);
                var m = ps.main; m.gravityModifier = -1f; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
                Fade(ps, Hex(RubyHot), Hex(Ruby), Hex(RubyDark, 0f));
                Stretch(ps, 1.5f);
            });
            return Save(root);
        }

        // Empowered (Blood Rite) or Shadowed (Shadowstep): a ruby ring at the feet with pulses drawn in to the body, ruby
        // embers and dark ink wisps rising, a faint glow on the body
        public static GameObject EmpoweredAura()
        {
            var root = Root_("VFX_EmpoweredAura", 0.9f, out var instance);
            var ring = MeshPart(root, "Ring", Crescent("VFX_EmpoweredRing", 0.8f, 0.98f, 360f), Tinted("VFX_EmpoweredRing", Ruby, 0.75f),
                new Vector3(0f, Ground + 0.015f, 0f), Vector3.one);
            instance.HideOnRelease = new Renderer[] { ring };
            Ps(root, "Pulse", _add, ps =>
            {
                Main(ps, 0.7f, 0f, 1f, Hex(Ruby), local: true);
                MeshParticle(ps, Crescent("VFX_EmpoweredPulse", 0.9f, 1f, 360f));
                ps.transform.localPosition = new Vector3(0f, Ground + 0.03f, 0f);
                Every(ps, 0.7f);
                Size(ps, (0f, 1.05f), (1f, 0.5f));
                Alpha(ps, (0f, 0f), (0.3f, 0.85f), (1f, 0f));
            });
            Ps(root, "Body", _additive, ps => Main(ps, 0.4f, 0f, 1.8f, Hex(Ruby, 0.2f), local: true, rate: 6f));
            Ps(root, "Embers", _additive, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.6f, 0.9f), new ParticleSystem.MinMaxCurve(0.2f, 0.5f), new ParticleSystem.MinMaxCurve(0.15f, 0.26f), Color.white, rate: 22f);
                Disc(ps, 0.55f, Ground + 0.1f);
                var m = ps.main; m.gravityModifier = -0.6f; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
                Fade(ps, Hex(RubyHot), Hex(Ruby), Hex(RubyDark, 0f));
                Shrink(ps);
            });
            Ps(root, "Wisps", _alpha, ps =>
            {
                Main(ps, new ParticleSystem.MinMaxCurve(0.7f, 1f), new ParticleSystem.MinMaxCurve(0.1f, 0.3f), new ParticleSystem.MinMaxCurve(0.35f, 0.55f), Color.white, rate: 6f);
                Shape(ps, ParticleSystemShapeType.Sphere, 0.45f, offset: new Vector3(0f, -0.4f, 0f));
                var m = ps.main; m.gravityModifier = -0.25f;
                Fade(ps, Hex(RubyInk, 0.55f), Hex(RubyInk, 0.35f), Hex(RubyInk, 0f));
                Grow(ps, 1.4f);
            });
            return Save(root);
        }

        // ---------- helpers ----------

        /// <summary>One mesh particle lying in the effect (Local), y meters up from the root; scales with the effect.</summary>
        static ParticleSystem MeshBurst(GameObject root, string name, Material material, Mesh mesh, float y, float life, Color color, float delay = 0f)
        {
            return Ps(root, name, material, ps =>
            {
                Main(ps, life, 0f, 1f, color, burst: 1, local: true);
                MeshParticle(ps, mesh);
                ps.transform.localPosition = new Vector3(0f, y, 0f);
                if (delay > 0f) { var m = ps.main; m.startDelay = delay; }
            });
        }

        /// <summary>Size over the particle's life, straight lines between the keys (time 0 to 1, size times the start size).</summary>
        static void Size(ParticleSystem ps, params (float time, float size)[] keys)
        {
            var frames = new Keyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                float into = i > 0 ? (keys[i].size - keys[i - 1].size) / Mathf.Max(1e-4f, keys[i].time - keys[i - 1].time) : 0f;
                float outOf = i < keys.Length - 1 ? (keys[i + 1].size - keys[i].size) / Mathf.Max(1e-4f, keys[i + 1].time - keys[i].time) : 0f;
                frames[i] = new Keyframe(keys[i].time, keys[i].size, i > 0 ? into : outOf, i < keys.Length - 1 ? outOf : into);
            }
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(frames));
        }

        /// <summary>Alpha over the particle's life (time 0 to 1); the colour stays the start colour (or the tint).</summary>
        static void Alpha(ParticleSystem ps, params (float time, float alpha)[] keys)
        {
            var alphas = new GradientAlphaKey[keys.Length];
            for (int i = 0; i < keys.Length; i++) alphas[i] = new GradientAlphaKey(keys[i].alpha, keys[i].time);
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alphas);
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = gradient;
        }

        /// <summary>Emitters on a flat disc of radius r (or its edge), y meters up. Shape scaling: VfxManager.Scale moves them, sizes stay.</summary>
        static void Disc(ParticleSystem ps, float radius, float y, bool edgeOnly = false)
        {
            Shape(ps, ParticleSystemShapeType.Circle, radius, FlatArc, new Vector3(0f, y, 0f));
            var shape = ps.shape; shape.radiusThickness = edgeOnly ? 0f : 1f;
            var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape;
        }

        /// <summary>Emitters on a flat arc of the given degrees centred on +Z (its edge only), y meters up, flying outward. Shape scaling.</summary>
        static void Arc(ParticleSystem ps, float radius, float degrees, float y)
        {
            Shape(ps, ParticleSystemShapeType.Circle, radius, FlatArc);
            var shape = ps.shape; shape.arc = degrees; shape.radiusThickness = 0f;
            // The arc starts at +X: turned so its middle is at +Z
            ps.transform.localPosition = new Vector3(0f, y, 0f);
            ps.transform.localRotation = Quaternion.Euler(0f, degrees * 0.5f - 90f, 0f);
            var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape;
        }

        /// <summary>Upward speed added over the life (m/s), in the effect's space.</summary>
        static void Rise(ParticleSystem ps, float min, float max)
        {
            var velocity = ps.velocityOverLifetime; velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(min, max);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }

        /// <summary>Circling the system's up axis at the given radians per second.</summary>
        static void Orbit(ParticleSystem ps, float speed)
        {
            var velocity = ps.velocityOverLifetime; velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = velocity.y = velocity.z = new ParticleSystem.MinMaxCurve(0f);
            velocity.orbitalX = velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(speed);
            velocity.radial = new ParticleSystem.MinMaxCurve(0f);
        }

        /// <summary>A random start turn on every axis and a random spin (mesh rocks).</summary>
        static void Tumble(ParticleSystem ps)
        {
            var m = ps.main;
            m.startRotation3D = true;
            m.startRotationX = m.startRotationY = m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var spin = ps.rotationOverLifetime; spin.enabled = true; spin.separateAxes = true;
            spin.x = spin.y = spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        }

        /// <summary>Looping: one particle every period seconds, the first at once.</summary>
        static void Every(ParticleSystem ps, float period)
        {
            var m = ps.main; m.loop = true; m.duration = period;
            var e = ps.emission; e.rateOverTime = 0f;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
        }

        static ParticleSystemRenderer RendererOf(ParticleSystem ps) => ps.GetComponent<ParticleSystemRenderer>();

        /// <summary>An additive, two-sided material in one colour, for mesh renderers (they don't carry a particle colour).</summary>
        static Material Tinted(string name, string hex, float alpha)
        {
            var material = ParticleMaterial(name, true, null, CullMode.Off);
            material.SetColor("_BaseColor", Hex(hex, alpha));
            return material;
        }

        /// <summary>A ring texture: clear in the middle, a faint glow rising toward a hard bright rim at 0.95 of its radius.</summary>
        static Texture2D RimTexture()
        {
            string path = $"{Root}/Textures/VFX_RimRing.png";
            if (!File.Exists(path))
            {
                const int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                        float a = d < 0.9f ? 0.3f * Mathf.Pow(d / 0.9f, 4f)
                                : d < 0.97f ? Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(0.9f, 0.93f, d))
                                : Mathf.Clamp01(1f - (d - 0.97f) / 0.03f);
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
    }

    /// <summary>The meshes for the melee looks (saved under Data/Vfx/Meshes by SaveMesh).</summary>
    static class MeleeMeshes
    {
        // Shield Wall: its reach and cone (ShieldWall.asset), and how the wall stands
        public const float WallRadius = 2.5f, WallHalfAngle = 60f;
        const float WallGround = -1f, WallHeight = 1.3f, WallLean = 0.2f;
        static float WallHalfLength => WallRadius * WallHalfAngle * Mathf.Deg2Rad;

        static readonly int[] IcoFaces = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                                           3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };

        static List<Vector3> IcoVertices()
        {
            float g = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new(-1, g, 0), new(1, g, 0), new(-1, -g, 0), new(1, -g, 0), new(0, -1, g), new(0, 1, g),
                new(0, -1, -g), new(0, 1, -g), new(g, 0, -1), new(g, 0, 1), new(-g, 0, -1), new(-g, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            return v;
        }

        /// <summary>Radial cracks in the ground plane out to about radius 1, jagged, tapering to points, with side cracks. The same seed gives the same cracks at any width.</summary>
        public static Mesh Cracks(string name, int seed, int branches, float width)
        {
            var random = new System.Random(seed);
            float R() => (float)random.NextDouble();
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int b = 0; b < branches; b++)
            {
                float heading = (b + (R() - 0.5f) * 0.6f) / branches * Mathf.PI * 2f;
                float length = 0.75f + 0.25f * R();
                var points = CrackPath(new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * 0.05f, heading, length, 6, R);
                Strip(v, t, points, width);
                int from = 2 + (int)(R() * 2f);
                float side = heading + (R() < 0.5f ? -1f : 1f) * (0.5f + 0.4f * R());
                Strip(v, t, CrackPath(points[from], side, length * (0.25f + 0.15f * R()), 3, R), width * 0.65f);
            }
            return SaveMesh(name, v, t, flat: false);
        }

        static List<Vector2> CrackPath(Vector2 start, float heading, float length, int segments, Func<float> random)
        {
            var points = new List<Vector2> { start };
            float step = length / segments;
            for (int i = 0; i < segments; i++)
            {
                heading += (random() - 0.5f) * 0.8f;
                start += new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * step;
                points.Add(start);
            }
            return points;
        }

        /// <summary>A flat band along the points (x, z), full width at the first and narrowing to a point at the last.</summary>
        static void Strip(List<Vector3> v, List<int> t, List<Vector2> points, float width)
        {
            int first = v.Count;
            for (int i = 0; i < points.Count; i++)
            {
                var direction = (points[Mathf.Min(points.Count - 1, i + 1)] - points[Mathf.Max(0, i - 1)]).normalized;
                var normal = new Vector2(-direction.y, direction.x) * (width * 0.5f * Mathf.Lerp(1f, 0.08f, i / (points.Count - 1f)));
                var p = points[i];
                v.Add(new Vector3(p.x + normal.x, 0f, p.y + normal.y));
                v.Add(new Vector3(p.x - normal.x, 0f, p.y - normal.y));
                if (i == 0) continue;
                int k = first + i * 2;
                t.AddRange(new[] { k - 2, k - 1, k + 1, k - 2, k + 1, k });
            }
        }

        /// <summary>A curved blade stroke in the ground plane through the origin, 2 long along X, bowed toward +Z, thickest past its middle.</summary>
        public static Mesh SlashBlade(string name, float width)
        {
            const int steps = 24;
            const float bow = 0.28f;
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i <= steps; i++)
            {
                float s = i / (float)steps, x = s * 2f - 1f;
                float z = bow * (1f - x * x) - bow * 0.5f;
                var tangent = new Vector2(1f, -2f * bow * x).normalized;
                var normal = new Vector2(-tangent.y, tangent.x);
                float w = width * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * s)), 0.7f) * (0.55f + 0.45f * s);   // Max: Sin(PI) is a hair below 0, and Pow would give NaN
                v.Add(new Vector3(x + normal.x * w * 0.35f, 0f, z + normal.y * w * 0.35f));
                v.Add(new Vector3(x - normal.x * w * 0.65f, 0f, z - normal.y * w * 0.65f));
                if (i == 0) continue;
                int k = i * 2;
                t.AddRange(new[] { k - 2, k - 1, k + 1, k - 2, k + 1, k });
            }
            return SaveMesh(name, v, t, flat: false);
        }

        /// <summary>A lumpy low-poly rock, about 1 across, a little flattened.</summary>
        public static Mesh Rock(string name)
        {
            var random = new System.Random(5);
            var points = IcoVertices();
            for (int i = 0; i < points.Count; i++)
                points[i] = Vector3.Scale(points[i] * (0.5f * (0.75f + 0.4f * (float)random.NextDouble())), new Vector3(1f, 0.8f, 1f));
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < IcoFaces.Length; i += 3)
                Tri(v, t, points[IcoFaces[i]], points[IcoFaces[i + 1]], points[IcoFaces[i + 2]], Vector3.zero);
            return SaveMesh(name, v, t);
        }

        /// <summary>A jagged stone spike, its base at the origin (so it grows up out of the ground), 1 tall.</summary>
        public static Mesh Spike(string name)
        {
            var random = new System.Random(7);
            float R() => (float)random.NextDouble();
            const int sides = 5;
            var bottom = new Vector3[sides];
            var middle = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = (i + (R() - 0.5f) * 0.4f) / sides * Mathf.PI * 2f;
                float radius = 0.18f + 0.08f * R();
                bottom[i] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                float twist = angle + 0.3f;
                middle[i] = new Vector3(Mathf.Cos(twist) * radius * 0.6f + 0.02f, 0.45f + 0.08f * R(), Mathf.Sin(twist) * radius * 0.6f);
            }
            var apex = new Vector3(0.05f, 1f, -0.03f);
            var inside = new Vector3(0.01f, 0.3f, 0f);
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Tri(v, t, bottom[i], bottom[j], middle[j], inside);
                Tri(v, t, bottom[i], middle[j], middle[i], inside);
                Tri(v, t, middle[i], middle[j], apex, inside);
                Tri(v, t, Vector3.zero, bottom[j], bottom[i], inside);
            }
            return SaveMesh(name, v, t);
        }

        /// <summary>A grappling hook about 0.9 long, its pointed tip along +Z: a shaft, a collar at the back and three barbed flukes curving back.</summary>
        public static Mesh Hook(string name)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            Box(v, t, new Vector3(0f, 0f, -0.1f), new Vector3(0.05f, 0.05f, 0.3f), Quaternion.identity);
            Box(v, t, new Vector3(0f, 0f, -0.42f), new Vector3(0.1f, 0.1f, 0.05f), Quaternion.Euler(0f, 0f, 45f));
            // The tip: a square point
            var tipBase = new[] { new Vector3(0.07f, 0f, 0.2f), new Vector3(0f, 0.07f, 0.2f), new Vector3(-0.07f, 0f, 0.2f), new Vector3(0f, -0.07f, 0.2f) };
            var tip = new Vector3(0f, 0f, 0.45f);
            var tipInside = new Vector3(0f, 0f, 0.27f);
            for (int i = 0; i < 4; i++) Tri(v, t, tipBase[i], tipBase[(i + 1) % 4], tip, tipInside);
            Tri(v, t, tipBase[0], tipBase[1], tipBase[2], tipInside);
            Tri(v, t, tipBase[0], tipBase[2], tipBase[3], tipInside);
            for (int k = 0; k < 3; k++)
            {
                var radial = Quaternion.Euler(0f, 0f, k * 120f + 90f) * Vector3.right;
                var a = new Vector3(0f, 0f, 0.14f) + radial * 0.04f;
                var b = a + radial * 0.26f - Vector3.forward * 0.2f;
                var c = b + radial * 0.05f + Vector3.forward * 0.15f;
                Beam(v, t, a, b, 0.06f);
                Beam(v, t, b, c, 0.05f);
            }
            return SaveMesh(name, v, t);
        }

        /// <summary>One chain link, a flat rounded-off rectangle 0.36 long (Z) and 0.22 wide (X), 0.06 thick.</summary>
        public static Mesh ChainLink(string name)
        {
            const float length = 0.36f, width = 0.22f, thick = 0.06f;
            var v = new List<Vector3>();
            var t = new List<int>();
            foreach (float side in new[] { -1f, 1f })
            {
                Box(v, t, new Vector3(side * (width - thick) * 0.5f, 0f, 0f), new Vector3(thick * 0.5f, thick * 0.5f, length * 0.5f - thick), Quaternion.identity);
                Box(v, t, new Vector3(0f, 0f, side * (length - thick) * 0.5f), new Vector3(width * 0.5f - thick, thick * 0.5f, thick * 0.5f), Quaternion.identity);
                // Corners cut at 45 degrees, so the link reads round rather than square
                foreach (float other in new[] { -1f, 1f })
                    Box(v, t, new Vector3(other * (width * 0.5f - thick * 0.85f), 0f, side * (length * 0.5f - thick * 0.85f)),
                        new Vector3(thick * 0.5f, thick * 0.5f, thick * 0.75f), Quaternion.Euler(0f, other * side * 45f, 0f));
            }
            return SaveMesh(name, v, t);
        }

        /// <summary>
        /// A ghost of a unit in a cloak, for a root 1.05 m above the feet: an eight-sided cloak with a ragged hem from the
        /// floor to the shoulders, a head, and an arm holding a blade forward (+Z).
        /// </summary>
        public static Mesh Ghost(string name)
        {
            const int sides = 8;
            var v = new List<Vector3>();
            var t = new List<int>();
            var hem = new Vector3[sides];
            var shoulder = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = i / (float)sides * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                hem[i] = direction * (i % 2 == 0 ? 0.42f : 0.34f) + Vector3.up * (i % 2 == 0 ? -1f : -0.88f);
                shoulder[i] = Vector3.Scale(direction, new Vector3(0.26f, 0f, 0.2f)) + Vector3.up * 0.3f;
            }
            var neck = new Vector3(0f, 0.36f, 0f);
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                var inside = new Vector3(0f, (hem[i].y + shoulder[i].y) * 0.5f, 0f);
                Tri(v, t, hem[i], hem[j], shoulder[j], inside);
                Tri(v, t, hem[i], shoulder[j], shoulder[i], inside);
                Tri(v, t, shoulder[i], shoulder[j], neck, new Vector3(0f, -0.35f, 0f));
            }
            var head = IcoVertices();
            var headCentre = new Vector3(0f, 0.56f, 0f);
            for (int i = 0; i < IcoFaces.Length; i += 3)
                Tri(v, t, headCentre + head[IcoFaces[i]] * 0.21f, headCentre + head[IcoFaces[i + 1]] * 0.21f, headCentre + head[IcoFaces[i + 2]] * 0.21f, headCentre);
            Beam(v, t, new Vector3(0.22f, 0.22f, 0f), new Vector3(0.32f, 0.02f, 0.18f), 0.12f);
            Beam(v, t, new Vector3(0.32f, 0.04f, 0.1f), new Vector3(0.36f, 0.12f, 0.95f), 0.07f);
            return SaveMesh(name, v, t);
        }

        /// <summary>A sphere of radius r around (0, centreY, 0), only the part above groundY, open at the floor.</summary>
        public static Mesh DomeShell(string name, float radius, float centreY, float groundY, int rings, int segments)
        {
            float thetaMax = Mathf.Acos(Mathf.Clamp((groundY - centreY) / radius, -1f, 1f));
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float theta = thetaMax * r / rings;
                float y = centreY + radius * Mathf.Cos(theta), ring = radius * Mathf.Sin(theta);
                for (int s = 0; s <= segments; s++)
                {
                    float phi = s / (float)segments * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(phi) * ring, y, Mathf.Sin(phi) * ring));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = r * (segments + 1) + s, b = a + 1, c = a + segments + 1, d = c + 1;
                    t.AddRange(new[] { a, c, b, b, c, d });
                }
            return SaveMesh(name, v, t, flat: false);
        }

        /// <summary>A geodesic lattice of thin bands on the same sphere (a once-subdivided icosahedron's edges), clipped at the floor.</summary>
        public static Mesh DomeLattice(string name, float radius, float centreY, float groundY, float width)
        {
            var points = IcoVertices();
            var mids = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (mids.TryGetValue(key, out int m)) return m;
                points.Add(((points[a] + points[b]) * 0.5f).normalized);
                return mids[key] = points.Count - 1;
            }
            var edges = new HashSet<(int, int)>();
            void Edge(int a, int b) => edges.Add(a < b ? (a, b) : (b, a));
            for (int i = 0; i < IcoFaces.Length; i += 3)
            {
                int a = IcoFaces[i], b = IcoFaces[i + 1], c = IcoFaces[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                Edge(a, ab); Edge(ab, b); Edge(b, bc); Edge(bc, c); Edge(c, ca); Edge(ca, a);
                Edge(ab, bc); Edge(bc, ca); Edge(ca, ab);
            }
            var centre = new Vector3(0f, centreY, 0f);
            var v = new List<Vector3>();
            var t = new List<int>();
            foreach (var (a, b) in edges)
            {
                var p = centre + points[a] * radius;
                var q = centre + points[b] * radius;
                if (p.y < groundY && q.y < groundY) continue;
                if (p.y < groundY) p = Vector3.Lerp(q, p, (q.y - groundY) / (q.y - p.y));
                else if (q.y < groundY) q = Vector3.Lerp(p, q, (p.y - groundY) / (p.y - q.y));
                var side = Vector3.Cross(q - p, ((p + q) * 0.5f - centre).normalized).normalized * (width * 0.5f);
                Quad(v, t, p - side, p + side, q + side, q - side);
            }
            return SaveMesh(name, v, t, flat: false);
        }

        /// <summary>Shield Wall: a point on the wall, u meters along it from its middle, h meters up from the floor.</summary>
        static Vector3 WallPoint(float u, float h)
        {
            float radius = WallRadius + WallLean * h / WallHeight;
            float angle = u / WallRadius;
            return new Vector3(Mathf.Sin(angle) * radius, WallGround + h, Mathf.Cos(angle) * radius);
        }

        static Vector3 FloorPoint(float u, float radius, float lift)
        {
            float angle = u / WallRadius;
            return new Vector3(Mathf.Sin(angle) * radius, WallGround + lift, Mathf.Cos(angle) * radius);
        }

        /// <summary>The wall's surface, floor to top.</summary>
        public static Mesh WallBand(string name)
        {
            const int steps = 24;
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < steps; i++)
            {
                float u0 = Mathf.Lerp(-WallHalfLength, WallHalfLength, i / (float)steps), u1 = Mathf.Lerp(-WallHalfLength, WallHalfLength, (i + 1) / (float)steps);
                Quad(v, t, WallPoint(u0, 0f), WallPoint(u1, 0f), WallPoint(u1, WallHeight), WallPoint(u0, WallHeight));
            }
            return SaveMesh(name, v, t, flat: false);
        }

        /// <summary>The wall's bright edges: a band along its top, its two ends, and a line on the floor along its foot.</summary>
        public static Mesh WallRim(string name)
        {
            const int steps = 24;
            const float top = 0.12f, end = 0.1f, line = 0.12f;
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < steps; i++)
            {
                float u0 = Mathf.Lerp(-WallHalfLength, WallHalfLength, i / (float)steps), u1 = Mathf.Lerp(-WallHalfLength, WallHalfLength, (i + 1) / (float)steps);
                Quad(v, t, WallPoint(u0, WallHeight - top), WallPoint(u1, WallHeight - top), WallPoint(u1, WallHeight), WallPoint(u0, WallHeight));
                Quad(v, t, FloorPoint(u0, WallRadius - line, 0.02f), FloorPoint(u1, WallRadius - line, 0.02f), FloorPoint(u1, WallRadius + line, 0.02f), FloorPoint(u0, WallRadius + line, 0.02f));
            }
            foreach (float side in new[] { -1f, 1f })
            {
                float outer = side * WallHalfLength, inner = side * (WallHalfLength - end);
                Quad(v, t, WallPoint(outer, 0f), WallPoint(inner, 0f), WallPoint(inner, WallHeight - top), WallPoint(outer, WallHeight - top));
            }
            return SaveMesh(name, v, t, flat: false);
        }

        /// <summary>A faint hexagon pattern over the wall, inside its rim.</summary>
        public static Mesh WallHex(string name)
        {
            const float cell = 0.32f, width = 0.035f;
            float dx = 1.5f * cell, dy = Mathf.Sqrt(3f) * cell;
            float uMax = WallHalfLength - 0.12f, hMin = 0.06f, hMax = WallHeight - 0.14f;
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int col = -12; col <= 12; col++)
                for (int row = -1; row <= 6; row++)
                {
                    var centre = new Vector2(col * dx, row * dy + ((col & 1) == 1 ? dy * 0.5f : 0f));
                    // Three edges per cell (upper right, top, upper left): the lattice draws each edge once
                    for (int k = 0; k < 3; k++)
                    {
                        var a = centre + cell * new Vector2(Mathf.Cos(k * Mathf.PI / 3f), Mathf.Sin(k * Mathf.PI / 3f));
                        var b = centre + cell * new Vector2(Mathf.Cos((k + 1) * Mathf.PI / 3f), Mathf.Sin((k + 1) * Mathf.PI / 3f));
                        if (Mathf.Abs(a.x) > uMax || Mathf.Abs(b.x) > uMax || a.y < hMin || b.y < hMin || a.y > hMax || b.y > hMax) continue;
                        var across = new Vector2(-(b - a).y, (b - a).x).normalized * (width * 0.5f);
                        Quad(v, t, WallPoint(a.x - across.x, a.y - across.y), WallPoint(a.x + across.x, a.y + across.y),
                             WallPoint(b.x + across.x, b.y + across.y), WallPoint(b.x - across.x, b.y - across.y));
                    }
                }
            return SaveMesh(name, v, t, flat: false);
        }

        /// <summary>The floor of the block cone, from near the caster out to the wall.</summary>
        public static Mesh WallFloor(string name)
        {
            const int steps = 24;
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < steps; i++)
            {
                float u0 = Mathf.Lerp(-WallHalfLength, WallHalfLength, i / (float)steps), u1 = Mathf.Lerp(-WallHalfLength, WallHalfLength, (i + 1) / (float)steps);
                Quad(v, t, FloorPoint(u0, 0.4f, 0.005f), FloorPoint(u1, 0.4f, 0.005f), FloorPoint(u1, WallRadius, 0.005f), FloorPoint(u0, WallRadius, 0.005f));
            }
            return SaveMesh(name, v, t, flat: false);
        }

        // ---------- mesh pieces ----------

        /// <summary>A triangle facing away from the inside point (for solid meshes on back-culled materials).</summary>
        static void Tri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - inside) < 0f) (b, c) = (c, b);
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        /// <summary>A quad a-b-c-d (for two-sided materials, so either winding).</summary>
        static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        static void Box(List<Vector3> v, List<int> t, Vector3 centre, Vector3 half, Quaternion rotation)
        {
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
                c[i] = centre + rotation * Vector3.Scale(half, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
            int[] faces = { 0,1,3,2, 4,6,7,5, 0,4,5,1, 2,3,7,6, 0,2,6,4, 1,5,7,3 };
            for (int f = 0; f < faces.Length; f += 4)
            {
                Tri(v, t, c[faces[f]], c[faces[f + 1]], c[faces[f + 2]], centre);
                Tri(v, t, c[faces[f]], c[faces[f + 2]], c[faces[f + 3]], centre);
            }
        }

        /// <summary>A square bar from a to b.</summary>
        static void Beam(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, float thickness)
        {
            var along = b - a;
            var up = Mathf.Abs(Vector3.Dot(along.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            Box(v, t, (a + b) * 0.5f, new Vector3(thickness * 0.5f, thickness * 0.5f, along.magnitude * 0.5f), Quaternion.LookRotation(along, up));
        }
    }
}
