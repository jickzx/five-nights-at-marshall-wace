using Fnmw.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.Game
{
    /// <summary>Full-screen colour overlay for the endings. Drawn above the world, below nothing else we own.</summary>
    public class ScreenFade : MonoBehaviour
    {
        Image _img;

        public static ScreenFade Create(Transform parent)
        {
            var go = new GameObject("ScreenFade");
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            if (XrBridge.Active && Camera.main)
            {
                // VR: the ending's camera is drawn on the theater screen (a render texture), which an overlay
                // canvas never reaches; hang the fade just in front of that camera instead.
                var cam = Camera.main;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = Mathf.Max(cam.nearClipPlane * 2f, 0.1f);
            }
            var img = new GameObject("Overlay").AddComponent<Image>();
            img.transform.SetParent(go.transform, false);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            img.color = new Color(0, 0, 0, 0);
            var f = go.AddComponent<ScreenFade>();
            f._img = img;
            return f;
        }

        public void Set(Color c, float alpha) { c.a = Mathf.Clamp01(alpha); _img.color = c; }
        public float Alpha => _img.color.a;
    }

    /// <summary>Small easing helpers for the cutscenes.</summary>
    public static class Ease
    {
        public static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        public static float InOut(float t) { t = Mathf.Clamp01(t); return 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI); }
        public static float Out(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }
        public static float In(float t) { t = Mathf.Clamp01(t); return t * t; }
        /// <summary>0..1 progress of now through [a, b].</summary>
        public static float Span(float now, float a, float b) => Mathf.Clamp01((now - a) / Mathf.Max(0.0001f, b - a));
    }
}
