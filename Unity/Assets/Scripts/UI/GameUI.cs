using System;
using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fnmw.UI
{
    /// <summary>
    /// The real IGameUI. Everything is built from code under one GameObject: a low-resolution canvas,
    /// the CRT glass that presents it, and one object per screen. The Game thread calls the Show*
    /// methods; this class routes input, keeps the glass in the right mood and calls back.
    /// </summary>
    public sealed class GameUI : MonoBehaviour, IGameUI
    {
        internal UiRig Rig;
        internal CrtFx Fx;
        internal GameState State;
        internal readonly RunJournal Journal = new RunJournal();

        TitleScreen _title;
        HudLayer _hud;
        PuzzleScreen _puzzle;
        DecodeScreen _decode;
        WorkedExampleScreen _example;
        DeathScreen _death;
        EscapeScreen _escape;
        TutorialLayer _tutorial;
        SubtitleLayer _subtitles;
        UiScreen _modal;
        RectTransform _timer;
        Text _timerText;
        float _lock;
        bool _built, _disposed;
        LangPuzzle _lastPuzzle;
        List<Tile> _lastAnswer;
        float _clock;                 // unscaled seconds the UI has ticked (subtitle timing)
        SubtitleLine _subtitle;
        float _subtitleFrom, _subtitleUntil;
        HintView _hint;
        PronunciationView _pronunciation;
        string _debrief;

        public static GameUI Create()
        {
            var go = new GameObject("GameUI");
            var ui = go.AddComponent<GameUI>();
            ui.Build();
            return ui;
        }

        void Build()
        {
            if (_built) return;
            _built = true;
            Rig = UiRig.Build(transform);
            Fx = new CrtFx(Rig.Mat);
            _hud = new HudLayer(this);
            _puzzle = new PuzzleScreen(this);
            _decode = new DecodeScreen(this);
            _example = new WorkedExampleScreen(this);
            _title = new TitleScreen(this);
            _death = new DeathScreen(this);
            _escape = new EscapeScreen(this);
            _timer = Kit.Node("RunTimer", Rig.Stage, 480, 6, 130, 24);
            Kit.Box("Back", _timer, Pal.Void.A(0.72f), 0, 0, 130, 24);
            _timerText = Kit.Label("Time", _timer, "TIME 00:00", 16, Pal.Bone, TextAnchor.MiddleRight, 6, 4, 118, 16);
            _timer.gameObject.SetActive(false);
            _hud.Build();
            _hud.Root.gameObject.SetActive(false);
            _subtitles = new SubtitleLayer(Rig.Stage);
            _tutorial = new TutorialLayer(this);     // built last: it draws over every screen
            _tutorial.Build();
            GameEvents.BeastStageChanged += OnBeastStage;
            Fx.SetMode(GlassMode.Off, true);
            Fx.Tick(0);
        }

        /// <summary>Releases textures and event hooks. Called on destroy; tests call it directly.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GameEvents.BeastStageChanged -= OnBeastStage;
            _puzzle?.Close(); _escape?.Close(); _title?.Close();
            Fx?.Dispose();
            Rig?.Dispose();
        }

        void OnDestroy() => Dispose();

        void OnBeastStage(int stage)
        {
            if (_disposed) return;
            Fx.Stage = stage;
            _hud.StageChanged(stage);
            // the title and the two end screens are outside the hunt: nothing jolts them
            if (_modal == _title || _modal == _escape || _modal == _death) return;
            Fx.Kick(shake: 0.6f, burst: 0.55f, tear: 1f, red: 0.4f);
        }

        // ---- IGameUI --------------------------------------------------------------------

        public void ShowTitle(Action onStart)
        {
            _debrief = null;   // a new run starts without the last one's account
            Swap(_title);
            _title.Show(onStart);
            Fx.SetMode(GlassMode.Title, true);
        }

        public void ShowHud(GameState state) => State = state;

        public void ShowPrompt(string text) => _hud.SetPrompt(text);

        public void ShowPuzzle(LangPuzzle puzzle, Action<string[]> onSubmit)
        {
            if (puzzle == null) return;
            _lastPuzzle = puzzle; _lastAnswer = null;
            Swap(_puzzle);
            _puzzle.Show(puzzle, onSubmit, Family());
            Fx.Kick(burst: 0.35f, tear: 0.5f);
        }

        public void ShowWorkedExample(WorkedExample example, Action onDone)
        {
            if (example?.Puzzle == null || example.Answer == null || example.Answer.Length == 0) { onDone?.Invoke(); return; }
            Swap(_example);
            // the example is in a language this run never uses: its glyphs take that language's hand, not the console's
            _example.Show(example, GlyphGen.FamilyFor(example.LanguageName), onDone);
            Fx.Kick(burst: 0.35f, tear: 0.5f);
        }

        public void ShowAnswerFeedback(AnswerResult result)
        {
            if (result == null || result.Correct) return;      // a right answer is answered by the reveal
            if (_modal == _puzzle) _puzzle.Reject(result);
            else Fx.Kick(shake: 0.8f, burst: 0.6f, tear: 0.8f, red: 0.5f);
        }

        public void ShowReveal(RevealInfo reveal, Action onContinue)
        {
            Swap(_decode);
            _decode.ShowReveal(reveal, _lastPuzzle, Family(), _lastAnswer, onContinue);
        }

        public void ShowQuiz(QuizQuestion quiz, Action<int> onChoose)
        {
            bool carryOn = _modal == _decode;
            if (!carryOn) Swap(_decode);
            _lock = 0.2f;
            _decode.ShowQuiz(quiz, _lastPuzzle, Family(), onChoose);
        }

        public void ShowEnd(bool escaped, GameState state, Action onRestart)
        {
            State = state ?? State;
            _hud.SetPrompt(null);
            ClearVoiceViews();
            if (escaped) { Swap(_escape); _escape.Show(State, Journal, onRestart, _debrief); }
            else { Swap(_death); _death.Show(onRestart, _debrief); }
        }

        public void ShowDebrief(string text)
        {
            // kept even with no end screen up: the account is usually ready during the ending cutscene, before ShowEnd
            _debrief = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            if (_modal == _escape) _escape.SetDebrief(_debrief);
            else if (_modal == _death) _death.SetDebrief(_debrief);
        }

        /// <summary>The debrief printed on the end screen in front, for tests and automated play; null when there is none.</summary>
        public string DebriefShown => _modal == _escape ? _escape.DebriefText : _modal == _death ? _death.DebriefText : null;

        public void HideAll()
        {
            Swap(null);
            _hud.SetPrompt(null);
            ClearVoiceViews();
        }

        public void ShowTutorial(TutorialView view) => _tutorial.Show(view);

        public void ShowSubtitle(SubtitleLine line)
        {
            _subtitle = line;
            _subtitleFrom = _clock;
            _subtitleUntil = string.IsNullOrWhiteSpace(line.Text) ? _clock : _clock + Mathf.Clamp(line.Seconds, 0.5f, 20f);
        }

        public void ShowHint(HintView hint) => _hint = hint;

        public void ShowPronunciation(PronunciationView view) => _pronunciation = view;

        void ClearVoiceViews()
        {
            _subtitle = default; _subtitleUntil = 0f; _hint = default; _pronunciation = default;
        }

        /// <summary>The subtitle to draw now (tag included), or null.</summary>
        internal string SubtitleText => _clock < _subtitleUntil ? SubtitleLayer.Format(_subtitle) : null;
        internal float SubtitleAge => _clock - _subtitleFrom;
        internal HintView Hint => _hint;
        internal PronunciationView Pronunciation => _pronunciation;

        // ---- internals ------------------------------------------------------------------

        void Swap(UiScreen next)
        {
            if (_modal != null && _modal != next) _modal.Close();
            _modal = next;
            _lock = 0.2f;        // a key that closed one screen must not also act on the next
        }

        ScriptFamily Family()
        {
            var world = (State ?? Services.State)?.World;
            return GlyphGen.FamilyFor(world?.Real?.Code ?? world?.Real?.Name);
        }

        internal void RememberAnswer(List<Tile> tiles) => _lastAnswer = tiles;

        /// <summary>The screen in front, if any, and the console, for layers that point at parts of it.</summary>
        internal UiScreen Front => _modal;
        internal PuzzleScreen Puzzle => _puzzle;
        internal WorkedExampleScreen Example => _example;
        /// <summary>The tutorial may draw over the shore, the console and the decode screen; never over the title or an ending.</summary>
        internal bool TutorialAllowed
        {
            get
            {
                var s = State ?? Services.State;
                if (s == null || s.Phase == GamePhase.Title || s.IsOver) return false;
                return _modal != _title && _modal != _death && _modal != _escape;
            }
        }

        /// <summary>Name of the screen in front, for tests and automated play.</summary>
        public string ScreenName => _modal == null ? "None" : _modal == _title ? "Title" : _modal == _puzzle ? "Puzzle"
            : _modal == _example ? "Example" : _modal == _decode ? _decode.StepName : _modal == _death ? "Death" : "Escape";

        public string PromptShown => _hud.PromptShown;
        public string TutorialCueShown => _tutorial.CueShown;
        public int PuzzlePicks => _puzzle.PickCount;
        /// <summary>The worked example's step ("Barrier" … "Decoded"), for tests and automated play; "None" when it is not up.</summary>
        public string ExampleStep => _modal == _example ? _example.Current.ToString() : "None";
        public int Cracks => Fx.Cracks;
        /// <summary>What the roaming subtitle line shows now (null when hidden), for tests.</summary>
        public string RoamingSubtitleShown => _subtitles.Root.gameObject.activeSelf ? _subtitles.Shown : null;
        /// <summary>The console footer's text now, for tests ("" when the console is not up).</summary>
        public string PuzzleFooterShown => _modal == _puzzle ? _puzzle.FooterShown : "";
        /// <summary>The pronunciation cue on the payoff now ("" when hidden), for tests.</summary>
        public string PronunciationCueShown => _decode.PronunciationCue;
        public int LanguagesLearned => Journal.Entries.Count;
        public Texture UiTexture => Rig.Rt;

        void Update() => Tick(Time.unscaledDeltaTime, Rig.ReadInput(Fx.Curve));

        /// <summary>Advances the whole UI by one frame. Update calls this; tests call it with scripted input.</summary>
        public void Tick(float dt, UiInput input)
        {
            if (_disposed) return;
            _clock += dt;
            Rig.Resize(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
            if (_lock > 0)
            {
                _lock -= dt;
                input = new UiInput { Digit = -1, HasPointer = input.HasPointer, Pointer = input.Pointer };
            }

            var s = State ?? Services.State;
            var phase = s != null ? s.Phase : GamePhase.Title;
            bool roaming = phase == GamePhase.Explore || phase == GamePhase.Chase;

            var modal = _modal;
            var glass = modal != null ? modal.Glass : (phase == GamePhase.Title || s == null || s.IsOver ? GlassMode.Off : GlassMode.Visor);
            if (Fx.Mode != glass) Fx.SetMode(glass);
            bool calmScreen = modal == _title || modal == _escape || modal == _death;
            Fx.Threat = calmScreen || s == null ? 0f : s.Threat;

            // the tutorial's band across the top of the console (and the worked example's) prints where the timer sits
            bool bandUp = modal == _example || (modal == _puzzle && _tutorial.CueShown != "None");
            bool timer = s != null && phase != GamePhase.Title && !s.IsOver && !calmScreen && !bandUp;
            _timer.gameObject.SetActive(timer);
            if (timer) Kit.SetText(_timerText, "TIME " + Lore.Clock(s.Elapsed));

            bool hud = modal == null && roaming;
            if (_hud.Root.gameObject.activeSelf != hud) _hud.Root.gameObject.SetActive(hud);
            if (hud) _hud.Tick(dt, UiInput.None);

            modal?.Tick(dt, input);
            // spoken lines while roaming; on the console they print in its footer instead
            _subtitles.Tick(modal == null && s != null && !s.IsOver && phase != GamePhase.Title ? SubtitleText : null, SubtitleAge);
            bool tutorial = TutorialAllowed;
            if (_tutorial.Root.gameObject.activeSelf != tutorial) { _tutorial.Root.gameObject.SetActive(tutorial); if (!tutorial) _tutorial.Hide(); }
            if (tutorial) _tutorial.Tick(dt, UiInput.None);
            Fx.Tick(dt);
        }
    }
}
