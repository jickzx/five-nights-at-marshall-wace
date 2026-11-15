using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Fnmw.Core;
using UnityEngine;

namespace Fnmw.Voice
{
    /// <summary>What this run has measured, in the coarse buckets the backend accepts. Nothing else leaves the game.</summary>
    internal sealed class RunFacts
    {
        public int Mistakes, WrongStreak;
        public string LastConcept;
        public float LastResponseTime;
        readonly Dictionary<string, int> _misses = new Dictionary<string, int>();
        static readonly HashSet<string> Known = new HashSet<string>(LangConcepts.All);

        public int Misses(string concept) => concept != null && _misses.TryGetValue(concept, out int n) ? n : 0;

        public void Observe(AnswerEvent e)
        {
            LastResponseTime = e.ResponseTime;
            if (e.Correct) { WrongStreak = 0; return; }
            Mistakes++; WrongStreak++;
            LastConcept = e.MissedConcept != null && Known.Contains(e.MissedConcept) ? e.MissedConcept : null;
            if (LastConcept != null) _misses[LastConcept] = Misses(LastConcept) + 1;
        }

        public string Pace => LastResponseTime <= 0f ? "normal" : LastResponseTime < 12f ? "fast" : LastResponseTime < 40f ? "normal" : "slow";
        public static string Distance(float metres) => float.IsInfinity(metres) || float.IsNaN(metres) || metres > 30f ? "far" : metres > 12f ? "mid" : "near";
        public static string Aggression(float a) => a < 0.34f ? "low" : a < 0.67f ? "mid" : "high";
    }

    /// <summary>
    /// Decides when the beast may speak: grounded in a real event, never over the quiz or the tutorial.
    /// Chatty (the game's setting) adds lines when a console opens, when a sentence is solved, after every mistake, and
    /// while the player is quiet; it also shortens the cooldown. Not chatty, the beast only reacts to mistakes, rarely.
    /// </summary>
    internal sealed class DialogueBrain
    {
        public const float Cooldown = 25f, MistakeDelay = 1.2f, StageDelay = 2.5f, MaxWait = 7f;
        public const int MaxLines = 8;
        public const float ChattyCooldown = 9f, PuzzleStartDelay = 1.5f, SolvedDelay = 0.8f, StalkAfter = 30f;
        public const int ChattyMaxLines = 36;

        public struct Pending { public string Trigger; public float EarliestAt, CreatedAt; public int Stage; }

        Pending? _pending;
        float _lastLineAt = float.NegativeInfinity, _quietSince = float.NaN;
        bool _firstMistakeDone, _finalChaseDone;
        readonly HashSet<int> _stagesSaid = new HashSet<int>();
        public int Lines { get; private set; }
        public bool InFlight;
        public bool Chatty = true;
        public Pending? Waiting => _pending;
        float CooldownNow => Chatty ? ChattyCooldown : Cooldown;
        int MaxLinesNow => Chatty ? ChattyMaxLines : MaxLines;

        /// <summary>Queues a line unless a weightier one is already waiting.</summary>
        void Offer(string trigger, float delay, float now)
        {
            if (_pending != null) return;
            _pending = new Pending { Trigger = trigger, EarliestAt = now + delay, CreatedAt = now };
        }

        public void OnAnswer(AnswerEvent e, RunFacts facts, float now)
        {
            _quietSince = now;
            if (e.Correct) { if (Chatty) Offer("solved", SolvedDelay, now); return; }
            string t = null;
            if (facts.LastConcept != null && facts.Misses(facts.LastConcept) >= 2) t = "repeat_concept";
            else if (facts.WrongStreak >= 2) t = "streak";
            else if (!_firstMistakeDone) t = "first_mistake";
            else if (Chatty) t = "mistake";
            if (t != null) _pending = new Pending { Trigger = t, EarliestAt = now + MistakeDelay, CreatedAt = now };
        }

        /// <summary>A new console opened: the beast comments on the sentence.</summary>
        public void OnPuzzleStart(float now)
        {
            _quietSince = now;
            if (Chatty) Offer("puzzle_start", PuzzleStartDelay, now);
        }

        public void OnStage(int stage, float now)
        {
            if (stage < 2 || _stagesSaid.Contains(stage)) return;
            _pending = new Pending { Trigger = "stage_up", EarliestAt = now + StageDelay, CreatedAt = now, Stage = stage };
        }

        public void OnChase(float now)
        {
            if (_finalChaseDone) return;
            _pending = new Pending { Trigger = "final_chase", EarliestAt = now + StageDelay, CreatedAt = now };
        }

        public static bool PhaseAllows(GamePhase p) => p == GamePhase.Explore || p == GamePhase.Puzzle || p == GamePhase.Chase;

