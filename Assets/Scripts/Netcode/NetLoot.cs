using Loottables;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

/// <summary>Which of the dungeon's loot tables a drop rolls (DungeonDefinition.KillLootTable, CompletionLootTable).</summary>
public enum NetLootTable : byte { Kill, Completion }

/// <summary>
/// The host to another machine: roll a drop from this table of the dungeon at this position. Loot is personal: every
/// machine rolls its own items, which only its players see and pick up.
/// </summary>
public struct NetLootDropRpc : IRpcCommand
{
    public NetLootTable Table;
    public float3 Position;
}

public static class NetLoot
{
    /// <summary>The host: has every other machine in the game roll this drop too.</summary>
    public static void Send(EntityManager entityManager, NetLootTable table, float3 position)
    {
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>(), ComponentType.ReadOnly<NetworkStreamInGame>());
        using var connections = query.ToEntityArray(Allocator.Temp);
        foreach (var connection in connections)
        {
            var rpc = entityManager.CreateEntity();
            entityManager.AddComponentData(rpc, new NetLootDropRpc { Table = table, Position = position });
            entityManager.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>The scene's dungeon table, or null outside a dungeon.</summary>
    public static LootTable TableOf(NetLootTable table)
    {
        if (!GameArea.TryGet(out var area) || area.Dungeon.Value == null) return null;
        return table == NetLootTable.Kill ? area.Dungeon.Value.KillLootTable : area.Dungeon.Value.CompletionLootTable;
    }
}

/// <summary>In someone else's game: rolls the host's drops for this machine's players (GroundLootManager.DropFromTable).</summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial class NetLootReceiveSystem : SystemBase
{
    EntityQuery _received;

    protected override void OnCreate()
    {
        _received = SystemAPI.QueryBuilder().WithAll<NetLootDropRpc, ReceiveRpcCommandRequest>().Build();
        RequireForUpdate(_received);
    }

    protected override void OnUpdate()
    {
        var groundLoot = GroundLootManager.Instance;
        using (var drops = _received.ToComponentDataArray<NetLootDropRpc>(Allocator.Temp))
            foreach (var drop in drops)
            {
                var table = NetLoot.TableOf(drop.Table);
                if (table == null || groundLoot == null) Debug.LogWarning($"NetLootReceiveSystem: no {drop.Table} loot table or GroundLootManager here, so the drop is lost.");
                else groundLoot.DropFromTable(table, drop.Position);
            }
        EntityManager.DestroyEntity(_received);
    }
}
