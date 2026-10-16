"""
The seabed crab, exported for Unity, with the animations the game needs.

    blender -b --disable-autoexec <Crab.blend> -P export_crab.py -- <out_dir> [preview_dir]

Writes (the .blend itself is never saved):
  Crab.fbx   the crab, thinned to a game-sized mesh (no leg hair - particles don't export), its rig without
             the IK controls (baked into the deform bones), and three takes:
               Walk    the source's sideways walk cycle (12 frames)
               Idle    breathing, eye stalks looking about, the claws taking turns picking at the sand and
                       lifting food to the mouth (4 s)
               Threat  body up and tilted back, claws raised wide open, snapping (2 s)
               Turn    stepping round on the spot, the walk's alternating sets of legs (0.6 s cycle)
               Groom   the claws rubbing the eye stalks clean, then wiping the mouth (3 s, once)
               Wave    one claw raised and waved (2.4 s, once)
               Startle a flinch: body down, claws in, eye stalks folded (0.6 s, once)
               Dig     wriggling down into the sand until only the eyes are out (2 s, once)
               Hidden  buried, eyes out, looking about (3 s)
               Emerge  pushing back up out of the sand (1.2 s, once)
  [preview_dir] stills of the new poses, to check them.

Blender: the crab faces -y and walks along x (sideways); up +z. Rig object scaled 0.08: pose positions below
are in rig units (metres / 0.08).
"""
import bpy, math, os, sys
from mathutils import Matrix, Quaternion, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.abspath(argv[0])
PREVIEW = os.path.abspath(argv[1]) if len(argv) > 1 else None
os.makedirs(OUT, exist_ok=True)
TARGET_TRIS = 8000

scene = bpy.context.scene
arm = bpy.data.objects["Crab_Rig"]
body = bpy.data.objects["Crab_low"]
UNIT = arm.scale.x                       # rig units -> metres
FPS = scene.render.fps

# ------------------------------------------------------------------ the crab only, game-sized

for o in list(bpy.data.objects):
    if o not in (arm, body):
        bpy.data.objects.remove(o, do_unlink=True)
for m in [m for m in body.modifiers if m.type == 'PARTICLE_SYSTEM']:
    body.modifiers.remove(m)

tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
if tris > TARGET_TRIS:
    dec = body.modifiers.new("Thin", 'DECIMATE')
    dec.ratio = TARGET_TRIS / tris
    with bpy.context.temp_override(object=body, active_object=body, selected_objects=[body]):
        bpy.ops.object.modifier_move_to_index(modifier="Thin", index=0)   # before the armature: thin the rest shape
        bpy.ops.object.modifier_apply(modifier="Thin")
print(f"[crab] mesh {tris} -> {sum(len(p.vertices) - 2 for p in body.data.polygons)} triangles")

# The IK controls drive the legs and claws in Blender; Unity gets the bones they move, baked.
for b in arm.data.bones:
    if b.name.startswith("IK."):
        b.use_deform = False

pb = arm.pose.bones
walk = arm.animation_data.action
walk.name = "Walk"

# ------------------------------------------------------------------ the walk: how fast it walks

# A foot on the ground slides back under the body at the walking speed: measure it, so the game can match
# its movement to the cycle (no skating feet).
def foot_track(name):
    out = []
    for f in range(int(walk.frame_range[0]), int(walk.frame_range[1]) + 1):
        scene.frame_set(f)
        out.append(pb[name].matrix.translation.copy())
    return out

speeds = []
pushes = 0.0
for leg in ("leg1.L", "leg2.L", "leg3.L", "leg4.L", "leg1.R", "leg2.R", "leg3.R", "leg4.R"):
    t = foot_track("IK.Root." + leg)
    floor = min(p.z for p in t)
    for a, b in zip(t, t[1:]):
        if a.z < floor + 0.02 / UNIT and b.z < floor + 0.02 / UNIT:   # both frames on the ground
            speeds.append(abs(b.x - a.x) * UNIT * FPS)
            pushes += b.x - a.x
