using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>Hopping passive: moves for HopTime out of every Interval seconds.</summary>
public struct Hopping : IComponentData
{
    public float Interval;
    public float HopTime;
    public float Elapsed;
}

/// <summary>
/// Hopping units stand still between hops and move Interval / HopTime times faster during them, so their average speed
/// stays their movement speed. Each starts at its own point of the cycle. After EffectMovementSystem sets the slows.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(EffectSystemGroup))]
[UpdateAfter(typeof(EffectMovementSystem))]
public partial struct HoppingSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Hopping>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (hopping, modifiers, entity) in SystemAPI.Query<RefRW<Hopping>, RefRW<MovementModifiers>>().WithEntityAccess())
        {
            ref var h = ref hopping.ValueRW;
            if (h.Interval <= 0f || h.HopTime <= 0f) continue;
            if (h.Elapsed == 0f) h.Elapsed = math.frac(entity.Index * 0.618f) * h.Interval;
            h.Elapsed = (h.Elapsed + deltaTime) % h.Interval + 1e-5f;
            bool inHop = h.Elapsed < h.HopTime;
            modifiers.ValueRW.SpeedMultiplier *= inHop ? h.Interval / h.HopTime : 0f;
        }
    }
}
