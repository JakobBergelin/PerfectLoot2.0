using UnityEngine;
using Unity.Entities;

/// <summary>
/// Visual part of a unit: links its health/resource bar child objects. Used on visual-only prefabs
/// (e.g. PlayerVisual) whose gameplay components are added at runtime.
/// </summary>
public class UnitBarsAuthoring : MonoBehaviour
{
    public GameObject HealthBarPivotGameObject;
    public GameObject HealthBarStickGameObject;
    public GameObject ResourceBarPivotGameObject;
    public GameObject ResourceBarStickGameObject;

    public class Baker : Baker<UnitBarsAuthoring>
    {
        public override void Bake(UnitBarsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            if (authoring.HealthBarPivotGameObject != null && authoring.HealthBarStickGameObject != null)
            {
                AddComponent(entity, new HealthBarUI
                {
                    PivotEntity = GetEntity(authoring.HealthBarPivotGameObject, TransformUsageFlags.NonUniformScale),
                    StickEntity = GetEntity(authoring.HealthBarStickGameObject, TransformUsageFlags.NonUniformScale)
                });
            }
            if (authoring.ResourceBarPivotGameObject != null && authoring.ResourceBarStickGameObject != null)
            {
                AddComponent(entity, new ResourceBarUI
                {
                    PivotEntity = GetEntity(authoring.ResourceBarPivotGameObject, TransformUsageFlags.NonUniformScale),
                    StickEntity = GetEntity(authoring.ResourceBarStickGameObject, TransformUsageFlags.NonUniformScale)
                });
            }
        }
    }
}
