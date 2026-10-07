using Unity.Burst;
using Unity.Entities;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
partial class PassiveSystem : SystemBase
{
    // TODO use midframe commandbuffer
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    
    [BurstCompile]
    protected override void OnCreate()
    {
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
    }

    private void ManageResource<T>(ResourceType type, T component, ResourceType currentType, EntityCommandBuffer commandBuffer, EntityManager entityManager, Entity entity) where T : unmanaged, IComponentData
    {
        if(currentType == type)
        {
            if(!entityManager.HasComponent<T>(entity))
            {
                commandBuffer.AddComponent<T>(entity, component);
            }   
        }
        else
        {
            if(entityManager.HasComponent<T>(entity))
            {
                commandBuffer.RemoveComponent<T>(entity);
            }
        }
    }

    private void ManageClass<T>(ClassType type, T component, ClassType currentClass, EntityCommandBuffer commandBuffer, EntityManager entityManager, Entity entity) where T : unmanaged, IComponentData
    {
        if(currentClass == type)
        {
            if(!entityManager.HasComponent<T>(entity))
            {
                commandBuffer.AddComponent<T>(entity, component);
            }   
        }
        else
        {
            if(entityManager.HasComponent<T>(entity))
            {
                commandBuffer.RemoveComponent<T>(entity);
            }
        }
    }

    private void ManagePassive<T>(PassiveType type, T component, DynamicBuffer<Passive> buffer, EntityCommandBuffer commandBuffer, EntityManager entityManager, Entity entity) where T : unmanaged, IComponentData
    {
        Passive foundPassive = new Passive{Type = PassiveType.None};
        foreach (var passive in buffer)
        {
            if(passive.Type == type)
            {
                foundPassive = passive;
                break;
            }
        }

        if(foundPassive.Type != PassiveType.None)
        {
            if(!entityManager.HasComponent<T>(entity))
            {
                commandBuffer.AddComponent<T>(entity, component);
            }   
        }
        else
        {
            if(entityManager.HasComponent<T>(entity))
            {
                commandBuffer.RemoveComponent<T>(entity);
            }
        }
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        // TODO check if passive updated
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        foreach (var (unitResource, entity) in SystemAPI.Query<RefRO<UnitResource>>().WithEntityAccess())
        {
            // TODO resourceUnit Changed?
            // TODO move passive balancing
            ManageResource(ResourceType.Mana, new Mana{}, unitResource.ValueRO.Type, commandBuffer, EntityManager, entity);
            ManageResource(ResourceType.Rage, new Rage{},unitResource.ValueRO.Type, commandBuffer, EntityManager, entity);
            ManageResource(ResourceType.Focus, new Focus{},unitResource.ValueRO.Type, commandBuffer, EntityManager, entity);
            ManageResource(ResourceType.Energy, new Energy{},unitResource.ValueRO.Type, commandBuffer, EntityManager, entity);
            ManageResource(ResourceType.Arcane, new Arcane{},unitResource.ValueRO.Type, commandBuffer, EntityManager, entity);
        }

        foreach (var (unitClass, entity) in SystemAPI.Query<RefRO<UnitClass>>().WithEntityAccess())
        {
            ManageClass(ClassType.Warrior, new Warrior{StrengthPerLevel = 3, AgilityPerLevel = 0, IntelligencePerLevel = 0}, unitClass.ValueRO.Type, commandBuffer, EntityManager, entity);
            ManageClass(ClassType.Mage, new Mage{StrengthPerLevel = 0, AgilityPerLevel = 0, IntelligencePerLevel = 3}, unitClass.ValueRO.Type, commandBuffer, EntityManager, entity);
            ManageClass(ClassType.Rogue, new Rogue{StrengthPerLevel = 0, AgilityPerLevel = 3, IntelligencePerLevel = 0}, unitClass.ValueRO.Type, commandBuffer, EntityManager, entity);
            ManageClass(ClassType.Monk, new Monk{StrengthPerLevel = 1, AgilityPerLevel = 1, IntelligencePerLevel = 1}, unitClass.ValueRO.Type, commandBuffer, EntityManager, entity);
            ManageClass(ClassType.Fighter, new Fighter{StrengthPerLevel = 2, AgilityPerLevel = 1, IntelligencePerLevel = 0}, unitClass.ValueRO.Type, commandBuffer, EntityManager, entity);
            ManageClass(ClassType.Paladin, new Paladin{StrengthPerLevel = 2, AgilityPerLevel = 0, IntelligencePerLevel = 1}, unitClass.ValueRO.Type, commandBuffer, EntityManager, entity);
            ManageClass(ClassType.Caster, new Caster{StrengthPerLevel = 0, AgilityPerLevel = 1, IntelligencePerLevel = 2}, unitClass.ValueRO.Type, commandBuffer, EntityManager, entity);
            ManageClass(ClassType.Cleric, new Cleric{StrengthPerLevel = 1, AgilityPerLevel = 0, IntelligencePerLevel = 2}, unitClass.ValueRO.Type, commandBuffer, EntityManager, entity);
            ManageClass(ClassType.Fencer, new Fencer{StrengthPerLevel = 1, AgilityPerLevel = 2, IntelligencePerLevel = 0}, unitClass.ValueRO.Type, commandBuffer, EntityManager, entity);
            ManageClass(ClassType.Savant, new Savant{StrengthPerLevel = 0, AgilityPerLevel = 2, IntelligencePerLevel = 1}, unitClass.ValueRO.Type, commandBuffer, EntityManager, entity);
        }

        foreach (var (skillBuffer, entity) in SystemAPI.Query<DynamicBuffer<Passive>>().WithEntityAccess())
        {
            ManagePassive(PassiveType.Golem, new Golem{}, skillBuffer, commandBuffer, EntityManager, entity);
            ManagePassive(PassiveType.Huskar, new Huskar{}, skillBuffer, commandBuffer, EntityManager, entity);
            ManagePassive(PassiveType.Medusa, new Medusa{}, skillBuffer, commandBuffer, EntityManager, entity);
            ManagePassive(PassiveType.Balance, new Balance{}, skillBuffer, commandBuffer, EntityManager, entity);
            ManagePassive(PassiveType.Luminosity, new Luminosity{}, skillBuffer, commandBuffer, EntityManager, entity);
        }
        
    }
}

