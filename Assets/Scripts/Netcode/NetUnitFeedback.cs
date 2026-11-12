using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

/// <summary>
/// On unit ghosts (enemies, companions, players): what other machines need to show it react, which only the host's
/// gameplay knows. Hits counts up per hit (DamagingSystem), Attacks with its melee attacks, Dying once it plays its
/// death, Statuses has a bit per shown status effect (NetUnitFeedback.Burning ...).
/// </summary>
public struct NetUnitFeedback : IComponentData
{
    public const byte Burning = 1, Poisoned = 2, Chilled = 4, Bleeding = 8, Frozen = 16;

    [GhostField] public byte Hits;
    [GhostField] public DamageType HitType;
    [GhostField] public byte Attacks;
    [GhostField] public bool Dying;
    [GhostField] public byte Statuses;
}

/// <summary>The host: fills each unit's NetUnitFeedback from its attacks, death and status effects (Hits: DamagingSystem).</summary>
[UpdateInGroup(typeof(HealthAndResourceSystemGroup), OrderLast = true)]
[WorldSystemFilter(GameWorlds.Server)]
[BurstCompile]
public partial struct NetUnitFeedbackWriteSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var behaviors = SystemAPI.GetComponentLookup<NpcBehavior>(true);
        var deaths = SystemAPI.GetComponentLookup<RigidDeath>(true);
        var burning = SystemAPI.GetComponentLookup<Burning>(true);
        var poisoned = SystemAPI.GetComponentLookup<Poisoned>(true);
        var chilled = SystemAPI.GetComponentLookup<Chilled>(true);
        var bleeding = SystemAPI.GetComponentLookup<Bleeding>(true);
        var frozen = SystemAPI.GetComponentLookup<Frozen>(true);
        foreach (var (feedback, entity) in SystemAPI.Query<RefRW<NetUnitFeedback>>().WithEntityAccess())
        {
            var value = feedback.ValueRO;
            if (behaviors.HasComponent(entity)) value.Attacks = (byte)behaviors[entity].AttackCount;
            value.Dying = deaths.HasComponent(entity);
            value.Statuses = (byte)(Bit(burning, entity, NetUnitFeedback.Burning) | Bit(poisoned, entity, NetUnitFeedback.Poisoned)
                | Bit(chilled, entity, NetUnitFeedback.Chilled) | Bit(bleeding, entity, NetUnitFeedback.Bleeding) | Bit(frozen, entity, NetUnitFeedback.Frozen));
            if (value.Attacks != feedback.ValueRO.Attacks || value.Dying != feedback.ValueRO.Dying || value.Statuses != feedback.ValueRO.Statuses)
                feedback.ValueRW = value;
        }
    }

    static int Bit<T>(in ComponentLookup<T> lookup, Entity entity, byte bit) where T : unmanaged, IComponentData, IEnableableComponent =>
        lookup.HasComponent(entity) && lookup.IsComponentEnabled(entity) ? bit : 0;
}

/// <summary>On another machine's unit ghost: the NetUnitFeedback.Hits already shown.</summary>
public struct NetUnitFeedbackSeen : IComponentData
{
    public byte Hits;
}

/// <summary>
/// In someone else's game: turns each unit's NetUnitFeedback into what the feedback systems read on the host. A new hit
/// starts its HitFlash (the flash, its hit effect and Hit clip follow); Attacks plays the Attack clip (RigidAnimationSystem
/// reads it); Dying plays the death (RigidDeath, which never ends here: the host removes the ghost); the status bits
/// switch the status components, for their puffs and ice block.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup), OrderFirst = true)]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial class NetUnitFeedbackApplySystem : SystemBase
{
    EntityQuery _new;

    protected override void OnCreate()
    {
        _new = SystemAPI.QueryBuilder().WithAll<NetUnitFeedback>().WithNone<NetUnitFeedbackSeen>().Build();
        RequireForUpdate<NetUnitFeedback>();
    }

    protected override void OnUpdate()
    {
        if (NetSession.IsHost) return;
        AddNew();

        SystemAPI.TryGetSingleton<HitFlashConfig>(out var config);
        var flashes = SystemAPI.GetComponentLookup<HitFlash>();
        var deaths = SystemAPI.GetComponentLookup<RigidDeath>(true);
        var burning = SystemAPI.GetComponentLookup<Burning>();
        var poisoned = SystemAPI.GetComponentLookup<Poisoned>();
        var chilled = SystemAPI.GetComponentLookup<Chilled>();
        var bleeding = SystemAPI.GetComponentLookup<Bleeding>();
        var frozen = SystemAPI.GetComponentLookup<Frozen>();
        var dying = new NativeList<Entity>(Allocator.Temp);
        foreach (var (feedback, seen, entity) in SystemAPI.Query<RefRO<NetUnitFeedback>, RefRW<NetUnitFeedbackSeen>>().WithEntityAccess())
        {
            var value = feedback.ValueRO;
            if (value.Hits != seen.ValueRO.Hits && flashes.HasComponent(entity))
            {
                ref var flash = ref flashes.GetRefRW(entity).ValueRW;
                flash.FlashColor = config.FlashColor(value.HitType);
                flash.Timer = config.Duration;
            }
            seen.ValueRW.Hits = value.Hits;
            if (value.Dying && !deaths.HasComponent(entity)) dying.Add(entity);
            burning.SetComponentEnabled(entity, (value.Statuses & NetUnitFeedback.Burning) != 0);
            poisoned.SetComponentEnabled(entity, (value.Statuses & NetUnitFeedback.Poisoned) != 0);
            chilled.SetComponentEnabled(entity, (value.Statuses & NetUnitFeedback.Chilled) != 0);
            bleeding.SetComponentEnabled(entity, (value.Statuses & NetUnitFeedback.Bleeding) != 0);
            frozen.SetComponentEnabled(entity, (value.Statuses & NetUnitFeedback.Frozen) != 0);
        }
        foreach (var entity in dying) RigidDeathUtility.ShowDying(EntityManager, entity, float.MaxValue);
    }

    // New ghosts: their hits so far count as shown, and the status components to switch, all off
    void AddNew()
    {
        if (_new.IsEmpty) return;
        using var entities = _new.ToEntityArray(Allocator.Temp);
        var statuses = new ComponentTypeSet(ComponentType.ReadWrite<Burning>(), ComponentType.ReadWrite<Poisoned>(), ComponentType.ReadWrite<Chilled>(),
            ComponentType.ReadWrite<Bleeding>(), ComponentType.ReadWrite<Frozen>());
        foreach (var entity in entities)
        {
            EntityManager.AddComponentData(entity, new NetUnitFeedbackSeen { Hits = EntityManager.GetComponentData<NetUnitFeedback>(entity).Hits });
            EntityManager.AddComponent(entity, statuses);
            EntityManager.SetComponentEnabled<Burning>(entity, false);
            EntityManager.SetComponentEnabled<Poisoned>(entity, false);
            EntityManager.SetComponentEnabled<Chilled>(entity, false);
            EntityManager.SetComponentEnabled<Bleeding>(entity, false);
            EntityManager.SetComponentEnabled<Frozen>(entity, false);
        }
    }
}
