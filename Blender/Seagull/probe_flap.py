# The original takes' wing motion relative to the chest: finger tip height/sweep per frame (to find their flap cycle).
import bpy, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import load, use_action
argv = sys.argv[sys.argv.index("--") + 1:]
arm, body = load(argv[0])
scene = bpy.context.scene
for name in argv[1:]:
    act = next(a for a in bpy.data.actions if a.name.endswith(name))
    use_action(arm, act)
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    print("==", name)
    ch_rest = arm.data.bones["Chest"].matrix_local
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        ch = arm.pose.bones["Chest"].matrix
        # into the chest's rest-aligned frame: rest axes (bird facing -Y in rest)
        to_ch = ch_rest @ ch.inverted()
        tipL = to_ch @ arm.pose.bones["Finger1_L"].tail
        handL = to_ch @ arm.pose.bones["Hand_L"].head
        tipR = to_ch @ arm.pose.bones["Finger1_R"].tail
        print(f"  f{f:3d} tipL=({tipL.x:+.2f},{tipL.y:+.2f},{tipL.z:+.2f}) handL=({handL.x:+.2f},{handL.y:+.2f},{handL.z:+.2f}) tipR=({tipR.x:+.2f},{tipR.y:+.2f},{tipR.z:+.2f})")
