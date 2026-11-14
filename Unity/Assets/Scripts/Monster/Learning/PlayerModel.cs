using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fnmw.Monster.Learning
{
    /// <summary>
    /// Online Bayesian logistic regression over the player's answers.
    ///
    ///   P(correct) = sigmoid( z / sqrt(1 + pi*v/8) )
    ///   z = bias + ability + mean(skill[c] for tested concepts c)
    ///       - wDifficulty * difficulty + wHesitation * hesitation
    ///       + wProximity * beastProximity + wStreak * tanh(streak/3)
    ///
    /// Ability and each concept skill carry a Gaussian belief (mean, variance). Every answer is
    /// one observation: an extended-Kalman (Laplace) step moves the means by the surprise
    /// (outcome - prediction) scaled by how unsure the model was, and shrinks the variance.
    /// The shared weights are learned by plain SGD with an L2 pull toward their priors.
    /// The sqrt(1 + pi*v/8) term is the probit approximation: unsure beliefs predict closer to 50%.
    /// </summary>
    public class PlayerModel
    {
        public const float PriorVar = 1.0f;          // unknown concept
        public const float AbilityPriorVar = 0.3f;
        public const float ProcessNoise = 0.005f;     // per answer: the beast never stops learning
        public const float RunNoise = 0.15f;         // per new run: memories fade a little
        public const float TargetSuccess = 0.7f;     // flow: right ~70% of the time
        public const float LearningRate = 0.05f;
        public const float L2 = 0.05f;
        public const float MinDifficulty = 0.05f, MaxDifficulty = 0.95f;
        public const int HistoryCap = 24;

        public const float W0Bias = 2.0f, W0Difficulty = 3.0f, W0Hesitation = -0.4f, W0Proximity = -0.5f, W0Streak = 0.3f;
        public const float LogRtPrior = 2.3f; // ~10 s per puzzle
        public const string General = "general";

        public LearnerState S { get; private set; }

        public PlayerModel(LearnerState state = null) { S = state ?? new LearnerState(); }

        // ---------------------------------------------------------------- beliefs

        public ConceptBelief Find(string concept)
        {
            foreach (var b in S.Concepts) if (b.Concept == concept) return b;
            return null;
        }

        public ConceptBelief Belief(string concept)
        {
            var b = Find(concept);
            if (b != null) return b;
            b = new ConceptBelief { Concept = concept };
            S.Concepts.Add(b);
            return b;
        }

        public void Register(IEnumerable<string> concepts)
        {
            if (concepts == null) return;
            foreach (var c in concepts) if (!string.IsNullOrEmpty(c)) Belief(c);
        }

        // ---------------------------------------------------------------- prediction

        public static float Sigmoid(float z) => 1f / (1f + Mathf.Exp(-z));
        public static float Logit(float p) { p = Mathf.Clamp(p, 1e-4f, 1 - 1e-4f); return Mathf.Log(p / (1 - p)); }
        static float Kappa(float variance) => Mathf.Sqrt(1f + Mathf.PI * variance / 8f);
        public static float Squash(float z, float variance) => Sigmoid(z / Kappa(variance));

        public float StreakFeature => (float)Math.Tanh(S.Streak / 3.0);

        /// <summary>Everything in z except the difficulty term, plus the predictive variance.</summary>
        float Intercept(IList<string> concepts, float proximity, bool context, out float variance)
        {
            float skill = 0, svar = 0;
            int n = Mathf.Max(1, concepts?.Count ?? 0);
            if (concepts != null)
                foreach (var c in concepts)
                {
                    var b = Find(c);
                    skill += b?.Mean ?? 0f;
                    svar += b?.Var ?? PriorVar;
                }
            else svar = PriorVar;
            variance = S.AbilityVar + svar / (n * n);
            float z = S.Bias + S.Ability + skill / n;
            if (context) z += S.WHesitation * S.Hesitation + S.WProximity * proximity + S.WStreak * StreakFeature;
            return z;
        }

        /// <summary>Probability the player gets a puzzle on these concepts right at this difficulty.</summary>
        public float Predict(IList<string> concepts, float difficulty, float proximity = 0f)
        {
            float z = Intercept(concepts, proximity, true, out float v) - S.WDifficulty * difficulty;
            return Squash(z, v);
        }

        /// <summary>Skill shown on the overlay: P(correct) on a medium (0.5) puzzle, calm context.</summary>
        public float SkillAtMedium(string concept)
        {
            var list = new[] { concept };
            float z = Intercept(list, 0, false, out float v) - S.WDifficulty * 0.5f;
            return Squash(z, v);
        }

        /// <summary>±1 sd band of SkillAtMedium, in probability space.</summary>
        public (float lo, float hi) SkillBand(string concept)
        {
            var b = Find(concept);
            float mean = b?.Mean ?? 0, sd = Mathf.Sqrt((b?.Var ?? PriorVar) + S.AbilityVar);
            float z = S.Bias + S.Ability + mean - S.WDifficulty * 0.5f;
            return (Sigmoid(z - sd), Sigmoid(z + sd));
        }

        /// <summary>Difficulty at which the predicted success equals the target (solved in closed form).</summary>
        public float DifficultyFor(IList<string> concepts, float target = TargetSuccess, float proximity = 0f)
        {
            float a = Intercept(concepts, proximity, true, out float v);
            float d = (a - Kappa(v) * Logit(target)) / Mathf.Max(0.5f, S.WDifficulty);
            return Mathf.Clamp(d, MinDifficulty, MaxDifficulty);
        }

        /// <summary>
        /// The concept to probe next: lowest lower-confidence-bound on skill. Weak concepts and
        /// concepts the beast knows little about both score low, so it tests where you're weakest
        /// or least known.
        /// </summary>
        public string FocusConcept
        {
            get
            {
                string best = null; float bestScore = float.MaxValue;
                foreach (var b in S.Concepts)
                {
                    if (b.Concept == General && S.Concepts.Count > 1) continue;
                    float score = b.Mean - 0.5f * Mathf.Sqrt(b.Var);
                    if (score < bestScore) { bestScore = score; best = b.Concept; }
                }
                return best;
            }
        }

        /// <summary>
        /// Average predicted success on a medium puzzle across the concepts you have been tested
        /// on (all known concepts before the first answer).
        /// </summary>
        public float Mastery
        {
            get
            {
                float sum = 0; int n = 0;
                foreach (var b in S.Concepts) if (b.Seen > 0) { sum += SkillAtMedium(b.Concept); n++; }
                if (n > 0) return sum / n;
                foreach (var b in S.Concepts) { sum += SkillAtMedium(b.Concept); n++; }
                if (n > 0) return sum / n;
                return Squash(S.Bias + S.Ability - S.WDifficulty * 0.5f, S.AbilityVar + PriorVar);
            }
        }

        // ---------------------------------------------------------------- learning

        /// <summary>Z-score of a response time against this player's own typical pace.</summary>
        public float ResponseZ(float seconds) =>
            seconds > 0 ? Mathf.Clamp((Mathf.Log(seconds) - S.LogRtBaseline) / 0.6f, -2f, 2f) : 0f;

        /// <summary>
        /// Learn from one answer. Concepts are the ones the answer is evidence about: every concept
        /// the puzzle tested when right, only the missed concept when wrong. Returns the prediction
        /// the model made before seeing the outcome.
        /// </summary>
        public float Learn(IList<string> concepts, float difficulty, float proximity, bool correct, float responseTime)
        {
            if (concepts == null || concepts.Count == 0) concepts = new[] { General };
            foreach (var c in concepts) Belief(c);
            int n = concepts.Count;

            float rtZ = ResponseZ(responseTime);
            // A slow, hesitant right answer is weaker evidence of skill than a fast one.
            float y = correct ? 1f - 0.3f * Mathf.Clamp01(rtZ / 2f) : 0f;

            float hes = S.Hesitation, streak = StreakFeature;
            float p = Predict(concepts, difficulty, proximity);
            float g = y - p;                                   // surprise
            float info = Mathf.Max(p * (1 - p), 0.01f);        // Fisher information of a Bernoulli

            // Extended-Kalman step with a diagonal covariance: one scalar observation of z.
            float a = 1f / n;
            float spread = S.AbilityVar;
            foreach (var c in concepts) spread += a * a * Find(c).Var;
            float denom = 1f + info * spread;
            foreach (var c in concepts)
            {
                var b = Find(c);
                float k = b.Var * a;
                b.Mean += k * g / denom;
                b.Var = Mathf.Min(PriorVar, Mathf.Max(0.02f, b.Var - k * k * info / denom) + ProcessNoise);
                b.Seen++;
                if (correct) b.Correct++;
            }
            {
                float k = S.AbilityVar;
                S.Ability += k * g / denom;
                S.AbilityVar = Mathf.Min(AbilityPriorVar, Mathf.Max(0.02f, S.AbilityVar - k * k * info / denom) + ProcessNoise * 0.5f);
            }

            // SGD on the shared weights (dz/dw = feature), pulled gently back toward their priors.
            S.Bias += LearningRate * (g - L2 * (S.Bias - W0Bias));
            S.WDifficulty += LearningRate * (g * -difficulty - L2 * (S.WDifficulty - W0Difficulty));
            S.WHesitation += LearningRate * (g * hes - L2 * (S.WHesitation - W0Hesitation));
            S.WProximity += LearningRate * (g * proximity - L2 * (S.WProximity - W0Proximity));
            S.WStreak += LearningRate * (g * streak - L2 * (S.WStreak - W0Streak));
            S.WDifficulty = Mathf.Clamp(S.WDifficulty, 0.5f, 6f);

            // Behaviour statistics.
            if (responseTime > 0) S.LogRtBaseline += 0.1f * (Mathf.Log(responseTime) - S.LogRtBaseline);
            S.Hesitation = 0.6f * S.Hesitation + 0.4f * rtZ;
            S.Momentum = 0.7f * S.Momentum + 0.3f * g;
            S.Streak = correct ? Mathf.Max(0, S.Streak) + 1 : Mathf.Min(0, S.Streak) - 1;
            S.TotalAnswers++;

            S.History.Add(new AnswerRecord
            {
                Concept = concepts[0], Predicted = p, Correct = correct, Difficulty = difficulty,
                ResponseTime = responseTime, Run = S.Runs,
            });
            while (S.History.Count > HistoryCap) S.History.RemoveAt(0);
            return p;
        }

        /// <summary>A new run starts: forget the mood of the last run, keep the skills but trust them a bit less.</summary>
        public void BeginRun()
        {
            S.Runs++;
            S.Streak = 0;
            S.Momentum = 0;
            S.Hesitation *= 0.5f;
            foreach (var b in S.Concepts) b.Var = Mathf.Min(PriorVar, b.Var + RunNoise);
            S.AbilityVar = Mathf.Min(AbilityPriorVar, S.AbilityVar + RunNoise * 0.5f);
        }
    }
}
