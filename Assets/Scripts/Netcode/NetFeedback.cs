using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

public enum NetFeedbackKind : byte
{
    Damage, Crit, Absorbed, Blocked, Dodged, Bleed, Burn, Poison, Heal,
    /// <summary>A one-shot effect only the host can tell happened (VfxSystem.SharedBurst).</summary>
    Burst,
    /// <summary>A Charged discharge's chain lightning: each machine finds the units it hops through (VfxSystem.QueueChain).</summary>
    Chain,
}

/// <summary>
/// The host to another machine: one bit of feedback from this frame, a combat number or a one-shot effect. Amount is the
/// number, a Burst's sideways size (0: as it is) or a Chain's radius.
/// </summary>
public struct NetFeedbackRpc : IRpcCommand
{
    public NetFeedbackKind Kind;
    public float3 Position;
    public float Amount;
    public VfxType Vfx;
    public quaternion Rotation;
    public bool Tinted;
    public DamageType Tint;
    /// <summary>Chain: it hops through enemies, otherwise through players and companions.</summary>
    public bool Enemies;
}

/// <summary>
/// The host: sends the combat numbers (from the damage and healing systems' events) and VfxSystem's host-only one-shots
/// to every other machine in the game, at most MaxPerFrame a frame so a big fight can't flood the connection.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup), OrderLast = true)]
public partial class NetFeedbackSendSystem : SystemBase
{
    const int MaxPerFrame = 24;

    NativeList<NetFeedbackRpc> _events;
    EntityQuery _connections;
    DamagingSystem _damaging;
    CalculateDamageSystem _calculateDamage;
    EffectDamageTextSystem _effectText;
    ApplyHealingSystem _healing;

    protected override void OnCreate()
    {
        _events = new NativeList<NetFeedbackRpc>(MaxPerFrame, Allocator.Persistent);
        _connections = SystemAPI.QueryBuilder().WithAll<NetworkStreamConnection, NetworkStreamInGame>().Build();
        _damaging = World.GetOrCreateSystemManaged<DamagingSystem>();
        _calculateDamage = World.GetOrCreateSystemManaged<CalculateDamageSystem>();
        _effectText = World.GetOrCreateSystemManaged<EffectDamageTextSystem>();
        _healing = World.GetOrCreateSystemManaged<ApplyHealingSystem>();
        _damaging.OnDamageTaken += OnDamageTaken;
        _damaging.OnDamageAbsorbed += OnAbsorbed;
        _calculateDamage.OnDamageBlocked += OnBlocked;
        _calculateDamage.OnDamageDodged += OnDodged;
        _effectText.OnBleeding += OnBleeding;
        _effectText.OnBurning += OnBurning;
        _effectText.OnPoisoned += OnPoisoned;
        _healing.OnHealingReceived += OnHealing;
    }

    protected override void OnDestroy()
    {
        _damaging.OnDamageTaken -= OnDamageTaken;
        _damaging.OnDamageAbsorbed -= OnAbsorbed;
        _calculateDamage.OnDamageBlocked -= OnBlocked;
        _calculateDamage.OnDamageDodged -= OnDodged;
        _effectText.OnBleeding -= OnBleeding;
        _effectText.OnBurning -= OnBurning;
        _effectText.OnPoisoned -= OnPoisoned;
        _healing.OnHealingReceived -= OnHealing;
        _events.Dispose();
    }

    void OnDamageTaken(float amount, float3 position, bool isCrit) => Number(isCrit ? NetFeedbackKind.Crit : NetFeedbackKind.Damage, amount, position);
    void OnAbsorbed(float amount, float3 position) => Number(NetFeedbackKind.Absorbed, amount, position);
    void OnBlocked(float amount, float3 position) => Number(NetFeedbackKind.Blocked, amount, position);
    void OnDodged(float3 position) => Number(NetFeedbackKind.Dodged, 0f, position);
    void OnBleeding(float amount, float3 position) => Number(NetFeedbackKind.Bleed, amount, position);
    void OnBurning(float amount, float3 position) => Number(NetFeedbackKind.Burn, amount, position);
    void OnPoisoned(float amount, float3 position) => Number(NetFeedbackKind.Poison, amount, position);
    void OnHealing(float amount, float3 position) => Number(NetFeedbackKind.Heal, amount, position);

