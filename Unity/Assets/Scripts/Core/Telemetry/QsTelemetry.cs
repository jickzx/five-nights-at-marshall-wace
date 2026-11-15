using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Fnmw.Core.Telemetry
{
    /// <summary>
    /// Samples head and controller poses every frame and posts them to the QS ingest API in windows
    /// (default 1 s windows, one every 0.5 s), together with the game events that give the motion meaning.
    ///
    /// Nothing is recorded until the candidate consents: call <see cref="Consent"/> from the consent screen after
    /// showing <see cref="ConsentStatement"/> (fetched from the server, so the text the person reads is the text
    /// whose hash is stored). The first <see cref="QsConfig.CalibrationSeconds"/> after consent are the calm
    /// calibration window the backend uses as the person's own baseline.
    ///
    /// The game never waits on this: requests run in a coroutine, one at a time, and a failure only costs data.
    /// Batches wait in a short queue (MaxQueuedBatches, about 2 s) while a request is in flight; beyond that the
    /// oldest window is dropped, so a bad network never builds a backlog. Events are never dropped by the queue
    /// and are retried on failure (they carry ids, so the server drops duplicates).
    /// </summary>
    public sealed class QsTelemetry : MonoBehaviour
    {
        public enum State { Idle, FetchingConsent, AwaitingConsent, SendingConsent, Recording, Stopped }

        public static QsTelemetry Instance { get; private set; }

        public State Current { get; private set; } = State.Idle;
        public string ConsentStatement { get; private set; }
        public string LastError { get; private set; }
        public int WindowsSent { get; private set; }
        public int WindowsDropped { get; private set; }

        QsConfig _config;
        IQsPoseSource _source;
        readonly QsFrameBuffer _buffer = new QsFrameBuffer(4.0);
        readonly List<QsEvent> _pending = new List<QsEvent>();
        string _consentVersion, _consentSha;
        double _clockOrigin;
        double _nextSend;
        double _calibrationEnd = double.NaN;
        int _seq;
        bool _inFlight;
        int _framesSinceSend;
        double _rateWindowStart;
        string _question;
        int _lastPuzzleId = -1;

        const int MaxPendingEvents = 500;
        const int MaxQueuedBatches = 4;

        sealed class Batch { public int Seq; public string Question; public double TStart, TEnd; public float Rate; public List<QsFrame> Frames; public List<QsEvent> Events; }
        readonly Queue<Batch> _queue = new Queue<Batch>();

        public static QsTelemetry Create(QsConfig config, IQsPoseSource source)
        {
            if (Instance != null) Destroy(Instance.gameObject);
            var go = new GameObject("[QS]") { hideFlags = HideFlags.HideInHierarchy };
            var t = go.AddComponent<QsTelemetry>();
            t._config = config;
            t._source = source;
            Instance = t;
            return t;
        }

        /// <summary>Seconds on the session clock (monotonic, starts when the component is created).</summary>
        public double Now => Time.realtimeSinceStartupAsDouble - _clockOrigin;

        void Awake() { _clockOrigin = Time.realtimeSinceStartupAsDouble; }

        void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- consent

        /// <summary>Fetch the consent statement to show. Moves to AwaitingConsent when it arrives.</summary>
        public void BeginConsent()
        {
            if (Current != State.Idle) return;
            Current = State.FetchingConsent;
            StartCoroutine(FetchConsent());
        }

        /// <summary>The candidate's answer. Declining stops the sender for this session; nothing is sent.</summary>
        public void Consent(bool accepted)
        {
            if (Current != State.AwaitingConsent) return;
            if (!accepted) { Stop("candidate declined consent"); return; }
            Current = State.SendingConsent;
            StartCoroutine(SendConsent());
        }

        IEnumerator FetchConsent()
        {
            using (var www = UnityWebRequest.Get(_config.ConsentTextUrl))
            {
                www.timeout = 10;
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success ||
                    !QsWire.TryReadConsent(www.downloadHandler.text, out _consentVersion, out var statement, out _consentSha))
                {
                    Stop("could not fetch the consent statement: " + www.error);
                    yield break;
                }
                ConsentStatement = statement;
                Current = State.AwaitingConsent;
            }
        }

        IEnumerator SendConsent()
        {
            string body = QsWire.Consent(_consentVersion, _consentSha, SystemInfo.deviceModel, Application.version);
            using (var www = Post(_config.ConsentUrl, body))
            {
                yield return www.SendWebRequest();
                if (www.responseCode != 201)
                {
                    Stop("consent was not recorded (HTTP " + www.responseCode + ")");
                    yield break;
                }
            }
            StartRecording();
        }

        void StartRecording()
        {
            Current = State.Recording;
            _clockOrigin = Time.realtimeSinceStartupAsDouble;
            _buffer.Clear();
            _nextSend = _config.WindowSeconds;
            _rateWindowStart = 0;
            _calibrationEnd = _config.CalibrationSeconds;
            Log("session_start", null, QsWire.Payload(("device", SystemInfo.deviceModel), ("source", _source.Name)), 0);
            Log("calibration_start", null, null, 0);
            Subscribe();
            Debug.Log("[QS] recording: source " + _source.Name + ", calibration " + _config.CalibrationSeconds + " s");
        }

        void Stop(string why)
        {
            if (Current == State.Stopped) return;
            Current = State.Stopped;
            LastError = why;
            Unsubscribe();
            Debug.Log("[QS] stopped: " + why);
        }

        // ---------------------------------------------------------------- sampling and sending

        void Update()
        {
            if (Current != State.Recording) return;
            double now = Now;
            _buffer.Add(_source.Sample(now));
            _framesSinceSend++;

            if (!double.IsNaN(_calibrationEnd) && now >= _calibrationEnd)
            {
                Log("calibration_end", null, null, now);
                _calibrationEnd = double.NaN;
            }

            if (now < _nextSend) return;
            double tEnd = _nextSend, tStart = tEnd - _config.WindowSeconds;
            _nextSend += _config.HopSeconds;
            if (_nextSend < now) _nextSend = now + _config.HopSeconds;   // after a hitch, skip ahead rather than burst

            float rate = (float)(_framesSinceSend / Math.Max(1e-3, now - _rateWindowStart));
            _framesSinceSend = 0;
            _rateWindowStart = now;

            var frames = _buffer.Window(tStart, tEnd);
            var events = new List<QsEvent>(_pending);
            _pending.Clear();
            if (frames.Count == 0 && events.Count == 0) return;
            _queue.Enqueue(new Batch { Seq = _seq, Question = _question, TStart = tStart, TEnd = tEnd, Rate = rate, Frames = frames, Events = events });
            if (frames.Count > 0) _seq++;
            while (_queue.Count > MaxQueuedBatches)
            {
                // Too far behind: drop the oldest window but carry its events forward.
                var old = _queue.Dequeue();
                if (old.Frames.Count > 0) WindowsDropped++;
                if (_queue.Count > 0) _queue.Peek().Events.InsertRange(0, old.Events); else _pending.InsertRange(0, old.Events);
            }
            if (!_inFlight) SendNext();
        }

        void SendNext()
        {
            if (_inFlight || _queue.Count == 0 || Current != State.Recording) return;
            var b = _queue.Dequeue();
            string body = QsWire.Batch(b.Seq, b.Question, b.TStart, b.TEnd, b.Rate, b.Frames, _source.HasControllers, b.Events);
            bool ending = b.Events.Exists(e => e.Type == "session_end");
            StartCoroutine(Send(body, b.Frames.Count > 0, b.Events, ending));
        }

        IEnumerator Send(string body, bool hasWindow, List<QsEvent> events, bool ending)
        {
            _inFlight = true;
            using (var www = Post(_config.IngestUrl, body))
            {
                yield return www.SendWebRequest();
                _inFlight = false;
                long code = www.responseCode;
                if (code == 202)
                {
                    if (hasWindow) WindowsSent++;
                    if (ending) { Stop("session ended"); yield break; }
                    SendNext();
                    yield break;
                }
                if (code == 401 || code == 403 || code == 409)
                {
                    Stop("server refused data (HTTP " + code + ")");   // bad token, no consent, or session ended
                    yield break;
                }
                // Network trouble or a 5xx: keep the events for the next batch (ids make the retry safe).
                if (hasWindow) WindowsDropped++;
                if (_pending.Count + events.Count <= MaxPendingEvents) _pending.InsertRange(0, events);
                LastError = "send failed: " + (string.IsNullOrEmpty(www.error) ? "HTTP " + code : www.error);
            }
            SendNext();
        }

        UnityWebRequest Post(string url, string json)
        {
            var www = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 10,
            };
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("X-QS-Ingest-Token", _config.IngestToken);
            return www;
        }

        // ---------------------------------------------------------------- game events

        /// <summary>Queue an event at the current session time. Also usable by game code for events GameEvents lacks.</summary>
        public void Log(string type, string questionKey, string payloadJson = null) => Log(type, questionKey, payloadJson, Now);

        void Log(string type, string questionKey, string payloadJson, double t)
        {
            if (Current != State.Recording) return;
            if (_pending.Count >= MaxPendingEvents) _pending.RemoveAt(0);
            _pending.Add(new QsEvent { T = t, Type = type, QuestionKey = questionKey, PayloadJson = payloadJson });
        }

        bool _subscribed;

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEvents.PuzzleStarted += OnPuzzleStarted;
            GameEvents.AnswerSubmitted += OnAnswer;
            GameEvents.QuizAnswered += OnQuiz;
            GameEvents.TilePicked += OnTile;
            GameEvents.TilesCleared += OnCleared;
            GameEvents.BeastStageChanged += OnStage;
            GameEvents.PhaseChanged += OnPhase;
            GameEvents.PlayerCaught += OnCaught;
        }

        void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            GameEvents.PuzzleStarted -= OnPuzzleStarted;
            GameEvents.AnswerSubmitted -= OnAnswer;
            GameEvents.QuizAnswered -= OnQuiz;
            GameEvents.TilePicked -= OnTile;
            GameEvents.TilesCleared -= OnCleared;
            GameEvents.BeastStageChanged -= OnStage;
            GameEvents.PhaseChanged -= OnPhase;
            GameEvents.PlayerCaught -= OnCaught;
        }

        void OnPuzzleStarted(LangPuzzle p)
        {
            if (p == null) return;
            _lastPuzzleId = p.Id;
            _question = QsWire.PuzzleKey(p.Id);
            Log("question_shown", _question, QsWire.Payload(("concept", p.Concept), ("difficulty", p.Difficulty)));
        }

        void OnTile(Tile t) => Log("tile_placed", _question);
        void OnCleared() => Log("answer_cleared", _question);

        void OnAnswer(AnswerEvent e)
        {
            string key = QsWire.PuzzleKey(e.PuzzleId);
            Log("answer_submitted", key, QsWire.Payload(("correct", e.Correct), ("missed_concept", e.MissedConcept)));
            if (!e.Correct) Log("answer_wrong_fx", key);   // the glass cracks and bleeds: a sudden stimulus
            else if (_question == key) _question = null;
        }

        void OnQuiz(QuizEvent e)
        {
            string key = QsWire.PuzzleKey(e.PuzzleId) + "-quiz";
            // The quiz is one-shot: the game moves on whatever the answer, so this answer closes the question.
            Log("answer_submitted", key, QsWire.Payload(("correct", e.Correct), ("final", true)));
            if (_question == key) _question = null;
        }

        void OnStage(int stage) => Log("monster_stage", _question, QsWire.Payload(("stage", stage)));
        void OnCaught() => Log("death", _question);

        void OnPhase(GamePhase from, GamePhase to)
        {
            // Leaving a console without solving it (walking away, or the chase starting) closes the question.
            // A correct answer goes on to Reveal; being caught is reported as death.
            if (from == GamePhase.Puzzle && to != GamePhase.Reveal && to != GamePhase.Dead && _question != null)
            {
                Log("question_abandoned", _question);
                _question = null;
            }
            if (to == GamePhase.Quiz && _lastPuzzleId >= 0)
            {
                _question = QsWire.PuzzleKey(_lastPuzzleId) + "-quiz";
                Log("question_shown", _question);
            }
            if (to == GamePhase.Chase) Log("chase_start", _question);
            if (from == GamePhase.Chase) Log("chase_end", _question);
            if (to == GamePhase.Escaped || to == GamePhase.Dead) Log("session_end", null, QsWire.Payload(("result", to.ToString())));
        }
    }
}
