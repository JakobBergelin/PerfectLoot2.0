using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelGeneration
{
    public enum PlacementKind
    {
        Prop,           // An ECS prefab, spawned when its chunk is built
        EnemySpawner,   // A spawner (like the hand-placed ones) with one of the rule's groups
        EdgeProp,       // A prop along edges: chosen edge connections, or where walkable ground ends (torches, railings, clutter)
    }

    /// <summary>
    /// What to place procedurally on a level and where (LevelPlacement). Assigned on a LevelLayout; reusable between
    /// levels. Every rule fills the reachable floor with points at least Spacing apart, keeping those that pass its checks.
    /// </summary>
    [CreateAssetMenu(fileName = "PlacementSet", menuName = "Level/Placement Set")]
    public class LevelPlacementSet : ScriptableObject
    {
        public List<PlacementRule> Rules = new List<PlacementRule>();
    }

    [Serializable]
    public class PlacementRule
    {
        public string Name;
        public PlacementKind Kind;

        [Tooltip("Meters between placements of this rule (spawners: between any two spawners). The level is filled at this spacing.")]
        [Min(0.5f)] public float Spacing = 15f;
        [Tooltip("No wall or water within this many meters of the spot")]
        [Min(0f)] public float ClearRadius = 2f;
        [Tooltip("Not within this many meters of the player start")]
        [Min(0f)] public float MinDistanceFromStart;

        [Header("Prop")]
        [Tooltip("Spawned (as an entity) with a random yaw on the floor; edge props face the walkable (or lower) side of their edge")]
        public GameObject Prefab;

        [Header("Edge Prop")]
        [Tooltip("Edge connection Ids (from the level's tile set) to place along, e.g. a Cliff edge. Empty: along every edge " +
                 "where walkable ground meets anything else.")]
        public List<int> EdgeIds = new List<int>();

        /// <summary>The rule as the placement job sees it, with its edge Ids turned into the tile set's edge bits.</summary>
        public PlacementRuleData ToData(WfcTileSet tileSet)
        {
            ulong edges = 0ul;
            if (tileSet != null)
                foreach (int id in EdgeIds)
                {
                    int index = tileSet.EdgeIndex(id);
                    if (index >= 0 && index < WfcConnections.Max) edges |= WfcConnections.Bit(index);
                }
            return new PlacementRuleData
            {
                Kind = Kind, Spacing = Spacing, ClearRadius = ClearRadius, MinDistanceFromStart = MinDistanceFromStart,
                MaxSlope = MaxSlope, EdgeMask = edges, AlongWalkableBorder = EdgeIds.Count == 0,
            };
        }

        [Header("Enemy Spawner")]
        [Tooltip("Steepest ground (degrees) a spawner may stand on")]
        [Range(0f, 90f)] public float MaxSlope = 20f;
        [Tooltip("Horizontal distance from the player that triggers the spawn")]
        public float TriggerRadius = 35f;
        [Tooltip("How far around the spot the group is scattered")]
        public float SpawnRadius = 4f;
        [Tooltip("Each placed spawner picks one group, by weight")]
        public List<PlacementEnemyGroup> Groups = new List<PlacementEnemyGroup>();
    }

    [Serializable]
    public class PlacementEnemyGroup
    {
        [Min(0f)] public float Weight = 1f;
        public List<EnemySpawner.Entry> Enemies = new List<EnemySpawner.Entry>();
    }
}
