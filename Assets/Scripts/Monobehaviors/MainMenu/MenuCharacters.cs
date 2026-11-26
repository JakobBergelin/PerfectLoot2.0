using System.Collections.Generic;
using LevelGeneration;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// The characters standing on the main menu's clearing (MenuSceneCamera's stage) while Character Select is open: player
/// 1's selected character (or a new one in no gear while one is being created) and, in couch co-op, player 2's picked
/// character beside it. Display only: the player's visual prefab (PlayerPrefab, CoreEntities) with its equipped items'
/// looks (EquippedVisual) and nothing of gameplay, so the rigs just stand in their Idle clip, facing the camera.
/// What to show is set from the menu (Player1, Creating, Visible); player 2's is read from AccountManager.
/// </summary>
public class MenuCharacters : MonoBehaviour
{
    /// <summary>Player 1's character to show, or null for none (or a new one while Creating).</summary>
    public static AccountCharacter Player1;
    /// <summary>A character is being created: player 1 is shown in no gear.</summary>
    public static bool Creating;
    /// <summary>Character Select is open; outside it nobody stands on the stage.</summary>
    public static bool Visible;

    [Tooltip("Meters between the two players, side by side")]
    public float Spacing = 1.6f;

    class Shown
    {
        public Entity Entity;
        public AccountCharacter Character;
        public bool New;
        public string Gear;
    }

