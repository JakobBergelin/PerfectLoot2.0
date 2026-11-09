using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// Phantom Edge's cast: counts the cooldown and, when it's ready and the skill is held, marks every living enemy within
/// Radius, nearest first, and starts the flurry (or adds the new marks to one that's still going). No enemies in range: no
/// cast and no cooldown. The cast is a Mobility use (Vanish, Refractionist); each strike later is a Melee use.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[BurstCompile]
public partial struct PhantomEdgeSystem : ISystem
{
    struct Candidate { public Entity Enemy; public float Distance; }

    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<PhantomEdge>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (edge, stats) in SystemAPI.Query<RefRW<PhantomEdge>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            edge.ValueRW.Elapsed = math.min(edge.ValueRO.Elapsed + deltaTime, SkillUtil.Cooldown(edge.ValueRO.Cooldown, stats.ValueRO.Value));

        var enemies = SystemAPI.QueryBuilder().WithAll<Enemy, LocalTransform, Health>().WithNone<RigidDeath>().Build();
        NativeArray<Entity> enemyEntities = default;
        NativeArray<LocalTransform> enemyTransforms = default;
        NativeArray<Health> enemyHealths = default;

        foreach (var (edge, flurry, marks, transform, skills, stats) in
                 SystemAPI.Query<RefRW<PhantomEdge>, RefRW<PhantomEdgeFlurry>, DynamicBuffer<PhantomEdgeMark>, RefRO<LocalTransform>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>())
        {
            ref var e = ref edge.ValueRW;
            if (e.Elapsed < SkillUtil.Cooldown(e.Cooldown, stats.ValueRO.Value)) continue;

            if (!enemyEntities.IsCreated)
            {
                enemyEntities = enemies.ToEntityArray(Allocator.Temp);
                enemyTransforms = enemies.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                enemyHealths = enemies.ToComponentDataArray<Health>(Allocator.Temp);
            }
            var origin = transform.ValueRO.Position;
            var found = new NativeList<Candidate>(Allocator.Temp);
            for (int i = 0; i < enemyEntities.Length; i++)
            {
                if (enemyHealths[i].CurrentValue <= 0f) continue;
                float distance = math.distance(enemyTransforms[i].Position.xz, origin.xz);
                if (distance <= e.Radius) found.Add(new Candidate { Enemy = enemyEntities[i], Distance = distance });
            }
            if (found.IsEmpty) continue;
            found.Sort(new ByDistance());

            // Marks join the end of the queue, without enemies already in it
            foreach (var candidate in found)
            {
                bool queued = false;
                foreach (var mark in marks) if (mark.Enemy == candidate.Enemy) { queued = true; break; }
                if (!queued) marks.Add(new PhantomEdgeMark { Enemy = candidate.Enemy });
            }

            ref var f = ref flurry.ValueRW;
            f.StrikeInterval = e.StrikeInterval;
            f.WeaponDamageMultiplier = e.WeaponDamageMultiplier;
            f.StrikeRadius = e.StrikeRadius;
            if (!f.Active)
            {
                f.Active = true;
                f.StartPosition = origin;
                f.StartRotation = transform.ValueRO.Rotation;
                f.NextStrike = 0f;  // The first strike at once
            }
            e.Elapsed = 0f;
            SkillUtil.MarkUsed(skills, SkillType.PhantomEdge, usedAs: SkillTags.Mobility);
        }

        // Report cooldown (after SkillActivationSystem's defaults)
        foreach (var (edge, skills, stats) in SystemAPI.Query<RefRW<PhantomEdge>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (!SkillUtil.FindSkill(skills, SkillType.PhantomEdge, ref index)) continue;
            ref var skill = ref skills.ElementAt(index);
            SkillUtil.ApplyCooldownReduction(ref skill, ref edge.ValueRW.Elapsed);
            float cooldown = SkillUtil.Cooldown(edge.ValueRO.Cooldown, stats.ValueRO.Value);
            skill.State.CooldownDuration = cooldown;
            skill.State.CooldownRemaining = math.max(0f, cooldown - edge.ValueRO.Elapsed);
        }
    }

    struct ByDistance : System.Collections.Generic.IComparer<Candidate>
    {
        public int Compare(Candidate a, Candidate b) => a.Distance.CompareTo(b.Distance);
    }
}

/// <summary>
/// A Phantom Edge flurry: keeps its caster invulnerable and, every StrikeInterval / attack speed, blinks it next to the
/// next marked enemy that's still alive (at weapon reach, on the side it came from, facing it) and strikes it once: a
/// one-frame physical hit of weapon damage * WeaponDamageMultiplier around the enemy. With no marks left, it returns the
/// caster to where it cast and ends. Movement input is ignored meanwhile (CharacterMovementSystem); other skills work.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateAfter(typeof(PhantomEdgeSystem))]
[BurstCompile]
public partial struct PhantomEdgeFlurrySystem : ISystem
{
    const float InvulnerableRefresh = 0.25f;    // Kept topped up every frame while the flurry lasts

    Random _random;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _random = Random.CreateFromIndex((uint)System.Environment.TickCount);
        state.RequireForUpdate<PhantomEdgeFlurry>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var transforms = SystemAPI.GetComponentLookup<LocalTransform>();
        var healths = SystemAPI.GetComponentLookup<Health>(true);
        var dying = SystemAPI.GetComponentLookup<RigidDeath>(true);
        var invulnerable = SystemAPI.GetComponentLookup<Invulnerable>();

