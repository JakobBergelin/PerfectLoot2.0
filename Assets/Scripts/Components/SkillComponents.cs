using Unity.Collections;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>An equipped active skill. Written by ActiveSkillManager, state filled in each frame for UI.</summary>
public struct Skill : IBufferElementData
{
    public SkillType Type;
    public int SlotIndex;   // Which slot/key (Skill1..Skill4) triggers it

    public SkillTags BaseTags;  // From the ActiveSkill asset
    public SkillTags Tags;      // This frame's tags: BaseTags (reset by SkillActivationSystem) plus whatever effects add

    public SkillState State;

    public float CooldownReduction; // Seconds to take off the remaining cooldown, used up by the skill's system (SkillUtil.ApplyCooldownReduction)

    public DamageLabel Label;       // What its hits carry (SkillDamageLabel)
    public float CritShake;         // Camera shake when one of its hits crits, 0 to 1 (CritShakeSystem)
    public int3 Requirements;       // Strength, Agility and Intelligence it needs (ActiveSkill.Requirements)

    public struct Triggers
    {
        public bool OnUsed;
        public bool AutoCast;   // OnUsed came from an effect (e.g. Echoing Blade), not the player pressing the skill
        public SkillTags UsedAs;    // Which of the skill's tags this frame's uses raise events for (see MarkUsed)
    }

    public Triggers events;
}

/// <summary>Per-frame skill state, reported for UI (mirrored to ActiveSkillSlot).</summary>
public struct SkillState
{
    public bool IsPressed;              // Set by SkillActivationSystem
    public bool IsActive;               // Firing/channeling; defaults to "enabled", skills may override
    public float CooldownRemaining;     // Only skills with a real cooldown (e.g. Dash) report this
    public float CooldownDuration;
    public float ResourceCost;          // Static, from the ActiveSkill asset
    public float ResourceCostPerSecond;
    public bool HasEnoughResource;      // Set by SkillActivationSystem
    public bool MeetsRequirements;      // The unit's attributes cover Skill.Requirements (SkillActivationSystem)
    public bool Usable;                 // MeetsRequirements, or anywhere but a dungeon: else it can't be used (SkillActivationSystem)
    public int Variant;                 // A skill's own mode, kept by its slot through deaths and scenes (Stances: 0 Strength, 1 Agility, 2 Intelligence)
}

public enum SkillType
{
    FistOfThunder,
    Barrage,
    Fireball,
    Dash,
    Whirlwind,
    Tornado,
    Teleport,
    Invulnerability,
    CaptainsShield,
    BallLightning,
    Flamethrower,
    SonicBall,
    HookshotSlash,
    Shadowstep,
    FrozenOrb,
    ColdSnap,
    Timezone,
    TeslaCoil,
    PoisonDart,
    FireHydra,
    StoneGuardian,
    Wolf,
    Wisp,
    RaiseThrall,
    MirrorImage,
    PlagueArrow,
    Swipe,
    Upheaval,
    PhantomEdge,
    Meteor,
    Immolation,
    CursedHammer,
    FrostLance,
    Stances,
    BloodRite,
}

/// <summary>
/// What kind of skill it is. Effects pick skills by tag (e.g. Echoing Blade casts a random Projectile skill) and
/// SkillTagEventSystem turns uses of Melee / Mobility skills into MeleeAttackUsed / MobilitySkillUsed. Element tags are the
/// asset's own element: an elemental conversion doesn't change them. Stored in assets: add new values at the end.
/// </summary>
[System.Flags]
public enum SkillTags
{
    None = 0,
    Projectile = 1 << 0,
    Melee = 1 << 1,
    Mobility = 1 << 2,
    Channeled = 1 << 3,
    Invulnerable = 1 << 4,
    Cooldown = 1 << 5,
    Elemental = 1 << 6,     // Deals non-Physical damage; goes with Fire, Cold, Poison or Lightning
    Fire = 1 << 7,
    Cold = 1 << 8,
    Poison = 1 << 9,
    Lightning = 1 << 10,
    Buff = 1 << 11,
    Defensive = 1 << 12,
    Generator = 1 << 13,    // Gives resource or another bonus when used
    Companion = 1 << 14,
    Barrier = 1 << 15,
    Periodic = 1 << 16,     // Damages over time through a PeriodicAttack
    Area = 1 << 17,         // Hits everything in an area (a field, a burst or a moving zone) rather than one target
    Toggle = 1 << 18,       // Its key switches it on or off; it stays on until pressed again
}