    void Number(NetFeedbackKind kind, float amount, float3 position) => Add(new NetFeedbackRpc { Kind = kind, Amount = amount, Position = position });

    /// <summary>A one-shot effect for the other machines, sized sideways by scale (0: as it is) and tinted by tint's damage colour.</summary>
    public void Burst(VfxType type, float3 position, quaternion rotation, float scale = 0f, bool tinted = false, DamageType tint = default) =>
        Add(new NetFeedbackRpc { Kind = NetFeedbackKind.Burst, Vfx = type, Position = position, Rotation = rotation, Amount = scale, Tinted = tinted, Tint = tint });

    public void Chain(float3 origin, float radius, bool enemies) =>
        Add(new NetFeedbackRpc { Kind = NetFeedbackKind.Chain, Position = origin, Amount = radius, Enemies = enemies });

    void Add(in NetFeedbackRpc feedback)
    {
        if (NetSession.IsHost && _events.Length < MaxPerFrame) _events.Add(feedback);
    }

    protected override void OnUpdate()
    {
        if (_events.IsEmpty) return;
        if (!_connections.IsEmpty)
        {
            using var connections = _connections.ToEntityArray(Allocator.Temp);
            foreach (var connection in connections)
                foreach (var feedback in _events)
                {
                    var rpc = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(rpc, feedback);
                    EntityManager.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                }
        }
        _events.Clear();
    }
}

/// <summary>
/// In someone else's game: raises the host's feedback as the same events its damage and healing systems raise there,
/// for CombatTextHUD and VfxSystem, plus OnBurst for the one-shot effects and chains.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup), OrderFirst = true)]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial class NetFeedbackReceiveSystem : SystemBase
{
    public event Action<float, float3, bool> OnDamageTaken;
    public event Action<float, float3> OnDamageAbsorbed, OnDamageBlocked, OnBleeding, OnBurning, OnPoisoned, OnHealingReceived;
    public event Action<float3> OnDamageDodged;
    public event Action<NetFeedbackRpc> OnBurst;

    EntityQuery _received;

    protected override void OnCreate()
    {
        _received = SystemAPI.QueryBuilder().WithAll<NetFeedbackRpc, ReceiveRpcCommandRequest>().Build();
        RequireForUpdate(_received);
    }

    protected override void OnUpdate()
    {
        using (var events = _received.ToComponentDataArray<NetFeedbackRpc>(Allocator.Temp))
            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case NetFeedbackKind.Damage: OnDamageTaken?.Invoke(e.Amount, e.Position, false); break;
                    case NetFeedbackKind.Crit: OnDamageTaken?.Invoke(e.Amount, e.Position, true); break;
                    case NetFeedbackKind.Absorbed: OnDamageAbsorbed?.Invoke(e.Amount, e.Position); break;
                    case NetFeedbackKind.Blocked: OnDamageBlocked?.Invoke(e.Amount, e.Position); break;
                    case NetFeedbackKind.Dodged: OnDamageDodged?.Invoke(e.Position); break;
                    case NetFeedbackKind.Bleed: OnBleeding?.Invoke(e.Amount, e.Position); break;
                    case NetFeedbackKind.Burn: OnBurning?.Invoke(e.Amount, e.Position); break;
                    case NetFeedbackKind.Poison: OnPoisoned?.Invoke(e.Amount, e.Position); break;
                    case NetFeedbackKind.Heal: OnHealingReceived?.Invoke(e.Amount, e.Position); break;
                    default: OnBurst?.Invoke(e); break;
                }
            }
        EntityManager.DestroyEntity(_received);
    }
}