        foreach (var (flurry, marks, stats, skills, entity) in
                 SystemAPI.Query<RefRW<PhantomEdgeFlurry>, DynamicBuffer<PhantomEdgeMark>, RefRO<CalculatedStats>, DynamicBuffer<Skill>>().WithEntityAccess())
        {
            ref var f = ref flurry.ValueRW;
            if (!f.Active) continue;

            if (invulnerable.HasComponent(entity))
            {
                invulnerable[entity] = new Invulnerable { TimeRemaining = InvulnerableRefresh, Duration = InvulnerableRefresh };
                invulnerable.SetComponentEnabled(entity, true);
            }

            var statBlock = stats.ValueRO.Value;
            f.NextStrike -= deltaTime * statBlock.AttackSpeed;
            if (f.NextStrike > 0f) continue;

            // The next mark that's still alive
            var target = Entity.Null;
            while (marks.Length > 0 && target == Entity.Null)
            {
                var enemy = marks[0].Enemy;
                marks.RemoveAt(0);
                if (transforms.HasComponent(enemy) && healths.HasComponent(enemy) && healths[enemy].CurrentValue > 0f && !dying.HasComponent(enemy)) target = enemy;
            }

            var self = transforms[entity];
            if (target == Entity.Null)
            {
                // Done: back where it was cast
                self.Position = f.StartPosition;
                self.Rotation = f.StartRotation;
                transforms[entity] = self;
                f.Active = false;
                continue;
            }

            // Blink next to it, on the side it came from, facing it, and strike
            var targetPosition = transforms[target].Position;
            var away = new float3(self.Position.x - targetPosition.x, 0f, self.Position.z - targetPosition.z);
            var side = math.normalizesafe(away, new float3(0f, 0f, -1f));
            self.Position = new float3(targetPosition.x, self.Position.y, targetPosition.z) + side * statBlock.WeaponReach;
            self.Rotation = quaternion.LookRotationSafe(-side, math.up());
            transforms[entity] = self;

            float weaponDamage = statBlock.WeaponDamage + _random.NextFloat(-statBlock.WeaponDamageRange, statBlock.WeaponDamageRange);
            var strike = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(strike, new LocalTransform { Position = targetPosition, Rotation = quaternion.identity, Scale = 1f });
            commandBuffer.AddComponent(strike, new Attack { Origin = DamageLabel.PhantomEdge, Damage = weaponDamage * f.WeaponDamageMultiplier, Type = DamageType.Physical, Radius = f.StrikeRadius, DamageHierarchy = DamageHierarchy.Main });
            commandBuffer.AddComponent(strike, new Source { Entity = entity });
            commandBuffer.AddComponent(strike, new AttackTarget { TargetType = TargetType.Enemy });
            commandBuffer.AddComponent(strike, new InitializeFromSource());
            commandBuffer.AddComponent(strike, new DestroyWithChildren());   // A single-frame hit

            f.NextStrike += f.StrikeInterval;
            SkillUtil.MarkUsed(skills, SkillType.PhantomEdge, usedAs: SkillTags.Melee);
        }
    }
}

/// <summary>
/// Hides a unit during its Phantom Edge flurry: every render part that's showing gets DisableRendering (and HiddenByFlurry,
/// so only those come back) when the flurry starts, and shows again when it ends. Structural changes, so on the main thread.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
public partial class PhantomEdgeVisibilitySystem : SystemBase
{
    protected override void OnCreate() => RequireForUpdate<PhantomEdgeFlurry>();

    readonly System.Collections.Generic.List<Entity> _parts = new System.Collections.Generic.List<Entity>();

    protected override void OnUpdate()
    {
        var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        foreach (var (flurry, entity) in SystemAPI.Query<RefRO<PhantomEdgeFlurry>>().WithEntityAccess())
        {
            // Every part under it, including ones attached after it was made (equipment), which its LinkedEntityGroup misses
            _parts.Clear();
            Collect(entity);
            foreach (var part in _parts)
            {
                if (!EntityManager.HasComponent<MaterialMeshInfo>(part)) continue;
                bool hiddenByUs = EntityManager.HasComponent<HiddenByFlurry>(part);
                if (flurry.ValueRO.Active && !hiddenByUs && !EntityManager.HasComponent<DisableRendering>(part))
                {
                    commandBuffer.AddComponent<DisableRendering>(part);
                    commandBuffer.AddComponent<HiddenByFlurry>(part);
                }
                else if (!flurry.ValueRO.Active && hiddenByUs)
                {
                    commandBuffer.RemoveComponent<DisableRendering>(part);
                    commandBuffer.RemoveComponent<HiddenByFlurry>(part);
                }
            }
        }
        commandBuffer.Playback(EntityManager);
        commandBuffer.Dispose();
    }

    void Collect(Entity entity)
    {
        _parts.Add(entity);
        if (!EntityManager.HasBuffer<Child>(entity)) return;
        using var children = EntityManager.GetBuffer<Child>(entity).ToNativeArray(Allocator.Temp);
        foreach (var child in children) Collect(child.Value);
    }
}
