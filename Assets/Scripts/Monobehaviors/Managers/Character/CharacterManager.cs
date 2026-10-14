using Characters;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class CharacterManager : MonoBehaviour
{
    public static CharacterManager Instance;

    [Header("Selected (empty = random from the lists below at start)")]
    public CharacterClass Class;
    public ResourceDefinition Resource;

    [Tooltip("Pushed to the player's Level at start and whenever this value changes. Class attributes scale with level.")]
    public int StartingLevel = 1;

    [Header("Everything that can be rolled at start")]
    public List<CharacterClass> AllClasses = new List<CharacterClass>();
    public List<ResourceDefinition> AllResources = new List<ResourceDefinition>();

    // What is currently applied to the player entity, so it can be replaced
    ResourceDefinition _appliedResource;
    int? _pushedLevel;
    bool _dirty;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        RandomizeEmpty();
        PushCharacter();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // The player entity may not exist yet (subscene still loading), so retry until it does.
    void Update()
    {
        if (_dirty) TryPushCharacter();
    }

    void RandomizeEmpty()
    {
        if (Class == null) Class = PickRandom(AllClasses);
        if (Resource == null) Resource = PickRandom(AllResources);
    }

    static T PickRandom<T>(List<T> options) where T : Object
    {
        var valid = options.FindAll(o => o != null);
        if (valid.Count == 0)
        {
            Debug.LogWarning($"CharacterManager has no {typeof(T).Name} to pick from.");
            return null;
        }
        return valid[Random.Range(0, valid.Count)];
    }

    /// <summary>Applies Class, Resource and StartingLevel to the player.</summary>
    [ContextMenu("Push character")]
    public void PushCharacter()
    {
        _dirty = true;
        TryPushCharacter();
    }

    void TryPushCharacter()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        var entityManager = world.EntityManager;
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>(), ComponentType.ReadWrite<UnitClass>(), ComponentType.ReadWrite<UnitResource>(), ComponentType.ReadWrite<Level>());
        if (query.IsEmpty) return;

        using var players = query.ToEntityArray(Allocator.Temp);
        foreach (var player in players)
        {
            if (Class != null) Class.Apply(entityManager, player);
            else CharacterClass.Remove(entityManager, player);

            if (_appliedResource != null && _appliedResource != Resource) _appliedResource.Remove(entityManager, player);
            if (Resource != null) Resource.Apply(entityManager, player);

            // Apply/Remove are structural changes, so set the records afterwards
            entityManager.SetComponentData(player, new UnitClass { Type = Class != null ? Class.Type : ClassType.None });
            entityManager.SetComponentData(player, new UnitResource { Type = Resource != null ? Resource.Type : ResourceType.None });

            // Only overwrite Level when StartingLevel changed, so a future XP system isn't reset
            if (_pushedLevel != StartingLevel)
            {
                var level = entityManager.GetComponentData<Level>(player);
                level.Value = StartingLevel;
                entityManager.SetComponentData(player, level);
            }
        }

        _appliedResource = Resource;
        _pushedLevel = StartingLevel;
        _dirty = false;
    }

    void OnValidate()
    {
        // Lets class/resource/level be changed in the Inspector during play mode
        if (Application.isPlaying && Instance == this) PushCharacter();

#if UNITY_EDITOR
        WarnAboutList(AllClasses, c => c.Type.ToString(), System.Enum.GetNames(typeof(ClassType)), "AllClasses", "classes");
        WarnAboutList(AllResources, r => r.Type.ToString(), System.Enum.GetNames(typeof(ResourceType)), "AllResources", "resources");
#endif
    }

#if UNITY_EDITOR
    void WarnAboutList<T>(List<T> list, System.Func<T, string> typeOf, string[] allTypes, string listName, string what) where T : Object
    {
        var seen = new HashSet<string>();
        foreach (var entry in list)
        {
            if (entry == null) { Debug.LogWarning($"{name}: {listName} has an empty entry.", this); continue; }
            if (!seen.Add(typeOf(entry))) Debug.LogWarning($"{name}: {typeOf(entry)} is in {listName} more than once.", this);
        }
        foreach (var type in allTypes)
            if (type != "None" && !seen.Contains(type))
                Debug.LogWarning($"{name}: {listName} is missing {type}. Run Tools > Create missing character assets.", this);
    }
#endif
}
