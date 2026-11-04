"""Perfect Loot player base body and equipment models for Blender (5.x).

The base body is rigid parts on the player rig (pl_rig): root, hips, legs, chest, head, arms, plus hand_L/hand_R sockets
that only carry weapons. Equipment pieces are modelled in body space around the base body at rest and exported one
object per socket, with the object's origin at that socket bone's head: in Unity each piece is parented to its bone at
the bone's origin and follows its animation (EquipmentVisualSystem).

Usage inside Blender:
    import pl_player_equipment as P
    arm, mesh = P.build_base(collection)        # base body, rig and clips
    P.export_base(arm, mesh, fbx_path)
    P.build_item("ShortSword", collection)     # one object per socket
    P.export_item("ShortSword", collection, fbx_path)
Character front is -Y (Unity +Z); left is +X.
"""
import bpy, bmesh, math, os
from mathutils import Vector, Matrix
import pl_blender as B
import pl_rig as R

BONES = {
    "root": ((0, 0, 0), None),
    "hips": ((0, 0, 0.88), "root"),
    "leg_L": ((0.13, 0, 0.86), "hips"),
    "leg_R": ((-0.13, 0, 0.86), "hips"),
    "chest": ((0, 0, 1.05), "hips"),
    "head": ((0, 0, 1.52), "chest"),
    "arm_L": ((0.36, 0, 1.42), "chest"),
    "arm_R": ((-0.36, 0, 1.42), "chest"),
    "hand_L": ((0.42, -0.12, 0.84), "arm_L"),
    "hand_R": ((-0.42, -0.12, 0.84), "arm_R"),
}
SOCKETS = [n for n in BONES if n != "root"]


def mats():
    return {
        "cloth": B.mat("PL_Player_Cloth", "#5A6E9C"),
        "leather": B.mat("PL_Player_Leather", "#6A4632"),
        "skin": B.mat("PL_Player_Skin", "#E0B496"),
        "hair": B.mat("PL_Player_Hair", "#2E2430"),
        "metal": B.mat("PL_Player_Metal", "#A8AEBA"),
        "cloak": B.mat("PL_Player_Cloak", "#7C2C44"),
        "wood": B.mat("CT_Wood", "#5C3E30"),
        "dark": B.mat("CT_DarkStone", "#3E3A4C"),
    }


def join(objs, name, coll):
    """Joins objects into one mesh (world space baked in, materials merged) without operators."""
    bm = bmesh.new()
    materials = []
    for o in objs:
        me = o.data.copy()
        me.transform(o.matrix_world)
        remap = []
        for m in me.materials:
            if m not in materials: materials.append(m)
            remap.append(materials.index(m))
        start = len(bm.faces)
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        for f in bm.faces[start:]:
            f.material_index = remap[f.material_index] if remap else 0
        bpy.data.meshes.remove(me)
        bpy.data.objects.remove(o, do_unlink=True)
    out = bpy.data.meshes.new(name)
    bm.to_mesh(out); bm.free()
    for m in materials: out.materials.append(m)
    for p in out.polygons: p.use_smooth = False
    o = bpy.data.objects.new(name, out); coll.objects.link(o)
    return o


def box(loc, size, m, bevel=0.02, rot=(0, 0, 0)):
    return B.prim('cube', loc, size, rot, m, bevel=bevel)


def taper(o, top_scale, axis=2):
    """Scales the top of a primitive (along axis) by top_scale, linear over its height (object space)."""
    zs = [v.co[axis] for v in o.data.vertices]
    lo, hi = min(zs), max(zs)
    c = sum((v.co for v in o.data.vertices), Vector()) / len(o.data.vertices)
    for v in o.data.vertices:
        t = (v.co[axis] - lo) / max(1e-6, hi - lo)
        s = 1 + (top_scale - 1) * t
        for k in range(3):
            if k != axis: v.co[k] = c[k] + (v.co[k] - c[k]) * s
    return o


# ---------- base body ----------

