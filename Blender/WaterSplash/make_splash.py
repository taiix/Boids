"""
Water splash flipbooks and droplet sprites for Reef Run, made with Blender's Mantaflow liquid simulation.

Run headless (it bakes a fluid sim, which takes minutes):
    blender -b --factory-startup -P make_splash.py -- <mode> <out_dir> [resolution] [style] [fps]

  mode  dive     - a body plunging into still water: the crown and jet thrown up around the hole
        leap     - a body bursting up out of the water: the sheet and column dragged up with it
        droplets - 16 water drops for the spray, no simulation
  out_dir  where the frames, the atlas PNG and the .blend go
  style    clear / lens (how the flipbook renders the water), or mesh - only bake and save the .blend, for
           export_meshes.py (the game plays the simulated water itself as 3D meshes)
  fps      simulated frames a second (30); more frames = smoother 3D splashes

Each flipbook is the splash ABOVE the waterline only (everything under the rest level is held out), side
on through an orthographic camera (4 m wide x 2 m tall), the waterline along the bottom edge of every frame,
32 frames (every other simulated frame: 2.1 s) packed 4 across x 8 down (frame 0 top-left) - what the Unity
side (WaterFx.Splash) plays on an upright billboard.
"""
import bpy, math, os, sys
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
MODE = argv[0] if argv else "dive"
OUT = os.path.abspath(argv[1] if len(argv) > 1 else os.path.join(os.path.dirname(__file__), MODE))
RES = int(argv[2]) if len(argv) > 2 else 128
# clear: water renders as see-through (only its reflections and glints are kept) - lens: opaque drops that
# bend the sky through them.
STYLE = argv[3] if len(argv) > 3 else "clear"
os.makedirs(OUT, exist_ok=True)

FRAME_W, FRAME_H = 512, 256          # one flipbook frame: 4 m wide x 2 m tall
VIEW_W = 4.0
COLS, ROWS = 4, 8
FPS = int(argv[4]) if len(argv) > 4 else 30
STEP = 2                             # render every other simulated frame: the whole splash, rise and fall
SIM_END = round((4 + COLS * ROWS * STEP + 2) * FPS / 30)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS


# ------------------------------------------------------------------ look

