using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class CameraFollowAuthoring : MonoBehaviour
{
    public Vector3 Offset = new Vector3(0, 3, -10);
    public Quaternion Rotation;
    [Tooltip("Roughly the seconds the camera takes to catch up with the player")]
    public float SmoothTime = 0.15f;
    [Tooltip("Meters per second the camera can move at most; keep it above the player's fastest walk")]
    public float MaxSpeed = 30f;
    [Tooltip("When the player is further than this (meters) from where the camera is heading, it jumps there instead")]
    public float SnapDistance = 20f;

    class Baker : Baker<CameraFollowAuthoring>
    {
        public override void Bake(CameraFollowAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new CameraFollow
            {
                Offset = authoring.Offset,
                Rotation = authoring.Rotation,
                SmoothTime = authoring.SmoothTime,
                MaxSpeed = authoring.MaxSpeed,
                SnapDistance = authoring.SnapDistance,
            });
        }
    }
}

public struct CameraFollow : IComponentData
{
    public float3 Offset;
    public Quaternion Rotation;
    public float SmoothTime;
    public float MaxSpeed;
    public float SnapDistance;

    public float3 Velocity;     // SmoothDamp's running velocity
    public bool HasTarget;      // False until a player is found (and while there is none), so the camera snaps to it first
}
