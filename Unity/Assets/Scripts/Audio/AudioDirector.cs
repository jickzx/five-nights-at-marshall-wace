using System.Collections.Generic;
using Fnmw.Audio.Synth;
using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Audio
{
    /// <summary>
    /// The real IAudioDirector. Owns the generative score (a ScoreEngine rendered in OnAudioFilterRead),
    /// listens to GameEvents, turns words into motifs, and positions the beast, footsteps and consoles in 3D.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour, IAudioDirector
    {
        public static AudioDirector Instance { get; private set; }

        public ScoreEngine Engine { get; private set; }
        public SfxBank Sfx { get; private set; }
        public IntensityModel Model { get; } = new IntensityModel();
        public int SampleRate { get; private set; }

        /// <summary>When set (audition mode), the score follows this instead of the game.</summary>
        public bool OverrideActive;
        public Mood OverrideMood = Mood.Unsettling;
        public float OverrideLevel;
        public int OverrideStage = 1;

        float _intensity;
        bool _ended;
        LangPuzzle _puzzle;
        readonly List<Tile> _picked = new List<Tile>();
        readonly Dictionary<string, Motif> _motifs = new Dictionary<string, Motif>();
        float _phraseEndsAt = -1f, _phraseStartedAt = -10f;
        Motif[] _lastPhrase;
        float _lastStageStinger = -10f;
        readonly HashSet<string> _warned = new HashSet<string>();

        internal SpatialAudio Spatial { get; private set; }
        AudioSource _scoreSource;
        MasterLimiter _limiter;

        public static AudioDirector Create()
        {
            var go = new GameObject("AudioDirector");
            return go.AddComponent<AudioDirector>();
        }

        void Awake()
        {
            Instance = this;
            SampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            Engine = new ScoreEngine(SampleRate);
            Sfx = new SfxBank(SampleRate);

            // The score bus: a 2D AudioSource whose filter renders the engine.
            var music = new GameObject("Score");
            music.transform.SetParent(transform, false);
            var src = music.AddComponent<AudioSource>();
            src.clip = AudioClip.Create("fnmw_silence", SampleRate, 1, SampleRate, false);
            src.loop = true; src.spatialBlend = 0f; src.priority = 0; src.playOnAwake = false;
            music.AddComponent<ScoreFilter>().Engine = Engine;
            src.Play();
            _scoreSource = src;
            // A device change (headphones unplugged, another app grabbing output) resets Unity audio and stops
            // every source. Restart the score so the music never silently dies mid-run.
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigChanged;

            Spatial = gameObject.AddComponent<SpatialAudio>();
            Spatial.Director = this;
            gameObject.AddComponent<AuditionMode>().Director = this;
            gameObject.AddComponent<AudioTap>();

            GameEvents.PhaseChanged += OnPhase;
            GameEvents.PuzzleStarted += OnPuzzle;
            GameEvents.AnswerSubmitted += OnAnswer;
            GameEvents.BeastStageChanged += OnStage;
            GameEvents.PlayerCaught += OnCaught;
            GameEvents.Escaped += OnEscaped;
            GameEvents.TilePicked += OnTilePicked;
            GameEvents.TilesCleared += OnTilesCleared;
        }

        void OnDestroy()
        {
            GameEvents.PhaseChanged -= OnPhase;
            GameEvents.PuzzleStarted -= OnPuzzle;
            GameEvents.AnswerSubmitted -= OnAnswer;
            GameEvents.BeastStageChanged -= OnStage;
            GameEvents.PlayerCaught -= OnCaught;
            GameEvents.Escaped -= OnEscaped;
            GameEvents.TilePicked -= OnTilePicked;
            GameEvents.TilesCleared -= OnTilesCleared;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigChanged;
            Sfx?.Destroy();
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------ IAudioDirector

        public void SetIntensity(float intensity01) => _intensity = Mathf.Clamp01(intensity01);

        public void PlayStinger(string name)
        {
            switch (name)
            {
                case "death": case "caught": Die(); break;
                case "liftoff": case "escape": case "escaped": Escape(); break;
                case "correct": AnswerStinger("correct", 0.55f); break;
                case "wrong": AnswerStinger("wrong", 0.8f); break;
                case "stage_up": StageStinger(Services.State.BeastStage); break;
                case "stage_up_2": StageStinger(2); break;
                case "stage_up_3": StageStinger(3); break;
                case "beast_roar": case "roar": Spatial.BeastSay("beast_roar", 1f); break;
                case "beast_lunge": case "lunge": Spatial.BeastSay("beast_lunge", 1f); break;
                case "beast_vocal": Spatial.BeastVocal(); break;
                case "footstep": Spatial.PlayerStep(); break;
                case "beast_step": Spatial.BeastSay("beast_step", 0.9f); break;
                case "console_on":
                    Engine.PlaySample(Sfx.Get("console_on"), 0.45f, 0f, 0f, 0.25f);
                    Engine.PlaySample(Sfx.Get("static", Sfx.NextVariant("static")), 0.2f, 0.3f, 0.25f, 0.2f);
                    break;
                case "static": Engine.PlaySample(Sfx.Get("static", Sfx.NextVariant("static")), 0.3f, Random.Range(-0.5f, 0.5f)); break;
                case "ship_power": Engine.PlaySample(Sfx.Get("ship_power"), 0.7f, 0f, 0f, 0.2f); break;
                case "begin": Engine.PlaySample(Sfx.Get("begin"), 0.7f, 0f, 0f, 0.5f); break;
                default:
                    var b = Sfx.Get(name);
                    if (b != null) Engine.PlaySample(b, 0.7f);
                    else if (_warned.Add(name)) Debug.LogWarning($"[Audio] unknown stinger '{name}'");
                    break;
            }
        }

        // ------------------------------------------------------------ score control

        void OnAudioConfigChanged(bool deviceWasChanged)
        {
            if (_scoreSource != null && !_scoreSource.isPlaying) _scoreSource.Play();
            Spatial?.RestartLoops();
        }

        void Update()
        {
            if (_scoreSource != null && !_scoreSource.isPlaying && _scoreSource.isActiveAndEnabled) OnAudioConfigChanged(false);
            if (!_limiter) _limiter = MasterLimiter.Ensure();
            var s = Services.State;
            if (OverrideActive)
            {
                Engine.SetTarget(OverrideMood, OverrideLevel, HeatFor(OverrideMood, OverrideLevel), OverrideStage);
                return;
            }
            var input = new MoodInput
            {
                Intensity = _intensity,
                Threat = s.Threat,
                Stage = s.BeastStage,
                Phase = (ScorePhase)(int)s.Phase,
                BeastDistance = s.BeastDistance,
            };
            var mood = Model.Update(input);
            if (mood == Mood.Death) Die();
            if (mood == Mood.Escape) Escape();
            Engine.SetTarget(mood, Model.Level, Model.Heat, s.BeastStage);
        }

        static float HeatFor(Mood m, float level)
        {
            switch (m)
            {
                case Mood.Unsettling: return Mathf.Clamp01(level / IntensityModel.TenseEnter);
                case Mood.Tense: return Mathf.Clamp01((level - IntensityModel.TenseExit) / (IntensityModel.PanicEnter - IntensityModel.TenseExit));
                case Mood.Panic: return Mathf.Clamp01((level - IntensityModel.PanicExit) / (1f - IntensityModel.PanicExit));
                default: return 0f;
            }
        }

        void Die()
        {
            if (_ended) return;
            _ended = true;
            Engine.TriggerDeath();
            Spatial.Silence();
        }

        void Escape()
        {
            if (_ended) return;
            _ended = true;
            Engine.TriggerEscape(Sfx.Get("ship_power"));
            Spatial.Silence();
        }

        /// <summary>Audition mode: return to a clean slate so endings can be replayed.</summary>
        public void ResetForAudition()
        {
            _ended = false;
            Model.Reset();
            Engine.ResetScore();
        }

        void StageStinger(int stage)
        {
            if (stage < 2 || Time.unscaledTime - _lastStageStinger < 1.5f) return;
            _lastStageStinger = Time.unscaledTime;
            Engine.PlaySample(Sfx.Get(stage >= 3 ? "stage_up_3" : "stage_up_2"), stage >= 3 ? 0.85f : 0.7f, 0f, 0f, 0.45f);
            Spatial.BeastSay(stage >= 3 ? "beast_roar" : "beast_vocal_2", 1f);
        }

        // Answer feedback lands at the end of the phrase so the stinger completes the musical sentence.
        void AnswerStinger(string name, float gain)
        {
            float now = Time.unscaledTime;
            float delay = now - _phraseStartedAt < 0.5f && _phraseEndsAt > now ? _phraseEndsAt - now : 0f;
            Engine.PlaySample(Sfx.Get(name), gain, 0f, delay, 0.3f);
        }

        // ------------------------------------------------------------ language as music

        string LanguageCode => Services.State.World?.Real?.Code;

        public Motif MotifFor(Tile t)
        {
            if (t == null) return null;
            string key = (t.Id ?? t.Alien) + "|" + t.GlyphSeed + "|" + LanguageCode;
            if (!_motifs.TryGetValue(key, out var m))
                _motifs[key] = m = MotifGenerator.Generate(t.GlyphSeed, t.Id ?? t.Alien, LanguageCode);
            return m;
        }

        Motif MotifForWord(string alien)
        {
            var tile = _puzzle?.Tiles?.Find(x => x.Alien == alien);
            if (tile == null && _puzzle != null)
                foreach (var ex in _puzzle.Examples) { tile = ex.Words.Find(x => x.Alien == alien); if (tile != null) break; }
            return tile != null ? MotifFor(tile) : MotifGenerator.Generate(MotifGenerator.StableHash(alien), alien, LanguageCode);
        }

        void OnPuzzle(LangPuzzle p) { _puzzle = p; _picked.Clear(); }

        void OnTilePicked(Tile t)
        {
            _picked.Add(t);
            Engine.PlayMotif(MotifFor(t), PhraseStyle.Pick);
        }

        void OnTilesCleared() => _picked.Clear();

        void OnAnswer(AnswerEvent e)
        {
            Motif[] phrase = null;
            if (_picked.Count > 0) phrase = _picked.ConvertAll(MotifFor).ToArray();
            else if (e.Correct) phrase = RevealPhrase(e.PuzzleId);
            if (phrase == null && _puzzle != null && _puzzle.Tiles.Count > 0)
            {
                // The UI did not report picks: approximate with the first tiles on offer.
                int n = Mathf.Min(2, _puzzle.Tiles.Count);
                phrase = new Motif[n];
                for (int i = 0; i < n; i++) phrase[i] = MotifFor(_puzzle.Tiles[i]);
            }
            if (phrase == null) return;
            _phraseStartedAt = Time.unscaledTime;
            _phraseEndsAt = _phraseStartedAt + Engine.PlayPhrase(phrase, e.Correct ? PhraseStyle.Correct : PhraseStyle.Wrong);
            if (e.Correct) _lastPhrase = phrase;
            _picked.Clear();
        }

        Motif[] RevealPhrase(int puzzleId)
        {
            var world = Services.State.World;
            if (world == null) return null;
            RevealInfo r = null;
            try { r = Services.Language.GetReveal(world, puzzleId); } catch { /* engine may not support it yet */ }
            if (r == null || string.IsNullOrEmpty(r.AlienText)) return null;
            var words = r.AlienText.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            var list = new List<Motif>();
            foreach (var w in words) list.Add(MotifForWord(w));
            return list.ToArray();
        }

        void OnPhase(GamePhase from, GamePhase to)
        {
            if (to == GamePhase.Reveal)
            {
                var phrase = _lastPhrase ?? (_puzzle != null ? RevealPhrase(_puzzle.Id) : null);
                // restate after the correct-answer phrase has finished
                float delay = Mathf.Max(0f, _phraseEndsAt - Time.unscaledTime) + 0.6f;
                if (phrase != null) Engine.PlayPhrase(phrase, PhraseStyle.Reveal, delay);
            }
            if (to == GamePhase.Dead) Die();
            if (to == GamePhase.Escaped) Escape();
        }

        void OnStage(int stage) => StageStinger(stage);
        void OnCaught() { Spatial.BeastSay("beast_lunge", 1f); Die(); }
        void OnEscaped() => Escape();

        /// <summary>Audition helper: play a language's three demo words as picks, wrong, correct and reveal.</summary>
        public float DemoLanguage(string code, float t0)
        {
            var words = new[]
            {
                MotifGenerator.Generate(101, "door", code), MotifGenerator.Generate(202, "open", code), MotifGenerator.Generate(303, "ship", code),
            };
            float t = t0;
            foreach (var w in words) { Engine.PlayMotif(w, PhraseStyle.Pick, t); t += 1.4f; }
            float wrong = Engine.PlayPhrase(words, PhraseStyle.Wrong, t);
            Engine.PlaySample(Sfx.Get("wrong"), 0.7f, 0f, wrong, 0.3f);
            t = wrong + 1.8f;
            float right = Engine.PlayPhrase(words, PhraseStyle.Correct, t);
            Engine.PlaySample(Sfx.Get("correct"), 0.5f, 0f, right, 0.3f);
            t = right + 1.6f;
            t = Engine.PlayPhrase(words, PhraseStyle.Reveal, t) + 2.5f;
            return t - t0;
        }
    }

    /// <summary>Renders the score on Unity's audio thread.</summary>
    public sealed class ScoreFilter : MonoBehaviour
    {
        [System.NonSerialized] public ScoreEngine Engine;
        void OnAudioFilterRead(float[] data, int channels) => Engine?.Process(data, channels);
    }
}
