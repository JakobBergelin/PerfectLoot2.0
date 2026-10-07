using Unity.Burst;
using Unity.Entities;
using UnityEngine;


[UpdateInGroup(typeof(CalculateStatsSystemGroup), OrderFirst = true)]
[BurstCompile]
partial struct ResetStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (calculatedStats, baseStats) in SystemAPI.Query<RefRW<CalculatedStats>, RefRO<BaseStats>>())
        {
            calculatedStats.ValueRW.Strength = baseStats.ValueRO.Strength;
            calculatedStats.ValueRW.Agility = baseStats.ValueRO.Agility;
            
            calculatedStats.ValueRW.Intelligence = baseStats.ValueRO.Intelligence;
            calculatedStats.ValueRW.Lifesteal = baseStats.ValueRO.Lifesteal;

            calculatedStats.ValueRW.Pierce = baseStats.ValueRO.Pierce;
            calculatedStats.ValueRW.Bounces = baseStats.ValueRO.Bounces;

            calculatedStats.ValueRW.WeaponDamage = baseStats.ValueRO.WeaponDamage;
            calculatedStats.ValueRW.WeaponDamageRange = 0.0f;
            calculatedStats.ValueRW.AttackSpeed = 1.0f;
            calculatedStats.ValueRW.PierceOnCrit = false;
        }
        foreach (var (armor, baseStats) in SystemAPI.Query<RefRW<Armor>, RefRO<BaseStats>>())
        {
            armor.ValueRW.Value = baseStats.ValueRO.Armor;
            armor.ValueRW.PhysicalResistanceMultiplier = 0.0f;
        }
        foreach (var (calculatedStatsPercentages, baseStatsPercentages) in SystemAPI.Query<RefRW<CalculatedStatsPercentages>, RefRO<BaseStatsPercentages>>())
        {
            calculatedStatsPercentages.ValueRW.AreaOfEffect = baseStatsPercentages.ValueRO.AreaOfEffect;
            calculatedStatsPercentages.ValueRW.CooldownReduction = baseStatsPercentages.ValueRO.CooldownReduction;
            calculatedStatsPercentages.ValueRW.ColdResitance = baseStatsPercentages.ValueRO.ColdResitance;
            calculatedStatsPercentages.ValueRW.FireResistance = baseStatsPercentages.ValueRO.FireResistance;
            calculatedStatsPercentages.ValueRW.PoisonResistance = baseStatsPercentages.ValueRO.PoisonResistance;
            calculatedStatsPercentages.ValueRW.LightningResistance = baseStatsPercentages.ValueRO.LightningResistance;
            calculatedStatsPercentages.ValueRW.AllResistance = baseStatsPercentages.ValueRO.AllResistance;
            calculatedStatsPercentages.ValueRW.CriticalChance = baseStatsPercentages.ValueRO.CriticalChance;
            calculatedStatsPercentages.ValueRW.CriticalDamage = baseStatsPercentages.ValueRO.CriticalDamage;
            calculatedStatsPercentages.ValueRW.BlockChance = baseStatsPercentages.ValueRO.BlockChance;
            calculatedStatsPercentages.ValueRW.Damage = 0.0f;
        }
        foreach (var (movementSpeed, baseStatsPercentages) in SystemAPI.Query<RefRW<MovementSpeed>, RefRO<BaseStatsPercentages>>())
        {
            movementSpeed.ValueRW.Value = baseStatsPercentages.ValueRO.MovementSpeed;
        }

        foreach (var (health, baseStats) in SystemAPI.Query<RefRW<Health>, RefRO<BaseStats>>())
        {
            health.ValueRW.MaxValue = baseStats.ValueRO.Health;
            health.ValueRW.MaxHealthMultiplier = 0.0f;
        }

        foreach (var (healthRegeneration, baseStats) in SystemAPI.Query<RefRW<HealthRegeneration>, RefRO<BaseStats>>())
        {
            healthRegeneration.ValueRW.Value = baseStats.ValueRO.HealthRegeneration;
        }

        foreach (var (resource, baseStats) in SystemAPI.Query<RefRW<Resource>, RefRO<BaseStats>>())
        {
            resource.ValueRW.MaxValue = baseStats.ValueRO.Health;
            resource.ValueRW.Regeneration = baseStats.ValueRO.HealthRegeneration;
            resource.ValueRW.RegenerationMultiplier = 0.0f;
        }

        // ResetMovementSpeedJob resetMovementSpeedJob = new ResetMovementSpeedJob();
        // resetMovementSpeedJob.ScheduleParallel();
    }
}

// [BurstCompile]
// public partial struct ResetMovementSpeedJob : IJobEntity
// {
//     public void Execute(ref MovementSpeed movementSpeed, in BaseStatsPercentages baseStatsPercentages)
//     {
//         movementSpeed.Value = baseStatsPercentages.MovementSpeed;
//     }
// }
