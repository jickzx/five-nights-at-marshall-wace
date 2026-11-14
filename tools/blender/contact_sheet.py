"""Assemble the rendered PNGs and sampled MP4 frames for visual review.

Run with system Python and Pillow after build_all.py or render_recordings.py.
"""
import io
import json
import subprocess
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
ROOT=Path(__file__).resolve().parents[2]
PREVIEWS=ROOT/'assets/previews'
FONT='/System/Library/Fonts/Menlo.ttc'
font=ImageFont.truetype(FONT,17)
small=ImageFont.truetype(FONT,13)
manifest=json.loads((ROOT/'Unity/Assets/Resources/Models/manifest.json').read_text())
names=['player','beast_stage1','beast_stage2','beast_stage3','ship',
       'console','door_sealed','mural_slab','ruin_pillar','ruin_arch',
       'rock_a','rock_b','wreck_debris','terrain_shore','blood_sea']
canvas=Image.new('RGB',(1500,1090),'#100e12')
draw=ImageDraw.Draw(canvas)
draw.text((20,16),'BLOOD SEA / UNITY ART KIT',font=ImageFont.truetype(FONT,28),fill='#e5d9be')
draw.text((20,53),'15 procedural assets | flat shading | metres | Y up | FBX named takes',font=font,fill='#b6aaa3')
for index,name in enumerate(names):
    x=index%5*300;y=index//5*336+90
    img=Image.open(PREVIEWS/f'{name}.png').convert('RGB').resize((300,300))
    canvas.paste(img,(x,y))
    draw.text((x+10,y+303),name,font=font,fill='#e5d9be')
canvas.save(PREVIEWS/'contact_sheet.png')
video_names=['player','beast_stage1','beast_stage2','beast_stage3','ship','door_sealed']
canvas=Image.new('RGB',(960,1680),'#100e12')
draw=ImageDraw.Draw(canvas)
ffmpeg='/opt/homebrew/opt/ffmpeg@6/bin/ffmpeg'
for row,name in enumerate(video_names):
    segments=manifest['models'][name]['preview_segments']
    if len(segments)==1:
        times=[segments[0]['end_seconds']*f for f in [0,.33,.66,.95]]
    else:
        times=[s['start_seconds']+(s['end_seconds']-s['start_seconds'])*.7 for s in segments]
    for col,t in enumerate(times):
        result=subprocess.run([ffmpeg,'-v','error','-ss',str(t),'-i',str(PREVIEWS/f'{name}.mp4'),
                               '-frames:v','1','-f','image2pipe','-vcodec','png','-'],capture_output=True,check=True)
        frame=Image.open(io.BytesIO(result.stdout)).convert('RGB')
        if frame.width==1440:
            frame=frame.crop((0,0,480,480))
        frame=frame.resize((240,240))
        canvas.paste(frame,(col*240,row*280+25))
    draw.text((10,row*280+4),name,font=font,fill='#e5d9be')
canvas.save(PREVIEWS/'animation_poses.png')
