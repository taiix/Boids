"""
The seagull for Unity: the user's model (Assets/Coral Reef Decor Pack/Seagull/source/Seagull.fbx - only read) with
new takes made for a gull out at sea. Its own takes are for a gull on land (standing, walking, eating), so they're
left out - though the new ones borrow from them: the folded wings from its Idle, the wings opening from IdleToFly,
and the hard lifting wingbeat (its FlyLifting) for taking off, bathing and braking to land.

    blender -b --factory-startup -P export_seagull.py -- <Seagull.fbx> <out_dir> [<preview_dir> <diffuse.png>]

Writes <out_dir>/Seagull.fbx: the bird (facing -Y here; +Z in Unity) with these takes, at 24 fps:

  Float       8 s loop  afloat: breathing, quick bird-like looks about (head snaps round and holds), neck stretched
              up when alert and sunk into its shoulders when it settles, a look up with one eye, a peek at the water,
              tail flicks, feet paddling under the water
  FloatPreen  4 s       preens: bill into its shoulder, then its breast, nibbling; a shake of the head after
  FloatShake  40 frames ruffles: a shiver through the body, head and tail, wings lifted and settled
  FloatFlap   64 frames rears up out of the water, a few wingbeats, drops back with a bob (gulls bathing)
  TakeOff     40 frames from floating: wings open, hard wingbeats while it runs on the water, legs tuck, climbing
  Fly         48 frames loop  four cruising wingbeats: the body heaves with each, the head held still in the air
              (as birds do), the tail and wingtips following through; a look aside
  Glide       6 s loop  wings out, riding the wind: gusts lift a wing, the tail twists to steer, the head scans the
              water below, tilting to look down with one eye
  Land        56 frames from a glide: flares nose-up looking down at the water, feet forward, braking wingbeats,
              splashes down (frame TOUCHDOWN) - the head carried forward - sinks and bobs up, folds its wings,
              settles its feathers - ending as Float begins

Where the bird sits: its origin is the waterline. Floating, the belly is a few cm under it; flying, the body rides
at that same height above the origin, so the game only moves the object (no root motion). Landing, the feet reach
the origin at TOUCHDOWN (the body is held up on its legs until then).

With a preview dir, contact sheets of every take are rendered there too.
"""
import bpy, os, sys, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Matrix, Vector, Quaternion
from common import load, use_action
from rig import Rig, q, X, Y, Z, mirror, blend, blend_some, smooth, ease
import poses as P

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], os.path.abspath(argv[1])
PREVIEW = os.path.abspath(argv[2]) if len(argv) > 2 else None
TEX = argv[3] if len(argv) > 3 else None
os.makedirs(OUT, exist_ok=True)

FPS = 24
TOUCHDOWN = 30   # Land: the frame its feet meet the water

arm, body = load(SRC)
scene = bpy.context.scene
scene.render.fps = FPS
rig = Rig(arm)
TAU = 2 * math.pi

# ------------------------------------------------------------------ poses borrowed from the model's own takes

def sample(take, frame):
    """A frame of one of the model's takes as a pose here. Its takes face +Y (the Spine turned half round): turned
    back to face -Y, with the Root left at rest."""
    act = next(a for a in bpy.data.actions if a.name.endswith(take))
    use_action(arm, act)
    scene.frame_set(frame)
    C = Matrix.Rotation(math.pi, 4, 'Z')
    mats = {pb.name: C @ pb.matrix for pb in arm.pose.bones}
    mats["Root"] = rig.rest["Root"].copy()
    return rig.from_armature_space(mats)

FOLD = sample("ANIM_Seagull_Idle", 1)                                    # standing, wings folded
UNFOLD = [sample("ANIM_Seagull_IdleToFly", f) for f in range(1, 11)]     # wings opening (0 folded .. 9 up high)
LIFT0 = 74                                                               # FlyLifting: wings at the top here...
LIFT = [sample("ANIM_Seagull_FlyLifting", f) for f in range(LIFT0, LIFT0 + 15)]   # ...one 14-frame cycle (2 beats)
use_action(arm, None)
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
REST = rig.identity()

def wings_of(src, base):
    return blend_some(base, src, 1.0, P.WINGS)

def lift_wings(base, c, depth=0.72):
    """The lifting wingbeat's wings at cycle position c (frames from its top; 14 per cycle). Its downstrokes reach
    below the feet (it hovers): depth < 1 eases them toward the glide wings, so they don't plunge into the sea."""
    c = c % 14.0
    i = int(c)
    w = blend_some(wings_of(LIFT[i], base), LIFT[i + 1], c - i, P.WINGS)
    return blend_some(glide_wings(base), w, depth, P.WINGS)

