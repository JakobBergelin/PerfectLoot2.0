using Unity.Entities;

// Buffs and debuffs. Each effect is its own enableable component, present (disabled) on every unit from spawn:
// enabled = the unit has the effect. The component holds the effect's state; its systems hold the logic.
// Apply effects with EffectApply (or the effect's own Apply helper). To add an effect: a component here, its logic,
// a TickTimedEffectJob line in TimedEffectSystem if it is timed, a line in EffectDisplaySystem and an EffectDefinition.

/// <summary>Every buff and debuff, for display (EffectDisplay, EffectDefinition).</summary>
public enum EffectType
{
    None,
    BrokenPromises,
    Refraction,
    Invulnerable,
    Bleeding,
    Burning,
    Poisoned,
    Chilled,
    Frozen,
    DeathsDoor,
    Shadowstep,
    CounterStrike,
    Bloodrush,
    Prism,
    Anchor,
    Momentum,
    Ascetic,
    Afterglow,
    PackInstinct,
    LifeLink,
    BoneWard,
    FesteringHorde,
}

public enum EffectKind
{
    Buff,
    Debuff,
}

/// <summary>An effect that runs out: TimedEffectSystem counts it down and disables it at 0.</summary>
public interface ITimedEffect
{
    float TimeRemaining { get; set; }
    float Duration { get; set; }
}

// ---------- Buffs ----------

/// <summary>Broken Promises legendary power's buff: extra critical chance.</summary>
public struct BrokenPromisesBuff : IComponentData, IEnableableComponent, ITimedEffect
{
    public float TimeRemaining;
    public float Duration;
    public float CriticalChanceBonus;
    float ITimedEffect.TimeRemaining { get => TimeRemaining; set => TimeRemaining = value; }
    float ITimedEffect.Duration { get => Duration; set => Duration = value; }
}

/// <summary>Death's Door legendary power's buff: extra critical chance while the wearer is at low health. Untimed: DeathsDoorSystem turns it on and off.</summary>
public struct DeathsDoorBuff : IComponentData, IEnableableComponent
{
    public float CriticalChanceBonus;
}

/// <summary>
/// Counter Strike legendary power's hits taken so far, one stack each, for the HUD. Untimed: CounterStrikeSystem sets
/// the stacks and turns it off when they reset.
/// </summary>
public struct CounterStrikeCharge : IComponentData, IEnableableComponent
{
    public int Stacks;
}

/// <summary>Each stack absorbs one hit. Untimed: lasts until the stacks are used up.</summary>
public struct Refraction : IComponentData, IEnableableComponent
{
    public int Stacks;
}

/// <summary>Takes no damage.</summary>
public struct Invulnerable : IComponentData, IEnableableComponent, ITimedEffect
{
    public float TimeRemaining;
    public float Duration;
    float ITimedEffect.TimeRemaining { get => TimeRemaining; set => TimeRemaining = value; }
    float ITimedEffect.Duration { get => Duration; set => Duration = value; }
}

/// <summary>
/// Shadowstep skill's buff: extra attack speed, and using a melee skill first jumps in front of the enemy nearest the
/// cursor when it is out of reach (ShadowstepJumpSystem).
/// </summary>
public struct ShadowstepBuff : IComponentData, IEnableableComponent, ITimedEffect
{
    public float TimeRemaining;
    public float Duration;
    public float AttackSpeedBonus;
    public float Range;     // Meters from the unit to the enemy it jumps to
    float ITimedEffect.TimeRemaining { get => TimeRemaining; set => TimeRemaining = value; }
    float ITimedEffect.Duration { get => Duration; set => Duration = value; }
}

/// <summary>Bloodrush passive's buff: attack speed per stack. All stacks fall off together when it runs out.</summary>
public struct BloodrushBuff : IComponentData, IEnableableComponent, ITimedEffect
{
    public int Stacks;
    public float AttackSpeedPerStack;
    public float TimeRemaining;
    public float Duration;
    float ITimedEffect.TimeRemaining { get => TimeRemaining; set => TimeRemaining = value; }
    float ITimedEffect.Duration { get => Duration; set => Duration = value; }
}

/// <summary>Prism passive's buff: extra damage.</summary>
public struct PrismBuff : IComponentData, IEnableableComponent, ITimedEffect
{
    public float DamageBonus;
    public float TimeRemaining;
    public float Duration;
    float ITimedEffect.TimeRemaining { get => TimeRemaining; set => TimeRemaining = value; }
    float ITimedEffect.Duration { get => Duration; set => Duration = value; }
}

/// <summary>Anchor passive's buff: armor per stack. Untimed: AnchorSystem adds stacks and turns it off when the unit moves.</summary>
public struct AnchorBuff : IComponentData, IEnableableComponent
{
    public int Stacks;
    public int ArmorPerStack;
}

