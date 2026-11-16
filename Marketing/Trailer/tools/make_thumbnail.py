"""Trailer thumbnail (1920x1080 PNG + JPG): the breaching shark coming at the camera, jaws open (53_shark_breach),
the Reef Run logo and the hook line. Run: python Marketing/Trailer/tools/make_thumbnail.py"""
import io, os, subprocess
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageEnhance

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT = os.path.join(ROOT, "Marketing", "Trailer")
CLIP = os.path.join(ROOT, "Marketing", "Clips", "mp4", "53_shark_breach.mp4")
LOGO = os.path.join(ROOT, "Marketing", "SteamCapsules", "library", "library_logo_1280x429.png")
FONT = os.path.join(ROOT, "Assets", "UI Toolkit", "Fonts", "BarlowCondensed-ExtraBold.ttf")
T = 3.0  # breaching shark, jaws open, coming at the camera

png = subprocess.run(["ffmpeg", "-nostdin", "-v", "error", "-ss", str(T), "-i", CLIP, "-frames:v", "1",
                      "-vf", "scale=1920:1080:flags=lanczos", "-f", "image2pipe", "-vcodec", "png", "-"],
                     capture_output=True, check=True).stdout
bg = Image.open(io.BytesIO(png)).convert("RGB")
bg = ImageEnhance.Contrast(bg).enhance(1.12)
bg = ImageEnhance.Color(bg).enhance(1.08)

# darken top band for the logo, bottom band for the line
W, H = bg.size
shade = Image.new("L", (W, H), 0)
d = ImageDraw.Draw(shade)
for y in range(H):
    top = max(0.0, 1 - y / (H * 0.42)) * 170
    bot = max(0.0, (y - H * 0.70) / (H * 0.30)) * 190
    d.line([(0, y), (W, y)], fill=int(max(top, bot)))
bg = Image.composite(Image.new("RGB", (W, H), (2, 14, 28)), bg, shade)

logo = Image.open(LOGO).convert("RGBA")
lw = 1000
logo = logo.resize((lw, int(logo.height * lw / logo.width)), Image.LANCZOS)
bg.paste(logo, ((W - lw) // 2, 10), logo)

font = ImageFont.truetype(FONT, 120)
txt = "THE SHARK HEARS YOU."
tw = ImageDraw.Draw(bg).textlength(txt, font=font)
x, y = (W - tw) / 2, H - 190
glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
ImageDraw.Draw(glow).text((x, y + 6), txt, font=font, fill=(0, 8, 20, 230))
glow = glow.filter(ImageFilter.GaussianBlur(10))
bg.paste(glow, (0, 0), glow)
ImageDraw.Draw(bg).text((x, y), txt, font=font, fill=(255, 255, 255))

bg.save(os.path.join(OUT, "ReefRun_Trailer_Thumbnail.png"))
bg.save(os.path.join(OUT, "ReefRun_Trailer_Thumbnail.jpg"), quality=93)
print("thumbnail written")