def unfold_wings(base, k):
    """The wings opening, k from 0 (folded) to 9 (up high)."""
    k = max(0.0, min(9.0, k))
    i = min(int(k), 8)
    return blend_some(wings_of(UNFOLD[i], base), UNFOLD[i + 1], k - i, P.WINGS)

def glide_wings(p, l_up=0.0, r_up=0.0, l_hand=0.0, r_hand=0.0, l_tip=0.0, r_tip=0.0):
    """Wings out as in a glide (from the rest pose's spread), with extra lift per side (degrees, + = up) for the
    whole wing, the hand and the wingtip feathers."""
    p = wings_of(REST, p)
    p = P.glide_wings(rig, p, arm_down=8.0, elbow_down=8.0, hand_up=38.0, hand_back=-4.0)
    p = rig.turn(p, "Shoulder_L", q(Y, -l_up)); p = rig.turn(p, "Shoulder_R", mirror(q(Y, -r_up)))
    p = rig.turn(p, "Hand_L", q(Y, -l_hand)); p = rig.turn(p, "Hand_R", mirror(q(Y, -r_hand)))
    for b, a in (("Finger1", l_tip), ("Finge2", l_tip * 0.6)):
        p = rig.turn(p, f"{b}_L", q(Y, -a))
    for b, a in (("Finger1", r_tip), ("Finge2", r_tip * 0.6)):
        p = rig.turn(p, f"{b}_R", mirror(q(Y, -a)))
    return p

def wing_beat(p, phase, amp=1.0):
    """The cruising wingbeat on top of glide wings: phase 0 = wings at the top, 0.5 at the bottom. The hand lags the
    arm and the wingtip feathers lag the hand, bending up under the air's load on the downstroke."""
    ph = TAU * phase
    up = 24 * math.cos(ph) + 6
    elbow = 10 * math.cos(ph - 0.6)
    hand = 16 * math.cos(ph - 1.1)
    tip = 12 * math.cos(ph - 1.9) + 4 * max(0.0, math.sin(ph))
    fold = max(0.0, -math.sin(ph))           # the upstroke: wing flexes, hand swept back
    p = rig.turn_lr(p, "Shoulder_L", q(Y, -up * amp) @ q(X, 5 * math.sin(ph) * amp))
    p = rig.turn_lr(p, "Elbow_L", q(Y, -elbow * amp) @ q(Z, -10 * fold * amp))
    p = rig.turn_lr(p, "Hand_L", q(Y, -hand * amp) @ q(Z, 30 * fold * amp) @ q(X, 9 * math.sin(ph) * amp))
    p = rig.turn_lr(p, "Finger1_L", q(Y, -tip * amp))
    p = rig.turn_lr(p, "Finge2_L", q(Y, -tip * 0.6 * amp))
    return p

# ------------------------------------------------------------------ motion helpers

def ease_back(t, s=1.3):
    """0 to 1 with a small overshoot at the end - a bird's head snapping to a new spot."""
    t = max(0.0, min(1.0, t))
    u = t - 1.0
    return 1 + (s + 1) * u ** 3 + s * u ** 2

class Snaps:
    """Values that jump from one held setting to the next: [(frame, (values...), frames to get there)]. Each move
    starts from wherever the last one had got to. snappy: a quick move that overshoots a touch (head movements);
    otherwise an even ease (posture)."""
    def __init__(self, keys, snappy=True):
        self.keys = keys
        self.e = ease_back if snappy else smooth
        self.starts = [keys[0][1]]
        for i in range(1, len(keys)):
            self.starts.append(self._chain(i - 1, keys[i][0]))

    def _chain(self, i, f):
        if i == 0:
            return self.keys[0][1]
        fk, vk, dk = self.keys[i]
        e = self.e((f - fk) / dk)
        return tuple(a + (b - a) * e for a, b in zip(self.starts[i], vk))

    def __call__(self, f):
        k = 0
        for i, key in enumerate(self.keys):
            if key[0] <= f:
                k = i
        return self._chain(k, f)

class Wobble:
    """Smooth wandering noise that repeats every n frames (a sum of whole-number harmonics, random phases)."""
    def __init__(self, n, seed, ks=(1, 2, 3, 5, 7)):
        rnd = random.Random(seed)
        self.n = n
        self.terms = [(k, rnd.uniform(0, TAU), 1.0 / k ** 0.7) for k in ks]
        self.norm = 1.0 / math.sqrt(sum(a * a for _, _, a in self.terms) / 2)

    def __call__(self, f, amp):
        return amp * self.norm * sum(a * math.sin(TAU * k * f / self.n + ph) for k, ph, a in self.terms)

def bump(f, start, length):
    """0 -> 1 -> 0 over [start, start + length]."""
    if f <= start or f >= start + length:
        return 0.0
    return math.sin(math.pi * (f - start) / length)

