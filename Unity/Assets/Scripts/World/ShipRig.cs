using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;

namespace Fnmw.World
{
    /// <summary>
    /// The escape ship's lights follow GameState.ShipPower: dead and dark at first, running lights after the
    /// first console, cockpit and engines glowing when fully powered, and a tall beacon during the final run.
    /// Exhaust plumes stay hidden until the escape cutscene lights them.
    /// </summary>
    public class ShipRig : MonoBehaviour
    {
        public GameObject Model;
        public bool HasArt;
        readonly List<Material> _lamps = new List<Material>();
        readonly List<Material> _cockpit = new List<Material>();
        Light _cabin, _engine, _navL, _navR;
        GameObject _beam;
        Material _beamMat;
        float _shown;

        void Start()
        {
            foreach (var r in Model.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name.ToLowerInvariant();
                if (n.Contains("exhaust")) { r.enabled = false; continue; }
                if (n.Contains("navigation_lamp") || n.Contains("entry_beacon") || n.Contains("eyebrow_light")) _lamps.Add(r.material);
                else if (n.Contains("cockpit_blank_screen") || n.Contains("cockpit_key") || n.Contains("windscreen")) _cockpit.Add(r.material);
            }
            if (!HasArt) BuildPlaceholderHatch();

            _cabin = MakeLight("CabinLight", new Vector3(0, 2.2f, -0.8f), new Color(1f, 0.85f, 0.65f), 6f);
            _engine = MakeLight("EngineGlow", new Vector3(0, 1.2f, 4.4f), new Color(1f, 0.45f, 0.15f), 9f);
            _navL = MakeLight("NavL", new Vector3(2.1f, 1.3f, 2.2f), new Color(1f, 0.15f, 0.08f), 4f);
            _navR = MakeLight("NavR", new Vector3(-2.1f, 1.3f, 2.2f), new Color(1f, 0.9f, 0.75f), 4f);

            _beam = new GameObject("Beacon");
            _beam.transform.SetParent(transform, false);
            _beamMat = BloodRain.ParticleMaterial(new Color(1f, 0.9f, 0.75f, 0.3f));
            for (int i = 0; i < 3; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(_beam.transform, false);
                q.transform.localPosition = new Vector3(0, 22f, 0);
                q.transform.localRotation = Quaternion.Euler(0, i * 60f, 180f);
                q.transform.localScale = new Vector3(1.4f, 40f, 1f);
                q.GetComponent<MeshRenderer>().sharedMaterial = _beamMat;
            }
            _beam.SetActive(false);
        }

        Light MakeLight(string name, Vector3 local, Color c, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.range = range; l.intensity = 0; l.shadows = LightShadows.None;
            return l;
        }

        void BuildPlaceholderHatch()
        {
            // The placeholder capsule has no door; give it a slab the cutscene can swing shut.
            var hinge = new GameObject("hatch");
            hinge.transform.SetParent(Model.transform, false);
            hinge.transform.localPosition = new Vector3(2.05f, 0.3f, 0f);
            hinge.transform.localRotation = Quaternion.Euler(0, 0, -70f);
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(slab.GetComponent<Collider>());
            slab.transform.SetParent(hinge.transform, false);
            slab.transform.localPosition = new Vector3(0, 1.3f, 0);
            slab.transform.localScale = new Vector3(0.15f, 2.6f, 1.6f);
            slab.GetComponent<MeshRenderer>().sharedMaterial = Ps1Look.Lit(new Color(0.36f, 0.16f, 0.09f), new Color(0.05f, 0.01f, 0f));
        }

        void Update()
        {
            var s = Services.State;
            _shown = Mathf.MoveTowards(_shown, s.ShipPower, Time.deltaTime * 0.5f);
            float t = Time.time;
            bool blink = Mathf.Repeat(t, 1.4f) < 0.18f;
            float p = _shown;
            Color lamp = new Color(1f, 0.55f, 0.25f) * (p > 0.01f ? (blink ? 3f : 0.6f) * Mathf.Max(0.4f, p) : 0.02f);
            foreach (var m in _lamps) m.SetColor("_EmissionColor", lamp);
            Color cockpit = new Color(0.9f, 0.75f, 0.5f) * Mathf.SmoothStep(0, 1, (p - 0.4f) / 0.6f) * 1.3f;
            foreach (var m in _cockpit) m.SetColor("_EmissionColor", cockpit);
            _navL.intensity = p > 0.01f && blink ? 2.5f : 0f;
            _navR.intensity = p > 0.01f && !blink && Mathf.Repeat(t + 0.7f, 1.4f) < 0.18f ? 2.5f : 0f;
            _cabin.intensity = 2.2f * Mathf.SmoothStep(0, 1, (p - 0.3f) / 0.7f);
            _engine.intensity = (p >= 0.99f ? 2.5f : 0f) * (0.85f + 0.15f * Mathf.PerlinNoise(t * 7f, 0));
            bool beacon = s.Phase == GamePhase.Chase || (p >= 0.99f && !s.IsOver);
            _beam.SetActive(beacon && s.Phase != GamePhase.Escaped);
            if (beacon) _beamMat.SetColor("_Tint", new Color(1f, 0.9f, 0.75f, 0.22f + 0.1f * Mathf.Sin(t * 5f)));
        }
    }
}
