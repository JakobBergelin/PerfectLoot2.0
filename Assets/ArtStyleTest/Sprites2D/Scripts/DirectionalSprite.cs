using UnityEngine;

namespace PerfectLoot.ArtStyleTest
{
    /// <summary>
    /// Doom-style 8-direction sprite: picks the frame that matches the angle between
    /// the actor's facing (root transform) and the camera.
    /// Frame 0 = facing the camera, frames continue clockwise when seen from above
    /// (frame k shows the actor turned k * 45 degrees to the screen-right of the camera).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class DirectionalSprite : MonoBehaviour
    {
        [Tooltip("8 frames, see class summary for order")]
        public Sprite[] frames = new Sprite[8];

        [Tooltip("Transform whose forward is the actor's facing. Defaults to the parent.")]
        public Transform facing;

        public Camera targetCamera;

        SpriteRenderer _renderer;

        void OnEnable() => _renderer = GetComponent<SpriteRenderer>();

        void LateUpdate()
        {
            if (frames == null || frames.Length == 0) return;
            var cam = targetCamera ? targetCamera : Camera.main;
            var root = facing ? facing : (transform.parent ? transform.parent : transform);
            if (!cam || !_renderer) return;

            Vector3 toCam = cam.transform.position - root.position; toCam.y = 0f;
            Vector3 fwd = root.forward; fwd.y = 0f;
            if (toCam.sqrMagnitude < 1e-6f || fwd.sqrMagnitude < 1e-6f) return;

            float rel = Vector3.SignedAngle(toCam, fwd, Vector3.up);
            int n = frames.Length;
            float step = 360f / n;
            int index = Mathf.RoundToInt(-rel / step) % n;
            if (index < 0) index += n;
            var s = frames[index];
            if (s && _renderer.sprite != s) _renderer.sprite = s;
        }
    }
}
