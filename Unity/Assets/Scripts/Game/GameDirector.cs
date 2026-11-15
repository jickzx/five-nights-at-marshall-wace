using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fnmw.Game
{
    /// <summary>
    /// The phase machine: Title -> Explore -> (Puzzle -> Reveal -> Quiz) per console -> Chase -> Escaped,
    /// or Dead whenever the beast catches you. Consoles are decoded in path order; each solved console adds
    /// ship power. When the ship is fully powered the final run begins and boarding plays the escape ending.
    /// Works with any mix of real and fake systems: every service call is guarded.
    /// </summary>
    public class GameDirector : MonoBehaviour
    {
        public static GameDirector Instance { get; private set; }

        public IReadOnlyList<Interactable> Route => _route;
        public Interactable Ship => _ship;
        public Interactable CurrentConsole => S.PuzzlesSolved < _route.Count ? _route[S.PuzzlesSolved] : null;
        public LangPuzzle CurrentPuzzle => _puzzle;
        public bool CutscenePlaying { get; private set; }
        /// <summary>The tutorial's worked example is on the console (phase Puzzle, no real puzzle yet).</summary>
        public bool ExampleOpen { get; private set; }

        readonly List<Interactable> _route = new List<Interactable>();
        Interactable _ship;
        LangPuzzle _puzzle;
        float _puzzleShownAt;
        bool _revealPending;   // a right answer is in; the reveal follows in a moment
        bool _forceDeath;
        PlayerController _player;

        GameState S => Services.State;

        void Awake() => Instance = this;

        void Start()
        {
            _player = FindAnyObjectByType<PlayerController>();
            S.Seed = DemoMode.On ? DemoMode.Seed : UnityEngine.Random.Range(1, int.MaxValue);
            Guard(() => Services.Difficulty.Reset(S.Seed), "Difficulty.Reset");
            S.World = GenerateWorld(S.Seed);

            var consoles = Interactable.All.Where(i => i && i.Type == Interactable.Kind.Console).OrderBy(i => i.Index).ToList();
            _ship = Interactable.All.FirstOrDefault(i => i && i.Type == Interactable.Kind.Ship);
            int puzzles = S.World.Puzzles.Count;
            if (consoles.Count == 0)
            {
                Debug.LogWarning("[GameDirector] no consoles in the world; spawning one in front of the player");
                consoles.Add(SpawnFallbackConsole());
            }
            _route.AddRange(PickRoute(consoles, puzzles));
            S.TotalPuzzles = _route.Count;
            foreach (var c in consoles) c.Enabled = false;
            if (_ship) _ship.Enabled = false;

            GameEvents.ConsoleActivated += OnConsole;
            GameEvents.PlayerCaught += OnCaught;
            Services.UI.ShowTitle(BeginExplore);
        }

        void OnDestroy()
        {
            GameEvents.ConsoleActivated -= OnConsole;
            GameEvents.PlayerCaught -= OnCaught;
            if (Instance == this) Instance = null;
        }

        /// <summary>Evenly spaced consoles along the path, as many as there are puzzles (both ends kept).</summary>
        public static List<T> PickRoute<T>(IList<T> consoles, int puzzles)
        {
            int n = Mathf.Clamp(puzzles, 0, consoles.Count);
            var picked = new List<T>();
            if (n == 0) return picked;
            if (n == 1) { picked.Add(consoles[0]); return picked; }
            for (int k = 0; k < n; k++)
                picked.Add(consoles[Mathf.RoundToInt(k * (consoles.Count - 1) / (float)(n - 1))]);
            return picked;
        }

        LanguageWorld GenerateWorld(int seed)
        {
            LanguageWorld w = null;
            try { w = Services.Language.Generate(seed); }
            catch (Exception ex) { Debug.LogException(ex); }
            if (w == null || w.Puzzles == null || w.Puzzles.Count == 0)
            {
                Debug.LogWarning("[GameDirector] language engine gave no puzzles; falling back to the fake engine");
                Services.Language = new Core.Fakes.FakeLanguageEngine();
                w = Services.Language.Generate(seed);
            }
            return w;
        }

        Interactable SpawnFallbackConsole()
        {
            var go = ModelLoader.Load("console");
            var p = _player ? _player.transform : null;
            go.transform.position = p ? p.position + p.forward * 6f : new Vector3(0, 0, 6);
            var it = go.AddComponent<Interactable>();
            it.Type = Interactable.Kind.Console; it.Index = 0;
            return it;
        }

        // ------------------------------------------------------------------ phases

        public void BeginExplore()
        {
            if (S.Phase != GamePhase.Title) return;
            Services.UI.HideAll();
            S.SetPhase(GamePhase.Explore);
            Stinger("begin");
            WakeNextConsole();
            if (TutorialCoach.Instance) TutorialCoach.Instance.BeginIfWanted();
        }

        void WakeNextConsole()
        {
            var c = CurrentConsole;
            if (c) c.Enabled = true;
        }

        void Update()
        {
            if (S.Phase == GamePhase.Title || S.IsOver) return;
            S.Elapsed += Time.deltaTime;
            float threat = Mathf.Max(Mathf.Clamp01(1f - S.BeastDistance / 30f), (S.BeastStage - 1) * 0.3f);
            if (S.Phase == GamePhase.Chase) threat = Mathf.Max(threat, 0.75f);
            S.SetThreat(threat);
            Guard(() => Services.Audio.SetIntensity(threat), "Audio.SetIntensity");
            Guard(() => Services.UI.ShowHud(S), "UI.ShowHud");

            // The final run: reaching the ramp is enough, no need to fumble for E with the beast behind you.
            if (S.Phase == GamePhase.Chase && _ship && Services.Player)
            {
                var a = _ship.transform.position; a.y = 0; var b = Services.Player.position; b.y = 0;
                if (Vector3.Distance(a, b) < 3.0f) BoardShip();
            }
        }

        GamePhase RoamPhase => S.PuzzlesSolved >= S.TotalPuzzles ? GamePhase.Chase : GamePhase.Explore;

        void OnConsole(int index)
        {
            if (S.Phase != GamePhase.Explore) return;
            var c = CurrentConsole;
            if (!c || c.Index != index) return;
            OpenPuzzle();
        }

        void OpenPuzzle()
        {
            // the first console of a tutorial run starts with a solved example, then hands over the real puzzle
            var example = TutorialCoach.Instance ? TutorialCoach.Instance.TakeExample() : null;
            if (example != null && !ExampleOpen)
            {
                ExampleOpen = true;
                S.SetPhase(GamePhase.Puzzle);
                Stinger("console_on");
                Services.UI.ShowPrompt(null);
                Guard(() => Services.UI.ShowWorkedExample(example, EndWorkedExample), "UI.ShowWorkedExample");
                return;
            }
            S.CurrentPuzzleIndex = Mathf.Min(S.PuzzlesSolved, S.World.Puzzles.Count - 1);
            float difficulty = 0.3f;
            Guard(() => difficulty = Services.Difficulty.Difficulty, "Difficulty");
            try { _puzzle = Services.Language.GetPuzzle(S.World, S.CurrentPuzzleIndex, difficulty); }
            catch (Exception ex) { Debug.LogException(ex); _puzzle = S.World.Puzzles[S.CurrentPuzzleIndex]; }
            if (_puzzle == null) _puzzle = S.World.Puzzles[S.CurrentPuzzleIndex];
            S.SetPhase(GamePhase.Puzzle);
            Stinger("console_on");
            GameEvents.RaisePuzzleStarted(_puzzle);
            _puzzleShownAt = Time.time;
            Services.UI.ShowPrompt(null);
            Services.UI.ShowPuzzle(_puzzle, Submit);
        }

        /// <summary>The worked example is finished or skipped: put the real first puzzle on the console.</summary>
        public void EndWorkedExample()
        {
            if (!ExampleOpen) return;
            ExampleOpen = false;
            if (TutorialCoach.Instance) TutorialCoach.Instance.ExampleDone = true;
            if (S.Phase == GamePhase.Puzzle) OpenPuzzle();
        }

        /// <summary>Step away from the console (Esc/Tab). It stays lit; come back to it.</summary>
        public void AbandonPuzzle()
        {
            if (S.Phase != GamePhase.Puzzle || _revealPending) return;   // solved: the reveal is coming, and it lights the next console
            ExampleOpen = false;   // an unfinished example plays again next time
            Services.UI.HideAll();
            S.SetPhase(GamePhase.Explore);
        }

        public void Submit(string[] tileIds)
        {
            // The console stays up for a beat after a right answer: a second Enter (or a click on SEND) in that
            // beat used to solve the next console too, skipping it and starting the final run early.
            if (S.Phase != GamePhase.Puzzle || _puzzle == null || ExampleOpen || _revealPending) return;
            AnswerResult result;
            try { result = Services.Language.Check(S.World, _puzzle.Id, tileIds); }
            catch (Exception ex) { Debug.LogException(ex); result = new AnswerResult { Correct = false, MissedConcept = "unknown", Feedback = "The console sputters." }; }
            result ??= new AnswerResult { Correct = false, Feedback = "The console sputters." };
            var ev = new AnswerEvent
            {
                PuzzleId = _puzzle.Id, Correct = result.Correct, ResponseTime = Time.time - _puzzleShownAt,
                MissedConcept = result.Correct ? null : (result.MissedConcept ?? "unknown"), Difficulty = _puzzle.Difficulty,
            };
            Guard(() => Services.Difficulty.Observe(ev), "Difficulty.Observe");
            GameEvents.RaiseAnswerSubmitted(ev);
            Guard(() => Services.UI.ShowAnswerFeedback(result), "UI.ShowAnswerFeedback");
            if (!result.Correct)
            {
                S.Mistakes++;
                Stinger("wrong");
                return;
            }
            Stinger("correct");
            _revealPending = true;
            SolveCurrentConsole();
            StartCoroutine(After(1.1f, ShowReveal));
        }

        void SolveCurrentConsole()
        {
            var c = CurrentConsole;
            if (c) c.Enabled = false;
            S.PuzzlesSolved = Mathf.Min(S.PuzzlesSolved + 1, S.TotalPuzzles);
            Stinger("ship_power");
        }

        void ShowReveal()
        {
            _revealPending = false;
            if (S.Phase != GamePhase.Puzzle) return;
            S.SetPhase(GamePhase.Reveal);
            RevealInfo r = null;
            try { r = Services.Language.GetReveal(S.World, _puzzle.Id); } catch (Exception ex) { Debug.LogException(ex); }
            if (r == null) { ContinueAfterQuiz(); return; }
            Services.UI.ShowReveal(r, ContinueReveal);
        }

        public void ContinueReveal()
        {
            if (S.Phase != GamePhase.Reveal) return;
            QuizQuestion q = null;
            try { q = Services.Language.GetQuiz(S.World, _puzzle.Id); } catch (Exception ex) { Debug.LogException(ex); }
            if (q == null || q.Options == null || q.Options.Length == 0) { ContinueAfterQuiz(); return; }
            S.SetPhase(GamePhase.Quiz);
            Services.UI.ShowQuiz(q, Choose);
        }

        public void Choose(int option)
        {
            if (S.Phase != GamePhase.Quiz) return;
            bool ok = false;
            try { ok = Services.Language.CheckQuiz(S.World, _puzzle.Id, option); } catch (Exception ex) { Debug.LogException(ex); }
            GameEvents.RaiseQuizAnswered(new QuizEvent { PuzzleId = _puzzle.Id, ChosenIndex = option, Correct = ok, RealLanguage = S.World.Real?.Name });
            Stinger(ok ? "correct" : "wrong");
            ContinueAfterQuiz();
        }

        void ContinueAfterQuiz()
        {
            Services.UI.HideAll();
            if (S.PuzzlesSolved >= S.TotalPuzzles) BeginFinalChase();
            else { S.SetPhase(GamePhase.Explore); WakeNextConsole(); }
        }

        /// <summary>The ship is powered: run for it.</summary>
        public void BeginFinalChase()
        {
            if (S.IsOver) return;
            S.SetPhase(GamePhase.Chase);
            if (_ship) _ship.Enabled = true;
            Stinger("beast_roar");
            GiveHeadStart();
        }

        /// <summary>Metres between you and the beast when the final run begins. It hunts at stage 3
        /// (~2.6-4.6 m/s); you walk at 3.4 and run at 5.6, so running gets you out and dawdling does not.</summary>
        public const float FinalRunHeadStart = 22f;

        /// <summary>The ship is powered and the beast becomes the hunter: start it behind you, away from the ship,
        /// not wherever it happened to be lurking (it could be between you and the ramp, or a lunge away).</summary>
        void GiveHeadStart()
        {
            var b = FindBeast(); var p = Services.Player;
            if (!b || !p) return;
            Vector3 away = _ship ? p.position - _ship.transform.position : -p.forward;
            away.y = 0;
            if (away.sqrMagnitude < 0.01f) away = -p.forward;
            away.Normalize();
            var flat = b.position - p.position; flat.y = 0;
            if (flat.magnitude >= FinalRunHeadStart && Vector3.Dot(flat.normalized, away) > 0.5f) return;   // already well behind
            var pos = p.position + away * FinalRunHeadStart;
            b.position = new Vector3(pos.x, p.position.y, pos.z);
            b.rotation = Quaternion.LookRotation(-away);
        }

        public void BoardShip()
        {
            if (S.IsOver || CutscenePlaying) return;
            if (S.Phase != GamePhase.Chase && S.Phase != GamePhase.Explore) return;
            if (S.PuzzlesSolved < S.TotalPuzzles) return;
            S.SetPhase(GamePhase.Escaped);
            Stinger("liftoff");
            GameEvents.RaiseEscaped();
            Services.UI.ShowPrompt(null);
            Services.UI.HideAll();
            CutscenePlaying = true;
            gameObject.AddComponent<EscapeCutscene>().Play(_ship ? _ship.transform.parent : null, () =>
            {
                CutscenePlaying = false;
                Services.UI.ShowEnd(true, S, Restart);
            });
        }

        void OnCaught()
        {
            if (S.IsOver || CutscenePlaying) return;
            if (DemoMode.On && !_forceDeath)
            {
                Debug.Log("[Demo] the beast caught you, but demo mode forgives it");
                return;
            }
            _forceDeath = false;
            S.SetPhase(GamePhase.Dead);
            Stinger("death");
            Services.UI.ShowPrompt(null);
            Services.UI.HideAll();
            CutscenePlaying = true;
            gameObject.AddComponent<DeathCutscene>().Play(() =>
            {
                CutscenePlaying = false;
                Services.UI.ShowEnd(false, S, Restart);
            });
        }

        public void Restart()
        {
            int idx = SceneManager.GetActiveScene().buildIndex;
            SceneManager.LoadScene(idx >= 0 ? idx : 0);
        }

        // ------------------------------------------------------------------ skip keys

        /// <summary>Jump straight to a phase (dev and demo keys, and the recording scripts).</summary>
        public void SkipTo(GamePhase phase)
        {
            if (CutscenePlaying && phase != GamePhase.Title) return;
            if (phase != GamePhase.Title && phase != GamePhase.Explore && TutorialCoach.Instance) TutorialCoach.Instance.Abort();
            ExampleOpen = false;
            switch (phase)
            {
                case GamePhase.Title: Restart(); break;
                case GamePhase.Explore:
                    if (S.Phase == GamePhase.Title) BeginExplore();
                    else if (!S.IsOver) { Services.UI.HideAll(); S.SetPhase(RoamPhase); }
                    break;
                case GamePhase.Puzzle:
                    SkipTo(GamePhase.Explore);
                    if (S.Phase != GamePhase.Explore || !CurrentConsole) break;
                    WarpToConsole(CurrentConsole);
                    OpenPuzzle();
                    break;
                case GamePhase.Reveal:
                    if (S.Phase != GamePhase.Puzzle) SkipTo(GamePhase.Puzzle);
                    if (S.Phase != GamePhase.Puzzle) break;
                    SolveCurrentConsole();
                    ShowReveal();
                    break;
                case GamePhase.Quiz:
                    if (S.Phase != GamePhase.Reveal) SkipTo(GamePhase.Reveal);
                    ContinueReveal();
                    break;
                case GamePhase.Chase:
                    SkipTo(GamePhase.Explore);
                    if (S.IsOver) break;
                    Services.UI.HideAll();
                    while (S.PuzzlesSolved < S.TotalPuzzles) { var c = CurrentConsole; if (c) c.Enabled = false; S.PuzzlesSolved++; }
                    MarkConsolesSolved();
                    if (_player && _route.Count > 0) WarpNear(_route[_route.Count - 1].transform.position, 4f);
                    BeginFinalChase();
                    PlaceBeast(behind: true, 14f);   // the skip key keeps its closer, scarier start
                    break;
                case GamePhase.Escaped:
                    if (S.Phase != GamePhase.Chase) SkipTo(GamePhase.Chase);
                    if (_ship && _player) WarpNear(_ship.transform.position, 2.5f);
                    BoardShip();
                    break;
                case GamePhase.Dead:
                    if (S.Phase == GamePhase.Title) BeginExplore();
                    Services.UI.HideAll();
                    if (S.Phase != GamePhase.Explore && S.Phase != GamePhase.Chase) S.SetPhase(RoamPhase);
                    PlaceBeast(behind: false, 2.2f);
                    _forceDeath = true;
                    GameEvents.RaisePlayerCaught();
                    break;
            }
        }

        void MarkConsolesSolved()
        {
            foreach (var c in _route) c.SendMessage("ForceSolved", SendMessageOptions.DontRequireReceiver);
        }

        void WarpToConsole(Interactable c)
        {
            if (!_player) return;
            WarpNear(c.transform.position, 1.6f);
        }

        void WarpNear(Vector3 target, float dist)
        {
            if (!_player) return;
            var from = target + Vector3.left * dist;
            from.y = target.y + 0.3f;
            _player.Warp(from);
            _player.Face(target);
        }

        /// <summary>Put the beast in front of (or behind) the player. The beast belongs to the Monster thread; found by name.</summary>
        public static Transform FindBeast()
        {
            var b = GameObject.Find("Beast");
            if (b) return b.transform;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name.IndexOf("beast", StringComparison.OrdinalIgnoreCase) >= 0) return root.transform;
            return null;
        }

        void PlaceBeast(bool behind, float dist)
        {
            var b = FindBeast();
            var p = Services.Player;
            if (!b || !p) return;
            var dir = behind ? -p.forward : p.forward; dir.y = 0; dir.Normalize();
            var pos = p.position + dir * dist;
            b.position = new Vector3(pos.x, p.position.y, pos.z);
            b.rotation = Quaternion.LookRotation(-dir);
        }

        // ------------------------------------------------------------------ helpers

        static void Stinger(string name)
        {
            try { Services.Audio.PlayStinger(name); } catch (Exception ex) { Debug.LogException(ex); }
        }

        static void Guard(Action a, string what)
        {
            try { a(); } catch (Exception ex) { Debug.LogError($"[GameDirector] {what} threw: {ex}"); }
        }

        static IEnumerator After(float s, Action a)
        {
            yield return new WaitForSeconds(s);
            a();
        }
    }
}
