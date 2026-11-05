using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Bone Ward and Festering Horde: every unit on a wearer's side within Radius gets its resistance, the wearer included,
/// and several wearers stack. Goes through the grid, as a whole group of enemies can be wearers.
/// </summary>
[UpdateInGroup(typeof(EffectSystemGroup))]
[BurstCompile]
public partial struct ResistanceAuraSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<UnitSpatialGrid>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var grid = SystemAPI.GetSingleton<UnitSpatialGrid>().Units;
        var buffs = SystemAPI.GetComponentLookup<ResistanceAuraBuff>();
        var enemies = SystemAPI.GetComponentLookup<Enemy>(true);
        state.CompleteDependency();  // The grid and the lookups are used here on the main thread

        foreach (var buff in SystemAPI.Query<RefRW<ResistanceAuraBuff>>())
            buff.ValueRW = default;

        // A dying enemy has lost its Enemy component, so it would count as being on the players' side
        foreach (var (ward, transform, wearer) in SystemAPI.Query<RefRO<BoneWard>, RefRO<LocalTransform>>().WithNone<RigidDeath>().WithEntityAccess())
            Spread(grid, ref buffs, enemies, wearer, transform.ValueRO.Position, ward.ValueRO.Radius,
                   new ResistanceAuraBuff { ColdResistance = ward.ValueRO.ColdResistance, ColdStacks = 1 });

        foreach (var (horde, transform, wearer) in SystemAPI.Query<RefRO<FesteringHorde>, RefRO<LocalTransform>>().WithNone<RigidDeath>().WithEntityAccess())
            Spread(grid, ref buffs, enemies, wearer, transform.ValueRO.Position, horde.ValueRO.Radius,
                   new ResistanceAuraBuff { PoisonResistance = horde.ValueRO.PoisonResistance, PoisonStacks = 1 });
    }

    static void Spread(in NativeParallelMultiHashMap<int, GridUnit> units, ref ComponentLookup<ResistanceAuraBuff> buffs, in ComponentLookup<Enemy> enemies,
                       Entity wearer, float3 position, float radius, in ResistanceAuraBuff stack)
    {
        bool wearerIsEnemy = enemies.HasComponent(wearer);
        var cell = UnitSpatialGrid.Cell(position);
        int reach = (int)math.ceil(radius / UnitSpatialGrid.CellSize);
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        {
            if (!units.TryGetFirstValue(UnitSpatialGrid.Hash(cell + new int2(x, z)), out var other, out var iterator)) continue;
            do
            {
                if (!buffs.HasComponent(other.Entity) || enemies.HasComponent(other.Entity) != wearerIsEnemy) continue;
                if (math.distancesq(other.Position.xz, position.xz) > radius * radius) continue;

                var buff = buffs[other.Entity];
                buff.ColdResistance += stack.ColdResistance;
                buff.PoisonResistance += stack.PoisonResistance;
                buff.ColdStacks += stack.ColdStacks;
                buff.PoisonStacks += stack.PoisonStacks;
                buffs[other.Entity] = buff;
            }
            while (units.TryGetNextValue(out other, ref iterator));
        }
    }
}

/// <summary>Adds the resistance of nearby Bone Ward and Festering Horde wearers.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct ResistanceAuraBuffStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (buff, stats) in SystemAPI.Query<RefRO<ResistanceAuraBuff>, RefRW<CalculatedStats>>())
        {
            stats.ValueRW.Value.ColdResistance += buff.ValueRO.ColdResistance;
            stats.ValueRW.Value.PoisonResistance += buff.ValueRO.PoisonResistance;
        }
    }
}
