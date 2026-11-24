using System;
using System.Collections.Generic;
using Enemies;
using Unity.Entities;
using UnityEngine;

namespace Passives
{
    /// <summary>An enemy passive: other enemies appear around it as its health drops past each threshold (HealthSpawnsSystem).</summary>
    [CreateAssetMenu(fileName = "HealthSpawns", menuName = "Passives/Health Spawns")]
    public class HealthSpawnsPassive : PassiveSkill
    {
        [Serializable]
        public struct Threshold
        {
            [Range(0f, 1f), Tooltip("Goes off once health is at or below this share of its max")]
            public float HealthBelow;
            public EnemyDefinition Into;
            [Min(0)] public int Count;
        }

        public List<Threshold> Thresholds = new();
        [Tooltip("Meters from the enemy that the new enemies land")]
        public float Spread = 2f;

        public override PassiveType Type => PassiveType.HealthSpawns;

        public override void Apply(EntityManager entityManager, Entity entity)
        {
            var buffer = entityManager.HasBuffer<HealthSpawn>(entity) ? entityManager.GetBuffer<HealthSpawn>(entity) : entityManager.AddBuffer<HealthSpawn>(entity);
            buffer.Clear();
            foreach (var t in Thresholds)
                buffer.Add(new HealthSpawn { Into = t.Into, HealthBelow = t.HealthBelow, Count = t.Count, Spread = Spread });
        }

        public override void Remove(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasBuffer<HealthSpawn>(entity)) entityManager.RemoveComponent<HealthSpawn>(entity);
        }
    }
}
