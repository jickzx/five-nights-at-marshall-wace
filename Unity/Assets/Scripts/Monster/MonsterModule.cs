using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Monster
{
    /// <summary>Monster thread entry point. Register real brain/director here; spawns the beast.</summary>
    public static class MonsterModule
    {
        public static void Install()
        {
            // Services.BeastBrain = new YourBrain(); Services.Difficulty = new YourDirector();
            var go = new GameObject("Beast");
            go.transform.position = new Vector3(0, 0, -25);
            go.AddComponent<BeastController>();
        }
    }

    /// <summary>Placeholder body: asks IBeastBrain what to do each frame and moves straight there.</summary>
    public class BeastController : MonoBehaviour
    {
        GameObject _model;
        int _modelStage;
        bool _caught;

        void Update()
        {
            var s = Services.State;
            if (_modelStage != s.BeastStage) SwapModel(s.BeastStage);
            var player = Services.Player;
            if (player == null || s.Phase == GamePhase.Title || s.IsOver) return;

            Vector3 flat(Vector3 v) => new Vector3(v.x, 0, v.z);
            float dist = Vector3.Distance(flat(transform.position), flat(player.position));
            var intent = Services.BeastBrain.Think(new BeastPerception
            {
                BeastPosition = transform.position, PlayerPosition = player.position, Distance = dist,
                Phase = s.Phase, Stage = s.BeastStage, Elapsed = s.Elapsed,
                Aggression = Services.Difficulty.BeastAggression, Mistakes = s.Mistakes,
            }, Time.deltaTime);

            s.SetBeastStage(intent.DesiredStage);
            var to = flat(intent.MoveTarget) - flat(transform.position);
            if (to.sqrMagnitude > 0.01f)
            {
                transform.position += to.normalized * Mathf.Min(intent.Speed * Time.deltaTime, to.magnitude);
                transform.rotation = Quaternion.LookRotation(to);
            }
            s.BeastDistance = Vector3.Distance(flat(transform.position), flat(player.position));
            if (!_caught && s.BeastDistance < 1.6f) { _caught = true; GameEvents.RaisePlayerCaught(); }
        }

        void SwapModel(int stage)
        {
            if (_model) Destroy(_model);
            _model = ModelLoader.Load("beast_stage" + stage, transform);
            _modelStage = stage;
        }
    }
}
