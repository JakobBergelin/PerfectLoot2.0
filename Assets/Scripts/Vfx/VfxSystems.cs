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

/// <summary>A split projectile (Slingshot) whose spark has played.</summary>
public struct VfxSplitSeen : IComponentData { }

/// <summary>
/// Gives skill entities their look through VfxManager without the skill systems knowing: fireballs, barrage bolts and
/// tornadoes get an effect that follows them (facing where they move) and an impact when they disappear; souls an orb; Fists of
/// Thunder hits (single-frame lightning attacks) a burst; the player a whirlwind arc while spinning and a trail while
/// dashing. Units burst when their hit flash starts and when they start dying (UnitVfx picks the effects). A marker
/// hovers over each Quarry wearer's target. Afflicted units give off small status puffs a few times a second. Portals
/// swirl, and players shimmer as they spawn. Ice shards (Shatter, Frozen Orb) are any non-homing Cold projectile. Crits,
/// dodges and blocks pop where they happen (the combat text's events); players kick up dust as they walk; Mirror Image
/// copies shimmer; Undertow's pulls ripple at the enemy's feet; Slingshot's splits spark. A Charged discharge sends a
/// bolt hopping through the units around it, a little after the damage; Frozen units stand in a block of ice. Bulwark flashes on hits from
/// the front. Loot on the ground has its own, GroundLootVfx.
/// In FeedbackSystemGroup, so single-frame attacks are seen before DestroyWithChildrenSystem removes them.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
public partial class VfxSystem : SystemBase
{
    const float StatusInterval = 0.4f;
    const int StatusCap = 10;   // Per status and interval, so swarms stay cheap
    const int HitCap = 8;       // Crit, dodge, block and ripple bursts per frame, each
    const int StrikeCap = 12;   // Strike effects (slash, shockwave, thrust) per frame
    const float StepLength = 1.2f;  // Meters walked between footstep puffs
    const float FootDepth = 1.05f;  // Meters from a unit's root (its capsule's centre) down to its feet, times its scale

    EntityQuery _newProjectiles, _newPeriodic, _newAttacks, _newSouls, _newBodies, _newPortals, _newMirrors, _newSplits, _bound, _gone, _unwatched, _charged;
    readonly List<Entity> _spawned = new List<Entity>();    // From CharacterManager.PlayerSpawned
    readonly List<Vector3> _crits = new List<Vector3>(), _dodges = new List<Vector3>(), _blocks = new List<Vector3>();   // From the damage systems' events
    readonly Dictionary<Entity, float3> _lastSteps = new Dictionary<Entity, float3>();
    DamagingSystem _damaging;
    CalculateDamageSystem _calculateDamage;
    (EntityQuery Query, VfxType Effect)[] _statuses;
    double _nextStatus;
    readonly Dictionary<Entity, int> _whirlwinds = new Dictionary<Entity, int>();
    readonly Dictionary<Entity, int> _dashes = new Dictionary<Entity, int>();
    readonly Dictionary<Entity, int> _quarryMarks = new Dictionary<Entity, int>();
    readonly List<Entity> _stale = new List<Entity>();

    // Charged: each discharge's bolts, waiting for their moment
    const int ChainLength = 6;      // Units a discharge's bolt hops through
    const int ArcCap = 12;          // New bolts per frame, so swarms stay cheap
    const float HopDelay = 0.06f;   // Seconds between hops
    struct PendingArc { public float At; public Vector3 From, To; }
    readonly List<PendingArc> _arcs = new List<PendingArc>();
    readonly List<Vector3> _chainTargets = new List<Vector3>();
    int _arcsThisFrame;
    EntityQuery _enemyBodies, _friendlyBodies;

    // Frozen: the ice block around each frozen unit
    struct IceBlock { public int Handle; public quaternion Yaw; public Vector3 Position; }
    readonly Dictionary<Entity, IceBlock> _iceBlocks = new Dictionary<Entity, IceBlock>();
    readonly HashSet<Entity> _frozenSeen = new HashSet<Entity>();

