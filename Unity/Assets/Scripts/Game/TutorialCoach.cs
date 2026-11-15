using Fnmw.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fnmw.Game
{
    /// <summary>
    /// Feeds TutorialFlow from the live game (distance walked, how far the player looked, the console,
    /// the hold-Q skip key, tile and answer events), holds the beast back while the guided console is
    /// ahead (GameState.BeastGrace) and passes the view to the UI every frame.
    /// The run begins it from GameDirector.BeginExplore when TutorialSession wants it.
    /// </summary>
    public class TutorialCoach : MonoBehaviour
    {
        public static TutorialCoach Instance { get; private set; }
        public readonly TutorialFlow Flow = new TutorialFlow();
        public bool Active => Flow.Active;
        /// <summary>The worked example for the first console (null if none could be built).</summary>
        public WorkedExample Example { get; private set; }
        /// <summary>Set by the director once the example has been walked through (or skipped).</summary>
        public bool ExampleDone;

        float _moved, _looked, _lastYaw, _lastPitch, _turn0;
        Vector3 _lastPos;
        bool _tracking;
        PlayerController _player;

        void Awake()
        {
            Instance = this;
            GameEvents.TilePicked += OnTile;
            GameEvents.AnswerSubmitted += OnAnswer;
        }

        void OnDestroy()
        {
            GameEvents.TilePicked -= OnTile;
            GameEvents.AnswerSubmitted -= OnAnswer;
            if (Instance == this) Instance = null;
        }

        void OnTile(Tile t) => Flow.OnTilePicked();
        void OnAnswer(AnswerEvent e) => Flow.OnAnswer(e.Correct);

        /// <summary>Start teaching if this session still wants it (first run, or asked for from the title).</summary>
        public void BeginIfWanted()
        {
            if (!TutorialSession.WantsTutorial) return;
            Flow.Begin();
            _moved = 0; _looked = 0; _tracking = false;
            _turn0 = XrBridge.StickTurnDegrees;
            ExampleDone = false;
            var t0 = Time.realtimeSinceStartup;
            var s = Services.State;
            try { Example = WorkedExampleBuilder.Build(Services.Language, s.World, s.TotalPuzzles); }
            catch (System.Exception ex) { Debug.LogException(ex); Example = null; }
            Debug.Log($"[Tutorial] started; worked example: {(Example == null ? "none" : Example.LanguageName + " '" + Example.Puzzle.TargetMeaning + "'")} ({(Time.realtimeSinceStartup - t0) * 1000f:0} ms)");
        }

        /// <summary>The worked example, once, at the first console of a tutorial run.</summary>
        public WorkedExample TakeExample() => Flow.Active && !Flow.Taught && !ExampleDone ? Example : null;

        /// <summary>Dev and skip keys: stop teaching this run (does not count as seen).</summary>
        public void Abort()
        {
            if (!Flow.Active) return;
            Flow.Abort();
            Services.State.BeastGrace = 0f;
            Debug.Log("[Tutorial] aborted by a skip key");
        }

        void Update()
        {
            var s = Services.State;
            if (s.Phase == GamePhase.Title) return;
            if (!_player) _player = FindAnyObjectByType<PlayerController>();

            if (Flow.Active)
            {
                Track();
                var d = GameDirector.Instance;
                var console = d ? d.CurrentConsole : null;
                float dist = float.PositiveInfinity;
                bool at = false;
                if (console && console.Enabled && Services.Player)
                {
                    var a = console.transform.position; a.y = 0; var b = Services.Player.position; b.y = 0;
                    dist = Vector3.Distance(a, b);
                    at = dist < console.Range;
                }
                var kb = Keyboard.current;
                Flow.Tick(Time.deltaTime, new TutorialFlow.Inputs
                {
                    Phase = s.Phase, Moved = _moved, Looked = _looked, ConsoleDistance = dist, AtConsole = at,
                    Solved = s.PuzzlesSolved, Total = s.TotalPuzzles, SkipHeld = kb != null && kb.qKey.isPressed || XrBridge.SkipHeld,
                    ExampleOpen = d && d.ExampleOpen,
                    // in a headset the body turns with the right stick, not the head: practise it once
                    NeedTurn = XrBridge.Active, Turned = XrBridge.StickTurnDegrees - _turn0,
                    HandsStep = XrBridge.Active, LookingDown = LookingAtHands(), AnyButton = AnyXrButton(),
                });
                if (!Flow.Active && d && d.ExampleOpen) d.EndWorkedExample();   // skipped mid-example: straight to the real puzzle
                if (Flow.Taught && !TutorialSession.Seen || Flow.Taught && TutorialSession.ReplayRequested)
                {
                    TutorialSession.Seen = true;
                    TutorialSession.ReplayRequested = false;
                    Debug.Log(Flow.Skipped ? "[Tutorial] skipped" : "[Tutorial] taught: the first console is solved");
                }
                if (!Flow.Active) Debug.Log("[Tutorial] finished");
            }
            // the guided console holds the beast back; the rest of a tutorial run eases it in (TutorialFlow.GraceFor)
            s.BeastGrace = Flow.GraceFor(s.Phase, s.PuzzlesSolved, s.TotalPuzzles);
            XrBridge.TutorialActive = Flow.Active;
            XrBridge.TutorialCue = Flow.Active ? Flow.Cue : TutorialCue.None;
            if (s.IsOver) return;
            try { Services.UI.ShowTutorial(Flow.View(s.PuzzlesSolved, s.TotalPuzzles)); }
            catch (System.Exception ex) { Debug.LogException(ex); }
        }

        /// <summary>VR: the head is pitched well down, where the hands are held.</summary>
        static bool LookingAtHands()
        {
            var h = XrBridge.Head;
            return XrBridge.Active && h && h.forward.y < -0.5f;   // ~30 degrees or more below the horizon
        }

        /// <summary>VR: any Touch button went down this frame (the bridge holds one frame of mapped input).</summary>
        static bool AnyXrButton() => XrBridge.Active &&
            (XrBridge.Interact || XrBridge.Flashlight || XrBridge.Enter || XrBridge.Click || XrBridge.Back || XrBridge.Leave || XrBridge.Hint);

        void Track()
        {
            var p = Services.Player;
            if (!p) return;
            float yaw = _player ? _player.ViewYaw : p.eulerAngles.y, pitch = _player ? _player.Pitch : 0f;
            if (_tracking)
            {
                var step = p.position - _lastPos; step.y = 0;
                if (step.magnitude < 2f) _moved += step.magnitude;   // ignore warps
                _looked += Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw)) + Mathf.Abs(pitch - _lastPitch);
            }
            _tracking = true;
            _lastPos = p.position; _lastYaw = yaw; _lastPitch = pitch;
        }
    }
}