def build_base(coll, name="PL_Base"):
    """Chunky base body in plain clothes (tunic, trousers, boots), rigged with Idle, Move and Hit."""
    M = mats()
    parts = []
    for side in (1, -1):
        x = 0.13 * side
        parts.append(taper(box((x, 0, 0.55), (0.19, 0.2, 0.62), M["cloth"]), 0.8))           # trouser leg
        parts.append(box((x, -0.04, 0.09), (0.21, 0.32, 0.18), M["leather"], bevel=0.04))      # boot
        sleeve = taper(box((0.42 * side, -0.03, 1.2), (0.16, 0.17, 0.42), M["cloth"]), 1.15)
        parts.append(sleeve)
        parts.append(box((0.42 * side, -0.08, 0.96), (0.12, 0.13, 0.14), M["skin"]))         # forearm
        parts.append(box((0.42 * side, -0.12, 0.84), (0.17, 0.19, 0.17), M["skin"], bevel=0.04))  # big hand
    parts.append(box((0, 0, 0.9), (0.44, 0.27, 0.18), M["leather"]))                         # waist
    parts.append(taper(box((0, 0, 1.24), (0.5, 0.3, 0.4), M["cloth"]), 1.12))                # tunic
    parts.append(box((0, 0, 1.47), (0.6, 0.28, 0.1), M["cloth"]))                            # shoulders
    parts.append(B.prim('cyl', (0, 0, 1.53), (0.15, 0.15, 0.1), m=M["skin"], seg=8))          # neck
    head = B.prim('ico', (0, -0.02, 1.71), (0.38, 0.38, 0.4), m=M["skin"], sub=2)
    parts.append(head)
    hair = B.prim('ico', (0, 0.03, 1.78), (0.41, 0.41, 0.33), m=M["hair"], sub=2)
    for v in hair.data.vertices:                      # leave the face open (object space: the hair's middle is at 0)
        if v.co.y < -0.08 and v.co.z < 0.06: v.co.y += 0.12
    parts.append(hair)
    for side in (1, -1):
        parts.append(box((0.08 * side, -0.215, 1.72), (0.07, 0.04, 0.09), M["hair"], bevel=0))  # eyes
    src = join(parts, name + "_Src", coll)

    def classify(c, s, mats_):
        x, y, z = c
        if abs(x) > 0.3 and z > 0.75: return "arm_L" if x > 0 else "arm_R"
        if z > 1.5: return "head"
        if z > 0.98: return "chest"
        if abs(x) > 0.05 and z < 0.84: return "leg_L" if x > 0 else "leg_R"
        return "hips"

    arm, mesh, counts = R.build(src, name + "_Rig", coll, BONES, classify)
    bpy.data.objects.remove(src, do_unlink=True)
    mesh.name = mesh.data.name = name + "_Animated"
    clips(arm)
    return arm, mesh, counts


def clips(arm):
    R.clip(arm, "PL_Base_Idle", 48, {
        "hips": [(0, (0, 0, 0), (0, 0, 0)), (24, (0, 0, 0), (0, 0, -0.015)), (48, (0, 0, 0), (0, 0, 0))],
        "chest": [(0, (0, 0, 0)), (24, (3, 0, 0), (0, 0, 0.01)), (48, (0, 0, 0))],
        "head": [(0, (0, 0, 0)), (24, (-2, 3, 0)), (48, (0, 0, 0))],
        "arm_L": [(0, (0, 0, 0)), (24, (-3, 0, -3)), (48, (0, 0, 0))],
        "arm_R": [(0, (0, 0, 0)), (24, (-3, 0, 3)), (48, (0, 0, 0))],
    }, loop=True)
    R.clip(arm, "PL_Base_Move", 24, {
        "hips": [(0, (0, 4, 0), (0, 0, 0)), (6, (0, 0, 0), (0, 0, 0.05)), (12, (0, -4, 0), (0, 0, 0)), (18, (0, 0, 0), (0, 0, 0.05)), (24, (0, 4, 0), (0, 0, 0))],
        "chest": [(0, (9, -6, 0)), (6, (10, 0, 0)), (12, (9, 6, 0)), (18, (10, 0, 0)), (24, (9, -6, 0))],
        "head": [(0, (-6, 5, 0)), (12, (-6, -5, 0)), (24, (-6, 5, 0))],
        "leg_L": [(0, (30, 0, 0)), (6, (0, 0, 0)), (12, (-32, 0, 0)), (18, (0, 0, 0)), (24, (30, 0, 0))],
        "leg_R": [(0, (-32, 0, 0)), (6, (0, 0, 0)), (12, (30, 0, 0)), (18, (0, 0, 0)), (24, (-32, 0, 0))],
        "arm_L": [(0, (-24, 0, 0)), (12, (22, 0, 0)), (24, (-24, 0, 0))],
        "arm_R": [(0, (22, 0, 0)), (12, (-24, 0, 0)), (24, (22, 0, 0))],
    }, loop=True)
    R.clip(arm, "PL_Base_Hit", 12, {
        "chest": [(0, (0, 0, 0)), (3, (-14, 6, 0)), (12, (0, 0, 0))],
        "head": [(0, (0, 0, 0)), (3, (-10, -8, 0)), (12, (0, 0, 0))],
        "hips": [(0, (0, 0, 0), (0, 0, 0)), (3, (0, 0, 0), (0, 0.06, -0.03)), (12, (0, 0, 0), (0, 0, 0))],
        "arm_L": [(0, (0, 0, 0)), (3, (12, 0, -15)), (12, (0, 0, 0))],
        "arm_R": [(0, (0, 0, 0)), (3, (12, 0, 15)), (12, (0, 0, 0))],
    })


