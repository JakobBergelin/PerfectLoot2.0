using UnityEngine;

/// <summary>
/// Keeps an ambient particle volume (embers, snow, spores) centered on the ground point the main camera looks at.
/// The particles simulate in world space, so moving the emitter doesn't drag the ones already in the air.
/// Visual only: nothing in gameplay reads it.
/// </summary>
public class AmbientParticlesFollow : MonoBehaviour
{
    [Tooltip("Height of the ground plane the camera's view is projected onto")]
    public float GroundHeight;

    void LateUpdate()
    {
        var camera = Camera.main;
        if (camera == null) return;
        var view = camera.transform;
        var forward = view.forward;
        var focus = forward.y < -0.05f ? view.position + forward * ((GroundHeight - view.position.y) / forward.y) : view.position;
        transform.position = new Vector3(focus.x, GroundHeight, focus.z);
    }
}
