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
    /// cast, a punch, a Whirlwind tick. Melee and Mobility skills also raise MeleeAttackUsed / MobilitySkillUsed from it.
    /// </summary>
    public static void MarkUsed(DynamicBuffer<Skill> buffer, SkillType type, bool autoCast = false)
    {
        int index = -1;
        if (!FindSkill(buffer, type, ref index)) return;
        ref var skill = ref buffer.ElementAt(index);
        skill.events.OnUsed = true;
        skill.events.AutoCast |= autoCast;
    }

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
    public float Elapsed;

    public bool Dashing;

    public float3 NextPosition;
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