speeds.sort()
stride_speed = speeds[len(speeds) // 2] if speeds else 0.3
print(f"[crab] walk cycle {(walk.frame_range[1] - walk.frame_range[0]) / FPS:.2f} s, feet on the ground move at "
      f"{stride_speed:.3f} m/s (the speed to walk at, at this size)")
# Feet on the ground push back: the body goes the other way. It faces -y, so +x is its left.
print(f"[crab] played forward, the walk carries it toward its {'left' if pushes < 0 else 'right'} (+x is its left)")

# ------------------------------------------------------------------ helpers for the new takes

scene.frame_set(0)
base = {}
for p in pb:
    base[p.name] = (p.location.copy(), p.rotation_quaternion.copy(), p.rotation_euler.copy(), p.scale.copy())
feet = {leg: pb["IK.Root." + leg].matrix.translation.copy() for leg in
        ("leg1.L", "leg2.L", "leg3.L", "leg4.L", "leg1.R", "leg2.R", "leg3.R", "leg4.R")}
foot_floor = min(p.z for p in feet.values())
hand_rest = {s: pb["IK.Root.Hand." + s].matrix.translation.copy() for s in ("L", "R")}
body_rest = pb["Body"].matrix.copy()

# Which way a claw's movable finger opens: the turn of Hand.5 that takes its tip furthest from the fixed one.
def finger_gap(side):
    scene.frame_set(0)
    bpy.context.view_layer.update()
    return (pb["Hand.7." + side].tail - pb["Hand.6." + side].tail).length

def pick_open_axis(side):
    hand = pb["Hand.5." + side]
    keep = hand.rotation_mode, hand.rotation_quaternion.copy(), hand.rotation_euler.copy()
    hand.rotation_mode = 'QUATERNION'
    best = (0.0, Vector((1, 0, 0)))
    closed = finger_gap(side)
    for axis in (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))):
        for sign in (1, -1):
            hand.rotation_quaternion = keep[1] @ Quaternion(axis * sign, math.radians(30))
            gap = finger_gap(side) - closed
            if gap > best[0]:
                best = (gap, axis * sign)
    hand.rotation_mode, hand.rotation_quaternion, hand.rotation_euler = keep
    print(f"[crab] {side} claw opens about {tuple(best[1])} (+{best[0] * UNIT * 100:.1f} cm at 30 degrees)")
    return best[1]

open_axis = {s: pick_open_axis(s) for s in ("L", "R")}

# Which way each eye stalk tips down: the left and right bones are mirrored, so the same turn folds one and
# lifts the other. Measure it: the turn about its own x that lowers its tip.
def pick_eye_down(side):
    stalk = pb["Eye.1." + side]
    keep = stalk.rotation_mode, stalk.rotation_quaternion.copy(), stalk.rotation_euler.copy()
    stalk.rotation_mode = 'QUATERNION'
    heights = {}
    for sign in (1, -1):
        stalk.rotation_quaternion = keep[1] @ Quaternion((1, 0, 0), math.radians(40 * sign))
        bpy.context.view_layer.update()
        heights[sign] = pb["Eye.3." + side].tail.z
    stalk.rotation_mode, stalk.rotation_quaternion, stalk.rotation_euler = keep
    bpy.context.view_layer.update()
    down = 1 if heights[1] < heights[-1] else -1
    print(f"[crab] {side} eye stalk tips down turning {'+' if down > 0 else '-'}x")
    return down

eye_down = {s: pick_eye_down(s) for s in ("L", "R")}

def new_take(name, frames):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data.action = act
    scene.frame_start, scene.frame_end = 0, frames
    return act

def key_base(frame):
    """Every bone as in the walk's first frame (the legs keep its bend), keyed."""
    for p in pb:
        loc, quat, eul, scl = base[p.name]
        p.location, p.rotation_quaternion, p.rotation_euler, p.scale = loc, quat, eul, scl
        p.keyframe_insert("location", frame=frame)
        p.keyframe_insert("rotation_quaternion" if p.rotation_mode == 'QUATERNION' else "rotation_euler", frame=frame)

def put(name, where, frame):
    """A control (no parent) to a point in rig space, keyed."""
    p = pb[name]
    m = p.matrix.copy()
    m.translation = where
    p.matrix = m
    p.keyframe_insert("location", frame=frame)

def body_pose(frame, lift=0.0, pitch=0.0, roll=0.0):
    """The body raised (metres) and tilted (degrees: pitch + = front up, roll + = right side up), keyed."""
    p = pb["Body"]
    pivot = body_rest.translation
    turn = Matrix.Rotation(math.radians(pitch), 4, 'X') @ Matrix.Rotation(math.radians(roll), 4, 'Y')
    m = Matrix.Translation(pivot + Vector((0, 0, lift / UNIT))) @ turn @ Matrix.Translation(-pivot) @ body_rest
    p.matrix = m
    p.keyframe_insert("location", frame=frame)
    p.keyframe_insert("rotation_quaternion" if p.rotation_mode == 'QUATERNION' else "rotation_euler", frame=frame)

