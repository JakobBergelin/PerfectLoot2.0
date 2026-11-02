using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class CameraFollowAuthoring : MonoBehaviour
{
    [Tooltip("Framing and smoothing; edit this asset (also live in Play mode) to tune the camera")]
    public CameraSettings Settings;

    class Baker : Baker<CameraFollowAuthoring>
    {
        public override void Bake(CameraFollowAuthoring authoring)
        {
            DependsOn(authoring.Settings);
            if (authoring.Settings == null)
            {
                Debug.LogError("CameraFollowAuthoring has no CameraSettings assigned; the camera won't follow the player.", authoring);
                return;
            }

            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            var follow = new CameraFollow();
            authoring.Settings.ApplyTo(ref follow);
            AddComponent(entity, follow);
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
