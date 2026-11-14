// The live, generative score. One instance renders the whole 2D music + ambience bus.
// Main thread: call the public Play*/Set*/Trigger* methods (they only enqueue).
// Audio thread: call Process() from OnAudioFilterRead. Offline: call Render().
using System;

namespace Fnmw.Audio.Synth
{
    public enum PhraseStyle { Pick, Correct, Wrong, Reveal }

    public sealed partial class ScoreEngine
    {
        // ------------------------------------------------------------------ layers
        enum L { Drone, Thump, Groan, Heart, Cluster, Tick, Riser, Drive, Shriek, Shepard, Ambience, Count }
        const int LayerCount = (int)L.Count;

        // Target gain of every layer for each mood (rows: Title, Unsettling, Tense, Panic, Death, Escape).
        static readonly float[,] MoodMix =
        {
            //  drone thump groan heart clust tick  riser drive shriek shep  amb
            { 0.30f, 0f,   0.5f, 0f,   0f,   0f,   0f,   0f,   0f,   0f,   1.0f },
            { 0.55f, 1f,   1.0f, 0f,   0.12f,0f,   0.08f,0f,   0f,   0f,   0.8f },
            { 0.75f, 0.2f, 0.45f,1f,   0.85f,0.8f, 0.5f, 0f,   0f,   0.18f,0.8f },
            { 0.55f, 0f,   0.15f,0.25f,0.6f, 1f,   0.8f, 1f,   0.75f,1f,   0.6f },
            { 0f,    0f,   0f,   0f,   0f,   0f,   0f,   0f,   0f,   0f,   0.10f },
            { 0f,    0f,   0f,   0f,   0f,   0f,   0f,   0f,   0f,   0f,   0f   },
        };

        // Per-layer output trim (balances the mix; measured with the render harness).
        static readonly float[] Trim = { 0.26f, 0.55f, 0.32f, 0.85f, 0.13f, 0.24f, 0.15f, 0.55f, 0.075f, 0.10f, 0.12f };
        // Per-layer reverb send.
        static readonly float[] SendAmt = { 0.25f, 0.6f, 0.85f, 0.15f, 0.45f, 0.2f, 0.4f, 0.12f, 0.5f, 0.35f, 0.1f };

        public readonly int SampleRate;
        readonly float _sr;

        // ------------------------------------------------------------------ control (main thread -> audio thread)
        volatile int _targetMood = (int)Mood.Title;
        volatile float _targetLevel, _targetHeat;
        volatile int _targetStage = 1;

        enum Cmd { Motif, Chord, Sample, Death, Escape, Reset }
        struct Command
        {
            public Cmd Type;
            public object Obj;
            public float A, B, C, D, E, F;
            public int I;
        }
        readonly Command[] _queue = new Command[512];
        int _qHead, _qTail;
        readonly object _lock = new object();

        // ------------------------------------------------------------------ state (audio thread only)
        readonly MoodScheduler _sched = new MoodScheduler();
        Mood _mood = Mood.Title;
        float _level, _heat;
        long _sample;           // samples rendered since construction
        long _step;             // 16th-note counter
        float _stepPhase;       // 0..1 progress to the next 16th
        Smoother _bpm;
        float _bpmTarget = 50f;
        readonly Smoother[] _gain = new Smoother[LayerCount];
        readonly float[] _gainTarget = new float[LayerCount];
        float _breathTarget = 1f; Smoother _breathS; float _breathTimer = 6f;
        float _panicClimb;      // semitones the panic layers have climbed
        int _clusterSet;
        Rng _rng = new Rng(1337);
        Smoother _duck;         // ducks the dense layers while motifs play
        float _motifActivity;
        Smoother _master;

        // drone
        readonly float[] _dronePh = new float[6];
        Svf _droneF;
        float _droneLfo;
        float _rootShift; Smoother _rootShiftS;

        // cluster strings
        readonly float[] _clHz = new float[5], _clTarget = new float[5], _clPh = new float[10], _clLfo = new float[5];
        Svf _clFL, _clFR;
        float _clGlide = 0.6f;

        // shriek
        readonly float[] _shPh = new float[8];
        Svf _shF;

        // shepard
        float _shepPos, _shepRate;
        readonly float[] _shepPh = new float[8];

        // riser
        Svf _riserF;

        // bass (panic)
        float _bassPh, _bassHz, _bassEnv, _bassEnvK, _bassAmp, _bassAmpK;
        Svf _bassF;

        // groans
        readonly Groan[] _groans = { new Groan(), new Groan() };
        float _groanTimer = 4f;

        // ambience
        OnePole _rainHpL, _rainHpR, _rainLpL, _rainLpR, _seaLpL, _seaLpR;
        float _brownL, _brownR, _ambT;
        float _dropTimer;

        // voices
        readonly Perc[] _perc = new Perc[28];
        readonly Voice[] _voices;
        readonly Pad[] _pads = new Pad[16];
        readonly SamplePlayer[] _players = new SamplePlayer[10];
        long _voiceAge;

        // scheduled melodic notes (motifs, bells) with absolute start times
        struct Note { public long At; public VoiceKind Kind; public float Hz, Dur, Vel, Bend, Pan, Send; }
        readonly Note[] _notes = new Note[192];
        int _noteCount;

        // endings
        float _endT = -1f;

        readonly Reverb _verb;
        float _compEnv = 0f;
        OnePole _hpL1, _hpL2, _hpR1, _hpR2; // master high-pass: no sub rumble or DC into the limiter
        double _grSum; long _grCount;
        float _relK, _attK;

