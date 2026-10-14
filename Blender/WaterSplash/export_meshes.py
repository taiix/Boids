"""
Export the baked splash as real 3D water: one mesh per simulated frame, only the water thrown up above the
surface around the impact, thinned to a game-sized triangle count, ready to play as a mesh flipbook.

    blender -b <splash_dive.blend> -P export_meshes.py -- <out.fbx>

  Entry_00..  the crown thrown up around a body diving in (simulated frames ENTRY)
  Leap_00..   the column of water rising and falling back, for a body leaping out (frames LEAP)

Each mesh: origin at the impact point on the still water (y up in Unity), cut just above the still
surface (the game sinks that edge just under its own waves).

Each frame's surface is a new shape, so to play smoothly between them every vertex also carries where it
gets to by the next frame, already in Unity's axes: x, y in the first UV channel, z in the second's x. The
game glides the vertices along it between frames. Where it gets to = carried along by the water's own
velocity (baked with speed vectors: make_splash.py ... mesh <fps>), then onto the next frame's surface, so
the hand-over to the next mesh lines up.
"""
import bpy, bmesh, os, sys
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.abspath(argv[0])
SPEED_SCALE = float(argv[1]) if len(argv) > 1 else 1.0    # if the baked speeds turn out to be in other units
RADIUS = 2.1                  # metres around the impact kept
TARGET_TRIS = 5500
SNAP = 0.08                   # how far from where the water carries it a vertex may snap onto the next surface

scene = bpy.context.scene
dom = bpy.data.objects["Domain"]
FPS = scene.render.fps


def frame(at30):
    """The simulated frame at the moment that was frame at30 of the original 30 fps simulation."""
    return round((at30 - 1) * FPS / 30) + 1


ENTRY = range(frame(3), frame(17) + 1)       # the crown
LEAP = range(frame(20), frame(56) + 1)       # the column: rising, tearing apart, falling back
MAX_MOVE = 9.0 / FPS                         # metres a vertex can travel in a frame (water flies at up to ~9 m/s)
print(f"[meshes] {FPS} fps: entry frames {ENTRY.start}-{ENTRY.stop - 1}, leap {LEAP.start}-{LEAP.stop - 1}")


def still_level():
    scene.frame_set(1)
    deps = bpy.context.evaluated_depsgraph_get()
    me = dom.evaluated_get(deps).to_mesh()
    far = sorted(v.co.z for v in me.vertices if abs(v.co.x) > 1.6)
    level = far[int(len(far) * 0.95)] if far else 0.0
    dom.evaluated_get(deps).to_mesh_clear()
    return level


REST = still_level()
CUT = REST + 0.03             # only water thrown up above the still surface (the game sinks this edge under its waves)
RIPPLE = REST + 0.12          # lower than this and lying flat = a ripple spreading over the surface, not a splash
print(f"[meshes] still water {REST:.3f}, keeping water above {CUT:.3f}")

out_col = bpy.data.collections.new("SplashMeshes")
scene.collection.children.link(out_col)
for o in list(scene.objects):
    o.select_set(False)