    protected override void OnCreate()
    {
        _newProjectiles = SystemAPI.QueryBuilder().WithAll<Projectile, LocalTransform>().WithNone<VfxBinding, PeriodicAttack>().Build();
        _newPeriodic = SystemAPI.QueryBuilder().WithAll<PeriodicAttack, LocalTransform>().WithNone<VfxBinding>().Build();
        _newAttacks = SystemAPI.QueryBuilder().WithAll<Attack, LocalTransform>().WithNone<VfxBinding, Projectile>().Build();
        _newSouls = SystemAPI.QueryBuilder().WithAll<Soul, LocalTransform>().WithNone<VfxBinding>().Build();
        _newBodies = SystemAPI.QueryBuilder().WithAny<FireHydraUnit, TimezoneField>().WithAll<LocalTransform>().WithNone<VfxBinding>().Build();
        _newPortals = SystemAPI.QueryBuilder().WithAll<Portal, LocalTransform>().WithNone<VfxBinding>().Build();
        _newMirrors = SystemAPI.QueryBuilder().WithAll<MirrorImageUnit, LocalTransform>().WithNone<VfxBinding>().Build();
        _newSplits = SystemAPI.QueryBuilder().WithAll<SplitProjectile, LocalTransform>().WithNone<VfxSplitSeen>().Build();
        CharacterManager.PlayerSpawned += OnPlayerSpawned;
        _damaging = World.GetOrCreateSystemManaged<DamagingSystem>();
        _damaging.OnDamageTaken += OnDamageTaken;
        _calculateDamage = World.GetOrCreateSystemManaged<CalculateDamageSystem>();
        _calculateDamage.OnDamageDodged += OnDodged;
        _calculateDamage.OnDamageBlocked += OnBlocked;
        _charged = SystemAPI.QueryBuilder().WithAll<ChargedInstance, LocalTransform>().WithNone<RigidDeath>().Build();
        _enemyBodies = SystemAPI.QueryBuilder().WithAll<Enemy, LocalTransform>().WithNone<RigidDeath>().Build();
        _friendlyBodies = SystemAPI.QueryBuilder().WithAny<Player, Companion>().WithAll<LocalTransform>().WithNone<RigidDeath>().Build();
        _statuses = new[]
        {
            (SystemAPI.QueryBuilder().WithAll<Burning, LocalTransform>().WithNone<RigidDeath>().Build(), VfxType.StatusBurn),
            (SystemAPI.QueryBuilder().WithAll<Poisoned, LocalTransform>().WithNone<RigidDeath>().Build(), VfxType.StatusPoison),
            (SystemAPI.QueryBuilder().WithAll<Chilled, LocalTransform>().WithNone<RigidDeath>().Build(), VfxType.StatusChill),
            (SystemAPI.QueryBuilder().WithAll<Bleeding, LocalTransform>().WithNone<RigidDeath>().Build(), VfxType.StatusBleed),
        };
        _bound = SystemAPI.QueryBuilder().WithAll<VfxBinding, LocalTransform>().Build();
        _gone = SystemAPI.QueryBuilder().WithAll<VfxBinding>().WithNone<LocalTransform>().Build();
        _unwatched = SystemAPI.QueryBuilder().WithAll<HitFlash, LocalTransform>().WithNone<VfxHitWatch, Prefab>().Build();
    }

    protected override void OnDestroy()
    {
        CharacterManager.PlayerSpawned -= OnPlayerSpawned;
        _damaging.OnDamageTaken -= OnDamageTaken;
        _calculateDamage.OnDamageDodged -= OnDodged;
        _calculateDamage.OnDamageBlocked -= OnBlocked;
    }

