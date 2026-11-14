# Blood sea art kit

All 15 production models are in `Unity/Assets/Resources/Models/`, with `manifest.json` beside them. Load a model with `Resources.Load("Models/player")`, replacing `player` with its manifest name. `Mock/` belongs to another thread and is untouched.

The kit includes the 1.8 m astronaut, three evolving creatures, escape ship, interactive console, sealed door, two rocks, pillar, arch, mural slab, wreckage, shore, and blood sea. All use flat shading and a shared rust, charcoal, bone, and red palette. The largest model has 1,496 triangles. The shore has 288 triangles and the flat sea has two.

## Rebuild

Requires Blender 5.2.1 LTS and ffmpeg. Sources are procedural; edit the scripts rather than the generated files.

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python tools/blender/build_all.py
```

Use `-- --pass1` for the original four unblockers, or `-- --only player ship` to rebuild selected assets. Each asset script also runs directly with `--python tools/blender/<name>.py`. `ART_SKIP_VIDEO=1` skips recording generation for a quick modelling iteration. To regenerate just the videos:

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python tools/blender/render_recordings.py
```

The encoder tries `FFMPEG`, `/opt/homebrew/bin/ffmpeg`, then `/opt/homebrew/opt/ffmpeg@6/bin/ffmpeg`. The latter was used here because the default executable has a missing x265 library. The script leaves the system installation alone.

## Coordinates and animation

The authoring scenes use metres, Z up and +Y forward. Before export, the builder rotates mesh data and bone rest matrices into +Y up and -Z forward. FBX objects have zero rotation, unit scale, and origins at the base. Exported unit metadata specifies one metre per unit. Each rigged model has one armature.

Every named action is sampled on every frame at 24 fps. Temporary NLA strips preserve the exact take names in FBX. The source scenes retain all named actions. Manifest `frame_start` and `frame_end` describe the Blender actions; `fbx_frame_start` and `fbx_frame_end` describe the zero-based exported takes. Bounding boxes describe the bind pose in exported coordinates.

| Model | Takes |
| --- | --- |
| player | idle, walk, run, death_martyr |
| beast_stage1, beast_stage2, beast_stage3 | idle, stalk, chase, lunge |
| ship | idle, hatch_close, takeoff |
| door_sealed | open |

Use Generic rigs for the custom skeletons. Set looping from the manifest's `loop` flag. Walk, run, stalk, and chase play in place. Lunge moves its root 0.9 m forward. Takeoff starts with the hatch closed and moves 5 m upward and 3 m forward. Keep those root movements in the animation or consume them through the game controller, but do not apply both. Hold the final pose of death_martyr, lunge, hatch_close, takeoff, and open.

## Interactive geometry

The console has a blank UV-mapped `screen` quad and independent `tile_0` through `tile_3` meshes. Press a tile 0.025 m along exported +Z, then restore it. The ship contains a visible cockpit console and a hinged side hatch. The standalone console is suitable for the game's decoding interaction.

The mural's `picture` quad is 1.22 m wide and 1.60 m high, with UVs spanning 0 to 1. Replace its material with the game mural. Both screen and picture faces point toward exported -Z.

The shore and sea are each 20 m square. With the shore at the origin, put the sea at Unity position `(0, 0.36, -14)`. The shore's dry edge lies at Z = +10 and its submerged edge at Z = -10. Repeat shore tiles along X in 20 m steps; repeat sea tiles in X and Z. The sea is flat so the game can supply any wave or blood shader.

FBX contains flat base colours and emissive values. Unity's selected render pipeline may require material conversion and emission enabled on `eye` and `signal`. No `.meta`, Unity editor scripts, colliders, controller code, or imported Unity material assets are supplied.

## Verification and previews

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python tools/blender/validate.py
python3 tools/blender/contact_sheet.py
```

The contact sheet command needs Pillow. It uses the saved PNGs and decoded MP4 frames.

`assets/previews/validation.json` records checks against the actual binary FBX and a fresh Blender FBX import: identity roots, metre units, take names, triangle counts, bounds, UVs, one armature, and sampled animated bounds. All six MP4s are decoded as H.264/yuv420p. These checks do not replace a Unity runtime test; the Unity project is owned by another thread.

The [contact sheet](previews/contact_sheet.png) shows every model. The [pose sheet](previews/animation_poses.png) samples the recordings. [Review notes](previews/README.md) link each recording and identify its clip timestamps.
