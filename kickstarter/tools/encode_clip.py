"""Turns a GameplayClip frame folder into the campaign formats.

  python tools/encode_clip.py <name> [--start S] [--length L] [--out NAME] [--gif-start S] [--gif-length L]

Reads Marketing/Clips/frames/<name>/ (numbered JPEGs + frames.txt with each frame's real time), puts the frames back
on an even 30 fps using those times (the Editor records unevenly, about 18-20 fps), optionally trims, and writes:
  Marketing/Clips/mp4/<out>.mp4          1920 px wide, H.264, for trailers and social posts
  kickstarter/assets/gifs/<out>.gif      600 px, 12 fps, 112 colours, bayer 4 (keeps ~4 s under 5 MB)
  kickstarter/assets/gifs/<out>.webp     680 px, 24 fps, for Kickstarter (it can show big GIFs as a still)
If the GIF comes out over 5 MB it is redone at fewer colours. Needs ffmpeg on PATH.
"""
import argparse
import os
import subprocess
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
FRAMES = os.path.join(ROOT, "Marketing", "Clips", "frames")
MP4 = os.path.join(ROOT, "Marketing", "Clips", "mp4")
GIFS = os.path.join(ROOT, "kickstarter", "assets", "gifs")
LIMIT = 5 * 1024 * 1024


def ff(*args):
    subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-y", *args], check=True)


def unpack_capture(folder):
    """GameplayClip now writes capture.mkv (hardware HEVC) + frames.txt naming 0000.png...; unpack it to PNGs once."""
    mkv = os.path.join(folder, "capture.mkv")
    if not os.path.exists(mkv):
        return
    first = os.path.join(folder, "0000.png")
    if os.path.exists(first) and os.path.getmtime(first) >= os.path.getmtime(mkv):
        return
    for f in os.listdir(folder):
        if f.endswith(".png"):
            os.remove(os.path.join(folder, f))
    ff("-i", mkv, "-start_number", "0", "-fps_mode", "passthrough", os.path.join(folder, "%04d.png"))


def concat_list(folder):
    """An ffmpeg concat list that shows each frame for as long as it was really on screen."""
    rows = []
    with open(os.path.join(folder, "frames.txt"), encoding="utf-8") as f:
        for line in f:
            parts = line.split()
            if len(parts) == 2 and os.path.exists(os.path.join(folder, parts[0])):
                rows.append((parts[0], float(parts[1])))
    if len(rows) < 2:
        raise SystemExit(f"{folder}: fewer than 2 frames")
    out = []
    for i, (name, t) in enumerate(rows):
        dur = (rows[i + 1][1] - t) if i + 1 < len(rows) else 1 / 30
        out.append(f"file '{os.path.join(folder, name).replace(os.sep, '/')}'\nduration {max(dur, 0.001):.4f}")
    out.append(f"file '{os.path.join(folder, rows[-1][0]).replace(os.sep, '/')}'")
    return "\n".join(out) + "\n", rows[-1][1]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("name")
    ap.add_argument("--start", type=float, default=0.0)
    ap.add_argument("--length", type=float, default=None, help="seconds kept after --start (default: all)")
    ap.add_argument("--out", default=None)
    ap.add_argument("--fps", type=int, default=30, help="output frame rate (60 if the capture ran near 60)")
    ap.add_argument("--gif-length", type=float, default=4.5, help="the GIF/WEBP keep at most this many seconds")
    ap.add_argument("--gif-start", type=float, default=0.0, help="where the GIF/WEBP start, in the trimmed clip")
    a = ap.parse_args()
    folder = os.path.join(FRAMES, a.name)
    out = a.out or a.name
    os.makedirs(MP4, exist_ok=True)
    os.makedirs(GIFS, exist_ok=True)

    unpack_capture(folder)
    text, total = concat_list(folder)
    with tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False, encoding="utf-8") as tf:
        tf.write(text)
        lst = tf.name
    try:
        # 1. An even 30 fps master at full size.
        master = os.path.join(tempfile.gettempdir(), f"{out}_master.mp4")
        trim = ["-ss", f"{a.start}"] + (["-t", f"{a.length}"] if a.length else [])
        ff("-f", "concat", "-safe", "0", "-i", lst, "-vf", f"fps={a.fps},format=yuv420p", "-c:v", "libx264", "-crf", "10",
           "-preset", "fast", "-an", os.path.join(tempfile.gettempdir(), f"{out}_even.mp4"))
        ff(*trim, "-i", os.path.join(tempfile.gettempdir(), f"{out}_even.mp4"), "-c:v", "libx264", "-crf", "12",
           "-preset", "fast", "-an", master)

        # 2. The MP4 for trailers and social.
        mp4 = os.path.join(MP4, f"{out}.mp4")
        ff("-i", master, "-vf", "scale=1920:-2:flags=lanczos", "-c:v", "libx264", "-crf", "16", "-preset", "slow",
           "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-an", mp4)

        # 3. GIF and WEBP: the first --gif-length seconds.
        g = ["-ss", f"{a.gif_start}", "-t", f"{a.gif_length}", "-i", master]
        gif = os.path.join(GIFS, f"{out}.gif")
        for colours in (112, 96, 80, 64):
            ff(*g, "-filter_complex",
               f"fps=12,scale=600:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors={colours}:stats_mode=diff[p];"
               f"[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle", gif)
            if os.path.getsize(gif) <= LIMIT:
                break
        webp = os.path.join(GIFS, f"{out}.webp")
        ff(*g, "-vf", "fps=24,scale=680:-1:flags=lanczos", "-c:v", "libwebp", "-quality", "72", "-compression_level", "6",
           "-loop", "0", "-an", webp)
        mb = lambda p: os.path.getsize(p) / 1048576
        print(f"{out}: source {total:.1f} s | mp4 {mb(mp4):.1f} MB | gif {mb(gif):.2f} MB | webp {mb(webp):.2f} MB")
    finally:
        os.unlink(lst)


if __name__ == "__main__":
    main()
