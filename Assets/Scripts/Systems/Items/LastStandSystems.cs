using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>Last Stand legendary power, added by LastStandPower while the item is equipped.</summary>
public struct LastStand : IComponentData
{
    public float Cooldown;      // Seconds before it can save the wearer again
    public float HealthLeft;    // Health the wearer is left with

    public float Elapsed;       // Up to Cooldown: ready at Cooldown
}

/// <summary>
/// Last Stand: when damage would kill the wearer and the cooldown is over, it stays alive with HealthLeft instead and
/// the cooldown starts. After all of this frame's damage, just before CheckDeathSystem, so every hit that frame is
/// covered by the one save.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateBefore(typeof(CheckDeathSystem))]
[BurstCompile]
public partial struct LastStandSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<LastStand>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (lastStand, health) in SystemAPI.Query<RefRW<LastStand>, RefRW<Health>>())
        {
            ref var l = ref lastStand.ValueRW;
            l.Elapsed = math.min(l.Elapsed + deltaTime, l.Cooldown);
            if (health.ValueRO.CurrentValue > 0f || l.Elapsed < l.Cooldown) continue;

            health.ValueRW.CurrentValue = l.HealthLeft;
            l.Elapsed = 0f;
        }
    }
}