/// <summary>
/// A request on the caster to cast a skill now, made by an effect (e.g. Echoing Blade). The skill's own system handles
/// it at the start of the next frame's skills: no attack recovery, the normal resource cost (skipped when it can't be paid)
/// unless Free, counted as a use with AutoCast. Cooldown skills cast without waiting for or restarting their cooldown.
/// Requests nobody handled are cleared (SkillCastRequestCleanupSystem).
/// </summary>
public struct SkillCastRequest : IBufferElementData
{
    public SkillType Type;
    public float3 Direction;
    public bool AutoCast;
    public bool Free;       // Costs no resource (e.g. Cascade)
}

public class SkillUtil
{
    /// <summary>
    /// A skill's resource cost (or cost per second) for a caster with these stats: lower by its ResourceCostReduction
    /// (0.25 = 25% cheaper, negative: dearer), never below 0. Use it for both the check and the payment.
    /// </summary>
    public static float Cost(float cost, in StatBlock stats) => cost * math.max(0f, 1f - stats.ResourceCostReduction);

    /// <summary>Cost for a Channeled skill: also changed by the caster's ChannelCostChange (Crossfire).</summary>
    public static float ChannelCost(float cost, in StatBlock stats) => Cost(cost, stats) * math.max(0f, 1f + stats.ChannelCostChange);

    /// <summary>Cost or ChannelCost, going by the skill's tags.</summary>
    public static float Cost(float cost, in StatBlock stats, SkillTags tags) =>
        (tags & SkillTags.Channeled) != 0 ? ChannelCost(cost, stats) : Cost(cost, stats);

    /// <summary>
    /// Whether a one-off cost (from Cost) can be paid: enough resource, or a cost up to the caster's AlwaysAffordableCost
    /// (Bottomless), which empties what it has. Not for costs per second.
    /// </summary>
    public static bool CanAfford(float resource, float cost, in CalculatedStats stats) => resource >= cost || cost <= stats.AlwaysAffordableCost;

    /// <summary>
    /// A skill's cooldown for a caster with these stats: shorter by its CooldownReduction (0.25 = 25% shorter, negative:
    /// longer), never below 0. For skills with a real cooldown (Dash, Teleport, Invulnerability, Ball Lightning); skills
    /// that repeat an attack (Fireball, Tornado, Fists of Thunder, Whirlwind) are timed by AttackSpeed instead.
    /// </summary>
    public static float Cooldown(float cooldown, in StatBlock stats) => cooldown * math.max(0f, 1f - stats.CooldownReduction);

    /// <summary>
    /// The damage of a skill with a real cooldown, raised by the caster's CooldownDamage (Patience) for each second of
    /// its base cooldown, the one before CooldownReduction. Not shown in tooltips, like the Damage % stat.
    /// </summary>
    public static float CooldownDamage(float damage, float cooldown, in CalculatedStats stats) => damage * (1f + cooldown * stats.CooldownDamage);

    /// <summary>Takes the seconds put on the skill (e.g. by Recharge) off its remaining cooldown: elapsed is its time since the last use.</summary>
    public static void ApplyCooldownReduction(ref Skill skill, ref float elapsed)
    {
        elapsed += skill.CooldownReduction;
        skill.CooldownReduction = 0f;
    }

    /// <summary>
    /// Turns the caster toward the cursor at the player turn speed and says whether it now faces within AttackFacingAngle:
    /// a skill with a clear direction goes off only then, so attacking behind you takes a turn first. Teleport and
    /// Flamethrower (its own tighter aim) don't use it.
    /// </summary>
    public static bool TurnToward(ref Unity.Transforms.LocalTransform transform, in DesiredDirection desired, in MovementConfig config, float deltaTime)
    {
        transform.Rotation = UnityEngine.Quaternion.RotateTowards(transform.Rotation, desired.Quaternion, config.RotationSpeed * deltaTime);
        return UnityEngine.Quaternion.Angle(transform.Rotation, desired.Quaternion) <= config.AttackFacingAngle;
    }

    /// <summary>Removes and returns the first request for this skill, if there is one.</summary>
    public static bool TryTakeRequest(DynamicBuffer<SkillCastRequest> requests, SkillType type, out SkillCastRequest request)
    {
        for (int i = 0; i < requests.Length; i++)
        {
            if (requests[i].Type != type) continue;
            request = requests[i];
            requests.RemoveAt(i);
            return true;
        }
        request = default;
        return false;
    }