def wag(f, start, length, cycles):
    """A quick side-to-side, fading in and out over [start, start + length]."""
    if f <= start or f >= start + length:
        return 0.0
    u = (f - start) / length
    return math.sin(TAU * cycles * u) * math.sin(math.pi * u)

def track(points, f):
    """A value eased between (frame, value) points."""
    if f <= points[0][0]:
        return points[0][1]
    for (f0, v0), (f1, v1) in zip(points, points[1:]):
        if f <= f1:
            return v0 + (v1 - v0) * smooth((f - f0) / (f1 - f0))
    return points[-1][1]

# ------------------------------------------------------------------ the body

def side_turn(p, bone, side, rot):
    return rig.turn(p, f"{bone}_L", rot) if side == "L" else rig.turn(p, f"{bone}_R", mirror(rot))

def legs_float(p, phase=None, amount=1.0):
    """Legs bent under the belly; paddling (each foot pushes back, then comes forward folded) when phase is given."""
    for side, off in (("L", 0.0), ("R", 0.5)):
        th, kn, ft, toe = 48.0, -85.0, 45.0, 0.0
        if phase is not None:
            ps = TAU * (phase + off)
            th += 18 * math.sin(ps) * amount
            kn += -14 * (1 + math.cos(ps)) * 0.5 * amount
            ft += 22 * math.sin(ps + 0.9) * amount
            toe = -18 * max(0.0, math.cos(ps)) * amount        # webs fold on the way forward
        p = side_turn(p, "Thigh", side, q(X, th))
        p = side_turn(p, "Knee", side, q(X, kn))
        p = side_turn(p, "Foot", side, q(X, ft))
        p = side_turn(p, "Toe", side, q(X, toe))
    return p

def legs_tucked(p):
    return P.tuck_legs(rig, p, thigh=75.0, knee=-30.0, foot=70.0)

def legs_reach(p, reach=1.0):
    """Legs swung forward and down to meet the water, toes spread."""
    p = side_turn(p, "Thigh", "L", q(X, -20 * reach)); p = side_turn(p, "Thigh", "R", q(X, -20 * reach))
    p = rig.turn_lr(p, "Knee_L", q(X, 10 * reach))
    p = rig.turn_lr(p, "Foot_L", q(X, -25 * reach))
    p = rig.turn_lr(p, "Toe_L", q(X, -20 * reach))
    return p

def legs_run(p, phase):
    """Running on the water: legs reach down and kick back, one after the other."""
    for side, off in (("L", 0.0), ("R", 0.5)):
        ps = TAU * (phase + off)
        th = 20 + 38 * math.sin(ps)
        kn = -20 - 22 * max(0.0, math.cos(ps))
        ft = 10 + 30 * math.sin(ps + 0.7)
        p = side_turn(p, "Thigh", side, q(X, th))
        p = side_turn(p, "Knee", side, q(X, kn))
        p = side_turn(p, "Foot", side, q(X, ft))
    return p

def chest_turn(p, rot, keep_wings=0.85):
    """Turn the chest (the neck's base) - the wings, hung on it, mostly stay as they were."""
    p = rig.turn(p, "Chest", rot)
    k = Quaternion().slerp(rot.inverted(), keep_wings)
    p = rig.turn(p, "Shoulder_L", k)
    return rig.turn(p, "Shoulder_R", k)

def neck(p, yaw=0.0, pitch=0.0, tilt=0.0, stretch=0.0, roll=0.0, lean=0.0, bob=0.0):
    """The head and neck. yaw: + looks to its left; pitch: + beak down (from the body's line); tilt: + head leaning
    to its left (looking up/down with one eye); stretch: +1 neck up and out (alert), -1 head sunk into its shoulders
    (settled); roll: the neck's base rolled (a shiver); lean: the neck swung forward/down (+) keeping the head's angle;
    bob: the head moved up (+, metres) - to hold it still while the body heaves. The chest takes a share of a turn,
    the head the rest; the head keeps the angle asked whatever the neck does."""
    cy = 0.3 * yaw
    cp = 0.35 * pitch + lean
    sp = -9.0 * stretch if stretch >= 0 else 7.0 * -stretch
    shift = (Vector((0, -0.012, 0.05)) * stretch if stretch >= 0 else Vector((0, 0.03, -0.05)) * -stretch)
    p = chest_turn(p, q(Z, cy) @ q(X, cp + sp) @ q(Y, -roll))
    p = rig.turn(p, "Head", q(Z, yaw - cy) @ q(X, pitch - cp - sp) @ q(Y, -tilt + roll))
    return rig.shift(p, "Head", shift + Vector((0, 0, bob)))