        /// <summary>The trigger to send now, or null. Marks one-shot triggers and starts the cooldown.</summary>
        public string Take(float now, GamePhase phase, float beastGrace)
        {
            if (float.IsNaN(_quietSince)) _quietSince = now;
            if (_pending == null && Chatty && (phase == GamePhase.Explore || phase == GamePhase.Puzzle)
                && now - Mathf.Max(_quietSince, _lastLineAt) >= StalkAfter)
                Offer("stalk", 0f, now);
            if (_pending == null) return null;
            var p = _pending.Value;
            if (now - p.CreatedAt > MaxWait) { _pending = null; if (p.Trigger == "stalk") _quietSince = now; return null; }   // a late taunt is worse than none
            if (now < p.EarliestAt || InFlight || Lines >= MaxLinesNow || now - _lastLineAt < CooldownNow) return null;
            if (!PhaseAllows(phase) || beastGrace > 0f) return null;
            _pending = null;
            _lastLineAt = now;
            Lines++;
            if (p.Trigger == "first_mistake") _firstMistakeDone = true;
            if (p.Trigger == "final_chase") _finalChaseDone = true;
            if (p.Trigger == "stage_up") _stagesSaid.Add(p.Stage);
            return p.Trigger;
        }

        public void Clear() => _pending = null;
    }

    /// <summary>One voice at a time. Native beats Ship beats Monster; a lower voice offered over a higher one is dropped.</summary>
    internal sealed class SpeechArbiter
    {
        readonly int[] _handle = new int[3];   // nonzero while a channel is preparing or playing
        static int Rank(VoiceChannel c) => c == VoiceChannel.Native ? 2 : c == VoiceChannel.Ship ? 1 : 0;

        public bool Busy(VoiceChannel c) => _handle[(int)c] != 0;

        /// <summary>True if <paramref name="c"/> may speak; the channels it outranks are returned for stopping.</summary>
        public bool Offer(VoiceChannel c, List<VoiceChannel> toStop)
        {
            for (int i = 0; i < 3; i++) if (_handle[i] != 0 && Rank((VoiceChannel)i) > Rank(c)) return false;
            for (int i = 0; i < 3; i++) if (_handle[i] != 0 && Rank((VoiceChannel)i) < Rank(c)) toStop.Add((VoiceChannel)i);
            return true;
        }

        public void Claim(VoiceChannel c, int handle) => _handle[(int)c] = handle;
        public void Release(VoiceChannel c, int handle) { if (_handle[(int)c] == handle || handle == 0) _handle[(int)c] = 0; }
        public void ReleaseAll() { for (int i = 0; i < 3; i++) _handle[i] = 0; }
    }

    /// <summary>Client-side repeat of the backend's monster-line check (defence in depth; the hint check is the engine's).</summary>
    internal static class TextGuard
    {
        static readonly Regex Chars = new Regex(@"^[A-Za-z .,'’!?\-]{1,120}$");
        static readonly string[] Banned =
        {
            "japan", "french", "france", "spanish", "spain", "german", "turkish", "turkey", "chinese", "china", "mandarin", "arabic",
            "korean", "english", "language", "gemini", "google", "elevenlabs", "openai", "claude", "remember", "http", "www",
        };

        public static bool MonsterLineOk(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (!Chars.IsMatch(text)) return false;
            if (text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length > 18) return false;
            string lower = text.ToLowerInvariant();
            foreach (var b in Banned) if (Regex.IsMatch(lower, @"\b" + b)) return false;
            return true;
        }
    }

    /// <summary>Three failures in a row open it for a minute; then one probe decides.</summary>
    internal sealed class CircuitBreaker
    {
        public const int Threshold = 3;
        public const float OpenSeconds = 60f;
        int _fails;
        float _openUntil = float.NegativeInfinity;
        bool _probing;

        public bool Allow(float now)
        {
            if (_fails < Threshold) return true;
            if (now < _openUntil || _probing) return false;
            _probing = true;     // one probe at a time
            return true;
        }

        public void Success() { _fails = 0; _probing = false; }

        public void Failure(float now)
        {
            _fails++;
            _probing = false;
            if (_fails >= Threshold) _openUntil = now + OpenSeconds;
        }

        public bool Open(float now) => _fails >= Threshold && now < _openUntil;
    }

    /// <summary>
    /// Decoded clips kept for replays, across scene reloads (not app restarts). Keyed by backend identity, the server's
    /// config version and the content, so a changed backend or voice never replays old audio under a new label; an entry
    /// remembers whether it was mock audio. Bounded by count and by total samples.
    /// </summary>
    internal static class ClipCache
    {
        public const int MaxEntries = 20;
        public const int MaxSamples = Protocol.SampleRate * 90;

        public sealed class Entry { public float[] Samples; public int Rate; public bool Mock; }
        static readonly LinkedList<(string key, Entry e)> Lru = new LinkedList<(string, Entry)>();
        static int _samples;

        public static int Count => Lru.Count;

        public static Entry Get(string key)
        {
            for (var n = Lru.First; n != null; n = n.Next)
                if (n.Value.key == key) { Lru.Remove(n); Lru.AddFirst(n); return n.Value.e; }
            return null;
        }

        public static void Put(string key, Entry e)
        {
            if (e?.Samples == null || e.Samples.Length > MaxSamples) return;
            for (var n = Lru.First; n != null; n = n.Next) if (n.Value.key == key) { _samples -= n.Value.e.Samples.Length; Lru.Remove(n); break; }
            Lru.AddFirst((key, e));
            _samples += e.Samples.Length;
            while (Lru.Count > MaxEntries || _samples > MaxSamples)
            {
                var last = Lru.Last;
                _samples -= last.Value.e.Samples.Length;
                Lru.RemoveLast();
            }
        }

        public static void Clear() { Lru.Clear(); _samples = 0; }
    }
}