/// <summary>Momentum legendary power's buff: damage per stack. Untimed: MomentumSystem adds stacks and turns it off when the unit stops.</summary>
public struct MomentumBuff : IComponentData, IEnableableComponent
{
    public int Stacks;
    public float DamagePerStack;
}

/// <summary>Ascetic legendary power's buff: damage, negative after a skill with a cost. Untimed: AsceticSystem sets it from the last skill used.</summary>
public struct AsceticBuff : IComponentData, IEnableableComponent
{
    public float DamageBonus;
}

/// <summary>Afterglow passive's buff: extra damage.</summary>
public struct AfterglowBuff : IComponentData, IEnableableComponent, ITimedEffect
{
    public float DamageBonus;
    public float TimeRemaining;
    public float Duration;
    float ITimedEffect.TimeRemaining { get => TimeRemaining; set => TimeRemaining = value; }
    float ITimedEffect.Duration { get => Duration; set => Duration = value; }
}

/// <summary>Pack Instinct passive's buff: extra critical chance. Untimed: PackInstinctSystem turns it on near a wearer.</summary>
public struct PackInstinctBuff : IComponentData, IEnableableComponent
{
    public float CriticalChanceBonus;
}

/// <summary>Linked by a Life Link wearer. Untimed: LifeLinkSystem turns it on for the linked unit.</summary>
public struct LifeLinkBuff : IComponentData, IEnableableComponent
{
    public float HealthRegeneration;    // Per second, from all wearers linked to it
}

/// <summary>What Bone Ward and Festering Horde wearers nearby add up to. Untimed: ResistanceAuraSystem works it out every frame.</summary>
public struct ResistanceAuraBuff : IComponentData
{
    public float ColdResistance;
    public float PoisonResistance;
    public int ColdStacks;      // Bone Ward wearers
    public int PoisonStacks;    // Festering Horde wearers
}

// ---------- Debuffs ----------

/// <summary>Enabled while the unit has BleedInstances (one per physical hit, each with its own timer).</summary>
public struct Bleeding : IComponentData, IEnableableComponent {}

public struct BleedInstance : IBufferElementData
{
    public float TimeRemaining;
    public float Duration;
    public Entity Source;
}

/// <summary>Enabled while the unit has BurnInstances (one per fire hit, each with its own timer).</summary>
public struct Burning : IComponentData, IEnableableComponent {}

public struct BurnInstance : IBufferElementData
{
    public float DamagePerSecond;
    public float TimeRemaining;
    public float Duration;
    public Entity Source;
}

public struct Poisoned : IComponentData, IEnableableComponent, ITimedEffect
{
    public float TimeRemaining;
    public float Duration;
    public float DamagePerSecond;
    public Entity Source;
    float ITimedEffect.TimeRemaining { get => TimeRemaining; set => TimeRemaining = value; }
    float ITimedEffect.Duration { get => Duration; set => Duration = value; }
}

/// <summary>Poison Dart's mark: if the unit dies with it, it explodes (PoisonMarkExplosionSystem). Another hit starts it over; it doesn't stack.</summary>
public struct PoisonMark : IComponentData, IEnableableComponent, ITimedEffect
{
    public float TimeRemaining;
    public float Duration;
    public float ExplosionDamage;
    public float ExplosionRadius;
    public Entity Source;
    float ITimedEffect.TimeRemaining { get => TimeRemaining; set => TimeRemaining = value; }
    float ITimedEffect.Duration { get => Duration; set => Duration = value; }
}

/// <summary>Builds up from cold damage and slows; at the max stacks the unit freezes (ChillSystem).</summary>
public struct Chilled : IComponentData, IEnableableComponent, ITimedEffect
{
    public int Stacks;
    public float TimeRemaining;
    public float Duration;
    float ITimedEffect.TimeRemaining { get => TimeRemaining; set => TimeRemaining = value; }
    float ITimedEffect.Duration { get => Duration; set => Duration = value; }
}

/// <summary>Can't move, turn or use skills.</summary>
public struct Frozen : IComponentData, IEnableableComponent, ITimedEffect
{
    public float TimeRemaining;
    public float Duration;
    float ITimedEffect.TimeRemaining { get => TimeRemaining; set => TimeRemaining = value; }
    float ITimedEffect.Duration { get => Duration; set => Duration = value; }
}

/// <summary>
/// Static Field legendary power's debuff: lower lightning resistance per stack (ConductiveStatsSystem). All stacks
/// fall off together when it runs out. Enemies only, so not shown on the HUD.
/// </summary>
public struct Conductive : IComponentData, IEnableableComponent, ITimedEffect
{
    public int Stacks;
    public float ResistancePerStack;
    public float TimeRemaining;
    public float Duration;
    float ITimedEffect.TimeRemaining { get => TimeRemaining; set => TimeRemaining = value; }
    float ITimedEffect.Duration { get => Duration; set => Duration = value; }
}

