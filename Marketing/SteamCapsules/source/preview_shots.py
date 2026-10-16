"""Shot options board: each new plate with the 3D title in the middle of the frame."""
import os, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import compose_capsules as C
import reefrun_logo3d as T

SHOTS = [("option_s1_jaws_below", "S1  Jaws Below"), ("option_s2_flank_pass", "S2  Flank Pass"),
         ("option_s3_face_off", "S3  Face-Off"), ("option_s4_sunburst", "S4  Sunburst")]
font = ImageFont.truetype(os.path.join(HERE, "fonts", "BarlowCondensed-ExtraBold.ttf"), 30)


def centre_scrim(img, strength=0.45):
    """A soft navy pool behind the middle of the frame, so the title holds over bright water."""
    w, h = img.size
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).ellipse((w * 0.18, h * 0.30, w * 0.82, h * 0.70), fill=int(255 * strength))
    m = m.filter(ImageFilter.GaussianBlur(h * 0.10))
    return Image.composite(Image.new("RGB", (w, h), C.NAVY), img, m)


def titled(plate, style="A", hfrac=0.34):
    img = centre_scrim(C.finish(C.plate(plate, (1232, 706)), None)).convert("RGBA")
    w, h = img.size
    lg = T.logo3d(round(h * hfrac), style)
    img.alpha_composite(lg, ((w - lg.width) // 2, (h - lg.height) // 2))
    return img.convert("RGB")


if __name__ == "__main__":
    style = sys.argv[1] if len(sys.argv) > 1 else "A"
    out = os.path.join(ROOT, "previews", "exploration", "shot_options"); os.makedirs(out, exist_ok=True)
    cw, ch = 980, 561
    sheet = Image.new("RGB", (cw * 2 + 60, (ch + 60) * 2 + 20), C.STEAM_BG)
    d = ImageDraw.Draw(sheet)
    for i, (slug, label) in enumerate(SHOTS):
        im = titled(slug, style)
        im.save(os.path.join(out, f"{slug}_title{style}.png"))
        x, y = 20 + (i % 2) * (cw + 20), 20 + (i // 2) * (ch + 60)
        d.text((x, y), label, font=font, fill=(238, 246, 247))
        sheet.paste(im.resize((cw, ch), Image.LANCZOS), (x, y + 44))
    sheet.save(os.path.join(out, f"shot_options_title{style}.png"))
    print("ok")
