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

/// <summary>Applying effects: writes the component and enables it. Works with ComponentLookups from any system.</summary>
public static class EffectApply
{
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
