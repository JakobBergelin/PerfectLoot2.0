using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;


[UpdateInGroup(typeof(CalculateStatsSystemGroup), OrderFirst = true)]
[BurstCompile]
partial struct ResetStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (calculatedStats, baseStats) in SystemAPI.Query<RefRW<CalculatedStats>, RefRO<BaseStats>>())
        {
            calculatedStats.ValueRW.Value = baseStats.ValueRO.Value;
            calculatedStats.ValueRW.PierceOnCrit = false;
        }
        foreach (var armor in SystemAPI.Query<RefRW<Armor>>())
        {
            armor.ValueRW.PhysicalResistanceMultiplier = 0.0f;
        }
        foreach (var health in SystemAPI.Query<RefRW<Health>>())
        {
            health.ValueRW.MaxHealthMultiplier = 0.0f;
        }
        foreach (var resource in SystemAPI.Query<RefRW<Resource>>())
        {
            resource.ValueRW.RegenerationMultiplier = 0.0f;
        }
    }
}

[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
partial struct ApplyEquipmentStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (calculatedStats, equipmentStats) in SystemAPI.Query<RefRW<CalculatedStats>, RefRO<EquipmentStats>>())
        {
            calculatedStats.ValueRW.Value.Add(equipmentStats.ValueRO.Value);
        }
    }
}

[UpdateInGroup(typeof(FinalizeStatsSystemGroup))]
[BurstCompile]
partial struct FinalizeStatsSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<CombatConfig>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var combatConfig = SystemAPI.GetSingleton<CombatConfig>();

        // No weapon equipped: fall back to unarmed reach
        foreach (var stats in SystemAPI.Query<RefRW<CalculatedStats>>())
        {
            if (stats.ValueRO.Value.WeaponReach <= 0f)
                stats.ValueRW.Value.WeaponReach = combatConfig.UnarmedWeaponReach;
        }

        foreach (var (armor, stats) in SystemAPI.Query<RefRW<Armor>, RefRO<CalculatedStats>>())
        {
            armor.ValueRW.Value = stats.ValueRO.Value.Armor;
            var fromArmor = 0.0f;
            if(armor.ValueRO.Value != 0)
            {
                fromArmor = 1.0f - math.pow(1.0f - (combatConfig.ReductionPerArmor * (1.0f + armor.ValueRO.PhysicalResistanceMultiplier)), armor.ValueRO.Value);
            }
            // Flat physical resistance stacks multiplicatively with the armor reduction
            armor.ValueRW.PhysicalResistance = 1.0f - (1.0f - fromArmor) * (1.0f - stats.ValueRO.Value.PhysicalResistance);
        }

        foreach (var (movementSpeed, stats) in SystemAPI.Query<RefRW<MovementSpeed>, RefRO<CalculatedStats>>())
        {
            movementSpeed.ValueRW.Value = stats.ValueRO.Value.MovementSpeed;
        }

        foreach (var (health, stats) in SystemAPI.Query<RefRW<Health>, RefRO<CalculatedStats>>())
        {
            health.ValueRW.MaxValue = stats.ValueRO.Value.Health * (1.0f + health.ValueRO.MaxHealthMultiplier);
        }

        foreach (var (healthRegeneration, stats) in SystemAPI.Query<RefRW<HealthRegeneration>, RefRO<CalculatedStats>>())
        {
            healthRegeneration.ValueRW.Value = stats.ValueRO.Value.HealthRegeneration;
        }

        foreach (var (resource, stats) in SystemAPI.Query<RefRW<Resource>, RefRO<CalculatedStats>>())
        {
            resource.ValueRW.MaxValue = stats.ValueRO.Value.Resource;
            resource.ValueRW.Regeneration = stats.ValueRO.Value.ResourceRegeneration * (1.0f + resource.ValueRO.RegenerationMultiplier);
        }
    }
}
