using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelGeneration
{
    public enum PlacementKind
    {
        Prop,           // An ECS prefab, spawned when its chunk is built
        EnemySpawner,   // A spawner (like the hand-placed ones) with one of the rule's groups
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
        [Tooltip("Spawned (as an entity) with a random yaw on the floor")]
        public GameObject Prefab;

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