def tail(p, pitch=0.0, yaw=0.0, twist=0.0):
    """+pitch tail up, +yaw tail toward its left, +twist its left edge down (steering)."""
    p = rig.turn(p, "Tail1", q(X, pitch * 0.6) @ q(Z, -yaw * 0.6) @ q(Y, twist * 0.6))
    return rig.turn(p, "Tail2", q(X, pitch * 0.4) @ q(Z, -yaw * 0.4) @ q(Y, twist * 0.4))

def place(p, pitch, sink, roll=0.0, yaw=0.0, lift=0.0):
    """The whole bird: pitch (+ tail up / nose down, from standing), roll (+ = its left wing down), yaw, and how
    far it sits down from standing (sink) less any lift."""
    rot = q(Z, yaw) @ q(Y, -roll) @ q(X, pitch)
    return rig.body(p, rot, Vector((0, 0, -sink + lift)), P.PIVOT)

def afloat(f, yaw=0.0, pitch=0.0, tilt=0.0, stretch=0.0, neck_roll=0.0, lean=0.0, roll=0.0, body_yaw=0.0,
           pitch_extra=0.0, lift=0.0, tail_pitch=0.0, tail_yaw=0.0, paddle=1.0, wings=None, breath=1.0, stroke=32.0):
    """Sitting on the water. The head's pitch here is in the world (0 = held as when standing). stroke: frames per
    paddle stroke (a take ends on a whole stroke, so it meets Float's start)."""
    p = wings if wings is not None else wings_of(FOLD, FOLD)
    p = legs_float(p, phase=f / stroke, amount=paddle)
    body_pitch = P.FLOAT_PITCH + pitch_extra
    p = rig.turn(p, "Tail1", q(X, 14))                    # tail cocked up off the water
    p = tail(p, tail_pitch, tail_yaw)
    p = chest_turn(p, q(X, 1.2 * breath * math.sin(TAU * f / 48)))   # breathing (a breath every 2 s)
    p = neck(p, yaw, pitch - body_pitch, tilt, stretch, neck_roll, lean)
    return place(p, body_pitch, P.FLOAT_SINK, roll, body_yaw, lift)

# ------------------------------------------------------------------ afloat

FLOAT_N = 192
_float_look = Snaps([                     # (yaw, pitch, tilt): looking about, a quick move and a hold
    (0, (0, 0, 0), 1),
    (14, (28, -4, 8), 4),
    (30, (34, 3, 16), 3),
    (44, (-4, 6, -4), 5),
    (56, (-38, -2, -8), 4),
    (66, (-30, -9, -18), 3),
    (80, (12, -16, 30), 5),               # up at the sky, with one eye
    (98, (0, 24, 0), 5),                  # down at the water ahead
    (112, (16, 30, 8), 3),
    (124, (2, 6, 0), 6),
    (150, (-24, 2, -10), 5),
    (166, (-10, -2, -4), 4),
    (180, (0, 0, 0), 5),
])
_float_posture = Snaps([(0, (0.0,), 1), (50, (0.6,), 10), (72, (0.9,), 8), (96, (0.2,), 10), (122, (-0.6,), 14),
                        (170, (-0.2,), 12), (182, (0.0,), 8)], snappy=False)
_w = {k: Wobble(FLOAT_N, s) for k, s in (("yaw", 1), ("pitch", 2), ("tilt", 3), ("roll", 4), ("byaw", 5), ("tail", 6))}

def float_pose(f):
    t = f / FLOAT_N
    yaw, pitch, tilt = _float_look(f)
    yaw += _w["yaw"](f, 1.2); pitch += _w["pitch"](f, 1.0); tilt += _w["tilt"](f, 1.5)
    flick = 12 * bump(f, 40, 7) + 8 * bump(f, 158, 6)
    return afloat(f, yaw, pitch, tilt, _float_posture(f)[0],
                  roll=_w["roll"](f, 1.6), body_yaw=_w["byaw"](f, 2.5),
                  pitch_extra=1.2 * math.sin(TAU * 2 * t) + 0.7 * math.sin(TAU * 6 * t + 0.5),   # each stroke surges it
                  lift=0.008 * math.sin(TAU * 3 * t),
                  tail_pitch=2 * math.sin(TAU * 2 * t) + flick, tail_yaw=8 * wag(f, 138, 14, 3) + _w["tail"](f, 2),
                  paddle=0.75 + 0.25 * math.sin(TAU * 2 * t + 1.0))

_preen_look = Snaps([(0, (0, 0, 0), 1), (4, (-40, 20, -10), 8), (12, (-100, 48, -25), 8), (42, (-30, 55, 0), 8),
                     (52, (0, 68, 0), 6), (74, (10, 6, 8), 6), (88, (0, 0, 0), 8)])
_preen_posture = Snaps([(0, (0.0,), 1), (4, (-0.4,), 8), (42, (-0.5,), 8), (74, (0.3,), 6), (88, (0.0,), 8)], snappy=False)

