using System.Collections;
using System.IO;
using UnityEngine;

namespace Fnmw.Game
{
    /// <summary>
    /// Dev tool for making the demo recordings: grabs the finished game view (UI included) every few frames
    /// and writes numbered JPEGs. Start and stop it from the Unity CLI:
    ///   Fnmw.Game.FrameRecorder.Begin("/tmp/frames", 12);  ...  Fnmw.Game.FrameRecorder.End();
    /// </summary>
    public class FrameRecorder : MonoBehaviour
    {
        static FrameRecorder _active;
        string _dir;
        float _interval, _next;
        int _n, _width;

        public static int Frames => _active ? _active._n : 0;

        public static void Begin(string dir, float fps = 12f, int width = 640)
        {
            End();
            Directory.CreateDirectory(dir);
            var go = new GameObject("FrameRecorder");
            DontDestroyOnLoad(go);
            _active = go.AddComponent<FrameRecorder>();
            _active._dir = dir; _active._interval = 1f / Mathf.Max(1f, fps); _active._width = width;
        }

        public static int End()
        {
            int n = Frames;
            if (_active) Destroy(_active.gameObject);
            _active = null;
            return n;
        }

        IEnumerator Start()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                if (Time.unscaledTime < _next) continue;
                _next = Time.unscaledTime + _interval;
                var full = ScreenCapture.CaptureScreenshotAsTexture();
                int h = Mathf.RoundToInt(_width * full.height / (float)full.width);
                var rt = RenderTexture.GetTemporary(_width, h);
                Graphics.Blit(full, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var small = new Texture2D(_width, h, TextureFormat.RGB24, false);
                small.ReadPixels(new Rect(0, 0, _width, h), 0, 0); small.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(Path.Combine(_dir, $"f_{_n++:00000}.jpg"), small.EncodeToJPG(88));
                Destroy(full); Destroy(small);
            }
        }
    }
}
