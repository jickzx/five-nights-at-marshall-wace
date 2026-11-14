using Fnmw.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fnmw.Game
{
    /// <summary>
    /// F1 demo mode on/off (restarts), F5 restart, F6 explore, F7 puzzle, F8 reveal, F9 quiz,
    /// F10 final chase, F11 escape ending, F12 death ending. A small corner label shows while demo mode is on.
    /// </summary>
    public class DevKeys : MonoBehaviour
    {
        GUIStyle _style;

        void Update()
        {
            var kb = Keyboard.current;
            var d = GameDirector.Instance;
            if (kb == null || d == null) return;
            if (kb.f1Key.wasPressedThisFrame) { DemoMode.On = !DemoMode.On; d.Restart(); return; }
            if (kb.f5Key.wasPressedThisFrame) d.Restart();
            if (kb.f6Key.wasPressedThisFrame) d.SkipTo(GamePhase.Explore);
            if (kb.f7Key.wasPressedThisFrame) d.SkipTo(GamePhase.Puzzle);
            if (kb.f8Key.wasPressedThisFrame) d.SkipTo(GamePhase.Reveal);
            if (kb.f9Key.wasPressedThisFrame) d.SkipTo(GamePhase.Quiz);
            if (kb.f10Key.wasPressedThisFrame) d.SkipTo(GamePhase.Chase);
            if (kb.f11Key.wasPressedThisFrame) d.SkipTo(GamePhase.Escaped);
            if (kb.f12Key.wasPressedThisFrame) d.SkipTo(GamePhase.Dead);
        }

        void OnGUI()
        {
            if (!DemoMode.On) return;
            _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold };
            _style.fontSize = Mathf.Max(10, Screen.height / 48);
            _style.normal.textColor = new Color(0.9f, 0.82f, 0.7f, 0.75f);
            GUI.Label(new Rect(Screen.width - 410, 8, 400, 30), "DEMO  ·  F5 restart  F6–F12 skip", _style);
        }
    }
}