def preen_pose(f):
    """Preening: bill into the right shoulder's feathers (that wing lifted out a little), then the breast, nibbling;
    head up and a quick shake."""
    yaw, pitch, tilt = _preen_look(f)
    nib = ease(14, 18, f) * (1 - ease(38, 42, f)) + ease(54, 58, f) * (1 - ease(70, 74, f))
    pitch += 7 * math.sin(TAU * f / 5.0) * nib
    yaw += 5 * math.sin(TAU * f / 7.0) * nib
    yaw += 24 * wag(f, 78, 10, 3)
    wing = ease(8, 16, f) * (1 - ease(36, 46, f))
    p = wings_of(FOLD, FOLD)
    p = rig.turn(p, "Shoulder_R", mirror(q(Y, -9 * wing) @ q(Z, 6 * wing)))
    return afloat(f, yaw, pitch, tilt, _preen_posture(f)[0], wings=p,
                  roll=-3 * wing + 2 * wag(f, 78, 10, 3), pitch_extra=2 * bump(f, 44, 30),
                  tail_yaw=10 * wag(f, 84, 12, 2.5), paddle=0.5)

def shake_pose(f):
    """Ruffling its feathers: hunches, a shiver runs through the body, head and tail, wings lifted and settled."""
    env = ease(4, 8, f) * (1 - ease(20, 26, f))
    shiver = lambda lag: math.sin(TAU * (f - 5 - lag) / 4.5) * env
    lift_w = 7 * ease(0, 5, f) * (1 - ease(22, 27, f)) + 14 * bump(f, 24, 8)
    p = wings_of(FOLD, FOLD)
    p = rig.turn_lr(p, "Shoulder_L", q(Y, -lift_w) @ q(Z, -0.4 * lift_w))
    return afloat(f, yaw=34 * shiver(0.8), pitch=8 * bump(f, 0, 10) + 5 * shiver(1.5), stretch=-0.4 * bump(f, 0, 30),
                  neck_roll=-9 * shiver(0.4), roll=13 * shiver(0), pitch_extra=3 * bump(f, 0, 10),
                  lift=-0.02 * bump(f, 0, 12), tail_yaw=18 * shiver(1.6), tail_pitch=6 * bump(f, 22, 10),
                  wings=p, paddle=0.6, stroke=40.0)

def bathe_pose(f):
    """Rears up out of the water and beats its wings a few times, then drops back with a bob (bathing)."""
    rear = ease(0, 8, f) * (1 - ease(40, 50, f))
    beats = ease(8, 11, f) * (1 - ease(28, 34, f))
    if f <= 8:
        p = unfold_wings(FOLD, 9 * smooth(f / 8))
    elif f <= 30:
        p = lift_wings(FOLD, f - 8)
    elif f <= 34:
        p = blend_some(lift_wings(FOLD, f - 8), unfold_wings(FOLD, 9), smooth((f - 30) / 4), P.WINGS)
    else:
        p = unfold_wings(FOLD, 9 * (1 - ease(34, 46, f)))
    p = chest_turn(p, q(X, 4 * math.sin(TAU * (f - 8) / 7.0 - 1.2) * beats))
    return afloat(f, yaw=18 * wag(f, 54, 8, 2), pitch=-4 * rear, stretch=0.6 * rear,
                  pitch_extra=-40 * rear + 3 * math.sin(TAU * (f - 8) / 7.0) * beats,
                  lift=0.12 * rear + 0.025 * math.sin(TAU * (f - 8) / 7.0) * beats - 0.04 * bump(f, 44, 14),
                  tail_pitch=-10 * rear, tail_yaw=10 * wag(f, 50, 10, 2.5), paddle=1 + 0.8 * rear, wings=p)

# ------------------------------------------------------------------ in the air

def fly_body(p, pitch_world, pitch_extra=0.0, roll=0.0, heave=0.0, yaw=0.0, tilt=0.0, stretch=0.0, lean=0.0):
    """In the air: the body level (FLY_PITCH from standing) plus pitch_extra; the head held at pitch_world (+ beak
    down, from standing), and held still against the body's heave."""
    body_pitch = P.FLY_PITCH + pitch_extra
    p = neck(p, yaw, pitch_world - body_pitch, tilt, stretch, lean=lean, bob=-0.7 * heave)
    return place(p, body_pitch, P.FLY_SINK, roll, 0.0, heave)

FLY_N = 48
_fly_look = Snaps([(0, (0, 0, 0), 1), (12, (24, 8, 10), 4), (26, (18, 14, 8), 3), (38, (0, 0, 0), 5)])
_fw = {k: Wobble(FLY_N, s) for k, s in (("roll", 11), ("tail", 12))}

