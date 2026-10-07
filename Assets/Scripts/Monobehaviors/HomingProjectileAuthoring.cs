using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

public class HomingProjectileAuthoring : MonoBehaviour
{
    public GameObject Target;
    public float TurnSpeed;
    public float TurnAcceleration;

    public HomingType HomingType;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public class Baker : Baker<HomingProjectileAuthoring>
    {
        public override void Bake(HomingProjectileAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new HomingProjectile
            {
                Target = GetEntity(authoring.Target, TransformUsageFlags.Dynamic),
                TurnSpeed = authoring.TurnSpeed,
                TurnAcceleration = authoring.TurnAcceleration,
                HomingType = authoring.HomingType,
            });
        }
    }
}

public enum HomingType
{
    Closest,
    Strongest,
    Weakest,
}

public struct HomingProjectile : IComponentData
{
    public Entity Target;
    public float TurnSpeed;
    public float TurnAcceleration;

    public HomingType HomingType;
}
