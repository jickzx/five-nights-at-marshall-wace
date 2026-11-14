using System;
using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Monster.Learning
{
    /// <summary>
    /// The beast's learning brain, behind IDifficultyDirector. It wraps a PlayerModel (online
    /// Bayesian logistic regression), turns its predictions into the pace of the game, explains
    /// itself for the debug overlay and persists what it learned.
    ///
    ///   Difficulty      the puzzle difficulty at which the model predicts a 70% success rate
    ///                   on the concept it wants to probe (keeps the player in flow)
    ///   FocusConcept    the concept with the lowest lower-confidence bound on skill
    ///   BeastAggression rises with predicted weakness, with doing worse than predicted and with
    ///                   mistake streaks; falls with the opposite
    /// </summary>
    public class AdaptiveDirector : IDifficultyDirector
    {
        public struct AggressionTerm { public string Label; public float Value; }

        /// <summary>What changed after the last answer, for the overlay's flash line.</summary>
        public struct Change
        {
            public bool Valid, Correct;
            public string Concept;
            public float SkillBefore, SkillAfter, AggressionBefore, AggressionAfter, DifficultyBefore, DifficultyAfter, Predicted;
            public float At; // Time.realtimeSinceStartup
        }

        public const float BaseAggression = 0.05f;

        public PlayerModel Model { get; private set; }
        public BrainStore Store { get; }
        public float Difficulty { get; private set; }
        public float BeastAggression { get; private set; }
        public string FocusConcept { get; private set; }
        public float PredictedSuccess { get; private set; }
        public string DifficultyReason { get; private set; } = "";
        public string AggressionReason { get; private set; } = "";
        public IReadOnlyList<AggressionTerm> AggressionTerms => _terms;
        public Change LastChange { get; private set; }
        public int Version { get; private set; }   // bumps whenever the model changes
        public event Action Updated;

        readonly List<AggressionTerm> _terms = new List<AggressionTerm>();
        string[] _pendingConcepts;
        float _pendingProximity;

        public AdaptiveDirector(BrainStore store = null, LearnerState state = null)
        {
            Store = store;
            Model = new PlayerModel(state ?? store?.Load());
            Recompute();
        }

        // ---------------------------------------------------------------- IDifficultyDirector

        /// <summary>Called by the game at the start of every run. Keeps what was learned.</summary>
        public void Reset(int seed)
        {
            Model.BeginRun();
            _pendingConcepts = null;
            LastChange = default;
            Recompute();
            Save();
        }

        public void Observe(AnswerEvent answer)
        {
            float skillBefore, aggBefore = BeastAggression, diffBefore = Difficulty;
            var concepts = EvidenceConcepts(answer);
            skillBefore = Model.SkillAtMedium(concepts[0]);
            float p = Model.Learn(concepts, answer.Difficulty, _pendingProximity, answer.Correct, answer.ResponseTime);
            Recompute();
            LastChange = new Change
            {
                Valid = true, Correct = answer.Correct, Concept = concepts[0], Predicted = p,
                SkillBefore = skillBefore, SkillAfter = Model.SkillAtMedium(concepts[0]),
                AggressionBefore = aggBefore, AggressionAfter = BeastAggression,
                DifficultyBefore = diffBefore, DifficultyAfter = Difficulty,
                At = Time.realtimeSinceStartup,
            };
            Save();
            Updated?.Invoke();
        }

        // ---------------------------------------------------------------- context from the game

        /// <summary>
        /// A puzzle was served. Concepts are what it tests (the language engine tags each tile);
        /// proximity is 0 (beast far) .. 1 (beast on top of the player).
        /// </summary>
        public void NotePuzzle(IEnumerable<string> concepts, float proximity01, LanguageWorld world = null)
        {
            RegisterWorld(world);
            _pendingConcepts = concepts?.Where(c => !string.IsNullOrEmpty(c)).Distinct().ToArray();
            if (_pendingConcepts != null && _pendingConcepts.Length == 0) _pendingConcepts = null;
            Model.Register(_pendingConcepts);
            if (_pendingConcepts != null && _worldConcepts.Count > 0) foreach (var c in _pendingConcepts) _worldConcepts.Add(c);
            _pendingProximity = Mathf.Clamp01(proximity01);
            Recompute();
        }

        LanguageWorld _registeredWorld;
        readonly HashSet<string> _worldConcepts = new HashSet<string>();

        /// <summary>Concepts the current world's puzzles test (empty until a world is known).</summary>
        public IReadOnlyCollection<string> WorldConcepts => _worldConcepts;

        /// <summary>Learn the names of every concept the world's puzzles test, so the overlay can show them all.</summary>
        public void RegisterWorld(LanguageWorld world)
        {
            if (world == null || world == _registeredWorld) return;
            _registeredWorld = world;
            _worldConcepts.Clear();
            foreach (var p in world.Puzzles)
                foreach (var c in ConceptsOf(p)) { _worldConcepts.Add(c); Model.Belief(c); }
            Recompute();
        }

        public static IEnumerable<string> ConceptsOf(LangPuzzle p) =>
            p?.Tiles == null ? Enumerable.Empty<string>()
                : p.Tiles.Where(t => t?.Concepts != null).SelectMany(t => t.Concepts).Where(c => !string.IsNullOrEmpty(c)).Distinct();

        string[] EvidenceConcepts(AnswerEvent a)
        {
            // Wrong: the language engine says which concept was missed, the error belongs to it.
            if (!a.Correct && !string.IsNullOrEmpty(a.MissedConcept)) return new[] { a.MissedConcept };
            // Right: evidence for every concept the puzzle tested.
            if (_pendingConcepts != null) return _pendingConcepts;
            return new[] { PlayerModel.General };
        }

        // ---------------------------------------------------------------- memory

        public void Save() => Store?.Save(Model.S);

        /// <summary>Wipe everything the beast learned about this player (overlay: F4).</summary>
        public void ForgetPlayer()
        {
            int runs = Model.S.Runs;
            Store?.Delete();
            Model = new PlayerModel(new LearnerState { Runs = runs });
            foreach (var c in _worldConcepts) Model.Belief(c);
            LastChange = default;
            Recompute();
            Save();
            Updated?.Invoke();
        }

        public string ToJson() => BrainStore.ToJson(Model.S);
        public static AdaptiveDirector FromJson(string json) => new AdaptiveDirector(null, BrainStore.FromJson(json));

        // ---------------------------------------------------------------- outputs

        void Recompute()
        {
            Version++;
            FocusConcept = Model.Focus(_worldConcepts);
            var focus = FocusConcept != null ? new[] { FocusConcept } : null;
            Difficulty = Model.DifficultyFor(focus);
            PredictedSuccess = Model.Predict(focus, Difficulty);
            string what = FocusConcept != null ? Pretty(FocusConcept) : "anything";
            if (Difficulty <= PlayerModel.MinDifficulty + 1e-3f)
                DifficultyReason = $"Easiest puzzles: even then only {PredictedSuccess:P0} likely on {what}";
            else if (Difficulty >= PlayerModel.MaxDifficulty - 1e-3f)
                DifficultyReason = $"Hardest puzzles: still {PredictedSuccess:P0} likely on {what}";
            else
                DifficultyReason = $"Tuned so you get {what} right {PlayerModel.TargetSuccess:P0} of the time (flow)";

            var s = Model.S;
            float mastery = Model.Mastery;
            _terms.Clear();
            _terms.Add(new AggressionTerm { Label = $"weakness (mastery {mastery:P0})", Value = 1.0f * (1f - mastery) });
            _terms.Add(new AggressionTerm
            {
                Label = s.Momentum < 0 ? "doing worse than predicted" : "doing better than predicted",
                Value = -0.4f * Mathf.Clamp(s.Momentum, -0.25f, 0.25f),
            });
            float streakTerm = s.Streak < 0 ? 0.06f * Mathf.Min(-s.Streak, 3) : -0.06f * Mathf.Min(s.Streak, 3);
            _terms.Add(new AggressionTerm
            {
                Label = s.Streak < 0 ? $"{-s.Streak} wrong in a row" : s.Streak > 0 ? $"{s.Streak} right in a row" : "no streak",
                Value = streakTerm,
            });
            BeastAggression = Mathf.Clamp01(BaseAggression + _terms.Sum(t => t.Value));

            if (s.Streak <= -2) AggressionReason = "It smells blood: mistakes in a row";
            else if (s.Streak >= 2) AggressionReason = "You keep getting it right: it keeps its distance";
            else if (s.Momentum < -0.1f) AggressionReason = "You did worse than it expected: it grows bolder";
            else if (s.Momentum > 0.1f) AggressionReason = "You did better than it expected: it backs off";
            else if (mastery < 0.45f) AggressionReason = $"It has learned you are weak at {what}";
            else if (s.TotalAnswers == 0) AggressionReason = "It is still watching, learning nothing yet";
            else AggressionReason = "It is sizing you up";
        }

        public static string Pretty(string concept) =>
            string.IsNullOrEmpty(concept) ? "-" : concept.Replace('-', ' ').Replace('_', ' ');
    }
}
