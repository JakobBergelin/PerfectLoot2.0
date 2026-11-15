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
    public Entity Proxy;        // On the host: the NetVfx stand-in that shows it on the other machines, or Entity.Null
    public float Scale;         // Above 0: the effect's size, sideways or (Uniform) every way; its impact gets the same
    public bool Uniform;        // Sized every way, each frame, to its entity's size (a growing flame, a wall)
    public bool Oriented;       // Turned like its entity (a wall) instead of facing where it moves
    public bool Hostile;        // The host: a projectile aimed at players, to see it turned or blocked
}

/// <summary>Last frame's hit flash timer and whether the death effect has played, for units' hit and death effects.</summary>
public struct VfxHitWatch : IComponentData
{
    public float LastFlash;
    public bool Died;
}

/// <summary>A split projectile (Slingshot) whose spark has played.</summary>
public struct VfxSplitSeen : IComponentData { }

/// <summary>An attack whose strike look has played (VfxSystem).</summary>
public struct VfxAttackSeen : IComponentData { }

/// <summary>A falling meteor whose rock has been sent down (VfxSystem).</summary>
public struct VfxMeteorRock : IComponentData { }

/// <summary>A companion whose arrival burst has played (VfxSystem).</summary>
public struct VfxArrivalSeen : IComponentData { }

/// <summary>
/// Gives skill entities their look through VfxManager without the skill systems knowing: projectiles (fireballs, barrage
/// bolts, arrows, Cursed Hammers, Captain's Shields), periodic attacks (tornadoes, Frozen Orbs, flames, Plague Arrows) and
/// fields get an effect that follows them (facing where they move) and an impact when they disappear; growing flames are
/// sized to their radius every frame. Meteors show their landing ring, a rock falling in for the last MeteorFallTime and
/// burning ground; Upheaval's quake its warning; Earth Bending's wall stands where it is, turned and sized like it, and
/// breaks. Souls get an orb, portals swirl, players shimmer as they spawn and companions as they arrive.
/// Attacks land with a strike drawn from their shape, or a look of their own for some skills (SkillStrike). Turned and
/// blocked shots spark, Captain's Shield sparks on each hit.
/// Players: effects that follow them while a state or aura is on (PlayerLooks, from NetPlayerActions), their stance's aura,
/// the hook and its chain, a burst for each skill use (CastLook) and a puff where they vanish and appear (Teleport, Phantom
/// Edge, Shadowstep's jumps). Units burst when their hit flash starts and when they start dying (UnitVfx picks the effects).
/// A marker hovers over each Quarry wearer's target and over Poison Dart's marks. Afflicted units give off small status
/// puffs a few times a second. Ice shards (Shatter, Frozen Orb) are any non-homing Cold projectile. Crits, dodges and blocks
/// pop where they happen (the combat text's events); players kick up dust as they walk; Mirror Image copies shimmer;
/// Undertow's pulls ripple at the enemy's feet; Slingshot's splits spark. A Charged discharge sends a bolt hopping through
/// the units around it, a little after the damage; Frozen units stand in a block of ice. Bulwark flashes on hits from the
/// front. Loot on the ground has its own, GroundLootVfx.
/// Every machine sees the same: host-only things through NetVfx stand-ins and SharedBurst, players through
/// NetPlayerActions, units through NetUnitFeedback.
/// In FeedbackSystemGroup, so single-frame attacks are seen before DestroyWithChildrenSystem removes them.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
public partial class VfxSystem : SystemBase
{
    /// <summary>Seconds a meteor's rock takes to fall in: it's sent down this long before the impact (the prefab's VfxSlide).</summary>
    public const float MeteorFallTime = 0.6f;

    const float StatusInterval = 0.4f;
    const int StatusCap = 10;   // Per status and interval, so swarms stay cheap
    const int HitCap = 8;       // Crit, dodge, block and ripple bursts per frame, each
    const int StrikeCap = 12;   // Strike effects (slash, shockwave, thrust) per frame
    const int MarkCap = 24;     // Poison marks shown at once
    const float StepLength = 1.2f;  // Meters walked between footstep puffs
    const float FootDepth = 1.05f;  // Meters from a unit's root (its capsule's centre) down to its feet, times its scale
    const float JumpDistance = 1.5f;    // Meters in one frame that count as a jump (Shadowstep), for its puffs

    // Effects that follow a player while one of its states or auras is on (NetPlayerActions), at its root and turned with
    // it; Start plays once as it comes on. Spin: degrees a second it turns on its own instead (Gale Ward's band, the other
    // way while GaleWardReversed).
    static readonly (ushort States, byte Auras, VfxType Effect, VfxType Start, float Spin)[] PlayerLooks =
    {
        (NetPlayerActions.Spinning, 0, VfxType.WhirlwindArc, VfxType.None, 0f),
        (NetPlayerActions.Dashing, 0, VfxType.DashTrail, VfxType.DashBurst, 0f),
        (NetPlayerActions.Flying, 0, VfxType.BallLightningBody, VfxType.LightningHit, 0f),
        (NetPlayerActions.Rolling, 0, VfxType.SonicBallBody, VfxType.None, 0f),
        (NetPlayerActions.Guarding, 0, VfxType.ShieldWallArc, VfxType.None, 0f),
        (0, NetPlayerActions.Invulnerable, VfxType.InvulnerableBubble, VfxType.None, 0f),
        (0, NetPlayerActions.Immolating, VfxType.ImmolationAura, VfxType.None, 0f),
        (0, NetPlayerActions.BarrageOn, VfxType.BarrageAura, VfxType.None, 0f),
        (0, NetPlayerActions.Empowered | NetPlayerActions.Shadowed, VfxType.EmpoweredAura, VfxType.None, 0f),
        (0, NetPlayerActions.GaleWard, VfxType.GaleWardBand, VfxType.None, 120f),
    };

