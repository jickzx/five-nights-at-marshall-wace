# Blood sea art kit

Build with Blender 5.2.1 LTS:

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --python-exit-code 1 --python tools/blender/build_all.py -- --pass1
```

The first delivery contains `beast_stage1`, `player`, `ship`, and `console`. The next delivery adds the remaining environment and creature stages, animation recordings, and export validation.

FBX files and the loader manifest live in `Unity/Assets/Resources/Models/`. Load an asset with `Resources.Load("Models/player")` or the corresponding model name. No Unity project or `.meta` files are needed to build the art. `Mock/` is not part of this kit.

The authoring scenes use metres, Z up and +Y forward. The exporter converts mesh coordinates and bone rest matrices to +Y up and -Z forward before writing FBX. Object transforms remain identity. Each Blender action is baked into a separately named FBX take through a temporary NLA strip. The source scenes retain the named actions.

Sources are in `assets/blender/`, stills in `assets/previews/`, and deterministic builders in `tools/blender/`. The manifest bounding boxes describe the bind pose in exported coordinates. Walk, run, stalk, and chase play in place. The lunge and takeoff actions include root motion. One-shot actions should hold their last pose.

The console has a blank UV-mapped `screen` quad and independent `tile_0` through `tile_3` objects. Tile presses move 0.025 m along exported +Z. Unity materials may need their emission enabled for the active render pipeline; the FBX stores flat base colours and emissive values.
