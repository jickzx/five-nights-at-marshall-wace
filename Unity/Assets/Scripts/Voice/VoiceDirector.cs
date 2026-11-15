using System.Text.RegularExpressions;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fnmw.Voice
{
    /// <summary>
    /// The voice module's only MonoBehaviour: feeds game events to <see cref="VoiceCore"/>, ticks it on unscaled
    /// time, and aborts everything when the scene unloads (restart) or the run ends. F2 toggles a one-line status
    /// that says whether the last line of each feature was live, cached, mock or offline.
    /// </summary>
    public sealed class VoiceDirector : MonoBehaviour, IVoiceDirector
    {
        internal VoiceCore Core { get; private set; }
        bool _debug;
        GUIStyle _style;

        public static VoiceDirector Create()
        {
            var go = new GameObject("VoiceDirector");
            var d = go.AddComponent<VoiceDirector>();
            d.Init(VoiceConfig.Load());
            return d;
        }

        internal void Init(VoiceConfig cfg, IVoiceTransport transport = null)
        {
            string build = Regex.Replace("unity-" + Application.version, "[^A-Za-z0-9._-]", "");
            if (build.Length > 32) build = build.Substring(0, 32);
            transport ??= cfg != null && cfg.Online ? new UnityTransport(cfg.BaseUrl) : null;
            Core = new VoiceCore(cfg, transport, () => Time.unscaledTime, build);
            GameEvents.PhaseChanged += OnPhase;
            GameEvents.PuzzleStarted += OnPuzzle;
            GameEvents.AnswerSubmitted += OnAnswer;
            GameEvents.BeastStageChanged += OnStage;
            if (cfg != null && cfg.Online) Debug.Log("[Voice] backend configured; voice features start " + (cfg.Enabled ? "on" : "off"));
        }

        void OnPhase(GamePhase from, GamePhase to) => Core?.OnPhase(from, to);
        void OnPuzzle(LangPuzzle p) => Core?.OnPuzzle(p);
        void OnAnswer(AnswerEvent e) => Core?.OnAnswer(e);
        void OnStage(int s) => Core?.OnStage(s);

        public bool HintsAvailable => Core != null && Core.HintsAvailable;
        public void RequestHint(int puzzleId) => Core?.RequestHint(puzzleId);
        public void NotifyQuizAnswerLocked(int puzzleId) => Core?.NotifyQuizAnswerLocked(puzzleId);
        public void RequestPronunciation(int puzzleId, bool replay) => Core?.RequestPronunciation(puzzleId, replay);

        void Update()
        {
            Core?.Tick();
            var kb = Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame) _debug = !_debug;
        }

        void OnGUI()
        {
            if (!_debug || Core == null) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(11, Screen.height / 50) };
            GUI.Label(new Rect(10, Screen.height - 28, Screen.width - 20, 24), Core.DebugLine(), _style);
        }

        void OnDestroy()
        {
            GameEvents.PhaseChanged -= OnPhase;
            GameEvents.PuzzleStarted -= OnPuzzle;
            GameEvents.AnswerSubmitted -= OnAnswer;
            GameEvents.BeastStageChanged -= OnStage;
            Core?.Dispose();   // in-flight requests are aborted: nothing from this run plays in the next
        }
    }
}