def fly_pose(f):
    ph = f / 12.0                                       # four beats
    amp = 1 + 0.07 * math.sin(TAU * f / FLY_N + 0.7)    # no two quite the same
    p = glide_wings(FOLD)
    p = wing_beat(p, ph, amp)
    p = legs_tucked(p)
    p = tail(p, -4 + 4 * math.sin(TAU * ph - 1.8), _fw["tail"](f, 1.5))
    p = chest_turn(p, q(X, 2.5 * math.sin(TAU * ph - 1.6)))
    yaw, pitch, tilt = _fly_look(f)
    return fly_body(p, 12 + pitch, pitch_extra=3 * math.sin(TAU * ph - 0.5), roll=_fw["roll"](f, 1.0),
                    heave=-0.03 * math.cos(TAU * ph), yaw=yaw, tilt=tilt)

GLIDE_N = 144
_glide_look = Snaps([(0, (0, 0, 0), 1), (16, (-30, 8, -12), 5), (40, (-36, 14, -15), 3), (56, (4, -2, 2), 5),
                     (72, (32, 10, 12), 5), (92, (26, 18, 10), 3), (108, (8, 2, 3), 5), (124, (0, 0, 0), 6)])

def glide_pose(f):
    t = TAU * f / GLIDE_N
    gust = bump(f, 60, 14)                              # a gust lifts both wings, the body rises a touch
    p = glide_wings(FOLD,
                    l_up=2 + 3.5 * math.sin(t) + 1.5 * math.sin(3 * t + 1.0) + math.sin(5 * t + 2) + 6 * gust,
                    r_up=2 + 3.5 * math.sin(t + 2.1) + 1.5 * math.sin(3 * t + 0.3) + math.sin(5 * t + 4) + 5 * gust,
                    l_hand=3 * math.sin(2 * t + 0.4) - 4 * gust, r_hand=3 * math.sin(2 * t + 2.0) - 4 * gust,
                    l_tip=4 * math.sin(4 * t + 1) + 6 * gust, r_tip=4 * math.sin(4 * t + 2.5) + 6 * gust)
    p = legs_tucked(p)
    roll = 3 * math.sin(t + 1.0) + math.sin(3 * t)
    p = tail(p, -4 + 2 * math.sin(2 * t) + 5 * bump(f, 64, 10), 2 * math.sin(t + 0.2), 6 * math.sin(t + 0.4))
    yaw, pitch, tilt = _glide_look(f)
    return fly_body(p, 10 + pitch, pitch_extra=1.2 * math.sin(2 * t) - 3 * gust, roll=roll,
                    heave=0.006 * math.sin(2 * t) + 0.02 * gust, yaw=yaw, tilt=tilt)

# ------------------------------------------------------------------ off the water and back

def takeoff_pose(f):
    # Wings: open (0-6), into the lifting beat (6-10), two lifting cycles (10-38), into the cruising beat's top.
    if f <= 6:
        p = unfold_wings(FOLD, 9 * smooth(f / 6))
    elif f <= 10:
        p = blend_some(unfold_wings(FOLD, 9), lift_wings(FOLD, 0), smooth((f - 6) / 4), P.WINGS)
    elif f <= 38:
        p = lift_wings(FOLD, f - 10)
    else:
        p = blend_some(lift_wings(FOLD, 28), wing_beat(glide_wings(FOLD), 0), smooth((f - 38) / 2), P.WINGS)
    # Legs: paddling -> running on the water with each beat -> tucked.
    run = ease(2, 8, f) * (1 - ease(24, 32, f))
    tuck = ease(26, 36, f)
    legs = blend(legs_float(p, phase=f / 32.0), legs_run(p, (f - 8) / 14.0), run)
    legs = blend(legs, legs_tucked(p), tuck)
    p = blend_some(p, legs, 1.0, P.LEGS)
    # Body: from sitting on the water, nose up as it climbs, then nearly level; heaving with each beat.
    pitch = track([(0, P.FLOAT_PITCH), (5, P.FLOAT_PITCH + 4), (12, -2), (30, 2), (40, P.FLY_PITCH - 8)], f)
    sink = track([(0, P.FLOAT_SINK), (40, P.FLY_SINK)], f)
    beats = ease(10, 14, f) * (1 - ease(32, 40, f))
    bob = 0.03 * math.sin(TAU * (f - 10) / 7.0) * beats
    p = rig.turn(p, "Tail1", q(X, 14 * (1 - ease(4, 14, f))))
    p = tail(p, -14 * ease(4, 10, f) * (1 - ease(30, 40, f)) - 4 * ease(30, 40, f) + 5 * math.sin(TAU * (f - 11) / 7.0) * beats)
    p = chest_turn(p, q(X, 4 * math.sin(TAU * (f - 10) / 7.0 - 1.2) * beats))
    # The neck stretched out ahead, the head held steady while the body tips and heaves under it.
    head_world = track([(0, 0), (6, 16), (14, 12), (40, 14)], f)
    stretch = track([(0, 0), (4, 0.7), (30, 0.6), (40, 0.1)], f)
    p = neck(p, 0, head_world - pitch, 0, stretch, bob=-0.7 * bob)
    return place(p, pitch, sink, lift=bob)

