"""Rasterize the AGC reference mark for Windows. Build-only dependency: Pillow."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import os
root=Path(__file__).resolve().parents[1]/"assets"
def mark(tray=None):
    im=Image.new("RGBA",(512,512));d=ImageDraw.Draw(im)
    if tray is None:d.rounded_rectangle((8,8,504,504),144,fill="#2b2741",outline="#5d5294",width=16)
    color=tray or "#b5abfc";width=51 if tray else 35
    def stroke(points):
        pts=[(round(x*16),round(y*16)) for x,y in points]
        d.line(pts,fill=color,width=width,joint="curve")
        for x,y in pts:d.ellipse((x-width//2,y-width//2,x+width//2,y+width//2),fill=color)
    stroke([(7,10),(25,10)] if tray else [(9,11.5),(23,11.5)])
    stroke([(7,17),(13.5,17),(17.5,21),(25,13.5)] if tray else [(9,17),(14.5,17),(18,20.5),(23.5,14)])
    return im
for name,t in [("app",None),("tray","#f1f1f5"),("tray-dark","#292b3b")]:
    mark(t).save(root/(name+".ico"),sizes=[(n,n) for n in (16,20,24,32,48,64,128,256)])
avatar=Image.new("RGB",(768,768),"#161826");m=mark().resize((306,306),Image.Resampling.LANCZOS)
avatar.paste(m,(231,127),m)
d=ImageDraw.Draw(avatar);f=ImageFont.truetype(os.path.join(os.environ["WINDIR"],"Fonts","segoeui.ttf"),54)
d.text((384,484),"automatic grammar",font=f,fill="#e9e9ed",anchor="mt")
d.text((384,551),"correction",font=f,fill="#b5abfc",anchor="mt")
avatar.save(root/"avatar.png")

