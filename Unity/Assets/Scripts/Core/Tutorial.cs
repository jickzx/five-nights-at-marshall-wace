// CORE CONTRACT. Changes here must be called out in your PR description.
namespace Fnmw.Core
{
    /// <summary>
    /// One teaching moment of the first-run tutorial. The Game thread decides which cue is live
    /// (TutorialFlow); the UI decides how it looks and what it says.
    /// </summary>
    public enum TutorialCue
    {
        None,
        Move,        // WASD to move, mouse to look
        Objective,   // wake the ship: decode the consoles, follow the beacon
        Console,     // standing at the console: E to use it
        PuzzleRead,  // the rows: a picture and the alien words for it
        PuzzleTiles, // tiles are words: press a number to place one
        PuzzleSend,  // Enter sends, Backspace undoes, a misfire feeds the beast
        Misfire,     // just misfired: the beast grew, read the rows again
        Reveal,      // decoded: the words were a real language all along, only the glyphs were alien
        Quiz,        // which language? number keys answer
        Beast,       // it grows with time and with misfires; it freezes while you look at it
        NextConsole, // n of N: more consoles, then the ship
        Example,     // the worked example is on the console (before the first real puzzle)
        Hands,       // VR only, first: look down at the controllers in your hands, the lit button is the one to press
    }

    /// <summary>
    /// A solved puzzle the first console walks through before the real one: the premise, what each tile
    /// means, the rule the examples show, and the answer built tile by tile. Built by the Game thread from
    /// a separate seed, in a language the run's consoles do not use, so it never gives a real answer away.
    /// </summary>
    public class WorkedExample
    {
        public LangPuzzle Puzzle;
        public int[] Answer;         // indices into Puzzle.Tiles, in order
        public string LanguageName;  // "German": the reveal at the end of the walk-through
        public string Rule;          // RevealInfo.Feature: the rule in one or two plain sentences
        public string RealText;      // RevealInfo.RealText: "Werkzeug"
        public string Translation;   // RevealInfo.Translation
    }

    /// <summary>What the UI should draw for the tutorial this frame. Passed every frame while a run is live.</summary>
    public struct TutorialView
    {
        public bool Active;        // false: draw nothing
        public TutorialCue Cue;
        public float CueTime;      // seconds the current cue has been showing
        public float SkipHold;     // 0..1 while the skip key is held
        public int Solved, Total;  // consoles, for the NextConsole cue
    }

    /// <summary>
    /// Session memory for the tutorial. Statics survive scene reloads (restarts), not app restarts:
    /// once it has been finished or skipped it stays off until the title screen asks for it again.
    /// </summary>
    public static class TutorialSession
    {
        /// <summary>Finished or skipped this session.</summary>
        public static bool Seen;
        /// <summary>The title screen asked to play it again on the next run.</summary>
        public static bool ReplayRequested;
        public static bool WantsTutorial => !Seen || ReplayRequested;
        public static void Reset() { Seen = false; ReplayRequested = false; }
    }
}