        // diagnostics, readable from any thread
        public Mood CurrentMood => _endT >= 0f ? _mood : _sched.Current;
        public float CurrentBpm => _bpm.Value;
        public long StepCount => _step;
        public float LastPeak { get; private set; }
        /// <summary>Average limiter gain reduction (dB) since the last call. Diagnostics only.</summary>
        public float TakeAverageGainReductionDb() { double v = _grCount > 0 ? _grSum / _grCount : 0; _grSum = 0; _grCount = 0; return (float)v; }

        public ScoreEngine(int sampleRate)
        {
            SampleRate = sampleRate;
            _sr = sampleRate;
            _voices = new Voice[24];
            for (int i = 0; i < _voices.Length; i++) _voices[i] = new Voice(sampleRate);
            for (int i = 0; i < _perc.Length; i++) _perc[i] = new Perc();
            for (int i = 0; i < _pads.Length; i++) _pads[i] = new Pad();
            for (int i = 0; i < _players.Length; i++) _players[i] = new SamplePlayer();
            _verb = new Reverb(sampleRate);
            _bpm.SetTime(2.5f, _sr); _bpm.Value = 50f;
            for (int i = 0; i < LayerCount; i++) _gain[i].SetTime(2.5f, _sr);
            _gainTarget[(int)L.Ambience] = 1f;
            _breathS.SetTime(3f, _sr); _breathS.Value = 1f;
            _duck.SetTime(0.25f, _sr); _duck.Value = 1f;
            _master.SetTime(0.02f, _sr); _master.Value = 1f;
            _rootShiftS.SetTime(3f, _sr);
            _hpL1.SetCutoff(30f, _sr); _hpL2.SetCutoff(30f, _sr); _hpR1.SetCutoff(30f, _sr); _hpR2.SetCutoff(30f, _sr);
            _rainHpL.SetCutoff(900f, _sr); _rainHpR.SetCutoff(1000f, _sr);
            _rainLpL.SetCutoff(6500f, _sr); _rainLpR.SetCutoff(6000f, _sr);
            _bassAmpK = 1f - (float)Math.Exp(-1.0 / (0.002 * _sr));
            _relK = (float)Math.Exp(-1.0 / (0.25 * _sr));
            _attK = (float)Math.Exp(-1.0 / (0.002 * _sr));
            SetCluster(0, true);
            for (int i = 0; i < 5; i++) _clLfo[i] = i * 0.21f;
        }

        // ================================================================== main-thread API

        /// <summary>Called every frame by the director. Mood changes are quantised on the audio thread.</summary>
        public void SetTarget(Mood mood, float level, float heat, int stage)
        {
            _targetMood = (int)mood; _targetLevel = level; _targetHeat = heat; _targetStage = stage;
        }

        /// <summary>Seconds per motif "beat" for each phrase style.</summary>
        public static float BeatSeconds(PhraseStyle style)
        {
            switch (style)
            {
                case PhraseStyle.Pick: return 0.2f;
                case PhraseStyle.Reveal: return 0.34f;
                default: return 0.16f;
            }
        }

        /// <summary>Play one word motif. Returns its length in seconds.</summary>
        public float PlayMotif(Motif m, PhraseStyle style, float delaySeconds = 0f, float detuneAmount = 0f)
        {
            if (m == null) return 0f;
            Enqueue(new Command { Type = Cmd.Motif, Obj = m, A = delaySeconds, B = (float)style, C = detuneAmount });
            return m.LengthBeats * BeatSeconds(style);
        }

        /// <summary>
        /// Play several word motifs back to back. Correct answers resolve into a consonant chord,
        /// wrong ones bend progressively out of tune. Returns total seconds until the phrase ends.
        /// </summary>
        public float PlayPhrase(Motif[] motifs, PhraseStyle style, float delaySeconds = 0f)
        {
            if (motifs == null || motifs.Length == 0) return 0f;
            float t = delaySeconds;
            float gap = style == PhraseStyle.Reveal ? 0.18f : 0.06f;
            for (int i = 0; i < motifs.Length; i++)
            {
                float detune = style == PhraseStyle.Wrong ? (i + 1f) / motifs.Length : 0f;
                t += PlayMotif(motifs[i], style, t, detune) + gap;
                if (style == PhraseStyle.Wrong) t += 0.04f * i; // the phrase drags as it falls apart
            }
            var mode = motifs[0].Mode ?? LanguageMode.Alien;
            if (style == PhraseStyle.Correct)
                PlayChord(mode.WarmChord(), PadKind.Warm, 0.06f, 0.5f, 1.6f, 0.5f, t);
            else if (style == PhraseStyle.Reveal)
                PlayChord(mode.WarmChord(), PadKind.Warm, 1.2f, t + 0.6f, 2.5f, 0.42f, delaySeconds);
            return t;
        }

        /// <summary>Sustained chord on the pad bus.</summary>
        public void PlayChord(int[] midis, PadKind kind, float attack, float hold, float release, float gain, float delaySeconds = 0f)
        {
            if (midis == null) return;
            for (int i = 0; i < midis.Length; i++)
            {
                float pan = midis.Length > 1 ? Dsp.Lerp(-0.6f, 0.6f, i / (midis.Length - 1f)) : 0f;
                Enqueue(new Command { Type = Cmd.Chord, I = (int)kind, A = Dsp.MidiToHz(midis[i]), B = attack, C = hold, D = release, E = gain / (float)Math.Sqrt(midis.Length), F = delaySeconds, Obj = pan });
            }
        }

