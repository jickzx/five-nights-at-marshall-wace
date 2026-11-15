# Meta Quest 2 port: setup, build and run (macOS)

The Quest build is the same game as the desktop build: same scene (`Assets/Scenes/Main.unity`), same rules, puzzles, screens and flow. Only the controls and the camera change. The VR code turns itself on only when an XR runtime is running, which happens only in the Android (Quest) build, so the desktop build plays exactly as before.

Links checked November 2026.

## Stack, and why

**Unity OpenXR (`com.unity.xr.openxr` 1.18.0) with the Meta Quest Support feature and the Oculus Touch Controller Profile, plus XR Plug-in Management (`com.unity.xr.management` 4.7.0) and the Input System.** These versions are the ones Unity 6000.6.9f1 ships and validates (they are bundled with the Editor).

- OpenXR is Unity's and Meta's recommended path for Quest apps in Unity 6. The legacy Oculus XR plug-in (`com.unity.xr.oculus`) is in maintenance.
- The game builds everything from code and already reads the Input System, so all it needs is a head pose, controller poses and buttons. OpenXR provides those with no vendor lock-in (the same code would run on other OpenXR headsets).
- Not added: **XR Interaction Toolkit**. The game's UI is drawn into a render texture and does its own hit-testing in "stage" pixels, so a uGUI/XRI ray interactor would not reach it. A small laser that converts the hit to stage space does the job. **Unity OpenXR: Meta** (`com.unity.xr.meta-openxr`) is for mixed reality (passthrough, planes, anchors), which this game doesn't use. **Meta XR SDK** (`com.meta.xr.sdk.*`) has more Meta-only features (Platform SDK, avatars, store entitlement), but it is heavier and Meta-only. None of that is needed to play one-to-one. You can add it later if you publish on the Horizon Store and need entitlement checks.

