"""Builds the Reef Run trailer edit in Blender's Video Sequence Editor.

    blender -b --factory-startup --python Marketing/Trailer/tools/build_trailer.py -- [--render]

Saves Marketing/Trailer/ReefRun_Trailer.blend (open it to tweak the cut by hand).
With --render it also renders a lossless master (FFV1 + FLAC MKV) to Marketing/Trailer/_master.mkv;
finish.ps1 turns that into the delivery H.264 MP4.
All times below are in seconds on the trailer timeline; src = in-point inside the source clip.
"""
import os, sys
import bpy

ROOT = r"D:\Unity\Projects\Boids\Boids"
CLIPS = os.path.join(ROOT, r"Marketing\Clips\mp4")
AUDIO = os.path.join(ROOT, r"Assets\Audio")
SFX = os.path.join(ROOT, r"Marketing\Trailer\sfx")
OUTDIR = os.path.join(ROOT, r"Marketing\Trailer")
FONT = os.path.join(ROOT, r"Assets\UI Toolkit\Fonts\BarlowCondensed-ExtraBold.ttf")
LOGO = os.path.join(ROOT, r"Marketing\SteamCapsules\library\library_logo_1280x429.png")
FPS = 30
TEAL = (0.17, 0.80, 0.80, 1.0)
WHITE = (1, 1, 1, 1)

# Music: "Generic Trailer Music" by Emma_MA (OpenGameArt, CC0 / public domain), one continuous strip.
# Started 46.13 s into the track so its own breakdown (79.3-82.1 s) sits under the riser and the held breath,
# and its re-entry at 82.13 s lands on the climax cut at 36.0. It runs at 76 BPM in a 3-3-2 pulse; the
# climax cuts step in eighth notes (60/152 s).
MUSIC = os.path.join(OUTDIR, "music", "Generic_Trailer_Music_Emma_MA_CC0.mp3")
MUSIC_IN = 46.13
BEAT = 60.0 / 152.0
C = [36.0]
for beats in (6, 4, 4, 6, 4, 4, 4, 8, 8):
    C.append(round(C[-1] + beats * BEAT, 3))
END = 66.6

VIDEO = [  # clip, t0, t1, src_in, options
    # HOOK
    ("50_shark_eyes_school", 0.00, 3.40, 0.00, dict(fade_in=0.8)),
    ("31_shark_hunt", 4.10, 6.40, 0.25, dict(mult=0.72)),           # sonar ring, then a red trail
    # INTRO
    ("54_fish_hides", 6.40, 9.40, 0.00, {}),
    ("55_fish_in_school", 9.40, 11.60, 0.00, {}),
    ("31_shark_hunt", 11.60, 14.00, 3.00, {}),                     # the shark under the sun
    ("54_fish_hides", 14.00, 16.60, 2.95, {}),
    ("25_fish_join_school", 16.60, 19.20, 1.00, {}),               # storm-dark school
    # GAMEPLAY
    ("37_fish_turtle", 19.20, 22.20, 0.50, {}),
    ("41_event_jellyfish", 22.20, 26.40, 1.50, {}),
    ("45_event_trawler", 26.40, 28.80, 2.50, {}),
    ("26_fish_nightfall", 28.80, 31.20, 0.50, {}),
    ("25_fish_join_school", 31.20, 32.40, 3.50, {}),
    ("45_event_trawler", 32.40, 34.10, 5.00, {}),
    ("40_event_red_tide", 34.10, 35.30, 1.80, dict(fade_out=0.3)),
    # CLIMAX (cuts on the music's eighth-note grid)
    ("57_shark_eats_side", C[0], C[1], 0.20, {}),                  # bite at 0.7 -> C0+0.5
    ("31_shark_hunt", C[1], C[2], 0.60, {}),                       # catch at ~0.95 -> C1+0.35
    ("55_fish_in_school", C[2], C[3], 2.00, {}),
    ("30_shark_orcas", C[3], C[4], 1.80, {}),                      # orca ram at 2.5 -> C3+0.7
    ("30_shark_orcas", C[4], C[5], 4.00, {}),
    ("56_shark_breach_side", C[5], C[6], 0.90, {}),                # leaves the water at ~1.0
    ("53_shark_breach", C[6], C[7], 1.90, {}),                     # jaws to camera at 3.0 -> C6+1.1
    ("29_fish_escape", C[7], C[8], 2.60, {}),
    ("51_shark_eats", C[8], C[9], 0.30, {}),                       # final charge, bite at 1.45 -> C8+1.15
    # END: blurred jellyfish reef behind the logo
    ("41_event_jellyfish", 56.0, 62.7, 0.20, dict(fade_in=0.6, fade_out=0.6, mult=0.5, blur=True)),
]

