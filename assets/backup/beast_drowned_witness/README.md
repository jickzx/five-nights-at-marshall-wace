# Backup: Drowned Witness (previous beast)

The beast that shipped before "The Principal". Nothing here is imported by Unity (it is outside `Unity/Assets`).

Revert:

```sh
cp assets/backup/beast_drowned_witness/fbx/* Unity/Assets/Resources/Models/
cp assets/backup/beast_drowned_witness/blender/* assets/blender/
# then restore the beast_stage1..3 entries in Unity/Assets/Resources/Models/manifest.json from manifest_entries.json
```

The `.fbx.meta` files keep their original GUIDs, so nothing that references the models breaks. Note that this model faces Unity -Z, so it runs backwards under `BeastController`.
