using Unity.Entities;
using UnityEngine;

/// <summary>Which effects a unit plays when hit and when it dies (VfxSystem). Units without one use UnitHit and UndeadDeath.</summary>
public struct UnitVfx : IComponentData
{
    public VfxType Hit;
    public VfxType Death;
}

/// <summary>On a unit's visual prefab root: its hit and death effects.</summary>
public class UnitVfxAuthoring : MonoBehaviour
{
    public VfxType Hit = VfxType.UnitHit;
    public VfxType Death = VfxType.UndeadDeath;

    public class Baker : Baker<UnitVfxAuthoring>
    {
        public override void Bake(UnitVfxAuthoring authoring)
        {
            AddComponent(GetEntity(TransformUsageFlags.Dynamic), new UnitVfx { Hit = authoring.Hit, Death = authoring.Death });
        }
    }
}
