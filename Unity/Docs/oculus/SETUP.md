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

1. **Android Build Support for Unity 6000.6.9f1**, if it is not installed.
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
   - Player (Android): package `com.fnmw.fivenights`, IL2CPP, ARM64, minimum API 32, target API 34, Vulkan only, Linear colour, GameActivity entry point.

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

Logs: `adb logcat -s Unity` (or MQDH > *Logs*). The game prints `[XR] headset detected: ...` and `[XR] VR rig installed` at boot.

## 5. Controls (Touch)

| Action | Desktop | Quest 2 Touch |
|---|---|---|
| Look | Mouse | Your head |
| Walk / strafe | WASD | Left stick (moves where you face) |
| Run | Shift | Hold left grip |
| Turn | Mouse | Right stick: 45° snap by default. Click the right stick to switch to smooth turning (remembered) |
| Use console / board ship | E | A or right trigger |
| Flashlight (lamp) | F | X |
| Leave the console | Esc / Tab | Tap Y (the left menu button also works) |
| Skip the intro | Space / Esc / Enter | A, right trigger, or tap Y |
| Skip the tutorial | Hold Q | Hold Y (the bar fills; it skips at 0.8 s) |
| Point at tiles, answers, buttons | Mouse | Aim the right controller (laser) |
| Select / place the tile under the laser | Click | Right trigger |
| Move the tile / answer cursor | Arrow keys | Either stick |
| Place the tile under the cursor / continue | Space | Right trigger with the laser off the screen |
| Send the answer / confirm / next | Enter | A |
| Undo the last tile / back | Backspace | Tap B |
| Clear the tiles / skip the worked example | Delete | Hold B (0.5 s) |
| Pick tile or answer N | 1-9, 0 | Laser + trigger, or stick + trigger/A |
| Title: start / replay tutorial | Enter / T | A (or trigger) / tap Y |
| Restart after an ending | Enter / click | A or trigger |
| Left trigger | – | Nothing (it used to send, and fired by accident) |

**Tap or hold.** B and Y each do two things. A *tap* fires when you let go, and only if you let go within 0.5 s. A *hold* never fires the tap first, so holding Y to skip the tutorial at the console does not walk you away from it, and holding B to clear never undoes a tile on the way. A held button fills towards amber on the controller model.