        /// <summary>Play a prerendered stinger on the 2D bus.</summary>
        public void PlaySample(float[] buffer, float gain = 1f, float pan = 0f, float delaySeconds = 0f, float send = 0.2f, float rate = 1f)
        {
            if (buffer == null) return;
            Enqueue(new Command { Type = Cmd.Sample, Obj = buffer, A = gain, B = pan, C = delaySeconds, D = send, E = rate });
        }

        /// <summary>Death: cut to near silence, then the martyr's choir.</summary>
        public void TriggerDeath() => Enqueue(new Command { Type = Cmd.Death });

        /// <summary>Escape: the dissonance resolves, the rain falls away, a luminous progression. shipPower may be null.</summary>
        public void TriggerEscape(float[] shipPower) => Enqueue(new Command { Type = Cmd.Escape, Obj = shipPower });

        /// <summary>Back to the title state (audition mode uses this to replay the arc).</summary>
        public void ResetScore() => Enqueue(new Command { Type = Cmd.Reset });

        void Enqueue(Command c)
        {
            lock (_lock)
            {
                int next = (_qTail + 1) % _queue.Length;
                if (next == _qHead) return; // full: drop rather than block the game
                _queue[_qTail] = c;
                _qTail = next;
            }
        }

        // ================================================================== rendering

        /// <summary>Offline render (clears the buffer first).</summary>
        public void Render(float[] interleaved, int channels)
        {
            Array.Clear(interleaved, 0, interleaved.Length);
            Process(interleaved, channels);
        }

        /// <summary>Audio thread entry point. Adds the score into data (interleaved).</summary>
        public void Process(float[] data, int channels)
        {
            Drain();
            int frames = data.Length / channels;
            int f = 0;
            float peak = 0f;
            while (f < frames)
            {
                int n = Math.Min(32, frames - f);
                Control(n);
                for (int k = 0; k < n; k++, f++)
                {
                    RenderFrame(out float l, out float r);
                    int o = f * channels;
                    if (channels == 1) data[o] += 0.5f * (l + r);
                    else
                    {
                        data[o] += l; data[o + 1] += r;
                        for (int c = 2; c < channels; c++) data[o + c] += 0f;
                    }
                    float a = Math.Max(Math.Abs(l), Math.Abs(r));
                    if (a > peak) peak = a;
                }
            }
            LastPeak = peak;
        }

        void Drain()
        {
            while (true)
            {
                Command c;
                lock (_lock)
                {
                    if (_qHead == _qTail) return;
                    c = _queue[_qHead];
                    _queue[_qHead].Obj = null;
                    _qHead = (_qHead + 1) % _queue.Length;
                }
                Execute(c);
            }
        }

        void Execute(Command c)
        {
            switch (c.Type)
            {
                case Cmd.Motif: ScheduleMotif((Motif)c.Obj, (PhraseStyle)(int)c.B, c.A, c.C); break;
                case Cmd.Chord: StartPad((PadKind)c.I, c.A, c.B, c.C, c.D, c.E, (float)c.Obj, Sec(c.F), 0.5f); break;
                case Cmd.Sample: StartSample((float[])c.Obj, c.A, c.B, Sec(c.C), c.D, c.E); break;
                case Cmd.Death: BeginEnding(Mood.Death, null); break;
                case Cmd.Escape: BeginEnding(Mood.Escape, (float[])c.Obj); break;
                case Cmd.Reset: DoReset(); break;
            }
        }

        int Sec(float seconds) => (int)(Math.Max(0f, seconds) * _sr);

        // ------------------------------------------------------------------ control rate (every 32 samples)

        void Control(int n)
        {
            float blockSec = n / _sr;
            _level = _targetLevel; _heat = _targetHeat;

            // Mood requests are quantised to the beat grid by the scheduler.
            var want = (Mood)_targetMood;
            if (_endT < 0f && want != Mood.Death && want != Mood.Escape)
                _sched.Request(want, _step);

            // Start any scheduled notes that fall inside this block.
            for (int i = 0; i < _noteCount; i++)
            {
                if (_notes[i].At > _sample + n) continue;
                var nt = _notes[i];
                StartVoice(nt.Kind, nt.Hz, nt.Dur, nt.Vel, nt.Bend, nt.Pan, nt.Send, (int)Math.Max(0, nt.At - _sample));
                _notes[i] = _notes[--_noteCount];
                i--;
            }

            UpdateTargets(blockSec);
            if (_endT >= 0f) EndingTimeline(blockSec);

            // Unsettling: the score breathes, with long silences where only the rain is left.
            _breathTimer -= blockSec;
            if (_breathTimer <= 0f)
            {
                bool silent = _mood == Mood.Unsettling && _breathTarget > 0.5f && _rng.Chance(0.45f);
                _breathTarget = silent ? 0.06f : 1f;
                _breathTimer = silent ? _rng.Range(5f, 9f) : _rng.Range(9f, 18f);
            }
            if (_mood != Mood.Unsettling) _breathTarget = 1f;

            // Groans: sparse distant metal.
            _groanTimer -= blockSec;
            if (_groanTimer <= 0f)
            {
                float g = _gain[(int)L.Groan].Value;
                if (g > 0.05f) StartGroan();
                _groanTimer = _mood == Mood.Tense ? _rng.Range(5f, 10f) : _rng.Range(7f, 15f);
            }

            // Droplets on the blood sea.
            float amb = _gain[(int)L.Ambience].Value;
            _dropTimer -= blockSec;
            while (_dropTimer <= 0f)
            {
                if (amb > 0.02f)
                    Perc(0, _rng.Range(1800f, 5200f), 0f, 0f, 0.0008f, _rng.Range(0.01f, 0.03f), 1f, 0f, 1000f, 1f,
                        _rng.Range(0.02f, 0.06f) * amb, _rng.Bi() * 0.9f, 0.3f);
                _dropTimer += _rng.Range(0.01f, 0.08f);
            }

            // Motif ducking: dense layers back off while words are spoken.
            _motifActivity = 0f;
            foreach (var v in _voices) if (v.On) { _motifActivity = 1f; break; }

            // Cluster glide and filters.
            for (int i = 0; i < 5; i++) _clHz[i] += (_clTarget[i] - _clHz[i]) * Math.Min(1f, blockSec / _clGlide);
            float clCut = 900f + 2600f * _level;
            _clFL.Set(clCut, 0.8f, _sr); _clFR.Set(clCut * 1.07f, 0.8f, _sr);
            _shF.Set(2600f + 900f * _heat, 2.2f, _sr);
            _riserF.Set(250f * (float)Math.Pow(2.0, 4.6f * Math.Max(_level, RiserCycle())), 1.6f, _sr);
            _droneLfo += blockSec * 0.05f;
            _droneF.Set(160f + 120f * Dsp.Sin(_droneLfo) + 500f * _level, 0.9f, _sr);
            _bassF.Set(300f + 1400f * _bassEnv, 1.8f, _sr);
            _seaLpL.SetCutoff(140f + 380f * Wave(0f), _sr);
            _seaLpR.SetCutoff(140f + 380f * Wave(0.37f), _sr);
            _ambT += blockSec;
        }

