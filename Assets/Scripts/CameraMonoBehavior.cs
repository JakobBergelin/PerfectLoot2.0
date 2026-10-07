using UnityEngine;
using Unity.Entities;
using Unity.Transforms;


public class CameraMonoBehavior : MonoBehaviour
{
    EntityManager entityManager;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;        
    }

    // Update is called once per frame
    void LateUpdate()
    {
        var cameraQuery = entityManager.CreateEntityQuery(typeof(LocalTransform), typeof(CameraFollow));

        if (cameraQuery.IsEmpty == false)
        {
            var cameraTransform = cameraQuery.GetSingleton<LocalTransform>();

            transform.position = cameraTransform.Position;
            transform.rotation = cameraTransform.Rotation;
        }

    }
}
