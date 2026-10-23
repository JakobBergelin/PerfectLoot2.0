using UnityEngine;
using UnityEngine.UI;

namespace PerfectLoot.ArtStyleTest
{
    /// <summary>
    /// Renders the camera into a low-resolution render texture and shows it full-screen with point
    /// filtering, so the 3D scene reads as chunky pixel art. Works in the Game view in edit mode too.
    ///
    /// Pixel-stable movement (orthographic cameras): the camera position is snapped to whole pixels in
    /// its own view plane, and the upscaled image is shifted by the leftover sub-pixel amount. Pixels
    /// never shimmer or crawl, but movement still looks smooth. The render texture has a 1 px margin
    /// on every side so the shift never reveals an empty edge.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class PixelArtCamera : MonoBehaviour
    {
        [Tooltip("Height of the low-res image in pixels")]
        public int verticalResolution = 270;

        [Tooltip("Visible half-height in world units (orthographic cameras). 0 = keep the camera's own size.")]
        public float orthographicSize = 0f;

        [Header("Follow (play mode)")]
        public Transform followTarget;
        [Tooltip("Camera position relative to the target. Captured automatically when left at zero.")]
        public Vector3 followOffset;
        public bool snapToPixels = true;

        const int Margin = 1;

        Camera _cam;
        RenderTexture _rt;
        GameObject _canvasGo;
        RawImage _img;
        Vector3 _desiredPosition;
        bool _hasDesired;

        void OnEnable()
        {
            _cam = GetComponent<Camera>();
            if (_cam.orthographic && orthographicSize <= 0f) orthographicSize = _cam.orthographicSize;
            _hasDesired = false;
            EnsureDisplay();
        }

        void OnDisable()
        {
            if (_cam)
            {
                _cam.targetTexture = null;
                if (_cam.orthographic && orthographicSize > 0f) _cam.orthographicSize = orthographicSize;
            }
            if (_canvasGo) DestroySafe(_canvasGo);
            if (_rt) { _rt.Release(); DestroySafe(_rt); }
            _canvasGo = null; _rt = null; _img = null;
        }

        void LateUpdate()
        {
            if (!_cam) return;
            EnsureDisplay();

            int h = Mathf.Max(64, verticalResolution);
            float upp = _cam.orthographic && orthographicSize > 0f ? 2f * orthographicSize / h : 0f; // world units per pixel
            Vector2 subPixel = Vector2.zero;

            if (Application.isPlaying)
            {
                if (followTarget)
                {
                    if (followOffset == Vector3.zero) followOffset = transform.position - followTarget.position;
                    _desiredPosition = followTarget.position + followOffset;
                    _hasDesired = true;
                }
                else if (!_hasDesired)
                {
                    _desiredPosition = transform.position;
                    _hasDesired = true;
                }

                if (snapToPixels && upp > 0f)
                {
                    Quaternion rot = transform.rotation;
                    Vector3 local = Quaternion.Inverse(rot) * _desiredPosition;
                    Vector3 snapped = new Vector3(Mathf.Round(local.x / upp) * upp, Mathf.Round(local.y / upp) * upp, local.z);
                    subPixel = new Vector2((local.x - snapped.x) / upp, (local.y - snapped.y) / upp);
                    transform.position = rot * snapped;
                }
                else
                {
                    transform.position = _desiredPosition;
                }
            }

            if (_img && _rt)
            {
                float w = _rt.width - 2 * Margin, hh = _rt.height - 2 * Margin;
                _img.uvRect = new Rect((Margin + subPixel.x) / _rt.width, (Margin + subPixel.y) / _rt.height, w / _rt.width, hh / _rt.height);
            }
        }

        void EnsureDisplay()
        {
            int h = Mathf.Max(64, verticalResolution);
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            int w = Mathf.Max(64, Mathf.RoundToInt(h * aspect));
            int rw = w + 2 * Margin, rh = h + 2 * Margin;

            if (_rt == null || _rt.width != rw || _rt.height != rh)
            {
                if (_rt) { _cam.targetTexture = null; _rt.Release(); DestroySafe(_rt); }
                _rt = new RenderTexture(rw, rh, 24) { filterMode = FilterMode.Point, name = "PixelArtRT", hideFlags = HideFlags.HideAndDontSave };
            }
            if (_cam.targetTexture != _rt) _cam.targetTexture = _rt;
            // the render texture is slightly larger than the visible image, so widen the view to match
            if (_cam.orthographic && orthographicSize > 0f) _cam.orthographicSize = orthographicSize * rh / h;

            if (_canvasGo == null)
            {
                _canvasGo = new GameObject("PixelArtCanvas", typeof(Canvas), typeof(RawImage)) { hideFlags = HideFlags.HideAndDontSave };
                var canvas = _canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = -100;
                _img = _canvasGo.GetComponent<RawImage>();
                var rect = _img.rectTransform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            if (_img && _img.texture != _rt) _img.texture = _rt;
        }

        static void DestroySafe(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
