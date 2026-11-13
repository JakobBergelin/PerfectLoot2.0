using Unity.Entities;
using Unity.Mathematics;

/// <summary>The clips a rigid model can play. Values index RigidAnimationBlob.Clips; keep them and add new ones at the end.</summary>
public enum RigidClip : byte
{
    Idle = 0,
    Move = 1,
    Attack = 2,
    Hit = 3,
    Death = 4,
    // Player skills
    Cast = 5,       // Fireball wind-up (held)
    Release = 6,    // Fireball thrown
    Shoot = 7,      // Barrage bolt
    PunchL = 8,     // Fists of Thunder
    PunchR = 9,
    Spin = 10,      // Whirlwind (loops)
    Dash = 11,
    Sweep = 12,     // Tornado cast
    Slash = 13,     // Swipe, Riposte, Phantom Edge strikes
    Slam = 14,      // Upheaval, Earth Bending
    Throw = 15,     // Cursed Hammer, Captain's Shield, Poison Dart, Hookshot
    Thrust = 16,    // Frost Lance
    Nova = 17,      // Self casts: Cold Snap, Blood Rite, Invulnerability, Stances, Immolation, Gale Ward
    Summon = 18,    // Companions, Thrall, Hydra, Mirror Image, Meteor, Timezone, Teleport
    Bow = 19,       // Multishot, Plague Arrow
    Channel = 20,   // Flamethrower, Tesla Coil (loops while channeling)
    Guard = 21,     // Shield Wall (loops while raised)
    Tuck = 22,      // Ball Lightning, Sonic Ball (loops while flying or rolling)
}

/// <summary>The clip a player plays once when it uses a skill (NetPlayerActions.CastSkill), or Idle for none.</summary>
public static class SkillClips
{
    public static RigidClip For(SkillType skill) => skill switch
    {
        SkillType.Fireball => RigidClip.Release,
        SkillType.Barrage => RigidClip.Shoot,
        SkillType.Tornado => RigidClip.Sweep,
        SkillType.FistOfThunder => RigidClip.PunchL,    // PunchR on every other cast
        SkillType.Swipe or SkillType.Riposte or SkillType.PhantomEdge => RigidClip.Slash,
        SkillType.Upheaval or SkillType.EarthBending => RigidClip.Slam,
        SkillType.CursedHammer or SkillType.CaptainsShield or SkillType.PoisonDart or SkillType.HookshotSlash or SkillType.FrozenOrb => RigidClip.Throw,
        SkillType.FrostLance => RigidClip.Thrust,
        SkillType.ColdSnap or SkillType.BloodRite or SkillType.Invulnerability or SkillType.Stances or SkillType.Immolation or SkillType.GaleWard or SkillType.Shadowstep => RigidClip.Nova,
        SkillType.StoneGuardian or SkillType.Wolf or SkillType.Wisp or SkillType.RaiseThrall or SkillType.FireHydra or SkillType.MirrorImage
            or SkillType.Meteor or SkillType.Timezone or SkillType.Teleport => RigidClip.Summon,
        SkillType.Multishot or SkillType.PlagueArrow => RigidClip.Bow,
        _ => RigidClip.Idle,
    };
}

public struct RigidAnimationBlob
{
    public int BoneCount;
    public BlobArray<RigidClipBlob> Clips;  // Indexed by RigidClip; Frames is 0 for clips the model doesn't have

    public bool Has(RigidClip clip) => (int)clip < Clips.Length && Clips[(int)clip].Frames > 0;
}

public struct RigidClipBlob
{
    public float Length;
    public float SampleRate;
    public bool Loop;
    public int Frames;
    public BlobArray<float3> Positions;
    public BlobArray<quaternion> Rotations;
    public BlobArray<float> Scales;
}

/// <summary>Plays RigidAnimationBlob clips on the RigidBone entities. RigidAnimationSystem picks the clip from gameplay state.</summary>
public struct RigidAnimator : IComponentData
{
    public BlobAssetReference<RigidAnimationBlob> Animation;
    public RigidClip Clip;
    public float Time;
    public RigidClip FromClip;      // Faded out over CrossFade seconds
    public float FromTime;
    public float Blend;             // 0 = FromClip, 1 = Clip
    public bool Started;
    // Last frame's values, to notice movement, attacks and hits
    public float3 LastPosition;
    public int LastAttackCount;
    public float LastFlashTimer;
    // A player's NetPlayerActions counters
    public byte LastCasts;
}

/// <summary>The animated bones, in RigidAnimationSet.Bones order.</summary>
public struct RigidBone : IBufferElementData
{
    public Entity Value;
}

/// <summary>A dead unit playing its Death clip: no longer a target or a body, destroyed when Timer runs out.</summary>
public struct RigidDeath : IComponentData
{
    public float Timer;
}

