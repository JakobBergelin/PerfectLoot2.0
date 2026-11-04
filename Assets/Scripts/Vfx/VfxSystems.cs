using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>An entity with a pooled effect following it. A cleanup component, so its impact can play after the entity is gone.</summary>
public struct VfxBinding : ICleanupComponentData
{
    public int Handle;          // 0: nothing follows (one-shot already played)
    public VfxType Impact;      // Played where the entity was when it goes away
    public float3 Position;
    public quaternion Rotation;
}

/// <summary>
/// Gives skill entities their look through VfxManager without the skill systems knowing: fireballs, barrage bolts and
/// tornadoes get an effect that follows them (facing where they move) and an impact when they disappear; Fists of
/// Thunder hits (single-frame lightning attacks) a burst; the player a whirlwind arc while spinning and a trail while
/// dashing. In FeedbackSystemGroup, so single-frame attacks are seen before DestroyWithChildrenSystem removes them.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
public partial class VfxSystem : SystemBase
{
    EntityQuery _newProjectiles, _newPeriodic, _newAttacks, _bound, _gone;
    readonly Dictionary<Entity, int> _whirlwinds = new Dictionary<Entity, int>();
    readonly Dictionary<Entity, int> _dashes = new Dictionary<Entity, int>();
    readonly List<Entity> _stale = new List<Entity>();

    protected override void OnCreate()
    {
        _newProjectiles = SystemAPI.QueryBuilder().WithAll<Projectile, LocalTransform>().WithNone<VfxBinding, PeriodicAttack>().Build();
        _newPeriodic = SystemAPI.QueryBuilder().WithAll<PeriodicAttack, LocalTransform>().WithNone<VfxBinding>().Build();
        _newAttacks = SystemAPI.QueryBuilder().WithAll<Attack, LocalTransform>().WithNone<VfxBinding, Projectile>().Build();
        _bound = SystemAPI.QueryBuilder().WithAll<VfxBinding, LocalTransform>().Build();
        _gone = SystemAPI.QueryBuilder().WithAll<VfxBinding>().WithNone<LocalTransform>().Build();
    }

    protected override void OnUpdate()
    {
        var vfx = VfxManager.Instance;
        var bindings = new NativeList<(Entity, VfxBinding)>(Allocator.Temp);

        if (vfx != null)
        {
            using (var entities = _newProjectiles.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    var t = EntityManager.GetComponentData<LocalTransform>(e);
                    bool homing = EntityManager.HasComponent<HomingProjectile>(e);
                    bool fire = EntityManager.HasComponent<Attack>(e) && EntityManager.GetComponentData<Attack>(e).Type == DamageType.Fire;
                    var (type, impact) = homing ? (VfxType.BarrageBolt, VfxType.BarrageImpact)
                        : fire ? (VfxType.FireballOrb, VfxType.FireballImpact) : (VfxType.None, VfxType.None);
                    bindings.Add((e, Bind(vfx, type, impact, t)));
                }
            using (var entities = _newPeriodic.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    bool tornado = EntityManager.GetComponentData<PeriodicAttack>(e).Skill == SkillType.Tornado;
                    var t = EntityManager.GetComponentData<LocalTransform>(e);
                    bindings.Add((e, tornado ? Bind(vfx, VfxType.Tornado, VfxType.TornadoEnd, t) : default));
                }
            using (var entities = _newAttacks.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    // Single-frame lightning attacks are Fists of Thunder punches landing
                    var t = EntityManager.GetComponentData<LocalTransform>(e);
                    if (EntityManager.GetComponentData<Attack>(e).Type == DamageType.Lightning)
                        vfx.Burst(VfxType.LightningHit, t.Position, Quaternion.identity);
                    bindings.Add((e, new VfxBinding { Position = t.Position, Rotation = quaternion.identity }));
                }
        }
        foreach (var (entity, binding) in bindings) EntityManager.AddComponentData(entity, binding);

        // Follow, facing the way they move
        foreach (var (binding, transform) in SystemAPI.Query<RefRW<VfxBinding>, RefRO<LocalTransform>>())
        {
            var position = transform.ValueRO.Position;
            var moved = position - binding.ValueRO.Position;
            if (math.lengthsq(moved) > 1e-6f) binding.ValueRW.Rotation = quaternion.LookRotationSafe(math.normalize(moved), math.up());
            binding.ValueRW.Position = position;
            if (vfx != null && binding.ValueRO.Handle != 0) vfx.Move(binding.ValueRO.Handle, position, binding.ValueRO.Rotation);
        }

        // Gone: release, impact where it was, let the entity be removed
        using (var gone = _gone.ToEntityArray(Allocator.Temp))
        {
            foreach (var e in gone)
            {
                var binding = EntityManager.GetComponentData<VfxBinding>(e);
                if (vfx == null) continue;
                if (binding.Handle != 0) vfx.Release(binding.Handle);
                if (binding.Impact != VfxType.None) vfx.Burst(binding.Impact, binding.Position, binding.Rotation);
            }
            EntityManager.RemoveComponent<VfxBinding>(gone);
        }

        if (vfx != null) UpdatePlayerEffects(vfx);
    }

    static VfxBinding Bind(VfxManager vfx, VfxType type, VfxType impact, LocalTransform t) => new VfxBinding
    {
        Handle = type == VfxType.None ? 0 : vfx.Spawn(type, t.Position, Quaternion.identity),
        Impact = impact,
        Position = t.Position,
        Rotation = quaternion.identity,
    };

    // Whirlwind arc while spinning, dash trail (and a burst at the start) while dashing
    void UpdatePlayerEffects(VfxManager vfx)
    {
        foreach (var (whirlwind, transform, entity) in SystemAPI.Query<RefRO<Whirlwind>, RefRO<LocalTransform>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
            Track(vfx, _whirlwinds, entity, whirlwind.ValueRO.Spinning, VfxType.WhirlwindArc, VfxType.None, transform.ValueRO);
        foreach (var (dash, transform, entity) in SystemAPI.Query<RefRO<Dash>, RefRO<LocalTransform>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
            Track(vfx, _dashes, entity, dash.ValueRO.Dashing, VfxType.DashTrail, VfxType.DashBurst, transform.ValueRO);

        foreach (var map in new[] { _whirlwinds, _dashes })
        {
            _stale.Clear();
            foreach (var (entity, handle) in map)
                if (!EntityManager.Exists(entity)) { vfx.Release(handle); _stale.Add(entity); }
            foreach (var entity in _stale) map.Remove(entity);
        }
    }

    static void Track(VfxManager vfx, Dictionary<Entity, int> map, Entity entity, bool on, VfxType type, VfxType startBurst, LocalTransform t)
    {
        bool has = map.TryGetValue(entity, out int handle);
        if (on)
        {
            if (!has)
            {
                map[entity] = handle = vfx.Spawn(type, t.Position, t.Rotation);
                if (startBurst != VfxType.None) vfx.Burst(startBurst, t.Position, t.Rotation);
            }
            vfx.Move(handle, t.Position, t.Rotation);
        }
        else if (has)
        {
            vfx.Release(handle);
            map.Remove(entity);
        }
    }
}