def land_pose(f):
    TD = TOUCHDOWN
    # Wings: glide -> cupped forward for the flare -> braking (lifting beat) -> held up -> folding -> folded.
    cup = ease(0, 12, f)
    glide = glide_wings(FOLD)
    cupped = rig.turn_lr(glide, "Shoulder_L", q(Y, -22) @ q(Z, -18) @ q(X, -14))
    cupped = rig.turn_lr(cupped, "Hand_L", q(Y, -10) @ q(X, -12))
    braking = lift_wings(FOLD, f - 12)
    if f <= 12:
        p = blend_some(glide, cupped, cup, P.WINGS)
    elif f <= 16:
        p = blend_some(cupped, braking, smooth((f - 12) / 4), P.WINGS)
    elif f <= TD + 2:
        p = braking
    elif f <= TD + 6:
        p = blend_some(braking, unfold_wings(FOLD, 8), smooth((f - TD - 2) / 4), P.WINGS)
    else:
        p = unfold_wings(FOLD, 8 * (1 - ease(TD + 6, TD + 18, f)))
    # Settling its feathers once the wings are folded: a little lift and shuffle.
    shuffle = 6 * bump(f, TD + 17, 7)
    p = rig.turn_lr(p, "Shoulder_L", q(Y, -shuffle))
    # Legs: tucked -> reaching forward for the water -> folding under the body afloat (paddling as Float begins).
    reach = ease(4, 16, f)
    legs = blend(legs_tucked(p), legs_reach(p), reach)
    legs = blend(legs, legs_float(p, phase=(f - 56) / 32.0), ease(TD + 2, TD + 12, f))
    p = blend_some(p, legs, 1.0, P.LEGS)
    # Body: level -> flared nose-up -> drops onto the water at TOUCHDOWN, sinks a little deep, bobs up to afloat.
    pitch = track([(0, P.FLY_PITCH), (14, -10), (TD - 2, -8), (TD + 4, P.FLOAT_PITCH + 6), (TD + 12, P.FLOAT_PITCH - 2),
                   (TD + 20, P.FLOAT_PITCH + 1), (56, P.FLOAT_PITCH)], f)
    tailp = track([(0, -4), (12, -22), (TD, -18), (TD + 6, 6), (TD + 14, 0)], f)
    braking_beats = ease(14, 18, f) * (1 - ease(TD - 2, TD + 4, f))
    # How the bird is held up off the water before it lands: its lowest toe at the origin at TOUCHDOWN.
    lift = track([(0, 0), (16, touch_lift), (TD, touch_lift), (TD + 5, -0.05), (TD + 12, 0.015), (TD + 20, -0.006), (56, 0)], f)
    lift += 0.025 * math.sin(TAU * (f - 12) / 7.0) * braking_beats
    float_mix = ease(TD, TD + 10, f)
    p = rig.turn(p, "Tail1", q(X, 14 * float_mix))
    p = tail(p, tailp * (1 - float_mix), 10 * wag(f, TD + 18, 10, 2.5))
    p = chest_turn(p, q(X, 4 * math.sin(TAU * (f - 12) / 7.0 - 1.2) * braking_beats))
    # The head: looking down at the water through the flare (+ = beak down, in the world), carried forward by its
    # own weight as the body stops on the water, then up and settled - with a little shake once the wings are in.
    head_world = track([(0, 14), (12, 24), (TD, 18), (TD + 8, 0)], f)
    stretch = track([(0, 0.0), (10, 0.5), (TD, 0.4), (TD + 8, -0.3), (TD + 18, -0.4), (56, 0.0)], f)
    lean = 16 * bump(f, TD - 1, 10)
    p = neck(p, 16 * wag(f, TD + 14, 8, 2), head_world - pitch, 0, stretch, lean=lean,
             bob=-0.7 * 0.025 * math.sin(TAU * (f - 12) / 7.0) * braking_beats)
    sink = P.FLY_SINK + (P.FLOAT_SINK - P.FLY_SINK) * float_mix
    return place(p, pitch, sink, lift=lift)

def lowest_toe(pose):
    m = rig.fk(pose)
    return min((m[b].translation.z for b in ("Toe_L", "Toe_R", "Toe_L_end", "Toe_R_end", "Foot_L", "Foot_R")))

# How high the landing bird must be held so its feet just meet the water at TOUCHDOWN (measured with no lift).
touch_lift = 0.0
touch_lift = -lowest_toe(land_pose(TOUCHDOWN)) + 0.03
print(f"[gull] landing: held up {touch_lift:.3f} m until touchdown")

