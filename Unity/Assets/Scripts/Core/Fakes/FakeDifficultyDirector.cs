using UnityEngine;

namespace Fnmw.Core.Fakes
{
    /// <summary>Mistakes push aggression up, fast correct answers ease it. Replaced by Scripts/Monster.</summary>
    public class FakeDifficultyDirector : IDifficultyDirector
    {
        public float Difficulty { get; private set; } = 0.3f;
        public float BeastAggression { get; private set; } = 0.2f;

        public void Reset(int seed) { Difficulty = 0.3f; BeastAggression = 0.2f; }

        public void Observe(AnswerEvent a)
        {
            if (a.Correct) { Difficulty = Mathf.Clamp01(Difficulty + 0.1f); BeastAggression = Mathf.Clamp01(BeastAggression - 0.05f); }
            else { Difficulty = Mathf.Clamp01(Difficulty - 0.1f); BeastAggression = Mathf.Clamp01(BeastAggression + 0.2f); }
        }
    }
}
