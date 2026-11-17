"""The seagull's key poses (rest frame: faces -Y, up +Z, left wing +X). Waterline / flight height: z = 0 is where
the bird's origin sits - on the water when floating; in flight the body rides at the same height."""
import math
from mathutils import Matrix, Vector
from rig import q, X, Y, Z, blend, blend_some

# The trunk's middle, for turning the whole bird.
PIVOT = Vector((0, 0.12, 0.36))
WINGS = ["Shoulder_L", "Elbow_L", "Hand_L", "Finger1_L", "Finge2_L", "Finger1_L_end", "Finge2_L_end",
         "Shoulder_R", "Elbow_R", "Hand_R", "Finger1_R", "Finge2_R", "Finger1_R_end", "Finge2_R_end"]
LEGS = ["Thigh_L", "Knee_L", "Foot_L", "Toe_L", "Toe_L_end", "Thigh_R", "Knee_R", "Foot_R", "Toe_R", "Toe_R_end"]

# Floating: how far the bird sinks from standing (rest) and its tail-up tilt.
FLOAT_SINK = 0.31
FLOAT_PITCH = 10.0
# Flying: the body level (tail up from standing) at the same height as when floating.
FLY_PITCH = 20.0
FLY_SINK = 0.29

def float_pose(rig, fold, pitch=FLOAT_PITCH, sink=FLOAT_SINK):
    p = rig.body(fold, q(X, pitch), Vector((0, 0, -sink)), PIVOT)
    # Tail cocked up off the water, head held upright again.
    p = rig.turn(p, "Tail1", q(X, 14))
    p = rig.turn(p, "Chest", q(X, -pitch * 0.6))
    p = rig.turn(p, "Head", q(X, -pitch * 0.4))
    # Legs bent under the belly (in the water).
    p = rig.turn_lr(p, "Thigh_L", q(X, 35))
    p = rig.turn_lr(p, "Knee_L", q(X, -45))
    p = rig.turn_lr(p, "Foot_L", q(X, 25))
    return p

def glide_wings(rig, p, arm_down=8.0, elbow_down=10.0, hand_up=30.0, hand_back=0.0, twist=0.0):
    p = rig.turn_lr(p, "Shoulder_L", q(Y, arm_down))
    p = rig.turn_lr(p, "Elbow_L", q(Y, elbow_down))
    p = rig.turn_lr(p, "Hand_L", q(Y, -hand_up) @ q(Z, hand_back))
    if twist:
        p = rig.turn_lr(p, "Shoulder_L", q(X, twist))
    return p

def tuck_legs(rig, p, thigh=75.0, knee=-30.0, foot=70.0):
    p = rig.turn_lr(p, "Thigh_L", q(X, thigh))
    p = rig.turn_lr(p, "Knee_L", q(X, knee))
    p = rig.turn_lr(p, "Foot_L", q(X, foot))
    return p

def fly_body(rig, base, pitch=FLY_PITCH, sink=FLY_SINK):
    p = rig.body(base, q(X, pitch), Vector((0, 0, -sink)), PIVOT)
    p = rig.turn(p, "Head", q(X, -pitch * 0.5))
    p = rig.turn(p, "Tail1", q(X, -4))
    return p
