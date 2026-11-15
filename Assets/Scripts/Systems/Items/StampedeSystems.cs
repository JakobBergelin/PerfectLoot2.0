using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Stampede unique power, added by StampedePower while the item is equipped.</summary>
public struct Stampede : IComponentData
{
    public float WeaponDamagePerMovementSpeed;  // Weapon damage × this × the movement speed bonus (10: +20% speed deals 2× weapon damage)
    public float Radius;
    public float HitForce;

    public bool WasDashing;                     // Dash's state last frame, to see a dash start
}

/// <summary>The attack that travels with a Stampede dash (its Source); removed when the dash ends.</summary>
public struct StampedeAttack : IComponentData {}

/// <summary>
/// Stampede: each dash carries an attack along with the wearer that hits every enemy it passes through once. Its damage
/// is weapon damage × WeaponDamagePerMovementSpeed × the wearer's movement speed bonus, so it grows with movement speed.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateAfter(typeof(DashSystem))]
public partial struct StampedeSystem : ISystem
{
    Unity.Mathematics.Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)Environment.TickCount);
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<Stampede>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        // A dash starts: an attack joins the wearer (Dash is only enabled while its key is held; Dashing goes on after)
        foreach (var (stampede, dash, stats, transform, entity) in SystemAPI.Query<RefRW<Stampede>, RefRO<Dash>, RefRO<CalculatedStats>, RefRO<LocalTransform>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            bool dashing = dash.ValueRO.Dashing;
            if (dashing && !stampede.ValueRO.WasDashing)
            {
                var s = stats.ValueRO.Value;
                float weaponDamage = s.WeaponDamage + _random.NextFloat(-s.WeaponDamageRange, s.WeaponDamageRange);
                float damage = weaponDamage * stampede.ValueRO.WeaponDamagePerMovementSpeed * math.max(0f, s.MovementSpeed);
                if (damage > 0f)
                {
                    var attack = commandBuffer.CreateEntity();
                    commandBuffer.AddComponent(attack, new LocalTransform{Position = transform.ValueRO.Position, Rotation = quaternion.identity, Scale = 1f});
                    commandBuffer.AddComponent(attack, new Attack{Origin = DamageLabel.Stampede, Damage = damage, Type = DamageType.Physical, DamageHierarchy = DamageHierarchy.Main,
                        Radius = stampede.ValueRO.Radius, HitForce = stampede.ValueRO.HitForce, GracePeriod = 2f});
                    commandBuffer.AddComponent(attack, new Source{Entity = entity});
                    commandBuffer.AddComponent(attack, new AttackTarget{TargetType = TargetType.Enemy});
                    commandBuffer.AddComponent(attack, new InitializeFromSource{});
                    commandBuffer.AddComponent(attack, new StampedeAttack{});
                }
            }
            stampede.ValueRW.WasDashing = dashing;
        }

        // Each attack stays on its wearer until the dash ends (or the power or Dash is gone)
        foreach (var (source, attack) in SystemAPI.Query<RefRO<Source>>().WithAll<StampedeAttack, LocalTransform>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            var owner = source.ValueRO.Entity;
            if (!SystemAPI.HasComponent<Stampede>(owner) || !SystemAPI.HasComponent<Dash>(owner) || !SystemAPI.GetComponent<Dash>(owner).Dashing
                || !SystemAPI.HasComponent<LocalTransform>(owner))
            {
                commandBuffer.AddComponent<DestroyWithChildren>(attack);
                continue;
            }
            SystemAPI.GetComponentRW<LocalTransform>(attack).ValueRW.Position = SystemAPI.GetComponent<LocalTransform>(owner).Position;
        }
    }
}
