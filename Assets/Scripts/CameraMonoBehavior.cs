using UnityEngine;
using Unity.Entities;
using Unity.Transforms;

/// <summary>
/// Copies the CameraFollow entity's transform (CoreEntities subscene) to this camera. Reads the current world every
/// frame: the camera is part of GameBootstrap's rig, and GameSceneLoader replaces the world on every scene change.
/// In the editor it also pushes Settings to the CameraFollow entity every frame, so edits to the asset in Play mode
/// show up live.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraMonoBehavior : MonoBehaviour
{
    [Tooltip("The same CameraSettings asset CameraFollowAuthoring bakes; sets the field of view")]
    public CameraSettings Settings;

    Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (Settings != null) cam.fieldOfView = Settings.FieldOfView;
    }

    void LateUpdate()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        using var cameraQuery = world.EntityManager.CreateEntityQuery(typeof(LocalTransform), typeof(CameraFollow));
        if (cameraQuery.IsEmpty) return;

#if UNITY_EDITOR
        // Live tuning: the new values take effect on CameraFollowSystem's next update
        if (Settings != null)
        {
            Entity cameraEntity = cameraQuery.GetSingletonEntity();
            var follow = world.EntityManager.GetComponentData<CameraFollow>(cameraEntity);
            Settings.ApplyTo(ref follow);
            world.EntityManager.SetComponentData(cameraEntity, follow);
            cam.fieldOfView = Settings.FieldOfView;
        }
#endif

        var cameraTransform = cameraQuery.GetSingleton<LocalTransform>();
        transform.position = cameraTransform.Position;
        transform.rotation = cameraTransform.Rotation;
    }
}
