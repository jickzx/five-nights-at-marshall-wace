using Fnmw.Core;
using UnityEngine;

namespace Fnmw.XR
{
    /// <summary>
    /// A dim chevron on the ground a couple of metres ahead, pointing the way to the current objective (the lit
    /// console, or the ship in the final run). The same target the visor's objective line uses
    /// (<see cref="Interactable.CurrentObjective"/>). In a headset the visor's bearing rail is hard to read, so
    /// this puts the direction where the player is already looking while they walk.
    ///
    /// It lies on whatever is underneath (raycast down, tilted to the surface, lifted a few millimetres), so it
    /// neither floats over dips nor sinks into slopes. Hidden on consoles, screens, cutscenes and endings; it
    /// fades out as the destination comes into reach. No per-frame allocations.
    /// </summary>
    public sealed class XrGuide : MonoBehaviour
    {
        public const float Ahead = 1.9f;          // metres in front of the player's feet
        public const float FadeFar = 5.5f, FadeNear = 2.6f;
        public const float MaxAlpha = 0.32f;
        const float Lift = 0.025f, ProbeUp = 2.5f, ProbeDown = 6f;

        static readonly RaycastHit[] Hits = new RaycastHit[12];

        Transform _player, _chevron, _beast;
        float _beastLookup;
        Material _mat;
        Renderer _renderer;
        float _alpha;
        bool _theater;

        public void Build(Transform player)
        {
            _player = player;
            var go = new GameObject("GuideChevron");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = ChevronMesh();
            _renderer = go.AddComponent<MeshRenderer>();
            var sh = Resources.Load<Shader>("XR/XrOverlay");
            _mat = new Material(sh ? sh : Shader.Find("Sprites/Default")) { name = "xr_guide", color = new Color(0.85f, 0.78f, 0.62f, 0f) };
            _mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);   // the ground and props hide it
            _mat.renderQueue = 3000;
            _renderer.sharedMaterial = _mat;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _chevron = go.transform;
            _renderer.enabled = false;
        }

        /// <summary>The rig tells the guide when the theater (a cutscene) is up.</summary>
        public void SetTheater(bool on) => _theater = on;

        public static bool ShouldShow(GamePhase phase, bool theater, bool cutscene, bool over) =>
            !theater && !cutscene && !over && (phase == GamePhase.Explore || phase == GamePhase.Chase);

        /// <summary>Opacity for a flat distance to the objective: full beyond FadeFar, gone inside FadeNear.</summary>
        public static float FadeFor(float distance) => MaxAlpha * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FadeNear, FadeFar, distance));

        void LateUpdate()
        {
            var s = Services.State;
            var target = s != null ? Interactable.CurrentObjective(s) : null;
            bool show = _player && _player.gameObject.activeInHierarchy && target && s != null
                && ShouldShow(s.Phase, _theater, XrBridge.CutscenePlaying || XrBridge.HoldTheater, s.IsOver);

            float want = 0f;
            Vector3 dir = Vector3.forward;
            if (show)
            {
                Vector3 to = target.transform.position - _player.position; to.y = 0;
                float d = to.magnitude;
                want = FadeFor(d);
                if (d > 0.01f) dir = to / d;
                if (d < Ahead + 0.5f) want = 0f;
            }
            // hide at once when a console or cutscene takes over, fade in and out gently otherwise
            _alpha = show ? Mathf.MoveTowards(_alpha, want, Time.unscaledDeltaTime * 0.8f) : 0f;
            if (_alpha <= 0.003f) { if (_renderer.enabled) _renderer.enabled = false; return; }

            if (!PlaceToward(dir)) { _renderer.enabled = false; return; }
            // a slow breath, so it reads as a guide and not a decal
            float pulse = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 2.2f);
            var c = _mat.color; c.a = _alpha * pulse; _mat.color = c;
            if (!_renderer.enabled) _renderer.enabled = true;
        }

        public Transform Chevron => _chevron;

        /// <summary>Lay the chevron on the ground <see cref="Ahead"/> metres along <paramref name="dir"/> from the
        /// player's feet. False when there is no floor there (it is then hidden).</summary>
        public bool PlaceToward(Vector3 dir)
        {
            if (!_beast && Time.unscaledTime > _beastLookup)
            {
                _beastLookup = Time.unscaledTime + 3f;      // the beast is the Monster thread's; found by name, rarely
                var b = GameObject.Find("Beast");
                if (b) _beast = b.transform;
            }
            Vector3 feet = _player.position;
            Vector3 at = feet + dir * Ahead;
            if (!Ground(at, feet.y, out var point, out var normal)) return false;
            // The sea is displaced in its shader and has no collider. Keep the marker above its crests.
            var state = Services.State;
            float waterTop = state.SeaLevel + state.SeaWaveCrest;
            if (point.y < waterTop) { point.y = waterTop; normal = Vector3.up; }
            // pointing along the ground, tilted with it
            Vector3 along = Vector3.ProjectOnPlane(dir, normal);
            if (along.sqrMagnitude < 1e-4f) along = dir;
            _chevron.SetPositionAndRotation(point + normal * Lift, Quaternion.LookRotation(along.normalized, normal));
            return true;
        }

        /// <summary>The highest solid surface under <paramref name="at"/> near the feet' height, ignoring the
        /// player, the beast and triggers.</summary>
        bool Ground(Vector3 at, float feetY, out Vector3 point, out Vector3 normal)
        {
            point = at; normal = Vector3.up;
            var origin = new Vector3(at.x, feetY + ProbeUp, at.z);
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, ProbeUp + ProbeDown, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                var t = h.collider.transform;
                if (t.IsChildOf(_player)) continue;
                if (_beast && t.IsChildOf(_beast)) continue;
                if (h.normal.y < 0.55f) continue;            // walls and steep rock faces are not floor
                if (h.point.y > feetY + 1.2f) continue;      // the top of a pillar is not where the path goes
                if (h.point.y > best) { best = h.point.y; point = h.point; normal = h.normal; }
            }
            return !float.IsNegativeInfinity(best);
        }

        /// <summary>A flat chevron (an arrow head without a stem), 0.5 m wide, in the XZ plane pointing +Z.</summary>
        static Mesh ChevronMesh()
        {
            const float w = 0.26f, len = 0.22f, thick = 0.075f;
            var v = new[]
            {
                new Vector3(-w, 0, -len * 0.5f), new Vector3(0, 0, len * 0.5f), new Vector3(w, 0, -len * 0.5f),
                new Vector3(w - thick, 0, -len * 0.5f), new Vector3(0, 0, len * 0.5f - thick * 1.4f), new Vector3(-w + thick, 0, -len * 0.5f),
            };
            // outer left, tip, outer right, inner right, inner tip, inner left: two quads (the arms)
            var tris = new[] { 0, 1, 4, 0, 4, 5, 4, 1, 2, 4, 2, 3 };
            var m = new Mesh { name = "xr_chevron", vertices = v, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
