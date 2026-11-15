# Gemini in the game

`Services.Ai` generates short text with Google's Gemini API. Without a key it is a fake that is never available, so the game plays exactly as before and every feature shows its stock line.

## Turn it on

1. Copy `Unity/secrets.example.json` to `Unity/Assets/Resources/Ai/secrets.json`, then paste the key into `geminiApiKey` in the copy. Never put a key in the example file: it is tracked. The whole `Assets/Resources/Ai` folder is gitignored.
2. Press Play. The console prints `[Ai] Gemini ready: model ...`. Each request then logs `[Ai] <feature> <status> in <ms> ms`.

Because that file is under `Resources`, a build made on a machine that has it carries the key inside the app and needs no setup where it runs. A `secrets.json` in the Unity project folder (beside a build's data folder, in a build) and one in the save folder are also read. Building with the key file present logs a warning that the build carries the key. Environment variables override all of them: `GEMINI_API_KEY`, `GEMINI_MODEL`, `FNMW_AI_PROXY_URL`, and `FNMW_AI_DISABLED=1` to switch it off.

This repository is public, so the key file must stay out of git: a key pushed to GitHub can be used by anyone and is likely to be disabled by Google. A key baked into a build can also be extracted from it, which is acceptable for a demo build handed to judges and not for a public release. A public release sets `aiProxyUrl` instead: the client POSTs the same body to that URL with `?model=<model>` and no key, and the proxy adds the key and forwards it to Gemini. The proxy is not written yet.

The default model is `gemini-3.5-flash-lite`. Change `geminiModel` if the account has no access to it.

## Use it from a feature

```csharp
using Fnmw.Core;
using Fnmw.Core.Ai;

[Serializable] class Reply { public string line; }

_pending?.Cancel();
if (Services.Ai.Available)
    _pending = Services.Ai.Request(new AiRequest
    {
        Feature = "taunt",
        System = "You are ... Reply with one line of at most 14 words.",
        Prompt = facts,
        SchemaJson = "{\"type\":\"OBJECT\",\"properties\":{\"line\":{\"type\":\"STRING\"}},\"required\":[\"line\"]}",
    }, r =>
    {
        if (!r.Ok || !AiText.TryParseJson(r.Text, out Reply reply)) return;   // keep the stock line
        string line = AiText.Clean(reply.line);
        if (!AiText.Fits(line, 110, 18)) return;
        Services.UI.ShowMonsterLine(line, 4f);
    });
```

`IAiText`, `AiRequest`, `AiResponse` and `AiHandle` are in `Assets/Scripts/Core/Ai/AiTypes.cs`. `AiText` has the reply checks. Tests use `Fnmw.Core.Fakes.ScriptedAiText`, which records requests and answers from a queue; set `Hold` to deliver replies later with `Flush()`.

## Rules for every feature

- **The game never waits.** Apply the answer, move the beast and show the stock line first. A reply only improves on what is already on screen.
- **Cancel stale requests.** Call `handle.Cancel()` when the puzzle changes, the run restarts or ends, or the object is destroyed. A cancelled callback never runs. When a new run boots, the client cancels every request still in flight itself, so never wait on a callback: check `handle.Done`.
- **The callback can run before `Request` returns** (disabled, throttled). Do not rely on it being asynchronous.
- **Check every reply.** A schema fixes the shape, not the length, the truth or the content. Enforce length with `AiText.Fits`, and reject a reply that names the language before the quiz or quotes the answer (`AiText.MentionsAny`).
- **Send only facts the game holds.** The model must not be asked to invent what the player did, and it never decides an answer, a puzzle, the difficulty or where the beast goes.
- **Keep requests rare.** Each feature has its own cooldown. `AiBudget` is a backstop: 2 s between requests of one feature, 2 in flight, 80 per session.
- **Keep the prompt builder and the reply check pure**, in their own class, with EditMode tests. Nothing in the tests may touch the network.

## UI seams

`IGameUI` has three optional methods for these features, each a no-op until the UI implements it: `ShowMonsterLine(text, seconds)`, `ShowHint(text)` and `ShowDebrief(text)`.

## What has been verified

The request and reply JSON are covered by tests. On 15 November 2026 a real key listed `gemini-3.5-flash-lite` as supporting `generateContent`, and the endpoint accepted the key, but the call itself was refused with HTTP 402 because the project's prepaid credits were used up. So a successful live reply has not been seen yet. A 402 reaches a feature as `BadResponse`, and it keeps its stock line.

## Checking your work without the Editor

- Compile: `python3 Unity/Tools/cc.py` compiles the Fnmw assemblies of the checkout it sits in with Unity's own compiler, without disturbing an open Editor. It borrows compiler settings from a checkout that Unity has opened, so an assembly whose asmdef is new is skipped and the script exits non-zero.
- Tests: from the `Unity` folder, `unity test "$PWD" --mode EditMode --filter AiScaffoldTests -o out.xml` (needs a `Library` folder in that checkout).
