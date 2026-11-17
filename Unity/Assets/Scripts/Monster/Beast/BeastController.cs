using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Monster.Beast
{
    /// <summary>
    /// The beast's body. Each frame it tells the brain what it can sense (including whether the
    /// player is looking at it), moves toward the brain's intent, swaps models when it evolves,
    /// plays clips or procedural motion, and raises the stage, threat and caught events.
    /// </summary>
    public class BeastController : MonoBehaviour
    {
        // Heights of the art thread's models (manifest.json); placeholders and wrong-unit imports are scaled to these.
        static readonly float[] StageHeight = { 2.35f, 2.9f, 3.55f };

        /// <summary>The height a stage's model is fitted to.</summary>
        public static float TargetHeight(int stage) => StageHeight[Mathf.Clamp(stage - 1, 0, 2)];

        /// <summary>
        /// Scale factor for a model measured <paramref name="measured"/> tall to come out <paramref name="want"/> tall.
        /// With <paramref name="exact"/> false it only corrects wrong import units (off by more than 0.6x-1.6x) and
        /// otherwise trusts the art. 1 when nothing should change.
        /// </summary>
        public static float FitScale(float measured, float want, bool exact)
        {
            if (measured <= 0.01f || want <= 0f) return 1f;
            float ratio = want / measured;
            if (exact) return Mathf.Abs(ratio - 1f) < 0.005f ? 1f : ratio;
            return ratio < 0.6f || ratio > 1.6f ? ratio : 1f;
        }

        public static BeastController Instance { get; private set; }
        public BeastIntent LastIntent { get; private set; }
        public bool Watched { get; private set; }
        public float Threat { get; private set; }

        GameObject _model;
        int _modelStage;
        bool _caught;
        BeastAnimator _anim;
        Light _glow;
        float _flash, _spawnPop = 1f;
        BeastMode _lastMode;
        bool _threatDirty;

        BeastBrain Brain => Services.BeastBrain as BeastBrain;

        void Awake()
        {
            Instance = this;
            _anim = gameObject.AddComponent<BeastAnimator>();
            var glow = new GameObject("glow");
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = new Vector3(0, 1.4f, 0.6f);
            _glow = glow.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.color = new Color(1f, 0.08f, 0.05f);
            _glow.range = 4f;
            _glow.intensity = 1.5f;
            _glow.shadows = LightShadows.None;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            var s = Services.State;
            if (_modelStage != s.BeastStage) SwapModel(s.BeastStage);
            (Services.Difficulty as Learning.AdaptiveDirector)?.RegisterWorld(s.World);
            AnimateEffects();
            var player = Services.Player;
            if (player == null || s.Phase == GamePhase.Title || s.IsOver) { _anim.Set(BeastAnimator.Pose.Idle, 0); return; }

            var cam = Camera.main;
            Vector3 fwd = cam ? cam.transform.forward : player.forward;
            Watched = cam && IsWatched(cam);
            float dist = Vector3.Distance(Flat(transform.position), Flat(player.position));

            var intent = Services.BeastBrain.Think(new BeastPerception
            {
                BeastPosition = transform.position, PlayerPosition = player.position, Distance = dist,
                Phase = s.Phase, Stage = s.BeastStage, Elapsed = s.Elapsed,
                Aggression = Services.Difficulty.BeastAggression, Mistakes = s.Mistakes,
                PlayerForward = fwd, Watched = Watched, Grace = s.BeastGrace,
            }, Time.deltaTime);
            LastIntent = intent;
            s.SetBeastStage(intent.DesiredStage);

            var to = Flat(intent.MoveTarget) - Flat(transform.position);
            float step = 0f;
            if (to.sqrMagnitude > 0.0025f && intent.Speed > 0f)
            {
                step = Mathf.Min(intent.Speed * Time.deltaTime, to.magnitude);
                transform.position += to.normalized * step;
            }
            SnapToGround();

            // Face: the player when still, backing off or creeping; otherwise where it is going.
            var mode = Brain?.Mode ?? BeastMode.Chase;
            Vector3 face = (step <= 0f || mode == BeastMode.Retreat || mode == BeastMode.Approach || mode == BeastMode.Stalk)
                ? Flat(player.position) - Flat(transform.position) : to;
            if (face.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(face), 1f - Mathf.Exp(-Time.deltaTime * (mode == BeastMode.Frozen ? 2f : 8f)));

            if (mode == BeastMode.Lunge && _lastMode != BeastMode.Lunge) { Services.Audio.PlayStinger("beast_roar"); _flash = 1f; GameEvents.RaiseBeastLunged(); }
            _lastMode = mode;
            _anim.Set(PoseFor(mode, intent), intent.Speed / 6f);

            s.BeastDistance = Vector3.Distance(Flat(transform.position), Flat(player.position));
            UpdateThreat(s, mode, intent);
            if (!_caught && intent.Hunting && s.BeastDistance < BeastBrain.CatchDistance)
            {
                _caught = true;
                Services.Audio.PlayStinger("beast_roar");
                GameEvents.RaisePlayerCaught();
            }
        }

        static BeastAnimator.Pose PoseFor(BeastMode m, BeastIntent i)
        {
            switch (m)
            {
                case BeastMode.Lunge: return BeastAnimator.Pose.Lunge;
                case BeastMode.Chase: return BeastAnimator.Pose.Chase;
                case BeastMode.Frozen: case BeastMode.Loom: return BeastAnimator.Pose.Frozen;
                case BeastMode.Retreat: return BeastAnimator.Pose.Stalk;
                default: return i.Speed > 0.05f ? (i.Speed > 3.5f ? BeastAnimator.Pose.Chase : BeastAnimator.Pose.Stalk) : BeastAnimator.Pose.Idle;
            }
        }

        /// <summary>
        /// Threat for music/UI: the scaffold's distance curve, raised by what the beast is doing.
        /// Never lower than the distance curve, so it agrees with GameDirector's own estimate.
        /// </summary>
        void UpdateThreat(GameState s, BeastMode mode, BeastIntent intent)
        {
            float distance = Mathf.Max(Mathf.Clamp01(1f - s.BeastDistance / 30f), (s.BeastStage - 1) * 0.3f);
            float behaviour;
            switch (mode)
            {
                case BeastMode.Lunge: behaviour = 1f; break;
                case BeastMode.Loom: behaviour = 0.9f; break;
                case BeastMode.Chase: behaviour = 0.8f + 0.2f * Services.Difficulty.BeastAggression; break;
                case BeastMode.Approach: behaviour = intent.Hunting ? 0.85f : 0.35f + 0.3f * Services.Difficulty.BeastAggression; break;
                case BeastMode.Frozen: behaviour = 0.4f + 0.3f * Mathf.Clamp01(1f - s.BeastDistance / 20f); break;
                case BeastMode.Stalk: behaviour = 0.25f + 0.3f * Services.Difficulty.BeastAggression; break;
                default: behaviour = 0.1f; break;
            }
            float target = Mathf.Max(distance, behaviour);
            Threat = mode == BeastMode.Lunge ? target : Mathf.MoveTowards(Threat, target, Time.deltaTime * 0.6f);
            Threat = Mathf.Max(Threat, distance);
            _threatDirty = true;
        }

        // Applied after GameDirector.Update so the beast's view of the threat is the one that sticks.
        void LateUpdate()
        {
            if (!_threatDirty) return;
            _threatDirty = false;
            var s = Services.State;
            if (!s.IsOver) s.SetThreat(Threat);
        }

        bool IsWatched(Camera cam)
        {
            Vector3 chest = transform.position + Vector3.up * TargetHeight(_modelStage) * 0.6f;
            Vector3 vp = cam.WorldToViewportPoint(chest);
            if (vp.z <= 0 || vp.x < 0.05f || vp.x > 0.95f || vp.y < 0.02f || vp.y > 0.98f) return false;
            float visible = RenderSettings.fog && RenderSettings.fogMode != FogMode.Linear
                ? Mathf.Min(40f, 1.9f / Mathf.Max(0.01f, RenderSettings.fogDensity))
                : RenderSettings.fog ? RenderSettings.fogEndDistance : 40f;
            if (vp.z > visible) return false;
            Vector3 from = cam.transform.position, dir = chest - from;
            foreach (var hit in Physics.RaycastAll(from, dir.normalized, dir.magnitude - 0.3f))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (Services.Player && hit.collider.transform.IsChildOf(Services.Player)) continue;
                return false; // something solid in the way
            }
            return true;
        }

        void SnapToGround()
        {
            var p = transform.position;
            float best = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(new Vector3(p.x, p.y + 30f, p.z), Vector3.down, 80f))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (Services.Player && hit.collider.transform.IsChildOf(Services.Player)) continue;
                var size = hit.collider.bounds.size;
                if (size.x < 8f && size.z < 8f) continue; // props, not ground
                best = Mathf.Max(best, hit.point.y);
            }
            if (!float.IsNegativeInfinity(best)) transform.position = new Vector3(p.x, best, p.z);
        }

        void SwapModel(int stage)
        {
            if (_model) { _anim.Unbind(); Destroy(_model); }
            var rot = transform.rotation;
            transform.rotation = Quaternion.identity;
            string name = "beast_stage" + stage;
            _model = ModelLoader.Load(name, transform);
            _model.transform.localPosition = Vector3.zero;
            _model.transform.localRotation = Quaternion.identity;
            var label = _model.transform.Find("label");
            if (label) label.gameObject.SetActive(false); // the beast is not a signpost
            bool art = ModelLoader.HasArt(name);
            // Fitted on the model (a child), before the animator records its base scale, so the procedural poses
            // scale around the fitted size and AnimateEffects' spawn pop (on this root) multiplies on top of it.
            FitHeight(_model, TargetHeight(stage), false);
            if (!art) AddEyes(_model);   // real models have their own faces
            else PlaceGlow(_model);
            transform.rotation = rot;
            _anim.Bind(_model, name);
            if (_modelStage != 0) { _flash = 1f; _spawnPop = 0f; }
            _modelStage = stage;
        }

        /// <summary>Scale a freshly loaded model (under an unscaled, unrotated root) to <paramref name="want"/> metres tall.</summary>
        public static void FitHeight(GameObject model, float want, bool exact)
        {
            var b = Bounds(model);
            // measured in the root's own units: the root may still be mid spawn-pop from an earlier evolution
            var parent = model.transform.parent;
            float rootScale = parent ? Mathf.Max(0.001f, parent.lossyScale.y) : 1f;
            float k = FitScale(b.size.y / rootScale, want, exact);
            if (k != 1f) model.transform.localScale *= k;
        }

        void AddEyes(GameObject model)
        {
            var b = Bounds(model);
            var local = transform.InverseTransformPoint(new Vector3(b.center.x, b.max.y - b.size.y * 0.15f, b.max.z));
            float size = Mathf.Clamp(b.size.y * 0.06f, 0.06f, 0.18f);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Material mat = null;
            if (shader != null && Application.isPlaying)
            {
                mat = new Material(shader);
                mat.SetColor("_BaseColor", new Color(1f, 0.1f, 0.05f));
                mat.color = new Color(1f, 0.1f, 0.05f);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                eye.name = "eye";
                Destroy(eye.GetComponent<Collider>());
                eye.transform.SetParent(model.transform, true);
                eye.transform.position = transform.TransformPoint(local + new Vector3(side * b.size.x * 0.18f, 0, 0.02f));
                eye.transform.localScale = Vector3.one * size / Mathf.Max(0.001f, model.transform.lossyScale.y);
                if (mat) eye.GetComponent<Renderer>().sharedMaterial = mat;
            }
            _glow.transform.position = transform.TransformPoint(local + new Vector3(0, 0, 0.4f));
        }

        void PlaceGlow(GameObject model)
        {
            var b = Bounds(model);
            _glow.transform.position = new Vector3(b.center.x, b.max.y - b.size.y * 0.12f, b.center.z) + transform.forward * (b.extents.z + 0.4f);
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) if (!(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
            return b;
        }

        void AnimateEffects()
        {
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 1.2f);
            _glow.intensity = 1.2f + 0.3f * Mathf.Sin(Time.time * 3f) + 10f * _flash;
            _glow.range = 4f + 6f * _flash;
            if (_spawnPop < 1f && _model)
            {
                _spawnPop = Mathf.MoveTowards(_spawnPop, 1f, Time.deltaTime * 1.5f);
                float jitter = (1f - _spawnPop) * 0.15f * Mathf.Sin(Time.time * 60f);
                transform.localScale = Vector3.one * (0.6f + 0.4f * Mathf.SmoothStep(0, 1, _spawnPop) + jitter);
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    }
}