def water_material(hold_out_below=None):
    """Water as it looks flying through the air: perfectly clear, bending the sky behind it, a hard glint from
    the sun. With hold_out_below, everything under that height renders as nothing (only the splash above
    the water)."""
    m = bpy.data.materials.new("Water")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = (0.92, 0.98, 1.0, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.0
    bsdf.inputs["IOR"].default_value = 1.33
    bsdf.inputs["Transmission Weight"].default_value = 1.0
    shader = bsdf.outputs["BSDF"]
    if hold_out_below is not None:
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        sep = nt.nodes.new("ShaderNodeSeparateXYZ")
        cut = nt.nodes.new("ShaderNodeMapRange")
        cut.inputs["From Min"].default_value = hold_out_below
        cut.inputs["From Max"].default_value = hold_out_below + 0.05
        hold = nt.nodes.new("ShaderNodeHoldout")
        mix = nt.nodes.new("ShaderNodeMixShader")
        nt.links.new(geo.outputs["Position"], sep.inputs["Vector"])
        nt.links.new(sep.outputs["Z"], cut.inputs["Value"])
        nt.links.new(cut.outputs["Result"], mix.inputs["Fac"])
        nt.links.new(hold.outputs["Holdout"], mix.inputs[1])
        nt.links.new(shader, mix.inputs[2])
        shader = mix.outputs["Shader"]
    nt.links.new(shader, out.inputs["Surface"])
    return m


def lights_and_film(w, h):
    # The world the water reflects and bends: bright sky overhead, hazy horizon, deep sea below. The camera
    # never sees it (transparent film) - only the water does.
    world = bpy.data.worlds.new("Sky")
    world.use_nodes = True
    wt = world.node_tree
    bg = wt.nodes["Background"]
    coords = wt.nodes.new("ShaderNodeTexCoord")
    sep = wt.nodes.new("ShaderNodeSeparateXYZ")
    ramp = wt.nodes.new("ShaderNodeValToRGB")
    els = ramp.color_ramp.elements
    els[0].position, els[0].color = 0.0, (0.05, 0.16, 0.22, 1.0)    # straight down: the sea
    els[1].position, els[1].color = 1.0, (1.0, 1.0, 1.0, 1.0)       # straight up: bright sky
    mid = els.new(0.5)
    mid.color = (0.32, 0.45, 0.55, 1.0)                              # the horizon
    to01 = wt.nodes.new("ShaderNodeMapRange")
    to01.inputs["From Min"].default_value = -1.0
    to01.inputs["From Max"].default_value = 1.0
    wt.links.new(coords.outputs["Generated"], sep.inputs["Vector"])
    wt.links.new(sep.outputs["Z"], to01.inputs["Value"])
    wt.links.new(to01.outputs["Result"], ramp.inputs["Fac"])
    wt.links.new(ramp.outputs["Color"], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = 1.4
    scene.world = world

    sun_data = bpy.data.lights.new("Sun", 'SUN')
    sun_data.energy = 8.0
    sun_data.angle = math.radians(2.5)                                # a hard, small glint
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (math.radians(40), 0, math.radians(-30))
    scene.collection.objects.link(sun)

    # Cycles on the GPU: real refraction and caustics, and clear water can render as truly see-through.
    r = scene.render
    r.engine = 'CYCLES'
    r.resolution_x, r.resolution_y = w, h
    r.resolution_percentage = 100
    r.film_transparent = True
    r.image_settings.file_format = 'PNG'
    r.image_settings.color_mode = 'RGBA'
    cp = bpy.context.preferences.addons['cycles'].preferences
    for kind in ('OPTIX', 'CUDA'):
        try:
            cp.compute_device_type = kind
            cp.refresh_devices()
            if any(d.type == kind for d in cp.devices):
                break
        except TypeError:
            continue
    for d in cp.devices:
        d.use = d.type != 'CPU'
    cy = scene.cycles
    cy.device = 'GPU'
    cy.samples = 160
    cy.use_denoising = True
    cy.max_bounces = cy.transmission_bounces = cy.transparent_max_bounces = 16
    cy.glossy_bounces = 8
    cy.caustics_refractive = cy.caustics_reflective = True
    clear = STYLE == "clear"
    for holder in (cy, r):
        if hasattr(holder, "film_transparent_glass"):
            holder.film_transparent_glass = clear
        if hasattr(holder, "film_transparent_roughness"):
            holder.film_transparent_roughness = 0.1
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'


def ortho_camera(centre, width):
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = width
    cam = bpy.data.objects.new("Cam", cam_data)
    cam.location = (centre[0], -8.0, centre[2])
    cam.rotation_euler = (math.radians(90), 0, 0)   # looking along +Y, Z up
    scene.collection.objects.link(cam)
    scene.camera = cam
    return cam


# ------------------------------------------------------------------ atlas

def pack(frames, cols, rows, fw, fh, path):
    """frames: PNG paths, packed left-to-right, top-to-bottom (frame 0 top-left, as Unity's flipbooks)."""
    atlas = np.zeros((rows * fh, cols * fw, 4), dtype=np.float32)
    for i, f in enumerate(frames[:cols * rows]):
        img = bpy.data.images.load(f)
        px = np.empty(fw * fh * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        px = px.reshape(fh, fw, 4)                     # Blender rows run bottom-up
        c, r = i % cols, i // cols
        y0 = (rows - 1 - r) * fh                       # so row 0 lands at the top of the image
        atlas[y0:y0 + fh, c * fw:(c + 1) * fw] = px
        bpy.data.images.remove(img)
    out = bpy.data.images.new("Atlas", cols * fw, rows * fh, alpha=True)
    out.pixels.foreach_set(atlas.ravel())
    out.filepath_raw = path
    out.file_format = 'PNG'
    out.save()
    print(f"[splash] wrote {path}")


def keyframes(obj):
    """Every keyframe on the object, through Blender 5's layered actions (or the old fcurves list)."""
    act = obj.animation_data.action if obj.animation_data else None
    if act is None:
        return []
    curves = []
    if getattr(act, "layers", None):
        for layer in act.layers:
            for strip in layer.strips:
                for bag in getattr(strip, "channelbags", []):
                    curves.extend(bag.fcurves)
    elif hasattr(act, "fcurves"):
        curves = list(act.fcurves)
    return [kp for fc in curves for kp in fc.keyframe_points]


# ------------------------------------------------------------------ the splash simulations

def splash(mode):
    # A 4.6 x 4.6 m tank (the crown never reaches its walls), 1 m deep, 1.8 m of air above: rest level z = 0.
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0.4))
    dom = bpy.context.object
    dom.name = "Domain"
    dom.scale = (4.6, 4.6, 2.8)
    bpy.ops.object.transform_apply(scale=True)
    md = dom.modifiers.new("Fluid", 'FLUID')
    md.fluid_type = 'DOMAIN'
    ds = md.domain_settings
    ds.domain_type = 'LIQUID'
    ds.resolution_max = RES
    ds.use_mesh = True
    ds.cache_type = 'ALL'
    ds.cache_directory = os.path.join(OUT, "cache")
    ds.cache_frame_start = 1
    ds.cache_frame_end = SIM_END
    ds.mesh_scale = 2 if RES >= 96 else 1           # smoother, finer surface
    ds.use_collision_border_top = False
    ds.use_adaptive_timesteps = True
    ds.timesteps_min = max(1, round(4 * 30 / FPS))  # a fast body moves several cells in a 30th of a second
    ds.timesteps_max = max(3, round(12 * 30 / FPS))
    ds.use_speed_vectors = True                     # how fast each point of the surface moves (for the 3D meshes)
    ds.cfl_condition = 2.0

    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, -0.5))
    pool = bpy.context.object
    pool.name = "Pool"
    pool.scale = (4.56, 4.56, 1.0)
    bpy.ops.object.transform_apply(scale=True)
    mp = pool.modifiers.new("Fluid", 'FLUID')
    mp.fluid_type = 'FLOW'
    mp.flow_settings.flow_type = 'LIQUID'
    mp.flow_settings.flow_behavior = 'GEOMETRY'
    pool.hide_render = True

    # What hits the water. A solid body barely moves this solver's water, so the splash is driven with water:
    # dive - a fast slug of water slamming down into the pool, which throws up the crown and jet a plunging
    #        body does; leap - a short burst shooting up through the surface, a column that rises and falls.
    if mode == "dive":
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.26, location=(0, 0, 0.5))
        slug = bpy.context.object
        slug.name = "Slug"
        slug.scale = (1.0, 1.0, 1.5)
        bpy.ops.object.transform_apply(scale=True)
        mf = slug.modifiers.new("Fluid", 'FLUID')
        mf.fluid_type = 'FLOW'
        fs = mf.flow_settings
        fs.flow_type = 'LIQUID'
        fs.flow_behavior = 'GEOMETRY'
        fs.use_initial_velocity = True
        fs.velocity_coord = (0.0, 0.0, -8.0)
        slug.hide_render = True
    else:
        # A plug of the surface itself flung upward, the way a body bursting out drags the water up with it.
        bpy.ops.mesh.primitive_cylinder_add(radius=0.2, depth=0.45, location=(0, 0, -0.1))
        jet = bpy.context.object
        jet.name = "Jet"
        mf = jet.modifiers.new("Fluid", 'FLUID')
        mf.fluid_type = 'FLOW'
        fs = mf.flow_settings
        fs.flow_type = 'LIQUID'
        fs.flow_behavior = 'GEOMETRY'
        fs.use_initial_velocity = True
        fs.velocity_coord = (0.0, 0.0, 6.5)
        jet.hide_render = True

    print(f"[splash] baking {mode} at resolution {RES} ...")
    with bpy.context.temp_override(scene=scene, active_object=dom, object=dom, selected_objects=[dom], selected_editable_objects=[dom]):
        bpy.ops.fluid.bake_all()
    print("[splash] baked")

    # Where the still water's surface ends up (the liquid mesh sits a little above the pool's top), measured
    # away from the splash: only what rises above that is kept.
    scene.frame_set(1)
    deps = bpy.context.evaluated_depsgraph_get()
    mesh = dom.evaluated_get(deps).to_mesh()
    far = sorted(v.co.z for v in mesh.vertices if abs(v.co.x) > 1.6)
    rest = far[int(len(far) * 0.95)] if far else 0.0
    dom.evaluated_get(deps).to_mesh_clear()
    print(f"[splash] still water at z = {rest:.3f}")
    if STYLE == "mesh":
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, f"splash_{mode}.blend"))
        print("[splash] saved for export_meshes.py")
        return
    dom.data.materials.clear()
    dom.data.materials.append(water_material(hold_out_below=rest + 0.04))
    lights_and_film(FRAME_W, FRAME_H)
    ortho_camera((0, 0, VIEW_W / 4), VIEW_W)        # 4 m wide, z 0 .. 2

    first = 4 if mode == "dive" else 2              # from the moment it breaks the surface
    frames = []
    for i in range(COLS * ROWS):
        scene.frame_set(first + i * STEP)
        path = os.path.join(OUT, f"frame_{i:02d}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        frames.append(path)
    pack(frames, COLS, ROWS, FRAME_W, FRAME_H, os.path.join(OUT, f"Splash_{mode}.png"))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, f"splash_{mode}.blend"))