def claw(side, frame, opening):
    """A claw's movable finger, 0 = as in the walk, 1 = wide open (about 35 degrees), keyed."""
    p = pb["Hand.5." + side]
    loc, quat, eul, scl = base[p.name]
    q = (quat if p.rotation_mode == 'QUATERNION' else eul.to_quaternion()) @ Quaternion(open_axis[side], math.radians(35 * opening))
    if p.rotation_mode == 'QUATERNION':
        p.rotation_quaternion = q
        p.keyframe_insert("rotation_quaternion", frame=frame)
    else:
        p.rotation_euler = q.to_euler(p.rotation_mode)
        p.keyframe_insert("rotation_euler", frame=frame)

def eye(side, frame, yaw, pitch):
    """An eye stalk turned (yaw) and tipped (pitch: + up, - down toward folding flat), keyed."""
    p = pb["Eye.1." + side]
    loc, quat, eul, scl = base[p.name]
    tip = -pitch * eye_down[side]
    q = (quat if p.rotation_mode == 'QUATERNION' else eul.to_quaternion()) @ Quaternion((0, 0, 1), math.radians(yaw)) @ Quaternion((1, 0, 0), math.radians(tip))
    if p.rotation_mode == 'QUATERNION':
        p.rotation_quaternion = q
        p.keyframe_insert("rotation_quaternion", frame=frame)
    else:
        p.rotation_euler = q.to_euler(p.rotation_mode)
        p.keyframe_insert("rotation_euler", frame=frame)

def plant_feet(frame, spread=1.0):
    """All eight feet on the ground under their walking stance (wider with spread > 1), keyed."""
    for leg, at in feet.items():
        p = Vector((at.x * spread, at.y * (1 + (spread - 1) * 0.5), foot_floor))
        put("IK.Root." + leg, p, frame)

def smooth(act):
    for fc in fcurves(act):
        for k in fc.keyframe_points:
            k.interpolation = 'BEZIER'
            k.handle_left_type = k.handle_right_type = 'AUTO_CLAMPED'

def fcurves(act):
    try:
        for layer in act.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    yield from bag.fcurves
    except AttributeError:
        yield from act.fcurves

def to_mouth(side):
    # In front of the mouth: between the claws, a little below and behind where they rest.
    r = hand_rest[side]
    return Vector((r.x * 0.35, r.y * 0.75, r.z * 0.85))

def to_sand(side):
    # Reaching down and forward to the sand.
    r = hand_rest[side]
    return Vector((r.x * 1.15, r.y * 1.35, foot_floor + 0.012 / UNIT))

# ------------------------------------------------------------------ Idle: 4 s

IDLE = 4 * FPS
idle = new_take("Idle", IDLE)
for f in (0, IDLE):
    key_base(f)
for f in range(0, IDLE + 1, 5):
    plant_feet(f)
    t = f / FPS
    # Breathing: a slow rise and fall with a little sway.
    body_pose(f, lift=0.0025 * math.sin(2 * math.pi * t / 2.0), roll=1.2 * math.sin(2 * math.pi * t / 4.0))
# Feeding: each claw in turn down to the sand (opening), a pinch, then up to the mouth (closed), and back.
for side, start in (("L", 0.3), ("R", 2.3)):
    def at(sec):
        return int(round((start + sec) * FPS))
    put("IK.Root.Hand." + side, hand_rest[side], 0)
    put("IK.Root.Hand." + side, hand_rest[side], at(0.0))
    claw(side, at(0.0), 0.0)
    put("IK.Root.Hand." + side, to_sand(side), at(0.45))
    claw(side, at(0.35), 0.8)
    claw(side, at(0.55), 0.0)          # the pinch
    put("IK.Root.Hand." + side, to_sand(side), at(0.6))
    put("IK.Root.Hand." + side, to_mouth(side), at(1.05))
    put("IK.Root.Hand." + side, to_mouth(side), at(1.3))
    put("IK.Root.Hand." + side, hand_rest[side], at(1.7))
    claw(side, at(1.7), 0.0)
    put("IK.Root.Hand." + side, hand_rest[side], IDLE)
    claw(side, IDLE, 0.0)
