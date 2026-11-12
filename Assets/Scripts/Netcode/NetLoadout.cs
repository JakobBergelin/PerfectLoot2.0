using System.Collections.Generic;
using ActiveSkills;
using Characters;
using Equipment;
using Passives;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

public enum NetLoadoutPart : byte { Skill, Passive, Item }

/// <summary>
/// A client to the host: one skill, passive or equipped item of a local player's loadout. All of them are sent just before
/// their NetLoadoutRpc, which they belong to.
/// </summary>
public struct NetLoadoutPartRpc : IRpcCommand
{
    public byte LocalIndex;
    public NetLoadoutPart Part;
    /// <summary>Skill: its slot, 0 to 3.</summary>
    public byte Slot;
    /// <summary>Skill: its SkillType. Passive: its PassiveType.</summary>
    public int Type;
    /// <summary>Skill: its slot's Variant (the skill's own mode, Stances' stance).</summary>
    public int Variant;
    /// <summary>Item: its RollableItem.Id, and what it was rolled from (RollableItem.RollFromSeed).</summary>
    public FixedString64Bytes ItemId;
    public uint Seed;
    public int RollVersion;
}

/// <summary>
/// A client to the host: a local player's loadout. Its class, resource and level, and the parts sent just before it, which
/// replace the player's skills, passives and items.
/// </summary>
public struct NetLoadoutRpc : IRpcCommand
{
    public byte LocalIndex;
    public ClassType Class;
    public ResourceType Resource;
    public int Level;
    public int Experience;
}

/// <summary>
/// In someone else's game: sends each local player's loadout (class, resource, level, skills, passives and equipped items,
/// from its managers) to the host, which runs the player. Again whenever a manager changes it (MarkDirty), and for every
/// new player ghost this machine gets (joined, respawned, a new scene), which has none of it yet.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial class NetLoadoutSendSystem : SystemBase
{
    // One bit per local player; static, so a change made between worlds (a scene change) isn't lost
    static int _dirty;

    // The player ghost each local player's loadout was last sent for
    readonly Entity[] _sentFor = new Entity[GameInput.MaxPlayers];
    EntityQuery _connection;

    /// <summary>A manager changed this local player's loadout.</summary>
    public static void MarkDirty(int playerIndex) => _dirty |= 1 << playerIndex;

    protected override void OnCreate()
    {
        _connection = SystemAPI.QueryBuilder().WithAll<NetworkStreamConnection, NetworkStreamInGame>().Build();
    }

    protected override void OnUpdate()
    {
        if (NetSession.IsHost || _connection.IsEmpty) return;
        for (int i = 0; i < GameInput.MaxPlayers; i++)
        {
            var characters = CharacterManager.For(i);
            if (characters == null || characters.Player == Entity.Null) continue;
            if (characters.Player != _sentFor[i]) MarkDirty(i);
            if ((_dirty & (1 << i)) == 0) continue;

            Send(i, characters);
            _sentFor[i] = characters.Player;
            _dirty &= ~(1 << i);
        }
    }

    void Send(int localIndex, CharacterManager characters)
    {
        var index = (byte)localIndex;
        var skills = ActiveSkillManager.For(localIndex);
        if (skills != null)
        {
            var slots = new[] { skills.Slot1, skills.Slot2, skills.Slot3, skills.Slot4 };
            for (int slot = 0; slot < slots.Length; slot++)
                if (slots[slot] != null && !slots[slot].IsEmpty)
                    Rpc(new NetLoadoutPartRpc { LocalIndex = index, Part = NetLoadoutPart.Skill, Slot = (byte)slot, Type = (int)slots[slot].EquippedSkill.Type, Variant = slots[slot].Variant });
        }

        var passives = PassiveSkillManager.For(localIndex);
        if (passives != null)
            foreach (var passive in passives.Active)
                Rpc(new NetLoadoutPartRpc { LocalIndex = index, Part = NetLoadoutPart.Passive, Type = (int)passive.Type });

        var inventory = InventoryManager.For(localIndex);
        if (inventory != null)
            foreach (var item in inventory.EquippedItems)
                Rpc(new NetLoadoutPartRpc { LocalIndex = index, Part = NetLoadoutPart.Item, ItemId = item.BaseItem.Id, Seed = item.Seed, RollVersion = item.RollVersion });

        Rpc(new NetLoadoutRpc
        {
            LocalIndex = index,
            Class = characters.Class != null ? characters.Class.Type : ClassType.None,
            Resource = characters.Resource != null ? characters.Resource.Type : ResourceType.None,
            Level = characters.StartingLevel,
            Experience = characters.EarnedExperience,
        });
    }

    // Sent to the host (a client's only connection)
    void Rpc<T>(T rpc) where T : unmanaged, IRpcCommand
    {
        var entity = EntityManager.CreateEntity();
        EntityManager.AddComponentData(entity, rpc);
        EntityManager.AddComponent<SendRpcCommandRequest>(entity);
    }
}

