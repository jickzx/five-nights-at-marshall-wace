using Fnmw.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fnmw.Game
{
    /// <summary>Phase machine: Title -> Explore -> Puzzle -> Reveal -> Quiz -> ... -> Escaped / Dead.</summary>
    public class GameDirector : MonoBehaviour
    {
        public static GameDirector Instance { get; private set; }
        float _puzzleShownAt;
        LangPuzzle _puzzle;

        GameState S => Services.State;

        void Awake() => Instance = this;

        void Start()
        {
            S.Seed = Random.Range(1, int.MaxValue);
            Services.Difficulty.Reset(S.Seed);
            S.World = Services.Language.Generate(S.Seed);
            S.TotalPuzzles = S.World.Puzzles.Count;
            GameEvents.ConsoleActivated += OnConsole;
            GameEvents.PlayerCaught += OnCaught;
            Services.UI.ShowTitle(BeginExplore);
        }

        void OnDestroy()
        {
            GameEvents.ConsoleActivated -= OnConsole;
            GameEvents.PlayerCaught -= OnCaught;
        }

        public void BeginExplore()
        {
            Services.UI.HideAll();
            S.SetPhase(GamePhase.Explore);
            Services.Audio.PlayStinger("begin");
        }

        void Update()
        {
            if (S.Phase == GamePhase.Title || S.IsOver) return;
            S.Elapsed += Time.deltaTime;
            float threat = Mathf.Max(Mathf.Clamp01(1f - S.BeastDistance / 30f), (S.BeastStage - 1) * 0.3f);
            S.SetThreat(threat);
            Services.Audio.SetIntensity(threat);
            if (S.BeastStage == 3 && S.Phase == GamePhase.Explore) S.SetPhase(GamePhase.Chase);
            Services.UI.ShowHud(S);
        }

        GamePhase RoamPhase => S.BeastStage == 3 ? GamePhase.Chase : GamePhase.Explore;

        void OnConsole(int index)
        {
            if (S.Phase != GamePhase.Explore && S.Phase != GamePhase.Chase) return;
            if (S.PuzzlesSolved >= S.TotalPuzzles) return;
            S.CurrentPuzzleIndex = S.PuzzlesSolved;
            _puzzle = Services.Language.GetPuzzle(S.World, S.CurrentPuzzleIndex, Services.Difficulty.Difficulty);
            S.SetPhase(GamePhase.Puzzle);
            Services.Audio.PlayStinger("console_on");
            GameEvents.RaisePuzzleStarted(_puzzle);
            _puzzleShownAt = Time.time;
            Services.UI.ShowPuzzle(_puzzle, Submit);
        }

        public void Submit(string[] tileIds)
        {
            if (S.Phase != GamePhase.Puzzle) return;
            var result = Services.Language.Check(S.World, _puzzle.Id, tileIds);
            var ev = new AnswerEvent
            {
                PuzzleId = _puzzle.Id, Correct = result.Correct, ResponseTime = Time.time - _puzzleShownAt,
                MissedConcept = result.MissedConcept, Difficulty = _puzzle.Difficulty,
            };
            Services.Difficulty.Observe(ev);
            GameEvents.RaiseAnswerSubmitted(ev);
            if (!result.Correct)
            {
                S.Mistakes++;
                Services.Audio.PlayStinger("wrong");
                Services.UI.ShowAnswerFeedback(result);
                return;
            }
            Services.Audio.PlayStinger("correct");
            S.SetPhase(GamePhase.Reveal);
            Services.UI.ShowReveal(Services.Language.GetReveal(S.World, _puzzle.Id), ContinueReveal);
        }

        public void ContinueReveal()
        {
            if (S.Phase != GamePhase.Reveal) return;
            S.SetPhase(GamePhase.Quiz);
            Services.UI.ShowQuiz(Services.Language.GetQuiz(S.World, _puzzle.Id), Choose);
        }

        public void Choose(int option)
        {
            if (S.Phase != GamePhase.Quiz) return;
            bool ok = Services.Language.CheckQuiz(S.World, _puzzle.Id, option);
            GameEvents.RaiseQuizAnswered(new QuizEvent { PuzzleId = _puzzle.Id, ChosenIndex = option, Correct = ok, RealLanguage = S.World.Real?.Name });
            S.PuzzlesSolved++;
            Services.UI.HideAll();
            if (S.PuzzlesSolved >= S.TotalPuzzles)
            {
                S.SetPhase(GamePhase.Escaped);
                Services.Audio.PlayStinger("liftoff");
                GameEvents.RaiseEscaped();
                Services.UI.ShowEnd(true, S, Restart);
            }
            else S.SetPhase(RoamPhase);
        }

        void OnCaught()
        {
            if (S.IsOver) return;
            S.SetPhase(GamePhase.Dead);
            Services.Audio.PlayStinger("death");
            Services.UI.HideAll();
            Services.UI.ShowEnd(false, S, Restart);
        }

        public void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
