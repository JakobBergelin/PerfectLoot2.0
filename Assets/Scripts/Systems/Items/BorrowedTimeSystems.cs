using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>Borrowed Time legendary power, added by BorrowedTimePower while the item is equipped.</summary>
public struct BorrowedTime : IComponentData
{
    public float DelayedShare;  // Part of all damage taken that is delayed (0.8 = 80%)
    public float Duration;      // Seconds the delayed damage is spread over
}

/// <summary>Damage held back by Borrowed Time, one entry per frame that took damage. DelayedDamageSystem deals it.</summary>
public struct DelayedDamage : IBufferElementData
{
    public float DamagePerSecond;
    public float TimeRemaining;
    public float Duration;
}

/// <summary>
/// Borrowed Time: takes DelayedShare off everything the wearer takes this frame and spreads it over the next Duration
/// seconds. After everything that changes damage taken, so only what's left of a hit is delayed. The hit itself still
/// lands; its status effects use the part that wasn't delayed.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(MedusaSystem))]
[UpdateAfter(typeof(BrawlerSystem))]
[UpdateAfter(typeof(BrittleSystem))]
[UpdateAfter(typeof(LongShotSystem))]
[UpdateAfter(typeof(BulwarkSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct BorrowedTimeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<BorrowedTime>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (borrowedTime, taken, delayed) in SystemAPI.Query<RefRO<BorrowedTime>, DynamicBuffer<CalculatedDamageInstance>, DynamicBuffer<DelayedDamage>>())
        {
            float total = 0f;
            for (int i = 0; i < taken.Length; i++)
            {
                ref var instance = ref taken.ElementAt(i);
                if (instance.Damage <= 0f) continue;

                float held = instance.Damage * borrowedTime.ValueRO.DelayedShare;
                instance.Damage -= held;
                total += held;
            }

            float duration = borrowedTime.ValueRO.Duration;
            if (total > 0f) delayed.Add(new DelayedDamage{DamagePerSecond = total / duration, TimeRemaining = duration, Duration = duration});
        }
    }
}

/// <summary>
/// Deals delayed damage straight to health: it has already been through armor and the like. Doesn't need the power, so
/// taking the item off doesn't clear what's owed. After DamagingSystem and before Last Stand and CheckDeathSystem.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(DamagingSystem))]
[BurstCompile]
public partial struct DelayedDamageSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (health, delayed) in SystemAPI.Query<RefRW<Health>, DynamicBuffer<DelayedDamage>>())
        {
            for (int i = delayed.Length - 1; i >= 0; i--)
            {
                ref var entry = ref delayed.ElementAt(i);
                float time = math.min(deltaTime, entry.TimeRemaining);
                health.ValueRW.CurrentValue -= entry.DamagePerSecond * time;
                entry.TimeRemaining -= time;
                if (entry.TimeRemaining <= 0f) delayed.RemoveAt(i);
            }
        }
    }
}
