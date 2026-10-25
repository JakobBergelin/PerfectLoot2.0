using UnityEngine;

namespace LevelGeneration
{
    /// <summary>
    /// Put on an empty child of a tile or piece prefab: Prefab (a light, a barrel, ...) is spawned at this child's position
    /// and rotation, moved with the bent level but never stretched. Everything under this child is ignored by the tile
    /// geometry, so a preview model can sit here.
    /// </summary>
    public class WfcPropAuthoring : MonoBehaviour
    {
        public GameObject Prefab;
    }
}
