# Audio (Fnmw.Audio)

Everything you hear is synthesised in C# at runtime. There are no audio files and nothing to import.

- **`Synth/`** is pure C# with no `UnityEngine` calls, so it renders identically in Unity, in tests and in the offline harness.
  - `ScoreEngine` is the live score, with a 16th-note sequencer, eleven layers and the two endings (`Endings.cs`).
  - `IntensityModel` turns intensity, threat, stage and phase into a mood, with hysteresis. `MoodScheduler` lands mood changes on the beat (or the bar, when calming down).
  - `MotifGenerator` gives each word a motif from its glyph seed, in the mode and instrument of its source language.
  - `SfxLibrary` renders every stinger and effect.
- **`AudioDirector`** is the `IAudioDirector`. It runs the engine in `OnAudioFilterRead`, listens to `GameEvents`, and restarts the score if the audio device resets.
- **`SpatialAudio`** handles 3D sound: beast vocalisations and steps follow the GameObject named `Beast`. It also plays player footsteps (from movement) and hum and static at every `Interactable`.
- **`MasterLimiter`** goes on the `AudioListener` once the player exists. It's transparent below 0.8 and soft-knees to a 0.97 ceiling, so the score plus every 3D sound never clips.
- **`AuditionMode`**: press **F9** (or backquote) in play mode, use Left/Right to step and R to replay. `Fnmw.Audio.AuditionMode.Instance.RunAll()` plays everything back to back.
- **`AudioTap`** records the listener mix. Call `Fnmw.Audio.AudioTap.StartRecording(path)` / `StopRecording()`.

## For other threads

- Call `Services.Audio.PlayStinger(name)` with any of: `begin`, `console_on`, `correct`, `wrong`, `stage_up`, `beast_roar`, `beast_lunge`, `beast_vocal`, `footstep`, `beast_step`, `static`, `ship_power`, `death`, `liftoff`. `death` and `liftoff` are one-shot and idempotent.
- `SetIntensity(0..1)` every frame (the GameDirector already does this). Stage, threat, phase and beast distance are read from `GameState`.
- **UI:** call `GameEvents.RaiseTilePicked(tile)` when a tile is placed and `GameEvents.RaiseTilesCleared()` on clear, and each word plays its motif. Without them the director still recovers the phrase from the reveal text.

## Rendering outside Unity

```bash
D=/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet
cd "Unity/Assets/Scripts/Audio/Tools~/RenderHarness" && $D run -c Release -- /tmp/fnmw-audio
```

This writes each state, both arcs, the language motifs and every stinger to WAV, and prints peak, RMS and CPU cost.
