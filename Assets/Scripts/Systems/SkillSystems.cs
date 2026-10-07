using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
partial class InputSkillSystem : SystemBase
{
    // TODO use midframe commandbuffer
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    
    [BurstCompile]
    protected override void OnCreate()
    {
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
    }

    private void ManageSkill<T>(SkillType type, T component, DynamicBuffer<Skill> buffer, EntityCommandBuffer commandBuffer, EntityManager entityManager, Entity entity) where T : unmanaged, IEnableableComponent, IComponentData
    {
        int index = -1;
        if (SkillUtil.FindSkill(buffer, type, ref index))
        {
            if(!entityManager.HasComponent<T>(entity))
            {
                commandBuffer.AddComponent<T>(entity, component);
            }

            commandBuffer.SetComponentEnabled<T>(entity, buffer[index].events.OnPressed);
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
        foreach (var (inputs, entity) in SystemAPI.Query<RefRO<InputsData>>().WithEntityAccess())
        {
            bool otherSkillPressed = false;
            if (EntityManager.HasBuffer<Skill>(entity))
            {
                var skills = EntityManager.GetBuffer<Skill>(entity);

                for(int i = 0; i < skills.Length; i++)
                {
                    var skillcopy = skills[i];
                    bool skillPressed = Convert.ToBoolean(inputs.ValueRO.Skill[i]);
                    skillcopy.events.OnPressed = skillPressed && !otherSkillPressed;
                    skillcopy.events.OnUsed = false;
                    if(skillPressed)
                    {
                        // TODO, replace with prio order, dash pressed should have prio over fireball etc.
                        otherSkillPressed = true;
                    }
                    skills[i] = skillcopy;
                }
            }
        }

        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        foreach (var (skillBuffer, entity) in SystemAPI.Query<DynamicBuffer<Skill>>().WithEntityAccess())
        {
            // TODO move skill balancing
            ManageSkill(SkillType.Barrage, new Barrage{AttackSpeed = 0.3f, Elapsed = 0.0f}, skillBuffer, commandBuffer, EntityManager, entity);
            ManageSkill(SkillType.FistOfThunder, new FistsOfThunder{}, skillBuffer, commandBuffer, EntityManager,  entity);
            ManageSkill(SkillType.Fireball, new FireBall{AttackSpeed = 0.5f, Elapsed = 0.0f}, skillBuffer, commandBuffer, EntityManager, entity);
            ManageSkill(SkillType.Dash, new Dash{Elapsed = 1f, Dashing = false}, skillBuffer, commandBuffer, EntityManager, entity);
            ManageSkill(SkillType.Whirlwind, new Whirlwind{}, skillBuffer, commandBuffer, EntityManager,  entity);
            ManageSkill(SkillType.Tornado, new Tornado{}, skillBuffer, commandBuffer, EntityManager,  entity);
        }        
    }
}
