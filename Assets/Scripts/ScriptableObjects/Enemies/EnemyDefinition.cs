using UnityEngine;

namespace Enemies
{
    /// <summary>
    /// One enemy type: its visual prefab and everything gameplay. Spawned through EnemyManager; the prefab has to be
    /// registered in EnemyPrefabsAuthoring (Entities subscene) so it gets baked.
    /// </summary>
    [CreateAssetMenu(fileName = "Enemy", menuName = "Enemies/Enemy Definition")]
    public class EnemyDefinition : ScriptableObject
    {
        public string Name;
        [Tooltip("Visual-only prefab: mesh, collider, rigidbody and UnitBarsAuthoring (like PlayerVisuals)")]
        public GameObject Prefab;
        [Tooltip("Replace the prefab material's color with Tint")]
        public bool OverrideColor;
        public Color Tint = Color.white;

        [Header("Body")]
        [Tooltip("Physics: collides with other units and the player (body-blocks). Swarm: only collides with the world, " +
                 "much cheaper for big numbers. Must match the prefab's layer (Enemy or EnemySwarm).")]
        public BodyType BodyType = BodyType.Physics;
        [Tooltip("Uniform size; the collider and health bar scale with it")]
        public float Scale = 1f;
        [Tooltip("Heavier units push lighter ones aside")]
        public float Mass = 1f;

        [Tooltip("For enemies MovementSpeed is the speed in units per second (for the player it is a bonus)")]
        public StatBlock Stats = new StatBlock
        {
            Health = 10f,
            HealthRegeneration = 1f,
            MovementSpeed = 3f,
            AttackSpeed = 1f,
            CriticalChance = 0.2f,
            CriticalDamage = 1f,
            BlockChance = 0.05f,
        };

        [Header("Behavior")]
        [Tooltip("Chases and attacks the player. Off for training dummies (the behavior and melee values below are then unused).")]
        public bool HasAI = true;
        [Tooltip("Distance at which it notices the player")]
        public float DetectionRadius = 15f;
        [Tooltip("Distance at which it stops and attacks; the hit itself has half this radius")]
        public float AttackRadius = 3f;
        [Tooltip("How fast it turns toward the player")]
        public float TurnSpeed = 2f;
        [Tooltip("How much the turn speed grows per second while chasing")]
        public float TurnAcceleration = 1f;

        [Header("Melee attack")]
        public float MeleeDamage = 0.5f;
        [Tooltip("Seconds between attacks, scaled by the attack speed stat")]
        public float SecondsBetweenAttacks = 1f;

        public string DisplayName => string.IsNullOrEmpty(Name) ? name : Name;

        /// <summary>Capsule radius of the visual prefabs (0.5) at this definition's scale.</summary>
        public float BodyRadius => 0.5f * Scale;

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Prefab == null) return;
            string expectedLayer = BodyType == BodyType.Swarm ? "EnemySwarm" : "Enemy";
            if (LayerMask.LayerToName(Prefab.layer) != expectedLayer)
                Debug.LogWarning($"{name}: BodyType {BodyType} needs a prefab on the {expectedLayer} layer, but {Prefab.name} is on " +
                                 $"'{LayerMask.LayerToName(Prefab.layer)}'. Use EnemySwarmVisuals for Swarm, EnemyVisuals for Physics.", this);
        }
#endif
    }
}
