using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using System;
using System.Linq;

[BurstCompile]
partial class RandomizeCharacterSystem : SystemBase
{
    // TODO use midframe commandbuffer
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    
    [BurstCompile]
    protected override void OnCreate()
    {
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
    }
    public static T RandomEnum<T>() where T : struct, IConvertible
    {
        if (!typeof(T).IsEnum) { throw new Exception("random enum variable is not an enum"); }

        var random = new System.Random();
        var values = Enum.GetValues(typeof(T));
        return (T)values.GetValue(random.Next(values.Length));
    }
    
    [BurstCompile]
    protected override void OnUpdate()
    {
        var random = new System.Random();
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        foreach (var(skillBuffer, level, inventoryBuffer, unitResource, unitClass, passiveBuffer, entity) in SystemAPI.Query<DynamicBuffer<Skill>, RefRW<Level>, DynamicBuffer<InventoryItem>, RefRW<UnitResource>, RefRW<UnitClass>,  DynamicBuffer<Passive>>().WithPresent<RandomizeCharacter>().WithEntityAccess())
        {
            skillBuffer.Clear();
            inventoryBuffer.Clear();
            passiveBuffer.Clear();

            unitResource.ValueRW.Type = Enum.GetValues(typeof(ResourceType)).Cast<ResourceType>().Where(_ => _ != ResourceType.None).OrderBy(_ => random.Next()).FirstOrDefault();
            unitClass.ValueRW.Type = Enum.GetValues(typeof(ClassType)).Cast<ClassType>().Where(_ => _ != ClassType.None).OrderBy(_ => random.Next()).FirstOrDefault();

            var tempSkillList = Enum.GetValues(typeof(SkillType)).Cast<SkillType>().OrderBy(_ => random.Next()).Take(4).ToList();

            foreach(var skillType in tempSkillList)
            {
                skillBuffer.Add(new Skill{Type = skillType});
            }

            var tempInventoryList = Enum.GetValues(typeof(ItemType)).Cast<ItemType>().Where(_ => _ != ItemType.None).OrderBy(_ => random.Next()).Take(5).ToList();

            foreach(var inventoryItemType in tempInventoryList)
            {
                inventoryBuffer.Add(new InventoryItem{Type = inventoryItemType, Seed = Guid.NewGuid()});
            }

            var tempPassiveList = Enum.GetValues(typeof(PassiveType)).Cast<PassiveType>().Where(_ => _ != PassiveType.None).OrderBy(_ => random.Next()).Take(2).ToList();

            foreach(var passiveType in tempPassiveList)
            {
                passiveBuffer.Add(new Passive{Type = passiveType});
            }

            level.ValueRW.Value = UnityEngine.Random.Range(1,10);
            
        }

        foreach (var (health, entity) in SystemAPI.Query<RefRW<Health>>().WithPresent<RandomizeCharacter>().WithEntityAccess())
        {
            health.ValueRW.CurrentValue = health.ValueRO.MaxValue;
            commandBuffer.RemoveComponent<RandomizeCharacter>(entity);
        }
    }
}