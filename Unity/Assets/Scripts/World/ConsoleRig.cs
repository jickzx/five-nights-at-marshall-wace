using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;

namespace Fnmw.World
{
    /// <summary>
    /// A console's lights. Dark red while locked, a pulsing bone-white beacon (light, screen, a pale shaft
    /// into the fog) when it is the next one to decode, steady amber once solved.
    /// It reads Interactable.Enabled (the director enables the next console) and the answer events.
    /// </summary>
    public class ConsoleRig : MonoBehaviour
    {
        static readonly Color Locked = new Color(0.22f, 0.01f, 0.01f);
        static readonly Color Ready = new Color(0.95f, 0.88f, 0.72f);
        static readonly Color Solved = new Color(1f, 0.5f, 0.12f);

        Interactable _it;
        Light _light;
        readonly List<Material> _screens = new List<Material>();
        GameObject _beam;
        Material _beamMat;
        bool _solved;
        static int _lastActivated = -1;
        public bool IsSolved => _solved;

        void Awake()
        {
            _it = GetComponent<Interactable>();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                string n = r.gameObject.name.ToLowerInvariant();
                bool screenPart = n.Contains("screen") || n.StartsWith("status") || n.StartsWith("tile_") || n.Contains("brow_strip") || n == "mesh";
                if (!screenPart) continue;
                _screens.Add(r.material);
            }
            var lg = new GameObject("ConsoleLight");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = new Vector3(0, 1.9f, -0.9f);
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Point; _light.range = 7.5f; _light.intensity = 0f; _light.shadows = LightShadows.None;

            _beam = new GameObject("Beacon");
            _beam.transform.SetParent(transform, false);
            _beamMat = BloodRain.ParticleMaterial(new Color(1f, 0.92f, 0.8f, 0.22f));
            for (int i = 0; i < 2; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(_beam.transform, false);
                // Flipped so uv.y = 0 sits at the top: the rain shader fades it out upward.
                q.transform.localPosition = new Vector3(0, 9f, 0);
                q.transform.localRotation = Quaternion.Euler(0, i * 90f, 180f);
                q.transform.localScale = new Vector3(0.32f, 18f, 1f);
                q.GetComponent<MeshRenderer>().sharedMaterial = _beamMat;
            }
        }

        void OnEnable()
        {
            GameEvents.ConsoleActivated += OnActivated;
            GameEvents.AnswerSubmitted += OnAnswer;
        }

        void OnDisable()
        {
            GameEvents.ConsoleActivated -= OnActivated;
            GameEvents.AnswerSubmitted -= OnAnswer;
        }

        void OnActivated(int i) => _lastActivated = i;
        void OnAnswer(AnswerEvent e) { if (e.Correct && _lastActivated == _it.Index) _solved = true; }

        void Update()
        {
            bool ready = _it && _it.Enabled && !_solved;
            bool active = ready && Services.State.Phase == GamePhase.Puzzle && _lastActivated == _it.Index;
            float t = Time.time;
            Color c; float li;
            if (_solved) { c = Solved * 0.9f; li = 1.4f; }
            else if (active) { c = Ready * 1.3f; li = 3.2f; }
            else if (ready) { float p = 0.65f + 0.35f * Mathf.Sin(t * 3.1f); c = Ready * p; li = 2.6f * p; }
            else { float f = Mathf.PerlinNoise(t * 2f, _it ? _it.Index : 0) > 0.75f ? 0.4f : 1f; c = Locked * f; li = 0.25f * f; }
            foreach (var m in _screens) m.SetColor("_EmissionColor", c);
            _light.color = _solved ? Solved : ready ? Ready : new Color(0.8f, 0.05f, 0.03f);
            _light.intensity = li;
            _beam.SetActive(ready);
            if (ready)
            {
                // Fade the shaft out as you walk up to it: a beacon from afar, not a pole in your face.
                var cam = Camera.main;
                float near = cam ? Mathf.Clamp01((Vector3.Distance(cam.transform.position, transform.position) - 4f) / 10f) : 1f;
                _beamMat.SetColor("_Tint", new Color(1f, 0.9f, 0.75f, (0.09f + 0.05f * Mathf.Sin(t * 3.1f)) * near));
            }
        }

        /// <summary>For tests and skip keys: mark solved without going through the events.</summary>
        public void ForceSolved() => _solved = true;
    }
}