[BurstCompile]
[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
public partial struct CalculateClassSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (playerclass, level, calculatedStats) in SystemAPI.Query<RefRO<Warrior>, RefRO<Level>, RefRW<CalculatedStats>>())
        {
            calculatedStats.ValueRW.Strength += level.ValueRO.Value * playerclass.ValueRO.StrengthPerLevel;
            calculatedStats.ValueRW.Agility += level.ValueRO.Value * playerclass.ValueRO.AgilityPerLevel;
            calculatedStats.ValueRW.Intelligence += level.ValueRO.Value * playerclass.ValueRO.IntelligencePerLevel;
        }
        foreach (var (playerclass, level, calculatedStats) in SystemAPI.Query<RefRO<Mage>, RefRO<Level>, RefRW<CalculatedStats>>())
        {
            calculatedStats.ValueRW.Strength += level.ValueRO.Value * playerclass.ValueRO.StrengthPerLevel;
            calculatedStats.ValueRW.Agility += level.ValueRO.Value * playerclass.ValueRO.AgilityPerLevel;
            calculatedStats.ValueRW.Intelligence += level.ValueRO.Value * playerclass.ValueRO.IntelligencePerLevel;
        }
        foreach (var (playerclass, level, calculatedStats) in SystemAPI.Query<RefRO<Rogue>, RefRO<Level>, RefRW<CalculatedStats>>())
        {
            calculatedStats.ValueRW.Strength += level.ValueRO.Value * playerclass.ValueRO.StrengthPerLevel;
            calculatedStats.ValueRW.Agility += level.ValueRO.Value * playerclass.ValueRO.AgilityPerLevel;
            calculatedStats.ValueRW.Intelligence += level.ValueRO.Value * playerclass.ValueRO.IntelligencePerLevel;
        }
        foreach (var (playerclass, level, calculatedStats) in SystemAPI.Query<RefRO<Monk>, RefRO<Level>, RefRW<CalculatedStats>>())
        {
            calculatedStats.ValueRW.Strength += level.ValueRO.Value * playerclass.ValueRO.StrengthPerLevel;
            calculatedStats.ValueRW.Agility += level.ValueRO.Value * playerclass.ValueRO.AgilityPerLevel;
            calculatedStats.ValueRW.Intelligence += level.ValueRO.Value * playerclass.ValueRO.IntelligencePerLevel;
        }
        foreach (var (playerclass, level, calculatedStats) in SystemAPI.Query<RefRO<Fighter>, RefRO<Level>, RefRW<CalculatedStats>>())
        {
            calculatedStats.ValueRW.Strength += level.ValueRO.Value * playerclass.ValueRO.StrengthPerLevel;
            calculatedStats.ValueRW.Agility += level.ValueRO.Value * playerclass.ValueRO.AgilityPerLevel;
            calculatedStats.ValueRW.Intelligence += level.ValueRO.Value * playerclass.ValueRO.IntelligencePerLevel;
        }
        foreach (var (playerclass, level, calculatedStats) in SystemAPI.Query<RefRO<Paladin>, RefRO<Level>, RefRW<CalculatedStats>>())
        {
            calculatedStats.ValueRW.Strength += level.ValueRO.Value * playerclass.ValueRO.StrengthPerLevel;
            calculatedStats.ValueRW.Agility += level.ValueRO.Value * playerclass.ValueRO.AgilityPerLevel;
            calculatedStats.ValueRW.Intelligence += level.ValueRO.Value * playerclass.ValueRO.IntelligencePerLevel;
        }
        foreach (var (playerclass, level, calculatedStats) in SystemAPI.Query<RefRO<Caster>, RefRO<Level>, RefRW<CalculatedStats>>())
        {
            calculatedStats.ValueRW.Strength += level.ValueRO.Value * playerclass.ValueRO.StrengthPerLevel;
            calculatedStats.ValueRW.Agility += level.ValueRO.Value * playerclass.ValueRO.AgilityPerLevel;
            calculatedStats.ValueRW.Intelligence += level.ValueRO.Value * playerclass.ValueRO.IntelligencePerLevel;
        }
        foreach (var (playerclass, level, calculatedStats) in SystemAPI.Query<RefRO<Cleric>, RefRO<Level>, RefRW<CalculatedStats>>())
        {
            calculatedStats.ValueRW.Strength += level.ValueRO.Value * playerclass.ValueRO.StrengthPerLevel;
            calculatedStats.ValueRW.Agility += level.ValueRO.Value * playerclass.ValueRO.AgilityPerLevel;
            calculatedStats.ValueRW.Intelligence += level.ValueRO.Value * playerclass.ValueRO.IntelligencePerLevel;
        }
        foreach (var (playerclass, level, calculatedStats) in SystemAPI.Query<RefRO<Fencer>, RefRO<Level>, RefRW<CalculatedStats>>())
        {
            calculatedStats.ValueRW.Strength += level.ValueRO.Value * playerclass.ValueRO.StrengthPerLevel;
            calculatedStats.ValueRW.Agility += level.ValueRO.Value * playerclass.ValueRO.AgilityPerLevel;
            calculatedStats.ValueRW.Intelligence += level.ValueRO.Value * playerclass.ValueRO.IntelligencePerLevel;
        }
        foreach (var (playerclass, level, calculatedStats) in SystemAPI.Query<RefRO<Savant>, RefRO<Level>, RefRW<CalculatedStats>>())
        {
            calculatedStats.ValueRW.Strength += level.ValueRO.Value * playerclass.ValueRO.StrengthPerLevel;
            calculatedStats.ValueRW.Agility += level.ValueRO.Value * playerclass.ValueRO.AgilityPerLevel;
            calculatedStats.ValueRW.Intelligence += level.ValueRO.Value * playerclass.ValueRO.IntelligencePerLevel;
        }
    }
}