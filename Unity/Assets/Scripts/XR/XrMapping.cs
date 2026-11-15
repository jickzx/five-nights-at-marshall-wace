using UnityEngine;

namespace Fnmw.XR
{
    /// <summary>
    /// Turns a thumbstick into arrow-key presses, the way a held key repeats: one press when the stick
    /// crosses the threshold, then repeats while it is held. Pure, so tests drive it by hand.
    /// </summary>
    public sealed class StickRepeater
    {
        public const float PressAt = 0.6f, ReleaseAt = 0.35f, RepeatDelay = 0.45f, RepeatEvery = 0.18f;

        int _dir = -1;      // 0 left, 1 right, 2 up, 3 down, -1 none
        float _held;

        public bool Left, Right, Up, Down;

        public void Tick(Vector2 stick, float dt)
        {
            Left = Right = Up = Down = false;
            int dir = Classify(stick, _dir);
            if (dir != _dir) { _dir = dir; _held = 0f; if (dir >= 0) Fire(dir); return; }
            if (dir < 0) return;
            float before = _held;
            _held += dt;
            if (_held < RepeatDelay) return;
            // fire once for every repeat boundary crossed this frame (at most one: frames are short)
            int a = before < RepeatDelay ? -1 : Mathf.FloorToInt((before - RepeatDelay) / RepeatEvery);
            int b = Mathf.FloorToInt((_held - RepeatDelay) / RepeatEvery);
            if (b > a) Fire(dir);
        }

        void Fire(int dir)
        {
            if (dir == 0) Left = true; else if (dir == 1) Right = true; else if (dir == 2) Up = true; else Down = true;
        }

        /// <summary>Which way the stick points, with hysteresis around the current direction.</summary>
        public static int Classify(Vector2 v, int current)
        {
            float m = v.magnitude;
            if (current >= 0 && m >= ReleaseAt && Dominant(v) == current) return current;
            if (m < PressAt) return -1;
            return Dominant(v);
        }

        static int Dominant(Vector2 v) => Mathf.Abs(v.x) >= Mathf.Abs(v.y) ? (v.x < 0 ? 0 : 1) : (v.y > 0 ? 2 : 3);
    }

    /// <summary>
    /// Snap or smooth turning from the right stick's x axis. Snap turns once per flick and needs the stick
    /// back near the centre before it turns again.
    /// </summary>
    public sealed class Turner
    {
        public bool Smooth;
        public float SnapDegrees = 45f, SmoothDegreesPerSecond = 120f;
        const float SnapAt = 0.7f, RearmAt = 0.3f, DeadZone = 0.15f;
        bool _armed = true;

        /// <summary>Degrees to yaw this frame (positive = turn right).</summary>
        public float Tick(float x, float dt)
        {
            if (Smooth)
            {
                _armed = true;
                float a = Mathf.Abs(x);
                if (a < DeadZone) return 0f;
                return Mathf.Sign(x) * Mathf.InverseLerp(DeadZone, 1f, a) * SmoothDegreesPerSecond * dt;
            }
            if (!_armed) { if (Mathf.Abs(x) < RearmAt) _armed = true; return 0f; }
            if (Mathf.Abs(x) < SnapAt) return 0f;
            _armed = false;
            return Mathf.Sign(x) * SnapDegrees;
        }
    }

    /// <summary>Where a ray meets the UI glass, as 0..1 on the glass (x right, y up).</summary>
    public static class GlassHit
    {
        /// <summary>
        /// The glass is a RectTransform whose readable face looks back along -forward. Returns false when the
        /// ray points away from it, is parallel to it, or misses its rect.
        /// </summary>
        public static bool Raycast(RectTransform glass, Vector3 origin, Vector3 dir, out Vector2 uv, out Vector3 point)
        {
            uv = default; point = default;
            if (!glass) return false;
            var plane = new Plane(-glass.forward, glass.position);
            var ray = new Ray(origin, dir);
            if (!plane.Raycast(ray, out float t) || t <= 0f) return false;
            point = ray.GetPoint(t);
            var local = glass.InverseTransformPoint(point);
            var r = glass.rect;
            if (r.width <= 0f || r.height <= 0f) return false;
            uv = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }
    }
}
