import bpy, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import load, use_action
argv = sys.argv[sys.argv.index("--") + 1:]
arm, body = load(argv[0])
scene = bpy.context.scene
for name, frames in (("FlyLifting", range(74, 89, 2)), ("IdleToFly", range(1, 27, 3))):
    act = next(a for a in bpy.data.actions if a.name.endswith(name))
    use_action(arm, act)
    for f in frames:
        scene.frame_set(f)
        s = []
        for b in ("Chest", "Head", "Tail1", "Thigh_L", "Knee_L", "Foot_L"):
            e = arm.pose.bones[b].matrix_basis.to_euler()
            s.append(f"{b}=({e.x*57.3:.0f},{e.y*57.3:.0f},{e.z*57.3:.0f})")
        print(f"[{name} f{f}] " + " ".join(s))
