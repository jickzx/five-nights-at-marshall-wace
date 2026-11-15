using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Game
{
    /// <summary>
    /// The first-run tutorial as a pure state machine: which cue is live, given what the player has done.
    /// It teaches in context and only advances on the player's own actions (or a timeout, so nobody is stuck):
    ///
    ///   Move -> Objective -> (walk) -> Console -> PuzzleRead -> PuzzleTiles -> PuzzleSend (-> Misfire -> PuzzleSend)
    ///   -> Reveal -> Quiz -> Beast -> NextConsole -> done
    ///
    /// The first console is the guided one. Until it is solved the beast is held back (Grace = 1).
    /// "Taught" turns true once that console is solved or the tutorial is skipped; the coach then marks the
    /// session so a restart does not show it again.
    /// </summary>
    public sealed class TutorialFlow
    {
        public const float SkipHoldSeconds = 0.8f;
        public const float MoveMetres = 3f, LookDegrees = 30f, MoveTimeout = 20f;
        /// <summary>VR only: degrees of right-stick turning the Move cue waits for (one 45 degree snap is enough).</summary>
        public const float TurnDegrees = 40f, MoveTimeoutXr = 35f;
        public const float ObjectiveMin = 4f, ObjectiveMax = 12f, ObjectiveNear = 7f;
        public const float ReadSeconds = 6f, MisfireSeconds = 4f, BeastSeconds = 7f, NextSeconds = 7f, NextNear = 8f;

        public struct Inputs
        {
            public GamePhase Phase;
            public float Moved;          // metres walked since the run began
            public float Looked;         // degrees turned since the run began
            public float ConsoleDistance;// metres to the console to decode next (infinity if none)
            public bool AtConsole;       // in range of it (the E prompt is showing)
            public int Solved, Total;
            public bool SkipHeld;
            public bool ExampleOpen;     // the worked example is on the console
            public bool NeedTurn;        // VR: the Move cue also wants the right stick turned (Turned degrees)
            public float Turned;         // degrees turned with the right stick since the run began
        }

        public bool Active { get; private set; }
        public bool Taught { get; private set; }
        public bool Skipped { get; private set; }
        public TutorialCue Cue { get; private set; }
        public float CueTime { get; private set; }
        public float SkipHold01 => Mathf.Clamp01(_hold / SkipHoldSeconds);

        /// <summary>1 while the guided first console is still ahead: the beast neither evolves nor presses.</summary>
        public float Grace => Active && !Taught ? 1f : 0f;

        float _hold;
        bool _tilePicked, _misfired, _beastShown, _objectiveShown;

        public void Begin()
        {
            Active = true; Taught = false; Skipped = false;
            _hold = 0; _tilePicked = false; _misfired = false; _beastShown = false; _objectiveShown = false;
            Set(TutorialCue.Move);
        }

        /// <summary>Hold-to-skip finished, or the player asked to skip.</summary>
        public void Skip()
        {
            if (!Active) return;
            Skipped = true; Taught = true;
            End();
        }

        /// <summary>A dev or skip key jumped the run around: stop teaching this run, without marking it seen.</summary>
        public void Abort() { if (Active) End(); }

        public void OnTilePicked() => _tilePicked = true;
        public void OnAnswer(bool correct) { if (!correct) _misfired = true; }

        public void Tick(float dt, Inputs i)
        {
            if (!Active) return;
            CueTime += dt;

            _hold = i.SkipHeld ? _hold + dt : 0f;
            if (_hold >= SkipHoldSeconds) { Skip(); return; }

            if (i.Phase == GamePhase.Dead || i.Phase == GamePhase.Escaped || i.Phase == GamePhase.Chase) { if (i.Solved > 0) Taught = true; End(); return; }
            if (i.Solved > 0) Taught = true;

            switch (i.Phase)
            {
                case GamePhase.Puzzle:
                    if (i.ExampleOpen && !Taught) { if (Cue != TutorialCue.Example) Set(TutorialCue.Example); }
                    else TickPuzzle();
                    break;
                case GamePhase.Reveal: if (Cue != TutorialCue.Reveal) Set(TutorialCue.Reveal); break;
                case GamePhase.Quiz: if (Cue != TutorialCue.Quiz) Set(TutorialCue.Quiz); break;
                case GamePhase.Explore: TickExplore(i); break;
            }
            _tilePicked = false; _misfired = false;
        }

        void TickPuzzle()
        {
            if (Taught) return;   // only the first console is guided
            switch (Cue)
            {
                case TutorialCue.PuzzleRead:
                    if (_tilePicked) Set(TutorialCue.PuzzleSend);
                    else if (CueTime > ReadSeconds) Set(TutorialCue.PuzzleTiles);
                    break;
                case TutorialCue.PuzzleTiles:
                    if (_tilePicked) Set(TutorialCue.PuzzleSend);
                    break;
                case TutorialCue.PuzzleSend:
                    if (_misfired) Set(TutorialCue.Misfire);
                    break;
                case TutorialCue.Misfire:
                    if (_misfired) Set(TutorialCue.Misfire);
                    else if (CueTime > MisfireSeconds) Set(TutorialCue.PuzzleSend);
                    break;
                default:
                    // came back to the console after stepping away: no need to read the rows again
                    Set(_tilePicked ? TutorialCue.PuzzleSend : TutorialCue.PuzzleRead);
                    break;
            }
            if (_misfired && Cue != TutorialCue.Misfire) Set(TutorialCue.Misfire);
        }

        void TickExplore(Inputs i)
        {
            if (!Taught)
            {
                switch (Cue)
                {
                    case TutorialCue.Move:
                        if (MovePractised(i) || CueTime > (i.NeedTurn ? MoveTimeoutXr : MoveTimeout) || i.AtConsole) { _objectiveShown = true; Set(TutorialCue.Objective); }
                        break;
                    case TutorialCue.Objective:
                        if (i.AtConsole) Set(TutorialCue.Console);
                        else if ((CueTime > ObjectiveMin && i.ConsoleDistance < ObjectiveNear) || CueTime > ObjectiveMax) Set(TutorialCue.None);
                        break;
                    case TutorialCue.Console:
                        if (!i.AtConsole) Set(TutorialCue.None);
                        break;
                    default:
                        // walking to the console, or stepped away from the puzzle
                        if (i.AtConsole) Set(TutorialCue.Console);
                        else if (!_objectiveShown) { _objectiveShown = true; Set(TutorialCue.Objective); }
                        else if (Cue != TutorialCue.None) Set(TutorialCue.None);
                        break;
                }
                return;
            }

            // first console solved: the beast, then what is left
            if (!_beastShown) { _beastShown = true; Set(TutorialCue.Beast); return; }
            if (Cue == TutorialCue.Beast)
            {
                if (CueTime > BeastSeconds) Set(TutorialCue.NextConsole);
                return;
            }
            if (Cue == TutorialCue.NextConsole && (CueTime > NextSeconds || i.ConsoleDistance < NextNear || i.Solved >= i.Total)) { End(); return; }
            if (Cue != TutorialCue.NextConsole) End();
        }

        /// <summary>Walked a few metres and looked around; in VR, also turned with the right stick.</summary>
        static bool MovePractised(Inputs i) =>
            i.Moved >= MoveMetres && i.Looked >= LookDegrees && (!i.NeedTurn || i.Turned >= TurnDegrees);

        void Set(TutorialCue c) { Cue = c; CueTime = 0f; }

        void End()
        {
            Active = false;
            Cue = TutorialCue.None;
            CueTime = 0f;
            _hold = 0f;
        }

        public TutorialView View(int solved, int total) => new TutorialView
        {
            Active = Active,          // the skip hint stays up for the whole tutorial; Cue None draws no card
            Cue = Active ? Cue : TutorialCue.None,
            CueTime = CueTime,
            SkipHold = SkipHold01,
            Solved = solved, Total = total,
        };
    }
}
