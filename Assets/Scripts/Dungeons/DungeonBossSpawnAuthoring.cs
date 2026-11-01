using Unity.Entities;
using UnityEngine;

/// <summary>
/// Where a dungeon's Kill Boss steps spawn their boss. Painted on the level layout with the Prefab brush (the prefab
/// from Tools → Create Dungeon Prefabs). It only exists once the player has come near enough for its part of the level
/// to be built; until then the boss spawns near the player.
/// </summary>
public class DungeonBossSpawnAuthoring : MonoBehaviour
{
    public class Baker : Baker<DungeonBossSpawnAuthoring>
    {
        public override void Bake(DungeonBossSpawnAuthoring authoring)
        {
            AddComponent<DungeonBossSpawnPoint>(GetEntity(TransformUsageFlags.Dynamic));
        }
    }
}
