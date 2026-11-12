using Equipment;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class InventoryManager : MonoBehaviour, IPerPlayer
{
    [Serializable]
    public class EquippedEntry
    {
        public string Slot;
        public string Item;
        public Rarity Rarity;
    }

    static readonly InventoryManager[] _players = new InventoryManager[GameInput.MaxPlayers];

    /// <summary>Player 1's.</summary>
    public static InventoryManager Instance => _players[0];

    /// <summary>A local player's (0 is player 1), or null when that player isn't in the game.</summary>
    public static InventoryManager For(int playerIndex) =>
        playerIndex >= 0 && playerIndex < _players.Length ? _players[playerIndex] : null;

    /// <summary>Which local player's this is: 0 is player 1 (the rig's own); player 2's copy is made by CoopManager.</summary>
    public int PlayerIndex { get; set; }
    public InventorySlot WeaponSlot, HeadSlot, BootsSlot, LegsSlot, ChestSlot, BeltSlot,
                         HandsSlot, ShouldersSlot, AmuletSlot, Ring1Slot, Ring2Slot,
                         OffhandSlot;

    [Header("Summary (read-only, rebuilt whenever a slot changes)")]
    public StatSummary Stats = new StatSummary();
    public List<EquippedEntry> Equipped = new List<EquippedEntry>();

    /// <summary>Raised after the summary has been rebuilt.</summary>
    public event Action<StatSummary> StatsChanged;

    (string Name, InventorySlot Slot)[] NamedSlots => new[]
    {
        (nameof(WeaponSlot), WeaponSlot), (nameof(HeadSlot), HeadSlot), (nameof(BootsSlot), BootsSlot),
        (nameof(LegsSlot), LegsSlot), (nameof(ChestSlot), ChestSlot), (nameof(BeltSlot), BeltSlot),
        (nameof(HandsSlot), HandsSlot), (nameof(ShouldersSlot), ShouldersSlot), (nameof(AmuletSlot), AmuletSlot),
        (nameof(Ring1Slot), Ring1Slot), (nameof(Ring2Slot), Ring2Slot), (nameof(OffhandSlot), OffhandSlot),
    };

    /// <summary>The names of the slots an item type goes in (as saved per character), in the order they're filled.</summary>
    public static string[] SlotNamesFor(ItemType type) => type switch
    {
        ItemType.Weapon => new[] { nameof(WeaponSlot) },
        ItemType.Head => new[] { nameof(HeadSlot) },
        ItemType.Chest => new[] { nameof(ChestSlot) },
        ItemType.Legs => new[] { nameof(LegsSlot) },
        ItemType.Belt => new[] { nameof(BeltSlot) },
        ItemType.Boots => new[] { nameof(BootsSlot) },
        ItemType.Hands => new[] { nameof(HandsSlot) },
        ItemType.Shoulders => new[] { nameof(ShouldersSlot) },
        ItemType.Amulet => new[] { nameof(AmuletSlot) },
        ItemType.Ring => new[] { nameof(Ring1Slot), nameof(Ring2Slot) },
        ItemType.Offhand => new[] { nameof(OffhandSlot) },
        _ => Array.Empty<string>(),
    };

    InventorySlot SlotNamed(string slotName)
    {
        foreach (var (name, slot) in NamedSlots)
            if (name == slotName) return slot;
        return null;
    }

    void Awake()
    {
        if (For(PlayerIndex) != null && For(PlayerIndex) != this) { Destroy(gameObject); return; }
        _players[PlayerIndex] = this;

        foreach (var (_, slot) in NamedSlots)
            if (slot != null) slot.Changed += OnSlotChanged;
        CharacterManager.PlayerSpawned += OnPlayerSpawned;
    }

    ItemDatabase _database;

    // AccountManager.Awake has run by now (it outlives scenes, so it may even be from an earlier one)
    void Start()
    {
        if (AccountManager.Instance != null)
        {
            _database = AccountManager.Instance.Items;
            _database.ItemChanged += OnDatabaseItemChanged;
        }
        else Debug.LogError("No AccountManager: inventory slots have no item database to read from.", this);
        LoadCharacterEquipment();
        RecalculateStats();
    }

    // The character picked in Character Select, whose equipment the slots show and save; null when the scene was played
    // without the main menu (the scene's own slots, not saved)
    AccountCharacter _character;
    bool _loadingEquipment;     // Equipping the saved items doesn't save them again

    void LoadCharacterEquipment()
    {
        var account = AccountManager.Instance;
        _character = account != null ? account.SelectedCharacterOf(PlayerIndex) : null;
        if (_character == null) return;

        _loadingEquipment = true;
        try
        {
            foreach (var (_, slot) in NamedSlots)
                if (slot != null) slot.Clear();
            foreach (var equipped in _character.Equipped)
            {
                var slot = SlotNamed(equipped.Slot);
                var baseItem = account.FindBaseItem(equipped.BaseItemId);
                if (slot == null || baseItem == null)
                {
                    Debug.LogWarning($"{_character.Name}'s {equipped.Rarity} item {equipped.BaseItemId} in {equipped.Slot}: " +
                                     (slot == null ? "no such slot" : "no such item in the database") + ", skipping it.", this);
                    continue;
                }
                Equip(slot, new ItemKey(baseItem, equipped.Rarity));    // Warns when it can't
            }
        }
        finally
        {
            _loadingEquipment = false;
        }
    }

    void SaveCharacterEquipment()
    {
        if (_character == null || AccountManager.Instance == null) return;
        var equipped = new List<AccountCharacter.EquippedItem>();
        foreach (var (name, slot) in NamedSlots)
        {
            if (slot == null || !slot.EquippedKey.IsSet) continue;
            var key = slot.EquippedKey;
            if (string.IsNullOrEmpty(key.BaseItem.Id))
            {
                Debug.LogWarning($"{key} has no Id, so it can't be saved as equipped. Run Tools → Assign Item IDs.", this);
                continue;
            }
            equipped.Add(new AccountCharacter.EquippedItem { Slot = name, BaseItemId = key.BaseItem.Id, Rarity = key.Rarity });
        }
        AccountManager.Instance.SetEquipped(_character, equipped);
    }

    void OnDestroy()
    {
        foreach (var (_, slot) in NamedSlots)
            if (slot != null) slot.Changed -= OnSlotChanged;
        CharacterManager.PlayerSpawned -= OnPlayerSpawned;
        if (_database != null) _database.ItemChanged -= OnDatabaseItemChanged;
        if (For(PlayerIndex) == this) _players[PlayerIndex] = null;
    }

    void OnSlotChanged(InventorySlot slot)
    {
        RecalculateStats();
        if (!_loadingEquipment) SaveCharacterEquipment();
    }

    // A database entry got a new item: every slot pointing to it now shows the new one
    void OnDatabaseItemChanged(ItemKey key)
    {
        if (IsEquipped(key)) RecalculateStats();
    }

    /// <summary>All inventory slots (null entries for unassigned slot references).</summary>
    public IEnumerable<InventorySlot> Slots
    {
        get { foreach (var (_, slot) in NamedSlots) yield return slot; }
    }

    /// <summary>The slot that has this key equipped, or null.</summary>
    public InventorySlot SlotWith(ItemKey key)
    {
        foreach (var (_, slot) in NamedSlots)
            if (slot != null && slot.EquippedKey == key) return slot;
        return null;
    }

    public bool IsEquipped(ItemKey key) => key.IsSet && SlotWith(key) != null;

    /// <summary>
    /// Equips a database item in a slot. Fails when the slot type doesn't match, the database has no item for the
    /// key, or another slot already has it (so e.g. both ring slots always hold different rings).
    /// </summary>
    public bool Equip(InventorySlot slot, ItemKey key)
    {
        if (slot == null || !key.IsSet) return false;
        if (key.BaseItem.ItemType != slot.SlotType)
        {
            Debug.LogWarning($"Can't put a {key.BaseItem.ItemType} into a {slot.SlotType} slot.", this);
            return false;
        }
        if (AccountManager.Instance == null || AccountManager.Instance.Items.Get(key) == null)
        {
            Debug.LogWarning($"The item database has no {key} to equip.", this);
            return false;
        }
        var other = SlotWith(key);
        if (other != null && other != slot)
        {
            Debug.LogWarning($"{key} is already equipped in {other.name}.", this);
            return false;
        }

        slot.SetKey(key);
        return true;
    }

    public void Unequip(InventorySlot slot)
    {
        if (slot != null) slot.Clear();
    }

    // New player entity (spawn/respawn): it has no equipment stats yet
    void OnPlayerSpawned(Entity player)
    {
        if (player != OwnPlayer) return;
        _appliedPowers.Clear();
        _equipmentStatsDirty = true;
        TryPushEquipmentStats();
    }

    /// <summary>Rebuilds Stats and Equipped from the current slots.</summary>
    [ContextMenu("Recalculate")]
    public void RecalculateStats()
    {
        Stats.Clear();
        Equipped.Clear();

        foreach (var (name, slot) in NamedSlots)
        {
            if (slot == null || slot.IsEmpty) continue;
            Stats.AddItem(slot.EquippedItem);
            Equipped.Add(new EquippedEntry
            {
                Slot = name,
                Item = slot.EquippedItem.BaseItem.Name,
                Rarity = slot.EquippedItem.Rarity
            });
        }

        Stats.Sort();
        _equipmentStatsDirty = true;
        TryPushEquipmentStats();
        StatsChanged?.Invoke(Stats);
    }

    bool _equipmentStatsDirty;

    // The player entity may not exist yet when slots change (subscene still loading), so retry until it does.
    void Update()
    {
        if (_equipmentStatsDirty) TryPushEquipmentStats();
    }

    /// <summary>Writes the summed equipment stats to the player's EquipmentStats component.</summary>
    void TryPushEquipmentStats()
    {
        // In someone else's game the host runs this player: the loadout is sent to it instead (NetLoadoutSendSystem)
        if (!NetSession.IsHost)
        {
            NetLoadoutSendSystem.MarkDirty(PlayerIndex);
            _equipmentStatsDirty = false;
            return;
        }

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        var entityManager = world.EntityManager;
        var player = OwnPlayer;
        if (player == Entity.Null || !entityManager.Exists(player) || !entityManager.HasComponent<EquipmentStats>(player)) return;

        ApplyEquipment(entityManager, player, EquippedItems, _appliedPowers);
        _equipmentStatsDirty = false;
    }

    /// <summary>The items in the slots, in slot order.</summary>
    public List<Item> EquippedItems
    {
        get
        {
            var items = new List<Item>();
            foreach (var (_, slot) in NamedSlots)
                if (slot != null && !slot.IsEmpty) items.Add(slot.EquippedItem);
            return items;
        }
    }

    /// <summary>
    /// Gives a player entity what these equipped items add: their summed stats (EquipmentStats), the powers of the
    /// Legendary/Unique ones (each power once, removing the ones applied before that aren't among them: appliedPowers,
    /// updated) and their 3D parts. Used for this machine's players and, on the host, for remote ones (NetLoadoutReceiveSystem).
    /// </summary>
    public static void ApplyEquipment(EntityManager entityManager, Entity player, IReadOnlyList<Item> items, List<LegendaryPower> appliedPowers)
    {
        // Rolled affixes plus fixed per-item stats (weapon reach, weapon damage range)
        var summary = new StatSummary();
        foreach (var item in items) summary.AddItem(item);
        var block = summary.ToStatBlock();
        foreach (var item in items) item.BaseItem.AddFixedStats(ref block);

        entityManager.SetComponentData(player, new EquipmentStats { Value = block });
        SyncLegendaryPowers(entityManager, player, items, appliedPowers);
        SyncVisuals(entityManager, player, items);
    }

    // This player's entity (CharacterManager with the same PlayerIndex), or Entity.Null
    Entity OwnPlayer
    {
        get
        {
            var characters = CharacterManager.For(PlayerIndex);
            return characters != null ? characters.Player : Entity.Null;
        }
    }

    // Powers currently applied to the player entity, so unequipped ones can be removed
    readonly List<LegendaryPower> _appliedPowers = new List<LegendaryPower>();

    /// <summary>Applies the powers of equipped Legendary/Unique items (each power once) and removes ones no longer equipped.</summary>
    static void SyncLegendaryPowers(EntityManager entityManager, Entity player, IReadOnlyList<Item> items, List<LegendaryPower> appliedPowers)
    {
        var equipped = new List<LegendaryPower>();
        foreach (var item in items)
            if (item.BaseItem is ILegendaryItem legendary && legendary.Power != null && !equipped.Contains(legendary.Power))
                equipped.Add(legendary.Power);

        foreach (var power in appliedPowers)
            if (!equipped.Contains(power)) power.Remove(entityManager, player);
        // Apply also refreshes already applied powers, so edited values take effect
        foreach (var power in equipped)
            power.Apply(entityManager, player);

        appliedPowers.Clear();
        appliedPowers.AddRange(equipped);
    }

    /// <summary>Tells the player's visual which items it wears (EquipmentVisualSystem attaches their 3D parts).</summary>
    static void SyncVisuals(EntityManager entityManager, Entity player, IReadOnlyList<Item> items)
    {
        if (!entityManager.HasBuffer<EquippedVisual>(player)) return;
        var visuals = entityManager.GetBuffer<EquippedVisual>(player);
        visuals.Clear();
        foreach (var item in items)
            visuals.Add(new EquippedVisual { Key = item.BaseItem.VisualKey });
        if (!entityManager.HasComponent<EquipmentVisualsChanged>(player)) entityManager.AddComponent<EquipmentVisualsChanged>(player);
    }

    [Header("Testing")]
    [Tooltip("Rolled and equipped by the \"Roll and equip test item\" context menu item (Play mode)")]
    public RollableItem TestItem;

    // Rolls TestItem, stores it in the account database (replacing the item there) and equips it
    [ContextMenu("Roll and equip test item")]
    void RollAndEquipTestItem()
    {
        if (!Application.isPlaying || TestItem == null || LootGenerationManager.Instance == null || AccountManager.Instance == null)
        {
            Debug.LogWarning("Needs Play mode, a TestItem, a LootGenerationManager and an AccountManager.", this);
            return;
        }

        var item = LootGenerationManager.Instance.RollItem(TestItem);
        if (!AccountManager.Instance.Items.Store(item)) return;
        var key = ItemKey.Of(item);

        // Already equipped: storing it updated that slot. Otherwise the matching slot; for rings the first empty one.
        var target = SlotWith(key);
        if (target == null)
        {
            foreach (var (_, slot) in NamedSlots)
            {
                if (slot == null || slot.SlotType != TestItem.ItemType) continue;
                if (target == null) target = slot;
                if (slot.IsEmpty) { target = slot; break; }
            }
            if (target == null)
            {
                Debug.LogWarning($"No {TestItem.ItemType} slot to equip {TestItem.name} in. It's stored in the database.", this);
                return;
            }
            Equip(target, key);
        }
        Debug.Log($"Equipped {key} in {target.name}.", this);
    }

    [ContextMenu("Log Summary")]
    void LogSummary()
    {
        var sb = new StringBuilder("Equipped:\n");
        foreach (var e in Equipped) sb.AppendLine($"  {e.Slot}: {e.Item} ({e.Rarity})");
        sb.AppendLine("Totals:");
        sb.Append(Stats);
        Debug.Log(sb.ToString());
    }
}
