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

}

/// <summary>
/// What kind of skill it is. Effects pick skills by tag (e.g. Echoing Blade casts a random Projectile skill) and
/// SkillTagEventSystem turns uses of Melee / Mobility skills into MeleeAttackUsed / MobilitySkillUsed. Stored in assets:
/// add new values at the end.
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
}

/// <summary>
/// A request on the caster to cast a skill now, made by an effect (e.g. Echoing Blade). The skill's own system handles
/// it at the start of the next frame's skills: no wind-up, the normal resource cost (skipped when it can't be paid),
/// counted as a use with AutoCast. Requests nobody handled are cleared (SkillCastRequestCleanupSystem).
/// </summary>
public struct SkillCastRequest : IBufferElementData
{
    public SkillType Type;
    public float3 Direction;
    public bool AutoCast;
}

public class SkillUtil
{
    /// <summary>
    /// A skill's resource cost (or cost per second) for a caster with these stats: lower by its ResourceCostReduction
    /// (0.25 = 25% cheaper, negative: dearer), never below 0. Use it for both the check and the payment.
    /// </summary>
    public static float Cost(float cost, in StatBlock stats) => cost * math.max(0f, 1f - stats.ResourceCostReduction);

    /// <summary>
    /// Whether a one-off cost (from Cost) can be paid: enough resource, or a cost up to the caster's AlwaysAffordableCost
    /// (Bottomless), which empties what it has. Not for costs per second.
    /// </summary>
    public static bool CanAfford(float resource, float cost, in CalculatedStats stats) => resource >= cost || cost <= stats.AlwaysAffordableCost;

    /// <summary>
    /// A skill's cooldown for a caster with these stats: shorter by its CooldownReduction (0.25 = 25% shorter, negative:
    /// longer), never below 0. For skills with a real cooldown (Dash, Teleport, Invulnerability, Ball Lightning); skills
    /// that repeat an attack (Fireball, Tornado, Barrage, Fists of Thunder, Whirlwind) are timed by AttackSpeed instead.
    /// </summary>
    public static float Cooldown(float cooldown, in StatBlock stats) => cooldown * math.max(0f, 1f - stats.CooldownReduction);

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

public struct FistsOfThunderHit
{
    public float Cooldown;  // Time before this punch can land, scaled by attack speed
    public float Damage;    // Flat damage, plus WeaponDamage * WeaponDamageMultiplier
}

public struct FistsOfThunder : IComponentData, IEnableableComponent
{
    public FixedList128Bytes<FistsOfThunderHit> Hits;  // The combo, up to 15 punches
    public float ResourceGeneration;    // Per punch
    public float WeaponDamageMultiplier;
    public float Radius;
    public float HitForce;

    public float TimeSinceLastPunch;
    public int PunchCount;
}

public struct Barrage : IComponentData, IEnableableComponent
{
    public float AttackSpeed;
    public float Damage;
    public float ProjectileSpeed;
    public float Radius;
    public float HomingTurnSpeed;
    public float HomingTurnAcceleration;
    public float Elapsed;
}

public struct FireBall : IComponentData, IEnableableComponent
{
    public float AttackSpeed;
    public float ResourceCost;
    public float Damage;
    public float ProjectileSpeed;
    public float Radius;
    public float HitForce;
    public float Elapsed;
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
public struct Invulnerability : IComponentData, IEnableableComponent
{
    public float Cooldown;
    public float Duration;
    public float ResourceCost;
    public float Elapsed;       // Since the last cast, up to Cooldown
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
/// Throws a homing shield (ShieldProjectile) while held, as long as none of the caster's shields is out: no wind-up or
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
/// Flies the unit to the clicked spot, lifted over the ground and Invulnerable, draining resource; no other skill,
/// moving or turning meanwhile. Landing releases a lightning attack that grows with the resource spent.
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
    public float3 Destination;
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
    public float Elapsed;
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
