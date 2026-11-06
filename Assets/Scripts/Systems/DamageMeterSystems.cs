using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>Owns the DamageMeter singleton and keeps its clock.</summary>
[UpdateInGroup(typeof(InputSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct DamageMeterSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.CreateSingleton(new DamageMeter { Rows = new NativeHashMap<MeterKey, MeterRow>(32, Allocator.Persistent), StartTime = -1.0 });
    }

    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingletonRW<DamageMeter>(out var meter) && meter.ValueRO.Rows.IsCreated)
            meter.ValueRW.Rows.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var meter = SystemAPI.GetSingletonRW<DamageMeter>();
        double now = SystemAPI.Time.ElapsedTime;
        if (meter.ValueRO.StartTime < 0.0) meter.ValueRW.StartTime = now;
        meter.ValueRW.Now = now;
    }
}

/// <summary>
/// Writes to the DamageMeter what's dealt and healed, for the systems that do it. Each amount goes to the row of the unit
/// and to the row of its source: a skill or effect, or for what has no label, who did it. Only players and companions
/// are tracked. Damage is what's left after armor and resistances, overkill included; healing is what was restored.
/// </summary>
public struct DamageMeterRecorder
{
    NativeHashMap<MeterKey, MeterRow> _rows;
    int _second;
    [ReadOnly] ComponentLookup<Player> _players;
    [ReadOnly] ComponentLookup<Companion> _companions;
    [ReadOnly] ComponentLookup<EnemyTargetable> _targetables;
    [ReadOnly] ComponentLookup<UnitInfo> _infos;

    public DamageMeterRecorder(ref SystemState state, in DamageMeter meter)
    {
        _rows = meter.Rows;
        _second = meter.Second;
        _players = state.GetComponentLookup<Player>(true);
        _companions = state.GetComponentLookup<Companion>(true);
        _targetables = state.GetComponentLookup<EnemyTargetable>(true);
        _infos = state.GetComponentLookup<UnitInfo>(true);
    }

    public void Damage(Entity target, Entity source, in DamageOrigin origin, float amount)
    {
        var attacker = IsTracked(origin.Unit) ? origin.Unit : source;
        var from = SourceName(origin.Label, attacker);
        if (IsTracked(attacker)) Add(MeterStat.DamageDone, attacker, from, amount);
        if (IsTracked(target)) Add(MeterStat.DamageReceived, target, from, amount);
    }

    public void Healing(Entity target, Entity source, DamageLabel label, float amount)
    {
        var from = SourceName(label, source);
        if (IsTracked(source)) Add(MeterStat.HealingDone, source, from, amount);
        if (IsTracked(target)) Add(MeterStat.HealingReceived, target, from, amount);
    }

    void Add(MeterStat stat, Entity unit, in MeterName source, float amount)
    {
        Add(new MeterKey { Stat = stat, Name = UnitName(unit) }, amount);
        Add(new MeterKey { Stat = stat, Name = source, BySource = true }, amount);
    }

    void Add(in MeterKey key, float amount)
    {
        if (!_rows.TryGetValue(key, out var row)) row = MeterRow.Start(_second);
        row.Add(_second, amount);
        _rows[key] = row;
    }

    bool IsTracked(Entity unit) => _players.HasComponent(unit) || _companions.HasComponent(unit);

    MeterName UnitName(Entity unit)
    {
        if (_players.HasComponent(unit)) return new MeterName { Player = _targetables[unit].FieldSlot + 1 };
        return _infos.HasComponent(unit) ? new MeterName { Unit = _infos[unit].Source } : default;
    }

    MeterName SourceName(DamageLabel label, Entity unit)
    {
        if (label != DamageLabel.None) return new MeterName { Label = label };
        return _players.HasComponent(unit) ? new MeterName { Label = DamageLabel.Other } : UnitName(unit);
    }
}
