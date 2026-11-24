using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Leap: a telegraphed jump onto the spot its target stood on when the cast began, hitting everything within Radius on landing.</summary>
public struct EnemyLeap : IComponentData
{
    public float Damage;
    public DamageType Type;
    public float Radius;        // Its Circle telegraph's size, so the hit matches the marker
    public float HitForce;
    public float AirTime;       // Seconds in the air, at the end of the telegraph
    public float Height;

    public bool Airborne;       // In the air: it moves itself, not UnitMoverSystem
    public float3 Start;        // Where it took off
}

/// <summary>
/// Leap: while its cast has AirTime or less to go the unit flies in an arc from where it took off to the cast's spot
/// (kept at its own height, so UnitMoverSystem settles it on the floor after), and lands as the marker fills: a hit on
/// everything within Radius there. A cancelled cast (frozen, say) drops it where it is.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateAfter(typeof(EnemySkillCastSystem))]
[BurstCompile]
public partial struct EnemyLeapSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var companions = SystemAPI.GetComponentLookup<Companion>(true);

        foreach (var (leapRef, cast, slots, fires, transform, entity) in
                 SystemAPI.Query<RefRW<EnemyLeap>, RefRO<EnemySkillCast>, DynamicBuffer<EnemySkillSlot>, DynamicBuffer<EnemySkillFire>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            ref var leap = ref leapRef.ValueRW;
            bool landed = false;
            foreach (var fire in fires)
            {
                if (fire.Type != EnemySkillType.Leap) continue;
                landed = true;
                var start = leap.Airborne ? leap.Start : transform.ValueRO.Position;
                var landing = new float3(fire.Position.x, start.y, fire.Position.z);
                transform.ValueRW.Position = landing;
                leap.Airborne = false;

                bool isCompanion = companions.HasComponent(entity);
                EnemySkillSide.Of(entity, isCompanion, isCompanion ? companions[entity].Owner : Entity.Null, out var source, out var targetType);
                var hitTransform = new LocalTransform { Position = fire.Position, Rotation = quaternion.identity, Scale = 1f };
                EnemySkillSide.SpawnHit(commandBuffer, entity, source, targetType, isCompanion, hitTransform,
                    new Attack { Origin = new DamageOrigin { Unit = entity }, Damage = leap.Damage, Type = leap.Type, Radius = leap.Radius, DamageHierarchy = DamageHierarchy.Main, HitForce = leap.HitForce });
            }
            if (landed) continue;

            var c = cast.ValueRO;
            bool leaping = c.Active && c.Slot < slots.Length && slots[c.Slot].Type == EnemySkillType.Leap && c.TimeRemaining <= leap.AirTime && leap.AirTime > 0f;
            if (leaping)
            {
                if (!leap.Airborne)
                {
                    leap.Airborne = true;
                    leap.Start = transform.ValueRO.Position;
                }
                float t = math.saturate(1f - c.TimeRemaining / leap.AirTime);
                var end = new float3(c.Position.x, leap.Start.y, c.Position.z);
                transform.ValueRW.Position = math.lerp(leap.Start, end, t) + new float3(0f, leap.Height * 4f * t * (1f - t), 0f);
            }
            else if (leap.Airborne)
            {
                // Cancelled mid-air: back down at its take-off height, where it is
                leap.Airborne = false;
                var p = transform.ValueRO.Position;
                transform.ValueRW.Position = new float3(p.x, leap.Start.y, p.z);
            }
        }
    }
}