        float Wave(float offset) => Dsp.Clamp01(0.5f + 0.35f * Dsp.Sin(_ambT / 9.3f + offset) + 0.15f * Dsp.Sin(_ambT / 3.7f + offset * 2f));

        /// <summary>Tense: noise swells across each 4-bar phrase and drops back on the downbeat.</summary>
        float RiserCycle() => _mood == Mood.Tense ? ((_step % 64) + _stepPhase) / 64f * 0.85f : 0f;

        void UpdateTargets(float blockSec)
        {
            if (_endT < 0f) _mood = _sched.Current;
            int row = (int)_mood;
            for (int i = 0; i < LayerCount; i++) _gainTarget[i] = MoodMix[row, i];

            // Continuous shaping inside a mood: the closer the beast, the more of everything.
            if (_mood == Mood.Tense)
            {
                _gainTarget[(int)L.Riser] *= 0.4f + 0.6f * _heat + 0.6f * RiserCycle();
                _gainTarget[(int)L.Cluster] *= 0.7f + 0.3f * _heat;
            }
            if (_mood == Mood.Unsettling)
            {
                _gainTarget[(int)L.Cluster] *= _heat;
            }

            // Tempo per mood; panic keeps climbing.
            switch (_mood)
            {
                case Mood.Title: _bpmTarget = 50f; break;
                case Mood.Unsettling: _bpmTarget = 52f; break;
                case Mood.Tense: _bpmTarget = 66f + 52f * _heat; break;
                case Mood.Panic: _bpmTarget = 140f + Math.Min(38f, _panicClimb * 4f) + 6f * _heat; break;
                case Mood.Death: _bpmTarget = 40f; break;
                case Mood.Escape: _bpmTarget = 84f; break;
            }
            _shepRate = _mood == Mood.Panic ? 0.11f + 0.05f * _heat : _mood == Mood.Tense ? 0.03f : 0f;
        }

        // ------------------------------------------------------------------ the 16th-note sequencer

        void OnStep()
        {
            _step++;
            if (_endT < 0f && _sched.OnStep(_step)) OnMoodChanged(_sched.Current);

            bool beat = _step % 4 == 0, bar = _step % 16 == 0;
            float beatSec = 60f / Math.Max(_bpm.Value, 20f);

            if (bar)
            {
                int barIndex = (int)(_step / 16);
                if (barIndex % 2 == 0 && (_mood == Mood.Tense || _mood == Mood.Panic || _mood == Mood.Unsettling)) SetCluster(barIndex / 2, false);
                if (_mood == Mood.Panic) _panicClimb = Math.Min(_panicClimb + 0.25f, 9.5f);
            }

            switch (_mood)
            {
                case Mood.Unsettling:
                    if (beat && _rng.Chance(0.32f))
                        Thump(_rng.Range(0f, 0.25f) * beatSec, 0.7f + 0.3f * _rng.Next01());
                    break;

                case Mood.Tense:
                    if (beat)
                    {
                        float hg = _gain[(int)L.Heart].Value * Trim[(int)L.Heart];
                        Heartbeat(0, hg, Math.Min(0.2f, 0.3f * beatSec));
                    }
                    if (_step % 2 == 0)
                    {
                        int pos = (int)(_step % 16);
                        bool accent = pos == 0 || pos == 6 || pos == 12; // 3+3+2 feel
                        if (accent || _rng.Chance(0.8f)) Tick(accent ? 1f : 0.55f, accent ? 3400f : 2800f);
                    }
                    if (beat && _rng.Chance(0.06f)) Thump(0, 0.5f);
                    break;

                case Mood.Panic:
                {
                    float dg = _gain[(int)L.Drive].Value * Trim[(int)L.Drive];
                    if (beat) Kick(dg);
                    if (_step % 8 == 4) Snare(dg * 0.55f);
                    BassStep((int)(_step % 16), dg);
                    Tick(_step % 4 == 0 ? 1f : 0.5f, _step % 2 == 0 ? 3600f : 5200f);
                    if (beat)
                    {
                        float hg = _gain[(int)L.Heart].Value * Trim[(int)L.Heart];
                        if (hg > 0.01f) Heartbeat(0, hg, 0.12f);
                    }
                    break;
                }
            }
        }

