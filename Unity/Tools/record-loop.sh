#!/bin/bash
# Usage (from Unity/, Editor open and ready): Tools/record-loop.sh <framesDir> [out.gif]
# Plays the fake loop end to end (win, then lose) through `unity command`, capturing game-view frames,
# then stitches them into a GIF with Python/Pillow (Homebrew ffmpeg works too if yours is healthy).
U=~/.unity/bin/unity; F=$1; rm -rf "$F"; mkdir -p "$F"
ev() { $U command script eval --code "$1" --no-banner >/dev/null 2>&1; }
D='Fnmw.Game.GameDirector.Instance'
$U command editor.playmode start --no-banner >/dev/null; sleep 3
( while [ ! -f "$F/stop" ]; do $U command capture game_view --output-path "$F/a_$(date +%s%N).png" --width 960 --height 540 --no-banner >/dev/null 2>&1; done ) &
sleep 0.5
( while [ ! -f "$F/stop" ]; do $U command capture game_view --output-path "$F/b_$(date +%s%N).png" --width 960 --height 540 --no-banner >/dev/null 2>&1; done ) &
sleep 2.5
ev "$D.BeginExplore();"; sleep 1
for z in 1 2 3 4 5 6; do ev "UnityEngine.Object.FindAnyObjectByType<Fnmw.Game.PlayerController>().Warp(new UnityEngine.Vector3(0,0,$z));"; sleep 0.6; done
sleep 1.5
ev 'Fnmw.Core.Interactable.All[0].Interact();'; sleep 3
ev "$D.Submit(new[]{\"open\",\"door\"});"; sleep 3
ev "$D.Submit(new[]{\"door\",\"open\"});"; sleep 3.5
ev "$D.ContinueReveal();"; sleep 2.5
ev "$D.Choose(0);"; sleep 2
ev 'Fnmw.Core.Interactable.All[0].Interact();'; sleep 2.5
ev "$D.Submit(new[]{\"ship\",\"start\"});"; sleep 3
ev "$D.ContinueReveal();"; sleep 2
ev "$D.Choose(0);"; sleep 4
ev "$D.Restart();"; sleep 2
ev "$D.BeginExplore();"; sleep 1
ev 'var p=Fnmw.Core.Services.Player; var b=UnityEngine.GameObject.Find("Beast").transform; p.rotation=UnityEngine.Quaternion.Euler(0,180,0); b.position=p.position+new UnityEngine.Vector3(0,0,-6);'
for i in $(seq 1 30); do sleep 1; ph=$($U command script eval --code 'return Fnmw.Core.Services.State.Phase.ToString();' --no-banner 2>/dev/null | tail -1); [ "$ph" = "Dead" ] && break; done
sleep 4
touch "$F/stop"; wait
$U command editor.playmode stop --no-banner >/dev/null
ls "$F"/*.png | wc -l
OUT=${2:-$F/loop.gif}
python3 -I - "$F" "$OUT" <<'PY'
import sys, os, re
from PIL import Image
d, out = sys.argv[1], sys.argv[2]
fs = sorted([f for f in os.listdir(d) if f.endswith('.png')], key=lambda f: int(re.sub(r'\D', '', f)))
ims = [Image.open(os.path.join(d, f)).convert('RGB').resize((640, 360)).quantize(colors=128) for f in fs]
ims[0].save(out, save_all=True, append_images=ims[1:], duration=250, loop=0, optimize=True)
print(out, len(ims), "frames")
PY