/// <summary>
/// Sunder legendary power's debuff: each stack adds to the next hits of a Sunder wearer (SunderSystem). All stacks fall
/// off together when it runs out. Enemies only, so not shown on the HUD.
/// </summary>
public struct Sundered : IComponentData, IEnableableComponent, ITimedEffect
{
    public int Stacks;
    public float TimeRemaining;
    public float Duration;
    float ITimedEffect.TimeRemaining { get => TimeRemaining; set => TimeRemaining = value; }
    float ITimedEffect.Duration { get => Duration; set => Duration = value; }
}

// ---------- Shared ----------

/// <summary>On every unit: how its effects limit movement this frame. Written by EffectMovementSystem.</summary>
public struct MovementModifiers : IComponentData
{
    public float SpeedMultiplier;   // 1 = normal; slows lower it, clamped to 0..1
    public bool PreventsActions;    // Frozen and similar: no moving, turning or skills
}

/// <summary>On every unit: damage-over-time dealt since the last combat text number, per kind.</summary>
public struct EffectDamageText : IComponentData
{
    public float Bleed;
    public float Burn;
    public float Poison;
    public float Timer;
}

/// <summary>Only on the player: its active effects, rebuilt every frame by EffectDisplaySystem for the HUD.</summary>
public struct EffectDisplay : IBufferElementData
{
    public EffectType Type;
    public int Stacks;
    public float TimeRemaining;
    public float Duration;      // 0 = untimed
    public float Magnitude;     // Effect-specific number shown in the description, e.g. damage per second or slow
}

/// <summary>On the player: the active effects of its InputsData.Hovered unit, for the unit panel. Units have no list of their own.</summary>
public struct HoveredEffect : IBufferElementData
{
    public EffectDisplay Value;
}

/// <summary>Applying effects: writes the component and enables it. Works with ComponentLookups from any system.</summary>
public static class EffectApply
{
    /// <summary>
    /// A buff's duration on a unit with these stats: longer or shorter by its BuffDuration (0.25 = +25%). At 0 or less
    /// the buff isn't applied.
    /// </summary>
    public static float BuffDuration(float duration, in StatBlock stats) => duration * (1f + stats.BuffDuration);

    /// <summary>
    /// Starts a timed effect, or restarts its timer if already active. Returns it so the caller can set its other
    /// fields; those keep their old values while it was already active. Check HasComponent first.
    /// </summary>
    public static RefRW<T> Refresh<T>(ComponentLookup<T> lookup, Entity entity, float duration)
        where T : unmanaged, IComponentData, IEnableableComponent, ITimedEffect
    {
        var effect = lookup.IsComponentEnabled(entity) ? lookup[entity] : default;
        effect.TimeRemaining = duration;
        effect.Duration = duration;
        lookup[entity] = effect;
        lookup.SetComponentEnabled(entity, true);
        return lookup.GetRefRW(entity);
    }

    /// <summary>Adds chill stacks (up to maxStacks) and restarts its timer.</summary>
    public static void AddChill(ComponentLookup<Chilled> lookup, Entity entity, int stacks, int maxStacks, float duration)
    {
        if (!lookup.HasComponent(entity)) return;
        int current = lookup.IsComponentEnabled(entity) ? lookup[entity].Stacks : 0;
        Refresh(lookup, entity, duration).ValueRW.Stacks = Unity.Mathematics.math.min(maxStacks, current + stacks);
    }

    /// <summary>Adds Refraction stacks, up to maxStacks.</summary>
    public static void AddRefraction(ComponentLookup<Refraction> lookup, Entity entity, int stacks, int maxStacks)
    {
        if (!lookup.HasComponent(entity)) return;
        int current = lookup.IsComponentEnabled(entity) ? lookup[entity].Stacks : 0;
        lookup[entity] = new Refraction { Stacks = Unity.Mathematics.math.min(maxStacks, current + stacks) };
        lookup.SetComponentEnabled(entity, true);
    }

    public static void AddBleed(BufferLookup<BleedInstance> bleeds, ComponentLookup<Bleeding> bleeding, Entity entity, float duration, Entity source)
    {
        if (!bleeds.HasBuffer(entity) || !bleeding.HasComponent(entity)) return;
        bleeds[entity].Add(new BleedInstance { TimeRemaining = duration, Duration = duration, Source = source });
        bleeding.SetComponentEnabled(entity, true);
    }

    public static void AddBurn(BufferLookup<BurnInstance> burns, ComponentLookup<Burning> burning, Entity entity, float damagePerSecond, float duration, Entity source)
    {
        if (!burns.HasBuffer(entity) || !burning.HasComponent(entity)) return;
        burns[entity].Add(new BurnInstance { DamagePerSecond = damagePerSecond, TimeRemaining = duration, Duration = duration, Source = source });
        burning.SetComponentEnabled(entity, true);
    }
}
