using System;
using System.Collections;
using Fnmw.Audio.Synth;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fnmw.Audio
{
    /// <summary>
    /// Hidden audition mode for demos and recordings. Press F9 (or `) to toggle, Left/Right to step,
    /// R to replay the current step. While active, the score ignores the game and plays the chosen state.
    /// From the CLI: Fnmw.Audio.AuditionMode.Instance.RunAll() walks the whole arc unattended.
    /// </summary>
    public sealed class AuditionMode : MonoBehaviour
    {
        public static AuditionMode Instance { get; private set; }
        public AudioDirector Director;

        public bool Active { get; private set; }
        public int Index { get; private set; }
        public string Caption { get; private set; } = "";
        public bool Running => _routine != null;

        Coroutine _routine;
        bool _runAll;
        GUIStyle _title, _body;

        struct Step { public string Name, Detail; public Func<IEnumerator> Run; public float Length; }
        Step[] _steps;

        void Awake()
        {
            Instance = this;
            _steps = new[]
            {
                new Step { Name = "1. Unsettling", Detail = "Detuned drones, slow irregular thumps, distant metal groans, long silences. Beast stage 1, far away.", Run = Unsettling, Length = 26f },
                new Step { Name = "2. Tense", Detail = "Heartbeat quickening as the beast closes, dissonant string clusters, rising noise, ticking 3+3+2 ostinato.", Run = Tense, Length = 26f },
                new Step { Name = "3. Panic (the chase)", Detail = "Driving pulse, shrieking high cluster, Shepard tone rising forever, tempo and pitch climbing.", Run = Panic, Length = 22f },
                new Step { Name = "4. The language: Japanese", Detail = "In scale on koto. Tile picks, a wrong answer collapsing, a right answer resolving, the reveal restated warmly.", Run = () => Language("ja"), Length = 0f },
                new Step { Name = "5. The language: French", Detail = "Whole-tone scale on glass flute.", Run = () => Language("fr"), Length = 0f },
                new Step { Name = "6. The language: Spanish", Detail = "Phrygian dominant on guitar, with grace notes.", Run = () => Language("es"), Length = 0f },
                new Step { Name = "7. Stingers", Detail = "Wrong, right, beast lunge, evolution to stage 2, evolution to stage 3, console on, static, ship power-up.", Run = Stingers, Length = 0f },
                new Step { Name = "8. The beast, in 3D", Detail = "Its vocalisations circle the listener: stage 1 chitter, stage 2 growl, stage 3 shriek. Footsteps on wet ground.", Run = Beast, Length = 0f },
                new Step { Name = "9. Death", Detail = "The panic cuts to near silence. A failing heartbeat, then a slow choir and a funeral bell. Solemn, martyr-like.", Run = Death, Length = 0f },
                new Step { Name = "10. Escape", Detail = "At lift-off the dissonance resolves into D major, the rain falls away, a wide luminous progression rises.", Run = Escape, Length = 0f },
            };
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f9Key.wasPressedThisFrame || kb.backquoteKey.wasPressedThisFrame) { if (Active) Stop(); else Play(0); }
            if (!Active) return;
            if (kb.rightArrowKey.wasPressedThisFrame) Play(Math.Min(Index + 1, _steps.Length - 1));
            if (kb.leftArrowKey.wasPressedThisFrame) Play(Math.Max(Index - 1, 0));
            if (kb.rKey.wasPressedThisFrame) Play(Index);
        }

        public int StepCount => _steps.Length;

        /// <summary>Start audition mode at a step (0-based).</summary>
        public void Play(int index)
        {
            _runAll = false;
            Begin(index);
        }

        /// <summary>Walk every step back to back (for recordings).</summary>
        public void RunAll()
        {
            Begin(0);
            _runAll = true;
        }

        void Begin(int index)
        {
            Active = true;
            Index = Mathf.Clamp(index, 0, _steps.Length - 1);
            if (_routine != null) StopCoroutine(_routine);
            Director.Spatial.StopOrbit();
            Director.Spatial.Unsilence();
            Director.ResetForAudition();
            Director.OverrideActive = true;
            Caption = _steps[Index].Name;
            _routine = StartCoroutine(RunStep());
        }

        IEnumerator RunStep()
        {
            yield return _steps[Index].Run();
            if (_steps[Index].Length > 0f) yield return new WaitForSecondsRealtime(0.01f);
            _routine = null;
            if (_runAll && Index < _steps.Length - 1) Begin(Index + 1);
            else if (_runAll) _runAll = false;
        }

        public void Stop()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            _runAll = false;
            Active = false;
            Director.Spatial.StopOrbit();
            Director.Spatial.Unsilence();
            Director.ResetForAudition();
            Director.OverrideActive = false;
        }

        // ------------------------------------------------------------ steps

        void Set(Mood m, float level, int stage)
        {
            Director.OverrideMood = m; Director.OverrideLevel = level; Director.OverrideStage = stage;
        }

        IEnumerator Ramp(Mood m, int stage, float from, float to, float seconds)
        {
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
            {
                Set(m, Mathf.Lerp(from, to, t / seconds), stage);
                yield return null;
            }
        }

        IEnumerator Unsettling() { yield return Ramp(Mood.Unsettling, 1, 0.05f, 0.3f, 26f); }
        IEnumerator Tense() { yield return Ramp(Mood.Tense, 2, 0.4f, 0.7f, 26f); }
        IEnumerator Panic() { yield return Ramp(Mood.Panic, 3, 0.8f, 1f, 22f); }

        IEnumerator Language(string code)
        {
            Set(Mood.Unsettling, 0.1f, 1);
            yield return new WaitForSecondsRealtime(1f);
            float len = Director.DemoLanguage(code, 0f);
            yield return new WaitForSecondsRealtime(len + 0.5f);
        }

        IEnumerator Stingers()
        {
            Set(Mood.Title, 0f, 1);
            string[] names = { "wrong", "correct", "beast_lunge", "stage_up_2", "stage_up_3", "console_on", "static", "ship_power" };
            float[] gaps = { 2.2f, 2.4f, 2.0f, 3.6f, 4.4f, 1.8f, 1.4f, 4f };
            yield return new WaitForSecondsRealtime(0.6f);
            for (int i = 0; i < names.Length; i++)
            {
                Caption = $"{_steps[Index].Name}: {names[i]}";
                string n = names[i];
                if (n == "beast_lunge") Director.Engine.PlaySample(Director.Sfx.Get(n), 0.8f);
                else if (n.StartsWith("stage_up")) Director.Engine.PlaySample(Director.Sfx.Get(n), 0.8f, 0f, 0f, 0.45f);
                else Director.PlayStinger(n);
                yield return new WaitForSecondsRealtime(gaps[i]);
            }
            Caption = _steps[Index].Name;
        }

        IEnumerator Beast()
        {
            Set(Mood.Unsettling, 0.2f, 1);
            var orbit = Director.Spatial.StartOrbit();
            var listener = FindAnyObjectByType<AudioListener>();
            Transform center = listener != null ? listener.transform : Camera.main != null ? Camera.main.transform : null;
            float t = 0f;
            for (int stage = 1; stage <= 3; stage++)
            {
                Director.OverrideStage = stage;
                Caption = $"{_steps[Index].Name}: stage {stage}";
                float end = t + 7f;
                float nextVocal = t + 0.3f, nextStep = t;
                while (t < end)
                {
                    float ang = t * 0.9f;
                    Vector3 c = center != null ? center.position : Vector3.zero;
                    orbit.position = c + new Vector3(Mathf.Sin(ang) * 6f, 0f, Mathf.Cos(ang) * 6f);
                    if (t >= nextVocal) { Director.Spatial.BeastVocal(); nextVocal = t + (stage == 3 ? 2.2f : 3f); }
                    if (stage >= 2 && t >= nextStep) { Director.Spatial.BeastSay("beast_step", stage == 3 ? 1f : 0.7f, Director.Sfx.NextVariant("beast_step")); nextStep = t + 0.55f; }
                    if (stage == 1 && t >= nextStep) { Director.Spatial.PlayerStep(); nextStep = t + 0.45f; }
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            Director.Spatial.StopOrbit();
        }

        IEnumerator Death()
        {
            Set(Mood.Panic, 0.95f, 3);
            yield return new WaitForSecondsRealtime(8f);
            Caption = $"{_steps[Index].Name}: caught";
            Director.Spatial.BeastSay("beast_lunge", 1f);
            yield return new WaitForSecondsRealtime(0.35f);
            Director.PlayStinger("death");
            yield return new WaitForSecondsRealtime(ScoreEngine.DeathLength);
        }

        IEnumerator Escape()
        {
            Set(Mood.Panic, 0.95f, 3);
            yield return new WaitForSecondsRealtime(8f);
            Caption = $"{_steps[Index].Name}: lift-off";
            Director.PlayStinger("liftoff");
            yield return new WaitForSecondsRealtime(ScoreEngine.EscapeLength);
        }

        // ------------------------------------------------------------ overlay

        void OnGUI()
        {
            if (!Active) return;
            GUI.depth = -1000;
            _title ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
            _body ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            _title.fontSize = Mathf.Max(16, Screen.height / 16);
            _body.fontSize = Mathf.Max(11, Screen.height / 34);
            var r = new Rect(Screen.width * 0.05f, Screen.height * 0.62f, Screen.width * 0.9f, Screen.height * 0.34f);
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.75f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            var e = Director.Engine;
            GUI.Label(new Rect(r.x + 16, r.y + 8, r.width - 32, r.height * 0.4f), "AUDITION  " + Caption, _title);
            GUI.Label(new Rect(r.x + 16, r.y + r.height * 0.42f, r.width - 32, r.height * 0.36f), _steps[Index].Detail, _body);
            GUI.Label(new Rect(r.x + 16, r.y + r.height * 0.8f, r.width - 32, r.height * 0.2f),
                $"mood {e.CurrentMood}   {e.CurrentBpm:0} bpm   level {Director.OverrideLevel:0.00}   [F9 exit, Left/Right step, R replay]", _body);
        }
    }
}