/// <summary>
/// The host: gives a remote client's player the loadout it sent, with the same code that gives this machine's players
/// theirs (CharacterManager.ApplyCharacter, ActiveSkillManager.ApplySkills, PassiveSkillManager.ApplyPassives,
/// InventoryManager.ApplyEquipment). Skills, passives and items are found in the host's own lists by type or id; items are
/// rolled again from their seeds. The client is trusted.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial class NetLoadoutReceiveSystem : SystemBase
{
    // What is applied to a remote player entity, so the next loadout can replace it
    class Applied
    {
        public CharacterClass Class;
        public ResourceDefinition Resource;
        public int? Level;
        public readonly List<ActiveSkill> Skills = new List<ActiveSkill>();
        public readonly List<PassiveSkill> Passives = new List<PassiveSkill>();
        public readonly List<LegendaryPower> Powers = new List<LegendaryPower>();
    }

    // Parts waiting for their loadout, by connection (NetworkId) and local player
    readonly Dictionary<(int NetworkId, int LocalIndex), List<NetLoadoutPartRpc>> _parts = new Dictionary<(int, int), List<NetLoadoutPartRpc>>();
    readonly Dictionary<Entity, Applied> _applied = new Dictionary<Entity, Applied>();
    EntityQuery _partRequests, _loadoutRequests;

    protected override void OnCreate()
    {
        _partRequests = SystemAPI.QueryBuilder().WithAll<NetLoadoutPartRpc, ReceiveRpcCommandRequest>().Build();
        _loadoutRequests = SystemAPI.QueryBuilder().WithAll<NetLoadoutRpc, ReceiveRpcCommandRequest>().Build();
        RequireAnyForUpdate(_partRequests, _loadoutRequests);
    }

    protected override void OnUpdate()
    {
        // Parts first: they were sent before their loadout
        using (var parts = _partRequests.ToComponentDataArray<NetLoadoutPartRpc>(Allocator.Temp))
        using (var requests = _partRequests.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp))
            for (int i = 0; i < parts.Length; i++)
            {
                var key = (NetworkIdOf(requests[i].SourceConnection), (int)parts[i].LocalIndex);
                if (!_parts.TryGetValue(key, out var list)) _parts[key] = list = new List<NetLoadoutPartRpc>();
                list.Add(parts[i]);
            }
        EntityManager.DestroyEntity(_partRequests);

        // Copied out first: applying makes structural changes
        var loadouts = new List<(int NetworkId, NetLoadoutRpc Loadout)>();
        using (var received = _loadoutRequests.ToComponentDataArray<NetLoadoutRpc>(Allocator.Temp))
        using (var requests = _loadoutRequests.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp))
            for (int i = 0; i < received.Length; i++)
                loadouts.Add((NetworkIdOf(requests[i].SourceConnection), received[i]));
        EntityManager.DestroyEntity(_loadoutRequests);

        foreach (var (networkId, loadout) in loadouts)
        {
            var key = (networkId, (int)loadout.LocalIndex);
            _parts.Remove(key, out var parts);
            Apply(networkId, loadout, parts ?? new List<NetLoadoutPartRpc>());
        }
    }

    int NetworkIdOf(Entity connection) =>
        EntityManager.HasComponent<NetworkId>(connection) ? EntityManager.GetComponentData<NetworkId>(connection).Value : -1;

    // The host's own player 1's managers hold the lists of everything that can be played
    void Apply(int networkId, NetLoadoutRpc loadout, List<NetLoadoutPartRpc> parts)
    {
        var entityManager = EntityManager;
        var player = FindPlayer(networkId, loadout.LocalIndex);
        // Gone (died) meanwhile: the client sends it again for its next player
        if (player == Entity.Null) return;

        var characters = CharacterManager.For(0);
        var skills = ActiveSkillManager.For(0);
        var passives = PassiveSkillManager.For(0);
        if (characters == null || skills == null || passives == null)
        {
            Debug.LogWarning("NetLoadoutReceiveSystem: the host has no player 1 managers to find the loadout's skills in.");
            return;
        }

        // Entities that are gone no longer need their records
        var gone = new List<Entity>();
        foreach (var entity in _applied.Keys) if (!entityManager.Exists(entity)) gone.Add(entity);
        foreach (var entity in gone) _applied.Remove(entity);
        if (!_applied.TryGetValue(player, out var applied)) _applied[player] = applied = new Applied();

        var characterClass = characters.AllClasses.Find(candidate => candidate != null && candidate.Type == loadout.Class);
        var resource = characters.AllResources.Find(candidate => candidate != null && candidate.Type == loadout.Resource);
        CharacterManager.ApplyCharacter(entityManager, player, characterClass, resource, ref applied.Class, ref applied.Resource);
        // Only when the client's level changed (or on a new entity), so experience earned here isn't reset
        if (applied.Level != loadout.Level) CharacterManager.SetLevel(entityManager, player, loadout.Level, loadout.Experience);
        applied.Level = loadout.Level;

        var skillsBySlot = new ActiveSkill[4];
        var variants = new int[4];
        var activePassives = new List<PassiveSkill>();
        var items = new List<Item>();
        foreach (var part in parts)
        {
            switch (part.Part)
            {
                case NetLoadoutPart.Skill:
                    if (part.Slot >= skillsBySlot.Length) break;
                    skillsBySlot[part.Slot] = skills.AllSkills.Find(candidate => candidate != null && (int)candidate.Type == part.Type);
                    variants[part.Slot] = part.Variant;
                    break;
                case NetLoadoutPart.Passive:
                    var passive = passives.AllPassives.Find(candidate => candidate != null && (int)candidate.Type == part.Type);
                    if (passive != null && !activePassives.Contains(passive)) activePassives.Add(passive);
                    break;
                case NetLoadoutPart.Item:
                    var item = Roll(part);
                    if (item != null) items.Add(item);
                    break;
            }
        }
        ActiveSkillManager.ApplySkills(entityManager, player, skillsBySlot, variants, applied.Skills);
        PassiveSkillManager.ApplyPassives(entityManager, player, activePassives, applied.Passives);
        InventoryManager.ApplyEquipment(entityManager, player, items, applied.Powers);

        Debug.Log($"NetLoadoutReceiveSystem: player {loadout.LocalIndex + 1} of connection {networkId}: {loadout.Class} {loadout.Resource} " +
                  $"level {loadout.Level}, skills [{string.Join(", ", applied.Skills.ConvertAll(s => s.Type.ToString()))}], " +
                  $"passives [{string.Join(", ", applied.Passives.ConvertAll(p => p.Type.ToString()))}], {items.Count} items");
    }

    // The item the client has, rolled again from its seed, or null when this game can't make it
    static Item Roll(NetLoadoutPartRpc part)
    {
        var baseItem = AccountManager.Instance != null ? AccountManager.Instance.FindBaseItem(part.ItemId.ToString()) : null;
        var settings = LootGenerationManager.Instance != null ? LootGenerationManager.Instance.GenerationSettings : null;
        if (baseItem == null || settings == null || part.RollVersion != RollableItem.CurrentRollVersion)
        {
            Debug.LogWarning($"NetLoadoutReceiveSystem: can't make the item {part.ItemId} (roll version {part.RollVersion}) here, so it's left out.");
            return null;
        }
        return baseItem.RollFromSeed(part.Seed, settings);
    }

    Entity FindPlayer(int networkId, int localIndex)
    {
        foreach (var (owner, netPlayer, entity) in SystemAPI.Query<RefRO<GhostOwner>, RefRO<NetPlayer>>().WithAll<RemotePlayer>().WithEntityAccess())
            if (owner.ValueRO.NetworkId == networkId && netPlayer.ValueRO.LocalIndex == localIndex) return entity;
        return Entity.Null;
    }
}
