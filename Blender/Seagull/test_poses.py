import bpy, sys, os, math, importlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Matrix, Vector
from common import load, use_action
from rig import Rig, q, X, Y, Z, blend, blend_some
import shots, poses as P

argv = sys.argv[sys.argv.index("--") + 1:]
src, tex, out = argv[0], argv[1], argv[2]
os.makedirs(out, exist_ok=True)
arm, body = load(src)
scene = bpy.context.scene
rig = Rig(arm)

def sample(name, frame, flip=True):
    act = next(a for a in bpy.data.actions if a.name.endswith(name))
    use_action(arm, act); scene.frame_set(frame)
    C = Matrix.Rotation(math.pi, 4, 'Z') if flip else Matrix()
    mats = {pb.name: C @ pb.matrix for pb in arm.pose.bones}
    mats["Root"] = rig.rest["Root"].copy()
    return rig.from_armature_space(mats)

FOLD = sample("ANIM_Seagull_Idle", 1)
use_action(arm, None)
REST = rig.identity()
FLOAT = P.float_pose(rig, FOLD)
G = blend_some(FOLD, REST, 1.0, P.WINGS)
GLIDE = P.fly_body(rig, P.tuck_legs(rig, P.glide_wings(rig, G)))

cam = shots.setup(body, tex)
shots.water_plane(0.0, 2.2)
files = []
for name, pose, views in (("FLOAT", FLOAT, ["side", "front", "low", "under"]), ("GLIDE", GLIDE, ["side", "front", "top", "three"])):
    rig.apply(pose); bpy.context.view_layer.update()
    files += shots.shoot(cam, os.path.join(out, name), views, centre=Vector((0, 0.1, 0.05)), dist=2.2)
shots.sheet(os.path.join(out, "..", "sheet_poses.png"), 4, files)