    readonly Shown[] _shown = { new Shown(), new Shown() };
    World _world;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Player1 = null;
        Creating = false;
        Visible = false;
    }

    void OnDestroy()
    {
        Player1 = null;
        Creating = false;
        Visible = false;
    }

    void LateUpdate()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;
        if (world != _world)
        {
            _world = world;
            foreach (var shown in _shown) shown.Entity = Entity.Null;
        }
        var entityManager = world.EntityManager;
        using var prefabQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerPrefab>());
        if (prefabQuery.IsEmpty || !MenuSceneCamera.TryGetStage(out var stage)) return;
        var prefab = prefabQuery.GetSingleton<PlayerPrefab>().Value;

        var account = AccountManager.Instance;
        var player2 = Visible && GameInput.PlayerCount > 1 && account != null ? account.SelectedCharacterOf(1) : null;
        bool showPlayer1 = Visible && (Player1 != null || Creating);
        // Alone in the middle; two side by side, player 1 on the left
        bool both = showPlayer1 && player2 != null;
        Show(entityManager, prefab, 0, showPlayer1, Creating ? null : Player1, Creating, stage + new float3(both ? -Spacing * 0.5f : 0f, 0f, 0f));
        Show(entityManager, prefab, 1, player2 != null, player2, false, stage + new float3(both ? Spacing * 0.5f : 0f, 0f, 0f));
    }

    void Show(EntityManager entityManager, Entity prefab, int slot, bool visible, AccountCharacter character, bool isNew, float3 spot)
    {
        var shown = _shown[slot];
        if (!visible)
        {
            Remove(entityManager, shown);
            return;
        }

        string gear = isNew ? "" : GearKey(character);
        bool exists = shown.Entity != Entity.Null && entityManager.Exists(shown.Entity) && entityManager.HasComponent<LocalTransform>(shown.Entity);
        if (!exists)
        {
            shown.Entity = entityManager.Instantiate(prefab);
            MakeDisplayOnly(entityManager, shown.Entity);
            shown.Gear = null;
        }
        shown.Character = character;
        shown.New = isNew;

        // On the ground at the spot, facing the camera (south)
        var transform = entityManager.GetComponentData<LocalTransform>(shown.Entity);
        // From the prefab: the copy has no collider left to measure
        float bottom = GroundPlacement.BottomOffset(entityManager, prefab);
        if (GroundPlacement.TryGetCollisionWorld(entityManager, out var collisionWorld) && GroundPlacement.TryPlace(collisionWorld, spot, bottom, out var placed))
            spot = placed;
        transform.Position = spot;
        var toCamera = (float3)Camera.main.transform.position - spot;
        toCamera.y = 0f;
        transform.Rotation = quaternion.LookRotationSafe(math.normalizesafe(toCamera, new float3(0, 0, -1)), math.up());
        entityManager.SetComponentData(shown.Entity, transform);

        if (gear == shown.Gear || !entityManager.HasBuffer<EquippedVisual>(shown.Entity)) return;
        shown.Gear = gear;
        var visuals = entityManager.GetBuffer<EquippedVisual>(shown.Entity);
        visuals.Clear();
        foreach (var key in gear.Split('|'))
            if (key.Length > 0) visuals.Add(new EquippedVisual { Key = new FixedString64Bytes(key) });
        if (!entityManager.HasComponent<EquipmentVisualsChanged>(shown.Entity)) entityManager.AddComponent<EquipmentVisualsChanged>(shown.Entity);
    }

    // The player prefab is a networked, physical unit: strip it to a model, so Netcode doesn't take the copy for a ghost
    // (and delete it), physics doesn't move it, and its health and resource bars don't show
    static void MakeDisplayOnly(EntityManager entityManager, Entity entity)
    {
        using var types = entityManager.GetComponentTypes(entity);
        foreach (var type in types)
        {
            var managed = type.GetManagedType();
            if (managed == null) continue;
            bool netcode = managed.Namespace == "Unity.NetCode";
            bool physics = managed == typeof(Unity.Physics.PhysicsVelocity) || managed == typeof(Unity.Physics.PhysicsMass) ||
                           managed == typeof(Unity.Physics.PhysicsDamping) || managed == typeof(Unity.Physics.PhysicsCollider) ||
                           managed == typeof(Unity.Physics.GraphicsIntegration.PhysicsGraphicalSmoothing);
            // No health either: at 0 it would count as dead (CheckDeathSystem) and be removed
            bool gameplay = managed == typeof(NetPlayer) || managed == typeof(NetPlayerInput) || managed == typeof(PredictedMove) ||
                            managed == typeof(Health) || managed == typeof(Level) || managed == typeof(Resource) ||
                            managed == typeof(CalculatedStats) || managed == typeof(Skill) || managed == typeof(EffectDisplay);
            if (netcode || physics || gameplay) entityManager.RemoveComponent(entity, type);
        }
        foreach (var bar in new[] { BarEntities<HealthBarUI>(entityManager, entity), BarEntities<ResourceBarUI>(entityManager, entity) })
            foreach (var part in bar)
                if (part != Entity.Null && entityManager.Exists(part)) entityManager.AddComponent<Disabled>(part);
        if (entityManager.HasComponent<HealthBarUI>(entity)) entityManager.RemoveComponent<HealthBarUI>(entity);
        if (entityManager.HasComponent<ResourceBarUI>(entity)) entityManager.RemoveComponent<ResourceBarUI>(entity);
    }

    static Entity[] BarEntities<T>(EntityManager entityManager, Entity entity) where T : unmanaged, IComponentData
    {
        if (!entityManager.HasComponent<T>(entity)) return System.Array.Empty<Entity>();
        if (typeof(T) == typeof(HealthBarUI))
        {
            var bar = entityManager.GetComponentData<HealthBarUI>(entity);
            return new[] { bar.BarEntity, bar.StickEntity };
        }
        var resource = entityManager.GetComponentData<ResourceBarUI>(entity);
        return new[] { resource.BarEntity, resource.StickEntity };
    }

    static void Remove(EntityManager entityManager, Shown shown)
    {
        if (shown.Entity != Entity.Null && entityManager.Exists(shown.Entity)) entityManager.DestroyEntity(shown.Entity);
        shown.Entity = Entity.Null;
        shown.Gear = null;
    }

    // The looks of the character's equipped items (RollableItem.VisualKey), joined, so a change is noticed
    static string GearKey(AccountCharacter character)
    {
        var account = AccountManager.Instance;
        if (character == null || account == null) return "";
        var keys = new List<string>();
        foreach (var equipped in character.Equipped)
        {
            var item = account.FindBaseItem(equipped.BaseItemId);
            if (item != null) keys.Add(item.VisualKey);
        }
        return string.Join("|", keys);
    }
}