# Eye stalks: looking about, now and then.
for f, yaw, pitch in ((0, 0, 0), (20, 12, 4), (38, 12, 4), (48, -8, 0), (70, -8, 0), (80, 4, -5), (IDLE, 0, 0)):
    eye("L", f, yaw, pitch)
    eye("R", f, yaw * 0.8, pitch)
smooth(idle)

# ------------------------------------------------------------------ Threat: 2 s, claws up

THREAT = 2 * FPS
threat = new_take("Threat", THREAT)
for f in (0, THREAT):
    key_base(f)
for f in range(0, THREAT + 1, 5):
    t = f / FPS
    plant_feet(f, spread=1.12)
    body_pose(f, lift=0.018 + 0.002 * math.sin(2 * math.pi * t / 1.0), pitch=10 + 1.5 * math.sin(2 * math.pi * t / 2.0))
    for side, sx in (("L", 1), ("R", -1)):
        r = hand_rest[side]
        sway = 0.006 / UNIT * math.sin(2 * math.pi * t / 2.0 + (0 if side == "L" else 1.3))
        # High and spread, a little forward: the classic display.
        up = Vector((r.x * 1.8, r.y * 1.05, r.z + 0.14 / UNIT + sway))
        put("IK.Root.Hand." + side, up, f)
# Claws wide open, two quick snaps.
for side in ("L", "R"):
    for f, opening in ((0, 1.0), (14, 1.0), (17, 0.0), (21, 1.0), (31, 1.0), (34, 0.0), (38, 1.0), (THREAT, 1.0)):
        claw(side, f, opening)
for f, yaw in ((0, 0), (25, 6), (THREAT, 0)):
    eye("L", f, yaw, 6)
    eye("R", f, -yaw, 6)
smooth(threat)


# ------------------------------------------------------------------ Turn: 0.6 s cycle, stepping round

# Turning on the spot the way crabs do: the same alternating sets of four legs as the walk, each foot lifting
# and stepping round the body while the other set holds it. Played forward the feet push it round
# anticlockwise seen from above (here; the game measures which way that is once imported).
TURN = 15
TURN_STEP = 16.0      # degrees each foot carries the body round per step
GROUP_A = ("leg1.L", "leg2.R", "leg3.L", "leg4.R")
centre = sum((Vector((v.x, v.y, 0)) for v in feet.values()), Vector()) / len(feet)
turn = new_take("Turn", TURN)
for f in (0, TURN):
    key_base(f)
for f in range(0, TURN + 1):
    t = f / TURN
    for leg, at in feet.items():
        phase = (t + (0.0 if leg in GROUP_A else 0.5)) % 1.0
        if phase < 0.5:       # on the ground, pushing round: slides back as the body turns
            angle = TURN_STEP * 0.5 * (1 - 4 * phase)      # +half a step to -half: one whole step
            lift = 0.0
        else:                 # in the air, reaching forward round for the next step
            q = (phase - 0.5) / 0.5
            angle = -TURN_STEP * 0.5 + TURN_STEP * q
            lift = 0.022 / UNIT * math.sin(math.pi * q)
        offset = Vector((at.x, at.y, 0)) - centre
        offset.rotate(Matrix.Rotation(math.radians(angle), 3, 'Z'))
        put("IK.Root." + leg, Vector((centre.x + offset.x, centre.y + offset.y, foot_floor + lift)), f)
    body_pose(f, lift=0.003 * abs(math.sin(2 * math.pi * t)), roll=1.5 * math.sin(2 * math.pi * t))
for side in ("L", "R"):
    put("IK.Root.Hand." + side, hand_rest[side], 0)
    put("IK.Root.Hand." + side, hand_rest[side], TURN)
smooth(turn)

# ------------------------------------------------------------------ Groom: 3 s, cleaning the eyes

# The claws come up to the eye stalks and rub them clean, a few short strokes, then wipe at the mouth.
GROOM = 75
groom = new_take("Groom", GROOM)
for f in (0, GROOM):
    key_base(f)
for f in range(0, GROOM + 1, 5):
    plant_feet(f)
    body_pose(f, lift=0.002, pitch=-3 if 8 < f < 60 else 0)
