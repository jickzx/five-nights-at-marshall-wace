using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.Game.Intro
{
    /// <summary>
    /// The intro's own screen layer: letterbox bars, a black fade, a colour flash/tint, the caption line,
    /// the planet title card and the skip hint. Its own overlay canvas, below the UI thread's glass (5000)
    /// and above the endings' ScreenFade (900). Destroyed with the intro.
    /// </summary>
    public sealed class IntroOverlay
    {
        public readonly GameObject Root;
        readonly RectTransform _top, _bottom;
        readonly Image _fade, _tint;
        readonly Text _caption, _title, _sub, _skip;
        string _captionText = "";

        public const string SkipText = "SPACE  /  ESC   SKIP";

        public IntroOverlay(Transform parent)
        {
            Root = new GameObject("IntroOverlay", typeof(RectTransform));
            Root.transform.SetParent(parent, false);
            var canvas = Root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 950;
            var scaler = Root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _tint = Img("Tint", Vector2.zero, Vector2.one);
            _top = Img("BarTop", new Vector2(0, 1), Vector2.one).rectTransform;
            _bottom = Img("BarBottom", Vector2.zero, new Vector2(1, 0)).rectTransform;
            _top.pivot = new Vector2(0.5f, 1f); _bottom.pivot = new Vector2(0.5f, 0f);
            _top.GetComponent<Image>().color = Color.black; _bottom.GetComponent<Image>().color = Color.black;
            _caption = Label("Caption", font, 30, TextAnchor.MiddleCenter, new Vector2(0.1f, 0f), new Vector2(0.9f, 0f), new Vector2(0, 58), 60);
            _title = Label("Title", font, 120, TextAnchor.MiddleCenter, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0, 40), 160);
            _sub = Label("Subtitle", font, 28, TextAnchor.MiddleCenter, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0, -60), 50);
            _skip = Label("Skip", font, 22, TextAnchor.MiddleRight, new Vector2(0.6f, 0f), new Vector2(0.98f, 0f), new Vector2(0, 30), 40);
            _skip.text = SkipText;
            _fade = Img("Fade", Vector2.zero, Vector2.one);
            _fade.color = Color.black;
            // Draw order: tint, bars, text, then the fade over everything.
            _fade.transform.SetAsLastSibling();
            SetBars(0); SetCaption(null, 0); SetTitle(null, null, 0); SetSkipHint(0); SetTint(Color.clear, 0);
        }

        Image Img(string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(Root.transform, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
            img.color = new Color(0, 0, 0, 0);
            return img;
        }

        Text Label(string name, Font font, int size, TextAnchor align, Vector2 min, Vector2 max, Vector2 pos, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(Root.transform, false);
            var t = go.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = align; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = new Color(0.86f, 0.8f, 0.74f, 0f);
            var rt = t.rectTransform;
            rt.anchorMin = min; rt.anchorMax = max; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(0, height);
            return t;
        }

        /// <summary>0 = no bars, 1 = full cinematic letterbox (11% of the height each).</summary>
        public void SetBars(float k)
        {
            float h = 1080f * 0.11f * Mathf.Clamp01(k);
            _top.sizeDelta = new Vector2(0, h);
            _bottom.sizeDelta = new Vector2(0, h);
        }

        public void SetFade(float alpha) { var c = Color.black; c.a = Mathf.Clamp01(alpha); _fade.color = c; }
        public float Fade => _fade.color.a;

        public void SetTint(Color c, float alpha) { c.a = Mathf.Clamp01(alpha); _tint.color = c; }

        /// <summary>Caption line in the lower bar. Glitch scrambles some letters (0..1).</summary>
        public void SetCaption(string text, float alpha, float glitch = 0f, Color? color = null)
        {
            _captionText = text ?? "";
            string shown = _captionText;
            if (glitch > 0f && shown.Length > 0)
            {
                var chars = shown.ToCharArray();
                for (int i = 0; i < chars.Length; i++)
                    if (chars[i] != ' ' && Random.value < glitch * 0.35f) chars[i] = "#/\\|_-=<>01"[Random.Range(0, 11)];
                shown = new string(chars);
            }
            _caption.text = shown;
            var c = color ?? new Color(0.86f, 0.8f, 0.74f);
            c.a = Mathf.Clamp01(alpha);
            _caption.color = c;
        }

        public void SetTitle(string title, string sub, float alpha)
        {
            _title.text = title ?? "";
            _sub.text = sub ?? "";
            var c = new Color(0.93f, 0.88f, 0.82f, Mathf.Clamp01(alpha));
            _title.color = c;
            c = new Color(0.8f, 0.45f, 0.38f, Mathf.Clamp01(alpha) * 0.9f);
            _sub.color = c;
        }

        public void SetSkipHint(float alpha) { var c = new Color(0.8f, 0.76f, 0.7f, Mathf.Clamp01(alpha) * 0.55f); _skip.color = c; }

        public void Destroy() { if (Root) Object.Destroy(Root); }
    }
}