        void OnMoodChanged(Mood m)
        {
            _mood = m;
            // Crossfade speed: escalation is quick, calming down is slow.
            float t = m == Mood.Panic ? 1.2f : m == Mood.Tense ? 2.2f : 4f;
            for (int i = 0; i < LayerCount; i++) _gain[i].SetTime(t, _sr);
            if (m != Mood.Panic) _panicClimb = Math.Max(0f, _panicClimb - 4f);
            if (m == Mood.Tense || m == Mood.Panic) SetCluster((int)(_step / 32) + 1, false);
        }

        // Cluster chords: dissonant stacks around D3. Panic transposes up with the climb.
        static readonly int[][] ClusterSets =
        {
            new[] { 0, 1, 6, 7, 13 },
            new[] { -2, 1, 3, 4, 10 },
            new[] { 0, 2, 3, 8, 9 },
            new[] { -1, 0, 5, 6, 11 },
        };

        void SetCluster(int index, bool snap)
        {
            _clusterSet = ((index % ClusterSets.Length) + ClusterSets.Length) % ClusterSets.Length;
            var set = ClusterSets[_clusterSet];
            float shift = _mood == Mood.Panic ? _panicClimb : 0f;
            for (int i = 0; i < 5; i++)
            {
                _clTarget[i] = Dsp.MidiToHz(50 + set[i] + shift);
                if (snap) _clHz[i] = _clTarget[i];
            }
            _clGlide = 0.8f;
        }

        // ------------------------------------------------------------------ percussion helpers

        void Perc(int delay, float f0, float f1, float sweep, float att, float dec, float tone, float noise, float noiseHz, float noiseQ, float gain, float pan, float send)
        {
            if (gain < 1e-4f) return;
            Perc slot = null;
            foreach (var p in _perc) if (!p.On) { slot = p; break; }
            if (slot == null) return; // pool exhausted: drop the quietest kind of sound (it is a droplet or tick)
            slot.Start(_sr, delay, f0, f1, sweep, att, dec, tone, noise, noiseHz, noiseQ, gain, pan, send);
        }

        void Thump(float delaySec, float vel)
        {
            float g = _gain[(int)L.Thump].Value * Trim[(int)L.Thump] * _breathS.Value * vel;
            Perc(Sec(delaySec), 72f, 44f, 9f, 0.004f, 0.55f, 1f, 0.5f, 140f, 0.7f, g, _rng.Range(-0.3f, 0.3f), SendAmt[(int)L.Thump]);
        }

        void Heartbeat(float delaySec, float g, float dubDelay)
        {
            Perc(Sec(delaySec), 62f, 36f, 30f, 0.004f, 0.11f, 1f, 0.25f, 120f, 0.7f, g, 0f, 0.12f);
            Perc(Sec(delaySec + dubDelay), 74f, 42f, 30f, 0.004f, 0.09f, 1f, 0.2f, 140f, 0.7f, g * 0.65f, 0f, 0.12f);
        }

        void Tick(float vel, float hz)
        {
            float g = _gain[(int)L.Tick].Value * Trim[(int)L.Tick] * vel;
            Perc(0, hz, hz, 0f, 0.0005f, 0.018f, 0.35f, 1f, hz, 5f, g, _rng.Range(-0.4f, 0.4f), SendAmt[(int)L.Tick]);
        }

        void Kick(float g) => Perc(0, 160f, 46f, 28f, 0.002f, 0.24f, 1f, 0.15f, 2000f, 0.7f, g, 0f, 0.05f);

        void Snare(float g) => Perc(0, 210f, 170f, 20f, 0.001f, 0.12f, 0.35f, 1.4f, 2300f, 0.8f, g * 0.6f, 0.1f, 0.25f);

        static readonly int[] BassPattern = { 0, 0, 12, 0, 1, 0, 12, 0, 0, 0, 12, 0, 1, 3, 1, -2 };

        void BassStep(int pos, float g)
        {
            float midi = 38 + BassPattern[pos] + _panicClimb;
            _bassHz = Dsp.MidiToHz(midi);
            _bassEnv = 1f;
            _bassEnvK = (float)Math.Exp(-4.0 / (0.13 * _sr));
        }

        void StartGroan()
        {
            foreach (var g in _groans)
            {
                if (g.On) continue;
                g.Start(_sr, _rng.Range(48f, 98f), _rng.Range(2f, 5f), _rng.Range(3.5f, 6.5f), _rng.Range(-0.85f, 0.85f), ref _rng);
                return;
            }
        }

        // ------------------------------------------------------------------ melodic voices

