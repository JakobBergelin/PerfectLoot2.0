using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

public class ProjectileAuthoring : MonoBehaviour
{
    public Vector3 Direction;
    public float Speed;
    public class Baker : Baker<ProjectileAuthoring>
    {
        public override void Bake(ProjectileAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Projectile
            {
                Direction = new float3(authoring.Direction.x, authoring.Direction.y,authoring.Direction.z),
                Speed = authoring.Speed,
            });
        }
    }
}

public struct Projectile : IComponentData
{    
    public float3 Direction;
    public float Speed;
    public bool DestroyOnHit;
    public int PierceCount;
    public int BounceCount;
}

