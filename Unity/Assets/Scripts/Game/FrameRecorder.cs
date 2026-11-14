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
        int _n;

        public static int Frames => _active ? _active._n : 0;

        public static void Begin(string dir, float fps = 12f)
        {
            End();
            Directory.CreateDirectory(dir);
            var go = new GameObject("FrameRecorder");
            DontDestroyOnLoad(go);
            _active = go.AddComponent<FrameRecorder>();
            _active._dir = dir; _active._interval = 1f / Mathf.Max(1f, fps);
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
                // Encode the captured texture as-is: blitting it through a render texture re-applies sRGB and washes it out.
                var full = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(_dir, $"f_{_n++:00000}.jpg"), full.EncodeToJPG(88));
                Destroy(full);
            }
        }
    }
}
