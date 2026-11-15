// CORE CONTRACT. Changes here must be called out in your PR description.
using UnityEngine;

namespace Fnmw.Core
{
    /// <summary>
    /// The seam between the VR port (Fnmw.XR) and the rest of the game. On desktop nothing ever sets
    /// <see cref="Active"/>, every field stays at its default and every reader falls through to its
    /// keyboard and mouse code unchanged.
    ///
    /// In a headset, Fnmw.XR detects a running XR display at boot, sets Active, and from then on writes one
    /// frame of Touch controller input here before anything else updates (it runs at execution order -900).
    /// The game reads these fields exactly where it reads the keyboard, so the rules, screens and flow stay
    /// one-to-one; only where the input comes from and where the picture goes changes.
    /// </summary>
    public enum XrLessonStage { None, Reading, Building }

    public static class XrBridge
    {
        /// <summary>True only while the game runs in a headset. Never true in the desktop build.</summary>
        public static bool Active;

        /// <summary>The camera the eyes see through right now: the player's head-tracked camera while playing,
        /// the theater camera while a cutscene is shown on the big screen. The UI glass follows it.</summary>
        public static Transform Head;

        /// <summary>Set while a cutscene owns a camera that is not the player's (the intro). In VR it is
        /// shown on a screen in front of the player instead of being strapped to their face.</summary>
        public static bool CutscenePlaying;

        /// <summary>The UI glass hanging in front of the player (set by Fnmw.UI in VR). The laser aims at it;
        /// its rect, in its own space, maps 0..1 onto PointerUv.</summary>
        public static RectTransform Glass;

        /// <summary>Set by the death ending for its first moment in VR: the scare plays in the world, in front of
        /// the player's own tracked view, before the rest of the ending moves to the theater screen.</summary>
        public static bool HoldTheater;

        /// <summary>Degrees the player has turned with the right stick since the rig was built (snap or smooth,
        /// both directions added up). The tutorial uses it to check the turn was practised.</summary>
        public static float StickTurnDegrees;

        /// <summary>The first-run tutorial is teaching right now (set by the Game thread's TutorialCoach every frame).
        /// The controller labels then show "hold Y: skip tutorial".</summary>
        public static bool TutorialActive;

        /// <summary>The UI publishes the worked-example step for controller help.</summary>
        public static XrLessonStage LessonStage;

        /// <summary>Published by the UI every frame: the console can ask the ship for a hint right now (X on Touch).
        /// The controller labels and wrist card only offer X when this is true.</summary>
        public static bool HintOffered;

        /// <summary>Published by the UI every frame: the quiz payoff can play the decoded sentence again (X on Touch).</summary>
        public static bool SpeakOffered;

        /// <summary>Layer the UI glass should sit on (0 normally; the theater's own layer while a cutscene is up).</summary>
        public static int GlassLayer;

        // ---- roaming (PlayerController) ----
        /// <summary>Left stick, x = strafe, y = forward. Relative to where the head faces.</summary>
        public static Vector2 Move;
        public static bool Run;            // held (Shift)
        public static bool Interact;       // pressed this frame (E)
        public static bool Flashlight;     // pressed this frame (F)
        public static bool Leave;          // pressed this frame (Esc / Tab: walk away from the console, skip the intro)
        public static bool SkipHeld;       // held (Q: skip the tutorial). In VR: Y held past a tap

        // ---- screens (UiRig.ReadInput) ----
        public static int Digit = -1;
        public static bool Enter, Space, Back, Clear, Left, Right, Up, Down, Click, Replay;
        /// <summary>X on a screen, pressed this frame: the console reads it as "ask the ship" (H), the quiz payoff as
        /// "hear it again" (P). Roaming reads the same press as the lamp (Flashlight) instead.</summary>
        public static bool Hint, Speak;
        /// <summary>Where the laser meets the UI glass, 0..1 across and up. Only meaningful while HasPointer.</summary>
        public static bool HasPointer;
        public static Vector2 PointerUv;

        /// <summary>Forget everything (scene load, tests).</summary>
        public static void Reset()
        {
            Active = false; Head = null; Glass = null; CutscenePlaying = false; GlassLayer = 0;
            HoldTheater = false; StickTurnDegrees = 0f; TutorialActive = false; LessonStage = XrLessonStage.None;
            HintOffered = SpeakOffered = false;
            ClearFrame();
        }

        /// <summary>Clear the per-frame input, keeping the mode.</summary>
        public static void ClearFrame()
        {
            Move = Vector2.zero; Run = Interact = Flashlight = Leave = SkipHeld = false;
            Digit = -1; Enter = Space = Back = Clear = Left = Right = Up = Down = Click = Replay = Hint = Speak = false;
            HasPointer = false; PointerUv = Vector2.zero;
        }
    }
}
