using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Fnmw.Core.Fakes
{
    /// <summary>
    /// IMGUI placeholder for every screen. Uses IMGUI key events (works with the Input System
    /// only setting) so Core does not depend on the Input System package. Replaced by Scripts/UI.
    /// </summary>
    public class FakeGameUI : MonoBehaviour, IGameUI
    {
        enum Screen { None, Title, Puzzle, Reveal, Quiz, End }
        Screen _screen;
        string _prompt, _feedback;
        GameState _hud;
        LangPuzzle _puzzle; Action<string[]> _onSubmit; readonly List<string> _picked = new List<string>();
        RevealInfo _reveal; Action _onContinue;
        QuizQuestion _quiz; Action<int> _onChoose;
        bool _escaped; Action _onAction;
        GUIStyle _big, _body;
        HintView _hint; SubtitleLine _subtitle; float _subtitleUntil;

        public static FakeGameUI Create()
        {
            var go = new GameObject("FakeGameUI");
            return go.AddComponent<FakeGameUI>();
        }

        public void ShowTitle(Action onStart) { _screen = Screen.Title; _onAction = onStart; }
        public void ShowHud(GameState s) => _hud = s;
        public void ShowPrompt(string text) => _prompt = text;
        public void ShowPuzzle(LangPuzzle p, Action<string[]> onSubmit) { _screen = Screen.Puzzle; _puzzle = p; _onSubmit = onSubmit; _picked.Clear(); _feedback = null; }
        public void ShowAnswerFeedback(AnswerResult r) { _feedback = r.Feedback; _picked.Clear(); }
        public void ShowReveal(RevealInfo r, Action onContinue) { _screen = Screen.Reveal; _reveal = r; _onContinue = onContinue; }
        public void ShowQuiz(QuizQuestion q, Action<int> onChoose) { _screen = Screen.Quiz; _quiz = q; _onChoose = onChoose; }
        public void ShowEnd(bool escaped, GameState s, Action onRestart) { _screen = Screen.End; _escaped = escaped; _hud = s; _onAction = onRestart; }
        public void HideAll() { _screen = Screen.None; _prompt = null; _feedback = null; _hint = default; _subtitle = default; }
        public void ShowSubtitle(SubtitleLine line) { _subtitle = line; _subtitleUntil = Time.unscaledTime + Mathf.Max(0.5f, line.Seconds); }
        public void ShowHint(HintView hint) => _hint = hint;

        void OnGUI()
        {
            _big ??= new GUIStyle(GUI.skin.label) { fontSize = 36, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _body ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            _body.fontSize = Mathf.Max(11, UnityEngine.Screen.height / 36);
            _big.fontSize = Mathf.Max(16, UnityEngine.Screen.height / 18);
            var e = Event.current;
            int digit = -1; bool enter = false, back = false, hintKey = false;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9) digit = e.keyCode - KeyCode.Alpha1;
                if (e.keyCode >= KeyCode.Keypad1 && e.keyCode <= KeyCode.Keypad9) digit = e.keyCode - KeyCode.Keypad1;
                enter = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Space;
                back = e.keyCode == KeyCode.Backspace;
                hintKey = e.keyCode == KeyCode.H;
            }

            if (_hud != null && _screen != Screen.Title)
            {
                GUI.Label(new Rect(10, 10, 600, 30),
                    $"t={_hud.Elapsed:0}s  phase={_hud.Phase}  beast stage {_hud.BeastStage}  dist {_hud.BeastDistance:0.0}m  threat {_hud.Threat:0.00}  solved {_hud.PuzzlesSolved}/{_hud.TotalPuzzles}  mistakes {_hud.Mistakes}", _body);
            }
            if (!string.IsNullOrEmpty(_prompt) && _screen == Screen.None)
                GUI.Label(new Rect(0, UnityEngine.Screen.height * 0.6f, UnityEngine.Screen.width, 50), _prompt, _big);

            var panel = new Rect(UnityEngine.Screen.width * 0.15f, UnityEngine.Screen.height * 0.15f, UnityEngine.Screen.width * 0.7f, UnityEngine.Screen.height * 0.7f);
            switch (_screen)
            {
                case Screen.Title:
                    GUI.Box(panel, "");
                    GUI.Label(panel, "FIVE NIGHTS AT MARSHALL WACE\n\nBlood tide rising.\n\nPress Enter to begin", _big);
                    if (enter) Fire(ref _onAction);
                    break;

                case Screen.Puzzle:
                {
                    GUI.Box(panel, "");
                    var sb = new StringBuilder();
                    sb.AppendLine($"ALIEN CONSOLE #{_puzzle.Id}\n");
                    foreach (var ex in _puzzle.Examples)
                        sb.AppendLine($"[{ex.Picture}]   {string.Join(" ", ex.Words.ConvertAll(w => w.Alien))}");
                    sb.AppendLine($"\nSay: \"{_puzzle.TargetMeaning}\"\n");
                    for (int i = 0; i < _puzzle.Tiles.Count; i++) sb.AppendLine($"  {i + 1}) {_puzzle.Tiles[i].Alien}");
                    var pickedAlien = _picked.ConvertAll(id => _puzzle.Tiles.Find(t => t.Id == id).Alien);
                    sb.AppendLine($"\nYour phrase: {string.Join(" ", pickedAlien)}");
                    sb.AppendLine("Number keys to add a tile, Backspace to clear, Enter to submit.");
                    if (_feedback != null) sb.AppendLine($"\n{_feedback}");
                    if (_hint.Visible && _hint.PuzzleId == _puzzle.Id) sb.AppendLine(_hint.Pending ? "\nSHIP: ANALYSING..." : $"\nSHIP: {_hint.Text}");
                    else if (Services.Voice.HintsAvailable) sb.AppendLine("H asks the ship for a hint.");
                    GUI.Label(new Rect(panel.x + 20, panel.y + 20, panel.width - 40, panel.height - 40), sb.ToString(), _body);
                    if (digit >= 0 && digit < _puzzle.Tiles.Count) { _picked.Add(_puzzle.Tiles[digit].Id); GameEvents.RaiseTilePicked(_puzzle.Tiles[digit]); }
                    if (back) { _picked.Clear(); GameEvents.RaiseTilesCleared(); }
                    if (enter && _picked.Count > 0) _onSubmit?.Invoke(_picked.ToArray());
                    if (hintKey) Services.Voice.RequestHint(_puzzle.Id);
                    break;
                }

                case Screen.Reveal:
                    GUI.Box(panel, "");
                    GUI.Label(new Rect(panel.x + 20, panel.y + 20, panel.width - 40, panel.height - 40),
                        $"DECODED\n\nYou said: {_reveal.AlienText}\nReal:   {_reveal.RealText}\nMeans:  {_reveal.Translation}\n\n{_reveal.Explanation}\n\nPress Enter", _body);
                    if (enter) Fire(ref _onContinue);
                    break;

                case Screen.Quiz:
                {
                    GUI.Box(panel, "");
                    var sb = new StringBuilder(_quiz.Prompt + "\n\n");
                    for (int i = 0; i < _quiz.Options.Length; i++) sb.AppendLine($"  {i + 1}) {_quiz.Options[i]}");
                    GUI.Label(new Rect(panel.x + 20, panel.y + 20, panel.width - 40, panel.height - 40), sb.ToString(), _body);
                    if (digit >= 0 && digit < _quiz.Options.Length) { var cb = _onChoose; _onChoose = null; cb?.Invoke(digit); }
                    break;
                }

                case Screen.End:
                    GUI.Box(panel, "");
                    GUI.Label(panel, _escaped
                        ? "LIFT-OFF\n\nThe ship tears free of the blood sea.\nYou escaped.\n\nEnter to play again"
                        : "MARTYR\n\nUnfortunately, after careful consideration, we decided to move forward with other candidates.\n\nEnter to try again", _big);
                    if (enter) Fire(ref _onAction);
                    break;
            }
            if (!string.IsNullOrEmpty(_subtitle.Text) && Time.unscaledTime < _subtitleUntil)
                GUI.Label(new Rect(10, UnityEngine.Screen.height - 40, UnityEngine.Screen.width - 20, 30),
                    (_subtitle.Channel == VoiceChannel.Monster ? "IT: " : "SHIP: ") + _subtitle.Text + (_subtitle.Source == VoiceSource.Mock ? "  [MOCK]" : ""), _body);
            if (digit >= 0 || enter || back || hintKey) e.Use();
        }

        static void Fire(ref Action a) { var cb = a; a = null; cb?.Invoke(); }
    }
}
