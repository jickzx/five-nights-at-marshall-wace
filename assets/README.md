# Blood sea art kit

All 15 production models are in `Unity/Assets/Resources/Models/`, with `manifest.json` beside them. Load a model with `Resources.Load("Models/player")`, replacing `player` with its manifest name. `Mock/` belongs to another thread and is untouched.

The kit includes the 1.8 m astronaut, three evolving creatures, escape ship, interactive console, sealed door, two rocks, pillar, arch, mural slab, wreckage, shore, and blood sea. All use flat shading and a shared rust, charcoal, bone, and red palette. The largest model has 1,404 triangles. The shore has 836 triangles and the flat sea has two.

## Rebuild

Requires Blender 5.2.1 LTS, ffmpeg, and a system `python3` with Pillow for the review sheets. Set `ART_PYTHON` to another Python executable if needed. Sources are procedural; edit the scripts rather than the generated files.

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python tools/blender/build_all.py
```

Use `-- --pass1` for the original four unblockers, or `-- --only player ship` to rebuild selected assets. Each asset script also runs directly with `--python tools/blender/<name>.py`. `ART_SKIP_VIDEO=1` skips recording generation for a quick modelling iteration. `ART_OUTPUT_ROOT=/absolute/scratch/path` isolates all generated outputs for parallel art experiments. To regenerate just the videos:

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

Use Generic rigs and set looping from the manifest. Walk, run, stalk and chase have no net forward travel; the game controller supplies locomotion. Preserve their animated vertical offsets and limb poses. Lunge moves its root 0.9 m forward. The player's seven-second `death_martyr` ends at `(0, -0.384, -0.015)` m relative to its starting root. The ship's nine-second `takeoff` starts with the hatch closed and ends at `(1.6, 12.35, -13.2)` m, with folded landing feet and the nose raised. Apply those authored movements once. Hold the final martyr, lunge, hatch-close and door-open poses; finish the escape transition at the end of takeoff.

## Interactive geometry

The console has a blank 0.79 × 0.19 m UV-mapped `screen`, centred at `(0, 1.235, -0.343)`, and independent `tile_0` through `tile_3` meshes. Its front faces -Z. Press a tile 0.025 m along model-local +Z, then restore its original position. Mesh origins remain at the asset base; mesh bounds give each key's visible centre. The ship has a decorative cockpit dashboard and a hinged side ramp. Its entry is 1.58 m wide with at least 1.91 m vertical clearance, and its open ramp extends to X = 2.87559 m. Play `hatch_close` before `takeoff`.

The mural's `picture` quad is 1.22 m wide and 1.60 m high, with UVs spanning 0 to 1. Replace its material with the game mural. Both screen and picture faces point toward exported -Z.

The sealed door opens in 3.5 seconds. Each leaf first unseals 0.06 m rearwards, then slides 1.19 m outwards. Allow 4.60 m total width for the rails. A conservative traversal opening is 2.04 m wide by 2.60 m high above the 0.11 m threshold.

The shore and sea are each 20 m square. With the shore at the origin, put the sea at Unity position `(0, 0.36, -14)`. The shore's dry edge lies at Z = +10 and its submerged edge at Z = -10. Repeat shore tiles along X in 20 m steps; repeat sea tiles in X and Z. The sea is flat so the game can supply any wave or blood shader.

FBX contains flat base colours and emissive values. Preserve emission for `eye`, `signal` and the ship's `escape_amber`, and preserve two-sided rendering for thin panels, screen/picture faces and water. Visors are opaque. The sea uses a separate `blood_sea_surface` material. Preview lighting and scenery are not exported. Unity material conversion and runtime appearance still need verification in the selected render pipeline. No `.meta`, colliders, controllers or Unity material assets are supplied.

## Verification and previews

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python tools/blender/validate.py
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python tools/blender/audit_beast_motion.py
python3 tools/blender/contact_sheet.py
python3 tools/blender/write_review_guide.py
```

The contact sheet command needs Pillow. It uses the saved PNGs and decoded MP4 frames.

`assets/previews/validation.json` records checks against the actual binary FBX and a fresh Blender FBX import: identity roots, metre units, take names, triangle counts, bounds, UVs, one armature, and sampled animated bounds. All seven MP4s are decoded as H.264/yuv420p. Revised inspection recordings show three simultaneous views at 12 fps with the original clip timing; key poses and static views also have dedicated PNG review sheets. The separate escape cinematic runs at 24 fps. These checks do not replace a Unity runtime test; the Unity project is owned by another thread.

The [contact sheet](previews/contact_sheet.png) shows every model. The [pose sheet](previews/animation_poses.png) samples the recordings. [Review notes](previews/README.md) link each recording and identify its clip timestamps.

`build_all.py` also renders the ship's separate nine-second escape cinematic over a blood sea. To repeat only that shot from the saved source:

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python tools/blender/render_escape.py
```

The beast motion audit evaluates all 678 baked frames for stance contact, ground clearance, connected joints and seamless loop endpoints. Its source hashes and measured tolerances are recorded in `assets/previews/beast_motion_validation.json`. Uploaded PR recordings are indexed by content hash in `assets/previews/attachments.json`.