# ------------------------------------------------------------------ droplets

def droplets():
    """16 drops of water in a 4 x 4 sheet: round to teardrop, some wobbling as they fly."""
    import random
    random.seed(7)
    mat = water_material()
    cell = 0.5
    for i in range(16):
        c, r = i % 4, i // 4
        x = (c - 1.5) * cell
        z = (1.5 - r) * cell
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.12, segments=48, ring_count=24, location=(x, 0, z))
        d = bpy.context.object
        stretch = random.uniform(1.0, 1.9)
        d.scale = (random.uniform(0.85, 1.05), random.uniform(0.85, 1.05), stretch)
        bpy.ops.object.transform_apply(scale=True)
        # A tail trailing behind the drop, and a little wobble.
        tail = random.uniform(0.0, 0.55)
        wob = random.uniform(0.0, 0.06)
        phase = random.uniform(0, 6.28)
        for v in d.data.vertices:
            k = max(0.0, (v.co.z - z) / (0.12 * stretch))       # 0 at the middle, 1 at the top
            squeeze = 1.0 - tail * k * k
            ang = math.atan2(v.co.y, v.co.x - x)
            w = 1.0 + wob * math.sin(3 * ang + phase)
            v.co.x = x + (v.co.x - x) * squeeze * w
            v.co.y = v.co.y * squeeze * w
        bpy.ops.object.shade_smooth()
        d.data.materials.append(mat)
    lights_and_film(512, 512)
    ortho_camera((0, 0, 0), 4 * cell)
    path = os.path.join(OUT, "Droplets.png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "droplets.blend"))
    print(f"[splash] wrote {path}")


if MODE == "droplets":
    droplets()
else:
    splash(MODE)
