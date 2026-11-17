import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Matrix, Vector
from common import load, use_action
from rig import Rig, q, X, Y, Z, mirror, blend_some
import shots, poses as P
argv = sys.argv[sys.argv.index("--") + 1:]
src, tex, out = argv[0], argv[1], argv[2]
os.makedirs(out, exist_ok=True)
arm, body = load(src); scene = bpy.context.scene; rig = Rig(arm)
def sample(name, frame):
    act = next(a for a in bpy.data.actions if a.name.endswith(name)); use_action(arm, act); scene.frame_set(frame)
    C = Matrix.Rotation(math.pi, 4, 'Z'); mats = {pb.name: C @ pb.matrix for pb in arm.pose.bones}
    mats["Root"] = rig.rest["Root"].copy(); return rig.from_armature_space(mats)
FOLD = sample("ANIM_Seagull_Idle", 1); use_action(arm, None)
def chest(p, rot, keep_wings=1.0):
    p = rig.turn(p, "Chest", rot)
    inv = rot.inverted()
    from mathutils import Quaternion
    k = Quaternion().slerp(inv, keep_wings)
    p = rig.turn(p, "Shoulder_L", k); p = rig.turn(p, "Shoulder_R", k)
    return p
tests = {
  "base": FOLD,
  "chest_dn20": chest(FOLD, q(X, 20)),
  "chest_up15": chest(FOLD, q(X, -15)),
  "chest_yaw30": chest(FOLD, q(Z, 30)),
  "head_up": rig.shift(chest(FOLD, q(X, -8)), "Head", Vector((0, -0.01, 0.05))),
  "head_sunk": rig.shift(chest(FOLD, q(X, 6)), "Head", Vector((0, 0.03, -0.05))),
  "preen": rig.turn(chest(FOLD, q(Z, 35) @ q(X, 25)), "Head", q(Z, 95) @ q(X, 45)),
  "tilt": rig.turn(FOLD, "Head", q(Y, -30) @ q(Z, 15)),
}
cam = shots.setup(body, tex, res=(240, 220))
files = []
for view in ("side", "front", "three"):
    for name, p in tests.items():
        rig.apply(p); bpy.context.view_layer.update()
        files += shots.shoot(cam, os.path.join(out, f"{name}"), [view], centre=Vector((0, 0.05, 0.45)), dist=1.6, ortho_scale=1.0)
shots.sheet(os.path.join(out, "..", "sheet_neck.png"), len(tests), files)
