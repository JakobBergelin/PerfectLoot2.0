using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// What a hit or heal shows as in the Damage Meter's skill view. None: goes by who did it (enemies and companions), Other:
/// an attack of a player nobody labeled. The HUD spaces the name out ("FrozenOrb" shows as "Frozen Orb").
/// </summary>
public enum DamageLabel
{
    None,
    Other,

    // Skills
    BallLightning,
    Barrage,
    CaptainsShield,
    ColdSnap,
    Fireball,
    FistsOfThunder,
    Flamethrower,
    FrozenOrb,
    HookshotSlash,
    PoisonDart,
    PoisonExplosion,
    SonicBall,
    TeslaCoil,
    Tornado,
    Whirlwind,

    // Status effects
    Bleed,
    Burn,
    Charged,
    Poison,

    // Legendary powers and passives
    BorrowedTime,
    ColdBlooded,
    CounterStrike,
    Earthshaker,
    Huskar,
    Overkill,
    Shatter,
    StaticField,
    Steam,
    Stormcall,
    ToxicTrail,

    // Healing
    LifeLink,
    LifeOnHit,
    Luminosity,
    Regeneration,
    Vigil,

    // Class passives
    Consecration,   // Guardian's release: its damage and its heal
    GuardianShare,  // Damage a Guardian takes for an ally
    IronPalm,       // Iron Palm's healing projectiles

    // Skills added later (kept at the end so the values above stay put)
    PlagueArrow,
    Swipe,
    Upheaval,
}

public static class SkillDamageLabel
{
    /// <summary>The label a skill's hits carry (its own attacks; a skill's extra parts, like Poison Dart's explosion, have their own). None: the skill deals no damage of its own.</summary>
    public static DamageLabel Of(SkillType type) => type switch
    {
        SkillType.BallLightning => DamageLabel.BallLightning,
        SkillType.Barrage => DamageLabel.Barrage,
        SkillType.CaptainsShield => DamageLabel.CaptainsShield,
        SkillType.ColdSnap => DamageLabel.ColdSnap,
        SkillType.Fireball => DamageLabel.Fireball,
        SkillType.FistOfThunder => DamageLabel.FistsOfThunder,
        SkillType.Flamethrower => DamageLabel.Flamethrower,
        SkillType.FrozenOrb => DamageLabel.FrozenOrb,
        SkillType.HookshotSlash => DamageLabel.HookshotSlash,
        SkillType.PoisonDart => DamageLabel.PoisonDart,
        SkillType.SonicBall => DamageLabel.SonicBall,
        SkillType.TeslaCoil => DamageLabel.TeslaCoil,
        SkillType.Tornado => DamageLabel.Tornado,
        SkillType.Whirlwind => DamageLabel.Whirlwind,
        SkillType.PlagueArrow => DamageLabel.PlagueArrow,
        SkillType.Swipe => DamageLabel.Swipe,
        SkillType.Upheaval => DamageLabel.Upheaval,
        _ => DamageLabel.None,
    };
}

/// <summary>What the Damage Meter credits a hit to, on attacks and damage instances.</summary>
public struct DamageOrigin
{
    public DamageLabel Label;
    public Entity Unit;     // The companion that made the attack, as its Source is its owner. Entity.Null: its Source

    public static implicit operator DamageOrigin(DamageLabel label) => new DamageOrigin { Label = label };
}

public enum MeterStat
{
    DamageDone,
    DamageReceived,
    HealingDone,
    HealingReceived,
}

/// <summary>A row's name. All empty: unknown, like a shot from an enemy that has died.</summary>
public struct MeterName : IEquatable<MeterName>
{
    public DamageLabel Label;
    public UnityObjectRef<ScriptableObject> Unit;   // An enemy's or companion's definition
    public int Player;                              // 1 for the first player, 0: not a player

    public bool Equals(MeterName other) => Label == other.Label && Player == other.Player && Unit.Equals(other.Unit);
    public override int GetHashCode() => ((int)Label * 397 ^ Player) * 397 ^ Unit.GetHashCode();
}

public struct MeterKey : IEquatable<MeterKey>
{
    public MeterStat Stat;
    public bool BySource;   // A row of the skill view, otherwise of the unit view
    public MeterName Name;

    public bool Equals(MeterKey other) => Stat == other.Stat && BySource == other.BySource && Name.Equals(other.Name);
    public override int GetHashCode() => (Name.GetHashCode() * 397 ^ (int)Stat) * 2 + (BySource ? 1 : 0);
}

/// <summary>One row's amounts: everything since the level started, and one entry per second of the last Window seconds.</summary>
public struct MeterRow
{
    public const int Window = 10;

    public float Total;
    FixedList64Bytes<float> _seconds;   // By second % Window
    int _lastSecond;                    // The newest second in _seconds

    public static MeterRow Start(int second)
    {
        var row = new MeterRow { _lastSecond = second };
        row._seconds.Length = Window;
        return row;
    }

    public void Add(int second, float amount)
    {
        for (int s = math.max(_lastSecond + 1, second - Window + 1); s <= second; s++)
            _seconds[s % Window] = 0f;
        _lastSecond = math.max(_lastSecond, second);

        _seconds[second % Window] += amount;
        Total += amount;
    }

    /// <summary>The amount of the last Window seconds, seen from second.</summary>
    public float Recent(int second)
    {
        float sum = 0f;
        for (int s = math.max(second, _lastSecond) - Window + 1; s <= _lastSecond; s++)
            if (s >= 0) sum += _seconds[s % Window];
        return sum;
    }
}

/// <summary>
/// Singleton: everything the Damage Meter shows, for players and companions. Each level has a new world, so it starts empty
/// with every level. Only used on the main thread.
/// </summary>
public struct DamageMeter : IComponentData
{
    public NativeHashMap<MeterKey, MeterRow> Rows;
    public double StartTime;    // Negative until the first update
    public double Now;

    /// <summary>Seconds since the level started.</summary>
    public float Elapsed => (float)(Now - StartTime);
    public int Second => (int)Elapsed;

    /// <summary>Seconds Recent covers: Window, less the part of the current second still to come and any time before the level started.</summary>
    public float RecentSeconds => math.min(Elapsed, MeterRow.Window - 1 + math.frac(Elapsed));
}
