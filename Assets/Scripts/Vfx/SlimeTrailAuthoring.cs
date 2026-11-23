using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>A unit that leaves a slime trail as it moves (SlimeTrailSystem): a ground decal every Spacing meters.</summary>
public struct SlimeTrail : IComponentData
{
    public UnityObjectRef<Material> Material;
    public float Spacing, Size;     // Meters, times the unit's scale
}

/// <summary>On a unit's visual prefab (the slimes'): it leaves a slime trail (SlimeTrailSystem).</summary>
public class SlimeTrailAuthoring : MonoBehaviour
{
    [Tooltip("A decal material (URP's decal shader), the trail's mark")]
    public Material Material;
    [Tooltip("Meters moved between marks, times the unit's scale")]
    [Min(0.1f)] public float Spacing = 0.9f;
    [Tooltip("Meters across one mark, times the unit's scale")]
    [Min(0.1f)] public float Size = 1.4f;

    public class Baker : Baker<SlimeTrailAuthoring>
    {
        public override void Bake(SlimeTrailAuthoring authoring)
        {
            if (authoring.Material == null) return;
            AddComponent(GetEntity(TransformUsageFlags.Dynamic), new SlimeTrail { Material = authoring.Material, Spacing = authoring.Spacing, Size = authoring.Size });
        }
    }
}

/// <summary>
/// Slime trails: a living unit with SlimeTrail stamps a ground mark (GroundDecals) each time it has moved its spacing,
/// so a slime leaves a line of glistening patches that fade after a few seconds. For the eyes only; every machine
/// stamps from the units it shows.
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class SlimeTrailSystem : SystemBase
{
    const float Hold = 5f, Fade = 3f;

    readonly Dictionary<Entity, float3> _last = new Dictionary<Entity, float3>();
    readonly HashSet<Entity> _seen = new HashSet<Entity>();
    EntityQuery _query;

    protected override void OnCreate()
    {
        _query = SystemAPI.QueryBuilder().WithAll<SlimeTrail, LocalToWorld>().WithNone<RigidDeath>().Build();
        RequireForUpdate(_query);
    }

    protected override void OnUpdate()
    {
        _seen.Clear();
        using var entities = _query.ToEntityArray(Allocator.Temp);
        using var trails = _query.ToComponentDataArray<SlimeTrail>(Allocator.Temp);
        using var transforms = _query.ToComponentDataArray<LocalToWorld>(Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
        {
            var e = entities[i];
            _seen.Add(e);
            float3 position = transforms[i].Position;
            float scale = math.length(transforms[i].Value.c0.xyz);
            if (!_last.TryGetValue(e, out var last)) { _last[e] = position; continue; }
            if (math.distancesq(position.xz, last.xz) < math.square(trails[i].Spacing * scale)) continue;
            _last[e] = position;
            GroundDecals.Place(trails[i].Material.Value, position, trails[i].Size * scale, UnityEngine.Random.Range(0f, 360f), Hold, Fade);
        }
        // Forget units that are gone
        if (_last.Count > _seen.Count)
        {
            var gone = new List<Entity>();
            foreach (var e in _last.Keys) if (!_seen.Contains(e)) gone.Add(e);
            foreach (var e in gone) _last.Remove(e);
        }
    }
}
