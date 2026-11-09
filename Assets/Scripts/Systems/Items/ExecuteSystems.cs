using Unity.Burst;
using Unity.Entities;

/// <summary>Execute legendary power, added by ExecutePower while the item is equipped.</summary>
public struct Execute : IComponentData
{
    public float HealthThreshold;   // An enemy a melee hit leaves below this much health dies
}

/// <summary>
/// Execute: when a melee hit of the wearer (from one of its skills tagged Melee, not its companions) leaves an enemy alive
/// below HealthThreshold, a last hit for the rest of its health kills it, credited to the wearer as Execute. Between working
/// out the damage and dealing it, so the frame's other hits count first and the kill is a normal one (on-kill effects).
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct ExecuteSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Execute>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var executes = SystemAPI.GetComponentLookup<Execute>(true);
        var skills = SystemAPI.GetBufferLookup<Skill>(true);
        state.CompleteDependency();  // The lookups are used here on the main thread

        foreach (var (health, taken) in SystemAPI.Query<RefRO<Health>, DynamicBuffer<CalculatedDamageInstance>>().WithAll<Enemy>())
        {
            float healthLeft = health.ValueRO.CurrentValue;
            if (healthLeft <= 0f) continue;

            var executioner = Entity.Null;
            float threshold = 0f;
            var direction = Unity.Mathematics.float3.zero;
            foreach (var instance in taken)
            {
                if (instance.Damage <= 0f) continue;
                healthLeft -= instance.Damage;
                if (executioner == Entity.Null && executes.HasComponent(instance.Source) && IsMeleeHit(instance, skills))
                {
                    executioner = instance.Source;
                    threshold = executes[instance.Source].HealthThreshold;
                    direction = instance.HitDirection;
                }
            }
            if (executioner == Entity.Null || healthLeft <= 0f || healthLeft >= threshold) continue;

            taken.Add(new CalculatedDamageInstance
            {
                Damage = healthLeft, Type = DamageType.Physical, Hierarchy = DamageHierarchy.Main, Source = executioner, Origin = DamageLabel.Execute, HitDirection = direction,
            });
        }
    }

    // A hit of one of the source's own skills that is tagged Melee right now
    static bool IsMeleeHit(in CalculatedDamageInstance instance, BufferLookup<Skill> skills)
    {
        if (instance.Hierarchy != DamageHierarchy.Main || instance.Origin.Unit != Entity.Null || !skills.HasBuffer(instance.Source)) return false;
        foreach (var skill in skills[instance.Source])
            if ((skill.Tags & SkillTags.Melee) != 0 && SkillDamageLabel.Of(skill.Type) == instance.Origin.Label && instance.Origin.Label != DamageLabel.None) return true;
        return false;
    }
}