        void ScheduleMotif(Motif m, PhraseStyle style, float delaySec, float detune)
        {
            if (m?.Notes == null) return;
            float beat = BeatSeconds(style);
            var kind = style == PhraseStyle.Reveal ? VoiceKind.Warm : KindFor(m.Mode?.Timbre ?? Timbre.Glass);
            float vel = style == PhraseStyle.Reveal ? 0.42f : 0.5f;
            long baseAt = _sample + Sec(delaySec);
            for (int i = 0; i < m.Notes.Length; i++)
            {
                var n = m.Notes[i];
                float hz = Dsp.MidiToHz(n.Midi);
                float dur = n.Length * beat * (style == PhraseStyle.Reveal ? 1.1f : 0.9f);
                long at = baseAt + Sec(n.Start * beat);
                float pan = Dsp.Clamp((n.Midi - 72) / 24f, -0.5f, 0.5f);
                // Wrong answers bend downward and grow a sour shadow voice a semitone away.
                float bend = style == PhraseStyle.Wrong ? -detune * (0.6f + 0.25f * i) : 0f;
                AddNote(at, kind, hz, dur, n.Velocity * vel, bend, pan, style == PhraseStyle.Reveal ? 0.55f : 0.3f);
                if (style == PhraseStyle.Wrong && detune > 0.2f)
                    AddNote(at + Sec(0.02f), VoiceKind.Glass, hz * (float)Math.Pow(2.0, (1f + 0.3f * detune) / 12f), dur, n.Velocity * vel * 0.6f * detune, bend * 1.5f, -pan, 0.3f);
                if (style == PhraseStyle.Reveal && i == 0)
                    AddNote(at, VoiceKind.Bell, hz * 0.5f, dur * 2f, 0.18f, 0f, 0f, 0.6f);
            }
        }

        static VoiceKind KindFor(Timbre t)
        {
            switch (t)
            {
                case Timbre.Koto: return VoiceKind.Koto;
                case Timbre.Flute: return VoiceKind.Flute;
                case Timbre.Guitar: return VoiceKind.Guitar;
                default: return VoiceKind.Glass;
            }
        }

        void AddNote(long at, VoiceKind kind, float hz, float dur, float vel, float bend, float pan, float send)
        {
            if (_noteCount >= _notes.Length) return;
            _notes[_noteCount++] = new Note { At = at, Kind = kind, Hz = hz, Dur = dur, Vel = vel, Bend = bend, Pan = pan, Send = send };
        }

        void StartVoice(VoiceKind kind, float hz, float dur, float vel, float bend, float pan, float send, int delay)
        {
            Voice slot = null;
            foreach (var v in _voices) if (!v.On) { slot = v; break; }
            if (slot == null)
            {
                // steal the oldest
                foreach (var v in _voices) if (slot == null || v.Age < slot.Age) slot = v;
            }
            slot.Start(_sr, delay, kind, hz, dur, vel, bend, pan, send, ref _rng, ++_voiceAge);
        }

        void StartPad(PadKind kind, float hz, float att, float hold, float rel, float gain, float pan, int delay, float send)
        {
            Pad slot = null;
            foreach (var p in _pads) if (!p.On) { slot = p; break; }
            if (slot == null) return;
            slot.Start(_sr, delay, kind, hz, att, hold, rel, gain, pan, send, ref _rng);
        }

        void StartSample(float[] buf, float gain, float pan, int delay, float send, float rate)
        {
            foreach (var p in _players)
            {
                if (p.On) continue;
                p.Start(buf, delay, gain, pan, send, rate <= 0f ? 1f : rate);
                return;
            }
        }

        void DoReset()
        {
            _endT = -1f;
            _sched.Reset();
            _mood = Mood.Title;
            _master.SetTime(0.5f, _sr);
            for (int i = 0; i < LayerCount; i++) _gain[i].SetTime(2f, _sr);
            foreach (var p in _pads) p.On = false;
            foreach (var p in _players) p.On = false;
            foreach (var v in _voices) v.On = false;
            _noteCount = 0;
            _panicClimb = 0f;
            _rootShift = 0f;
            _clusterSet = 0;
            SetCluster(0, false);
        }

        // ------------------------------------------------------------------ one stereo frame