def export_base(arm, mesh, path):
    R.export(arm, mesh, path)


# ---------- equipment ----------

ITEMS = {}


def item(fn):
    ITEMS[fn.__name__] = fn
    return fn


def build_item(name, coll):
    """Runs the item's builder, which returns {socket: [parts in body space]}; one object per socket, origin at the bone."""
    for o in list(coll.objects):
        if o.name.startswith(name + "__"): bpy.data.objects.remove(o, do_unlink=True)
    pieces = ITEMS[name](mats())
    out = []
    for socket, parts in pieces.items():
        o = join(parts, f"{name}__{socket}", coll)
        head = Vector(BONES[socket][0])
        o.data.transform(Matrix.Translation(-head))
        o.location = head
        out.append(o)
    return out


def export_item(name, coll, path):
    """FBX with one object per socket (origin at the bone head) + the material order per object in slots.txt."""
    objs = [o for o in coll.objects if o.name.startswith(name + "__")]
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    # Origins stay at the bone heads: Unity only reads the meshes, which are relative to them
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True,
                             use_mesh_modifiers=True, mesh_smooth_type='OFF', add_leaf_bones=False, bake_anim=False,
                             object_types={'MESH'})
    slots = os.path.join(os.path.dirname(path), "slots.txt")
    lines = [l for l in (open(slots).read().splitlines() if os.path.exists(slots) else ["# object|material slots in submesh order"])
             if not l.startswith(name + "__")]
    lines += [o.name + "|" + ",".join(m.name for m in o.data.materials) for o in objs]
    open(slots, "w").write("\n".join(lines))
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)


@item
def ShortSword(M):
    """Broad straight blade held point-forward, chunky crossguard and pommel."""
    hx, hy, hz = BONES["hand_R"][0]
    blade = box((hx, hy - 0.5, hz + 0.02), (0.07, 0.72, 0.14), M["metal"], bevel=0.01)
    for v in blade.data.vertices:                     # point (object space: the blade's middle is at 0)
        if v.co.y < -0.3: v.co.z *= 0.15
    guard = box((hx, hy - 0.12, hz + 0.02), (0.1, 0.07, 0.34), M["metal"])
    grip = box((hx, hy, hz + 0.02), (0.06, 0.18, 0.06), M["leather"])
    pommel = B.prim('ico', (hx, hy + 0.11, hz + 0.02), (0.1, 0.1, 0.1), m=M["metal"], sub=1)
    return {"hand_R": [blade, guard, grip, pommel]}


@item
def GreatHelm(M):
    """Bucket helm over the whole head, eye slit and breathing holes, flat crown."""
    hx, hy, hz = BONES["head"][0]
    shell = taper(box((0, -0.02, 1.73), (0.46, 0.46, 0.48), M["metal"], bevel=0.03), 0.92)
    crown = box((0, -0.02, 1.98), (0.42, 0.42, 0.05), M["metal"], bevel=0.015)
    slit = box((0, -0.255, 1.76), (0.34, 0.03, 0.05), M["dark"], bevel=0)
    rib = box((0, -0.255, 1.66), (0.05, 0.035, 0.22), M["metal"], bevel=0.01)
    holes = [box((x, -0.25, 1.62), (0.03, 0.03, 0.03), M["dark"], bevel=0) for x in (-0.12, -0.07, 0.07, 0.12)]
    return {"head": [shell, crown, slit, rib] + holes}
