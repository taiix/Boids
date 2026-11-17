# For each take: frames, and per sampled frame the Root's world position/rotation and where the beak, wing tips, feet are.
import bpy, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import load, use_action, mesh_box
argv = sys.argv[sys.argv.index("--") + 1:]
arm, body = load(argv[0])
scene = bpy.context.scene
def wp(b): return tuple(round(v, 2) for v in arm.matrix_world @ arm.pose.bones[b].head)
for act in bpy.data.actions:
    use_action(arm, act)
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    print(f"== {act.name.split('|')[-1]} {f0}-{f1}")
    step = max(1, (f1 - f0) // 8)
    for f in range(f0, f1 + 1, step):
        scene.frame_set(f)
        r = arm.pose.bones["Root"]
        e = (arm.matrix_world @ r.matrix).to_euler()
        print(f"  f{f} root={wp('Root')} rotdeg={tuple(round(v*57.3) for v in e)} beak={wp('Beak_end')} handL={wp('Hand_L')} fingL={wp('Finger1_L')} handR={wp('Hand_R')} footL={wp('Foot_L')} tail={wp('Tail2')}")