def grab(frame, name):
    scene.frame_set(frame)
    deps = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(dom.evaluated_get(deps))
    mw = dom.matrix_world
    # The water's speed at each point of the surface (m/s), looked up below by position (thinning the mesh
    # moves and merges its vertices).
    speeds = None
    vel = me.attributes.get("velocity")
    if vel is not None and vel.domain == 'POINT':
        speeds = KDTree(len(me.vertices))
        for v in me.vertices:
            speeds.insert(mw @ v.co - Vector((0.0, 0.0, REST)), v.index)
        speeds.balance()
        speeds = (speeds, [mw.to_3x3() @ Vector(d.vector) for d in vel.data])
        if not any(d.length_squared > 1e-12 for d in speeds[1]):
            speeds = None   # the attribute is there but empty (Mantaflow didn't write the speeds for this bake)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.transform(mw)                                   # world space: the still surface is at REST
    bm.normal_update()
    doomed = []
    for f in bm.faces:
        c = f.calc_center_median()
        if all(v.co.z < CUT for v in f.verts) or (c.x * c.x + c.y * c.y) > RADIUS * RADIUS:
            doomed.append(f)
        # Ripples: low, nearly flat water. In game they'd be pale plates lying on (and poking out of) the waves.
        elif all(v.co.z < RIPPLE for v in f.verts) and abs(f.normal.z) > 0.6:
            doomed.append(f)
    bmesh.ops.delete(bm, geom=doomed, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    for v in bm.verts:
        v.co.z -= REST                                 # origin on the still surface
    bm.to_mesh(me)
    bm.free()
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    obj = bpy.data.objects.new(name, me)
    SPEEDS[name] = speeds
    out_col.objects.link(obj)
    if tris > TARGET_TRIS:
        dec = obj.modifiers.new("Thin", 'DECIMATE')
        dec.ratio = TARGET_TRIS / tris
        deps = bpy.context.evaluated_depsgraph_get()
        thin = bpy.data.meshes.new_from_object(obj.evaluated_get(deps))
        obj.modifiers.remove(dec)
        old = obj.data
        obj.data = thin
        bpy.data.meshes.remove(old)
    for p in obj.data.polygons:
        p.use_smooth = True
    final = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    print(f"[meshes] {name}: frame {frame}, {tris} -> {final} triangles")
    obj.select_set(True)
    return obj


SPEEDS = {}


def link(frames):
    """Store in each frame where its vertices get to by the next frame (the last keeps flowing)."""
    dt = SPEED_SCALE / FPS
    carried = snapped = 0
    flows, nearest = [], []
    for a, b in zip(frames, frames[1:] + [None]):
        me = a.data
        speeds = SPEEDS.get(a.name)
        bvh = None
        if b is not None and len(b.data.polygons) > 0:
            nxt = b.data
            bvh = BVHTree.FromPolygons([v.co.copy() for v in nxt.vertices], [tuple(p.vertices) for p in nxt.polygons])
        moves = []
        for v in me.vertices:
            flow = Vector((0.0, 0.0, 0.0))
            if speeds is not None:
                _, i, _ = speeds[0].find(v.co)
                flow = speeds[1][i] * dt
                if bvh is not None and v.index % 7 == 0:
                    near = bvh.find_nearest(v.co, MAX_MOVE)
                    if near[0] is not None:
                        flows.append(flow.length)
                        nearest.append((near[0] - v.co).length)
                if flow.length > MAX_MOVE:
                    flow *= MAX_MOVE / flow.length
                carried += 1
            move = flow
            if bvh is not None:
                # Onto the next frame's surface near where the water carries it (or, without speeds, the
                # nearest point of it), so the hand-over to the next mesh lines up.
                hit = bvh.find_nearest(v.co + flow, SNAP if speeds is not None else MAX_MOVE)
                if hit[0] is not None:
                    move = hit[0] - v.co
                    snapped += 1
            moves.append(move)
        xy = me.uv_layers.new(name="MoveXY")
        z = me.uv_layers.new(name="MoveZ")
        for loop in me.loops:
            d = moves[loop.vertex_index]
            # Blender (x, y, z) arrives in Unity as (x, z, y) (checked against the imported bounds).
            xy.data[loop.index].uv = (d.x, d.z)
            z.data[loop.index].uv = (d.y, 0.0)
        far = max((d.length for d in moves), default=0.0)
        mn = [min(v.co[k] for v in me.vertices) for k in range(3)] if me.vertices else [0, 0, 0]
        mx = [max(v.co[k] for v in me.vertices) for k in range(3)] if me.vertices else [0, 0, 0]
        centre = [round((mn[k] + mx[k]) / 2, 2) for k in range(3)]
        print(f"[meshes] {a.name}: furthest move {far:.3f} m, box centre {centre}")
    print(f"[meshes] {carried} vertex moves from the water's speed, {snapped} snapped onto the next surface")
    if flows:
        flows.sort()
        nearest.sort()
        # Carried by the speed vs straight to the nearest point of the next surface (which only sees motion
        # across the surface, so it should be the smaller): way off = the speeds are in other units.
        print(f"[meshes] median move by speed {flows[len(flows) // 2]:.4f} m, to the next surface {nearest[len(nearest) // 2]:.4f} m")


entry = [grab(f, f"Entry_{i:02d}") for i, f in enumerate(ENTRY)]
leap = [grab(f, f"Leap_{i:02d}") for i, f in enumerate(LEAP)]
link(entry)
link(leap)

bpy.context.view_layer.objects.active = out_col.objects[0]
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=True, object_types={'MESH'},
    axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    bake_space_transform=True, mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False)
print("[meshes] wrote", OUT)
