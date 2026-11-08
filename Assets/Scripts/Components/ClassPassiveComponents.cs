using Unity.Entities;
using Unity.Mathematics;

// Class passives: one per class, added with the class (CharacterClass.ClassPassive), never picked in a passive slot.

/// <summary>Precision (Fencer): a frame with a critical hit takes CooldownCut seconds off every skill's cooldown (PrecisionSystem).</summary>
public struct Precision : IComponentData
{
    public float CooldownCut;
}

/// <summary>Exploit (Savant): hits deal DamagePerStatus more for each different status effect on the target (ExploitSystem).</summary>
public struct Exploit : IComponentData
{
    public float DamagePerStatus;
}

/// <summary>
/// Unstoppable (Warrior): while at least EnemyCount enemies are within Radius, the wearer can't be chilled (so not slowed
/// or frozen by chill) or knocked back (UnstoppableSystem). Active is worked out every frame.
/// </summary>
public struct Unstoppable : IComponentData
{
    public float Radius;
    public int EnemyCount;
    public bool Active;
}

/// <summary>
/// Vanish (Rogue): after a Mobility skill, enemies lose track of the wearer for Duration seconds, except those it hits
/// meanwhile (VanishSystem). Since: when the current vanish started (elapsed time).
/// </summary>
public struct Vanish : IComponentData
{
    public float Duration;
    public float TimeRemaining;
    public double Since;
}

/// <summary>Brace (Fighter): a melee attack gives BlockBonus block chance for Duration seconds (BraceSystem).</summary>
public struct Brace : IComponentData
{
    public float BlockBonus;
    public float Duration;
    public float TimeRemaining;
}

/// <summary>
/// Recall (Mage): using every equipped skill within Window seconds restores ResourceFraction of the resource and finishes
/// the shortest cooldown that's running (RecallSystem). LastUsed: when each skill slot was last used by the player.
/// </summary>
public struct Recall : IComponentData
{
    public float Window;
    public float ResourceFraction;
    public double4 LastUsed;
}

/// <summary>
/// Desperation (Caster): below StartHealth (a fraction of max health) cooldowns tick faster, up to MaxSpeedUp (0.25 = 25%
/// faster) at FullHealth and below (DesperationStatsSystem).
/// </summary>
public struct Desperation : IComponentData
{
    public float StartHealth;
    public float FullHealth;
    public float MaxSpeedUp;
}

/// <summary>
/// Overflow (Cleric): healing the wearer does past full health becomes a Barrier on the one healed, up to MaxShare of
/// their max health, lasting Duration seconds from the last top-up (ApplyHealingSystem).
/// </summary>
public struct Overflow : IComponentData
{
    public float MaxShare;
    public float Duration;
}

/// <summary>
/// Guardian (Paladin): allies within Radius pass Share of the damage they take to the wearer, which gains ArmorPerAlly
/// for each ally in range. Everything the wearer takes fills Charge; at ConsecrationThreshold it releases Consecration
/// around it: a heal of ConsecrationHealShare of its max health for allies and ConsecrationDamage to enemies, within
/// ConsecrationRadius (GuardianSystem).
/// </summary>
public struct Guardian : IComponentData
{
    public float Radius;
    public float Share;
    public int ArmorPerAlly;
    public float ConsecrationThreshold;
    public float ConsecrationRadius;
    public float ConsecrationDamage;
    public float ConsecrationHealShare;
    public float Charge;
}

/// <summary>
/// Iron Palm (Monk): every hit the wearer dodges or blocks stores a Ki; at KiNeeded it sends a healing projectile to
/// itself and each ally within Range, healing HealShare of their max health (IronPalmSystem).
/// </summary>
public struct IronPalm : IComponentData
{
    public int KiNeeded;
    public float HealShare;
    public float Range;
    public float ProjectileSpeed;
    public int Ki;
}

/// <summary>
/// A projectile that heals Target by HealShare of its max health when it gets there, then goes away; also goes away if the
/// target is gone (HealOnArrivalSystem). Iron Palm's healing orbs. Homes with HomingProjectile; has no Attack, so it hits nothing.
/// </summary>
public struct HealOnArrival : IComponentData
{
    public Entity Target;
    public float HealShare;
    public DamageLabel Label;
    public float ArmedIn;   // Seconds before it can land, so it is seen flying (and the wearer's own makes its detour)
}
