using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Audio
{
    /// <summary>
    /// Everything the player should hear *somewhere*: the beast's voice and steps (3D, attached to the beast),
    /// the player's wet footsteps, and hum + static at each console. Uses Unity's spatialiser on AudioClips
    /// rendered by SfxBank; nothing needs to be imported or wired by other threads.
    /// </summary>
    public sealed class SpatialAudio : MonoBehaviour
    {
        public AudioDirector Director;

        Transform _beast;
        AudioSource _beastVoice, _beastFeet, _playerFeet;
        Vector3 _beastLast, _playerLast;
        float _beastStride, _playerStride, _vocalTimer = 5f, _scanTimer;
        bool _silenced;
        readonly Dictionary<Interactable, ConsoleSound> _consoles = new Dictionary<Interactable, ConsoleSound>();
        Transform _orbit; // audition-mode stand-in for the beast

        sealed class ConsoleSound { public AudioSource Hum, Static; public float Timer; }

        static AudioSource MakeSource(Transform parent, string name, bool spatial, float minDist, float maxDist)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = spatial ? 1f : 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = minDist; s.maxDistance = maxDist;
            s.dopplerLevel = 0f; // pitch-shifting growls sounds cheap
            s.spread = 40f;
            return s;
        }

        Transform BeastTransform
        {
            get
            {
                if (_orbit != null) return _orbit;
                if (_beast == null)
                {
                    // The Monster thread spawns a GameObject named "Beast" (MonsterModule).
                    var go = GameObject.Find("Beast");
                    if (go != null) _beast = go.transform;
                }
                return _beast;
            }
        }

        void EnsureBeastSources()
        {
            var b = BeastTransform;
            if (b == null) return;
            if (_beastVoice == null || _beastVoice.transform.parent != b)
            {
                if (_beastVoice) Destroy(_beastVoice.gameObject);
                if (_beastFeet) Destroy(_beastFeet.gameObject);
                _beastVoice = MakeSource(b, "BeastVoice", true, 3f, 70f);
                _beastVoice.transform.localPosition = new Vector3(0, 1.6f, 0);
                _beastFeet = MakeSource(b, "BeastFeet", true, 2f, 35f);
                _beastLast = b.position;
            }
        }

        void Update()
        {
            if (_silenced) return;
            var state = Services.State;
            bool roaming = state.Phase != GamePhase.Title && !state.IsOver;

            EnsureBeastSources();
            var b = BeastTransform;
            if (b != null && _beastVoice != null && (roaming || _orbit != null))
            {
                // Vocalisations: more often as the beast evolves and closes in.
                int stage = _orbit != null ? Director.OverrideStage : state.BeastStage;
                float near = float.IsInfinity(state.BeastDistance) ? 0f : Mathf.Clamp01(1f - state.BeastDistance / 40f);
                _vocalTimer -= Time.deltaTime * (1f + near * 1.5f);
                if (_vocalTimer <= 0f)
                {
                    BeastVocal();
                    _vocalTimer = stage >= 3 ? Random.Range(2.5f, 5f) : stage == 2 ? Random.Range(5f, 9f) : Random.Range(7f, 13f);
                }
                // Steps: heavier as it grows; only audible when it actually moves.
                Vector3 d = b.position - _beastLast; d.y = 0;
                _beastLast = b.position;
                _beastStride += d.magnitude;
                float strideLen = stage >= 3 ? 2.2f : 1.6f;
                if (_beastStride > strideLen && stage >= 2)
                {
                    _beastStride = 0f;
                    var bank = Director.Sfx;
                    _beastFeet.pitch = Random.Range(0.9f, 1.05f) * (stage >= 3 ? 0.85f : 1f);
                    _beastFeet.PlayOneShot(bank.Clip("beast_step", bank.NextVariant("beast_step")), stage >= 3 ? 0.85f : 0.65f);
                }
            }

            // Player footsteps, from movement.
            var p = Services.Player;
            if (p != null)
            {
                if (_playerFeet == null || _playerFeet.transform.parent != p)
                {
                    _playerFeet = MakeSource(p, "Footsteps", false, 1f, 10f);
                    _playerLast = p.position;
                }
                Vector3 d = p.position - _playerLast; d.y = 0;
                _playerLast = p.position;
                float step = d.magnitude;
                if (step < 1f) _playerStride += step; // ignore teleports
                if (_playerStride > 1.5f && roaming) { _playerStride = 0f; PlayerStep(); }
            }

            // Console hum and static.
            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f) { _scanTimer = 1f; ScanConsoles(); }
            foreach (var kv in _consoles)
            {
                if (kv.Key == null) continue;
                var cs = kv.Value;
                cs.Hum.volume = kv.Key.Enabled ? 0.35f : 0.08f;
                cs.Timer -= Time.deltaTime;
                if (cs.Timer <= 0f && kv.Key.Enabled)
                {
                    cs.Timer = Random.Range(4f, 11f);
                    var bank = Director.Sfx;
                    cs.Static.PlayOneShot(bank.Clip("static", bank.NextVariant("static")), 0.35f);
                }
            }
        }

        void ScanConsoles()
        {
            foreach (var it in Interactable.All)
            {
                if (it == null || _consoles.ContainsKey(it)) continue;
                var hum = MakeSource(it.transform, "ConsoleHum", true, 1.5f, 14f);
                hum.transform.localPosition = new Vector3(0, 1f, 0);
                hum.clip = Director.Sfx.Clip("console_hum");
                hum.loop = true;
                hum.pitch = it.Type == Interactable.Kind.Ship ? 0.5f : 1f + 0.03f * (it.Index % 4);
                hum.volume = 0.35f;
                hum.time = Random.Range(0f, 1.9f);
                hum.Play();
                var st = MakeSource(it.transform, "ConsoleStatic", true, 1.5f, 18f);
                _consoles[it] = new ConsoleSound { Hum = hum, Static = st, Timer = Random.Range(1f, 6f) };
            }
        }

        // ------------------------------------------------------------ API used by the director

        public void BeastVocal()
        {
            int stage = _orbit != null ? Director.OverrideStage : Services.State.BeastStage;
            string name = "beast_vocal_" + Mathf.Clamp(stage, 1, 3);
            BeastSay(name, stage >= 3 ? 0.85f : 0.75f, Director.Sfx.NextVariant(name));
        }

        public void BeastSay(string name, float volume, int variant = 0)
        {
            EnsureBeastSources();
            var clip = Director.Sfx.Clip(name, variant);
            if (clip == null) return;
            if (_beastVoice != null)
            {
                _beastVoice.pitch = Random.Range(0.94f, 1.04f);
                _beastVoice.PlayOneShot(clip, volume);
            }
            else Director.Engine.PlaySample(Director.Sfx.Get(name, variant), volume * 0.6f); // no beast yet: 2D
        }

        public void PlayerStep()
        {
            if (_playerFeet == null) return;
            var bank = Director.Sfx;
            _playerFeet.pitch = Random.Range(0.93f, 1.07f);
            _playerFeet.PlayOneShot(bank.Clip("footstep", bank.NextVariant("footstep")), Random.Range(0.35f, 0.5f));
        }

        /// <summary>Endings: the world goes quiet so the score can speak.</summary>
        public void Silence()
        {
            _silenced = true;
            foreach (var kv in _consoles) if (kv.Value.Hum) kv.Value.Hum.Stop();
        }

        public void Unsilence() => _silenced = false;

        /// <summary>After an audio reset: looping hums were stopped along with everything else.</summary>
        public void RestartLoops()
        {
            if (_silenced) return;
            foreach (var kv in _consoles) if (kv.Value.Hum != null && !kv.Value.Hum.isPlaying) kv.Value.Hum.Play();
        }

        /// <summary>Audition: a stand-in emitter that circles the listener so the 3D placement is audible.</summary>
        public Transform StartOrbit()
        {
            if (_orbit == null) _orbit = new GameObject("AuditionBeast").transform;
            _silenced = false;
            return _orbit;
        }

        public void StopOrbit()
        {
            if (_orbit != null) Destroy(_orbit.gameObject);
            _orbit = null; // the sources die with it; EnsureBeastSources rebuilds them on the real beast
        }
    }
}
