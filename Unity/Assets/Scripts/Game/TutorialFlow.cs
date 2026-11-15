using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Game
{
    /// <summary>
    /// The first-run tutorial as a pure state machine: which cue is live, given what the player has done.
    /// It teaches in context and only advances on the player's own actions (or a timeout, so nobody is stuck):
    ///
    ///   (VR: Hands ->) Move -> Objective -> (walk) -> Console -> PuzzleRead -> PuzzleTiles -> PuzzleSend (-> Misfire -> PuzzleSend)
    ///   -> Reveal -> Quiz -> Beast -> NextConsole -> done
    ///
    /// The first console is the guided one. Until it is solved the beast is held back (Grace = 1). After it, the
    /// rest of a tutorial run eases the beast in rather than letting it loose at once: Grace steps down from
    /// <see cref="RampStart"/> towards 0 console by console, and is 0 for the final run. A skipped tutorial gets no ramp.
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
        /// <summary>VR only: the Hands cue (look down at the controllers) waits this long looking down, and gives up after HandsTimeout.</summary>
        public const float HandsLookSeconds = 1.5f, HandsMin = 2.5f, HandsTimeout = 14f;
        /// <summary>Grace just after the guided console. It falls linearly to 0 at the last console.</summary>
        public const float RampStart = 0.6f;
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
            public bool HandsStep;       // VR: start with the Hands cue (look down at the controllers)
            public bool LookingDown;     // VR: the head is pitched down towards the hands right now
            public bool AnyButton;       // VR: any controller button pressed this frame (the player found them)
        }

        public bool Active { get; private set; }
        public bool Taught { get; private set; }
        public bool Skipped { get; private set; }
        public TutorialCue Cue { get; private set; }
        public float CueTime { get; private set; }
        public float SkipHold01 => Mathf.Clamp01(_hold / SkipHoldSeconds);

        /// <summary>The beast's grace as of the last tick (see <see cref="GraceFor"/>).</summary>
        public float Grace => GraceFor(_phase, _solved, _total);

        /// <summary>
        /// 0..1, how much the beast is held back (GameState.BeastGrace). 1 while the guided first console is still
        /// ahead: it neither evolves nor presses. Then, for the rest of a run that taught the tutorial, it eases in:
        /// RampStart after the first console, falling to 0 by the last one. 0 in the final run, and in a run that
        /// skipped or aborted the tutorial, or never had one.
        /// </summary>
        public float GraceFor(GamePhase phase, int solved, int total)
        {
            if (!_ramp) return 0f;
            if (phase == GamePhase.Chase || phase == GamePhase.Dead || phase == GamePhase.Escaped) return 0f;
            if (Active && !Taught) return 1f;
            if (solved <= 0) return 0f;
            if (total <= 1) return RampStart;
            return Mathf.Clamp01(RampStart * (1f - (solved - 1f) / (total - 1f)));
        }

        float _hold, _lookedDown;
        bool _tilePicked, _misfired, _beastShown, _objectiveShown, _handsShown, _ramp;
        GamePhase _phase;
        int _solved, _total;

        public void Begin()
        {
            Active = true; Taught = false; Skipped = false; _ramp = true;
            _hold = 0; _lookedDown = 0; _tilePicked = false; _misfired = false; _beastShown = false; _objectiveShown = false; _handsShown = false;
            _phase = GamePhase.Explore; _solved = 0; _total = 0;
            Set(TutorialCue.Move);
        }

        /// <summary>Hold-to-skip finished, or the player asked to skip.</summary>
        public void Skip()
        {
            if (!Active) return;
            Skipped = true; Taught = true; _ramp = false;
            End();
        }

        /// <summary>A dev or skip key jumped the run around: stop teaching this run, without marking it seen.</summary>
        public void Abort() { if (Active) { _ramp = false; End(); } }

        public void OnTilePicked() => _tilePicked = true;
        public void OnAnswer(bool correct) { if (!correct) _misfired = true; }

        public void Tick(float dt, Inputs i)
        {
            if (!Active) return;
            _phase = i.Phase; _solved = i.Solved; _total = i.Total;
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
                case GamePhase.Explore: TickExplore(i, dt); break;
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

        void TickExplore(Inputs i, float dt)
        {
            if (!Taught)
            {
                // VR: before anything else, find the controllers in your hands
                if (i.HandsStep && !_handsShown && Cue == TutorialCue.Move && !i.AtConsole) { _handsShown = true; Set(TutorialCue.Hands); }
                switch (Cue)
                {
                    case TutorialCue.Hands:
                        _lookedDown = i.LookingDown ? _lookedDown + dt : 0f;
                        if ((CueTime > HandsMin && (_lookedDown >= HandsLookSeconds || i.AnyButton)) || CueTime > HandsTimeout || i.AtConsole) Set(TutorialCue.Move);
                        break;
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
