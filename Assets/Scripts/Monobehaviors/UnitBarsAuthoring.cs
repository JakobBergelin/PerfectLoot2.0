using UnityEngine;
using UnityEngine.Serialization;
using Unity.Entities;

/// <summary>
/// Visual part of a unit: links its health/resource bar child objects. Used on visual-only prefabs
/// (e.g. PlayerVisual) whose gameplay components are added at runtime. Each bar is a Stick, turned to face the
/// camera, holding the Bar quad (UnitBarAuthoring) that UnitBar.shader draws.
/// </summary>
public class UnitBarsAuthoring : MonoBehaviour
{
    [FormerlySerializedAs("HealthBarPivotGameObject")] public GameObject HealthBarGameObject;
    public GameObject HealthBarStickGameObject;
    [FormerlySerializedAs("ResourceBarPivotGameObject")] public GameObject ResourceBarGameObject;
    public GameObject ResourceBarStickGameObject;

    public class Baker : Baker<UnitBarsAuthoring>
    {
        public override void Bake(UnitBarsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            // The player visual is the rendered part of the player, so it carries the hit flash
            HitFlashBaking.Add(this, entity);
            if (authoring.HealthBarGameObject != null && authoring.HealthBarStickGameObject != null)
            {
                AddComponent(entity, new HealthBarUI
                {
                    BarEntity = GetEntity(authoring.HealthBarGameObject, TransformUsageFlags.Dynamic),
                    StickEntity = GetEntity(authoring.HealthBarStickGameObject, TransformUsageFlags.Dynamic)
                });
            }
            if (authoring.ResourceBarGameObject != null && authoring.ResourceBarStickGameObject != null)
            {
                AddComponent(entity, new ResourceBarUI
                {
                    BarEntity = GetEntity(authoring.ResourceBarGameObject, TransformUsageFlags.Dynamic),
                    StickEntity = GetEntity(authoring.ResourceBarStickGameObject, TransformUsageFlags.Dynamic)
                });
            }
        }
    }
}
