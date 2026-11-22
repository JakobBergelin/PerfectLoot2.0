using System;
using UnityEngine;

namespace LevelGeneration
{
    /// <summary>
    /// The small animals that live in a level, per biome: decoration only (no colliders, nothing to hit). Placed from the
    /// level's seed where they belong and moved by AmbientCreatureSystem on every machine that shows the level, so they
    /// cost no network traffic. Darters (squirrels, rabbits) live along the edge of the not-walkable ground and dash off
    /// from players; flyers (birds) peck on open ground and fly off to another spot; swimmers (fish) drift in the water and
    /// dart away from the bank.
    /// </summary>
    [CreateAssetMenu(menuName = "Perfect Loot/Level/Ambient Life", fileName = "AmbientLife")]
    public class AmbientLifeSet : ScriptableObject
    {
        public enum Kind : byte { Darter, Flyer, Swimmer }

        [Serializable]
        public class Creature
        {
            public GameObject Prefab;
            public Kind Kind;
            [Tooltip("How many live in a level")]
            [Min(0)] public int Count = 8;
            [Tooltip("Meters: a player this close makes it flee")]
            [Min(0f)] public float FleeRadius = 4f;
            [Tooltip("Meters per second when it moves on its own (fleeing is faster)")]
            [Min(0.1f)] public float Speed = 1.5f;
            [Tooltip("Meters above the ground it lives at (a fish just above the water's surface)")]
            public float Height;
            public Vector2 Scale = new Vector2(1.2f, 1.6f);
        }

        public Creature[] Creatures = new Creature[0];
    }
}
