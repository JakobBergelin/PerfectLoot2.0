using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Shatter legendary power, added by ShatterPower while the item is equipped.</summary>
public struct Shatter : IComponentData
{
    public int ShardCount;
    public float SpreadAngle;       // Degrees between neighboring shards
    public float Damage;            // Cold, before the wearer's damage %
    public float ProjectileSpeed;
    public float Radius;            // Each shard's hit radius, scaled by the wearer's area of effect
    public float Range;             // Meters each shard flies at most
}

/// <summary>
/// Shatter: when a frozen enemy dies, whatever killed it, ShardCount cold shards fly out from it in a fan, the way its
/// killing blow went (a main hit that frame, LastHit), or a random way if it died to damage over time. The shards are
/// the wearer's normal projectiles (damage %, conversion, crits, pierce, bounce and projectile speed apply), so a shard
/// that kills another frozen enemy shatters it too. After the damage is dealt and before CheckDeathSystem removes the
/// dead, while they are still frozen.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(DamagingSystem))]
[UpdateBefore(typeof(CheckDeathSystem))]
[BurstCompile]
public partial struct ShatterSystem : ISystem
{
    uint _frame;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<Shatter>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var random = Unity.Mathematics.Random.CreateFromIndex(_frame++);
        double now = SystemAPI.Time.ElapsedTime;
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        var wearers = new NativeList<Entity>(Allocator.Temp);
        foreach (var (_, entity) in SystemAPI.Query<RefRO<Shatter>>().WithEntityAccess())
            wearers.Add(entity);

        foreach (var (health, lastHit, transform) in SystemAPI.Query<RefRO<Health>, RefRO<LastHit>, RefRO<LocalTransform>>()
                     .WithAll<Enemy, Frozen>().WithNone<DestroyWithChildren>())
        {
            if (health.ValueRO.CurrentValue > 0f) continue;

            // The killing blow's way if it was a hit this frame, otherwise random
            var direction = lastHit.ValueRO.Time == now ? lastHit.ValueRO.Direction : float3.zero;
            direction.y = 0f;
            if (math.lengthsq(direction) < 1e-6f)
            {
                var flat = random.NextFloat2Direction();
                direction = new float3(flat.x, 0f, flat.y);
            }
            direction = math.normalize(direction);

            foreach (var wearer in wearers)
            {
                var shatter = SystemAPI.GetComponent<Shatter>(wearer);
                int count = math.max(1, shatter.ShardCount);
                float firstAngle = -0.5f * (count - 1) * shatter.SpreadAngle;
                for (int i = 0; i < count; i++)
                {
                    var shardDirection = math.rotate(quaternion.RotateY(math.radians(firstAngle + i * shatter.SpreadAngle)), direction);
                    IceShard.Spawn(commandBuffer, wearer, shatter.Damage, shatter.ProjectileSpeed, shatter.Radius, shatter.Range, transform.ValueRO.Position + shardDirection * 0.5f, shardDirection, DamageLabel.Shatter);
                }
            }
        }
    }
}
