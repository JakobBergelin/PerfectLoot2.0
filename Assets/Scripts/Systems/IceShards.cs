using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Ice shards: small cold projectiles that break on their first hit (Shatter, Frozen Orb). The source's damage %,
/// conversion, area, crits, pierce, bounce and projectile speed apply, as for any projectile. Each flies at most range meters.
/// </summary>
public static class IceShard
{
    public static void Spawn(EntityCommandBuffer commandBuffer, Entity source, float damage, float speed, float radius, float range, float3 position, float3 direction, DamageLabel label)
    {
        var newEntity = commandBuffer.CreateEntity();

        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = position, Rotation = quaternion.identity, Scale = 0.5f});
        commandBuffer.AddComponent(newEntity, new Projectile{Direction = direction, Speed = speed, DestroyOnHit = true, BounceCount = 0, PierceCount = 0, FollowGround = true});
        commandBuffer.AddComponent(newEntity, new ProjectileRange{MaxDistance = range, LastPosition = position});
        commandBuffer.AddComponent(newEntity, new Attack{Origin = label, Damage = damage, Type = DamageType.Cold, DamageHierarchy = DamageHierarchy.Main, Radius = radius});
        commandBuffer.AddComponent(newEntity, new Source{Entity = source});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
    }
}
