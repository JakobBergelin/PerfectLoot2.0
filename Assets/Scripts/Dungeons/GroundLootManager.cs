using System.Collections.Generic;
using System.Linq;
using Equipment;
using LevelGeneration;
using Loottables;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Items lying on the ground (dungeon kills and completion, DungeonManager). GroundLootHUD shows a label for each;
/// clicking it within PickupRange picks the item up: it's given to AccountManager right away and waits in its drop queue
/// for review in town. Items left on the ground are lost when the scene changes (every scene load makes a new world).
/// Kept between scenes (GameBootstrap's rig).
/// </summary>
public class GroundLootManager : MonoBehaviour
{
    public class GroundItem
    {
        public Item Item;
        public Vector3 Position;
    }

    public static GroundLootManager Instance;

    [Tooltip("Meters (along the ground) from the player within which a dropped item can be picked up")]
    public float PickupRange = 6f;
    [Tooltip("Items from one drop land this many meters around its point, at least")]
    public float ScatterMin = 0.4f;
    [Tooltip("Items from one drop land this many meters around its point, at most")]
    public float ScatterMax = 1.6f;

    readonly List<GroundItem> _items = new List<GroundItem>();
    World _world;

    /// <summary>The items on the ground, oldest first.</summary>
    public IReadOnlyList<GroundItem> Items => _items;

    /// <summary>Changes whenever an item lands or is picked up (GroundLootHUD rebuilds its labels).</summary>
    public int Version { get; private set; }

    /// <summary>Items picked up in this scene.</summary>
    public int PickedUp { get; private set; }

    /// <summary>Raised when an item is picked up, after it's off the ground (GroundLootVfx bursts there).</summary>
    public event System.Action<GroundItem> ItemPickedUp;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // A new world is a new scene: what was left on the ground is gone
    void Update()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == _world) return;
        _world = world;
        _items.Clear();
        PickedUp = 0;
        Version++;
    }

    /// <summary>Rolls the table and drops every item it gives around the point. Gold isn't dropped yet.</summary>
    public void DropFromTable(LootTable table, Vector3 point)
    {
        if (table == null) return;
        var loot = LootGenerationManager.Instance;
        var account = AccountManager.Instance;
        if (loot == null || account == null)
        {
            Debug.LogWarning("GroundLootManager: no LootGenerationManager or AccountManager, so nothing drops.", this);
            return;
        }

        foreach (var candidate in table.Explore().OfType<Item>())
        {
            if (candidate.BaseItem == null) continue;
            var item = loot.RollItem(candidate.BaseItem);
            // Only items the account's database has entries for can be kept (LootGenerationManager's tables)
            if (!account.Items.Contains(ItemKey.Of(item)))
            {
                Debug.LogWarning($"GroundLootManager: {table.name} dropped {ItemKey.Of(item)}, which isn't in LootGenerationManager's loot tables, " +
                                 "so it can't be kept. Skipping it.", table);
                continue;
            }
            Drop(item, point);
        }
    }

    /// <summary>Puts the item on the ground near the point.</summary>
    public void Drop(Item item, Vector3 point)
    {
        if (item == null) return;
        float angle = Random.value * Mathf.PI * 2f;
        float distance = Mathf.Lerp(ScatterMin, Mathf.Max(ScatterMin, ScatterMax), Mathf.Sqrt(Random.value));
        var position = point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

        // On the floor there; the point's height where there's none (over a wall or water)
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated && GroundPlacement.TryGetCollisionWorld(world.EntityManager, out CollisionWorld collisionWorld) &&
            GroundPlacement.TryFindGround(collisionWorld, position, out var ground) && Mathf.Abs(ground.y - point.y) < 2f)
            position = ground;
        else
            position.y = point.y;

        _items.Add(new GroundItem { Item = item, Position = position });
        Version++;
    }

    /// <summary>Puts the item on the ground near the player, if there is one.</summary>
    public void DropNearPlayer(Item item)
    {
        if (TryGetPlayer(out var player)) Drop(item, player);
    }

    /// <summary>True when the player stands within PickupRange of the item.</summary>
    public bool InReach(GroundItem item)
    {
        if (item == null || !TryGetPlayer(out var player)) return false;
        var offset = new Vector2(item.Position.x - player.x, item.Position.z - player.z);
        return offset.sqrMagnitude <= PickupRange * PickupRange;
    }

    /// <summary>Picks the item up if it's in reach: it's the account's now (AccountManager.ReceiveDrop). False otherwise.</summary>
    public bool PickUp(GroundItem item)
    {
        if (!_items.Contains(item) || !InReach(item)) return false;
        var account = AccountManager.Instance;
        if (account == null) return false;
        _items.Remove(item);
        PickedUp++;
        Version++;
        account.ReceiveDrop(item.Item);
        ItemPickedUp?.Invoke(item);
        return true;
    }

    static bool TryGetPlayer(out float3 position)
    {
        position = default;
        var characterManager = CharacterManager.Instance;
        var world = World.DefaultGameObjectInjectionWorld;
        if (characterManager == null || world == null || !world.IsCreated) return false;
        var entityManager = world.EntityManager;
        var player = characterManager.Player;
        if (player == Entity.Null || !entityManager.Exists(player) || !entityManager.HasComponent<LocalTransform>(player)) return false;
        position = entityManager.GetComponentData<LocalTransform>(player).Position;
        return true;
    }
}
