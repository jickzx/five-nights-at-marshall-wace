using System;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fnmw.Game.Intro
{
    /// <summary>Beat boundaries of the opening, in seconds from the moment the title hands over.</summary>
    public static class IntroBeats
    {
        public const float FadeIn = 1.5f;        // black lifts on the cockpit
        public const float Cockpit = 7.0f;       // 0 - 7: pilot's view, the wormhole mouth swells ahead, warning
        public const float Entry = 11.0f;        // 7 - 11: outside, the ship is dragged into the mouth; white flash
        public const float Stutter = 14.3f;      // 14.3 - 14.9: the transit freezes in silence, then lurches backward
        public const float StutterEnd = 14.9f;
        public const float Transit = 19.6f;      // 11 - 19.6: inside the tunnel, blue turns to red, it narrows and strobes
        public const float Emerge = 27.0f;       // 19.6 - 27: TrES-2b, the black hole fills the sky; title card
        public const float Burn = 29.6f;         // 27 - 29.6: falling into the planet, plasma on the canopy
        public const float Impact = 32.2f;       // 29.6 - 32.2: the burning ship falls through the storm onto the shore
        public const float Handoff = 33.4f;      // black hold, then Explore begins under the fade
        public const float PostFade = 2.2f;      // fade up on the player's own eyes
        public const float SkipFade = 0.35f;
        public const float SkipGrace = 0.6f;     // ignore skip keys this long, and until they are released once
    }

    /// <summary>
    /// The opening cutscene, played between the title and Explore: aboard a survey ship, into a wormhole,
    /// an unsettling transit, out into the TrES-2b system under a black hole, and a burning fall onto the
    /// shore where the wreck already lies. Then the run begins.
    ///
    /// Ownership while it plays: the game stays in GamePhase.Title (so the run clock, beast and HUD stay idle),
    /// the player's camera renders nothing and a dedicated intro camera draws the runtime-built set on its own
    /// layer. Cursor hidden. Space / Esc / Enter / gamepad Start or South skips. Every object it makes is freed
    /// and the player's camera restored before <c>onArrive</c> runs; a scene reload mid-intro tears it down too.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class IntroCutscene : MonoBehaviour
    {
        /// <summary>Global switch (tests, recording scripts). The -nointro launch flag also turns it off.</summary>
        public static bool Enabled = true;
        static bool? _noIntroFlag;

        public static bool ShouldPlay
        {
            get
            {
                if (_noIntroFlag == null)
                {
                    _noIntroFlag = false;
                    foreach (var a in Environment.GetCommandLineArgs())
                        if (a == "-nointro" || a == "--nointro") _noIntroFlag = true;
                }
                return Enabled && !_noIntroFlag.Value;
            }
        }

        public bool Arrived { get; private set; }
        public float Time01 => Mathf.Clamp01(_t / IntroBeats.Handoff);
        public float T => _t;
        public bool Skipping => _skipping;

        Action _onArrive;
        IntroArt _art;
        IntroSet _set;
        IntroOverlay _overlay;
        GameObject _rig;
        Camera _cam, _playerCam;
        int _playerMask;
        bool _maskSaved;
        AudioSource _drone, _hiss, _alarm, _impact;

        float _t, _post, _skipT, _postLength = IntroBeats.PostFade;
        bool _playing, _skipping, _armed, _impactPlayed, _staticA, _staticB;
        float _tunnelV, _tunnelU, _tunnelRoll, _diskU;
        int _descentFrames;
        Vector3 _shipPosEmerge;
        Quaternion _camRot;

        // Descent, in world space.
        Vector3 _dStart, _dCtrl, _dImpact, _dCam;

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Start the opening. <paramref name="onArrive"/> is called exactly once, when the player should take over.</summary>
        public void Play(Action onArrive)
        {
            _onArrive = onArrive;
            var player = Services.Player ? Services.Player.GetComponent<PlayerController>() : null;
            _playerCam = player ? player.Camera : null;
            try
            {
                _art = new IntroArt();
                if (!_art.Valid || !_playerCam) throw new InvalidOperationException(_playerCam ? "intro shader missing" : "no player camera");
                Build();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Intro] could not start the opening, going straight to the shore: " + ex.Message);
                Cleanup();
                Arrive();
                Destroy(this);
                return;
            }
            _playing = true;
            Guard(() => Services.Audio.SetIntensity(0.05f));
        }

        void Build()
        {
            _rig = new GameObject("Intro");
            _set = new IntroSet(_art);
            _overlay = new IntroOverlay(_rig.transform);
            _overlay.SetFade(1f);

            var camGo = new GameObject("IntroCamera");
            camGo.transform.SetParent(_rig.transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.depth = _playerCam.depth + 5f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.cullingMask = 1 << IntroSet.Layer;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 8000f;
            _cam.fieldOfView = 60f;

            _playerMask = _playerCam.cullingMask;
            _maskSaved = true;
            _playerCam.cullingMask = 0;

            _drone = Source("Drone", _art.Drone, true);
            _hiss = Source("Hiss", _art.Hiss, true);
            _alarm = Source("Alarm", _art.Alarm, true);
            _impact = Source("Impact", _art.Impact, false);

            _set.Dust.Play();
            _camRot = Quaternion.identity;
            PrepareDescent();
        }

        AudioSource Source(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_rig.transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = loop; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f;
            if (loop) s.Play();
            return s;
        }

        void PrepareDescent()
        {
            var spawn = GameObject.FindWithTag("Respawn");
            Transform anchor = spawn ? spawn.transform : Services.Player;
            Vector3 p = anchor.position;
            Vector3 fwd = anchor.forward; fwd.y = 0; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize();
            Vector3 sea = Vector3.Cross(Vector3.up, fwd); // the spawn faces down the shore; its right is the sea side
            _dImpact = p - fwd * 6.5f + Vector3.up * 0.6f;
            _dStart = _dImpact - fwd * 70f + Vector3.up * 62f + sea * 30f;
            _dCtrl = _dImpact - fwd * 28f + Vector3.up * 22f + sea * 9f;
            _dCam = p + fwd * 7f - sea * 3.5f + Vector3.up * 1.7f;
        }

        /// <summary>End now (dev skip keys, SkipTo). Hands over immediately under a short fade.</summary>
        public void Finish()
        {
            if (Arrived) return;
            _postLength = 0.8f;
            _overlay?.SetFade(1f);
            Handoff();
        }

        void Handoff()
        {
            if (Arrived) return;
            _playing = false;
            RestorePlayerView();
            if (_cam) { _cam.targetTexture = null; Destroy(_cam.gameObject); }
            _cam = null;
            _set?.Destroy();
            _set = null;
            if (_drone) _drone.Stop();
            if (_hiss) _hiss.Stop();
            if (_alarm) _alarm.Stop();
            _post = 0f;
            Arrive();
        }

        void Arrive()
        {
            if (Arrived) return;
            Arrived = true;
            var cb = _onArrive; _onArrive = null;
            try { cb?.Invoke(); } catch (Exception ex) { Debug.LogException(ex); }
        }

        void RestorePlayerView()
        {
            if (_maskSaved && _playerCam) _playerCam.cullingMask = _playerMask;
            _maskSaved = false;
        }

        void Cleanup()
        {
            RestorePlayerView();
            _set?.Destroy(); _set = null;
            _overlay?.Destroy(); _overlay = null;
            if (_rig) Destroy(_rig);
            _rig = null;
            _art?.Dispose(); _art = null;
        }

        void OnDestroy()
        {
            Cleanup();
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            if (!_playing || _skipping) return;
            bool held = SkipHeld(out bool pressed);
            if (!_armed) { if (_t > IntroBeats.SkipGrace && !held) _armed = true; return; }
            if (pressed && _t < IntroBeats.Handoff) { _skipping = true; _skipT = 0f; }
        }

        static bool SkipHeld(out bool pressed)
        {
            pressed = false; bool held = false;
            var kb = Keyboard.current;
            if (kb != null)
            {
                held |= kb.spaceKey.isPressed || kb.escapeKey.isPressed || kb.enterKey.isPressed || kb.numpadEnterKey.isPressed;
                pressed |= kb.spaceKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                held |= pad.startButton.isPressed || pad.buttonSouth.isPressed;
                pressed |= pad.startButton.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame;
            }
            return held;
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 20f); // a hitch should not swallow a beat

            if (Arrived)
            {
                // Fade up on the player's own view while Explore runs underneath.
                _post += Time.deltaTime;
                float k = Mathf.Clamp01(_post / _postLength);
                if (_overlay != null)
                {
                    _overlay.SetFade(1f - Ease.InOut(k));
                    _overlay.SetBars(1f - Ease.InOut(Mathf.Clamp01(k * 1.4f)));
                    _overlay.SetTint(Color.clear, 0f);
                    _overlay.SetCaption(null, 0f);
                    _overlay.SetSkipHint(0f);
                }
                if (_impact) _impact.volume = 0.9f * (1f - k);
                if (k >= 1f) Destroy(this);
                return;
            }
            if (!_playing) return;

            Cursor.visible = false; // the title left it visible; Explore will lock it
            // World's PS1 pass renders the main camera into a low-res texture shown on an overlay canvas, which
            // would cover a camera drawing to the screen. Draw into the same target (after the player camera).
            if (_cam && _playerCam && _cam.targetTexture != _playerCam.targetTexture) _cam.targetTexture = _playerCam.targetTexture;

            if (_skipping)
            {
                _skipT += Time.deltaTime;
                float k = Mathf.Clamp01(_skipT / IntroBeats.SkipFade);
                _overlay.SetFade(Mathf.Max(_overlay.Fade, k));
                float mute = 1f - k;
                _drone.volume *= mute; _hiss.volume *= mute; _alarm.volume *= mute;
                if (k >= 1f) { _postLength = 1.2f; Handoff(); }
                return;
            }

            _t += dt;
            try { Step(_t, dt); }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Finish();
                return;
            }
            if (_t >= IntroBeats.Handoff) Handoff();
        }

        // ------------------------------------------------------------------ the timeline

        void Step(float t, float dt)
        {
            _overlay.SetSkipHint(Ease.Span(t, 1f, 2f) * (1f - Ease.Span(t, IntroBeats.Impact, IntroBeats.Impact + 0.3f)));
            _overlay.SetBars(Ease.InOut(Ease.Span(t, 0f, 1.2f)));
            _overlay.SetTitle(null, null, 0f);

            if (t < IntroBeats.Cockpit) ShotCockpit(t);
            else if (t < IntroBeats.Entry) ShotEntry(t);
            else if (t < IntroBeats.Transit) ShotTransit(t, dt);
            else if (t < IntroBeats.Emerge) ShotEmerge(t, dt);
            else if (t < IntroBeats.Burn) ShotBurn(t, dt);
            else if (t < IntroBeats.Impact + 0.2f) ShotDescent(t);
            else ShotBlack(t);
        }

        void Mode(bool cockpit, bool stars, bool wormhole, bool tunnel, bool system)
        {
            Active(_set.Cockpit, cockpit);
            Active(_set.Exterior, !cockpit);
            Active(_set.Stars, stars);
            Active(_set.Wormhole, wormhole);
            Active(_set.Tunnel, tunnel);
            if (_set.Planet.gameObject.activeSelf != system) _set.SetSystemVisible(system);
        }

        static void Active(Transform t, bool on) { if (t && t.gameObject.activeSelf != on) t.gameObject.SetActive(on); }

        void Audio(float drone, float dronePitch, float hiss, float hissPitch, float alarm)
        {
            _drone.volume = drone * 0.55f; _drone.pitch = dronePitch;
            _hiss.volume = hiss * 0.5f; _hiss.pitch = hissPitch;
            _alarm.volume = alarm * 0.35f;
        }

        void PlaceCamera(Vector3 eye, Quaternion rot, float shake)
        {
            if (shake > 0f)
            {
                eye += UnityEngine.Random.insideUnitSphere * shake;
                rot *= Quaternion.Euler(UnityEngine.Random.Range(-1f, 1f) * shake * 6f, UnityEngine.Random.Range(-1f, 1f) * shake * 6f, 0f);
            }
            _cam.transform.SetPositionAndRotation(eye, rot);
        }

        void CockpitLights(float t, float alarm, float flicker)
        {
            for (int i = 0; i < _set.Screens.Length; i++)
            {
                float k = 0.75f + 0.25f * Mathf.Sin(t * (1.3f + i * 0.7f) + i);
                if (flicker > 0f && UnityEngine.Random.value < flicker) k *= UnityEngine.Random.Range(0f, 0.4f);
                var c = i == 1 ? new Color(0.85f, 0.55f, 0.2f) : new Color(0.25f, 0.7f, 0.75f);
                _set.Screens[i].SetColor("_Color", c * k);
            }
            float pulse = alarm > 0f ? (0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f * 1.6f)) * alarm : 0f;
            _set.AlarmMat.SetColor("_Color", new Color(1f, 0.12f, 0.06f) * (0.05f + pulse * 1.6f));
        }

        void EngineGlows(float k, Color c)
        {
            foreach (var g in _set.EngineGlows)
            {
                IntroSet.Billboard(g, _cam.transform.position);
                g.localScale = Vector3.one * (1.2f + 0.6f * k) * (0.9f + 0.1f * Mathf.PerlinNoise(Time.time * 12f, g.position.x));
            }
            _set.EngineGlows[0].GetComponent<MeshRenderer>().sharedMaterial.SetColor("_Color", c * (0.6f + k));
        }

        /// <summary>0 - 7 s. The pilot's seat. Ahead, a pinprick swells into a turning mouth of light.</summary>
        void ShotCockpit(float t)
        {
            Mode(cockpit: true, stars: true, wormhole: true, tunnel: false, system: false);
            _set.Ship.localPosition = Vector3.zero;
            _set.Ship.localRotation = Quaternion.identity;
            float grow = Ease.In(t / IntroBeats.Cockpit);
            _set.Wormhole.localScale = Vector3.one * Mathf.Lerp(6f, 150f, grow);
            _set.WormholeMouth.localRotation = Quaternion.Euler(0, 0, t * 24f);
            _set.MouthMat.SetColor("_Color", Color.white * Mathf.Lerp(0.6f, 1.2f, grow));

            float warn = Ease.Span(t, 4.6f, 5.0f);
            CockpitLights(t, warn, 0f);
            float shake = Ease.Span(t, 5.2f, IntroBeats.Cockpit) * 0.025f;
            var eye = _set.CockpitEye.position + new Vector3(Mathf.Sin(t * 0.7f) * 0.02f, Mathf.Sin(t * 0.9f) * 0.015f, 0);
            var rot = Quaternion.Euler(-3f + Mathf.Sin(t * 0.45f) * 1.2f, Mathf.Sin(t * 0.3f) * 2f, Mathf.Sin(t * 0.5f) * 1f);
            PlaceCamera(eye, rot, shake);
            _cam.fieldOfView = 62f;

            _overlay.SetFade(1f - Ease.InOut(t / IntroBeats.FadeIn));
            _overlay.SetTint(Color.clear, 0f);
            float c1 = Ease.Span(t, 1.4f, 1.8f) * (1f - Ease.Span(t, 4.1f, 4.5f));
            float c2 = Ease.Span(t, 4.8f, 5.1f) * (1f - Ease.Span(t, 6.5f, 6.9f));
            if (c2 > 0f) _overlay.SetCaption("WARNING  ·  GRAVITY GRADIENT RISING", c2, 0f, new Color(1f, 0.55f, 0.45f));
            else _overlay.SetCaption("DEEP TRANSIT  ·  CORRIDOR LOCKED", c1);

            Audio(0.6f * Ease.Span(t, 0f, 1.5f), 1f, Ease.Span(t, 5.5f, 7f) * 0.3f, 1f, warn);
        }

        /// <summary>7 - 11 s. Outside: the small ship is hauled into the mouth, rolling; it fills the frame; white.</summary>
        void ShotEntry(float t)
        {
            Mode(cockpit: false, stars: true, wormhole: true, tunnel: false, system: false);
            float u = Ease.Span(t, IntroBeats.Cockpit, IntroBeats.Entry);
            float z = (IntroSet.WormholeZ - 6f) * Mathf.Pow(u, 2.6f);
            _set.Ship.localPosition = new Vector3(0, 0, z);
            _set.Ship.localRotation = Quaternion.Euler(0, 0, 70f * Ease.In(u) + Mathf.Sin(t * 9f) * 2f * u);
            _set.Wormhole.localScale = Vector3.one * Mathf.Lerp(150f, 300f, Ease.InOut(u));
            _set.WormholeMouth.localRotation = Quaternion.Euler(0, 0, t * (24f + 60f * u));
            _set.MouthMat.SetColor("_Color", Color.white * Mathf.Lerp(1.2f, 2f, u));

            var ship = _set.Ship.position;
            Vector3 off = Vector3.Lerp(new Vector3(-9f, 3.4f, -17f), new Vector3(-3.6f, 1.6f, -10f), Ease.InOut(u));
            Vector3 eye = ship + off;
            Vector3 mouth = _set.Wormhole.position;
            Vector3 look = Vector3.Lerp(ship + Vector3.forward * 8f, mouth, Ease.InOut(Ease.Span(u, 0.3f, 1f)) * 0.6f);
            PlaceCamera(eye, Quaternion.LookRotation(look - eye) * Quaternion.Euler(0, 0, -8f * u), 0.04f + 0.22f * u * u);
            _cam.fieldOfView = Mathf.Lerp(55f, 75f, Ease.In(u));
            EngineGlows(1f, new Color(0.55f, 0.75f, 1f));

            _overlay.SetFade(0f);
            _overlay.SetCaption(null, 0f);
            float flash = Ease.In(Ease.Span(t, 10.3f, IntroBeats.Entry));
            _overlay.SetTint(new Color(0.92f, 0.95f, 1f), flash);
            if (!_staticA && t > 10.6f) { _staticA = true; Stinger("static"); }

            Audio(0.75f, Mathf.Lerp(1f, 0.75f, u), Mathf.Lerp(0.3f, 0.9f, u), Mathf.Lerp(1f, 1.4f, u), 0.6f);
            Guard(() => Services.Audio.SetIntensity(Mathf.Lerp(0.1f, 0.35f, u)));
        }

        /// <summary>11 - 19.6 s. The corridor. It should feel wrong: it freezes, lurches back, turns red and closes in.</summary>
        void ShotTransit(float t, float dt)
        {
            Mode(cockpit: true, stars: false, wormhole: false, tunnel: true, system: false);
            _set.Ship.localPosition = Vector3.zero;
            _set.Ship.localRotation = Quaternion.identity;
            bool frozen = t >= IntroBeats.Stutter && t < IntroBeats.StutterEnd;
            bool lurch = t >= IntroBeats.StutterEnd && t < IntroBeats.StutterEnd + 0.45f;
            float u = Ease.Span(t, IntroBeats.Entry, IntroBeats.Transit);

            float speed = frozen ? 0f : lurch ? -2.2f : Mathf.Lerp(1.6f, 4.2f, u);
            float twist = frozen ? 0f : Mathf.Lerp(0.06f, 0.3f, u);
            _tunnelV -= speed * dt;
            _tunnelU += twist * dt;
            _tunnelRoll += (frozen ? 0f : Mathf.Lerp(20f, 70f, u)) * dt;
            _set.TunnelMat.mainTextureOffset = new Vector2(_tunnelU, _tunnelV);
            _set.Tunnel.localRotation = Quaternion.Euler(0, 0, _tunnelRoll);

            float narrow = Ease.InOut(Ease.Span(t, 16.4f, 18.6f));
            float burst = Ease.In(Ease.Span(t, 18.6f, 19.2f));
            float s = (1f + 0.08f * Mathf.Sin(t * 3.1f)) * Mathf.Lerp(1f, 0.5f, narrow) * Mathf.Lerp(1f, 2.6f, burst);
            _set.Tunnel.localScale = new Vector3(s, s, 1f);

            Color blue = new Color(0.35f, 0.5f, 1f), violet = new Color(0.6f, 0.28f, 0.9f), red = new Color(0.95f, 0.14f, 0.07f);
            Color c = t < 15f ? Color.Lerp(blue, violet, Ease.Span(t, 12f, 15f)) : Color.Lerp(violet, red, Ease.Span(t, 15f, 18f));
            float strobe = t > 16.4f && t < 18.6f ? (Mathf.Sin(t * 22f) > 0f ? 1.2f : 0.35f) : 1f;
            float bright = Mathf.Lerp(0.5f, 1.1f, u) * strobe * (frozen ? 0.7f : 1f);
            _set.TunnelMat.SetColor("_Color", c * bright);
            _set.TunnelGlowMat.SetColor("_Color", Color.Lerp(new Color(0.8f, 0.85f, 1f), new Color(1f, 0.3f, 0.2f), Ease.Span(t, 15f, 18f)) * (0.8f + burst * 2f));
            _set.TunnelGlow.localScale = Vector3.one * Mathf.Lerp(60f, 260f, burst);

            CockpitLights(t, 1f, frozen ? 0f : Mathf.Lerp(0.05f, 0.35f, u));
            float shake = frozen ? 0f : Mathf.Lerp(0.02f, 0.06f, u) + (lurch ? 0.12f : 0f) + narrow * 0.04f;
            if (!frozen)
                _camRot = Quaternion.Euler(-2f + Mathf.Sin(t * 1.7f) * 2f, Mathf.Sin(t * 1.1f) * 3f, Mathf.Sin(t * 0.8f) * 6f + (lurch ? 8f : 0f));
            PlaceCamera(_set.CockpitEye.position, _camRot, shake);
            _cam.fieldOfView = Mathf.Lerp(78f, 64f, Ease.Span(t, IntroBeats.Entry, 12.5f)) + narrow * 10f - (lurch ? 6f : 0f);

            // Out of the white of the entry, and into white, then black, at the exit.
            float flashIn = 1f - Ease.Out(Ease.Span(t, IntroBeats.Entry, 11.7f));
            float flashOut = Ease.In(Ease.Span(t, 18.9f, 19.2f));
            _overlay.SetTint(new Color(0.92f, 0.95f, 1f), Mathf.Max(flashIn, flashOut));
            _overlay.SetFade(Ease.Span(t, 19.2f, IntroBeats.Transit));

            float c1 = Ease.Span(t, 12.4f, 12.6f) * (1f - Ease.Span(t, 15.4f, 15.6f));
            float c2 = Ease.Span(t, 16f, 16.3f) * (1f - Ease.Span(t, 18.3f, 18.6f));
            if (c2 > 0f) _overlay.SetCaption("NAV CANNOT RESOLVE POSITION", c2, 0.25f, new Color(1f, 0.55f, 0.45f));
            else _overlay.SetCaption("SIGNAL LOST", c1 * (UnityEngine.Random.value < 0.12f ? 0.2f : 1f), frozen ? 0f : 0.6f, new Color(1f, 0.4f, 0.32f));
            if (!_staticB && t > IntroBeats.StutterEnd) { _staticB = true; Stinger("static"); }

            if (frozen) Audio(0f, 1f, 0f, 1f, 0f);
            else Audio(0.85f, Mathf.Lerp(0.8f, 1.5f, u) * (lurch ? 0.6f : 1f), 0.85f, Mathf.Lerp(1f, 1.7f, u), 0.7f);
            Guard(() => Services.Audio.SetIntensity(Mathf.Lerp(0.35f, 0.6f, u)));
        }

        /// <summary>19.6 - 27 s. Silence and black, then TrES-2b: a dark world, and a black hole filling its sky.</summary>
        void ShotEmerge(float t, float dt)
        {
            Mode(cockpit: false, stars: true, wormhole: false, tunnel: false, system: true);
            Active(_set.ExitMouth, true);
            var dust = _set.Dust.emission; dust.rateOverTime = 0f;
            float u = Ease.Span(t, IntroBeats.Transit, IntroBeats.Emerge);

            // The ship tumbles out of the closing exit and steadies, nose drifting toward the planet.
            float steady = Ease.Out(Ease.Span(t, IntroBeats.Transit, 25f));
            _shipPosEmerge = new Vector3(0, -6f * u, 70f * Ease.Out(u));
            _set.Ship.localPosition = _shipPosEmerge;
            var toPlanet = (IntroSet.PlanetPos - _shipPosEmerge).normalized;
            var heading = Quaternion.LookRotation(Vector3.Lerp(Vector3.forward, toPlanet, 0.35f * u));
            var tumble = Quaternion.Euler(Mathf.Sin(t * 1.3f) * 25f * (1f - steady), Mathf.Sin(t * 0.9f) * 15f * (1f - steady), (1f - steady) * 160f + Mathf.Sin(t * 0.6f) * 4f);
            _set.Ship.localRotation = heading * tumble;

            _set.ExitMouth.localPosition = new Vector3(0, 0, -20f);
            _set.ExitMouth.localScale = Vector3.one * 45f * (1f - Ease.InOut(Ease.Span(t, 20.2f, 23f)));
            _set.ExitMat.SetColor("_Color", new Color(1f, 0.55f, 0.5f) * (1f - Ease.Span(t, 21f, 23f)));

            _diskU += dt * 0.025f;
            _set.DiskMat.mainTextureOffset = new Vector2(_diskU, 0f);

            // Camera: low behind the ship, looking past it into the black hole; then down at the planet.
            var ship = _set.Ship.position;
            Vector3 eye = _set.Root.TransformPoint(_shipPosEmerge + new Vector3(-9f + 2f * u, -3.4f, -22f + 6f * u));
            Vector3 hole = _set.Hole.position;
            Vector3 planetTop = _set.Planet.position + Vector3.up * IntroSet.PlanetRadius * 0.55f;
            Vector3 lookHole = Vector3.Lerp(ship, hole, 0.85f);
            Vector3 look = Vector3.Lerp(lookHole, Vector3.Lerp(hole, planetTop, 0.75f), Ease.InOut(Ease.Span(t, 24.2f, IntroBeats.Emerge)));
            PlaceCamera(eye, Quaternion.LookRotation(look - eye) * Quaternion.Euler(0, 0, 4f), 0.004f);
            _cam.fieldOfView = Mathf.Lerp(66f, 58f, u);
            IntroSet.Billboard(_set.Lens, _cam.transform.position);
            IntroSet.Billboard(_set.ExitMouth, _cam.transform.position);
            EngineGlows(0.4f, new Color(0.55f, 0.75f, 1f));

            _overlay.SetTint(Color.clear, 0f);
            _overlay.SetFade(1f - Ease.InOut(Ease.Span(t, 20.3f, 21.8f)));
            _overlay.SetCaption(null, 0f);
            float title = Ease.Span(t, 21.6f, 22.4f) * (1f - Ease.Span(t, 25f, 25.8f));
            _overlay.SetTitle("TrES-2b", "UNCHARTED  ·  ORBIT DECAYING", title);

            float wake = Ease.Span(t, 20.3f, 22f);
            Audio(0.5f * wake, 0.6f, 0.08f * wake, 0.7f, Ease.Span(t, 25.6f, 26.2f) * 0.5f);
            Guard(() => Services.Audio.SetIntensity(0.2f));
        }

        /// <summary>27 - 29.6 s. The pilot's view again, falling into the planet: plasma streams over the canopy.</summary>
        void ShotBurn(float t, float dt)
        {
            Mode(cockpit: true, stars: true, wormhole: false, tunnel: false, system: true);
            Active(_set.ExitMouth, false);
            float u = Ease.Span(t, IntroBeats.Emerge, IntroBeats.Burn);
            var target = IntroSet.PlanetPos + Vector3.up * IntroSet.PlanetRadius * 0.2f;
            var dir = (target - _shipPosEmerge).normalized;
            _set.Ship.localPosition = _shipPosEmerge + dir * 700f * Ease.In(u);
            _set.Ship.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0, 0, Mathf.Sin(t * 2.2f) * 6f);
            var em = _set.Plasma.emission;
            em.rateOverTime = Mathf.Lerp(150f, 900f, u);
            if (!_set.Plasma.isPlaying) _set.Plasma.Play();
            em = _set.Dust.emission; em.rateOverTime = 0f;

            _diskU += dt * 0.025f;
            _set.DiskMat.mainTextureOffset = new Vector2(_diskU, 0f);
            IntroSet.Billboard(_set.Lens, _cam.transform.position);
            CockpitLights(t, 1f, 0.3f);
            var rot = _set.Ship.rotation * Quaternion.Euler(4f, 0f, 0f);
            PlaceCamera(_set.CockpitEye.position, rot, Mathf.Lerp(0.04f, 0.14f, u));
            _cam.fieldOfView = Mathf.Lerp(64f, 72f, u);

            float flicker = 0.18f + 0.12f * Mathf.PerlinNoise(t * 14f, 0f) + 0.25f * u;
            float cut = Ease.In(Ease.Span(t, 29.25f, IntroBeats.Burn));
            _overlay.SetTint(new Color(1f, 0.42f, 0.12f), Mathf.Max(flicker, cut));
            _overlay.SetFade(0f);
            float cap = Ease.Span(t, 27.3f, 27.5f) * (1f - Ease.Span(t, 29.1f, 29.3f));
            _overlay.SetCaption("HULL BREACH  ·  BRACE", cap, 0.04f, new Color(1f, 0.55f, 0.45f));

            Audio(0.9f, Mathf.Lerp(0.9f, 1.25f, u), Mathf.Lerp(0.5f, 1f, u), Mathf.Lerp(0.7f, 0.95f, u), 0.8f);
            Guard(() => Services.Audio.SetIntensity(Mathf.Lerp(0.3f, 0.55f, u)));
        }

        /// <summary>29.6 - 32.4 s. The real shore, from the ground: a burning shape falls out of the storm and hits.</summary>
        void ShotDescent(float t)
        {
            Mode(cockpit: false, stars: false, wormhole: false, tunnel: false, system: false);
            var plasma = _set.Plasma.emission; plasma.rateOverTime = 0f;
            _cam.cullingMask = _playerMask | (1 << IntroSet.Layer);
            _cam.farClipPlane = _playerCam.farClipPlane;
            _cam.backgroundColor = RenderSettings.fogColor;

            float u = Mathf.Pow(Ease.Span(t, IntroBeats.Burn, IntroBeats.Impact), 1.35f);
            bool hit = t >= IntroBeats.Impact;
            Vector3 a = Vector3.Lerp(_dStart, _dCtrl, u), b = Vector3.Lerp(_dCtrl, _dImpact, u);
            Vector3 pos = Vector3.Lerp(a, b, u);
            Vector3 vel = b - a;
            Active(_set.Ship, !hit);
            _set.Ship.position = pos;
            if (vel.sqrMagnitude > 1e-4f) _set.Ship.rotation = Quaternion.LookRotation(vel) * Quaternion.Euler(0, 0, t * 140f);
            // The ship just jumped ~6 km from the space set. World-space emission interpolates from the previous
            // position, so emit nothing until the systems have seen two frames down here (no fire column to the sky).
            if (_descentFrames == 0) { _set.Fire.Clear(); _set.Smoke.Clear(); _set.Fire.Play(); _set.Smoke.Play(); }
            bool emit = !hit && _descentFrames >= 2;
            _descentFrames++;
            var fire = _set.Fire.emission; fire.rateOverTime = emit ? 520f : 0f;
            var smoke = _set.Smoke.emission; smoke.rateOverTime = emit ? 140f : 0f;
            EngineGlows(0.2f, new Color(1f, 0.5f, 0.2f));

            float near = Ease.Span(t, IntroBeats.Impact - 0.8f, IntroBeats.Impact);
            var lookAt = Vector3.Lerp(pos, _dImpact, hit ? 1f : 0.15f);
            var want = Quaternion.LookRotation(lookAt - _dCam);
            _camRot = t < IntroBeats.Burn + 0.05f ? want : Quaternion.Slerp(_camRot, want, 0.25f);
            PlaceCamera(_dCam, _camRot, 0.01f + near * 0.06f + (hit ? 0.35f : 0f));
            _cam.fieldOfView = 56f;

            float flashFromBurn = 1f - Ease.Out(Ease.Span(t, IntroBeats.Burn, IntroBeats.Burn + 0.5f));
            float impactFlash = hit ? 1f : 0f;
            _overlay.SetTint(new Color(1f, 0.55f, 0.25f), Mathf.Max(flashFromBurn * 0.9f, impactFlash));
            _overlay.SetFade(hit ? Ease.Span(t, IntroBeats.Impact + 0.05f, IntroBeats.Impact + 0.2f) : 0f);
            _overlay.SetCaption(null, 0f);

            if (hit && !_impactPlayed)
            {
                _impactPlayed = true;
                _impact.volume = 0.9f;
                _impact.Play();
            }
            if (hit) Audio(0f, 1f, 0f, 1f, 0f);
            else Audio(0.7f, 1f, Mathf.Lerp(0.6f, 1f, u), Mathf.Lerp(0.55f, 0.8f, u), 0f);
            Guard(() => Services.Audio.SetIntensity(Mathf.Lerp(0.4f, 0.6f, u)));
        }

        /// <summary>32.4 - 33.4 s. Black. Only the impact rings out. Then the player opens their eyes.</summary>
        void ShotBlack(float t)
        {
            _overlay.SetFade(1f);
            _overlay.SetTint(Color.clear, 0f);
            _overlay.SetCaption(null, 0f);
            Audio(0f, 1f, 0f, 1f, 0f);
        }

        // ------------------------------------------------------------------ helpers

        static void Stinger(string name) => Guard(() => Services.Audio.PlayStinger(name));

        static void Guard(Action a)
        {
            try { a(); } catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}
