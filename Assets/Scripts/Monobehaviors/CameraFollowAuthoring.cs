using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class CameraFollowAuthoring : MonoBehaviour
{
    public Vector3 Offset = new Vector3(0, 3, -10);
    public Quaternion Rotation;

    class Baker : Baker<CameraFollowAuthoring>
    {
        public override void Bake(CameraFollowAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new CameraFollow
            {
                Offset = authoring.Offset,
                Rotation = authoring.Rotation,
            });
        }
    }
}

public struct CameraFollow : IComponentData
{
    public float3 Offset;
    public Quaternion Rotation;
}