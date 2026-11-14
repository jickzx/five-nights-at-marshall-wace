using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Fnmw.Monster.Beast
{
    /// <summary>
    /// Plays the art thread's clips (idle, stalk, chase, lunge) when the model has them, through a
    /// PlayableGraph so no AnimatorController asset is needed. Without clips it animates the
    /// placeholder procedurally: breathing, crouching, lurching and the odd twitch.
    /// </summary>
    public class BeastAnimator : MonoBehaviour
    {
        public enum Pose { Idle, Stalk, Chase, Lunge, Frozen }

        static readonly Dictionary<Pose, string[]> Aliases = new Dictionary<Pose, string[]>
        {
            { Pose.Idle, new[] { "idle", "breath", "stand" } },
            { Pose.Stalk, new[] { "stalk", "creep", "walk", "sneak" } },
            { Pose.Chase, new[] { "chase", "run", "sprint", "gallop" } },
            { Pose.Lunge, new[] { "lunge", "attack", "pounce", "leap" } },
        };

        public bool HasClips => _clips.Count > 0;
        public string ClipSummary { get; private set; } = "procedural";
        /// <summary>The clip currently playing (or the procedural pose).</summary>
        public string CurrentClip => _clips.Count > 0 ? _clips[IndexFor(_pose)].clip.name : "procedural " + _pose.ToString().ToLowerInvariant();

        Transform _body;              // the loaded model, animated procedurally when there are no clips
        Vector3 _baseScale;
        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        Animation _legacy;
        readonly List<(Pose pose, AnimationClip clip)> _clips = new List<(Pose, AnimationClip)>();
        readonly List<float> _weights = new List<float>();
        Pose _pose = Pose.Idle;
        float _speed01, _twitchAt, _phase;
        Quaternion _twitch = Quaternion.identity;

        public void Bind(GameObject model, string resourceName)
        {
            Unbind();
            _body = model.transform;
            _baseScale = _body.localScale;
            CollectClips(model, resourceName);
            if (_clips.Count == 0) { ClipSummary = "procedural (no clips)"; return; }

            _legacy = model.GetComponentInChildren<Animation>();
            if (_legacy != null)
            {
                foreach (var (pose, clip) in _clips)
                    if (_legacy.GetClip(clip.name) == null) { clip.legacy = true; _legacy.AddClip(clip, clip.name); }
                ClipSummary = "legacy: " + string.Join(", ", _clips.ConvertAll(c => c.clip.name));
                return;
            }

            var animator = model.GetComponentInChildren<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.applyRootMotion = false;
            _graph = PlayableGraph.Create("Beast");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _mixer = AnimationMixerPlayable.Create(_graph, _clips.Count);
            for (int i = 0; i < _clips.Count; i++)
            {
                var cp = AnimationClipPlayable.Create(_graph, _clips[i].clip);
                _graph.Connect(cp, 0, _mixer, i);
                _weights.Add(i == 0 ? 1 : 0);
            }
            AnimationPlayableOutput.Create(_graph, "Beast", animator).SetSourcePlayable(_mixer);
            _graph.Play();
            ClipSummary = string.Join(", ", _clips.ConvertAll(c => c.clip.name));
        }

        void CollectClips(GameObject model, string name)
        {
            var found = new List<AnimationClip>();
            found.AddRange(Resources.LoadAll<AnimationClip>("Models/" + name));
            if (found.Count == 0) found.AddRange(Resources.LoadAll<AnimationClip>("Models/Mock/" + name));
            var anim = model.GetComponentInChildren<Animator>();
            if (anim != null && anim.runtimeAnimatorController != null) found.AddRange(anim.runtimeAnimatorController.animationClips);
            var legacy = model.GetComponentInChildren<Animation>();
            if (legacy != null) foreach (AnimationState s in legacy) found.Add(s.clip);

            foreach (Pose pose in Aliases.Keys)
            {
                AnimationClip pick = null;
                foreach (var alias in Aliases[pose])
                {
                    foreach (var c in found)
                        if (c != null && !c.name.StartsWith("__preview__") && c.name.ToLowerInvariant().Contains(alias)) { pick = c; break; }
                    if (pick != null) break;
                }
                if (pick != null) _clips.Add((pose, pick));
            }
        }

        public void Unbind()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _clips.Clear(); _weights.Clear(); _legacy = null; _body = null;
        }

        void OnDestroy() => Unbind();

        /// <summary>speed01: 0 still .. 1 flat out.</summary>
        public void Set(Pose pose, float speed01) { _pose = pose; _speed01 = Mathf.Clamp01(speed01); }

        int IndexFor(Pose pose)
        {
            Pose want = pose == Pose.Frozen ? Pose.Idle : pose;
            for (int i = 0; i < _clips.Count; i++) if (_clips[i].pose == want) return i;
            // Fall back along a sensible chain: chase -> stalk -> idle, lunge -> chase -> stalk.
            if (want == Pose.Lunge) return IndexFor(Pose.Chase);
            if (want == Pose.Chase) return IndexFor(Pose.Stalk);
            if (want == Pose.Stalk) return IndexFor(Pose.Idle);
            return 0;
        }

        void Update()
        {
            if (_body == null) return;
            if (_clips.Count > 0) { UpdateClips(); return; }
            UpdateProcedural();
        }

        void UpdateClips()
        {
            int target = IndexFor(_pose);
            if (_legacy != null)
            {
                var name = _clips[target].clip.name;
                if (!_legacy.IsPlaying(name)) _legacy.CrossFade(name, 0.25f);
                return;
            }
            float k = Mathf.Clamp01(Time.deltaTime / 0.25f);
            for (int i = 0; i < _clips.Count; i++)
            {
                _weights[i] = Mathf.MoveTowards(_weights[i], i == target ? 1f : 0f, k);
                _mixer.SetInputWeight(i, _weights[i]);
                var cp = (AnimationClipPlayable)_mixer.GetInput(i);
                var clip = _clips[i].clip;
                bool loop = _clips[i].pose != Pose.Lunge;
                if (loop && clip.length > 0 && cp.GetTime() >= clip.length) cp.SetTime(cp.GetTime() % clip.length);
                if (i == target && _weights[i] < 0.05f && !loop) cp.SetTime(0); // restart lunge on entry
                float sp = _pose == Pose.Frozen ? 0f : (_clips[i].pose == Pose.Stalk || _clips[i].pose == Pose.Chase) ? Mathf.Lerp(0.6f, 1.4f, _speed01) : 1f;
                cp.SetSpeed(sp);
            }
        }

        void UpdateProcedural()
        {
            float t = Time.time;
            Vector3 scale = Vector3.one; Quaternion rot = Quaternion.identity; Vector3 pos = Vector3.zero;
            switch (_pose)
            {
                case Pose.Idle:
                    scale = new Vector3(1, 1 + 0.03f * Mathf.Sin(t * 1.6f), 1);
                    break;
                case Pose.Stalk:
                    _phase += Time.deltaTime * Mathf.Lerp(2f, 6f, _speed01);
                    scale = new Vector3(1.12f, 0.82f, 1.12f);
                    rot = Quaternion.Euler(12, 0, 6 * Mathf.Sin(_phase));
                    pos = new Vector3(0, 0.05f * Mathf.Abs(Mathf.Sin(_phase)), 0);
                    break;
                case Pose.Chase:
                    _phase += Time.deltaTime * Mathf.Lerp(8f, 14f, _speed01);
                    scale = new Vector3(1, 0.92f + 0.08f * Mathf.Sin(_phase * 2), 1);
                    rot = Quaternion.Euler(22 + 6 * Mathf.Sin(_phase * 2), 0, 4 * Mathf.Sin(_phase));
                    pos = new Vector3(0, 0.18f * Mathf.Abs(Mathf.Sin(_phase)), 0);
                    break;
                case Pose.Lunge:
                    scale = new Vector3(0.9f, 0.9f, 1.4f);
                    rot = Quaternion.Euler(30, 0, 0);
                    break;
                case Pose.Frozen:
                    // Perfectly still, except a sudden twitch now and then.
                    if (t > _twitchAt)
                    {
                        _twitch = Random.value < 0.5f ? Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(-15f, 15f), Random.Range(-10f, 10f)) : Quaternion.identity;
                        _twitchAt = t + (_twitch == Quaternion.identity ? Random.Range(0.8f, 2.2f) : 0.09f);
                    }
                    rot = _twitch;
                    break;
            }
            float k = 1f - Mathf.Exp(-Time.deltaTime * 12f);
            _body.localScale = Vector3.Lerp(_body.localScale, Vector3.Scale(_baseScale, scale), k);
            _body.localRotation = _pose == Pose.Frozen ? rot : Quaternion.Slerp(_body.localRotation, rot, k);
            _body.localPosition = Vector3.Lerp(_body.localPosition, pos, k);
        }
    }
}
