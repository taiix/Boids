"""Contact sheet of a take: 8 evenly spaced frames. python -I sheet.py <name> [out.jpg]"""
import os
import sys
from PIL import Image

name = sys.argv[1]
d = os.path.join(r"D:\Unity\Projects\Boids\Boids\Marketing\Clips\frames", name)
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "sheet.jpg")
fs = sorted(f for f in os.listdir(d) if f.endswith((".jpg", ".png")))
picks = [fs[int(i * (len(fs) - 1) / 7)] for i in range(8)]
sheet = Image.new("RGB", (1920, 540))
for i, f in enumerate(picks):
    im = Image.open(os.path.join(d, f)).convert("RGB").resize((480, 270))
    sheet.paste(im, ((i % 4) * 480, (i // 4) * 270))
sheet.save(out, quality=80)
print(f"{name}: {len(fs)} frames")
