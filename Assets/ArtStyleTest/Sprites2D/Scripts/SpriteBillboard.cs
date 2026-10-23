using UnityEngine;

namespace PerfectLoot.ArtStyleTest
{
    /// <summary>
    /// Keeps a sprite parallel to the camera's view plane (pivot at the feet stays on the ground).
    /// Sprites are pre-rendered at the camera's pitch, so matching the camera rotation shows them undistorted.
    /// </summary>
    [ExecuteAlways]
    public class SpriteBillboard : MonoBehaviour
    {
        [Tooltip("Leave empty to use Camera.main")]
        public Camera targetCamera;

        [Tooltip("Lay the sprite flat on the ground instead (decals such as lava cracks)")]
        public bool groundDecal;

        void LateUpdate()
        {
            if (groundDecal)
            {
                transform.rotation = Quaternion.Euler(90f, transform.parent ? transform.parent.eulerAngles.y : 0f, 0f);
                return;
            }
            var cam = targetCamera ? targetCamera : Camera.main;
            if (cam) transform.rotation = cam.transform.rotation;
        }
    }
}
