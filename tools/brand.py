"""Regenerate the original project artwork. Build-only dependency: Pillow (MIT-CMU)."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import os
root = Path(__file__).resolve().parents[1] / "assets"
root.mkdir(exist_ok=True)
purple, ink, white = "#ae8bff", "#191521", "#faf7ff"
def mark():
    im = Image.new("RGBA", (512,512))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((12,12,500,500), 125, fill=ink)
    # A lowercase 'a' with a check in the right stem.
    d.ellipse((100,173,313,386), fill=purple)
    d.ellipse((150,223,263,336), fill=ink)
    d.rounded_rectangle((262,163,310,386), 20, fill=purple)
    d.line((288,195,325,229,408,133), fill=white, width=31, joint="curve")
    return im
m = mark()
for name, sizes in [("tray",[16,20,24,32,48,64]), ("app",[16,24,32,48,64,128,256])]:
    m.save(root / (name+".ico"), sizes=[(v,v) for v in sizes])
avatar = Image.new("RGB",(768,768),ink)
avatar.paste(m.resize((420,420),Image.Resampling.LANCZOS),(174,66),m.resize((420,420),Image.Resampling.LANCZOS))
d=ImageDraw.Draw(avatar)
font=ImageFont.truetype(os.path.join(os.environ["WINDIR"],"Fonts","segoeuib.ttf"),61)
for y,label,color in [(514,"automatic",white),(592,"grammar correction",purple)]:
    d.text((384,y),label,font=font,fill=color,anchor="mt")
avatar.save(root/"avatar.png")

