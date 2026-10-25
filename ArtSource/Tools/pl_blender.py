"""Perfect Loot art-test modeling helpers for Blender (5.x).

Usage inside Blender:
    import sys; sys.path.append(r"<repo>/ArtSource/Tools")
    import importlib, pl_blender; importlib.reload(pl_blender)
    from pl_blender import *
"""
import bpy, math, random, os
from mathutils import Vector, Euler, noise

MATS = {}  # name -> (srgb hex, emission hex or "")


def hexc(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


def lin(c):
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)


def mat(name, color, emit=None, strength=3.0):
    """Material registered for export (base color in sRGB hex, optional emission hex)."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    c = hexc(color)
    m.diffuse_color = (*c, 1)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    if b:
        b.inputs["Base Color"].default_value = (*lin(c), 1)
        if emit:
            b.inputs["Emission Color"].default_value = (*lin(hexc(emit)), 1)
            b.inputs["Emission Strength"].default_value = strength
        else:
            b.inputs["Emission Strength"].default_value = 0
    MATS[name] = (color, emit or "")
    return m


def activate(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o


def prim(kind, loc=(0, 0, 0), scale=(1, 1, 1), rot=(0, 0, 0), m=None, bevel=0.0, seg=8, sub=1):
    loc = Vector(loc)
    if kind == 'cube': bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    elif kind == 'sphere': bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=max(4, seg // 2), radius=0.5, location=loc)
    elif kind == 'ico': bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=sub, radius=0.5, location=loc)
    elif kind == 'cyl': bpy.ops.mesh.primitive_cylinder_add(vertices=seg, radius=0.5, depth=1, location=loc)
    elif kind == 'cone': bpy.ops.mesh.primitive_cone_add(vertices=seg, radius1=0.5, radius2=0, depth=1, location=loc)
    elif kind == 'frustum': bpy.ops.mesh.primitive_cone_add(vertices=seg, radius1=0.5, radius2=0.3, depth=1, location=loc)
    elif kind == 'torus': bpy.ops.mesh.primitive_torus_add(major_segments=max(seg, 12), minor_segments=4, major_radius=0.5, minor_radius=0.06, location=loc)
    o = bpy.context.active_object
    o.scale = scale
    o.rotation_euler = Euler([math.radians(a) for a in rot])
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if bevel > 0:
        b = o.modifiers.new("b", "BEVEL"); b.width = bevel; b.segments = 1; b.limit_method = 'ANGLE'
        bpy.ops.object.modifier_apply(modifier="b")
    if m: o.data.materials.append(m)
    return o


def skin(points, radii, m, edges=None, subd=1, name="skin"):
    if edges is None: edges = [(i, i + 1) for i in range(len(points) - 1)]
    me = bpy.data.meshes.new(name); me.from_pydata([Vector(p) for p in points], edges, []); me.update()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    sk = o.modifiers.new("skin", "SKIN"); sk.use_smooth_shade = True; sk.branch_smoothing = 0.5
    for i, r in enumerate(radii):
        me.skin_vertices[0].data[i].radius = r if isinstance(r, (tuple, list)) else (r, r)
    me.skin_vertices[0].data[0].use_root = True
    activate(o); bpy.ops.object.modifier_apply(modifier="skin")
    if subd:
        s = o.modifiers.new("sub", "SUBSURF"); s.levels = subd; s.render_levels = subd
        bpy.ops.object.modifier_apply(modifier="sub")
    if m: o.data.materials.append(m)
    return o


def strand(pts, r0, r1, m, subd=1):
    n = len(pts)
    return skin(pts, [r0 + (r1 - r0) * i / (n - 1) for i in range(n)], m, subd=subd)


def jag(o, amount=0.08, seed=0, zbias=0.0):
    R = random.Random(seed)
    for v in o.data.vertices:
        v.co += Vector((R.uniform(-1, 1), R.uniform(-1, 1), R.uniform(-1, 1) + zbias)) * amount
    return o


def lumpy(o, amount=0.06, scale=1.5, seed=0):
    off = Vector((seed * 3.1, seed * 1.7, seed * 2.3))
    for v in o.data.vertices:
        v.co += v.normal * noise.noise(v.co * scale + off) * amount
    return o


def jag_hem(o, zmax, amount=0.15, seed=0):
    R = random.Random(seed)
    for v in o.data.vertices:
        if v.co.z < zmax: v.co.z += R.uniform(-amount, amount * 0.3)
    return o


def crack(pts, m, w=0.07, d=0.06):
    out = []
    for a, b in zip(pts[:-1], pts[1:]):
        a = Vector(a); b = Vector(b); c = (a + b) / 2; L = (b - a).length
        bpy.ops.mesh.primitive_cube_add(size=1, location=c)
        o = bpy.context.active_object; o.scale = (w, d, L + w * 0.6)
        o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference((b - a).normalized())
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        o.data.materials.append(m); out.append(o)
    return out


def plane(w, h, loc, rot, m, xs=8, ys=6, thick=0.04, hem=0.0, seed=0):
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=xs, y_subdivisions=ys, size=1, location=loc)
    o = bpy.context.active_object; o.scale = (w, h, 1); o.rotation_euler = [math.radians(a) for a in rot]
    R = random.Random(seed)
    for v in o.data.vertices:
        if v.co.y < -0.35 and hem: v.co.y += R.uniform(-hem, hem * 0.3) / h
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    s = o.modifiers.new("so", "SOLIDIFY"); s.thickness = thick; s.offset = 0
    bpy.ops.object.modifier_apply(modifier="so")
    o.data.materials.append(m); return o


def coll(name):
    c = bpy.data.collections.get(name)
    if not c:
        c = bpy.data.collections.new(name); bpy.context.scene.collection.children.link(c)
    return c


def clear_coll(name):
    c = bpy.data.collections.get(name)
    if c:
        for o in list(c.objects): bpy.data.objects.remove(o, do_unlink=True)


def finalize(name, parts, c, place=(0, 0, 0), smooth_angle=20):
    """Join parts, origin at bottom center, UVs, smooth by angle, faces sorted by material."""
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts: p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    o = bpy.context.active_object; o.name = name; o.data.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    xs = [v.co.x for v in o.data.vertices]; ys = [v.co.y for v in o.data.vertices]; zs = [v.co.z for v in o.data.vertices]
    off = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)))
    for v in o.data.vertices: v.co -= off
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.mesh.sort_elements(type='MATERIAL', elements={'FACE'})
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth_angle))
    for md in list(o.modifiers):
        try: bpy.ops.object.modifier_apply(modifier=md.name)
        except Exception: pass
    for cc in o.users_collection: cc.objects.unlink(o)
    c.objects.link(o)
    o.location = place
    return o


def decimate(o, target_tris):
    tris = sum(len(f.vertices) - 2 for f in o.data.polygons)
    if tris > target_tris:
        activate(o); d = o.modifiers.new("dec", "DECIMATE"); d.ratio = target_tris / tris
        bpy.ops.object.modifier_apply(modifier="dec")
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.sort_elements(type='MATERIAL', elements={'FACE'}); bpy.ops.object.mode_set(mode='OBJECT')


def export_collection(coll_name, outdir):
    """FBX per object + slots.txt (material order) + materials.txt (sRGB base|emission)."""
    os.makedirs(outdir, exist_ok=True)
    slots = ["# object|material slots in submesh order"]; used = set(); out = []
    for o in bpy.data.collections[coll_name].objects:
        activate(o); loc = o.location.copy(); o.location = (0, 0, 0)
        bpy.ops.export_scene.fbx(filepath=os.path.join(outdir, o.name + ".fbx"), use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                                 axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True, use_mesh_modifiers=True,
                                 mesh_smooth_type='OFF', add_leaf_bones=False, bake_anim=False)
        o.location = loc
        slots.append(o.name + "|" + ",".join(m.name for m in o.data.materials))
        used.update(m.name for m in o.data.materials)
        out.append((o.name, sum(len(f.vertices) - 2 for f in o.data.polygons)))
    open(os.path.join(outdir, "slots.txt"), "w").write("\n".join(slots))
    lines = ["# name|base color (sRGB hex)|emission hex"]
    for n in sorted(used):
        col, em = MATS.get(n, ("#808080", ""))
        lines.append(f"{n}|{col}|{em}")
    open(os.path.join(outdir, "materials.txt"), "w").write("\n".join(lines))
    return out


def preview(objs, path, yaw=-15, pitch=70, res=(1600, 900), lens=50, pad=1.1):
    """Workbench render of the given objects (for quick visual checks)."""
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_cavity = True; sc.display.shading.show_object_outline = True
    sc.view_settings.view_transform = 'Standard'
    sc.render.resolution_x, sc.render.resolution_y = res; sc.render.film_transparent = False
    cam = bpy.data.objects.get("PreviewCam")
    if not cam:
        cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam")); sc.collection.objects.link(cam)
    cam.data.lens = lens
    hidden = {}
    for o in sc.objects:
        if o.type == 'MESH': hidden[o.name] = o.hide_render; o.hide_render = o not in objs
    mn = Vector((1e9,) * 3); mx = Vector((-1e9,) * 3)
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c); mn = Vector(map(min, mn, w)); mx = Vector(map(max, mx, w))
    ctr = (mn + mx) / 2; rad = (mx - mn).length / 2 * pad
    rot = Euler((math.radians(pitch), 0, math.radians(yaw)))
    cam.rotation_euler = rot
    cam.location = ctr + rot.to_quaternion() @ Vector((0, 0, rad / math.tan(cam.data.angle / 2)))
    sc.camera = cam; sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    for n, h in hidden.items(): bpy.data.objects[n].hide_render = h
