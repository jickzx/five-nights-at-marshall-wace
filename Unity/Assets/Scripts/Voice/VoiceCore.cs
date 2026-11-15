using System;
using System.Collections.Generic;
using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Voice
{
    /// <summary>
    /// Every voice decision, without MonoBehaviours: VoiceDirector feeds it events and ticks it; tests drive it
    /// with a fake transport and a fake clock. It talks to the rest of the game only through Services.
    ///
    /// Staleness: each async result is checked against the core's epoch (bumped per scene load, so a restart
    /// invalidates everything) and a per-feature token (bumped when the puzzle, console content or phase moves on).
    /// Anything stale is dropped without playing or showing.
    /// </summary>
    internal sealed class VoiceCore : IVoiceDirector
    {
        // ---------------------------------------------------------------- statics (survive scene reloads)

        internal static int Epoch;

        sealed class Link
        {
            public string Identity, Token, ConfigVersion;
            public float ExpiresAt;
            public bool Mock;
            public Protocol.Features Features;
            public readonly HashSet<string> Off = new HashSet<string>();
        }

        static Link s_link;

        /// <summary>Tests: forget the session and the clip cache.</summary>
        internal static void ResetStatics() { s_link = null; ClipCache.Clear(); }

        // ---------------------------------------------------------------- timeouts (seconds, unscaled)

        public const float SessionTimeout = 4f, MonsterTimeout = 5f, HintTimeout = 3.5f, PronounceTimeout = 4f, SpeechTimeout = 6f;
        public const float SessionRetryDelay = 2f, HintCooldown = 4f, MonsterMaxAge = 8f;
        public const int MaxPronounceAttempts = 2, MaxHintsPerConsole = 2;

        enum LinkState { Offline, Connecting, Waiting, Online, Failed }

        readonly VoiceConfig _cfg;
        readonly IVoiceTransport _net;
        readonly Func<float> _now;
        readonly string _build;
        readonly int _epoch;
        LinkState _state;
        bool _sessionRetried, _resessioned, _disposed;
        float _retryAt;
        int _seq;

        readonly CircuitBreaker _breaker = new CircuitBreaker();
        readonly SpeechArbiter _arb = new SpeechArbiter();
        readonly RunFacts _facts = new RunFacts();
        readonly DialogueBrain _brain = new DialogueBrain();

        sealed class Pending { public int Handle; public float Deadline; public Action<VoiceResponse> Done; public bool Finished; public string Feature; }
        readonly List<Pending> _pending = new List<Pending>();

        /// <summary>Last outcome per feature, for the F2 debug line and tests: live, cache, mock, offline or an error code.</summary>
        public readonly Dictionary<string, string> Last = new Dictionary<string, string> { { "session", "-" }, { "monster", "-" }, { "hint", "-" }, { "pronounce", "-" } };

        // monster
        int _monsterToken;
        int _subtitleHandle;

        // the console in play
        LangPuzzle _puzzle;
        string _puzzleKey;
        string _missed;
        int _attempts;
        int _hintsGiven, _hintToken;
        bool _hintInFlight, _hintShowing;
        float _lastHintAt = float.NegativeInfinity;
        HintView _lastHint;
        ClipCache.Entry _lastHintClip;
        bool? _hintsAvailable;

        // pronunciation
        enum Pron { None, Waiting, Loading, Ready, Failed, Unavailable, Hidden }
        Pron _pron;
        int _pronId, _pronToken, _pronAttempts;
        string _pronText, _pronLang, _pronCacheKey;
        bool _pronWant, _pronPlaying, _quizAnswerLocked;
        VoiceSource _pronSource;

        public VoiceCore(VoiceConfig cfg, IVoiceTransport net, Func<float> now, string build = "unity")
        {
            _cfg = cfg ?? new VoiceConfig();
            _net = _cfg.Online ? net : null;
            _now = now;
            _build = build;
            _epoch = ++Epoch;
            if (_net == null) { _state = LinkState.Offline; Last["session"] = _cfg.Enabled ? "offline" : "disabled"; return; }
            if (s_link != null && s_link.Identity == _cfg.Identity && s_link.ExpiresAt - now() > 60f) { _state = LinkState.Online; Last["session"] = s_link.Mock ? "mock" : "live"; }
            else { s_link = null; Connect(); }
        }

        bool Current => !_disposed && _epoch == Epoch;
        GameState S => Services.State;
        float Now => _now();

        public bool Online => _state == LinkState.Online;
        public bool MockMode => s_link != null && s_link.Mock;
        public string Mode => _state == LinkState.Online ? (MockMode ? "MOCK" : "LIVE") : _state.ToString().ToUpperInvariant();

        bool FeatureOn(string f)
        {
            if (_state != LinkState.Online || s_link == null || !_cfg.Enabled || s_link.Off.Contains(f)) return false;
            var x = s_link.Features;
            return f == "monster" ? x.monster : f == "hint" ? x.hint : f == "hint_speech" ? x.hint_speech : f == "pronounce" && x.pronounce;
        }

        // ---------------------------------------------------------------- transport with deadlines

        void Send(VoiceRequest r, Action<VoiceResponse> done, string feature = "session")
        {
            if (_net == null) { done(new VoiceResponse { Error = "offline" }); return; }
            var p = new Pending { Deadline = Now + r.Timeout, Done = done, Feature = feature };
            p.Handle = _net.Send(r, res =>
            {
                if (p.Finished) return;
                p.Finished = true;
                _pending.Remove(p);
                if (Current) done(res);
            });
            if (!p.Finished) _pending.Add(p);
        }

        /// <summary>An authenticated call. ok gets a 200; fail gets an error code ("timeout", "network", a backend code).</summary>
        void Call(string feature, VoiceRequest r, Action<VoiceResponse> ok, Action<string> fail)
        {
            if (_state != LinkState.Online || s_link == null) { fail("offline"); return; }
            if (!_breaker.Allow(Now)) { fail("breaker"); return; }
            r.Bearer = s_link.Token;
            Send(r, res =>
            {
                if (res.Status == 200) { _breaker.Success(); ok(res); return; }
                string code = res.Status == 0 ? (res.Error ?? "network") : Protocol.ErrorCode(res.Text) ?? ("http_" + res.Status);
                if (!Protocol.Codes.Contains(code) && res.Status != 0) code = "http_" + res.Status;
                if (res.Status == 401)
                {
                    if (!_resessioned) { _resessioned = true; s_link = null; Connect(); }
                    else { _state = LinkState.Offline; s_link = null; Last["session"] = "unauthorized"; }
                }
                else if (code == "budget_exhausted" || code == "disabled")
                {
                    s_link?.Off.Add(feature);   // definitive: this feature stays off for the rest of the session
                    if (feature == "hint") s_link?.Off.Add("hint_speech");
                }
                else if (res.Status == 0 || res.Status == 429 || res.Status >= 500) _breaker.Failure(Now);
                fail(code);
            }, feature);
        }

        /// <summary>Aborts this feature's requests (the player moved on). The backend keeps any audio it was making.</summary>
        void CancelFeature(string feature)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (p.Feature != feature) continue;
                p.Finished = true;
                _pending.RemoveAt(i);
                _net?.Cancel(p.Handle);
            }
        }

        void Connect()
        {
            _state = LinkState.Connecting;
            var body = JsonUtility.ToJson(new Protocol.SessionReq { build = _build });
            Send(new VoiceRequest { Path = "/v1/session", Json = body, Bearer = _cfg.Token, Timeout = SessionTimeout }, res =>
            {
                var s = res.Status == 200 ? Protocol.Parse<Protocol.SessionRes>(res.Text) : null;
                if (Protocol.ValidSession(s))
                {
                    s_link = new Link
                    {
                        Identity = _cfg.Identity, Token = s.session, ConfigVersion = s.config_version, Mock = s.mode == "mock",
                        Features = s.features, ExpiresAt = Now + Mathf.Min(s.expires_in_s, 7200),
                    };
                    _state = LinkState.Online;
                    Last["session"] = s_link.Mock ? "mock" : "live";
                    return;
                }
                Last["session"] = res.Status == 0 ? (res.Error ?? "network") : res.Status == 401 ? "unauthorized" : "http_" + res.Status;
                if (res.Status != 401 && !_sessionRetried) { _sessionRetried = true; _state = LinkState.Waiting; _retryAt = Now + SessionRetryDelay; }
                else _state = LinkState.Failed;   // offline until the next scene load
            });
        }

        // ---------------------------------------------------------------- tick

        public void Tick()
        {
            if (_disposed) return;
            float now = Now;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (p.Finished || now < p.Deadline) continue;
                p.Finished = true;
                _pending.RemoveAt(i);
                _net?.Cancel(p.Handle);
                p.Done(new VoiceResponse { Status = 0, Error = "timeout" });
            }
            if (_state == LinkState.Waiting && now >= _retryAt) Connect();
            if (_state == LinkState.Online && s_link != null && s_link.ExpiresAt - now < 60f) { s_link = null; Connect(); }
            if (_state == LinkState.Online && _pron == Pron.Waiting) StartPronounce();
            if (_state == LinkState.Failed || _state == LinkState.Offline) { if (_pron == Pron.Waiting) _pron = Pron.Hidden; }
            TickMonster(now);
        }

        // ---------------------------------------------------------------- events

        public void OnPhase(GamePhase from, GamePhase to)
        {
            if (to == GamePhase.Reveal || to == GamePhase.Quiz)
            {
                StopMonster();
                ClearHint();
                if (to == GamePhase.Reveal) BeginPronounce();
            }
            if (from == GamePhase.Quiz && to != GamePhase.Quiz)
            {
                _pronWant = false;
                _quizAnswerLocked = false;
                if (!_pronPlaying && _arb.Busy(VoiceChannel.Native))
                {
                    Services.Audio.StopVoice(VoiceChannel.Native, 0f);
                    _arb.Release(VoiceChannel.Native, 0);
                }
            }
            if (from == GamePhase.Puzzle && to == GamePhase.Explore) ClearHint();     // walked away from the console
            if (to == GamePhase.Chase) { ClearHint(); _brain.OnChase(Now); }
            if (to == GamePhase.Dead || to == GamePhase.Escaped || to == GamePhase.Title) EndRun();
        }

        public void OnPuzzle(LangPuzzle p)
        {
            string key = Protocol.PuzzleKey(p);
            bool same = _puzzle != null && p != null && p.Id == _puzzle.Id && key == _puzzleKey;
            if (!same)
            {
                ClearHint();
                _hintsGiven = 0; _attempts = 0; _missed = null; _lastHintClip = null; _lastHint = default; _lastHintAt = float.NegativeInfinity;
                _hintsAvailable = null;
            }
            _puzzle = p; _puzzleKey = key;
            // a new console: the previous sentence's voice stops and its state is forgotten
            if (_pronId != (p?.Id ?? 0) || !same)
            {
                if (_arb.Busy(VoiceChannel.Native))
                {
                    Services.Audio.StopVoice(VoiceChannel.Native, 0.15f);
                    _arb.Release(VoiceChannel.Native, 0);
                }
                if (_pron == Pron.Loading) CancelFeature("pronounce");
                _pronToken++; _pron = Pron.None; _pronWant = false; _pronId = 0; _quizAnswerLocked = false;
            }
        }

        public void OnAnswer(AnswerEvent e)
        {
            _facts.Observe(e);
            _brain.OnAnswer(e, _facts, Now);
            if (!e.Correct && _puzzle != null && e.PuzzleId == _puzzle.Id) { _attempts++; _missed = e.MissedConcept; }
        }

        public void OnStage(int stage) => _brain.OnStage(stage, Now);

        void EndRun()
        {
            _quizAnswerLocked = false;
            _pronWant = false;
            CancelAll();
            foreach (VoiceChannel c in Enum.GetValues(typeof(VoiceChannel))) Services.Audio.StopVoice(c, 0f);
            _arb.ReleaseAll();
            Services.UI.ShowSubtitle(default);
            _brain.Clear();
        }

        /// <summary>Aborts every request; nothing in flight will be shown or played afterwards.</summary>
        public void CancelAll()
        {
            _net?.CancelAll();
            foreach (var p in _pending) p.Finished = true;
            _pending.Clear();
            _monsterToken++; _hintToken++; _pronToken++;
            _brain.InFlight = false; _hintInFlight = false;
            if (_pron == Pron.Loading || _pron == Pron.Waiting) _pron = Pron.Failed;
        }

        public void Dispose()
        {
            if (_disposed) return;
            CancelAll();
            _disposed = true;
        }

        // ---------------------------------------------------------------- playback

        /// <summary>Offers a line to the arbiter and plays it. onEvent sees Started (with the real length) and the end.</summary>
        bool Play(VoiceChannel ch, float[] samples, bool mock, Action<VoicePlayback> onEvent)
        {
            var stop = new List<VoiceChannel>();
            if (!_arb.Offer(ch, stop)) return false;
            foreach (var c in stop)
            {
                Services.Audio.StopVoice(c, 0.15f);
                _arb.Release(c, 0);
            }
            var clip = new VoiceClip { Samples = samples, SampleRate = Protocol.SampleRate, Channel = ch, Stage = Mathf.Clamp(S.BeastStage, 1, 3), Gain = _cfg.Volume, Mock = mock };
            bool ended = false;
            int handle = Services.Audio.PlayVoice(clip, ev =>
            {
                if (ev.Event != VoicePlaybackEvent.Started) { ended = true; _arb.Release(ch, ev.Handle); }
                onEvent?.Invoke(ev);
            });
            if (handle != 0 && !ended) _arb.Claim(ch, handle);
            return handle != 0;
        }

        void FetchSpeech(Protocol.SpeechRef sp, string feature, Action<float[], bool> ok, Action<string> fail)
        {
            Call(feature, new VoiceRequest { Method = "GET", Path = "/v1/speech/" + sp.id, Timeout = SpeechTimeout, Binary = true, MaxBytes = Protocol.MaxAudioBytes }, res =>
            {
                var samples = Protocol.Pcm(res.Bytes);
                if (samples == null) { fail("bad_audio"); return; }
                ok(samples, sp.source == "mock" || MockMode);
            }, fail);
        }

        // ---------------------------------------------------------------- monster dialogue

        void TickMonster(float now)
        {
            if (!FeatureOn("monster") || _brain.InFlight) return;
            string trigger = _brain.Take(now, S.Phase, S.BeastGrace);
            if (trigger == null) return;
            float aggression = 0.5f;
            try { aggression = Services.Difficulty.BeastAggression; } catch (Exception) { }
            var req = new Protocol.MonsterReq
            {
                seq = ++_seq, trigger = trigger, stage = Mathf.Clamp(S.BeastStage, 1, 3), concept = _facts.LastConcept,
                concept_misses = Mathf.Clamp(_facts.Misses(_facts.LastConcept), 0, 99), wrong_streak = Mathf.Clamp(_facts.WrongStreak, 0, 99),
                mistakes = Mathf.Clamp(S.Mistakes, 0, 999), total = Mathf.Clamp(S.TotalPuzzles, 0, 12),
                pace = _facts.Pace, distance = RunFacts.Distance(S.BeastDistance), aggression = RunFacts.Aggression(aggression),
            };
            req.solved = Mathf.Clamp(S.PuzzlesSolved, 0, req.total);
            int token = ++_monsterToken;
            float sentAt = now;
            _brain.InFlight = true;
            Call("monster", new VoiceRequest { Path = "/v1/monster-line", Json = Protocol.MonsterJson(req), Timeout = MonsterTimeout }, res =>
            {
                _brain.InFlight = false;
                if (token != _monsterToken) return;
                var r = Protocol.Parse<Protocol.LineRes>(res.Text);
                if (r == null || !Protocol.SourceOk(r.source) || !TextGuard.MonsterLineOk(r.text) || !Protocol.ValidSpeech(r.speech)) { Last["monster"] = "invalid"; return; }
                string text = r.text.Trim();
                var source = Protocol.Source(r.speech.source, MockMode);
                FetchSpeech(r.speech, "monster", (samples, mock) =>
                {
                    if (token != _monsterToken || !DialogueBrain.PhaseAllows(S.Phase) || Now - sentAt > MonsterMaxAge) { Last["monster"] = "stale"; return; }
                    bool played = Play(VoiceChannel.Monster, samples, mock, ev =>
                    {
                        if (ev.Event == VoicePlaybackEvent.Started)
                        {
                            Last["monster"] = Label(source);
                            if (_cfg.Subtitles) { _subtitleHandle = ev.Handle; Services.UI.ShowSubtitle(new SubtitleLine { Channel = VoiceChannel.Monster, Text = text, Seconds = ev.Seconds, Source = source }); }
                        }
                        else if (ev.Handle != 0 && ev.Handle == _subtitleHandle) { _subtitleHandle = 0; Services.UI.ShowSubtitle(default); }
                    });
                    if (!played) Last["monster"] = "dropped";
                }, code => { if (token == _monsterToken) Last["monster"] = code; });
            }, code => { _brain.InFlight = false; if (token == _monsterToken) Last["monster"] = code; });
        }

        void StopMonster()
        {
            CancelFeature("monster");
            _monsterToken++;
            _brain.InFlight = false;
            _brain.Clear();
            if (_arb.Busy(VoiceChannel.Monster)) { Services.Audio.StopVoice(VoiceChannel.Monster, 0.15f); _arb.Release(VoiceChannel.Monster, 0); }
            if (_subtitleHandle != 0) { _subtitleHandle = 0; Services.UI.ShowSubtitle(default); }
        }

        static string Label(VoiceSource s) => s == VoiceSource.Mock ? "mock" : s == VoiceSource.Cache ? "cache" : s == VoiceSource.Offline ? "offline" : "live";

        // ---------------------------------------------------------------- hints

        public bool HintsAvailable
        {
            get
            {
                if (_puzzle == null || S?.World == null) return false;
                if (_hintsAvailable == null)
                {
                    try { _hintsAvailable = Services.Language.GetHintContext(S.World, _puzzle.Id, null) != null; }
                    catch (Exception) { _hintsAvailable = false; }
                }
                return _hintsAvailable.Value;
            }
        }

        public void RequestHint(int puzzleId)
        {
            if (_disposed || _puzzle == null || puzzleId != _puzzle.Id || S.Phase != GamePhase.Puzzle) return;
            float now = Now;
            if (_hintInFlight || now - _lastHintAt < HintCooldown) return;
            _lastHintAt = now;
            if (_hintsGiven >= MaxHintsPerConsole)
            {
                // the console has had its hints: show (and say) the last one again, no new request
                ShowHint(_lastHint);
                if (_lastHintClip != null) PlayHintClip(_lastHintClip, _hintToken);
                return;
            }
            HintContext ctx = null;
            try { ctx = Services.Language.GetHintContext(S.World, _puzzle.Id, _missed); } catch (Exception e) { Debug.LogException(e); }
            if (ctx == null || ctx.OfflineHints == null || ctx.OfflineHints.Length == 0) return;
            int level = _hintsGiven + 1;
            var req = FeatureOn("hint") && !_breaker.Open(now) ? Protocol.HintFor(_puzzle, ++_seq, level, ctx.Concept, _attempts) : null;
            if (req == null) { OfflineHint(ctx, level); return; }

            int token = ++_hintToken;
            int id = _puzzle.Id;
            _hintInFlight = true;
            ShowHint(new HintView { PuzzleId = id, Visible = true, Pending = true, Level = level, Source = VoiceSource.Live });
            Call("hint", new VoiceRequest { Path = "/v1/hint", Json = JsonUtility.ToJson(req), Timeout = HintTimeout }, res =>
            {
                if (token != _hintToken) return;
                _hintInFlight = false;
                var r = Protocol.Parse<Protocol.LineRes>(res.Text);
                bool safe = false;
                if (r != null && Protocol.SourceOk(r.source) && !string.IsNullOrWhiteSpace(r.text))
                    try { safe = Services.Language.IsHintSafe(S.World, id, r.text.Trim()); } catch (Exception e) { Debug.LogException(e); }
                if (!safe) { Last["hint"] = r == null ? "invalid" : "unsafe"; OfflineHint(ctx, level); return; }
                var view = new HintView { PuzzleId = id, Visible = true, Text = r.text.Trim(), Level = level, Source = Protocol.Source(r.source, MockMode) };
                _hintsGiven = level; _lastHint = view; _lastHintClip = null;
                ShowHint(view);
                Last["hint"] = Label(view.Source);
                if (!FeatureOn("hint_speech") || !Protocol.ValidSpeech(r.speech)) return;   // the text is the feature; audio is extra
                FetchSpeech(r.speech, "hint_speech", (samples, mock) =>
                {
                    if (token != _hintToken || !_hintShowing) return;   // the player moved on: keep quiet
                    _lastHintClip = new ClipCache.Entry { Samples = samples, Rate = Protocol.SampleRate, Mock = mock };
                    PlayHintClip(_lastHintClip, token);
                }, code => { });
            }, code =>
            {
                if (token != _hintToken) return;
                _hintInFlight = false;
                Last["hint"] = code;
                OfflineHint(ctx, level);   // shown no later than the timeout
            });
        }

        void OfflineHint(HintContext ctx, int level)
        {
            var view = new HintView { PuzzleId = _puzzle.Id, Visible = true, Text = ctx.OfflineHints[Mathf.Min(level, ctx.OfflineHints.Length) - 1], Level = level, Source = VoiceSource.Offline };
            _hintsGiven = level; _lastHint = view; _lastHintClip = null;
            if (Last["hint"] == "-" || _state != LinkState.Online) Last["hint"] = "offline";
            ShowHint(view);
        }

        void PlayHintClip(ClipCache.Entry clip, int token)
        {
            if (token != _hintToken || !_hintShowing) return;
            Play(VoiceChannel.Ship, clip.Samples, clip.Mock, null);
        }

        void ShowHint(HintView v)
        {
            _hintShowing = v.Visible;
            Services.UI.ShowHint(v);
        }

        void ClearHint()
        {
            CancelFeature("hint"); CancelFeature("hint_speech");
            _hintToken++;
            _hintInFlight = false;
            if (_hintShowing) { _hintShowing = false; Services.UI.ShowHint(default); }
            if (_arb.Busy(VoiceChannel.Ship)) { Services.Audio.StopVoice(VoiceChannel.Ship, 0.15f); _arb.Release(VoiceChannel.Ship, 0); }
        }

        // ---------------------------------------------------------------- pronunciation

        /// <summary>At the reveal: work out the sentence and fetch its audio quietly. Nothing is played here.</summary>
        void BeginPronounce()
        {
            if (_puzzle == null || S.World == null) return;
            _pronToken++;
            _pronId = _puzzle.Id; _pronWant = false; _pronAttempts = 0; _pronPlaying = false; _pronAsked = false; _quizAnswerLocked = false;
            RevealInfo r = null;
            try { r = Services.Language.GetReveal(S.World, _pronId); } catch (Exception e) { Debug.LogException(e); }
            _pronText = r?.SpeechText?.Trim(); _pronLang = r?.SpeechLang;
            if (string.IsNullOrEmpty(_pronText) || string.IsNullOrEmpty(_pronLang)) { _pron = Pron.Unavailable; return; }
            if (_net == null || !_cfg.Enabled) { _pron = Pron.Hidden; return; }   // no backend configured: no cue at all
            if (_state == LinkState.Connecting || _state == LinkState.Waiting) { _pron = Pron.Waiting; return; }
            StartPronounce();
        }

        string PronKey() => s_link == null ? null : $"{_cfg.Identity}|{s_link.ConfigVersion}|native|{_pronLang}|{_pronText}";

        void StartPronounce()
        {
            if (!FeatureOn("pronounce")) { _pron = _state == LinkState.Online ? Pron.Unavailable : Pron.Hidden; return; }
            _pronCacheKey = PronKey();
            string cacheKey = _pronCacheKey;
            var hit = ClipCache.Get(_pronCacheKey);
            if (hit != null) { _pron = Pron.Ready; _pronSource = hit.Mock ? VoiceSource.Mock : VoiceSource.Cache; Last["pronounce"] = Label(_pronSource); return; }
            if (_breaker.Open(Now)) { _pron = Pron.Failed; Last["pronounce"] = "breaker"; return; }
            int token = ++_pronToken;
            _pron = Pron.Loading;
            _pronAttempts++;
            var body = JsonUtility.ToJson(new Protocol.PronounceReq { seq = ++_seq, lang = _pronLang, text = _pronText });
            Call("pronounce", new VoiceRequest { Path = "/v1/pronounce", Json = body, Timeout = PronounceTimeout }, res =>
            {
                if (token != _pronToken) return;
                var r = Protocol.Parse<Protocol.PronounceRes>(res.Text);
                if (r == null || !Protocol.ValidSpeech(r.speech)) { PronFailed("invalid"); return; }
                var source = Protocol.Source(r.speech.source, MockMode);
                FetchSpeech(r.speech, "pronounce", (samples, mock) =>
                {
                    // valid audio is kept even if the player has moved on, so a replay never needs a new request
                    ClipCache.Put(cacheKey, new ClipCache.Entry { Samples = samples, Rate = Protocol.SampleRate, Mock = mock });
                    if (token != _pronToken) return;
                    _pron = Pron.Ready; _pronSource = mock ? VoiceSource.Mock : source;
                    Last["pronounce"] = Label(_pronSource);
                    if (_pronWant && _quizAnswerLocked && S.Phase == GamePhase.Quiz) { _pronWant = false; PlayPronunciation(_pronSource); }
                    else ShowPron(PronunciationState.Ready, _pronSource, quietUnlessAsked: true);
                }, code => { if (token == _pronToken) PronFailed(code); });
            }, code => { if (token == _pronToken) PronFailed(code); });
        }

        void PronFailed(string code)
        {
            _pron = Pron.Failed;
            Last["pronounce"] = code;
            if (_pronWant) { _pronWant = false; ShowPron(PronunciationState.Unavailable, VoiceSource.Offline); }
        }

        bool _pronAsked;   // the payoff has asked at least once for this sentence: the cue may show

        void ShowPron(PronunciationState state, VoiceSource source, bool quietUnlessAsked = false)
        {
            if (quietUnlessAsked && !_pronAsked) return;
            Services.UI.ShowPronunciation(new PronunciationView { PuzzleId = _pronId, State = state, Source = source });
        }

        /// <summary>The quiz payoff (replay false) and P on it (replay true). Only path that ever plays the Native channel.</summary>
        public void RequestPronunciation(int puzzleId, bool replay)
        {
            if (_disposed || !_quizAnswerLocked || S.Phase != GamePhase.Quiz || puzzleId == 0 || puzzleId != _pronId) return;
            _pronAsked = true;
            switch (_pron)
            {
                case Pron.Ready:
                    PlayPronunciation(replay ? (_pronSource == VoiceSource.Mock ? VoiceSource.Mock : VoiceSource.Cache) : _pronSource);
                    break;
                case Pron.Loading:
                case Pron.Waiting:
                    _pronWant = true;
                    ShowPron(PronunciationState.Loading, VoiceSource.Live);
                    break;
                case Pron.Failed:
                    if (replay && _pronAttempts < MaxPronounceAttempts && FeatureOn("pronounce")) { _pronWant = true; StartPronounce(); ShowPron(_pron == Pron.Loading ? PronunciationState.Loading : PronunciationState.Unavailable, VoiceSource.Live); }
                    else ShowPron(PronunciationState.Unavailable, VoiceSource.Offline);
                    break;
                case Pron.Unavailable:
                    ShowPron(PronunciationState.Unavailable, VoiceSource.Offline);
                    break;
                default:
                    ShowPron(PronunciationState.Hidden, VoiceSource.Offline);
                    break;
            }
        }

        public void NotifyQuizAnswerLocked(int puzzleId)
        {
            if (Current && S.Phase == GamePhase.Quiz && puzzleId != 0 && puzzleId == _pronId)
                _quizAnswerLocked = true;
        }

        void PlayPronunciation(VoiceSource source)
        {
            var clip = ClipCache.Get(_pronCacheKey);
            if (clip == null) { _pron = Pron.Failed; ShowPron(PronunciationState.Unavailable, VoiceSource.Offline); return; }
            int token = _pronToken;
            ShowPron(PronunciationState.Preparing, source);   // processing: not playing yet
            bool ok = Play(VoiceChannel.Native, clip.Samples, clip.Mock, ev =>
            {
                if (token != _pronToken) return;
                if (ev.Event == VoicePlaybackEvent.Started) { _pronPlaying = true; ShowPron(PronunciationState.Playing, source); Last["pronounce"] = Label(source); }
                else { _pronPlaying = false; ShowPron(PronunciationState.Ready, source); }
            });
            if (!ok && !_pronPlaying) ShowPron(PronunciationState.Ready, source);
        }

        // ---------------------------------------------------------------- debug

        public string DebugLine() =>
            $"VOICE {Mode}  session {Last["session"]}  monster {Last["monster"]}  hint {Last["hint"]}  pronounce {Last["pronounce"]}" +
            (MockMode ? "  [MOCK: canned text, tone audio, not speech]" : "");
    }
}
