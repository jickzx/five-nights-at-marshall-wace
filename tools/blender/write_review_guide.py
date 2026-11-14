"""Keep the review guide's clip timestamps synchronized with the exported manifest."""
import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
manifest=json.loads((ROOT/'Unity/Assets/Resources/Models/manifest.json').read_text())
names=['beast_stage1','beast_stage2','beast_stage3','player','ship','door_sealed']
lines=['# Art review', '', '![All 15 models](contact_sheet.png)', '',
       'The revised recordings show front, side and rear views together at natural animation speed. Each clip has a labelled pose sheet; every model has a three-angle silhouette sheet. The manifest records exact source ranges and recording timestamps.', '',
       '| Recording | Clip start times (seconds) |', '| --- | --- |']
for name in names:
    entry=manifest['models'][name]
    segments=entry.get('preview_segments',[])
    times=', '.join(f"{s['start_seconds']:.2f} {s['clip']}" for s in segments)
    lines.append(f'| [{name}]({name}.mp4) | {times} |')
lines += ['', 'The beasts share a forked crown, wedge mask, long hooked fingers and a protruding spine. Stage 2 opens the rib cage and raises two secondary arms. Stage 3 exposes the torso cavity, splits the jaw, and adds collar eyes, torn skin and moving viscera. Stalk uses long planted steps; chase lowers the body into an asymmetric, snapping gait.', '',
          'The player death descends to both knees, opens the arms and tips the head back, then holds. The ship takeoff pauses for ignition before lifting, retracting its landing feet, banking and accelerating. The camera follows its root so the ending remains visible.', '',
          '| Model | Surface review | Animation pose sheets |', '| --- | --- | --- |']
for name,entry in manifest['models'].items():
    views=f'[{name}]({name}_views.png)' if 'views' in entry['files'] else 'Blockout review pending'
    clips=', '.join(f'[{clip}]({Path(path).name})' for clip,path in entry.get('review_sheets',{}).items()) or '—'
    lines.append(f'| {name} | {views} | {clips} |')
lines += ['', '![Selected animation poses](animation_poses.png)', '',
          'The [asset README](../README.md) contains build and integration instructions. The [validation report](validation.json) covers the actual binary FBX files, fresh imports and decoded recordings. Unity runtime import has not been tested in this worktree.', '']
(ROOT/'assets/previews/README.md').write_text('\n'.join(lines))
