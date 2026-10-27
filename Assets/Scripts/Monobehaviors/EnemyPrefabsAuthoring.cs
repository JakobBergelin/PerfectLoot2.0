using System.Collections.Generic;
using Enemies;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Bakes the visual prefab of every listed enemy definition so EnemyManager can spawn them at runtime: the enemies
/// available everywhere (in the CoreEntities subscene). A level's own enemies come from its LevelLayout, which bakes
/// the same list for every enemy its spawners and placement rules use. Prefabs are looked up by the definition's asset name.
/// </summary>
public class EnemyPrefabsAuthoring : MonoBehaviour
{
    public List<EnemyDefinition> Definitions = new List<EnemyDefinition>();

    public class Baker : Baker<EnemyPrefabsAuthoring>
    {
        public override void Bake(EnemyPrefabsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            var buffer = AddBuffer<EnemyPrefabElement>(entity);
            var seen = new HashSet<string>();

            foreach (var definition in authoring.Definitions)
            {
                DependsOn(definition);
                if (definition == null) continue;
                if (definition.Prefab == null)
                {
                    Debug.LogWarning($"EnemyPrefabsAuthoring: {definition.name} has no Prefab.", authoring);
                    continue;
                }
                if (!seen.Add(definition.name))
                {
                    Debug.LogWarning($"EnemyPrefabsAuthoring: more than one definition is named {definition.name}; names must be unique.", authoring);
                    continue;
                }
                buffer.Add(new EnemyPrefabElement
                {
                    Key = definition.name,
                    Prefab = GetEntity(definition.Prefab, TransformUsageFlags.Dynamic),
                });
            }
        }
    }
}

/// <summary>A baked enemy prefab, keyed by its EnemyDefinition's asset name.</summary>
public struct EnemyPrefabElement : IBufferElementData
{
    public FixedString64Bytes Key;
    public Entity Prefab;
}