        void RenderFrame(out float outL, out float outR)
        {
            // clock
            float bpm = _bpm.Next(_bpmTarget);
            _stepPhase += bpm * 4f / 60f / _sr;
            if (_stepPhase >= 1f) { _stepPhase -= 1f; OnStep(); }

            for (int i = 0; i < LayerCount; i++) _gain[i].Next(_gainTarget[i]);
            float breath = _breathS.Next(_breathTarget);
            float duck = _duck.Next(_motifActivity > 0f ? 0.55f : 1f);
            float rootShift = _rootShiftS.Next(_rootShift);

            float dl = 0f, dr = 0f, sl = 0f, sr = 0f; // dry and reverb-send buses
            float g;

            // --- drone: detuned saws on D2 and A2, sine sub, a faint tritone that comes and goes
            g = _gain[(int)L.Drone].Value * Trim[(int)L.Drone] * (_mood == Mood.Unsettling ? breath : 1f);
            if (g > 1e-4f)
            {
                float rs = (float)Math.Pow(2.0, rootShift / 12f);
                float d2 = 73.42f * rs, aa2 = 110f * rs;
                float s0 = Osc(0, d2 * 0.9963f, true), s1 = Osc(1, d2 * 1.0041f, true);
                float s2 = Osc(2, aa2 * 0.998f, true), s3 = Osc(3, aa2 * 1.003f, true);
                float sub = Osc(4, d2 * 0.5f, false);
                float tri = Osc(5, 103.83f * rs, true) * (0.35f + 0.35f * Dsp.Sin(_ambT * 0.031f));
                float left = s0 + s2 * 0.6f + tri * 0.4f, right = s1 + s3 * 0.6f + tri * 0.4f;
                _droneF.Process((left + right) * 0.5f);
                float body = _droneF.Low;
                float side = (left - right) * 0.08f;
                float sl_ = (body + side) * g + sub * g * 0.45f, sr_ = (body - side) * g + sub * g * 0.45f;
                dl += sl_; dr += sr_;
                sl += sl_ * SendAmt[0]; sr += sr_ * SendAmt[0];
            }

            // --- cluster strings
            g = _gain[(int)L.Cluster].Value * Trim[(int)L.Cluster] * duck;
            if (g > 1e-4f)
            {
                float l = 0f, r = 0f;
                for (int i = 0; i < 5; i++)
                {
                    _clLfo[i] += (0.07f + 0.02f * i) / _sr;
                    float amp = 0.55f + 0.45f * Dsp.Sin(_clLfo[i]);
                    float vib = 1f + 0.0035f * Dsp.Sin(_ambT * (4.7f + 0.3f * i) + i);
                    float f1 = _clHz[i] * 0.997f * vib, f2 = _clHz[i] * 1.0032f * vib;
                    float a = Saw(i * 2, f1), b = Saw(i * 2 + 1, f2);
                    float pan = (i - 2) * 0.35f;
                    l += (a * (1f - pan) + b * 0.6f) * amp;
                    r += (b * (1f + pan) + a * 0.6f) * amp;
                }
                _clFL.Process(l); _clFR.Process(r);
                float ol = _clFL.Low * g, or = _clFR.Low * g;
                dl += ol; dr += or; sl += ol * SendAmt[4]; sr += or * SendAmt[4];
            }

            // --- shriek: high cluster with fast tremolo
            g = _gain[(int)L.Shriek].Value * Trim[(int)L.Shriek] * duck;
            if (g > 1e-4f)
            {
                float s = 0f;
                int[] sh = ShriekNotes;
                for (int i = 0; i < 4; i++)
                {
                    float hz = Dsp.MidiToHz(86 + sh[i] + _panicClimb) * (1f + 0.006f * Dsp.Sin(_ambT * (6f + i)));
                    s += SawN(_shPh, i, hz);
                }
                _shF.Process(s);
                float trem = 0.6f + 0.4f * Dsp.Sin(_ambT * (11f + 3f * _heat));
                float o = _shF.Band * trem * g;
                dl += o * 0.8f; dr += o; sl += o * SendAmt[8]; sr += o * SendAmt[8];
            }

            // --- shepard: endless rise
            g = _gain[(int)L.Shepard].Value * Trim[(int)L.Shepard];
            _shepPos += _shepRate / _sr;
            if (_shepPos >= 1f) _shepPos -= 1f;
            if (g > 1e-4f)
            {
                float s = 0f;
                for (int k = 0; k < 8; k++)
                {
                    float oct = k + _shepPos;              // 0..8 octaves above 27.5 Hz
                    float hz = 27.5f * (float)Math.Pow(2.0, oct);
                    float w = (oct - 4.5f) / 1.5f;
                    float amp = (float)Math.Exp(-w * w);   // bell curve over log frequency
                    _shepPh[k] += hz / _sr; if (_shepPh[k] >= 1f) _shepPh[k] -= 1f;
                    s += (Dsp.Sin(_shepPh[k]) + 0.15f * Dsp.Sin(_shepPh[k] * 3f)) * amp;
                }
                float o = s * g;
                dl += o; dr += o; sl += o * SendAmt[9]; sr += o * SendAmt[9];
            }

            // --- riser noise
            g = _gain[(int)L.Riser].Value * Trim[(int)L.Riser];
            if (g > 1e-4f)
            {
                _riserF.Process(_rng.Bi());
                float o = _riserF.Band * g;
                float w = _rng.Bi() * 0.2f;
                dl += o * (1f + w); dr += o * (1f - w); sl += o * SendAmt[6]; sr += o * SendAmt[6];
            }

            // --- bass (panic)
            g = _gain[(int)L.Drive].Value * Trim[(int)L.Drive];
            if (g > 1e-4f && _bassHz > 0f)
            {
                _bassPh += _bassHz / _sr; if (_bassPh >= 1f) _bassPh -= 1f;
                _bassEnv *= _bassEnvK;
                _bassF.Process(Dsp.Saw(_bassPh, _bassHz / _sr));
                // 2 ms attack on the gain so each new 16th swells in instead of stepping (no clicks)
                _bassAmp += (0.2f + 0.8f * _bassEnv - _bassAmp) * _bassAmpK;
                float o = _bassF.Low * _bassAmp * g * 0.7f;
                dl += o; dr += o;
            }

            // --- groans
            g = _gain[(int)L.Groan].Value * Trim[(int)L.Groan] * (_mood == Mood.Unsettling ? 0.4f + 0.6f * breath : 1f);
            foreach (var gr in _groans)
            {
                if (!gr.On) continue;
                float s = gr.Next(ref _rng) * g;
                Dsp.Pan(gr.Pan, out float pl, out float pr);
                dl += s * pl * 0.35f; dr += s * pr * 0.35f; sl += s * pl; sr += s * pr;
            }

            // --- ambience: blood rain + a heaving sea
            g = _gain[(int)L.Ambience].Value * Trim[(int)L.Ambience];
            if (g > 1e-5f)
            {
                float nl = _rng.Bi(), nr = _rng.Bi();
                float rainL = _rainLpL.Process(_rainHpL.HighPass(nl)), rainR = _rainLpR.Process(_rainHpR.HighPass(nr));
                float rainAmt = 0.75f + 0.25f * Dsp.Sin(_ambT * 0.11f);
                _brownL = _brownL * 0.995f + _rng.Bi() * 0.05f;
                _brownR = _brownR * 0.995f + _rng.Bi() * 0.05f;
                float seaL = _seaLpL.Process(_brownL) * (0.4f + Wave(0f)), seaR = _seaLpR.Process(_brownR) * (0.4f + Wave(0.37f));
                float ol = (rainL * rainAmt * 0.9f + seaL * 0.7f) * g, or = (rainR * rainAmt * 0.9f + seaR * 0.7f) * g;
                dl += ol; dr += or; sl += ol * SendAmt[10]; sr += or * SendAmt[10];
            }

            // --- percussion
            foreach (var p in _perc)
            {
                if (!p.On) continue;
                if (p.Delay > 0) { p.Delay--; continue; }
                float s = p.Next(ref _rng);
                Dsp.Pan(p.Pan, out float pl, out float pr);
                dl += s * pl; dr += s * pr; sl += s * pl * p.Send; sr += s * pr * p.Send;
            }

            // --- motif voices
            foreach (var v in _voices)
            {
                if (!v.On) continue;
                if (v.Delay > 0) { v.Delay--; continue; }
                float s = v.Next() * 0.5f;
                Dsp.Pan(v.Pan, out float pl, out float pr);
                dl += s * pl; dr += s * pr; sl += s * pl * v.Send; sr += s * pr * v.Send;
            }

            // --- pads (reveal chords, choir, escape)
            foreach (var p in _pads)
            {
                if (!p.On) continue;
                if (p.Delay > 0) { p.Delay--; continue; }
                float s = p.Next();
                Dsp.Pan(p.Pan, out float pl, out float pr);
                dl += s * pl; dr += s * pr; sl += s * pl * p.Send; sr += s * pr * p.Send;
            }

            // --- stingers
            foreach (var p in _players)
            {
                if (!p.On) continue;
                if (p.Delay > 0) { p.Delay--; continue; }
                float s = p.Next();
                Dsp.Pan(p.Pan, out float pl, out float pr);
                dl += s * pl; dr += s * pr; sl += s * pl * p.Send; sr += s * pr * p.Send;
            }

            // --- reverb + master
            _verb.Process(sl, sr, out float wl, out float wr);
            float m = _master.Next(1f);
            float mixL = (dl + wl * 0.9f) * m, mixR = (dr + wr * 0.9f) * m;
            mixL = _hpL2.HighPass(_hpL1.HighPass(mixL)); mixR = _hpR2.HighPass(_hpR1.HighPass(mixR));

            // Peak compressor into a bounded soft clip: the output can never exceed 0.95.
            float a2 = Math.Max(Math.Abs(mixL), Math.Abs(mixR));
            _compEnv = a2 > _compEnv ? a2 + (_compEnv - a2) * _attK : a2 + (_compEnv - a2) * _relK;
            const float thr = 0.5f;
            float cg = _compEnv > thr ? thr / _compEnv * (1f + (_compEnv - thr) * 0.3f) : 1f;
            if (cg > 1f) cg = 1f;
            if (cg < 0.999f) _grSum += -20.0 * Math.Log10(cg);
            _grCount++;
            outL = 0.95f * Dsp.SoftClip(mixL * cg / 0.95f);
            outR = 0.95f * Dsp.SoftClip(mixR * cg / 0.95f);
            _sample++;
        }