    EntityQuery _newProjectiles, _newPeriodic, _newAttacks, _newSouls, _newBodies, _newPortals, _newMirrors, _newSplits, _newStandIns, _bound, _gone, _unwatched, _charged;
    EntityQuery _newDelayed, _newMeteors, _fallingMeteors, _newHazards, _newWalls, _newCompanions;
    readonly List<Entity> _spawned = new List<Entity>();    // From CharacterManager.PlayerSpawned
    readonly List<Vector3> _crits = new List<Vector3>(), _dodges = new List<Vector3>(), _blocks = new List<Vector3>();   // From the damage systems' events
    readonly Dictionary<Entity, float3> _lastSteps = new Dictionary<Entity, float3>();
    DamagingSystem _damaging;
    CalculateDamageSystem _calculateDamage;
    NetFeedbackSendSystem _send;        // The host: its one-shots for the other machines
    NetFeedbackReceiveSystem _receive;  // Someone else's game: the host's events and one-shots
    readonly List<NetFeedbackRpc> _received = new List<NetFeedbackRpc>();
    (EntityQuery Query, VfxType Effect)[] _statuses;
    double _nextStatus;
    readonly List<Entity> _stale = new List<Entity>();
    readonly HashSet<Entity> _present = new HashSet<Entity>();

    // Players: one map per PlayerLooks row, then the rest, each player's effect handle
    readonly Dictionary<Entity, int>[] _playerLooks = new Dictionary<Entity, int>[PlayerLooks.Length];
    readonly Dictionary<Entity, int> _hookHeads = new Dictionary<Entity, int>(), _hookChains = new Dictionary<Entity, int>();
    readonly Dictionary<Entity, int> _quarryMarks = new Dictionary<Entity, int>(), _poisonMarks = new Dictionary<Entity, int>();
    readonly Dictionary<Entity, (int Handle, byte Stance)> _stances = new Dictionary<Entity, (int, byte)>();
    struct PlayerSeen { public byte Casts; public ushort States; public float3 Position; }
    readonly Dictionary<Entity, PlayerSeen> _players = new Dictionary<Entity, PlayerSeen>();

