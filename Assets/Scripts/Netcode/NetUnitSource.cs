using Enemies;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// On enemy and companion ghosts: the name of the asset the host made it from, its UnitInfo (several share one prefab):
/// an EnemyDefinition, or a companion's skill. Set with Set.
/// </summary>
public struct NetUnitSource : IComponentData
{
    [GhostField] public FixedString64Bytes Key;
    /// <summary>On its players' side: a summon or a Thrall.</summary>
    [GhostField] public bool Companion;

    /// <summary>The host: names the unit's source for the other machines, on a ghost body that has this baked.</summary>
    public static void Set(EntityManager entityManager, Entity entity, Object source, bool companion)
    {
        if (entityManager.HasComponent<NetUnitSource>(entity))
            entityManager.SetComponentData(entity, new NetUnitSource { Key = source.name, Companion = companion });
    }
}

/// <summary>
/// In someone else's game: gives each enemy and companion ghost what this machine needs to show it, which only the host
/// adds. UnitInfo, from the enemy lists every machine bakes or every skill (ActiveSkillManager), so hovering and the unit
/// panel find it; the Enemy tag (aim assist) or Companion (its bar's colour; its owner stays the host's); and its source's
/// tint. The host's own units already have all of it.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup), OrderFirst = true)]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial class NetUnitSourceSystem : SystemBase
{
    EntityQuery _unresolved;

    protected override void OnCreate()
    {
        _unresolved = SystemAPI.QueryBuilder().WithAll<NetUnitSource>().WithNone<UnitInfo>().Build();
        RequireForUpdate(_unresolved);
    }

    protected override void OnUpdate()
    {
        using var entities = _unresolved.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
        {
            var source = EntityManager.GetComponentData<NetUnitSource>(entity);
            if (source.Key.IsEmpty) continue;
            var asset = FindSource(source.Key);
            if (asset == null) continue;

            EntityManager.AddComponentData(entity, new UnitInfo { Source = asset });
            if (source.Companion) EntityManager.AddComponentData(entity, new Companion());
            else EntityManager.AddComponent<Enemy>(entity);
            if (asset is EnemyDefinition { OverrideColor: true } definition) EnemyEntitySetup.ApplyTint(EntityManager, entity, definition.Tint);
            if (asset is ActiveSkills.CompanionSkill skill) EnemyEntitySetup.ApplyTint(EntityManager, entity, skill.Tint);
        }
    }

    // Every enemy list (the core one and the level's), then every skill
    ScriptableObject FindSource(in FixedString64Bytes key)
    {
        foreach (var list in SystemAPI.Query<DynamicBuffer<EnemyPrefabElement>>())
            foreach (var element in list)
                if (element.Key == key) return element.Definition.Value;
        var skills = ActiveSkillManager.Instance;
        if (skills != null)
        {
            string name = key.ToString();
            foreach (var skill in skills.AllSkills)
                if (skill != null && skill.name == name) return skill;
        }
        return null;
    }
}
