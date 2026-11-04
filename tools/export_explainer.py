"""Export the supplied AGC Explainer HTML, including its original sound and captions.

Development only: pip install playwright imageio-ffmpeg
python tools/export_explainer.py "AGC Explainer.html" --browser "path/to/msedge.exe"
"""
import argparse
import base64
import gzip
import json
from pathlib import Path
import re
import subprocess
import tempfile

import imageio_ffmpeg
from playwright.sync_api import sync_playwright


def embedded(source, name):
    match = re.search(fr'<script type="__bundler/{name}">([\s\S]*?)</script>', source)
    if not match:
        raise ValueError(f"Bundle has no {name}")
    return json.loads(match[1])


def export(source, browser, output):
    output.mkdir(parents=True, exist_ok=True)
    html = source.read_text(encoding="utf-8")
    track = next(x["uuid"] for x in embedded(html, "ext_resources") if x["id"] == "soundtrack")
    resource = embedded(html, "manifest")[track]
    data = base64.b64decode(resource["data"])
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    selector = "[data-om-exportable-video-with-duration-secs]"
    with tempfile.TemporaryDirectory() as tmp, sync_playwright() as pw:
        wav = Path(tmp, "sound.wav")
        wav.write_bytes(gzip.decompress(data) if resource.get("compressed") else data)
        engine = pw.chromium.launch(executable_path=browser, args=["--allow-file-access-from-files"])
        page = engine.new_page(viewport={"width": 1280, "height": 720}, device_scale_factor=1)
        page.route(re.compile(r"^https?://"), lambda route: route.abort())
        page.goto(source.resolve().as_uri())
        stage = page.locator(selector)
        stage.wait_for(timeout=60000)
        page.evaluate("document.fonts.ready")
        page.add_style_tag(content=f"{selector} {{position:fixed!important;top:0!important;left:0!important;transform-origin:0 0!important;transform:scale(0.666666667)!important;z-index:2147483647!important;}}")
        duration = float(stage.get_attribute("data-om-exportable-video-with-duration-secs"))
        mp4 = output / "agc-explainer.mp4"
        command = [ffmpeg,"-y","-loglevel","error","-f","image2pipe","-framerate","24","-c:v","mjpeg","-i","pipe:0",
                   "-i",str(wav),"-map","0:v:0","-map","1:a:0","-t",str(duration),"-c:v","libx264","-preset","fast",
                   "-crf","19","-pix_fmt","yuv420p","-c:a","aac","-b:a","144k","-movflags","+faststart",str(mp4)]
        session = page.context.new_cdp_session(page)
        with subprocess.Popen(command, stdin=subprocess.PIPE) as encoder:
            try:
                for i in range(round(duration * 24)):
                    stage.evaluate("(el,t)=>el.dispatchEvent(new CustomEvent('data-om-seek-to-time-frame',{detail:{time:t,sync:true}}))", i/24)
                    frame = base64.b64decode(session.send("Page.captureScreenshot", {"format":"jpeg","quality":91,"fromSurface":True})["data"])
                    encoder.stdin.write(frame)
                    if i == 72:
                        (output / "agc-explainer-capa.jpg").write_bytes(frame)
            finally:
                encoder.stdin.close()
            if encoder.wait():
                raise RuntimeError("Video encoding failed")
        engine.close()
    palette = "fps=9,scale=640:360:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=96:stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle"
    subprocess.run([ffmpeg,"-y","-loglevel","error","-i",str(mp4),"-vf",palette,"-loop","0",str(output/"agc-explainer.gif")],check=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("--browser", required=True)
    parser.add_argument("--out", type=Path, default=Path("docs/media"))
    args = parser.parse_args()
    export(args.source, args.browser, args.out)
