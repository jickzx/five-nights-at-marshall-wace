# QS sender

The Unity game sends headset and controller motion and game events to the QS backend after the player accepts the in-game consent statement. When enabled, the microphone is processed on the device into breathing features; raw audio is never sent. Without configuration, telemetry is disabled.

## Demo day

1. An employer owner creates a device key with `POST /v1/devices` for the employer's question set. Copy `Unity/qs.example.json` to `Unity/Assets/Resources/Qs/qs.json` and set `endpointUrl` and `deviceKey`. This file is gitignored; the key is embedded in the APK, so distribute the APK only to trusted devices and revoke the key if it is lost.
2. Build from the repository root with Unity 6000.6.9f1 and Android Build Support installed:

   ```sh
   /Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "$PWD/Unity" -executeMethod Fnmw.XR.EditorTools.QuestSetup.BuildApk -logFile /tmp/quest-build.log
   adb install -r Unity/Builds/Quest/FiveNightsAtMarshallWace-Quest.apk
   ```

3. Launch the game on the Quest, accept the consent screen with the controller, and allow microphone permission. Declining continues the game without recording. The headset creates its own pseudonymous session; no per-session token is needed.
4. Sign in as the demo employer at https://qs-data-pipeline.vercel.app/qs/ to see the session. Keep the headset online throughout play.

The API must use HTTPS except for localhost or LAN testing. The `FNMW_QS_URL`, `FNMW_QS_DEVICE_KEY`, `FNMW_QS_ENABLED`, and `FNMW_QS_MICROPHONE` environment variables override configuration for development. A pre-created session and ingest token can also be supplied with `FNMW_QS_SESSION` and `FNMW_QS_TOKEN`.

After consent, the first 20 seconds are calibration. The sender posts overlapping motion windows, breathing features when available, and game events including puzzle, answer, monster, chase and session events. The backend produces per-question records and an end-of-session summary. The Quest uses XR headset and Touch controller poses; a non-XR build uses the camera and reports controllers untracked. Network errors can cause windows to be dropped; events are retained and retried.

For Quest setup and troubleshooting see [Quest setup](../oculus/SETUP.md). For API details see [the backend](../../../qs-backend/README.md).