**Quest 2 is still supported.** Meta announced on 2 Nov 2026 that Horizon OS v2.13 (rolling out Jan to Mar 2027) is the last feature update for Quest 2 and Quest Pro. Security fixes continue through 2027, the Store stays open, and existing apps keep working. See [Support Meta Quest 2 and Meta Quest Pro after Horizon OS v2.13](https://developers.meta.com/vr/resources/quest-2-quest-pro-legacy-support/).

## 1. Install on the Mac

1. **Android Build Support for Unity 6000.6.9f1** (this Editor doesn't have it yet).
   Unity Hub > **Installs** > the **⋯** menu on *Unity 6000.6.9f1* > **Add modules** > tick **Android Build Support**. Under it, tick **OpenJDK** and **Android SDK & NDK Tools** too > **Continue** / **Install**. That is about 5 GB.
   Check: Unity > **Settings** (macOS: *Unity > Settings…*) > **External Tools**. *JDK*, *Android SDK*, *Android NDK* and *Gradle* should all say "installed with Unity (recommended)".
2. **Meta Quest Developer Hub (MQDH) for macOS**: [download](https://developers.meta.com/horizon/downloads/package/oculus-developer-hub-mac/) ([docs](https://developers.meta.com/horizon/documentation/unity/ts-mqdh-getting-started/)). It installs builds, streams the headset view, shows logs and includes `adb`. Optional alternatives are plain `adb` (`brew install android-platform-tools`; this Mac already has `/opt/homebrew/bin/adb`) or [SideQuest](https://sidequestvr.com/setup-howto).
3. A **USB-C data cable** (a charge-only cable won't work). Meta's Link cable or any USB 3 C-to-C/A-to-C data cable is fine.

## 2. Meta account, organization, headset

1. **Meta developer account.** Sign in at [developers.meta.com/horizon](https://developers.meta.com/horizon/) with the Meta account the headset uses. You must be 18 or older.
2. **Verify the account** (one time). In the [Developer Dashboard](https://developers.meta.com/horizon/manage/), go to the account's **Verification** tab and add a phone number (2FA) or payment method.
3. **Create an organization.** Developer Dashboard > **Create Organization** (or *My Organizations* > *Create*). Any name works. Developer mode requires that you belong to an organization, and recent reports say the organization must be verified too ([device setup](https://developers.meta.com/horizon/documentation/native/android/mobile-device-setup/)).
4. **Developer mode** in the **Meta Horizon** phone app (the one paired with the headset): **Menu > Devices >** pick your *Quest 2* **> Headset settings > Developer mode >** turn it **on**. Then reboot the headset (hold power > *Restart*). Docs: [Enable developer mode](https://developers.meta.com/horizon/documentation/android-apps/enable-developer-mode).
5. **Allow USB debugging.** Plug the headset into the Mac, put it on, and accept **Allow USB debugging?** with **Always allow from this computer**. If you also get a "USB detected / access data" notification, allow that as well.
6. Check the connection: MQDH > **Device Manager** should show the Quest 2 with a green **Active** badge. You can also run `adb devices` and look for one device listed as `device`, not `unauthorized`.

## 3. Configure the project (once, after installing Android support)

Open `Unity/` in Unity 6000.6.9f1, then:

1. **Fnmw > Quest > Configure project for Quest.** This does the following, and it is safe to run again:
   - XR Plug-in Management: **OpenXR** loader for **Android**. **No** loader for **Standalone**, so the desktop build stays flat.
   - OpenXR (Android): turns on **Meta Quest Support** (Quest 2 is among its target devices) and **Oculus Touch Controller Profile**, and sets render mode to **Single Pass Instanced** (multiview).
   - Player (Android): package `com.fnmw.fivenights`, IL2CPP, ARM64, min/target API 32, Vulkan only, Linear colour, GameActivity entry point.
   - Scripting define `FNMW_QS_XR` on Android, so QS telemetry reads the headset and Touch controllers instead of the camera. This only picks the pose source: the QS sender stays off without `qs.json`, and there is no in-game consent screen yet, so a headset build sends nothing (see `Docs/qs/README.md`).

   To check by hand: **Edit > Project Settings > XR Plug-in Management**. On the Android tab, *OpenXR* should be ticked. Under **> OpenXR**, on the Android tab, *Interaction Profiles* should list *Oculus Touch Controller Profile* and *OpenXR Feature Groups* should have *Meta Quest Support* ticked.
2. **Edit > Project Settings > XR Plug-in Management > Project Validation** (Android tab): fix anything red with **Fix All**.
3. Commit the changes the configure step makes under `Assets/XR/` and `ProjectSettings/` so teammates get the same setup.

## 4. Build and run

**From the Editor:** **Fnmw > Quest > Build APK**. This configures, switches the active platform to Android, sets ASTC texture compression and writes `Unity/Builds/Quest/FiveNightsAtMarshallWace-Quest.apk`, which git ignores. The first build takes a while (IL2CPP + Gradle). Or use **File > Build Profiles > Android > Switch Platform**, then **Build And Run** with the Quest selected under *Run Device*.

**From the terminal** (with the Editor closed on that project):

```bash
cd Unity
/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -quit \
  -projectPath "$PWD" -executeMethod Fnmw.XR.EditorTools.QuestSetup.BuildApk -logFile /tmp/quest-build.log
# add -development for a development build (Profiler, logcat stack traces)
adb install -r Builds/Quest/FiveNightsAtMarshallWace-Quest.apk
adb shell am start -n com.fnmw.fivenights/com.unity3d.player.UnityPlayerGameActivity
```

You can also drag the APK onto MQDH > *Device Manager* > **Apps**. To launch it in the headset, go to **Library > Applications >** the filter at the top right **> Unknown Sources > Five Nights at Marshall Wace**.

**Release signing** (Meta rejects debug-signed uploads): set `FNMW_KEYSTORE`, `FNMW_KEYSTORE_PASS`, `FNMW_KEY_ALIAS` (and optionally `FNMW_KEY_PASS`, `FNMW_VERSION_CODE`) in the shell before the build. Build APK applies them for that build only and clears the keystore path and alias afterwards, so nothing machine-specific lands in `ProjectSettings.asset`. Without them the APK is debug-signed, which is fine for sideloading.

Logs: `adb logcat -s Unity` (or MQDH > *Logs*). The game prints `[XR] headset detected: ...` and `[XR] VR rig installed` at boot.

## 5. Controls (Touch)

| Action | Desktop | Quest 2 Touch |
|---|---|---|
| Look | Mouse | Your head |
| Walk / strafe | WASD | Left stick (moves where you face) |
| Run | Shift | Hold left grip |
| Turn | Mouse | Right stick: 45° snap by default. Click the right stick to switch to smooth turning (remembered) |
| Use console / board ship | E | A or right trigger |
| Flashlight | F | B |
| Leave the console | Esc / Tab | Menu button (left ☰) |
| Skip the intro | Space / Esc / Enter | A, X or menu |
| Skip the tutorial | Hold Q | Hold the left stick down (click and hold) |
| Point at tiles, answers, buttons | Mouse | Aim the right controller (laser), right trigger clicks |
| Move the tile / answer cursor | Arrow keys | Either stick |
| Place the tile under the cursor / continue | Space | A |
| Send the answer / confirm | Enter | X (or left trigger) |
| Undo the last tile / back | Backspace | B |
| Clear the tiles / skip the worked example | Delete | Y |
| Pick tile or answer N | 1-9, 0 | Laser + trigger, or stick + A |
| Title: start / replay tutorial | Enter / T | A / Y |
| Restart after an ending | Enter / click | A, X or trigger |

A card on the left controller lists these controls in the headset. The on-screen text still names the keyboard keys (`[ENTER]`, `HOLD Q`), and the card shows which button does each one. The world prompts say "Press A".

## 6. What changes in VR, and why

- **Camera.** Head tracking drives the view. Height is calibrated so your eyes sit at the game's 1.6 m eye height, whether you stand or sit, and it recalibrates when you recenter (hold the Meta button). Walking in your room moves the character, which still collides. There is no head bob in VR (comfort).
- **No PS1 low-res pass in VR.** On desktop, World's RetroScreen draws the camera into a 240-line texture shown on a screen-space overlay. A headset never shows overlay canvases, so in VR the eyes render the scene directly at full resolution. Vertex snapping is off too.
- **UI.** The same UI texture with the same CRT glass hangs in front of you at about 1.9 m, 2.4 m wide. While you explore, it follows your head closely (like a visor). On the title, console, decode, quiz and end screens it follows lazily, so it stays put while you read.
- **Cutscenes** (the wormhole intro, death, escape) move a camera on their own. In VR they play on a 4.8 m screen in a dark room ("theater") instead of moving your head for you. Their fades and captions are drawn into that picture.
- **Comfort.** Snap turning is on by default, and a dark vignette closes in while you move with the stick. The game pauses (time and audio) while the Quest system menu has focus.

## Troubleshooting

- `adb devices` shows `unauthorized`: put the headset on and accept the USB debugging prompt. If no prompt appears, re-plug the cable or toggle developer mode off and on.
- No device at all: try another cable (it must carry data) or another port, and make sure developer mode is on and the headset was rebooted.
- Black screen, but the app runs: check *Project Validation* and that OpenXR is the Android loader. The log should show `[XR] headset detected`.
- Build fails "Android Build Support is not installed": see step 1.1.
- Desktop: nothing changes. Standalone has no XR loader. To be extra sure, launch with `-novr`.
