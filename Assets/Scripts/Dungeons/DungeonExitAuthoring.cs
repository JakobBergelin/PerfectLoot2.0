using Unity.Entities;
using UnityEngine;

/// <summary>
/// A dungeon's exit: reaching it finishes a Reach Exit step. Painted on the level layout with the Prefab brush (the
/// prefab from Tools → Create Dungeon Prefabs).
/// </summary>
public class DungeonExitAuthoring : MonoBehaviour
{
    [Tooltip("Meters (along the ground) the player has to come within")]
    public float Range = 3f;
    [Tooltip("Scaled to Range, so the ring on the ground shows the exit. Its mesh has a radius of 1.")]
    public Transform RangeRing;

#if UNITY_EDITOR
    void OnValidate()
    {
        Range = Mathf.Max(0.1f, Range);
        // Not during OnValidate itself: changing a transform there warns (SendMessage)
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && RangeRing != null && RangeRing.localScale != new Vector3(Range, 1f, Range))
                RangeRing.localScale = new Vector3(Range, 1f, Range);
        };
    }
#endif

    public class Baker : Baker<DungeonExitAuthoring>
    {
        public override void Bake(DungeonExitAuthoring authoring)
        {
            AddComponent(GetEntity(TransformUsageFlags.Dynamic), new DungeonExit { Range = authoring.Range });
        }
    }
}
