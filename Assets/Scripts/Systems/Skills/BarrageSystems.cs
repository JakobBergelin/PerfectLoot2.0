using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Barrage: pressing its key switches it on or off, even while another skill is held, and its slot stays lit while it's
/// on. Whether on or off, every ResourcePerMissile resource the caster spent this frame (its negative ResourceChangers,
/// costs per second included) launches a homing missile: on, it goes for the strongest enemy, off, the weakest. Last of
/// the skills, after every cost is in and after Insight, so a cast it makes free doesn't count. Also casts it for
/// effects that ask (Echoing Blade).
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(InsightSystem))]
[BurstCompile]
public partial struct BarrageSystem : ISystem
{
    Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Random.CreateFromIndex((uint)System.Environment.TickCount);
        state.RequireForUpdate<Barrage>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // SkillState: no cooldown or cost to report; IsActive says whether it's on
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        // Never enabled: the key is read here, so that a skill in a lower slot can't hold it back
        foreach (var (barrage, inputs, modifiers, skills, resourceChanges, requests, transform, entity) in
                 SystemAPI.Query<RefRW<Barrage>, RefRO<InputsData>, RefRO<MovementModifiers>, DynamicBuffer<Skill>, DynamicBuffer<ResourceChanger>, DynamicBuffer<SkillCastRequest>, RefRO<LocalTransform>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            int index = -1;
            if (!SkillUtil.FindSkill(skills, SkillType.Barrage, ref index)) continue;
            // Requirements not met in a dungeon: no missiles (On / Off only picks their targets, so it's kept)
            if (!skills[index].State.Usable)
            {
                barrage.ValueRW.Spent = 0f;
                skills.ElementAt(index).State.IsActive = false;
                continue;
            }

            int slot = skills[index].SlotIndex;
            if (slot >= 0 && slot < 4 && inputs.ValueRO.SkillPressedThisFrame[slot] && !modifiers.ValueRO.PreventsActions)
            {
                barrage.ValueRW.Active = !barrage.ValueRO.Active;
                SkillUtil.MarkUsed(skills, SkillType.Barrage);
            }
            skills.ElementAt(index).State.IsActive = barrage.ValueRO.Active;

            var position = transform.ValueRO.Position;
            var homing = barrage.ValueRO.Active ? HomingType.Strongest : HomingType.Weakest;

            // Casts requested by effects: Barrage has no cost and homes on its own
            while (SkillUtil.TryTakeRequest(requests, SkillType.Barrage, out var request))
            {
                Spawn(commandBuffer, entity, barrage.ValueRO, position, ConeDirection(barrage.ValueRO.ConeAngle, ref _random), homing);
                SkillUtil.MarkUsed(skills, SkillType.Barrage, request.AutoCast);
            }

            foreach (var change in resourceChanges)
                if (change.Value < 0f) barrage.ValueRW.Spent -= change.Value;

            float perMissile = math.max(barrage.ValueRO.ResourcePerMissile, 1f);
            while (barrage.ValueRO.Spent >= perMissile)
            {
                barrage.ValueRW.Spent -= perMissile;
                Spawn(commandBuffer, entity, barrage.ValueRO, position, ConeDirection(barrage.ValueRO.ConeAngle, ref _random), homing);
                SkillUtil.MarkUsed(skills, SkillType.Barrage, autoCast: true);
            }
        }
    }

    // A random direction at most coneAngle from straight up, spread evenly over the cone
    static float3 ConeDirection(float coneAngle, ref Random random)
    {
        float cosTheta = random.NextFloat(math.cos(coneAngle), 1f);
        float sinTheta = math.sqrt(math.max(0f, 1f - cosTheta * cosTheta));
        float phi = random.NextFloat(0f, math.PI2);
        return new float3(sinTheta * math.cos(phi), cosTheta, sinTheta * math.sin(phi));
    }

    /// <summary>One homing missile, launched from above the caster along direction.</summary>
    public static void Spawn(EntityCommandBuffer commandBuffer, Entity caster, in Barrage barrage, float3 casterPosition, float3 direction, HomingType homing)
    {
        var newEntity = commandBuffer.CreateEntity();

        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = casterPosition + new float3(0, 1, 0), Rotation = quaternion.identity, Scale = 0.5f});
        commandBuffer.AddComponent(newEntity, new Projectile{Direction = direction, Speed = barrage.ProjectileSpeed, DestroyOnHit = true, BounceCount = 0, PierceCount = 0});
        commandBuffer.AddComponent(newEntity, new Attack{Origin = DamageLabel.Barrage, Damage = barrage.Damage, Type = DamageType.Physical, DamageHierarchy = DamageHierarchy.Main, Radius = barrage.Radius});
        commandBuffer.AddComponent(newEntity, new HomingProjectile{TurnSpeed = barrage.HomingTurnSpeed, TurnAcceleration = barrage.HomingTurnAcceleration, HomingType = homing});
        commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
    }
}