TEXT = [  # text, t0, t1, size, colour, y
    ("THE SHARK HEARS YOU.", 4.40, 6.40, 128, WHITE, 0.50),
    ("3 FISH.", 7.20, 9.30, 140, TEAL, 0.50),
    ("1 SHARK.", 11.90, 13.95, 140, WHITE, 0.50),
    ("HIDE IN THE SCHOOL.", 14.30, 16.55, 104, WHITE, 0.50),
    ("WORK TOGETHER.", 19.60, 22.15, 104, WHITE, 0.50),
    ("SURVIVE 10 MINUTES.", 32.40, 35.25, 112, WHITE, 0.50),
    ("WISHLIST ON STEAM", 58.6, 62.6, 70, TEAL, 0.24),
    ("UNACITE STUDIO", 63.2, 66.5, 84, WHITE, 0.50),
]

A = AUDIO
SOUND = [  # file, t0, t1 (None = natural length), src_in, volume, fade_in, fade_out
    (rf"{A}\Water\AmbientDeep.ogg", 0.0, 36.0, 0.0, 0.50, 1.0, 0.6),
    (rf"{A}\Water\AmbientDeep.ogg", 55.3, END, 10.0, 0.50, 0.5, 1.5),
    (MUSIC, 0.0, C[0] - 0.03, MUSIC_IN, 0.62, 2.5, 0.0),              # build, quieter under the text cards
    (MUSIC, C[0] - 0.03, C[9], MUSIC_IN + C[0] - 0.03, 0.95, 0.0, 0.07),  # climax, cut dead on the smash
    (rf"{A}\Tension\Tension_Drone.ogg", 55.4, END, 0.0, 0.40, 1.0, 2.0),
    # hook
    (rf"{A}\Fish\Heartbeat.ogg", 0.6, None, 0.0, 1.0, 0, 0),
    (rf"{A}\Fish\Heartbeat.ogg", 1.7, None, 0.0, 1.0, 0, 0),
    (rf"{A}\Fish\Heartbeat.ogg", 2.8, None, 0.0, 1.0, 0, 0),
    (rf"{SFX}\whoosh.wav", 1.95, None, 0.0, 0.70, 0, 0),
    (rf"{SFX}\boom_big.wav", 3.40, None, 0.0, 0.62, 0, 0),
    (rf"{A}\Shark\Sonar.mp3", 4.25, None, 0.0, 0.95, 0, 0),
    (rf"{SFX}\hit.wav", 4.40, None, 0.0, 0.55, 0, 0),
    (rf"{A}\Shark\Bite_1.ogg", 4.80, None, 0.0, 0.55, 0, 0),
    # intro
    (rf"{SFX}\hit.wav", 7.20, None, 0.0, 0.65, 0, 0),
    (rf"{SFX}\hit.wav", 11.90, None, 0.0, 0.80, 0, 0),
    (rf"{A}\Tension\Tension_Sting.ogg", 11.90, 14.6, 0.0, 0.50, 0, 1.0),
    (rf"{SFX}\whoosh_fast.wav", 13.85, None, 0.0, 0.40, 0, 0),
    # gameplay
    (rf"{SFX}\whoosh.wav", 26.25, None, 0.0, 0.55, 0, 0),
    (rf"{A}\Tension\Tension_Sting.ogg", 26.40, 29.0, 0.0, 0.50, 0, 1.0),
    (rf"{A}\Events\Thunder.ogg", 31.25, None, 0.0, 0.90, 0, 0),
    (rf"{SFX}\hit.wav", 32.40, None, 0.0, 0.60, 0, 0),
    (rf"{A}\Events\NetCatch.ogg", 33.35, None, 0.0, 0.80, 0, 0),
    (rf"{SFX}\riser.wav", 32.40, None, 0.0, 0.80, 0, 0),
    # climax
    (rf"{SFX}\boom_big.wav", C[0], None, 0.0, 0.62, 0, 0),
    (rf"{A}\Shark\Bite_1.ogg", C[0] + 0.50, None, 0.0, 0.85, 0, 0),
    (rf"{A}\Shark\Bite_2.ogg", C[1] + 0.35, None, 0.0, 0.85, 0, 0),
    (rf"{SFX}\whoosh_fast.wav", C[2] - 0.15, None, 0.0, 0.45, 0, 0),
    (rf"{A}\Shark\Bite_3.ogg", C[3] + 0.70, None, 0.0, 0.85, 0, 0),
    (rf"{A}\Shark\Bite_1.ogg", C[4] + 0.30, None, 0.0, 0.60, 0, 0),
    (rf"{SFX}\whoosh_fast.wav", C[5] - 0.15, None, 0.0, 0.45, 0, 0),
    (rf"{SFX}\hit.wav", C[5] + 0.10, None, 0.0, 0.70, 0, 0),           # breach: the shark clears the surface
    (rf"{A}\Shark\Bite_2.ogg", C[6] + 1.10, None, 0.0, 0.80, 0, 0),    # breach: jaws to camera
    (rf"{SFX}\whoosh_fast.wav", C[8] - 0.15, None, 0.0, 0.45, 0, 0),
    (rf"{SFX}\whoosh.wav", C[8] + 0.40, None, 0.0, 0.75, 0, 0),
    (rf"{A}\Shark\Bite_3.ogg", C[8] + 1.15, None, 0.0, 1.0, 0, 0),
    # ending
    (rf"{SFX}\boom_big.wav", C[9], None, 0.0, 0.62, 0, 0),
    (rf"{SFX}\boom_soft.wav", 56.40, None, 0.0, 0.5, 0, 0),
    (rf"{SFX}\hit.wav", 58.60, None, 0.0, 0.35, 0, 0),
    (rf"{A}\Shark\Sonar.mp3", 63.10, None, 0.0, 0.60, 0, 0),
]


