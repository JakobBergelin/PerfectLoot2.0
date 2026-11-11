using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Shield Wall while held (and not prevented): the caster turns toward the cursor and is Raised, which slows it
/// (ShieldWallSlowSystem) and blocks projectiles (ShieldWallBlockSystem). Raising it is a use.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct ShieldWallSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<MovementConfig>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (shield, transform, desiredDirection, modifiers, skills, entity) in
                 SystemAPI.Query<RefRW<ShieldWall>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<MovementModifiers>, DynamicBuffer<Skill>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            bool raised = SystemAPI.IsComponentEnabled<ShieldWall>(entity) && !modifiers.ValueRO.PreventsActions;
            if (raised && !shield.ValueRO.Raised) SkillUtil.MarkUsed(skills, SkillType.ShieldWall);
            shield.ValueRW.Raised = raised;
            if (raised) SkillUtil.TurnToward(ref transform.ValueRW, desiredDirection.ValueRO, movementConfig, deltaTime);
        }
    }
}

/// <summary>Slows a unit while its Shield Wall is raised (from the previous frame's state).</summary>
[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct ShieldWallSlowSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (shield, stats) in SystemAPI.Query<RefRO<ShieldWall>, RefRW<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            if (shield.ValueRO.Raised) stats.ValueRW.Value.MovementSpeed -= shield.ValueRO.Slow;
    }
}

/// <summary>
/// Raised Shield Walls destroy every enemy projectile inside their cone in front of the caster (Reach scaled by area of
/// effect, at any height) before it can hit, and each one blocked builds ResourcePerBlock. After the projectiles have
/// moved and before attacks hit.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(ProjectileSystem))]
public partial struct ShieldWallBlockSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<ShieldWall>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // The raised shields: the cone at the caster, its reach, and whose it is
        var cones = new NativeList<LocalTransform>(Allocator.Temp);
        var shapes = new NativeList<AttackShape>(Allocator.Temp);
        var reaches = new NativeList<float>(Allocator.Temp);
        var casters = new NativeList<Entity>(Allocator.Temp);
        foreach (var (shield, transform, stats, entity) in
                 SystemAPI.Query<RefRO<ShieldWall>, RefRO<LocalTransform>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            if (!shield.ValueRO.Raised) continue;
            cones.Add(transform.ValueRO);
            shapes.Add(new AttackShape { Kind = AttackShapeKind.Cone, HalfAngle = shield.ValueRO.HalfAngle });
            reaches.Add(shield.ValueRO.Reach * math.max(0f, stats.ValueRO.Value.AreaMultiplier));
            casters.Add(entity);
        }
        if (cones.IsEmpty) return;

        var blocked = new NativeList<Entity>(Allocator.Temp);
        var blocks = new NativeArray<int>(cones.Length, Allocator.Temp);
        foreach (var (target, transform, entity) in
                 SystemAPI.Query<RefRO<AttackTarget>, RefRO<LocalTransform>>().WithAll<Projectile, Attack>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            if (target.ValueRO.TargetType != TargetType.Player) continue;
            for (int i = 0; i < cones.Length; i++)
            {
                if (!AttackShapes.Contains(cones[i], reaches[i], true, shapes[i], transform.ValueRO.Position)) continue;
                blocked.Add(entity);
                blocks[i]++;
                break;
            }
        }
        if (blocked.IsEmpty) return;

        for (int i = 0; i < casters.Length; i++)
        {
            if (blocks[i] == 0 || !SystemAPI.HasBuffer<ResourceChanger>(casters[i])) continue;
            float resource = blocks[i] * SystemAPI.GetComponent<ShieldWall>(casters[i]).ResourcePerBlock;
            SystemAPI.GetBuffer<ResourceChanger>(casters[i]).Add(new ResourceChanger { Value = resource });
        }

        // Gone at once: without its Attack it can't hit this frame
        state.EntityManager.RemoveComponent<Attack>(blocked.AsArray());
        state.EntityManager.AddComponent<DestroyWithChildren>(blocked.AsArray());
    }
}
