using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public static class CompanionUtil
{
    /// <summary>The critical chance a companion's hit adds to its owner's: its Pack Instinct bonus, unless its owner has that already.</summary>
    public static float CriticalChanceBonus(in ComponentLookup<PackInstinctBuff> buffs, Entity companion, Entity owner)
    {
        if (!buffs.HasComponent(companion) || !buffs.IsComponentEnabled(companion)) return 0f;
        bool ownerHasIt = buffs.HasComponent(owner) && buffs.IsComponentEnabled(owner);
        return ownerHasIt ? 0f : buffs[companion].CriticalChanceBonus;
    }
}

/// <summary>
/// Companions have their owner's stats, so EnemySkillCastSystem times their skills by the owner's attack speed. What
/// protects them is their own: their resistances, block and dodge, as for their health and armor.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateBefore(typeof(EnemySkillCastSystem))]
[BurstCompile]
public partial struct CompanionStatsSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Companion>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (companion, stats) in SystemAPI.Query<RefRO<Companion>, RefRW<CalculatedStats>>())
        {
            var owner = companion.ValueRO.Owner;
            if (!SystemAPI.HasComponent<CalculatedStats>(owner)) continue;

            var own = stats.ValueRO.Value;
            stats.ValueRW = SystemAPI.GetComponent<CalculatedStats>(owner);
            ref var value = ref stats.ValueRW.Value;
            value.ColdResistance = own.ColdResistance;
            value.FireResistance = own.FireResistance;
            value.LightningResistance = own.LightningResistance;
            value.PoisonResistance = own.PoisonResistance;
            value.AllResistance = own.AllResistance;
            value.BlockChance = own.BlockChance;
            value.DodgeChance = own.DodgeChance;
        }
    }
}

/// <summary>
/// Sets each companion's EnemyTarget to the nearest living enemy within SightRadius that it can see, on its owner's
/// FlowField. EnemySkillCastSystem then uses its skills on that target, as it does for enemies.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(FlowFieldSystem))]
[BurstCompile]
public partial struct CompanionDetectionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<CompanionDetection>();
        state.RequireForUpdate<FlowFields>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var fields = SystemAPI.GetSingleton<FlowFields>();
        var targetables = SystemAPI.GetComponentLookup<EnemyTargetable>(true);
        state.CompleteDependency();  // The flow fields are used here on the main thread

        foreach (var (target, detection, companion, transform) in
                 SystemAPI.Query<RefRW<EnemyTarget>, RefRO<CompanionDetection>, RefRO<Companion>, RefRO<LocalTransform>>())
        {
            target.ValueRW.Unit = Entity.Null;
            var owner = companion.ValueRO.Owner;
            if (!targetables.HasComponent(owner)) continue;

            var field = fields[targetables[owner].FieldSlot];
            var position = transform.ValueRO.Position;
            float nearestDistanceSq = detection.ValueRO.SightRadius * detection.ValueRO.SightRadius;

            foreach (var (enemyTransform, health, enemy) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<Health>>().WithAll<Enemy>().WithNone<RigidDeath>().WithEntityAccess())
            {
                var enemyPosition = enemyTransform.ValueRO.Position;
                float distanceSq = math.distancesq(enemyPosition.xz, position.xz);
                if (distanceSq >= nearestDistanceSq || health.ValueRO.CurrentValue <= 0f || !field.CanSee(position, enemyPosition)) continue;
                nearestDistanceSq = distanceSq;
                target.ValueRW.Unit = enemy;
            }
        }
    }
}
