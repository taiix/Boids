"""
Pose maths for the seagull rig, in the rest pose's frame: the bird faces -Y, up +Z, its left wing toward +X.

A pose is {bone: (rotation quaternion, location)} - the bones' own (basis) values, what gets keyed. Turning a bone
"by Q" applies Q about the bone's head in its parent's frame as the parent sits now, with axes that line up
with the armature's at rest - so turns read the same on every bone (X = side to side, Y = back to front,
Z = up) and chain like a puppet's joints.
"""
import math
from mathutils import Matrix, Quaternion, Vector

X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))

def q(axis, deg):
    return Quaternion(axis, math.radians(deg))

def mirror(rot):
    """A turn of a left-side bone, as the right side's mirror of it (across the YZ plane)."""
    return Quaternion((rot.w, rot.x, -rot.y, -rot.z))

class Rig:
    def __init__(self, arm):
        self.arm = arm
        self.bones = arm.data.bones
        self.rest = {b.name: b.matrix_local.copy() for b in self.bones}
        self.rq = {n: m.to_quaternion() for n, m in self.rest.items()}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in self.bones}
        order, seen = [], set()
        def visit(b):
            if b.name in seen: return
            if b.parent: visit(b.parent)
            seen.add(b.name); order.append(b.name)
        for b in self.bones: visit(b)
        self.order = order

    # ---- poses
    def identity(self):
        return {n: (Quaternion(), Vector()) for n in self.order}

    def current(self):
        return {pb.name: (pb.rotation_quaternion.copy(), pb.location.copy()) for pb in self.arm.pose.bones}

    def from_armature_space(self, mats):
        """Bases from armature-space pose matrices."""
        pose = {}
        for n in self.order:
            p = self.parent[n]
            if p is None:
                local = self.rest[n].inverted() @ mats[n]
            else:
                local = (mats[p] @ self.rest[p].inverted() @ self.rest[n]).inverted() @ mats[n]
            pose[n] = (local.to_quaternion().normalized(), local.to_translation())
        return pose

    def fk(self, pose):
        """Armature-space matrices of a pose."""
        mats = {}
        for n in self.order:
            rot, loc = pose[n]
            basis = Matrix.Translation(loc) @ rot.to_matrix().to_4x4()
            p = self.parent[n]
            mats[n] = (self.rest[n] if p is None else mats[p] @ self.rest[p].inverted() @ self.rest[n]) @ basis
        return mats

    def apply(self, pose):
        for pb in self.arm.pose.bones:
            rot, loc = pose[pb.name]
            pb.rotation_quaternion = rot
            pb.location = loc

    def key(self, frame):
        for pb in self.arm.pose.bones:
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            pb.keyframe_insert("location", frame=frame)

    # ---- edits (each returns a new pose)
    def turn(self, pose, bone, rot):
        pose = dict(pose)
        r, l = pose[bone]
        rq = self.rq[bone]
        k = rq.inverted() @ rot @ rq
        pose[bone] = ((k @ r).normalized(), k @ l)
        return pose

    def turn_lr(self, pose, side_bone, rot):
        """A turn of the left bone (name with _L) and its mirror on the right."""
        pose = self.turn(pose, side_bone, rot)
        return self.turn(pose, side_bone.replace("_L", "_R"), mirror(rot))

    def body(self, pose, rot=Quaternion(), move=Vector(), pivot=Vector((0, 0.1, 0.42))):
        """The whole bird (its Root) turned about a point and moved, in armature space."""
        mats = self.fk(pose)
        m = Matrix.Translation(move + pivot) @ rot.to_matrix().to_4x4() @ Matrix.Translation(-pivot) @ mats["Root"]
        local = self.rest["Root"].inverted() @ m
        pose = dict(pose)
        pose["Root"] = (local.to_quaternion().normalized(), local.to_translation())
        return pose

def blend(a, b, t):
    out = {}
    for n in a:
        ra, la = a[n]; rb, lb = b[n]
        if ra.dot(rb) < 0: rb = -rb
        out[n] = (ra.slerp(rb, t).normalized(), la.lerp(lb, t))
    return out

def blend_some(a, b, t, names):
    out = dict(a)
    for n in names:
        ra, la = a[n]; rb, lb = b[n]
        if ra.dot(rb) < 0: rb = -rb
        out[n] = (ra.slerp(rb, t).normalized(), la.lerp(lb, t))
    return out

def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)

def ease(t0, t1, t):
    return smooth((t - t0) / (t1 - t0)) if t1 > t0 else (1.0 if t >= t1 else 0.0)

def _shift(self, pose, bone, offset):
    """Move a bone's head by offset (metres, in its parent's rest-aligned frame) - for the head on its neck."""
    pose = dict(pose)
    r, l = pose[bone]
    pose[bone] = (r, l + self.rq[bone].inverted() @ offset)
    return pose
Rig.shift = _shift