for side, sx in (("L", 1), ("R", -1)):
    r = hand_rest[side]
    at_eye = Vector((sx * 0.42, -1.2, 1.42))          # in front of its eye stalk (rig units)
    put("IK.Root.Hand." + side, r, 0)
    put("IK.Root.Hand." + side, at_eye, 12)
    for k, f in enumerate(range(15, 42, 5)):          # strokes: up and in, down and out
        rub = Vector((sx * -0.05, 0.0, 0.07)) if k % 2 == 0 else Vector((sx * 0.03, 0.0, -0.05))
        put("IK.Root.Hand." + side, at_eye + rub, f + (2 if side == "R" else 0))
    put("IK.Root.Hand." + side, to_mouth(side), 50)
    put("IK.Root.Hand." + side, to_mouth(side) + Vector((0, 0, 0.05)), 55)
    put("IK.Root.Hand." + side, to_mouth(side), 60)
    put("IK.Root.Hand." + side, r, GROOM)
    claw(side, 0, 0.0)
    claw(side, 12, 0.3)
    claw(side, 45, 0.3)
    claw(side, GROOM, 0.0)
for f, pitch in ((0, 0), (12, -10), (44, -10), (52, 0), (GROOM, 0)):
    eye("L", f, 0, pitch)
    eye("R", f, 0, pitch)
smooth(groom)

# ------------------------------------------------------------------ Wave: 2.4 s, a claw raised and waved

WAVE = 60
wave = new_take("Wave", WAVE)
for f in (0, WAVE):
    key_base(f)
for f in range(0, WAVE + 1, 5):
    plant_feet(f)
    body_pose(f, lift=0.008 if 6 < f < 54 else 0.0, roll=-3 if 6 < f < 54 else 0)
r = hand_rest["R"]
high = Vector((r.x * 1.6, r.y * 1.0, r.z + 0.13 / UNIT))
put("IK.Root.Hand.R", r, 0)
put("IK.Root.Hand.R", high, 10)
for k, f in enumerate(range(16, 46, 6)):              # waving: down a little, back up
    put("IK.Root.Hand.R", high + Vector((0.05, 0.0, -0.45 if k % 2 == 0 else 0.0)), f)
put("IK.Root.Hand.R", high, 48)
put("IK.Root.Hand.R", r, WAVE)
for f, opening in ((0, 0.0), (10, 0.9), (46, 0.9), (WAVE, 0.0)):
    claw("R", f, opening)
put("IK.Root.Hand.L", hand_rest["L"], 0)
put("IK.Root.Hand.L", hand_rest["L"], WAVE)
for f, yaw in ((0, 0), (12, -8), (48, -8), (WAVE, 0)):
    eye("L", f, yaw, 4)
    eye("R", f, yaw, 4)
smooth(wave)

# ------------------------------------------------------------------ Startle: 0.6 s, a flinch

# Everything pulled in: the body drops, the claws come in to the mouth, the eye stalks fold down.
STARTLE = 15
EYE_FOLD = -55
startle = new_take("Startle", STARTLE)
for f in (0, STARTLE):
    key_base(f)
for f in range(0, STARTLE + 1):
    plant_feet(f, spread=1.0 + 0.06 * min(1.0, f / 3))
for f, lift, pitch in ((0, 0.0, 0), (3, -0.014, -4), (STARTLE, -0.012, -3)):
    body_pose(f, lift=lift, pitch=pitch)
for side in ("L", "R"):
    put("IK.Root.Hand." + side, hand_rest[side], 0)
    put("IK.Root.Hand." + side, to_mouth(side) + Vector((0, -0.1, -0.12)), 3)
    put("IK.Root.Hand." + side, to_mouth(side) + Vector((0, -0.1, -0.12)), STARTLE)
    claw(side, 0, 0.0)
    claw(side, 3, 0.0)
for f, pitch in ((0, 0), (3, EYE_FOLD), (STARTLE, EYE_FOLD)):
    eye("L", f, 0, pitch)
    eye("R", f, 0, pitch)
smooth(startle)

# ------------------------------------------------------------------ Dig / Hidden / Emerge: into the sand

# Digging in: wriggling side to side, legs scraping, the body sinking until only the eyes are out.
DIG = 50
SUNK = -0.16            # metres the body ends up below where it stands: the shell's top level with the sand
dig = new_take("Dig", DIG)
for f in (0, DIG):
    key_base(f)