    void OnPlayerSpawned(Entity player) => _spawned.Add(player);
    void OnDamageTaken(float damage, float3 position, bool isCrit) { if (isCrit && _crits.Count < HitCap) _crits.Add(position); }
    void OnDodged(float3 position) { if (_dodges.Count < HitCap) _dodges.Add(position); }
    void OnBlocked(float amount, float3 position) { if (_blocks.Count < HitCap) _blocks.Add(position); }

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
                    bool poison = EntityManager.HasComponent<Attack>(e) && EntityManager.GetComponentData<Attack>(e).Type == DamageType.Poison;
                    bool cold = EntityManager.HasComponent<Attack>(e) && EntityManager.GetComponentData<Attack>(e).Type == DamageType.Cold;
                    bool attack = EntityManager.HasComponent<Attack>(e);
                    var (type, impact) = EntityManager.HasComponent<HealOnArrival>(e)
                        ? (EntityManager.GetComponentData<HealOnArrival>(e).Label == DamageLabel.ShadowPriest ? (VfxType.DarkHealOrb, VfxType.DarkHealBurst) : (VfxType.HealOrb, VfxType.HealBurst))
                        : homing ? (VfxType.BarrageBolt, VfxType.BarrageImpact)
                        : EntityManager.HasComponent<ShieldProjectile>(e) ? (VfxType.ShieldDisc, VfxType.None)
                        : fire ? (VfxType.FireballOrb, VfxType.FireballImpact)
                        : poison ? (VfxType.PoisonDart, VfxType.None)
                        : cold ? (VfxType.IceShard, VfxType.IceShardImpact)
                        // Everything else that hits (arrows, thrown weapons, lightning) gets a dart tinted by its damage type
                        : attack ? (VfxType.ProjectileStreak, EntityManager.GetComponentData<Attack>(e).Type == DamageType.Lightning ? VfxType.LightningHit : VfxType.SlingshotSpark)
                        : (VfxType.None, VfxType.None);
                    var bound = Bind(vfx, type, impact, t);
                    if (type == VfxType.ProjectileStreak) vfx.Tint(bound.Handle, StrikeColor(EntityManager.GetComponentData<Attack>(e).Type));
                    bindings.Add((e, bound));
                }
            using (var entities = _newPeriodic.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    var attack = EntityManager.GetComponentData<PeriodicAttack>(e);
                    var t = EntityManager.GetComponentData<LocalTransform>(e);
                    var binding = attack.Skill switch
                    {
                        SkillType.Tornado => Bind(vfx, VfxType.Tornado, VfxType.TornadoEnd, t),
                        SkillType.FrozenOrb => Bind(vfx, VfxType.FrozenOrb, VfxType.None, t),
                        SkillType.Flamethrower => Bind(vfx, VfxType.Flame, VfxType.None, t),
                        SkillType.TeslaCoil => Bind(vfx, VfxType.TeslaField, VfxType.None, t),
                        _ => default,
                    };
                    if (attack.Skill == SkillType.TeslaCoil) vfx.Scale(binding.Handle, attack.Radius);
                    bindings.Add((e, binding));
                }
            // A pop when the soul goes, taken or faded
            using (var entities = _newSouls.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                    bindings.Add((e, Bind(vfx, VfxType.SoulOrb, VfxType.SoulPop, EntityManager.GetComponentData<LocalTransform>(e))));
            // Turned like the portal, as its ring faces one way
            using (var entities = _newPortals.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    var t = EntityManager.GetComponentData<LocalTransform>(e);
                    var binding = Bind(vfx, VfxType.PortalSwirl, VfxType.None, t);
                    binding.Rotation = t.Rotation;
                    bindings.Add((e, binding));
                }
            // A shimmer while the copy lasts, and a pop as it comes and as it goes
            using (var entities = _newMirrors.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    var t = EntityManager.GetComponentData<LocalTransform>(e);
                    vfx.Burst(VfxType.MirrorPop, t.Position, Quaternion.identity);
                    bindings.Add((e, Bind(vfx, VfxType.MirrorShimmer, VfxType.MirrorPop, t)));
                }
            // A split makes a copy of the shot with neither one's spark played: the original still has its binding (Instantiate
            // doesn't copy cleanup components), so one spark per split
            using (var entities = _newSplits.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in entities)
                    if (EntityManager.HasComponent<VfxBinding>(e))
                        vfx.Burst(VfxType.SlingshotSpark, EntityManager.GetComponentData<LocalTransform>(e).Position, Quaternion.identity);
                EntityManager.AddComponent<VfxSplitSeen>(_newSplits);
            }
            // Players that spawned since the last update: at their position now
            foreach (var player in _spawned)
                if (EntityManager.Exists(player) && EntityManager.HasComponent<LocalTransform>(player))
                    vfx.Burst(VfxType.PlayerSpawn, EntityManager.GetComponentData<LocalTransform>(player).Position, Quaternion.identity);
            using (var entities = _newBodies.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    var t = EntityManager.GetComponentData<LocalTransform>(e);
                    bool field = EntityManager.HasComponent<TimezoneField>(e);
                    var binding = Bind(vfx, field ? VfxType.TimezoneField : VfxType.FireHydra, VfxType.None, t);
                    if (field) vfx.Scale(binding.Handle, EntityManager.GetComponentData<TimezoneField>(e).Radius);
                    bindings.Add((e, binding));
                }
            int strikes = 0;
            using (var entities = _newAttacks.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    // Single-frame lightning attacks are Fists of Thunder punches landing
                    var t = EntityManager.GetComponentData<LocalTransform>(e);
                    var attack = EntityManager.GetComponentData<Attack>(e);
                    if (attack.Origin.Label == DamageLabel.Consecration)
                    {
                        int nova = vfx.Burst(VfxType.Consecration, t.Position, Quaternion.identity);
                        if (attack.Radius > 0f) vfx.Scale(nova, attack.Radius);
                    }
                    else if (attack.Type == DamageType.Lightning) vfx.Burst(VfxType.LightningHit, t.Position, Quaternion.identity);
                    // A discharge (LightningSystem) hits the charged unit's allies: enemies when it has an enemy target
                    if (attack.Type == DamageType.Lightning && attack.Origin.Label == DamageLabel.Charged)
                    {
                        bool enemies = EntityManager.HasComponent<AttackTarget>(e) && EntityManager.GetComponentData<AttackTarget>(e).TargetType == TargetType.Enemy;
                        QueueChain(t.Position, attack.Radius, enemies);
                    }
                    else if (attack.Type == DamageType.Cold && !EntityManager.HasComponent<AttackShape>(e))
                    {
                        // Sized to the attack's area (Cold Snap's radius); drawn at radius 1
                        int nova = vfx.Burst(VfxType.ColdNova, t.Position, Quaternion.identity);
                        if (attack.Radius > 0f) vfx.Scale(nova, attack.Radius);
                    }
                    // Player-side area attacks land with a strike drawn from their shape, tinted by damage type
                    if (strikes < StrikeCap && attack.Origin.Label != DamageLabel.Consecration && TargetsEnemies(e) && Strike(vfx, e, t, attack)) strikes++;
                    bindings.Add((e, new VfxBinding { Position = t.Position, Rotation = quaternion.identity }));
                }
        }
        _spawned.Clear();
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
            UpdateQuarryMarks(vfx);
            UpdateStatusEffects(vfx);
            UpdateCombatEffects(vfx);
            UpdateArcs(vfx);
            UpdateIceBlocks(vfx);
        }
        _arcsThisFrame = 0;
        _crits.Clear();
        _dodges.Clear();
        _blocks.Clear();
    }

    void UpdateCombatEffects(VfxManager vfx)
    {
        // The events give the unit's root, its capsule's centre
        foreach (var position in _crits) vfx.Burst(VfxType.CritPop, position + Vector3.up * 1.1f, Quaternion.identity);
        foreach (var position in _dodges) vfx.Burst(VfxType.DodgeFlash, position + Vector3.up * 0.2f, Quaternion.identity);
        foreach (var position in _blocks) vfx.Burst(VfxType.BlockFlash, position + Vector3.up * 0.3f, Quaternion.identity);

        double now = SystemAPI.Time.ElapsedTime;

        // Undertow: hits that pulled (a negative HitForce) this frame
        int ripples = 0;
        foreach (var (lastHit, transform) in SystemAPI.Query<RefRO<LastHit>, RefRO<LocalTransform>>().WithAll<Enemy>().WithNone<RigidDeath>())
        {
            if (ripples == HitCap) break;
            if (lastHit.ValueRO.Time != now || lastHit.ValueRO.HitForce >= 0f) continue;
            vfx.Burst(VfxType.UndertowRipple, Feet(transform.ValueRO), Quaternion.identity);
            ripples++;
        }

        // Bulwark: hits from in front this frame, the way BulwarkSystem tells (the hit goes away from its attacker)
        foreach (var (lastHit, transform) in SystemAPI.Query<RefRO<LastHit>, RefRO<LocalTransform>>().WithAll<Bulwark>())
        {
            if (lastHit.ValueRO.Time != now) continue;
            if (math.dot(-lastHit.ValueRO.Direction.xz, transform.ValueRO.Forward().xz) <= 0f) continue;
            vfx.Burst(VfxType.BulwarkShimmer, transform.ValueRO.Position, transform.ValueRO.Rotation);
        }

        // Footsteps: a puff every StepLength walked; none while dashing, nor after a jump (teleport, respawn)
        var dashes = SystemAPI.GetComponentLookup<Dash>(true);
        foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Player>().WithEntityAccess())
        {
            var position = transform.ValueRO.Position;
            bool dashing = dashes.HasComponent(entity) && dashes[entity].Dashing;
            if (!_lastSteps.TryGetValue(entity, out var last) || dashing || math.distance(last.xz, position.xz) > StepLength * 4f)
            {
                _lastSteps[entity] = position;
                continue;
            }
            if (math.distance(last.xz, position.xz) < StepLength) continue;
            vfx.Burst(VfxType.FootstepDust, Feet(transform.ValueRO), Quaternion.identity);
            _lastSteps[entity] = position;
        }
        _stale.Clear();
        foreach (var entity in _lastSteps.Keys)
            if (!EntityManager.Exists(entity)) _stale.Add(entity);
        foreach (var entity in _stale) _lastSteps.Remove(entity);
    }

    bool TargetsEnemies(Entity e) => EntityManager.HasComponent<AttackTarget>(e) && EntityManager.GetComponentData<AttackTarget>(e).TargetType == TargetType.Enemy;

    /// <summary>
    /// The generic look of an area attack: a slash arc for a cone (its reach), a thrust streak for a rectangle (its length),
    /// a shockwave for a round attack of 2 m or more. Lightning and Cold round attacks keep their own bursts.
    /// </summary>
    bool Strike(VfxManager vfx, Entity e, LocalTransform t, Attack attack)
    {
        int handle;
        if (EntityManager.HasComponent<AttackShape>(e))
        {
            var shape = EntityManager.GetComponentData<AttackShape>(e);
            bool cone = shape.Kind == AttackShapeKind.Cone;
            handle = vfx.Burst(cone ? VfxType.StrikeSlash : VfxType.StrikeThrust, t.Position, t.Rotation);
            vfx.Scale(handle, cone ? math.max(attack.Radius, 0.5f) : math.max(shape.HalfExtents.z, 0.5f));
        }
        else if (attack.Radius >= 2f && attack.Type != DamageType.Cold && attack.Type != DamageType.Lightning)
        {
            handle = vfx.Burst(VfxType.StrikeShock, t.Position, Quaternion.identity);
            vfx.Scale(handle, attack.Radius);
        }
        else return false;
        vfx.Tint(handle, StrikeColor(attack.Type));
        return true;
    }

    static Color StrikeColor(DamageType type) => type switch
    {
        DamageType.Fire => new Color(1f, 0.48f, 0.16f),
        DamageType.Cold => new Color(0.62f, 0.9f, 1f),
        DamageType.Poison => new Color(0.55f, 1f, 0.23f),
        DamageType.Lightning => new Color(1f, 0.91f, 0.35f),
        _ => new Color(0.91f, 0.93f, 0.97f),
    };

    static Vector3 Feet(LocalTransform t) => t.Position - new float3(0f, FootDepth * t.Scale, 0f);

    // One-shot puffs rather than an effect per unit: nothing to track, and the cap bounds the cost in a swarm
    void UpdateStatusEffects(VfxManager vfx)
    {
        double now = SystemAPI.Time.ElapsedTime;
        if (now < _nextStatus) return;
        _nextStatus = now + StatusInterval;

        foreach (var (query, effect) in _statuses)
            using (var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < math.min(transforms.Length, StatusCap); i++)
                    StatusPuff(vfx, effect, transforms[i]);

        int puffs = 0;
        using (var entities = _charged.ToEntityArray(Allocator.Temp))
            foreach (var e in entities)
            {
                if (puffs == StatusCap) break;
                if (EntityManager.GetBuffer<ChargedInstance>(e, true).Length == 0) continue;
                StatusPuff(vfx, VfxType.StatusCharged, EntityManager.GetComponentData<LocalTransform>(e));
                puffs++;
            }
    }

    static void StatusPuff(VfxManager vfx, VfxType effect, LocalTransform t) =>
        vfx.Burst(effect, (Vector3)t.Position + Vector3.up * 0.8f * t.Scale, Quaternion.identity);

    // Keyed by the wearer: the marker jumps to the next target, and goes with the passive or the wearer
    void UpdateQuarryMarks(VfxManager vfx)
    {
        var transforms = SystemAPI.GetComponentLookup<LocalTransform>(true);
        foreach (var (quarry, entity) in SystemAPI.Query<RefRO<Quarry>>().WithEntityAccess())
        {
            var target = quarry.ValueRO.Target;
            bool marked = transforms.HasComponent(target);
            var t = marked ? transforms[target] : default;
            t.Position.y += 1.6f * t.Scale;  // Over the head: the root is the capsule's centre
            Track(vfx, _quarryMarks, entity, marked, VfxType.QuarryMark, VfxType.None, t);
        }

        _stale.Clear();
        foreach (var (entity, handle) in _quarryMarks)
            if (!EntityManager.HasComponent<Quarry>(entity)) { vfx.Release(handle); _stale.Add(entity); }
        foreach (var entity in _stale) _quarryMarks.Remove(entity);
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

    // A discharge's bolt: from the charged unit to the nearest unit in reach, from there to the nearest one left, and so
    // on. Each hop a little after the last, so it reads as lightning jumping through the pack (the damage is already done).
    void QueueChain(float3 origin, float radius, bool enemies)
    {
        if (_arcsThisFrame >= ArcCap || radius <= 0f) return;
        _chainTargets.Clear();
        using (var transforms = (enemies ? _enemyBodies : _friendlyBodies).ToComponentDataArray<LocalTransform>(Allocator.Temp))
            foreach (var t in transforms)
            {
                float distance = math.distance(origin.xz, t.Position.xz);
                // The charged unit itself stands where the discharge is
                if (distance > 0.3f && distance <= radius) _chainTargets.Add(t.Position);
            }

        Vector3 from = origin;
        float at = UnityEngine.Time.time;
        for (int hop = 0; hop < ChainLength && _chainTargets.Count > 0 && _arcsThisFrame < ArcCap; hop++)
        {
            int nearest = 0;
            for (int i = 1; i < _chainTargets.Count; i++)
                if ((_chainTargets[i] - from).sqrMagnitude < (_chainTargets[nearest] - from).sqrMagnitude) nearest = i;
            var to = _chainTargets[nearest];
            _chainTargets.RemoveAt(nearest);
            at += HopDelay;
            _arcs.Add(new PendingArc { At = at, From = from, To = to });
            _arcsThisFrame++;
            from = to;
        }
    }

    void UpdateArcs(VfxManager vfx)
    {
        for (int i = _arcs.Count - 1; i >= 0; i--)
        {
            var arc = _arcs[i];
            if (UnityEngine.Time.time < arc.At) continue;
            vfx.Arc(arc.From, arc.To);
            vfx.Burst(VfxType.LightningHit, arc.To, Quaternion.identity);
            _arcs.RemoveAt(i);
        }
    }

    // A block of ice around every frozen unit, sized to it and turned a random way; it cracks apart when the unit thaws or dies
    void UpdateIceBlocks(VfxManager vfx)
    {
        _frozenSeen.Clear();
        foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Frozen>().WithNone<RigidDeath>().WithEntityAccess())
        {
            var t = transform.ValueRO;
            if (!_iceBlocks.TryGetValue(entity, out var block))
            {
                block.Yaw = quaternion.RotateY(UnityEngine.Random.Range(0f, 2f * math.PI));
                block.Handle = vfx.Spawn(VfxType.IceBlock, t.Position, block.Yaw);
                vfx.ScaleUniform(block.Handle, t.Scale);
            }
            block.Position = t.Position;
            vfx.Move(block.Handle, t.Position, block.Yaw);
            _iceBlocks[entity] = block;
            _frozenSeen.Add(entity);
        }

        _stale.Clear();
        foreach (var (entity, block) in _iceBlocks)
        {
            if (_frozenSeen.Contains(entity)) continue;
            vfx.Release(block.Handle);
            vfx.Burst(VfxType.IceShardImpact, block.Position, Quaternion.identity);
            _stale.Add(entity);
        }
        foreach (var entity in _stale) _iceBlocks.Remove(entity);
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
