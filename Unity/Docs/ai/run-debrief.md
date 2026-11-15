# Run debrief

When the run ends, the end screen shows two or three sentences about it: the languages met, where the player slipped, how far the beast grew, and how it ended. It works with no key: a stock debrief built from the log goes up first, and a Gemini reply replaces it only if it passes every check.

Code: `Assets/Scripts/Game/Debrief/`. `RunDebrief` (added to the GameDirector object by `GameModule`) hosts `RunDebriefer`, which listens to `GameEvents`. `DebriefWriter` holds the stock text, the request and the reply check. `RunLog` is the record. UI: `GameUI.ShowDebrief` and the death and escape screens in `UI/OtherScreens.cs`.

## What the log records

From `PuzzleStarted`, `AnswerSubmitted`, `QuizAnswered`, `BeastStageChanged` and `PhaseChanged`:

- each console reached: its real language, its rule (concept), when it opened, every wrong answer with the concept missed, every response time, whether it was solved, the quiz result
- every beast stage change with its time, and the highest stage
- the outcome, elapsed time, consoles decoded of total, and whether the player was caught at an open console

It resets when Explore begins after the title, and the restart reloads the scene, which makes a new log anyway. After the run ends it ignores further events. The model receives `RunLog.Facts()` and nothing else.

## When it runs

On the phase change to Escaped or Dead, before the ending cutscene, the stock debrief is sent to the UI and the request goes out. The UI keeps the text and prints it when `ShowEnd` brings the screen up, so the reply has the length of the cutscene to arrive (timeout 10 s). A reply that arrives while the end screen is up replaces the stock text there. A restart (`GameDirector.Restarting`) or destroying the component cancels the request, and a run counter drops any reply from an earlier run even if its handle was not cancelled. `RunDebrief` subscribes to `Restarting` in `Awake` and unsubscribes in `OnDestroy`. Because domain reload is off on entering play mode, `GameDirector` also clears `Restarting` before each play session, so a subscriber left over from the last one can never fire.

The escape screen keeps a four-line band above the AGAIN cue for the debrief, enough for 210 characters, whether the text arrives before or after the screen. With five languages the header closes up so every language keeps its name row and one phrase beside a full band.

## Reply checks

`DebriefWriter.Check` rejects a reply that:

- is not JSON, is longer than 210 characters or 42 words, or is not two or three sentences
- names a language the run did not reach (from a list of about 50 language names plus every language of the run's world)
- has a number the facts do not back. A clock (`m:ss`) must match a logged one exactly, and a count ("2 tries", "stage 3") must be in the facts. A time may be rounded honestly: a number before "minutes" may be any logged time or the gap between two logged times, floored or rounded to the minute (5:58 allows "five minutes" and "six minutes", not "seven"), and a number before "seconds" may be such a time exactly or to the nearest 5 or 10. Numbers are digits or words up to twelve
- mentions being caught in an escape, or lifting off in a death
- says the beast, the game or no one in particular ("Japanese was chosen to break you") chose, picked, targeted, controlled or steered anything, or that the game was rigged. The player choosing is fine: "you picked the wrong word" passes. The doer is the nearest subject before the verb, so "the beast hunted you and chose its moment" is still rejected
- calls the run flawless or effortless ("you breezed through it", "never slipped") when the log has wrong answers, unless the sentence names a console that had none ("you breezed through German")
- claims a slip ("you stumbled", "a wrong word") when the log has no wrong answers, unless it is negated ("not one slip", "without a mistake")

What the checks still cannot catch: a wrong claim made in words they do not know ("you took your time", "you hesitated", "the shore went quiet"), invented feelings and scenery, a wrong concept name, a language name missing from the list, a flawless claim about a clean console in a sentence that also covers one with slips, a rounding word that misleads ("over six minutes" for 5:58), and a true number attached to the wrong event ("the beast reached stage 3 after six minutes" when it got there at 2:10). They also still reject some fair replies, for example a noun such as "the target word" after the beast in the same sentence. In those cases the stock debrief stays up.

## Demo mode

Demo mode uses a fixed seed, so every demo run meets the same languages in the same order. A death debrief that named the language of the console the player died at, before they decoded it, would spoil it for the next demo run. With `DemoMode.On`, `RunDebriefer` sets `RunLog.HideUndecoded`: the language of every unsolved console is left out of the stock text (it says "caught you at 2:50, 2 of 3 consoles decoded" instead of naming the console), out of `Facts()` (the console is "undecoded language") and so out of the request, and the reply check forbids its name. Outside demo mode nothing changes. An escape decodes every console, so it is never affected.

## See it in play

Finish a run, or press F11 for the escape ending or F12 for the death ending. The console says which debrief is up:

- `[Debrief] shown: stock (AI unavailable)`: no key, or AI is switched off
- `[Debrief] shown: stock (Timeout)` (or another status): the request failed
- `[Debrief] shown: stock (reply rejected: <reason>)`: the reply failed a check
- `[Debrief] shown: live (1.2 s)`: the Gemini text replaced the stock one

The stock text always follows the same pattern: "You read …. You slipped N times, mostly on … at the … console. The beast reached stage N; you lifted off at m:ss."

Tests: `RunDebriefTests` and `DebriefUiTests` in `Assets/Tests/EditMode/Tutorial/`.
