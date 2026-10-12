using System.Collections.Generic;
using UnityEngine;

namespace Loottables
{
    public abstract class Lootable : ScriptableObject
    {
        public abstract List<Loot> Explore();
    }
}