def F(t):
    return int(round(t * FPS)) + 1


def key_alpha(s, pts):
    for t, a in pts:
        s.blend_alpha = a
        s.keyframe_insert("blend_alpha", frame=F(t))


def fades(s, t0, t1, fin, fout, attr="blend_alpha", level=1.0):
    pts = []
    if fin:
        pts += [(t0, 0.0), (t0 + fin, level)]
    if fout:
        pts += [(t1 - fout, level), (t1, 0.0)]
    for t, a in pts:
        setattr(s, attr, a)
        s.keyframe_insert(attr, frame=F(t))
    if not pts:
        setattr(s, attr, level)


def trim(s, t0, t1, src_in):
    s.frame_start = F(t0) - int(round(src_in * FPS))
    s.frame_final_start = F(t0)
    s.frame_final_end = F(t1)


def build():
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = 1920, 1080, 100
    sc.render.fps, sc.render.fps_base = FPS, 1.0
    sc.frame_start, sc.frame_end = 1, F(END) - 1
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.render.film_transparent = False
    sc.audio_volume = 0.7  # mix headroom; finish.py normalises loudness
    se = sc.sequence_editor_create()
    st = se.strips
    font = bpy.data.fonts.load(FONT)

    ch = {"v": 2}
    for i, (clip, t0, t1, src, o) in enumerate(VIDEO):
        chan = 2 + (i % 2)  # alternate so abutting cuts are easy to see in the UI
        s = st.new_movie(f"V{i:02d}_{clip}", os.path.join(CLIPS, clip + ".mp4"), chan, 1, fit_method="FIT")
        trim(s, t0, t1, src)
        s.blend_type = "ALPHA_OVER"
        if "mult" in o:
            s.color_multiply = o["mult"]
        fades(s, t0, t1, o.get("fade_in", 0), o.get("fade_out", 0))
        if o.get("blur"):
            s.transform.scale_x = s.transform.scale_y = 1.35
            b = st.new_effect("EndBlur", "GAUSSIAN_BLUR", 4, frame_start=F(t0), length=F(t1) - F(t0), input1=s)
            b.size_x = b.size_y = 18
            b.blend_type = "ALPHA_OVER"
            s.mute = False
            fades(b, t0, t1, o.get("fade_in", 0), o.get("fade_out", 0))

    # white flash on the climax drop
    fl = st.new_effect("Flash", "COLOR", 7, frame_start=F(C[0]), length=6)
    fl.color = (1, 1, 1)
    fl.blend_type = "ADD"
    key_alpha(fl, [(C[0], 0.85), (C[0] + 5 / FPS, 0.0)])

    # logo
    lg = st.new_image("Logo", LOGO, 6, F(56.4), fit_method="ORIGINAL")
    lg.frame_final_end = F(62.7)
    lg.blend_type = "ALPHA_OVER"
    lg.transform.offset_y = 70
    for t, sc_ in ((56.4, 0.84), (62.7, 0.92)):  # slow push-in
        lg.transform.scale_x = lg.transform.scale_y = sc_
        lg.transform.keyframe_insert("scale_x", frame=F(t))
        lg.transform.keyframe_insert("scale_y", frame=F(t))
    fades(lg, 56.4, 62.7, 0.9, 0.5)

    for i, (txt, t0, t1, size, col, y) in enumerate(TEXT):
        s = st.new_effect(f"T{i:02d}", "TEXT", 8, frame_start=F(t0), length=F(t1) - F(t0))
        s.text = txt
        s.font = font
        s.font_size = size
        s.color = col
        s.location = (0.5, y)
        s.anchor_x = s.anchor_y = "CENTER"
        s.use_shadow = True
        s.shadow_color = (0, 0.03, 0.08, 0.85)
        s.shadow_blur = 0.35
        s.shadow_offset = 0.04
        s.blend_type = "ALPHA_OVER"
        fades(s, t0, t1, 0.2, 0.3)
        # small settle: starts slightly bigger
        for t, k in ((t0, 1.06), (t0 + 0.35, 1.0)):
            s.transform.scale_x = s.transform.scale_y = k
            s.transform.keyframe_insert("scale_x", frame=F(t))
            s.transform.keyframe_insert("scale_y", frame=F(t))

    lanes = []  # greedy channel allocation for sound strips
    for i, (path, t0, t1, src, vol, fin, fout) in enumerate(SOUND):
        s = st.new_sound(f"A{i:02d}_{os.path.basename(path)}", path, 10, 1)
        if t1 is None:
            t1 = t0 + (s.frame_final_end - s.frame_final_start - int(round(src * FPS))) / FPS
        f0, f1 = F(t0), F(t1)
        lane = next((k for k, end in enumerate(lanes) if end <= f0), None)
        if lane is None:
            lanes.append(0); lane = len(lanes) - 1
        lanes[lane] = f1
        s.channel = 10 + lane
        trim(s, t0, t1, src)
        fades(s, t0, t1, fin, fout, attr="volume", level=vol)

    r = sc.render
    r.image_settings.media_type = "VIDEO" if hasattr(r.image_settings, "media_type") else None
    r.image_settings.file_format = "FFMPEG"
    r.ffmpeg.format = "MKV"
    r.ffmpeg.codec = "FFV1"
    r.ffmpeg.audio_codec = "FLAC"
    r.ffmpeg.audio_mixrate = 48000
    r.ffmpeg.audio_channels = "STEREO"
    r.filepath = os.path.join(OUTDIR, "_master.mkv")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUTDIR, "ReefRun_Trailer.blend"))
    print("BUILT", sc.frame_end, "frames,", len(st), "strips; climax cuts", C)


build()
if "--render" in sys.argv:
    bpy.ops.render.render(animation=True)
    print("RENDERED", bpy.context.scene.render.filepath)
