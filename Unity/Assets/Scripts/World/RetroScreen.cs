using Fnmw.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.World
{
    /// <summary>
    /// The PS1 pass. The main camera renders into a ~240-line render texture with point filtering; a
    /// full-screen RawImage presents it through Fnmw/RetroScreen (Bayer dither into a limited colour
    /// depth, a faint scanline and vignette). The same resolution drives vertex snapping in Fnmw/PS1Lit.
    /// Canvases from other areas draw above it at full resolution, so UI text stays crisp.
    /// </summary>
    public class RetroScreen : MonoBehaviour
    {
        public static RetroScreen Instance { get; private set; }
        public int Lines = 240;
        public bool On = true;

        RenderTexture _rt;
        Camera _target;
        RawImage _image;
        Material _mat;
        Canvas _canvas;
        Camera _presenter;

        public static RetroScreen Create(Transform parent)
        {
            var go = new GameObject("RetroScreen");
            go.transform.SetParent(parent, false);
            var rs = go.AddComponent<RetroScreen>();
            Instance = rs;

            var presenter = new GameObject("RetroPresenterCamera").AddComponent<Camera>();
            presenter.transform.SetParent(go.transform, false);
            presenter.cullingMask = 0;
            presenter.clearFlags = CameraClearFlags.SolidColor;
            presenter.backgroundColor = Color.black;
            presenter.depth = -100;
            rs._presenter = presenter;

            var canvasGo = new GameObject("RetroCanvas");
            canvasGo.transform.SetParent(go.transform, false);
            rs._canvas = canvasGo.AddComponent<Canvas>();
            rs._canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rs._canvas.sortingOrder = -1000;
            var imgGo = new GameObject("Screen");
            imgGo.transform.SetParent(canvasGo.transform, false);
            rs._image = imgGo.AddComponent<RawImage>();
            rs._image.raycastTarget = false;
            var rt = rs._image.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            rs._mat = new Material(Ps1Look.Load("RetroScreen")) { name = "retro_screen" };
            rs._image.material = rs._mat;
            return rs;
        }

        /// <summary>0..1 fade of the whole picture toward a colour, for cutscenes that want it under the UI.</summary>
        public void SetFade(float amount, Color color)
        {
            _mat.SetFloat("_Fade", amount);
            _mat.SetColor("_FadeColor", color);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != _target)
            {
                if (_target) _target.targetTexture = null;
                _target = cam;
            }
            if (!cam) return;
            if (!On)
            {
                cam.targetTexture = null;
                _canvas.enabled = false;
                Shader.SetGlobalVector("_FnmwSnapRes", Vector4.zero);
                return;
            }
            _canvas.enabled = true;
            int h = Mathf.Max(120, Lines);
            int w = Mathf.Max(160, Mathf.RoundToInt(h * Screen.width / (float)Mathf.Max(1, Screen.height)));
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                if (_rt) { cam.targetTexture = null; _rt.Release(); Destroy(_rt); }
                _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, name = "fnmw_lowres", antiAliasing = 1 };
                _rt.Create();
                _image.texture = _rt;
            }
            if (cam.targetTexture != _rt) cam.targetTexture = _rt;
            Shader.SetGlobalVector("_FnmwSnapRes", new Vector4(w, h, 0, 0));
        }

        void OnDestroy()
        {
            if (_target) _target.targetTexture = null;
            if (_rt) { _rt.Release(); Destroy(_rt); }
            if (Instance == this) Instance = null;
        }
    }
}
