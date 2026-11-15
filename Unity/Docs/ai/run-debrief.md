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

On the phase change to Escaped or Dead, before the ending cutscene, the stock debrief is sent to the UI and the request goes out. The UI keeps the text and prints it when `ShowEnd` brings the screen up, so the reply has the length of the cutscene to arrive (timeout 10 s). A reply that arrives while the end screen is up replaces the stock text there. A restart (`GameDirector.Restarting`) or destroying the component cancels the request, and a run counter drops any reply from an earlier run.

## Reply checks

`DebriefWriter.Check` rejects a reply that is not JSON; is longer than 210 characters or 42 words; is not two or three sentences; names a language that the run did not reach (from a list of about 50 language names plus every language of the run's world); has a number, as digits or as a word up to twelve, that does not appear in the facts; mentions being caught in an escape, or lifting off in a death; or uses words saying the beast targeted, chose, picked or controlled anything.

The checks cannot catch a wrong claim that uses no number or language name ("you breezed through it" after five mistakes), a wrong concept name, a language name missing from the list, or invented feelings and scenery. They also reject some fair replies, for example "you picked the wrong word" or "the beast reached stage 3 after six minutes" when the time was 5:58. In those cases the stock debrief stays up.

## See it in play

Finish a run, or press F11 for the escape ending or F12 for the death ending. The console says which debrief is up:

- `[Debrief] shown: stock (AI unavailable)`: no key, or AI is switched off
- `[Debrief] shown: stock (Timeout)` (or another status): the request failed
- `[Debrief] shown: stock (reply rejected: <reason>)`: the reply failed a check
- `[Debrief] shown: live (1.2 s)`: the Gemini text replaced the stock one

The stock text always follows the same pattern: "You read …. You slipped N times, mostly on … at the … console. The beast reached stage N; you lifted off at m:ss."

Tests: `RunDebriefTests` and `DebriefUiTests` in `Assets/Tests/EditMode/Tutorial/`.
