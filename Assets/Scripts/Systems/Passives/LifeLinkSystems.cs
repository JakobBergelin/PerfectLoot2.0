using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Life Link: links each wearer to the living player in Range with the lowest share of their max Health, never itself.
/// That player gets the wearer's Health regeneration through a PeriodicHealingInstance, even with the wearer at full
/// Health, and a copy of every heal the wearer receives this frame, before ApplyHealingSystem uses those up.
/// </summary>
[UpdateInGroup(typeof(HealthAndResourceSystemGroup))]
[UpdateAfter(typeof(HealingSystem))]
[UpdateAfter(typeof(AccumilatedPeriodicHealSystem))]
[UpdateBefore(typeof(ApplyHealingSystem))]
[BurstCompile]
public partial struct LifeLinkSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var buffs = SystemAPI.GetComponentLookup<LifeLinkBuff>();
        float deltaTime = SystemAPI.Time.DeltaTime;
        state.CompleteDependency();  // The lookup is used here on the main thread

        // Runs without wearers too, so the buff goes when the last one does
        foreach (var buff in SystemAPI.Query<EnabledRefRW<LifeLinkBuff>>())
            buff.ValueRW = false;

        foreach (var (link, transform, regeneration, healing, entity) in
                 SystemAPI.Query<RefRW<LifeLink>, RefRO<LocalTransform>, RefRO<HealthRegeneration>, DynamicBuffer<HealingInstance>>().WithEntityAccess())
        {
            float rangeSq = link.ValueRO.Range * link.ValueRO.Range;
            var target = Entity.Null;
            float lowestShare = float.MaxValue;
            foreach (var (health, playerTransform, player) in SystemAPI.Query<RefRO<Health>, RefRO<LocalTransform>>().WithAll<Player>().WithNone<RigidDeath>().WithEntityAccess())
            {
                if (player == entity || health.ValueRO.CurrentValue <= 0f) continue;
                if (math.distancesq(playerTransform.ValueRO.Position.xz, transform.ValueRO.Position.xz) > rangeSq) continue;
                float share = health.ValueRO.CurrentValue / health.ValueRO.MaxValue;
                if (share >= lowestShare) continue;
                lowestShare = share;
                target = player;
            }

            link.ValueRW.Target = target;
            if (target == Entity.Null) continue;

            float linked = buffs.IsComponentEnabled(target) ? buffs[target].HealthRegeneration : 0f;
            buffs[target] = new LifeLinkBuff { HealthRegeneration = linked + regeneration.ValueRO.Value };
            buffs.SetComponentEnabled(target, true);

            AddRegeneration(SystemAPI.GetBuffer<PeriodicHealingInstance>(target), entity, regeneration.ValueRO.Value * deltaTime, link.ValueRO.TickRate);

            var targetHealing = SystemAPI.GetBuffer<HealingInstance>(target);
            for (int i = 0; i < healing.Length; i++)
                targetHealing.Add(healing[i]);
        }
    }

    // One instance per wearer, as for a PeriodicHeal area
    static void AddRegeneration(DynamicBuffer<PeriodicHealingInstance> instances, Entity wearer, float heal, float tickRate)
    {
        for (int i = 0; i < instances.Length; i++)
        {
            if (instances[i].Area != wearer) continue;
            instances.ElementAt(i).AccumulatedHeal += heal;
            return;
        }
        instances.Add(new PeriodicHealingInstance { Area = wearer, AccumulatedHeal = heal, Tick = tickRate, Source = wearer, Label = DamageLabel.LifeLink });
    }
}

/// <summary>Placeholder art: each link as a line, seen in the Scene view or with Gizmos on.</summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
partial struct LifeLinkUISystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var up = new float3(0f, 1f, 0f);
        foreach (var (link, transform) in SystemAPI.Query<RefRO<LifeLink>, RefRO<LocalTransform>>())
        {
            var target = link.ValueRO.Target;
            if (!SystemAPI.HasComponent<LocalTransform>(target)) continue;
            Debug.DrawLine(transform.ValueRO.Position + up, SystemAPI.GetComponent<LocalTransform>(target).Position + up, Color.green);
        }
    }
}
