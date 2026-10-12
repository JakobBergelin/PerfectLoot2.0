using System;
using UnityEngine;

namespace Loottables
{
    [Serializable]
    public class LootEntry
    {
        public Lootable Entry;
        [Tooltip("And mode: chance 0-1. Or mode: relative weight.")]
        public float Value = 1f;
    }
}
