using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Fnmw.Game
{
    /// <summary>
    /// Plays a named take from a model's FBX (Resources/Models/&lt;model&gt; or Mock/) through the Playables API,
    /// so no Animator Controller asset is needed. Returns false when the art or the take is missing, and the
    /// caller animates in code instead.
    /// </summary>
    public class ClipPlayer : MonoBehaviour
    {
        PlayableGraph _graph;
        AnimationClipPlayable _clip;

        public static AnimationClip Find(string model, string take)
        {
            foreach (var path in new[] { "Models/" + model, "Models/Mock/" + model })
            {
                AnimationClip best = null;
                foreach (var c in Resources.LoadAll<AnimationClip>(path))
                {
                    if (!c || c.name.StartsWith("__preview__")) continue;
                    if (string.Equals(c.name, take, StringComparison.OrdinalIgnoreCase)) return c;
                    if (c.name.EndsWith("|" + take, StringComparison.OrdinalIgnoreCase) || c.name.IndexOf(take, StringComparison.OrdinalIgnoreCase) >= 0) best = c;
                }
                if (best) return best;
            }
            return null;
        }

        /// <summary>Play a take on target (holding the last frame). Returns the player, or null if there is no such take.</summary>
        public static ClipPlayer Play(GameObject target, string model, string take, float speed = 1f, float startTime = 0f)
        {
            if (!target) return null;
            var clip = Find(model, take);
            if (!clip) return null;
            var animator = target.GetComponentInChildren<Animator>();
            if (!animator) animator = target.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var cp = animator.gameObject.GetComponent<ClipPlayer>() ?? animator.gameObject.AddComponent<ClipPlayer>();
            cp.Stop();
            cp._graph = PlayableGraph.Create("fnmw_" + take);
            cp._graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(cp._graph, "anim", animator);
            cp._clip = AnimationClipPlayable.Create(cp._graph, clip);
            cp._clip.SetSpeed(speed);
            cp._clip.SetDuration(clip.length);
            cp._clip.SetTime(Mathf.Clamp(startTime, 0f, clip.length));
            output.SetSourcePlayable(cp._clip);
            cp._graph.Play();
            cp.Length = clip.length;
            return cp;
        }

        public float Length { get; private set; }
        public float Time01 => _clip.IsValid() && Length > 0 ? (float)(_clip.GetTime() / Length) : 1f;

        void Update()
        {
            // Hold the final pose: clamp time instead of letting a non-looping take run on.
            if (_clip.IsValid() && _clip.GetTime() >= Length) { _clip.SetTime(Length); _clip.SetSpeed(0); }
        }

        public void Stop()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        void OnDestroy() => Stop();
    }
}
