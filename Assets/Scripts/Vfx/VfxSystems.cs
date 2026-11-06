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

/// <summary>Last frame's hit flash timer and whether the death effect has played, for units' hit and death effects.</summary>
public struct VfxHitWatch : IComponentData
{
    public float LastFlash;
    public bool Died;
}

/// <summary>
/// Gives skill entities their look through VfxManager without the skill systems knowing: fireballs, barrage bolts and
/// tornadoes get an effect that follows them (facing where they move) and an impact when they disappear; souls an orb; Fists of
/// Thunder hits (single-frame lightning attacks) a burst; the player a whirlwind arc while spinning and a trail while
/// dashing. Units burst when their hit flash starts and when they start dying (UnitVfx picks the effects).
/// In FeedbackSystemGroup, so single-frame attacks are seen before DestroyWithChildrenSystem removes them.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
public partial class VfxSystem : SystemBase
{
    EntityQuery _newProjectiles, _newPeriodic, _newAttacks, _newSouls, _bound, _gone, _unwatched;
    readonly Dictionary<Entity, int> _whirlwinds = new Dictionary<Entity, int>();
    readonly Dictionary<Entity, int> _dashes = new Dictionary<Entity, int>();
    readonly List<Entity> _stale = new List<Entity>();

    protected override void OnCreate()
    {
        _newProjectiles = SystemAPI.QueryBuilder().WithAll<Projectile, LocalTransform>().WithNone<VfxBinding, PeriodicAttack>().Build();
        _newPeriodic = SystemAPI.QueryBuilder().WithAll<PeriodicAttack, LocalTransform>().WithNone<VfxBinding>().Build();
        _newAttacks = SystemAPI.QueryBuilder().WithAll<Attack, LocalTransform>().WithNone<VfxBinding, Projectile>().Build();
        _newSouls = SystemAPI.QueryBuilder().WithAll<Soul, LocalTransform>().WithNone<VfxBinding>().Build();
        _bound = SystemAPI.QueryBuilder().WithAll<VfxBinding, LocalTransform>().Build();
        _gone = SystemAPI.QueryBuilder().WithAll<VfxBinding>().WithNone<LocalTransform>().Build();
        _unwatched = SystemAPI.QueryBuilder().WithAll<HitFlash, LocalTransform>().WithNone<VfxHitWatch, Prefab>().Build();
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
            using (var entities = _newSouls.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                    bindings.Add((e, Bind(vfx, VfxType.SoulOrb, VfxType.None, EntityManager.GetComponentData<LocalTransform>(e))));
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

        if (vfx != null)
        {
            UpdatePlayerEffects(vfx);
            UpdateUnitEffects(vfx);
        }
    }

    // A burst when a unit's hit flash starts, and one when it starts dying (RigidDeath)
    void UpdateUnitEffects(VfxManager vfx)
    {
        EntityManager.AddComponent<VfxHitWatch>(_unwatched);
        var profiles = SystemAPI.GetComponentLookup<UnitVfx>(true);
        var deaths = SystemAPI.GetComponentLookup<RigidDeath>(true);
        foreach (var (flash, transform, watch, entity) in SystemAPI.Query<RefRO<HitFlash>, RefRO<LocalTransform>, RefRW<VfxHitWatch>>().WithEntityAccess())
        {
            var profile = profiles.HasComponent(entity) ? profiles[entity] : new UnitVfx { Hit = VfxType.UnitHit, Death = VfxType.UndeadDeath };
            var position = (Vector3)transform.ValueRO.Position;
            float scale = transform.ValueRO.Scale;
            // Above the body's middle (the root is the capsule's centre, inside the model) so the burst isn't hidden
            if (flash.ValueRO.Timer > watch.ValueRO.LastFlash + 0.001f && !watch.ValueRO.Died)
                vfx.Burst(profile.Hit, position + Vector3.up * 0.8f * scale, Quaternion.identity);
            watch.ValueRW.LastFlash = flash.ValueRO.Timer;
            if (!watch.ValueRO.Died && deaths.HasComponent(entity))
            {
                watch.ValueRW.Died = true;
                vfx.Burst(profile.Death, position + Vector3.up * 0.6f * scale, Quaternion.identity);
            }
        }
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
