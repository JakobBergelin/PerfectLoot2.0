using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>On a bar's quad (UnitBar.shader): gives it the per-unit fill and kind that HealthUISystem and ResourceUISystem write.</summary>
public class UnitBarAuthoring : MonoBehaviour
{
    public class Baker : Baker<UnitBarAuthoring>
    {
        public override void Bake(UnitBarAuthoring authoring)
        {
            // Full and hidden until its unit's system has decided what kind of bar it is
            AddComponent(GetEntity(TransformUsageFlags.Dynamic), new UnitBarParams { Value = new float4(1f, UnitBarKind.Hidden, 0f, 0f) });
        }
    }
}
