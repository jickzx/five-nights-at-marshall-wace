using System;
using System.Collections.Generic;

namespace Fnmw.Monster.Learning
{
    /// <summary>Belief about one grammar concept: a Gaussian over the player's skill (logit scale).</summary>
    [Serializable]
    public class ConceptBelief
    {
        public string Concept;
        public float Mean;                         // skill offset on the logit scale, 0 = average
        public float Var = PlayerModel.PriorVar;   // how unsure the beast is
        public int Seen;
        public int Correct;
    }

    /// <summary>One answer the beast learned from: what it predicted and what actually happened.</summary>
    [Serializable]
    public class AnswerRecord
    {
        public string Concept;
        public float Predicted;
        public bool Correct;
        public float Difficulty;
        public float ResponseTime;
        public int Run;
    }

    /// <summary>
    /// Everything the beast has learned about this player. Plain fields only so JsonUtility can
    /// round-trip it to Application.persistentDataPath.
    /// </summary>
    [Serializable]
    public class LearnerState
    {
        public int Version = 1;
        public int Runs;
        public int TotalAnswers;

        // Global ability shared by every concept (logit scale) and its variance.
        public float Ability;
        public float AbilityVar = PlayerModel.AbilityPriorVar;

        // Shared weights of the logistic model, learned online by SGD.
        public float Bias = PlayerModel.W0Bias;
        public float WDifficulty = PlayerModel.W0Difficulty;
        public float WHesitation = PlayerModel.W0Hesitation;
        public float WProximity = PlayerModel.W0Proximity;
        public float WStreak = PlayerModel.W0Streak;

        // Running statistics of the player's behaviour.
        public float LogRtBaseline = PlayerModel.LogRtPrior; // typical ln(response seconds)
        public float Hesitation;   // EWMA of how much slower than usual recent answers were (z-score)
        public float Momentum;     // EWMA of (outcome - prediction): + means better than expected
        public int Streak;         // +n right in a row, -n wrong in a row

        public List<ConceptBelief> Concepts = new List<ConceptBelief>();
        public List<AnswerRecord> History = new List<AnswerRecord>();
    }
}
