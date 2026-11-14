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
    times=[0,2.5,4.5,7.9] if row<4 else ([0,2.8,3.9,5.9] if name=='ship' else [0,.5,1,1.9])
    for col,t in enumerate(times):
        result=subprocess.run([ffmpeg,'-v','error','-ss',str(t),'-i',str(PREVIEWS/f'{name}.mp4'),
                               '-frames:v','1','-f','image2pipe','-vcodec','png','-'],capture_output=True,check=True)
        frame=Image.open(io.BytesIO(result.stdout)).convert('RGB').resize((240,240))
        canvas.paste(frame,(col*240,row*280+25))
    draw.text((10,row*280+4),name,font=font,fill='#e5d9be')
canvas.save(PREVIEWS/'animation_poses.png')
