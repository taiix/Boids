"""Turns Blender's lossless master (_master.mkv) into the delivery MP4:
1920x1080 H.264 High (CRF 14, yuv420p, BT.709, faststart) + AAC 320 kb/s 48 kHz,
audio loudness-normalised (two-pass EBU R128) to -14 LUFS integrated, -1 dBTP.
Run: python Marketing/Trailer/tools/finish.py"""
import json, os, re, subprocess

D = os.path.join(os.path.dirname(__file__), "..")
SRC = os.path.join(D, "_master.mkv")
OUT = os.path.join(D, "ReefRun_Trailer.mp4")
TARGET = "I=-14:TP=-1.0:LRA=11"
# +4 dB into a brick-wall limiter first, so the booms can't stop loudnorm from staying linear
PRE = "volume=4dB,alimiter=limit=0.79:attack=1:release=60:level=false"

p = subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-i", SRC, "-vn",
                    "-af", f"{PRE},loudnorm={TARGET}:print_format=json", "-f", "null", "-"],
                   capture_output=True, text=True)
m = json.loads(re.findall(r"\{[^{}]*\}", p.stderr)[-1])
print("measured", {k: m[k] for k in ("input_i", "input_tp", "input_lra")})
af = (f"{PRE},loudnorm={TARGET}:measured_I={m['input_i']}:measured_TP={m['input_tp']}:"
      f"measured_LRA={m['input_lra']}:measured_thresh={m['input_thresh']}:"
      f"offset={m['target_offset']}:linear=true,aresample=48000")
subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-y", "-i", SRC,
                "-c:v", "libx264", "-preset", "slow", "-crf", "14", "-profile:v", "high", "-level", "4.2",
                "-pix_fmt", "yuv420p", "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709",
                "-g", "60", "-bf", "2",
                "-af", af, "-c:a", "aac", "-b:a", "320k", "-ar", "48000", "-ac", "2",
                "-movflags", "+faststart", OUT], check=True)
print("wrote", OUT)
