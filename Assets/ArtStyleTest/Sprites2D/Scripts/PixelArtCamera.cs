using UnityEngine;
using UnityEngine.UI;

namespace PerfectLoot.ArtStyleTest
{
    /// <summary>
    /// Renders the camera into a low-resolution render texture and shows it full-screen with
    /// point filtering, so the whole 3D scene reads as chunky pixel art. Play mode only.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PixelArtCamera : MonoBehaviour
    {
        public int verticalResolution = 270;

        Camera _cam;
        RenderTexture _rt;
        GameObject _canvasGo;

        void OnEnable()
        {
            _cam = GetComponent<Camera>();
            int h = Mathf.Max(64, verticalResolution);
            int w = Mathf.RoundToInt(h * (float)Screen.width / Mathf.Max(1, Screen.height));
            _rt = new RenderTexture(w, h, 24) { filterMode = FilterMode.Point, name = "PixelArtRT" };
            _cam.targetTexture = _rt;

            _canvasGo = new GameObject("PixelArtCanvas", typeof(Canvas), typeof(RawImage));
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -100;
            var img = _canvasGo.GetComponent<RawImage>();
            img.texture = _rt;
            var rect = img.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        void OnDisable()
        {
            if (_cam) _cam.targetTexture = null;
            if (_canvasGo) Destroy(_canvasGo);
            if (_rt) { _rt.Release(); Destroy(_rt); }
        }
    }
}
