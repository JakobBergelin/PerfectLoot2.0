using System.Collections.Generic;
using EnemySkills;
using Passives;
using UnityEngine;

namespace Enemies
{
    /// <summary>
    /// One enemy type: its visual prefab and everything gameplay. Spawned through EnemyManager; the prefab has to be
    /// registered in EnemyPrefabsAuthoring (Entities subscene) so it gets baked.
    /// </summary>
    [CreateAssetMenu(fileName = "Enemy", menuName = "Enemies/Enemy Definition")]
    public class EnemyDefinition : ScriptableObject, IUnitDescription
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
        [Tooltip("Chases and attacks the player. Off for training dummies (the behavior, skill and melee values below are then unused).")]
        public bool HasAI = true;
        [Tooltip("Distance at which it sees the player, with line of sight; shrinks toward Proximity Radius for a player " +
                 "behind it. Swarm: notices the player within this distance, no line of sight.")]
        public float DetectionRadius = 15f;
        [Tooltip("Physics only: notices the player this close even through walls")]
        public float ProximityRadius = 4f;
        [Tooltip("Physics only: when it finds the player itself, enemies within this many meters join in")]
        public float AlertRadius = 10f;
        [Tooltip("A player attack missing it by up to this many meters (beyond the attack's radius) makes it notice the player")]
        public float NearMissMargin = 2f;
        [Tooltip("Physics only: gives up after this many seconds without seeing, being hit by or being alerted to the player")]
        public float ForgetTime = 5f;
        [Tooltip("Distance from the target at which it stops walking. Swarm: also where it attacks, with a hit of half this radius.")]
        public float AttackRadius = 3f;
        [Tooltip("How fast it turns toward the player")]
        public float TurnSpeed = 2f;
        [Tooltip("How much the turn speed grows per second while chasing")]
        public float TurnAcceleration = 1f;

        [Header("Ranged")]
        [Tooltip("Distance it tries to keep from its target: it backs away from a closer one, and walks closer to a farther one or " +
                 "one it can't see. 0: not ranged, it walks up to Attack Radius. Physics only.")]
        public float DesiredDistance;
        [Tooltip("How many meters off Desired Distance it may be before it moves")]
        public float DistanceTolerance = 1.5f;

        [Header("Skills")]
        [Tooltip("In priority order: it uses the first one that's ready, so big skills go first and basic attacks last. Empty: it " +
                 "never attacks. One skill of each kind per enemy. Physics only: Swarm enemies use the melee attack below.")]
        public List<EnemySkill> Skills = new List<EnemySkill>();

        [Header("Passives")]
        [Tooltip("The same assets players and companions use. Swarm enemies can have them too.")]
        public List<PassiveSkill> Passives = new List<PassiveSkill>();

        [Header("Melee attack (Swarm only)")]
        public float MeleeDamage = 0.5f;
        [Tooltip("Seconds between attacks, scaled by the attack speed stat")]
        public float SecondsBetweenAttacks = 1f;
        [Tooltip("Degrees per second it turns toward the player while attacking")]
        public float AttackTurnSpeed = 360f;
        [Tooltip("Attacks only once it faces within this many degrees of the player")]
        public float AttackFacingAngle = 60f;

        public string DisplayName => string.IsNullOrEmpty(Name) ? name : Name;
        List<PassiveSkill> IUnitDescription.Passives => Passives;
        List<EnemySkill> IUnitDescription.Skills => Skills;

        /// <summary>Capsule radius of the visual prefabs (0.5) at this definition's scale.</summary>
        public float BodyRadius => 0.5f * Scale;

#if UNITY_EDITOR
        void OnValidate()
        {
            if (BodyType == BodyType.Swarm && (Skills.Count > 0 || DesiredDistance > 0f))
                Debug.LogWarning($"{name}: Swarm enemies only use the melee attack, so their skills and Desired Distance do nothing.", this);
            var kinds = new HashSet<EnemySkillType>();
            foreach (var skill in Skills)
                if (skill != null && !kinds.Add(skill.Type))
                    Debug.LogWarning($"{name}: more than one {skill.Type} skill; an enemy can only have one of each kind.", this);

            if (Prefab == null) return;
            string expectedLayer = BodyType == BodyType.Swarm ? "EnemySwarm" : "Enemy";
            if (LayerMask.LayerToName(Prefab.layer) != expectedLayer)
                Debug.LogWarning($"{name}: BodyType {BodyType} needs a prefab on the {expectedLayer} layer, but {Prefab.name} is on " +
                                 $"'{LayerMask.LayerToName(Prefab.layer)}'. Use EnemySwarmVisuals for Swarm, EnemyVisuals for Physics.", this);
        }
#endif
    }
}
