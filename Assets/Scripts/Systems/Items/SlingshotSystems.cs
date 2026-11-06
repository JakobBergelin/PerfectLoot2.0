using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Slingshot legendary power, added by SlingshotPower while the item is equipped.</summary>
public struct Slingshot : IComponentData
{
    public float SpreadAngle;   // Degrees between the two shots
    public float AllyRadius;    // Meters from an ally's center, added to the shot's hit radius
    public float MinFlight;     // Meters a shot flies before it can split, so a companion doesn't split its own shots
}

/// <summary>A projectile that has split (Slingshot), and its copy. Neither splits again.</summary>
public struct SplitProjectile : IComponentData { }

/// <summary>
/// Slingshot: the wearer's single-hit projectiles, its companions' included, split in two when they pass through another
/// player or a companion. The copy keeps the shot's damage, range and the targets it already hit, and its visual is
/// added by VfxSystem. Shots that deal damage over time don't split, and Captain's Shield moves itself. After projectiles
/// have moved, so the copy can hit this frame.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(ProjectileSystem))]
[BurstCompile]
public partial struct SlingshotSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Slingshot>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var slingshots = SystemAPI.GetComponentLookup<Slingshot>(true);
        var toSplit = new NativeList<Entity>(Allocator.Temp);

        foreach (var (projectile, attack, source, transform, entity) in SystemAPI.Query<RefRO<Projectile>, RefRO<Attack>, RefRO<Source>, RefRO<LocalTransform>>()
                     .WithNone<SplitProjectile, ShieldProjectile, InitializeFromSource>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            var shooter = source.ValueRO.Entity;
            if (!slingshots.HasComponent(shooter)) continue;

            var slingshot = slingshots[shooter];
            var position = transform.ValueRO.Position;
            if (math.distance(position.xz, projectile.ValueRO.StartPosition.xz) < slingshot.MinFlight) continue;
            if (IsInsideAlly(ref state, shooter, position, slingshot.AllyRadius + attack.ValueRO.Radius)) toSplit.Add(entity);
        }

        var entityManager = state.EntityManager;
        foreach (var entity in toSplit)
        {
            float halfSpread = math.radians(0.5f * entityManager.GetComponentData<Slingshot>(entityManager.GetComponentData<Source>(entity).Entity).SpreadAngle);
            entityManager.AddComponent<SplitProjectile>(entity);
            var copy = entityManager.Instantiate(entity);

            var projectile = entityManager.GetComponentData<Projectile>(entity);
            var direction = projectile.Direction;
            projectile.Direction = math.rotate(quaternion.RotateY(halfSpread), direction);
            entityManager.SetComponentData(entity, projectile);
            projectile.Direction = math.rotate(quaternion.RotateY(-halfSpread), direction);
            entityManager.SetComponentData(copy, projectile);
        }
    }

    // The shooter doesn't count: its shots pass through it on the way back
    bool IsInsideAlly(ref SystemState state, Entity shooter, float3 position, float radius)
    {
        foreach (var (transform, ally) in SystemAPI.Query<RefRO<LocalTransform>>().WithAny<Player, Companion>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            if (ally != shooter && math.distance(transform.ValueRO.Position.xz, position.xz) <= radius) return true;
        }
        return false;
    }
}