**Mapping changes from the first Quest build (PR #17), and why.** Send moved from X / left trigger to **A**, because the left trigger sat under the fingers holding the grip and sent answers by accident. Place moved from A to the **right trigger**, which is also the laser's click. The lamp moved from B to **X**, so B is only undo / clear. Clear moved from Y to **hold B**, and leave moved from the menu button to **tap Y**, with skip tutorial on **hold Y** (it was a held left-stick click). That frees the Y conflict between "replay tutorial" and "skip worked example".

**The controller guide in the headset.** Both controllers are drawn as Touch-like models. Every button lights and sinks while you press it (the trigger swings with the pull, the sticks tilt), and a short label beside each button says what it does *right now*: USE / LAMP / WALK / RUN while roaming, PLACE / SEND / UNDO · HOLD CLEAR / LEAVE at a console, START / TUTORIAL on the title. During the worked example the labels distinguish NEXT from PLACE / SEND and explicitly say HOLD SKIP EXAMPLE for B. A card over the left wrist (turn your wrist up) repeats the current controls in at most five large lines. In VR the on-screen copy names Touch buttons instead of keys: the tutorial, the worked example, the console's footer, the title's replay key, the A keycap in place of the Enter icon, and the intro's skip hint. The desktop build keeps the keyboard copy.

**Tutorial in VR.** The first cue teaches the left stick walk and the right stick turn. It waits until you have walked a few metres *and* turned with the stick at least once (one snap is enough), or 35 s, whichever comes first. Head-looking alone does not count, because in VR the body only turns with the stick. The objective cue points at the ground chevron and the wrist card.

## 6. What changes in VR, and why

- **Camera.** Head tracking drives the view. Height is calibrated so your eyes sit at the game's 1.6 m eye height, whether you stand or sit, and it recalibrates when you recenter (hold the Meta button). Walking in your room moves the character, which still collides. There is no head bob in VR (comfort).
- **No PS1 low-res pass in VR.** On desktop, World's RetroScreen draws the camera into a 240-line texture shown on a screen-space overlay. A headset never shows overlay canvases, so in VR the eyes render the scene directly at full resolution. Vertex snapping is off too.
- **UI.** The same UI texture with the same CRT glass hangs in front of you at about 1.9 m, 2.4 m wide. While you explore, it follows your head closely (like a visor). On the title, console, decode, quiz and end screens it follows lazily, so it stays put while you read.
- **Cutscenes** (the wormhole intro, death, escape) move a camera on their own. In VR they play on a 4.8 m screen in a dark room ("theater") instead of moving your head for you. Their fades and captions are drawn into that picture. **The screen comes up straight in front of wherever your headset faces when the cutscene starts**, at eye level, however you have turned or walked in your room (it was fixed down the room's forward axis, so a turned player saw it to the side or behind). It realigns if you recenter while it is up.
- **Death scare.** The beast first appears in the world, in your own tracked view: for about a second it stands 1.35 m in front of your face, turned to you, with a red pulse. Nothing moves your head. Then the rest of the ending plays on the theater screen in front of that same view. No extra sound plays: the catch roar and the death stinger are the only audio, as on desktop.
- **Beast size.** In a headset the beast is fitted exactly to **2.05 / 2.35 / 2.7 m** for stages 1 / 2 / 3. That is still taller than the 1.8 m player, but it no longer reads as a building at arm's length. Desktop keeps its art heights (2.35 / 2.9 / 3.55 m, corrected only for wrong import units). The fit scales the model itself before the animator records its base scale, so the procedural poses and the spawn "pop" scale around the fitted size.
- **Ground guide.** While you roam (and on the final run to the ship), a dim chevron lies on the ground about 1.9 m ahead, pointing to the same target as the visor's objective line (the lit console, or the ship). It sits on the real surface, tilted with slopes and ignoring pillar tops and walls. On flooded paths it rises above the sea's maximum wave crest so opaque water cannot hide it. It fades out from 5.5 m to 2.6 m from the target, and it is hidden at consoles, on screens, in cutscenes and after death.
- **The beast "in front" for the skip keys** (dev / demo) goes where your head looks, not where the body faces.
- **Comfort.** Snap turning is on by default, and a dark vignette closes in while you move with the stick. The game pauses (time and audio) while the Quest system menu has focus.

### Verified so far, and what still needs a headset

EditMode tests cover: theater placement at turned and sideways-displaced room poses, both on the pure layout and on the real rig driven frame by frame (intro, death after the in-world beat, realigning on the next cutscene); the Touch mapping, including tap/hold timing for B and Y and the dead left trigger; the guide on flat ground, slopes, beside pillars and over a void, and its target policy; beast heights and fitting under a mid-pop root; the scare pose; the VR turn practice; and the VR copy on the tutorial and console. A headset is still needed to judge: the controller models' proportions against your real hands (they are approximations built from primitives), the readability of the labels and the wrist card, the scare's timing and distance for comfort, and the chevron's brightness in the fog.

## Troubleshooting

- `adb devices` shows `unauthorized`: put the headset on and accept the USB debugging prompt. If no prompt appears, re-plug the cable or toggle developer mode off and on.
- No device at all: try another cable (it must carry data) or another port, and make sure developer mode is on and the headset was rebooted.
- Black screen, but the app runs: check *Project Validation* and that OpenXR is the Android loader. The log should show `[XR] headset detected`.
- Build fails "Android Build Support is not installed": see step 1.1.
- Desktop: nothing changes. Standalone has no XR loader. To be extra sure, launch with `-novr`.
