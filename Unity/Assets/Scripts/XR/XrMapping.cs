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
    /// Snap or smooth turning from the right stick. Snap turns once per flick: the stick has to be pushed
    /// mostly sideways (a flick that drifts up or down still counts, a forward or back push does not) and has
    /// to come back near the centre before it turns again.
    /// </summary>
    public sealed class Turner
    {
        public bool Smooth;
        public float SnapDegrees = 45f, SmoothDegreesPerSecond = 120f;
        const float SnapAt = 0.7f, RearmAt = 0.3f, DeadZone = 0.15f;
        bool _armed = true;

        /// <summary>Degrees to yaw this frame (positive = turn right), from the stick's x axis alone.</summary>
        public float Tick(float x, float dt) => Tick(new Vector2(x, 0f), dt);

        /// <summary>Degrees to yaw this frame (positive = turn right).</summary>
        public float Tick(Vector2 stick, float dt)
        {
            float x = stick.x;
            if (Smooth)
            {
                _armed = true;
                float a = Mathf.Abs(x);
                if (a < DeadZone) return 0f;
                return Mathf.Sign(x) * Mathf.InverseLerp(DeadZone, 1f, a) * SmoothDegreesPerSecond * dt;
            }
            // Snap reads how far the stick is pushed, not x alone: a thumb rarely flicks exactly sideways, and a
            // full push 40 degrees off the axis only reaches x = 0.69, which never turned.
            float push = stick.magnitude;
            if (!_armed) { if (push < RearmAt) _armed = true; return 0f; }
            if (push < SnapAt || Mathf.Abs(x) < Mathf.Abs(stick.y)) return 0f;
            _armed = false;
            return Mathf.Sign(x) * SnapDegrees;
        }

        /// <summary>
        /// Call instead of Tick while the stick is not turning the player (it steps the cursor on a screen, or
        /// a cutscene is playing). A stick still held when turning comes back must be let go first, so the view
        /// does not jump the moment the player gets control.
        /// </summary>
        public void Rest(Vector2 stick)
        {
            if (stick.magnitude >= RearmAt) _armed = false;
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

namespace Fnmw.XR
{
    /// <summary>
    /// Where the theater screen goes when a cutscene starts. Everything is in tracking space (the space the
    /// headset reports its pose in, under the theater's CameraOffset): the screen is put straight ahead of the
    /// way the face points at that moment, at eye level, wherever the player stands or faces in their room.
    /// Pure, so tests drive it with any room pose.
    /// </summary>
    public static class TheaterLayout
    {
        /// <summary>Metres from the eyes to the screen.</summary>
        public const float Distance = 3.4f;
        /// <summary>Screen size, metres (16:9, about 70 degrees wide at Distance).</summary>
        public static readonly Vector2 Size = new Vector2(4.8f, 2.7f);

        /// <summary>The way the face points, flattened onto the floor. Looking straight down or up falls back to
        /// the top (or bottom) of the head, which still points where the face was turned.</summary>
        public static Vector3 Facing(Quaternion headRot)
        {
            Vector3 f = headRot * Vector3.forward; f.y = 0;
            if (f.sqrMagnitude < 1e-4f) { f = headRot * (Vector3.up * -Mathf.Sign((headRot * Vector3.forward).y)); f.y = 0; }
            if (f.sqrMagnitude < 1e-6f) return Vector3.forward;
            return f.normalized;
        }

        /// <summary>
        /// Screen pose for a head at <paramref name="headPos"/> / <paramref name="headRot"/> (tracking space).
        /// The quad faces the viewer (its +Z points away from them, the way a Unity quad shows its face).
        /// Height is the head's own (the offset already puts the eyes at the game's eye height); with no
        /// tracked height yet it uses <paramref name="eyeHeight"/>.
        /// </summary>
        public static void Screen(Vector3 headPos, Quaternion headRot, float eyeHeight, out Vector3 pos, out Quaternion rot)
        {
            Vector3 f = Facing(headRot);
            float y = headPos.y > 0.2f ? headPos.y : eyeHeight;
            pos = new Vector3(headPos.x, y, headPos.z) + f * Distance;
            rot = Quaternion.LookRotation(f, Vector3.up);
        }
    }

    /// <summary>
    /// One button that means one thing when tapped and another when held: the tap fires on release (only if it
    /// was let go before the threshold), the hold fires once when the threshold is reached. So holding never
    /// sets off the tap action first (holding Y to skip the tutorial must not walk away from the console).
    /// </summary>
    public sealed class HoldTap
    {
        public readonly float Threshold;
        float _held;
        bool _down, _fired;

        public bool Tap { get; private set; }     // this frame: released before the threshold
        public bool Hold { get; private set; }    // this frame: just reached the threshold
        public bool Down => _down;
        public float Held => _down ? _held : 0f;
        /// <summary>0..1 towards the hold, for on-controller feedback.</summary>
        public float Progress => _down && !_fired ? Mathf.Clamp01(_held / Threshold) : 0f;

        public HoldTap(float threshold) { Threshold = threshold; }

        public void Tick(bool down, float dt)
        {
            Tap = Hold = false;
            if (down)
            {
                if (!_down) { _down = true; _held = 0f; _fired = false; return; }
                _held += dt;
                if (!_fired && _held >= Threshold) { _fired = true; Hold = true; }
                return;
            }
            if (_down && !_fired) Tap = true;
            _down = false; _held = 0f; _fired = false;
        }

        public void Reset() { _down = false; _held = 0f; _fired = false; Tap = Hold = false; }
    }

    /// <summary>The raw Touch controller state for one frame.</summary>
    public struct TouchFrame
    {
        public Vector2 LeftStick, RightStick;
        public bool A, B, X, Y;                 // held
        public bool RightTrigger, LeftTrigger;  // held
        public bool LeftGrip, Menu;             // held
        /// <summary>The right controller's laser is on the UI glass this frame.</summary>
        public bool PointerOnGlass;
    }

    /// <summary>What the game should see this frame (copied onto XrBridge by the rig).</summary>
    public struct MappedInput
    {
        public Vector2 Move;
        public bool Run, Interact, Flashlight, Leave, SkipHeld;
        public bool Enter, Space, Back, Clear, Replay, Click;
        public bool Hint, Speak;   // X on a screen: ask the ship (console) / hear it again (quiz payoff)
        public bool Left, Right, Up, Down;
    }

    /// <summary>
    /// The Touch layout, as one pure mapping so tests can press buttons by hand:
    /// <code>
    ///   right trigger  aim / select / place (clicks the glass under the laser; off the glass it places the
    ///                  tile under the stick cursor / continues); uses the console while roaming
    ///   A              send / confirm (Enter); uses the console while roaming
    ///   B              tap: undo / back (Backspace)      hold: clear the tiles / skip the worked example (Delete)
    ///   left stick     walk (and steps the screen cursor, as does the right stick)
    ///   left grip      run
    ///   right stick    turn (snap or smooth; click switches)
    ///   X              lamp (F) while roaming; on a screen: ask the ship for a hint on the console (H),
    ///                  hear the decoded sentence again on the quiz payoff (P). Each reader only looks at its own
    ///   Y              tap: leave the console / skip the intro (Esc), replay tutorial on the title (T)
    ///                  hold: skip the tutorial (hold Q)
    ///   left trigger   nothing (it used to send, and fired by accident while gripping)
    ///   menu           also leaves the console / skips the intro
    /// </code>
    /// </summary>
    public sealed class TouchMapper
    {
        /// <summary>Seconds a hold takes (B clear, Y skip). A tap is a release before this.</summary>
        public const float HoldSeconds = 0.5f;

        public readonly HoldTap B = new HoldTap(HoldSeconds);
        public readonly HoldTap Y = new HoldTap(HoldSeconds);
        readonly StickRepeater _arrows = new StickRepeater();
        bool _a, _x, _trig, _menu;

        public MappedInput Tick(in TouchFrame f, float dt)
        {
            var m = new MappedInput();
            bool a = f.A && !_a, x = f.X && !_x, trig = f.RightTrigger && !_trig, menu = f.Menu && !_menu;
            _a = f.A; _x = f.X; _trig = f.RightTrigger; _menu = f.Menu;
            B.Tick(f.B, dt);
            Y.Tick(f.Y, dt);

            // roaming
            m.Move = f.LeftStick.sqrMagnitude < 0.02f ? Vector2.zero : f.LeftStick;
            m.Run = f.LeftGrip;
            m.Interact = a || trig;
            m.Flashlight = x;
            m.Leave = Y.Tap || menu;
            // Y held: the tutorial's own hold-to-skip timer (0.8 s, longer than a tap) fills while it is down.
            // A tap (released inside HoldSeconds) leaves the console instead; a longer hold never does.
            m.SkipHeld = Y.Down;

            // screens
            m.Enter = a;
            m.Click = trig && f.PointerOnGlass;
            m.Space = trig && !f.PointerOnGlass;   // never both: a click already places the tile under the laser
            m.Back = B.Tap;
            m.Clear = B.Hold;
            m.Replay = Y.Tap;
            // X is the lamp while roaming and the voice button on a screen. The player reads Flashlight only while it
            // can move, the console reads only Hint and the payoff only Speak, so one press never does two things.
            m.Hint = x;
            m.Speak = x;
            var stick = f.RightStick.sqrMagnitude >= f.LeftStick.sqrMagnitude ? f.RightStick : f.LeftStick;
            _arrows.Tick(stick, dt);
            m.Left = _arrows.Left; m.Right = _arrows.Right; m.Up = _arrows.Up; m.Down = _arrows.Down;
            return m;
        }
    }
}