for f in range(0, DIG + 1, 2):
    t = f / DIG
    sink = SUNK * (t * t * (3 - 2 * t))
    body_pose(f, lift=sink, roll=7 * math.sin(2 * math.pi * f / 8) * (1 - t * 0.5), pitch=-4 * t)
    for leg, at in feet.items():
        scrape = 0.03 / UNIT * math.sin(2 * math.pi * f / 8 + (0 if leg in GROUP_A else math.pi))
        put("IK.Root." + leg, Vector((at.x + scrape * (1 if at.x > 0 else -1), at.y, foot_floor + sink / UNIT)), f)
    for side in ("L", "R"):
        tuck = to_mouth(side) + Vector((0, -0.1, -0.1))
        put("IK.Root.Hand." + side, Vector((tuck.x, tuck.y, tuck.z + sink / UNIT)), f)
for f, pitch in ((0, 0), (DIG, 8)):                 # eyes up, peeking
    eye("L", f, 0, pitch)
    eye("R", f, 0, pitch)
smooth(dig)

# Hidden: buried, eyes out, turning slowly to look about.
HIDDEN = 75
hidden = new_take("Hidden", HIDDEN)
for f in (0, HIDDEN):
    key_base(f)
for f in range(0, HIDDEN + 1, 5):
    body_pose(f, lift=SUNK + 0.002 * math.sin(2 * math.pi * f / HIDDEN), pitch=-4)
    for leg, at in feet.items():
        put("IK.Root." + leg, Vector((at.x, at.y, foot_floor + SUNK / UNIT)), f)
    for side in ("L", "R"):
        tuck = to_mouth(side) + Vector((0, -0.1, -0.1))
        put("IK.Root.Hand." + side, Vector((tuck.x, tuck.y, tuck.z + SUNK / UNIT)), f)
for f, yaw in ((0, 0), (15, 18), (30, 18), (45, -15), (62, -15), (HIDDEN, 0)):
    eye("L", f, yaw, 8)
    eye("R", f, yaw, 8)
smooth(hidden)

# Emerge: pushing back up out of the sand, shaking it off.
EMERGE = 30
emerge = new_take("Emerge", EMERGE)
for f in (0, EMERGE):
    key_base(f)
for f in range(0, EMERGE + 1, 2):
    t = f / EMERGE
    rise = SUNK * (1 - t * t * (3 - 2 * t))
    body_pose(f, lift=rise, roll=6 * math.sin(2 * math.pi * f / 7) * (1 - t), pitch=-4 * (1 - t))
    for leg, at in feet.items():
        put("IK.Root." + leg, Vector((at.x, at.y, foot_floor + rise / UNIT)), f)
    for side in ("L", "R"):
        tuck = to_mouth(side) + Vector((0, -0.1, -0.1))
        p = tuck.lerp(hand_rest[side], t)
        put("IK.Root.Hand." + side, Vector((p.x, p.y, p.z + rise / UNIT)), f)
for f, pitch in ((0, 8), (EMERGE, 0)):
    eye("L", f, 0, pitch)
    eye("R", f, 0, pitch)
smooth(emerge)

# ------------------------------------------------------------------ previews

if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.render.resolution_x, scene.render.resolution_y = 640, 420
    if hasattr(scene.render.image_settings, "media_type"):
        scene.render.image_settings.media_type = 'IMAGE'      # the source file renders a video
    scene.render.image_settings.file_format = 'PNG'
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    for act, frames in ((walk, (0, 6)), (idle, (0, 22, 34)), (threat, (0, 16)), (turn, (0, 4)),
                        (groom, (20, 52)), (wave, (22,)), (startle, (6,)), (dig, (25, 50)), (hidden, (20,)),
                        (emerge, (15,))):
        arm.animation_data.action = act
        for f in frames:
            scene.frame_set(f)
            for view, pos in (("front", (0.25, -0.7, 0.32)), ("side", (0.75, -0.1, 0.2))):
                cam.location = pos
                cam.rotation_euler = (Vector((0, 0, 0.06)) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
                scene.render.filepath = os.path.join(PREVIEW, f"{act.name}_{f:02d}_{view}.png")
                bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)

# ------------------------------------------------------------------ export

arm.animation_data.action = walk
scene.frame_start, scene.frame_end = 0, int(walk.frame_range[1])
for o in bpy.data.objects:
    o.select_set(o in (arm, body))
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, "Crab.fbx"), use_selection=True, object_types={'ARMATURE', 'MESH'},
    axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    bake_space_transform=False, add_leaf_bones=False, path_mode='STRIP',
    use_mesh_modifiers=True, mesh_smooth_type='FACE', use_armature_deform_only=True,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.5)
print(f"[crab] wrote Crab.fbx; stride speed {stride_speed:.3f} m/s")