    // The host: shots turned or blocked, and Captain's Shield hits, this frame (from the binding pass)
    readonly List<(float3 Position, quaternion Rotation)> _deflects = new List<(float3, quaternion)>(), _shieldHits = new List<(float3, quaternion)>();

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
        _newAttacks = SystemAPI.QueryBuilder().WithAll<Attack, LocalTransform>().WithNone<VfxAttackSeen, Projectile>().Build();
        _newSouls = SystemAPI.QueryBuilder().WithAll<Soul, LocalTransform>().WithNone<VfxBinding>().Build();
        _newBodies = SystemAPI.QueryBuilder().WithAny<ElementalHydraUnit, TimezoneField>().WithAll<LocalTransform>().WithNone<VfxBinding>().Build();
        _newPortals = SystemAPI.QueryBuilder().WithAll<Portal, LocalTransform>().WithNone<VfxBinding>().Build();
        _newMirrors = SystemAPI.QueryBuilder().WithAll<MirrorImageUnit, LocalTransform>().WithNone<VfxBinding>().Build();
        _newStandIns = SystemAPI.QueryBuilder().WithAll<NetVfx, LocalTransform>().WithNone<VfxBinding>().Build();
        _newSplits = SystemAPI.QueryBuilder().WithAll<SplitProjectile, LocalTransform>().WithNone<VfxSplitSeen>().Build();
        _newDelayed = SystemAPI.QueryBuilder().WithAll<DelayedAttack, LocalTransform>().WithNone<VfxBinding>().Build();
        _newMeteors = SystemAPI.QueryBuilder().WithAll<MeteorStrike, LocalTransform>().WithNone<VfxBinding>().Build();
        _fallingMeteors = SystemAPI.QueryBuilder().WithAll<MeteorStrike, LocalTransform>().WithNone<VfxMeteorRock>().Build();
        _newHazards = SystemAPI.QueryBuilder().WithAll<Hazard, LocalTransform>().WithNone<VfxBinding>().Build();
        _newWalls = SystemAPI.QueryBuilder().WithAll<EarthWall, LocalTransform>().WithNone<VfxBinding>().Build();
        // Companion: every machine's (NetUnitSourceSystem adds it on the others)
        _newCompanions = SystemAPI.QueryBuilder().WithAll<Companion, LocalTransform>().WithNone<VfxArrivalSeen, MirrorImageUnit>().Build();
        CharacterManager.PlayerSpawned += OnPlayerSpawned;
        _damaging = World.GetOrCreateSystemManaged<DamagingSystem>();
        _damaging.OnDamageTaken += OnDamageTaken;
        _calculateDamage = World.GetOrCreateSystemManaged<CalculateDamageSystem>();
        _calculateDamage.OnDamageDodged += OnDodged;
        _calculateDamage.OnDamageBlocked += OnBlocked;
        _send = World.GetOrCreateSystemManaged<NetFeedbackSendSystem>();
        _receive = World.GetOrCreateSystemManaged<NetFeedbackReceiveSystem>();
        _receive.OnDamageTaken += OnDamageTaken;
        _receive.OnDamageDodged += OnDodged;
        _receive.OnDamageBlocked += OnBlocked;
        _receive.OnBurst += OnReceivedBurst;
        // Charged and Poison Dart's marks: status bits set by the host (NetUnitFeedback), on every machine
        _charged = SystemAPI.QueryBuilder().WithAll<NetUnitFeedback, LocalTransform>().WithNone<RigidDeath>().Build();
        _enemyBodies = SystemAPI.QueryBuilder().WithAll<Enemy, LocalTransform>().WithNone<RigidDeath>().Build();
        // NetPlayer: every machine's players, on every machine
        _friendlyBodies = SystemAPI.QueryBuilder().WithAny<NetPlayer, Companion>().WithAll<LocalTransform>().WithNone<RigidDeath>().Build();
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
        for (int i = 0; i < _playerLooks.Length; i++) _playerLooks[i] = new Dictionary<Entity, int>();
    }

    protected override void OnDestroy()
    {
        CharacterManager.PlayerSpawned -= OnPlayerSpawned;
        _damaging.OnDamageTaken -= OnDamageTaken;
        _calculateDamage.OnDamageDodged -= OnDodged;
        _calculateDamage.OnDamageBlocked -= OnBlocked;
        _receive.OnDamageTaken -= OnDamageTaken;
        _receive.OnDamageDodged -= OnDodged;
        _receive.OnDamageBlocked -= OnBlocked;
        _receive.OnBurst -= OnReceivedBurst;
    }

    void OnPlayerSpawned(Entity player) => _spawned.Add(player);
    void OnDamageTaken(float damage, float3 position, bool isCrit) { if (isCrit && _crits.Count < HitCap) _crits.Add(position); }
    void OnDodged(float3 position) { if (_dodges.Count < HitCap) _dodges.Add(position); }
    void OnBlocked(float amount, float3 position) { if (_blocks.Count < HitCap) _blocks.Add(position); }
    void OnReceivedBurst(NetFeedbackRpc feedback) => _received.Add(feedback);

    protected override void OnUpdate()
    {
        var vfx = VfxManager.Instance;
        var bindings = new NativeList<(Entity, VfxBinding)>(Allocator.Temp);
        // The looks of effects that follow what only the host has, for their stand-ins (made below, on the host)
        var standIns = new NativeList<(Entity, NetVfx)>(Allocator.Temp);

        if (vfx != null)
        {
            using (var entities = _newProjectiles.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    var t = EntityManager.GetComponentData<LocalTransform>(e);
                    bool attack = EntityManager.HasComponent<Attack>(e);
                    var damageType = attack ? EntityManager.GetComponentData<Attack>(e).Type : DamageType.Physical;
                    var (type, impact) = EntityManager.HasComponent<HealOnArrival>(e)
                        ? (EntityManager.GetComponentData<HealOnArrival>(e).Label == DamageLabel.ShadowPriest ? (VfxType.DarkHealOrb, VfxType.DarkHealBurst) : (VfxType.HealOrb, VfxType.HealBurst))
                        : EntityManager.HasComponent<HomingProjectile>(e) ? (VfxType.BarrageBolt, VfxType.BarrageImpact)
                        : EntityManager.HasComponent<ShieldProjectile>(e) ? (VfxType.ShieldDisc, VfxType.None)
                        : EntityManager.HasComponent<CursedHammerProjectile>(e) ? (VfxType.CursedHammer, VfxType.None)   // Its bursts are attacks of their own
                        : !attack ? (VfxType.None, VfxType.None)
                        : damageType == DamageType.Fire ? (VfxType.FireballOrb, VfxType.FireballImpact)
                        : damageType == DamageType.Poison ? (VfxType.PoisonDart, VfxType.None)
                        : damageType == DamageType.Cold ? (VfxType.IceShard, VfxType.IceShardImpact)
                        : damageType == DamageType.Physical ? (VfxType.Arrow, VfxType.SlingshotSpark)     // Multishot's and archers' arrows
                        // Everything else that hits (lightning, thrown things) gets a dart tinted by its damage type
                        : (VfxType.ProjectileStreak, damageType == DamageType.Lightning ? VfxType.LightningHit : VfxType.SlingshotSpark);
                    var look = new NetVfx { Type = type, Impact = impact };
                    if (type == VfxType.ProjectileStreak)
                    {
                        look.Tinted = true;
                        look.Tint = damageType;
                    }
                    var binding = Bind(vfx, look, t);
                    binding.Hostile = attack && EntityManager.HasComponent<AttackTarget>(e) && EntityManager.GetComponentData<AttackTarget>(e).TargetType == TargetType.Player;
                    bindings.Add((e, binding));
                    standIns.Add((e, look));
                }
            using (var entities = _newPeriodic.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    var attack = EntityManager.GetComponentData<PeriodicAttack>(e);
                    var t = EntityManager.GetComponentData<LocalTransform>(e);
                    var look = attack.Skill switch
                    {
                        SkillType.Tornado => new NetVfx { Type = VfxType.Tornado, Impact = VfxType.TornadoEnd },
                        SkillType.FrozenOrb => new NetVfx { Type = VfxType.FrozenOrb, Impact = VfxType.FrozenOrbEnd },
                        // Drawn at radius 1, sized every frame to the flame's growing radius; embers where it dies
                        SkillType.Flamethrower => new NetVfx { Type = VfxType.Flame, Impact = VfxType.FlameEmbers, Scale = attack.Radius, Uniform = true },
                        SkillType.TeslaCoil => new NetVfx { Type = VfxType.TeslaField, Scale = attack.Radius },
                        SkillType.PlagueArrow => new NetVfx { Type = VfxType.PlagueArrow },
                        _ => default,
                    };
                    bindings.Add((e, Bind(vfx, look, t)));
                    standIns.Add((e, look));
                }
            // Waiting attacks: Upheaval's quake shows where it will heave, sized to its radius once the area of effect applies
            using (var entities = _newDelayed.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    var delayed = EntityManager.GetComponentData<DelayedAttack>(e);
                    var look = delayed.Attack.Origin.Label == DamageLabel.Upheaval
                        ? new NetVfx { Type = VfxType.UpheavalWarning, Scale = delayed.Attack.Radius * AreaOf(e) }
                        : default;
                    bindings.Add((e, Bind(vfx, look, EntityManager.GetComponentData<LocalTransform>(e))));
                    standIns.Add((e, look));
                }
            // Meteors: the ring where each will land (its impact is an attack later, with its own look below)
            using (var entities = _newMeteors.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    var look = new NetVfx { Type = VfxType.MeteorWarning, Scale = EntityManager.GetComponentData<MeteorStrike>(e).Radius * AreaOf(e) };
                    bindings.Add((e, Bind(vfx, look, EntityManager.GetComponentData<LocalTransform>(e))));
                    standIns.Add((e, look));
                }
            // ...its rock, falling in for the last MeteorFallTime
            using (var entities = _fallingMeteors.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    if (EntityManager.GetComponentData<MeteorStrike>(e).Delay > MeteorFallTime) continue;
                    SharedBurst(vfx, VfxType.MeteorRock, EntityManager.GetComponentData<LocalTransform>(e).Position, quaternion.identity);
                    EntityManager.AddComponent<VfxMeteorRock>(e);
                }
            // ...and the burning ground it leaves
            using (var entities = _newHazards.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    var hazard = EntityManager.GetComponentData<Hazard>(e);
                    var look = hazard.Origin == DamageLabel.Meteor ? new NetVfx { Type = VfxType.BurningGround, Scale = hazard.Radius } : default;
                    bindings.Add((e, Bind(vfx, look, EntityManager.GetComponentData<LocalTransform>(e))));
                    standIns.Add((e, look));
                }
            // Earth Bending's wall: turned and sized like it (drawn for scale 1, its feet at its root), breaking when it goes
            using (var entities = _newWalls.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    var t = EntityManager.GetComponentData<LocalTransform>(e);
                    var look = new NetVfx { Type = VfxType.EarthWall, Impact = VfxType.EarthWallBreak, Scale = t.Scale, Uniform = true, Oriented = true };
                    bindings.Add((e, Bind(vfx, look, t)));
                    standIns.Add((e, look));
                }
            // A pop when the soul goes, taken or faded
            using (var entities = _newSouls.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    var look = new NetVfx { Type = VfxType.SoulOrb, Impact = VfxType.SoulPop };
                    bindings.Add((e, Bind(vfx, look, EntityManager.GetComponentData<LocalTransform>(e))));
                    standIns.Add((e, look));
                }
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
                    SharedBurst(vfx, VfxType.MirrorPop, t.Position, quaternion.identity);
                    var look = new NetVfx { Type = VfxType.MirrorShimmer, Impact = VfxType.MirrorPop };
                    bindings.Add((e, Bind(vfx, look, t)));
                    standIns.Add((e, look));
                }
            // A split makes a copy of the shot with neither one's spark played: the original still has its binding (Instantiate
            // doesn't copy cleanup components), so one spark per split
            using (var entities = _newSplits.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in entities)
                    if (EntityManager.HasComponent<VfxBinding>(e))
                        SharedBurst(vfx, VfxType.SlingshotSpark, EntityManager.GetComponentData<LocalTransform>(e).Position, quaternion.identity);
                EntityManager.AddComponent<VfxSplitSeen>(_newSplits);
            }
            // Players that spawned since the last update: at their position now
            foreach (var player in _spawned)
                if (EntityManager.Exists(player) && EntityManager.HasComponent<LocalTransform>(player))
                    vfx.Burst(VfxType.PlayerSpawn, EntityManager.GetComponentData<LocalTransform>(player).Position, Quaternion.identity);
            // Companions as they arrive, on every machine (their ghosts reach the others)
            using (var transforms = _newCompanions.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                foreach (var t in transforms) vfx.Burst(VfxType.CompanionSpawn, t.Position, t.Rotation);
                EntityManager.AddComponent<VfxArrivalSeen>(_newCompanions);
            }
            using (var entities = _newBodies.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    var t = EntityManager.GetComponentData<LocalTransform>(e);
                    var look = EntityManager.HasComponent<TimezoneField>(e)
                        ? new NetVfx { Type = VfxType.TimezoneField, Scale = EntityManager.GetComponentData<TimezoneField>(e).Radius }
                        : new NetVfx { Type = VfxType.ElementalHydra };
                    bindings.Add((e, Bind(vfx, look, t)));
                    standIns.Add((e, look));
                }
            // In someone else's game: the host's stand-ins for what only it has; their impact plays where they go away
            if (!NetSession.IsHost)
                using (var entities = _newStandIns.ToEntityArray(Allocator.Temp))
                    foreach (var e in entities)
                        bindings.Add((e, Bind(vfx, EntityManager.GetComponentData<NetVfx>(e), EntityManager.GetComponentData<LocalTransform>(e))));
            int strikes = 0;
            using (var entities = _newAttacks.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in entities)
                {
                    // Single-frame lightning attacks are Fists of Thunder punches landing
                    var t = EntityManager.GetComponentData<LocalTransform>(e);
                    var attack = EntityManager.GetComponentData<Attack>(e);
                    if (attack.Origin.Label == DamageLabel.Consecration)
                        SharedBurst(vfx, VfxType.Consecration, t.Position, quaternion.identity, math.max(attack.Radius, 0f));
                    else if (attack.Type == DamageType.Lightning) SharedBurst(vfx, VfxType.LightningHit, t.Position, quaternion.identity);
                    // A discharge (LightningSystem) hits the charged unit's allies: enemies when it has an enemy target
                    if (attack.Type == DamageType.Lightning && attack.Origin.Label == DamageLabel.Charged)
                    {
                        bool enemies = EntityManager.HasComponent<AttackTarget>(e) && EntityManager.GetComponentData<AttackTarget>(e).TargetType == TargetType.Enemy;
                        QueueChain(t.Position, attack.Radius, enemies);
                        _send.Chain(t.Position, attack.Radius, enemies);
                    }
                    else if (attack.Type == DamageType.Cold && !EntityManager.HasComponent<AttackShape>(e))
                    {
                        // Sized to the attack's area (Cold Snap's radius); drawn at radius 1
                        SharedBurst(vfx, VfxType.ColdNova, t.Position, quaternion.identity, math.max(attack.Radius, 0f));
                    }
                    // Player-side area attacks land with a look of their own skill's, or a strike drawn from their shape, tinted by damage type
                    if (strikes < StrikeCap && attack.Origin.Label != DamageLabel.Consecration && TargetsEnemies(e) && (SkillStrike(vfx, e, t, attack) || Strike(vfx, e, t, attack))) strikes++;
                }
                EntityManager.AddComponent<VfxAttackSeen>(_newAttacks);
            }
        }
        _spawned.Clear();
        foreach (var (entity, binding) in bindings) EntityManager.AddComponentData(entity, binding);
        if (NetSession.IsHost) MakeStandIns(standIns);

        // Follow, facing the way they move (or turned like their entity); the stand-ins follow along. On the host, shots
        // that stop being aimed at players (turned, or blocked: their Attack taken away) spark, and Captain's Shields on hits.
        var standInMoves = new NativeList<(Entity, LocalTransform)>(Allocator.Temp);
        var growing = SystemAPI.GetComponentLookup<GrowingFlame>(true);
        var periodic = SystemAPI.GetComponentLookup<PeriodicAttack>(true);
        var projectiles = SystemAPI.GetComponentLookup<Projectile>(true);
        var attacks = SystemAPI.GetComponentLookup<Attack>(true);
        var targets = SystemAPI.GetComponentLookup<AttackTarget>(true);
        var shields = SystemAPI.GetComponentLookup<ShieldProjectile>(true);
        var looks = SystemAPI.GetComponentLookup<NetVfx>(true);
        foreach (var (binding, transform, entity) in SystemAPI.Query<RefRW<VfxBinding>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            ref var b = ref binding.ValueRW;
            var t = transform.ValueRO;
            if (b.Oriented) b.Rotation = t.Rotation;
            else
            {
                var moved = t.Position - b.Position;
                if (math.lengthsq(moved) > 1e-6f) b.Rotation = quaternion.LookRotationSafe(math.normalize(moved), math.up());
            }
            b.Position = t.Position;
            if (b.Uniform)
            {
                // A growing flame's radius on the host, its stand-in's scale on the other machines (or a wall's scale).
                // A stand-in never goes below the size it was made with: a ghost's transform can arrive a frame late (0),
                // and a burst emitted at scale 0 never shows.
                float size = growing.HasComponent(entity) && periodic.HasComponent(entity) ? periodic[entity].Radius
                    : looks.HasComponent(entity) ? math.max(t.Scale, looks[entity].Scale) : t.Scale;
                if (size > 1e-3f && math.abs(size - b.Scale) > 1e-3f)
                {
                    b.Scale = size;
                    if (vfx != null) vfx.ScaleUniform(b.Handle, size);
                }
            }
            if (vfx != null && b.Handle != 0) vfx.Move(b.Handle, t.Position, b.Rotation);
            if (b.Proxy != Entity.Null)
                standInMoves.Add((b.Proxy, new LocalTransform { Position = t.Position, Rotation = b.Oriented ? b.Rotation : quaternion.identity, Scale = b.Uniform ? b.Scale : 1f }));

            if (b.Hostile && projectiles.HasComponent(entity) && (!attacks.HasComponent(entity) || (targets.HasComponent(entity) && targets[entity].TargetType != TargetType.Player)))
            {
                b.Hostile = false;
                _deflects.Add((t.Position, b.Rotation));
            }
            if (shields.HasComponent(entity) && attacks.HasComponent(entity) && attacks[entity].Events.OnHit) _shieldHits.Add((t.Position, b.Rotation));
        }
        foreach (var (standIn, transform) in standInMoves)
            if (EntityManager.HasComponent<LocalTransform>(standIn)) EntityManager.SetComponentData(standIn, transform);
        if (vfx != null)
        {
            foreach (var (position, rotation) in _deflects) SharedBurst(vfx, VfxType.DeflectSpark, position, rotation);
            foreach (var (position, rotation) in _shieldHits) SharedBurst(vfx, VfxType.ShieldHit, position, rotation);
        }
        _deflects.Clear();
        _shieldHits.Clear();

        // Gone: release, impact where it was (sized like the effect), let the entity be removed (its stand-in with it)
        using (var gone = _gone.ToEntityArray(Allocator.Temp))
        {
            foreach (var e in gone)
            {
                var binding = EntityManager.GetComponentData<VfxBinding>(e);
                if (binding.Proxy != Entity.Null && EntityManager.Exists(binding.Proxy)) EntityManager.DestroyEntity(binding.Proxy);
                if (vfx == null) continue;
                if (binding.Handle != 0) vfx.Release(binding.Handle);
                if (binding.Impact == VfxType.None) continue;
                int impact = vfx.Burst(binding.Impact, binding.Position, binding.Rotation);
                if (binding.Scale > 0f)
                {
                    if (binding.Uniform) vfx.ScaleUniform(impact, binding.Scale);
                    else vfx.Scale(impact, binding.Scale);
                }
            }
            EntityManager.RemoveComponent<VfxBinding>(gone);
        }

        if (vfx != null)
        {
            UpdatePlayerEffects(vfx);
            UpdateUnitEffects(vfx);
            UpdateQuarryMarks(vfx);
            UpdatePoisonMarks(vfx);
            UpdateStatusEffects(vfx);
            UpdateCombatEffects(vfx);
            PlayReceived(vfx);
            UpdateArcs(vfx);
            UpdateIceBlocks(vfx);
        }
        _arcsThisFrame = 0;
        _crits.Clear();
        _received.Clear();
        _dodges.Clear();
        _blocks.Clear();
    }

    // The area of effect of an entity's source (1 without one), for effects sized before the attack's own radius is
    float AreaOf(Entity e)
    {
        if (!EntityManager.HasComponent<Source>(e)) return 1f;
        var source = EntityManager.GetComponentData<Source>(e).Entity;
        return EntityManager.HasComponent<CalculatedStats>(source) ? math.max(0f, EntityManager.GetComponentData<CalculatedStats>(source).Value.AreaMultiplier) : 1f;
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
            SharedBurst(vfx, VfxType.UndertowRipple, Feet(transform.ValueRO), quaternion.identity);
            ripples++;
        }

        // Bulwark: hits from in front this frame, the way BulwarkSystem tells (the hit goes away from its attacker)
        foreach (var (lastHit, transform) in SystemAPI.Query<RefRO<LastHit>, RefRO<LocalTransform>>().WithAll<Bulwark>())
        {
            if (lastHit.ValueRO.Time != now) continue;
            if (math.dot(-lastHit.ValueRO.Direction.xz, transform.ValueRO.Forward().xz) <= 0f) continue;
            SharedBurst(vfx, VfxType.BulwarkShimmer, transform.ValueRO.Position, transform.ValueRO.Rotation);
        }

        // Footsteps: a puff every StepLength walked; none while dashing, nor after a jump (teleport, respawn)
        foreach (var (transform, actions, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<NetPlayerActions>>().WithEntityAccess())
        {
            var position = transform.ValueRO.Position;
            bool dashing = actions.ValueRO.Has(NetPlayerActions.Dashing);
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
    /// Skills whose hits have a look of their own: true when it replaces the generic Strike. Swipe's arc at the swinger,
    /// over the swept angle and sized to its reach; Upheaval's slam and quake sized to their radius; Phantom Edge's slash
    /// and the afterimage where its caster blinked to; Riposte's flash plays with its cast (CastLook); Meteor's impact
    /// adds a fireball burst to the generic shockwave.
    /// </summary>
    bool SkillStrike(VfxManager vfx, Entity e, LocalTransform t, Attack attack)
    {
        switch (attack.Origin.Label)
        {
            case DamageLabel.Swipe:
                if (!EntityManager.HasComponent<SweepingAttack>(e)) return true;
                var sweep = EntityManager.GetComponentData<SweepingAttack>(e);
                if (!EntityManager.HasComponent<LocalTransform>(sweep.Center)) return true;
                // Drawn over the whole swept arc, the same for both swing directions
                SharedBurst(vfx, VfxType.SwipeArc, EntityManager.GetComponentData<LocalTransform>(sweep.Center).Position, quaternion.LookRotationSafe(sweep.Forward, math.up()), sweep.Distance);
                return true;
            case DamageLabel.Upheaval:
                // The quake turned like its warning (identity), so their cracks line up; the slam like its caster
                bool quake = EntityManager.HasComponent<QuakeAttack>(e);
                var slammer = EntityManager.HasComponent<Source>(e) ? EntityManager.GetComponentData<Source>(e).Entity : Entity.Null;
                var facing = !quake && EntityManager.HasComponent<LocalTransform>(slammer) ? EntityManager.GetComponentData<LocalTransform>(slammer).Rotation : quaternion.identity;
                SharedBurst(vfx, quake ? VfxType.UpheavalQuake : VfxType.UpheavalSlam, t.Position, facing, math.max(attack.Radius, 0.5f));
                return true;
            case DamageLabel.PhantomEdge:
                var caster = EntityManager.HasComponent<Source>(e) ? EntityManager.GetComponentData<Source>(e).Entity : Entity.Null;
                if (!EntityManager.HasComponent<LocalTransform>(caster))
                {
                    SharedBurst(vfx, VfxType.PhantomSlash, t.Position, quaternion.identity);
                    return true;
                }
                // The caster has just blinked next to the enemy, facing it
                var at = EntityManager.GetComponentData<LocalTransform>(caster);
                SharedBurst(vfx, VfxType.PhantomSlash, t.Position, at.Rotation);
                SharedBurst(vfx, VfxType.PhantomAfterimage, at.Position, at.Rotation);
                return true;
            case DamageLabel.Riposte:
                return true;
            case DamageLabel.Meteor:
                SharedBurst(vfx, VfxType.FireballImpact, t.Position, quaternion.identity);
                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// The generic look of an area attack: a slash arc for a cone (its reach), a thrust streak for a rectangle (its length),
    /// a shockwave for a round attack of 2 m or more. Lightning and Cold round attacks keep their own bursts.
    /// </summary>
    bool Strike(VfxManager vfx, Entity e, LocalTransform t, Attack attack)
    {
        if (EntityManager.HasComponent<AttackShape>(e))
        {
            var shape = EntityManager.GetComponentData<AttackShape>(e);
            bool cone = shape.Kind == AttackShapeKind.Cone;
            SharedBurst(vfx, cone ? VfxType.StrikeSlash : VfxType.StrikeThrust, t.Position, t.Rotation,
                cone ? math.max(attack.Radius, 0.5f) : math.max(shape.HalfExtents.z, 0.5f), tinted: true, attack.Type);
        }
        else if (attack.Radius >= 2f && attack.Type != DamageType.Cold && attack.Type != DamageType.Lightning)
            SharedBurst(vfx, VfxType.StrikeShock, t.Position, quaternion.identity, attack.Radius, tinted: true, attack.Type);
        else return false;
        return true;
    }

    // A one-shot only the host can tell happened: played here, and on the other machines (NetFeedbackSendSystem).
    // Sized sideways by scale (0: as it is), tinted by the damage type's colour.
    void SharedBurst(VfxManager vfx, VfxType type, float3 position, quaternion rotation, float scale = 0f, bool tinted = false, DamageType tint = default)
    {
        PlayBurst(vfx, type, position, rotation, scale, tinted, tint);
        _send.Burst(type, position, rotation, scale, tinted, tint);
    }

    static void PlayBurst(VfxManager vfx, VfxType type, float3 position, quaternion rotation, float scale, bool tinted, DamageType tint)
    {
        int handle = vfx.Burst(type, position, rotation);
        if (scale > 0f) vfx.Scale(handle, scale);
        if (tinted) vfx.Tint(handle, StrikeColor(tint));
    }

    // Someone else's game: the host's one-shots and chains since the last update
    void PlayReceived(VfxManager vfx)
    {
        foreach (var feedback in _received)
        {
            if (feedback.Kind == NetFeedbackKind.Chain) QueueChain(feedback.Position, feedback.Amount, feedback.Enemies);
            else PlayBurst(vfx, feedback.Vfx, feedback.Position, feedback.Rotation, feedback.Amount, feedback.Tinted, feedback.Tint);
        }
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
        using (var feedbacks = _charged.ToComponentDataArray<NetUnitFeedback>(Allocator.Temp))
        using (var transforms = _charged.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            for (int i = 0; i < feedbacks.Length && puffs < StatusCap; i++)
            {
                if ((feedbacks[i].Statuses & NetUnitFeedback.Charged) == 0) continue;
                StatusPuff(vfx, VfxType.StatusCharged, transforms[i]);
                puffs++;
            }
    }

    static void StatusPuff(VfxManager vfx, VfxType effect, LocalTransform t) =>
        vfx.Burst(effect, (Vector3)t.Position + Vector3.up * 0.8f * t.Scale, Quaternion.identity);

    // Keyed by the wearer: the marker jumps to the next target, and goes with the passive or the wearer (NetPlayerActions)
    void UpdateQuarryMarks(VfxManager vfx)
    {
        var transforms = SystemAPI.GetComponentLookup<LocalTransform>(true);
        foreach (var (actions, entity) in SystemAPI.Query<RefRO<NetPlayerActions>>().WithEntityAccess())
        {
            var target = actions.ValueRO.QuarryTarget;
            bool marked = transforms.HasComponent(target);
            var t = marked ? transforms[target] : default;
            t.Position.y += 1.6f * t.Scale;  // Over the head: the root is the capsule's centre
            Track(vfx, _quarryMarks, entity, marked, VfxType.QuarryMark, VfxType.None, t);
        }
        ReleaseWithout<NetPlayerActions>(vfx, _quarryMarks);
    }

    // Poison Dart's marks: over the head of every marked unit (a NetUnitFeedback status bit), under a Quarry marker
    void UpdatePoisonMarks(VfxManager vfx)
    {
        _present.Clear();
        using (var entities = _charged.ToEntityArray(Allocator.Temp))
        using (var feedbacks = _charged.ToComponentDataArray<NetUnitFeedback>(Allocator.Temp))
        using (var transforms = _charged.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            for (int i = 0; i < entities.Length && _present.Count < MarkCap; i++)
            {
                if ((feedbacks[i].Statuses & NetUnitFeedback.PoisonMarked) == 0) continue;
                var t = transforms[i];
                t.Position.y += 1.3f * t.Scale;
                t.Rotation = quaternion.identity;
                Track(vfx, _poisonMarks, entities[i], true, VfxType.PoisonMark, VfxType.None, t);
                _present.Add(entities[i]);
            }
        _stale.Clear();
        foreach (var (entity, handle) in _poisonMarks)
            if (!_present.Contains(entity)) { vfx.Release(handle); _stale.Add(entity); }
        foreach (var entity in _stale) _poisonMarks.Remove(entity);
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

    // An effect that follows its entity, sized and tinted as the look says (a host's stand-in carries the same look)
    static VfxBinding Bind(VfxManager vfx, in NetVfx look, LocalTransform t)
    {
        var binding = Bind(vfx, look.Type, look.Impact, t);
        binding.Scale = look.Scale;
        binding.Uniform = look.Uniform;
        binding.Oriented = look.Oriented;
        if (look.Oriented)
        {
            binding.Rotation = t.Rotation;
            vfx.Move(binding.Handle, t.Position, t.Rotation);
        }
        if (look.Scale > 0f)
        {
            if (look.Uniform) vfx.ScaleUniform(binding.Handle, look.Scale);
            else vfx.Scale(binding.Handle, look.Scale);
        }
        if (look.Tinted) vfx.Tint(binding.Handle, StrikeColor(look.Tint));
        return binding;
    }

    // The host: a NetVfx ghost for each new effect that follows what only it has, so the other machines show it too
    void MakeStandIns(NativeList<(Entity, NetVfx)> standIns)
    {
        if (standIns.IsEmpty || !SystemAPI.TryGetSingleton<NetPrefabs>(out var prefabs) || prefabs.Vfx == Entity.Null) return;
        foreach (var (entity, look) in standIns)
        {
            if (look.Type == VfxType.None) continue;
            var t = EntityManager.GetComponentData<LocalTransform>(entity);
            var standIn = EntityManager.Instantiate(prefabs.Vfx);
            // Turned and sized like its entity when the look says so; otherwise only where it is
            EntityManager.SetComponentData(standIn, new LocalTransform { Position = t.Position, Rotation = look.Oriented ? t.Rotation : quaternion.identity, Scale = look.Uniform ? look.Scale : 1f });
            EntityManager.SetComponentData(standIn, look);
            var binding = EntityManager.GetComponentData<VfxBinding>(entity);
            binding.Proxy = standIn;
            EntityManager.SetComponentData(entity, binding);
        }
    }

    static VfxBinding Bind(VfxManager vfx, VfxType type, VfxType impact, LocalTransform t) => new VfxBinding
    {
        Handle = type == VfxType.None ? 0 : vfx.Spawn(type, t.Position, Quaternion.identity),
        Impact = impact,
        Position = t.Position,
        Rotation = quaternion.identity,
    };

    // Everything players show, on every machine (NetPlayerActions): PlayerLooks, the stance, the hook, casts and puffs
    void UpdatePlayerEffects(VfxManager vfx)
    {
        float time = (float)SystemAPI.Time.ElapsedTime;
        foreach (var (actions, transform, entity) in SystemAPI.Query<RefRO<NetPlayerActions>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            var a = actions.ValueRO;
            var t = transform.ValueRO;
            for (int i = 0; i < PlayerLooks.Length; i++)
            {
                var look = PlayerLooks[i];
                bool on = (a.States & look.States) != 0 || (a.Auras & look.Auras) != 0;
                var at = t;
                if (look.Spin != 0f) at.Rotation = quaternion.RotateY(math.radians(look.Spin * time * (a.HasAura(NetPlayerActions.GaleWardReversed) ? -1f : 1f)));
                Track(vfx, _playerLooks[i], entity, on, look.Effect, look.Start, at);
            }
            UpdateStance(vfx, entity, a.Stance, t);
            UpdateHook(vfx, entity, a, t);
            UpdateCasts(vfx, entity, a, t);
        }

        foreach (var map in _playerLooks) ReleaseWithout<NetPlayerActions>(vfx, map);
        ReleaseWithout<NetPlayerActions>(vfx, _hookHeads);
        ReleaseWithout<NetPlayerActions>(vfx, _hookChains);
        _stale.Clear();
        foreach (var (entity, stance) in _stances)
            if (!EntityManager.HasComponent<NetPlayerActions>(entity)) { vfx.Release(stance.Handle); _stale.Add(entity); }
        foreach (var entity in _stale) _stances.Remove(entity);
        _stale.Clear();
        foreach (var entity in _players.Keys)
            if (!EntityManager.HasComponent<NetPlayerActions>(entity)) _stale.Add(entity);
        foreach (var entity in _stale) _players.Remove(entity);
    }

    // The stance's aura in its colour: Strength ruby, Agility teal, Intelligence sapphire; made again when it changes
    void UpdateStance(VfxManager vfx, Entity entity, byte stance, LocalTransform t)
    {
        bool has = _stances.TryGetValue(entity, out var current);
        if (has && current.Stance != stance)
        {
            vfx.Release(current.Handle);
            _stances.Remove(entity);
            has = false;
        }
        if (stance == 0) return;
        if (!has)
        {
            current = (vfx.Spawn(VfxType.StanceAura, t.Position, t.Rotation), stance);
            vfx.Tint(current.Handle, stance == NetPlayerActions.StanceStrength ? new Color(0.88f, 0.21f, 0.31f)
                : stance == NetPlayerActions.StanceAgility ? new Color(0.31f, 0.84f, 0.78f) : new Color(0.29f, 0.49f, 1f));
            _stances[entity] = current;
        }
        vfx.Move(current.Handle, t.Position, t.Rotation);
    }

    // Hookshot Slash: the hook at HookPoint facing away from the player, and the chain from the player's hand to it
    void UpdateHook(VfxManager vfx, Entity entity, in NetPlayerActions a, LocalTransform t)
    {
        bool hooking = a.Has(NetPlayerActions.Hooking);
        var away = a.HookPoint - t.Position;
        away.y = 0f;
        var head = new LocalTransform { Position = a.HookPoint, Rotation = quaternion.LookRotationSafe(math.normalizesafe(away, t.Forward()), math.up()), Scale = 1f };
        Track(vfx, _hookHeads, entity, hooking, VfxType.HookHead, VfxType.None, head);
        Track(vfx, _hookChains, entity, hooking, VfxType.HookChain, VfxType.None, t);
        if (hooking && _hookChains.TryGetValue(entity, out int chain))
            vfx.SetEnds(chain, t.Position + math.rotate(t.Rotation, new float3(0f, 0.37f, 0.6f)), a.HookPoint);   // Both hands, held out (Channel clip)
    }

    // A burst for each skill use (Casts counts up), and puffs where the player vanishes and appears: Teleport (its cast),
    // Phantom Edge (Phasing starting and ending) and Shadowstep's jumps (a jump while Shadowed). From last frame's spot.
    void UpdateCasts(VfxManager vfx, Entity entity, in NetPlayerActions a, LocalTransform t)
    {
        if (_players.TryGetValue(entity, out var seen))
        {
            bool teleported = false;
            if (a.Casts != seen.Casts)
            {
                var skill = (SkillType)a.CastSkill;
                var (effect, tint) = CastLook(skill);
                int handle = vfx.Burst(effect, t.Position, t.Rotation);
                if (tint.a > 0f) vfx.Tint(handle, tint);
                if (skill == SkillType.Teleport)
                {
                    vfx.Burst(VfxType.VanishPuff, seen.Position, t.Rotation);
                    teleported = true;
                }
            }
            bool phasing = a.Has(NetPlayerActions.Phasing), wasPhasing = (seen.States & NetPlayerActions.Phasing) != 0;
            if (phasing && !wasPhasing) vfx.Burst(VfxType.VanishPuff, seen.Position, t.Rotation);
            else if (!phasing && wasPhasing) vfx.Burst(VfxType.AppearPuff, t.Position, t.Rotation);
            else if (!phasing && !teleported && a.HasAura(NetPlayerActions.Shadowed) && math.distance(seen.Position.xz, t.Position.xz) > JumpDistance)
            {
                vfx.Burst(VfxType.VanishPuff, seen.Position, t.Rotation);
                vfx.Burst(VfxType.AppearPuff, t.Position, t.Rotation);
            }
        }
        _players[entity] = new PlayerSeen { Casts = a.Casts, States = a.States, Position = t.Position };
    }

    /// <summary>
    /// The burst at the player when it uses a skill, and its tint (clear: none). Spells flash in their element's colour,
    /// summons draw a gold circle; the rest have their own (Teleport's appear puff, Blood Rite, Riposte) or none.
    /// </summary>
    static (VfxType Effect, Color Tint) CastLook(SkillType skill) => skill switch
    {
        SkillType.Teleport => (VfxType.AppearPuff, default),
        SkillType.BloodRite => (VfxType.BloodBurst, default),
        SkillType.Riposte => (VfxType.RiposteFlash, default),
        SkillType.StoneGuardian or SkillType.Wolf or SkillType.Wisp or SkillType.ElementalHydra or SkillType.RaiseThrall => (VfxType.SummonCircle, default),
        SkillType.Meteor or SkillType.Immolation => (VfxType.CastFlash, new Color(1f, 0.48f, 0.1f)),                                   // Fire #FF7A1A
        SkillType.FrozenOrb or SkillType.ColdSnap or SkillType.FrostLance or SkillType.Timezone => (VfxType.CastFlash, new Color(0.5f, 0.9f, 1f)),   // Frost #7FE6FF
        SkillType.TeslaCoil or SkillType.CursedHammer or SkillType.Tornado => (VfxType.CastFlash, new Color(0.29f, 0.49f, 1f)),       // Lightning sapphire #4A7CFF
        SkillType.PlagueArrow => (VfxType.CastFlash, new Color(0.55f, 1f, 0.23f)),                                                     // Poison #8CFF3A
        SkillType.GaleWard => (VfxType.CastFlash, new Color(0.31f, 0.84f, 0.78f)),                                                     // Wind teal #4FD6C8
        SkillType.MirrorImage or SkillType.Invulnerability => (VfxType.CastFlash, new Color(0.71f, 0.48f, 1f)),                        // Magic violet #B57BFF
        SkillType.EarthBending => (VfxType.CastFlash, new Color(0.89f, 0.91f, 0.94f)),                                                 // Physical steel #E2E8F0
        _ => (VfxType.None, default),
    };

    // Releases the effects of entities that no longer have T (gone, or the component taken off)
    void ReleaseWithout<T>(VfxManager vfx, Dictionary<Entity, int> map) where T : unmanaged, IComponentData
    {
        _stale.Clear();
        foreach (var (entity, handle) in map)
            if (!EntityManager.HasComponent<T>(entity)) { vfx.Release(handle); _stale.Add(entity); }
        foreach (var entity in _stale) map.Remove(entity);
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
