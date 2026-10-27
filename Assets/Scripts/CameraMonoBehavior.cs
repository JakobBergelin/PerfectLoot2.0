using UnityEngine;
using Unity.Entities;
using Unity.Transforms;

/// <summary>
/// Copies the CameraFollow entity's transform (CoreEntities subscene) to this camera. Reads the current world every
/// frame: the camera is part of GameBootstrap's rig, and GameSceneLoader replaces the world on every scene change.
/// </summary>
public class CameraMonoBehavior : MonoBehaviour
{
    void LateUpdate()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        using var cameraQuery = world.EntityManager.CreateEntityQuery(typeof(LocalTransform), typeof(CameraFollow));
        if (cameraQuery.IsEmpty) return;

        var cameraTransform = cameraQuery.GetSingleton<LocalTransform>();
        transform.position = cameraTransform.Position;
        transform.rotation = cameraTransform.Rotation;
    }
}
