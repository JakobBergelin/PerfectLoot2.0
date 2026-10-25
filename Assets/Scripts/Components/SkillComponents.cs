using Unity.Collections;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>An equipped active skill. Written by ActiveSkillManager, state filled in each frame for UI.</summary>
public struct Skill : IBufferElementData
{
    public SkillType Type;
    public int SlotIndex;   // Which slot/key (Skill1..Skill4) triggers it

    public SkillState State;

    public struct Triggers
    {
        public bool OnUsed;
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

public class SkillUtil
{
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
    public Entity WeaponEntity;
    public float Elapsed;
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
/// One-frame event on every unit: enabled by a mobility skill (Dash, ...) when it is used, read by reactions such as
/// Refractionist at the end of SkillSystemGroup, and disabled again by MobilitySkillEventSystem at the start of the next one.
/// </summary>
public struct MobilitySkillUsed : IComponentData, IEnableableComponent {}