        static readonly int[] ShriekNotes = { 0, 1, 2, 6 };

        float Osc(int i, float hz, bool saw)
        {
            float d = hz / _sr;
            _dronePh[i] += d; if (_dronePh[i] >= 1f) _dronePh[i] -= 1f;
            return saw ? Dsp.Saw(_dronePh[i], d) : Dsp.Sin(_dronePh[i]);
        }

        float Saw(int i, float hz)
        {
            float d = hz / _sr;
            _clPh[i] += d; if (_clPh[i] >= 1f) _clPh[i] -= 1f;
            return Dsp.Saw(_clPh[i], d);
        }

        float SawN(float[] ph, int i, float hz)
        {
            float d = hz / _sr;
            ph[i] += d; if (ph[i] >= 1f) ph[i] -= 1f;
            return Dsp.Saw(ph[i], d);
        }
    }

    /// <summary>Distant metallic groan: inharmonic partials bending down, plus creaking friction noise.</summary>
    sealed class Groan
    {
        public bool On;
        public float Pan;
        float _t, _dur, _hz, _bend, _sr;
        readonly float[] _ph = new float[4];
        static readonly float[] Ratio = { 1f, 2.76f, 5.40f, 8.93f };
        static readonly float[] Amp = { 1f, 0.55f, 0.35f, 0.2f };
        Svf _creak;
        float _jit;

        public void Start(float sr, float hz, float bendSemis, float dur, float pan, ref Rng rng)
        {
            On = true; _sr = sr; _t = 0f; _hz = hz; _bend = bendSemis; _dur = dur; Pan = pan;
            for (int i = 0; i < 4; i++) _ph[i] = rng.Next01();
            _creak.Reset();
        }

        public float Next(ref Rng rng)
        {
            _t += 1f / _sr;
            if (_t >= _dur) { On = false; return 0f; }
            float u = _t / _dur;
            float f = _hz * (float)Math.Pow(2.0, -_bend * u / 12f);
            float s = 0f;
            for (int i = 0; i < 4; i++)
            {
                _ph[i] += f * Ratio[i] / _sr; if (_ph[i] >= 1f) _ph[i] -= 1f;
                s += Dsp.Sin(_ph[i]) * Amp[i] * (i == 0 ? 1f : 1f - u * 0.5f);
            }
            _jit += (rng.Bi() - _jit) * 0.002f;
            _creak.Set(f * 4.3f, 9f, _sr);
            _creak.Process(rng.Bi() * (0.4f + 4f * Math.Abs(_jit)));
            float env = Dsp.AHR(_t, _dur * 0.3f, _dur * 0.3f, _dur * 0.4f);
            return (s * 0.6f + _creak.Band * 1.4f) * env;
        }
    }
}
