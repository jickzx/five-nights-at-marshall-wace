using Fnmw.Core;

namespace Fnmw.XR
{
    /// <summary>What the player is doing, as far as the controls care.</summary>
    public enum XrContext { Title, Roam, Console, Screen, Cutscene, LessonReading, LessonBuilding }

    /// <summary>
    /// The controller guide: the short labels beside each button and the wrist card, for the current context.
    /// Pure, so tests can check every context names the buttons that work there (and only those).
    /// </summary>
    public static class XrHelp
    {
        public static XrContext ContextFor(GamePhase phase, bool theater, XrLessonStage lesson = XrLessonStage.None)
        {
            if (theater) return XrContext.Cutscene;
            switch (phase)
            {
                case GamePhase.Title: return XrContext.Title;
                case GamePhase.Explore: case GamePhase.Chase: return XrContext.Roam;
                case GamePhase.Puzzle: return lesson == XrLessonStage.Building ? XrContext.LessonBuilding
                    : lesson == XrLessonStage.Reading ? XrContext.LessonReading : XrContext.Console;
                case GamePhase.Dead: case GamePhase.Escaped: return XrContext.Cutscene;
                default: return XrContext.Screen;
            }
        }

        /// <summary>Labels for the right controller: A, B, stick, trigger, grip.</summary>
        public static void RightLabels(XrContext c, bool smoothTurn, out string a, out string b, out string stick, out string trigger, out string grip)
        {
            grip = "";
            switch (c)
            {
                case XrContext.Title: a = "START"; b = ""; stick = ""; trigger = "SELECT"; return;
                case XrContext.Roam: a = "USE"; b = ""; stick = smoothTurn ? "TURN" : "SNAP TURN"; trigger = "USE"; return;
                case XrContext.LessonReading: a = "NEXT"; b = "BACK · HOLD SKIP EXAMPLE"; stick = "BACK / NEXT"; trigger = "NEXT"; return;
                case XrContext.LessonBuilding: a = "PLACE / SEND"; b = "UNDO · HOLD SKIP EXAMPLE"; stick = "BACK / NEXT"; trigger = "PLACE / SEND"; return;
                case XrContext.Console: a = "SEND"; b = "UNDO · HOLD CLEAR"; stick = "CURSOR"; trigger = "PLACE"; return;
                case XrContext.Screen: a = "OK"; b = ""; stick = "CURSOR"; trigger = "SELECT"; return;
                default: a = ""; b = ""; stick = ""; trigger = ""; return;
            }
        }

        /// <summary>Labels for the left controller: X, Y, stick, trigger, grip.</summary>
        public static void LeftLabels(XrContext c, bool tutorial, out string x, out string y, out string stick, out string trigger, out string grip)
        {
            trigger = "";
            switch (c)
            {
                case XrContext.Title: x = ""; y = "TUTORIAL"; stick = ""; grip = ""; return;
                case XrContext.Roam: x = "LAMP"; y = tutorial ? "HOLD: SKIP TUTORIAL" : ""; stick = "WALK"; grip = "RUN"; return;
                case XrContext.LessonReading:
                case XrContext.LessonBuilding: x = ""; y = tutorial ? "LEAVE · HOLD SKIP TUTORIAL" : "LEAVE"; stick = "BACK / NEXT"; grip = ""; return;
                case XrContext.Console: x = ""; y = tutorial ? "LEAVE · HOLD SKIP" : "LEAVE"; stick = "CURSOR"; grip = ""; return;
                case XrContext.Screen: x = ""; y = ""; stick = "CURSOR"; grip = ""; return;
                default: x = ""; y = ""; stick = ""; grip = ""; return;
            }
        }

        /// <summary>The wrist card: a heading and at most five short lines, big enough to read at arm's length.</summary>
        public static string Card(XrContext c, bool smoothTurn, bool tutorial)
        {
            switch (c)
            {
                case XrContext.Title:
                    return "TITLE\nA  start\nY  replay tutorial\nTrigger  select";
                case XrContext.LessonReading:
                    return "EXAMPLE\nA / trigger  next\nB  back\nHold B  skip example\nY  leave";
                case XrContext.LessonBuilding:
                    return "PRACTICE\nA / trigger  place / send\nB  undo\nHold B  skip example\nY  leave";
                case XrContext.Console:
                    return "CONSOLE\nTrigger  place tile\nA  send\nB  undo  ·  hold B  clear\nY  leave" + (tutorial ? "  ·  hold Y  skip tutorial" : "");
                case XrContext.Screen:
                    return "SCREEN\nTrigger  select\nA  ok\nSticks  move cursor";
                case XrContext.Cutscene:
                    return "CUTSCENE\nA or Y  skip (intro)";
                default:
                    return "MOVE\nLeft stick  walk  ·  grip  run\nRight stick  turn (" + (smoothTurn ? "smooth" : "snap") + ")\nA / trigger  use  ·  X  lamp\n"
                        + (tutorial ? "Hold Y  skip tutorial" : "Click right stick  snap/smooth");
            }
        }
    }
}
