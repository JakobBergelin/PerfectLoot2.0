using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// On every unit: damage it soaks before its health (DamagingSystem), from any source (Overflow, future barrier skills).
/// One pool: sources add into it through BarrierUtil.Add, each up to its own cap. When TimeRemaining runs out, whatever is
/// left falls off (BarrierSystem). 0 Value: no barrier.
/// </summary>
public struct Barrier : IComponentData
{
    public float Value;
    public float TimeRemaining;
}

/// <summary>
/// On every unit: hits it dodged or blocked this frame, counted by CalculateDamageSystem and cleared at the start of the
/// next frame's damage (HitsAvoidedResetSystem). For reactions such as Iron Palm.
/// </summary>
public struct HitsAvoided : IComponentData
{
    public int Count;
}

public static class BarrierUtil
{
    /// <summary>Adds amount, but not past cap (a source only tops the barrier up to its own cap), and restarts the timer.</summary>
    public static void Add(ref Barrier barrier, float amount, float cap, float duration)
    {
        if (amount <= 0f) return;
        barrier.Value = math.max(barrier.Value, math.min(barrier.Value + amount, cap));
        barrier.TimeRemaining = math.max(barrier.TimeRemaining, duration);
    }

    /// <summary>Takes as much of damage as the barrier holds off it; returns what gets through.</summary>
    public static float Absorb(ref Barrier barrier, float damage)
    {
        float soaked = math.min(barrier.Value, damage);
        barrier.Value -= soaked;
        return damage - soaked;
    }
}
