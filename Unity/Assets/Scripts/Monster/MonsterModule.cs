using Fnmw.Core;
using Fnmw.Monster.Beast;
using Fnmw.Monster.Learning;
using Fnmw.Monster.Overlay;
using UnityEngine;

namespace Fnmw.Monster
{
    /// <summary>
    /// Monster thread entry point. Registers the learning director and the beast brain, wires
    /// them to the game's events and spawns the beast and the F3 debug overlay.
    /// </summary>
    public static class MonsterModule
    {
        public static AdaptiveDirector Director { get; private set; }
        public static BeastBrain Brain { get; private set; }

        public static void Install()
        {
            Director = new AdaptiveDirector(BrainStore.Default());
            Brain = new BeastBrain();
            Services.Difficulty = Director;
            Services.BeastBrain = Brain;

            // GameDirector calls Director.Observe itself; we add what the answer event lacks:
            // the concepts the puzzle tests and how close the beast was when it was served.
            GameEvents.PuzzleStarted += p =>
            {
                float d = Services.State.BeastDistance;
                float proximity = float.IsInfinity(d) ? 0f : Mathf.Clamp01(1f - d / 30f);
                Director.NotePuzzle(AdaptiveDirector.ConceptsOf(p), proximity, Services.State.World);
            };
            GameEvents.AnswerSubmitted += e => Brain.OnAnswer(e.Correct, Director.BeastAggression);

            var go = new GameObject("Beast");
            go.transform.position = new Vector3(0, 0, -BeastBrain.FogEdge);
            go.AddComponent<BeastController>();
            go.AddComponent<BrainOverlay>().Bind(Director);
            Debug.Log($"[Beast] memory: {Director.Store.Path} (run {Director.Model.S.Runs}, {Director.Model.S.TotalAnswers} answers)");
        }
    }
}