    /// <summary>
    /// Raises the skill's OnUsed event this frame (autoCast: cast by an effect, not the player). Every use counts: a
    /// cast, a punch, a Whirlwind tick. Melee and Mobility skills also raise MeleeAttackUsed / MobilitySkillUsed from it;
    /// usedAs limits that to some of its tags (e.g. Hookshot Slash: its launch is a Mobility use, each hit a Melee use).
    /// </summary>
    public static void MarkUsed(DynamicBuffer<Skill> buffer, SkillType type, bool autoCast = false, SkillTags usedAs = AllTags)
    {
        int index = -1;
        if (!FindSkill(buffer, type, ref index)) return;
        ref var skill = ref buffer.ElementAt(index);
        skill.events.OnUsed = true;
        skill.events.AutoCast |= autoCast;
        skill.events.UsedAs |= usedAs;
    }

    public const SkillTags AllTags = (SkillTags)~0;

    public static bool FindSkill(DynamicBuffer<Skill> buffer,SkillType type, ref int index)
    {
        for(int i = 0; i < buffer.Length; i++)
        {
            if(buffer[i].Type == type)
            {
                index = i;
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// A caster's recovery from its last attack, shared by the skills that repeat an attack (Fireball, Tornado, Frozen Orb,
/// Fists of Thunder): one fires as soon as this is down to 0 and adds its own time between attacks, so
/// switching skills never skips a wait. Counted down by attack speed (AttackRecoverySystem).
/// </summary>
public struct AttackRecovery : IComponentData
{
    public float Remaining;
}

public struct FistsOfThunderHit
{
    public float Cooldown;  // Recovery after this punch, scaled by attack speed
    public float Damage;    // Flat damage, plus WeaponDamage * WeaponDamageMultiplier
}

public struct FistsOfThunder : IComponentData, IEnableableComponent
{
    public FixedList128Bytes<FistsOfThunderHit> Hits;  // The combo, up to 15 punches
    public float ResourceGeneration;    // Per punch
    public float WeaponDamageMultiplier;
    public float Radius;
    public float HitForce;

    public int PunchCount;
}

/// <summary>
/// Barrage, a toggle: BarrageSystem reads its key, so the component stays disabled and Active says whether it's on.
/// While equipped, every ResourcePerMissile resource the caster spends launches a homing missile up into a cone. On:
/// missiles go for the strongest enemy; off: the weakest.
/// </summary>
public struct Barrage : IComponentData, IEnableableComponent
{
    public float ResourcePerMissile;
    public float ConeAngle;         // Radians from straight up a missile can leave at
    public float Damage;
    public float ProjectileSpeed;
    public float Radius;
    public float HomingTurnSpeed;
    public float HomingTurnAcceleration;
    public bool Active;
    public float Spent;             // Toward the next missile
}

public struct FireBall : IComponentData, IEnableableComponent
{
    public float AttackSpeed;
    public float ResourceCost;
    public float Damage;
    public float ProjectileSpeed;
    public float Radius;
    public float HitForce;
}

public struct Dash : IComponentData, IEnableableComponent
{
    public float Cooldown;
    public float CooldownPerAgility;    // The cooldown is divided by 1 + Agility × this (DashSystem.CooldownFor)
    public float Elapsed;

    public bool Dashing;

    public float3 NextPosition;
}

public struct Teleport : IComponentData, IEnableableComponent
{
    public float Cooldown;
    public float Range;
    public float CastTime;      // Seconds standing still between pressing it and landing
    public float ResourceCost;
    public float Elapsed;       // Since the last cast, up to Cooldown

    public bool Casting;
    public float CastElapsed;
    public float3 Target;
}

/// <summary>Grants the Invulnerable buff for Duration seconds.</summary>
/// <summary>A toggle: RaiseThrallSystem reads its key, so the component stays disabled and Active says whether it's on.</summary>
public struct RaiseThrall : IComponentData, IEnableableComponent
{
    public float CriticalChancePenalty;
    public float MarkDuration;
    public int MaxThralls;
    public float Threat;
    public float SightRadius;
    public BoidSettings Movement;
    public bool Active;
}

public struct Invulnerability : IComponentData, IEnableableComponent
{
    public float Cooldown;
    public float Duration;
    public float ResourceCost;
    public float Elapsed;       // Since the last cast, up to Cooldown
}

/// <summary>Cold Snap: a burst around the caster that deals cold damage and chills enemies heavily.</summary>
public struct ColdSnap : IComponentData, IEnableableComponent
{
    public float Cooldown;
    public float Damage;
    public float Radius;
    public int ChillStacks;     // Left on an enemy by one cast, the hit's own chill included
    public float Elapsed;       // Since the last cast, up to Cooldown
}

/// <summary>Timezone: leaves a TimezoneField where the caster stands.</summary>
public struct Timezone : IComponentData, IEnableableComponent
{
    public float Cooldown;
    public float Duration;
    public float Radius;
    public int ChillPerTick;    // Per TimezoneFieldSystem.TickInterval
    public float ProjectileSlow;
    public float Elapsed;       // Since the last cast, up to Cooldown
}

/// <summary>A Timezone field on the ground: chills enemies and slows all projectiles inside it (TimezoneFieldSystem).</summary>
public struct TimezoneField : IComponentData
{
    public float Radius;            // The caster's area of effect already applied
    public float TimeRemaining;
    public int ChillPerTick;
    public float ProjectileSlow;    // 0.8 = 80% slower
}

/// <summary>
/// Tesla Coil: channeled while held, with the unit locked in place. A TeslaCoilField stands around it, and the drain
/// rises for every second of the channel. The cooldown starts when the channel ends.
/// </summary>
public struct TeslaCoil : IComponentData, IEnableableComponent
{
    public float Cooldown;
    public float ResourceCostPerSecond;     // At the start of the channel
    public float ResourceCostIncrease;      // Added to it for every second of the channel
    public float Radius;
    public float DamagePerSecond;
    public float TickRate;
    public float Elapsed;       // Since the channel ended, up to Cooldown
    public float ChannelTime;
    public bool Channeling;
}

/// <summary>Marks the PeriodicAttack around a channeling Tesla Coil (its Source): it also blocks enemy projectiles (TeslaCoilFieldSystem).</summary>
public struct TeslaCoilField : IComponentData {}

public struct PoisonDart : IComponentData, IEnableableComponent
{
    public float AttackSpeed;
    public float WeaponDamageMultiplier;
    public float ProjectileSpeed;
    public float Radius;
    public float HitForce;
    public float MarkDuration;
    public float ExplosionDamage;       // Poison, before the caster's damage %
    public float ExplosionRadius;       // Scaled by the caster's area of effect
}

/// <summary>On a Poison Dart's Attack: PoisonMarkSystem puts a PoisonMark on everything it hits.</summary>
public struct PoisonDartHit : IComponentData
{
    public float MarkDuration;
    public float ExplosionDamage;
    public float ExplosionRadius;
}

public struct FireHydra : IComponentData, IEnableableComponent
{
    public float Cooldown;
    public float ResourceCost;
    public float Range;
    public float Duration;      // Seconds a Hydra stays
    public float Elapsed;       // Since the last cast, up to Cooldown
    public Entity Prefab;       // The Hydra to copy, made by FireHydraSkill
}

/// <summary>On a placed Hydra, which also has Companion. Goes away when TimeRemaining runs out (FireHydraSystem).</summary>
public struct FireHydraUnit : IComponentData
{
    public float TimeRemaining;
}

public struct MirrorImage : IComponentData, IEnableableComponent
{
    public float Cooldown;
    public float ResourceCost;
    public float Duration;      // Seconds a copy stays
    public float SpawnDistance; // Meters in front of the caster
    public UnityEngine.Color Tint;
    public float Elapsed;       // Since the last cast, up to Cooldown
    public Entity Prefab;       // The copy to summon, with the caster's attacks (MirrorImageSkill.SetUpCopy)
}

/// <summary>On a copy made by Mirror Image, which also has Companion. Goes away when TimeRemaining runs out (MirrorImageInputSystem).</summary>
public struct MirrorImageUnit : IComponentData
{
    public float TimeRemaining;
    public UnityEngine.Color Tint;
    public bool Ending;         // Its keys are let go, it's removed next frame
    public bool Tinted;         // MirrorImageTintSystem is done with it
}

/// <summary>
/// One equipped CompanionSkill on its caster: summons a companion in front of them. One at a time: the cooldown only runs
/// once it's gone (CompanionSummonSystem).
/// </summary>
public struct CompanionSummon : IBufferElementData
{
    public SkillType Type;
    public float Cooldown;
    public float ResourceCost;
    public float SpawnDistance; // Meters in front of the caster
    public float Scale;
    public float Elapsed;       // Since the last companion went, up to Cooldown
    public Entity Prefab;       // The companion to copy, made by CompanionSkill
    public bool Held;           // Its key is down (SkillActivationSystem)
    public bool HasUnit;        // Its companion is alive (CompanionSummonSystem)

    /// <summary>The index of the skill in summons, -1 when it isn't there.</summary>
    public static int Find(in DynamicBuffer<CompanionSummon> summons, SkillType type)
    {
        for (int i = 0; i < summons.Length; i++)
            if (summons[i].Type == type) return i;
        return -1;
    }
}

/// <summary>On a summoned companion, with the skill that summoned it.</summary>
public struct SummonedCompanion : IComponentData
{
    public SkillType Type;
}

/// <summary>Grants the Shadowstep buff for Duration seconds: extra attack speed, and melee skills jump to the target.</summary>
public struct Shadowstep : IComponentData, IEnableableComponent
{
    public float Cooldown;
    public float Duration;
    public float AttackSpeedBonus;  // Added to AttackSpeed while the buff lasts, 0.15 = +15%
    public float Range;             // Meters; the furthest enemy a melee skill jumps to
    public float Elapsed;           // Since the last cast, up to Cooldown
}

/// <summary>
/// Throws a homing shield (ShieldProjectile) while held, as long as none of the caster's shields is out: no attack recovery or
/// cooldown, so a faster shield means more throws. Damage is scaled by the caster's BlockChance when thrown.
/// </summary>
public struct CaptainsShield : IComponentData, IEnableableComponent
{
    public float ResourceCost;
    public float Damage;
    public float ProjectileSpeed;
    public float Radius;
    public float HitForce;
    public int Bounces;             // Targets after the first, before the Bounces stat
    public float TurnSpeed;         // Radians per second, at the start and after each new target
    public float TurnAcceleration;  // Radians per second, per second
    public float NoTargetDistance;  // How far it flies toward the cursor when there's no enemy to home on
}

/// <summary>
/// Flies the unit the way it faced at take-off for as long as the skill is held, lifted over the ground and Invulnerable,
/// draining resource; no other skill, moving or turning meanwhile. Landing releases a lightning attack that grows with the
/// resource spent.
/// </summary>
public struct BallLightning : IComponentData, IEnableableComponent
{
    public float Cooldown;              // After landing
    public float ResourceCost;          // To start
    public float ResourceCostPerSecond; // While flying
    public float Speed;                 // Meters per second
    public float Lift;                  // Meters above what's below
    public float Damage;                // Of the landing attack, before the resource spent
    public float SpentToDouble;         // Resource spent that doubles the damage
    public float Radius;
    public float HitForce;

    public float Elapsed;               // Since landing, up to Cooldown
    public bool Flying;
    public float3 Start;
    public float3 Direction;           // Flat, kept from take-off
    public float FloorY;                // Height of the floor it flies over, where how high it can rise is measured from
    public float Spent;                 // Resource spent on this flight, start cost included
}

public struct Whirlwind : IComponentData, IEnableableComponent
{
    public float ResourceCostPerSecond;
    public float WeaponDamageMultiplier;
    public float RotationSpeed;     // Radians per second
    public float AttackRadius;
    public float SpinUpTime;        // Seconds of spinning before the weapon starts hitting
    public float HitForce;
    public float TickInterval;      // Seconds between melee ticks (MeleeAttackUsed) while the weapon is out
    public Entity WeaponEntity;
    public float Elapsed;
    public float TickElapsed;
    public bool Spinning;
}

public struct Tornado : IComponentData, IEnableableComponent
{
    public float AttackSpeed;
    public float ResourceCost;
    public float Radius;
    public float DirectionDuration; // Seconds before a tornado picks a new random direction
    public float Speed;
    public float DamagePerSecond;
    public float WeaponDamagePerSecond;
    public float Lifetime;
    public float TickRate;
    public float HitForce;
}

/// <summary>Frozen Orb: casts a slow orb that damages around it and spins, firing ice shards out of its front and back.</summary>
/// <summary>
/// Swipe: a slow weapon swing in front of the caster, alternating right to left and left to right (SwingCount). Each
/// swing sweeps an arc of ±ArcAngle degrees (SweepingAttack) and gives ResourcePerHit for every enemy it hits. Its
/// reach is twice the caster's weapon reach and it counts the area of effect bonus twice.
/// </summary>
public struct Swipe : IComponentData, IEnableableComponent
{
    public float AttackSpeed;           // Seconds between swings, before the attack speed stat
    public float Damage;
    public float WeaponDamageMultiplier;
    public float Radius;                // Of the blade's hit circle, before area of effect
    public float ArcAngle;              // Degrees to each side of facing
    public float SweepDuration;         // Seconds one swing takes to cross the arc
    public float HitForce;
    public float ResourcePerHit;
    public int SwingCount;              // Even: right to left, odd: left to right; kept when let go, so attacks resume from the other side
}

/// <summary>
/// Phantom Edge: when off cooldown, marks every living enemy within Radius (nearest first) and starts a flurry
/// (PhantomEdgeFlurry): the caster, hidden and invulnerable, blinks to each marked enemy in turn and strikes it once for
/// weapon damage * WeaponDamageMultiplier, StrikeInterval / attack speed seconds apart, then returns to where it cast.
/// The cooldown starts with the flurry, so it can be ready again before the flurry ends; casting then adds new marks.
/// </summary>
public struct PhantomEdge : IComponentData, IEnableableComponent
{
    public float Radius;
    public float StrikeInterval;        // Seconds between strikes at attack speed 1
    public float WeaponDamageMultiplier;
    public float StrikeRadius;          // Of each strike's hit circle, around the target
    public float Cooldown;
    public float Elapsed;               // Since the last cast, up to the cooldown
}

/// <summary>
/// A Phantom Edge flurry in progress on its caster: the marked enemies still to strike (PhantomEdgeMark, in order), when the
/// next strike comes and where to return to. Added with the skill and kept, so an unequipped skill's flurry still finishes.
/// </summary>
public struct PhantomEdgeFlurry : IComponentData
{
    public bool Active;
    public float3 StartPosition;
    public quaternion StartRotation;
    public float NextStrike;            // Attack-speed seconds until the next strike
    public float StrikeInterval;
    public float WeaponDamageMultiplier;
    public float StrikeRadius;
}

/// <summary>An enemy marked by Phantom Edge, waiting for its strike.</summary>
public struct PhantomEdgeMark : IBufferElementData
{
    public Entity Enemy;
}

/// <summary>A render part Phantom Edge hid on its caster (it gets DisableRendering back off when the flurry ends).</summary>
public struct HiddenByFlurry : IComponentData
{
}

/// <summary>
/// Meteor: calls a meteor down on the cursor (at most Range away, clamped to it). Impact seconds later it hits everything
/// within Radius for Damage fire, then leaves burning ground (a Hazard) for HazardDuration. Repeats while held.
/// </summary>
public struct Meteor : IComponentData, IEnableableComponent
{
    public float AttackSpeed;           // Seconds between casts, before the attack speed stat
    public float ResourceCost;
    public float Range;
    public float Delay;                 // Seconds from the cast to the impact
    public float Damage;
    public float Radius;
    public float HazardDamagePerSecond;
    public float HazardRadius;
    public float HazardDuration;
}

/// <summary>
/// A falling meteor (with a LocalTransform where it lands and a Source): MeteorStrikeSystem counts Delay down, then it
/// hits there and leaves its burning ground. The source's damage %, conversion and area of effect apply at the impact.
/// </summary>
public struct MeteorStrike : IComponentData
{
    public float Delay;                 // Seconds left until the impact
    public float FallTime;              // The whole delay, for the warning circle
    public float Damage;
    public float Radius;
    public float HazardDamagePerSecond;
    public float HazardRadius;
    public float HazardDuration;
}

/// <summary>
/// Immolation, a toggle: ImmolationSystem reads its key, so the component stays disabled and Active says whether it's on.
/// While on it drains resource and burns enemies around the caster; without enough resource it stays on but pauses.
/// </summary>
public struct Immolation : IComponentData, IEnableableComponent
{
    public float DamagePerSecond;           // Fire, before the Intelligence part and the damage %
    public float DamagePerIntelligence;     // Added to DamagePerSecond for each point of the caster's Intelligence
    public float Radius;                    // Scaled by area of effect
    public float TickRate;                  // Seconds between hits
    public float ResourceCostPerSecond;
    public bool Active;
    public float Elapsed;                   // Seconds toward the next hit, only counted while it's burning
}

/// <summary>
/// Cursed Hammer: throws a tumbling hammer toward the cursor that starts slow and keeps speeding up. It deals no damage
/// itself: where it hits an enemy or a wall it bursts, hitting everything within Radius for Damage lightning. Repeats while held.
/// </summary>
public struct CursedHammer : IComponentData, IEnableableComponent
{
    public float AttackSpeed;           // Seconds between casts, before the attack speed stat
    public float ResourceCost;
    public float StartSpeed;            // Scaled by the projectile speed stat
    public float Acceleration;          // Meters per second gained each second
    public float MaxSpeed;
    public float HitRadius;             // How close an enemy has to come to the hammer to be hit
    public float SpinPerMeter;          // Radians it tumbles per meter flown
    public float Damage;
    public float Radius;
}

/// <summary>
/// A flying Cursed Hammer (with a Projectile, LocalTransform and Source). CursedHammerFlightSystem speeds it up, spins it
/// and bursts it on contact. With pierce it bursts on each enemy and flies on; with bounces it bursts on the wall too.
/// </summary>
public struct CursedHammerProjectile : IComponentData
{
    public float Acceleration;
    public float MaxSpeed;
    public float HitRadius;
    public float SpinPerMeter;
    public float Damage;
    public float Radius;
    public float SpinAngle;             // Radians tumbled so far
    public Entity LastHit;              // The enemy it last burst on, so a pierce doesn't hit it again on the next frames
}

/// <summary>
/// Upheaval: slams the ground at weapon reach in front of the caster (Slam*), and QuakeDelay seconds later the ground
/// heaves in a bigger circle around the same spot (Quake*), throwing enemies outward. Both add weapon damage.
/// </summary>
public struct Upheaval : IComponentData, IEnableableComponent
{
    public float AttackSpeed;           // Seconds between slams, before the attack speed stat
    public float ResourceCost;
    public float SlamDamage;
    public float SlamWeaponDamageMultiplier;
    public float SlamRadius;
    public float QuakeDelay;
    public float QuakeDamage;
    public float QuakeWeaponDamageMultiplier;
    public float QuakeRadius;
    public float QuakeHitForce;
}

/// <summary>
/// Plague Arrow: a slow arrow that poisons everything within Radius along its path and always flies Range meters. The
/// caster's projectile speed scales its speed, so a slower arrow spends longer on the way and deals more.
/// </summary>
public struct PlagueArrow : IComponentData, IEnableableComponent
{
    public float AttackSpeed;
    public float ResourceCost;
    public float DamagePerSecond;
    public float TickRate;
    public float Radius;            // Damage radius around the arrow
    public float Speed;             // Before the caster's projectile speed
    public float Range;             // Meters it always flies
}

public struct FrozenOrb : IComponentData, IEnableableComponent
{
    public float AttackSpeed;
    public float ResourceCost;
    public float DamagePerSecond;
    public float TickRate;
    public float Radius;            // Damage radius around the orb
    public float Speed;
    public float Lifetime;
    public float SpinSpeed;         // Degrees per second
    public float ShardInterval;     // Seconds between shard bursts (one shard out of the front, one out of the back)
    public float ShardDamage;
    public float ShardSpeed;
    public float ShardRadius;
    public float ShardRange;        // Meters each shard flies at most
}

/// <summary>On a Frozen Orb in flight: its spin and shard timer (FrozenOrbSpinSystem).</summary>
public struct FrozenOrbProjectile : IComponentData
{
    public float SpinSpeed;         // Degrees per second
    public float SpinAngle;         // Degrees; the front faces this way
    public float ShardInterval;
    public float ShardElapsed;
    public float ShardDamage;
    public float ShardSpeed;
    public float ShardRadius;
    public float ShardRange;
}

/// <summary>
/// One-frame event on every unit: enabled by SkillTagEventSystem when a skill tagged Mobility (Dash, ...) is used, read by
/// reactions such as Refractionist (SkillReactionSystemGroup), and disabled again by MobilitySkillEventSystem at the
/// start of the next frame's skills.
/// </summary>
public struct MobilitySkillUsed : IComponentData, IEnableableComponent {}

/// <summary>
/// One-frame event on every unit, like MobilitySkillUsed: enabled when a skill tagged Melee is used (each Fists of Thunder
/// punch, each Whirlwind tick), read by reactions such as Echoing Blade.
/// </summary>
public struct MeleeAttackUsed : IComponentData, IEnableableComponent {}

/// <summary>
/// Flamethrower: channeled while held. Pays ResourceCost when the spray starts, then drains ResourceCostPerSecond and
/// sprays FlamesPerSecond growing fire flames (GrowingFlame) in a cone toward the cursor. The unit is slowed meanwhile.
/// </summary>
public struct Flamethrower : IComponentData, IEnableableComponent
{
    public float ResourceCost;
    public float ResourceCostPerSecond;
    public float FlamesPerSecond;
    public float Spread;            // Radians each side of the aim a flame can leave at
    public float Speed;
    public float Lifetime;
    public float StartRadius;
    public float EndRadius;
    public float DamagePerSecond;   // Per flame, to every enemy inside it
    public float TickRate;
    public float HitForce;
    public float Slow;              // Movement speed taken off while spraying (0.4 = 40%)
    public float Elapsed;           // Seconds toward the next flame
    public bool Spraying;
}

/// <summary>
/// Sonic Ball: while held the unit rolls the way it faced when it started, steered only by left/right (turning the ball
/// itself), bouncing off walls and water. It passes through enemies, hitting them with a physical attack scaled by its
/// Armor, and gains movement speed, Armor and Fire Resistance meanwhile (SonicBallStatsSystem). No other skill, moving
/// or turning meanwhile. Free; the cooldown starts when the roll ends.
/// </summary>
public struct SonicBall : IComponentData, IEnableableComponent
{
    public float Cooldown;              // After the roll ends
    public float MovementSpeedBonus;    // While rolling (0.3 = +30%)
    public int ArmorBonus;
    public float FireResistanceBonus;
    public float ArmorDamageMultiplier; // Damage per hit = (Armor + ArmorBonus) * this, at the start of the roll
    public float Radius;
    public float HitForce;
    public float TurnSpeed;             // Radians per second left/right turns the ball

    public float Elapsed;               // Since the roll ended, up to Cooldown
    public bool Rolling;
    public float3 Heading;              // Flat, normalized
}

/// <summary>The attack that follows a rolling Sonic Ball (its Source); removed when the roll ends.</summary>
public struct SonicBallAttack : IComponentData {}

public enum HookshotState : byte
{
    Idle,
    Firing,     // The hook flies out; the unit stands still
    Pulling,    // Latched: the unit is pulled to it
}

/// <summary>
/// Hookshot Slash: pressing it fires a hook toward the cursor (up to Range) that latches there or on the first wall, then
/// pulls the unit to it over water and through enemies, slashing each one it passes once for weapon damage. No other
/// skill, moving or turning meanwhile. No cooldown; costs ResourceCost.
/// </summary>
public struct HookshotSlash : IComponentData, IEnableableComponent
{
    public float ResourceCost;
    public float Range;
    public float HookSpeed;             // Meters per second
    public float PullSpeed;             // Meters per second
    public float WeaponDamageMultiplier;
    public float Radius;
    public float HitForce;

    public HookshotState State;
    public float3 Direction;            // Flat, toward the cursor when fired
    public float3 HookPosition;
    public float HookDistance;          // How far the hook flies: to the cursor, at most Range
    public float3 PullEnd;              // Where the pull stops: before walls, on floor
    public int HitsSeen;                // Enemies its attack has hit this pull (each one a Melee use)
}

/// <summary>The attack that travels with a Hookshot Slash pull (its Source); removed when the pull ends.</summary>
public struct HookshotSlashAttack : IComponentData {}

/// <summary>
/// Frost Lance: a thin line of cold thrust from the weapon's reach, Length meters toward the cursor, hitting everything
/// on it for weapon damage * WeaponDamageMultiplier. Repeats while held, AttackSpeed apart; each thrust builds
/// ResourcePerAttack and grants the Frost Lance buff (FrostLanceBuff: less damage from projectiles) for BuffDuration.
/// Area of effect makes the line longer and wider, still starting at the weapon's reach.
/// </summary>
public struct FrostLance : IComponentData, IEnableableComponent
{
    public float AttackSpeed;               // Seconds between thrusts, before the attack speed stat
    public float WeaponDamageMultiplier;
    public float Length;                    // Meters from the weapon's reach outward, before area of effect
    public float Width;                     // Before area of effect
    public float Height;                    // How high the line reaches, to catch units of any size
    public float HitForce;                  // Knockback, sideways out of the line
    public float ResourcePerAttack;
    public float ProjectileDamageReduction; // The buff's: 0.2 = -20%
    public float BuffDuration;              // Seconds, scaled by the buff duration stat
}

/// <summary>
/// Stances: each press (Cooldown apart) moves to the next stance, Strength, Agility, Intelligence and back, kept in the
/// skill's SkillState.Variant. The stance gives Bonus to its attribute (StancesStatsSystem) while the skill is usable.
/// </summary>
public struct Stances : IComponentData, IEnableableComponent
{
    public float Cooldown;
    public float Bonus;
    public float Elapsed;       // Seconds since the last switch
}

/// <summary>
/// Blood Rite: when off cooldown, pays HealthCost of max health (never below 1) and grants the Blood Rite buff
/// (BloodRiteBuff: Strength and Agility) for Duration, scaled by BuffDuration.
/// </summary>
public struct BloodRite : IComponentData, IEnableableComponent
{
    public float Cooldown;
    public float HealthCost;    // Share of max health: 0.2 = 20%
    public float Duration;
    public float StrengthBonus;
    public float AgilityBonus;
    public float Elapsed;       // Seconds since the last use
}