TAKES = [("Float", FLOAT_N, float_pose), ("FloatPreen", 96, preen_pose), ("FloatShake", 40, shake_pose),
         ("FloatFlap", 64, bathe_pose), ("TakeOff", 40, takeoff_pose), ("Fly", FLY_N, fly_pose),
         ("Glide", GLIDE_N, glide_pose), ("Land", 56, land_pose)]

made = {}
for name, n, fn in TAKES:
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    use_action(arm, act)
    for f in range(0, n + 1):
        rig.apply(fn(f))
        rig.key(f)
    if hasattr(act, "slots") and len(act.slots) and hasattr(arm.animation_data, "action_slot"):
        arm.animation_data.action_slot = act.slots[0]
    made[name] = act
    print(f"[gull] {name}: {n} frames ({n / FPS:.2f} s)")

# Seams: the loops' last frames are their first; the floating extras and Land end as Float begins.
def max_diff(a, b):
    return max(min(d, 2 * math.pi - d) for d in (abs(a[n][0].rotation_difference(b[n][0]).angle) for n in a)) * 57.3
f0 = float_pose(0)
print(f"[gull] seams (deg): Float {max_diff(f0, float_pose(FLOAT_N)):.2f}  Fly {max_diff(fly_pose(0), fly_pose(FLY_N)):.2f}"
      f"  Glide {max_diff(glide_pose(0), glide_pose(GLIDE_N)):.2f}  Land->Float {max_diff(land_pose(56), f0):.2f}"
      f"  Preen->Float {max_diff(preen_pose(96), f0):.2f}  Shake->Float {max_diff(shake_pose(40), f0):.2f}"
      f"  Flap->Float {max_diff(bathe_pose(64), f0):.2f}  TakeOff->Fly {max_diff(takeoff_pose(40), fly_pose(0)):.2f}")
m = rig.fk(f0)
print(f"[gull] afloat: toe z {lowest_toe(f0):.3f}, tail tip z {m['Tail2_end'].translation.z:.3f}, beak z {m['Beak_end'].translation.z:.3f}")

# ------------------------------------------------------------------ previews

if PREVIEW:
    import shots
    os.makedirs(PREVIEW, exist_ok=True)
    cam = shots.setup(body, TEX, res=(220, 190))
    shots.water_plane(0.0, 2.6)
    only = os.environ.get("GULL_PREVIEW", "")
    plan = {"Float": (list(range(0, 192, 16)), ["side", "three"]),
            "FloatPreen": ([0, 8, 16, 24, 36, 48, 56, 64, 76, 82, 90, 96], ["side", "three"]),
            "FloatShake": ([0, 4, 6, 8, 10, 12, 16, 20, 24, 28, 34, 40], ["front", "three"]),
            "FloatFlap": ([0, 4, 8, 12, 16, 20, 26, 32, 38, 44, 52, 60], ["side", "front"]),
            "TakeOff": ([0, 4, 8, 12, 16, 20, 26, 32, 38], ["side", "front"]),
            "Fly": ([0, 3, 6, 9, 12, 15, 18, 24, 30, 36, 42], ["side", "front"]),
            "Glide": (list(range(0, 144, 12)), ["side", "front"]),
            "Land": ([0, 8, 14, 20, 26, 30, 32, 34, 38, 42, 48, 56], ["side", "three"])}
    for name, (frames, views) in plan.items():
        if only and name not in only.split(","):
            continue
        use_action(arm, made[name])
        files = []
        for view in views:
            for f in frames:
                scene.frame_set(f)
                files += shots.shoot(cam, os.path.join(PREVIEW, f"{name}_{f:03d}"), [view],
                                     centre=Vector((0, 0.1, 0.12)), dist=2.4, ortho_scale=1.8)
        shots.sheet(os.path.join(PREVIEW, f"sheet_{name}.png"), len(frames), files)
        print(f"[gull] preview {name}")

# ------------------------------------------------------------------ export

use_action(arm, made["Float"])
scene.frame_start, scene.frame_end = 0, FLOAT_N
for o in list(bpy.data.objects):
    if o not in (arm, body):
        bpy.data.objects.remove(o, do_unlink=True)
for o in bpy.data.objects:
    o.select_set(True)
bpy.context.view_layer.objects.active = arm
body.data.materials[0].name = "Seagull"
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, "Seagull.fbx"), use_selection=True, object_types={'ARMATURE', 'MESH'},
    axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    bake_space_transform=False, add_leaf_bones=False, path_mode='STRIP',
    use_mesh_modifiers=True, mesh_smooth_type='FACE', use_armature_deform_only=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0)
print(f"[gull] wrote {os.path.join(OUT, 'Seagull.fbx')}")
