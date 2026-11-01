using LevelGeneration;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Whether the current scene is a town or a dungeon (its LevelLayout's LevelArea). A scene without a layout is a town,
/// so test scenes keep everything. Neither while the scene is still loading.
/// </summary>
public static class GameArea
{
    static World _world;
    static bool _known;
    static LevelArea _area;

    /// <summary>The scene is a town: items and skills can be changed, and found items are reviewed.</summary>
    public static bool IsTown => Refresh() && _area.Kind == AreaKind.Town;

    /// <summary>The scene is a dungeon (DungeonManager runs it).</summary>
    public static bool IsDungeon => Refresh() && _area.Kind == AreaKind.Dungeon;

    /// <summary>The scene's area, once its subscenes are loaded.</summary>
    public static bool TryGet(out LevelArea area)
    {
        bool known = Refresh();
        area = _area;
        return known;
    }

    // Read once per world: the layout's LevelArea when its subscene is loaded, or Town once everything has loaded without one
    static bool Refresh()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != _world)
        {
            _world = world;
            _known = false;
            _area = default;
        }
        if (_known) return true;
        if (world == null || !world.IsCreated) return false;

        var entityManager = world.EntityManager;
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<LevelArea>());
        if (!query.IsEmpty)
        {
            using var areas = query.ToComponentDataArray<LevelArea>(Allocator.Temp);
            _area = areas[0];
            _known = true;
        }
        else if (LevelState.IsReady(entityManager))
        {
            _area = new LevelArea { Kind = AreaKind.Town };
            _known = true;
        }
        return _known;
    }

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _world = null;
        _known = false;
        _area = default;
    }
}
