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
        "iron": B.mat("CT_Iron", "#5A5E68"),
        "wine": B.mat("CT_Cloth", "#6E2A44"),
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
    skill_clips(arm)
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


def skill_clips(arm):
    """Skill clips (RigidClip names). Arm angles: pitch < 0 swings the arm forward (-90 level, -180 straight up),
    roll > 0 swings it toward +X (out for arm_L, in for arm_R), yaw > 0 turns left."""
    R.clip(arm, "PL_Base_Cast", 24, {                 # fireball wind-up: right hand raised, left hand aiming
        "chest": [(0, (-5, 15, 0)), (12, (-6, 17, 0)), (24, (-5, 15, 0))],
        "arm_R": [(0, (-125, 0, -20)), (12, (-130, 0, -24)), (24, (-125, 0, -20))],
        "arm_L": [(0, (-70, 0, 6)), (12, (-72, 0, 6)), (24, (-70, 0, 6))],
        "head": [(0, (-8, -10, 0)), (24, (-8, -10, 0))],
    }, loop=True)
    R.clip(arm, "PL_Base_Release", 12, {               # throw
        "chest": [(0, (-5, 20, 0)), (3, (12, -22, 0)), (12, (0, 0, 0))],
        "arm_R": [(0, (-140, 0, -20)), (3, (-75, 0, -4)), (12, (-15, 0, 0))],
        "arm_L": [(0, (-70, 0, 6)), (3, (-15, 0, 18)), (12, (0, 0, 0))],
        "hips": [(0, (0, 0, 0), (0, 0, 0)), (3, (0, 0, 0), (0, -0.05, -0.02)), (12, (0, 0, 0), (0, 0, 0))],
    })
    R.clip(arm, "PL_Base_Shoot", 10, {                 # barrage bolt flung upward
        "chest": [(0, (0, 0, 0)), (3, (-10, 5, 0)), (10, (0, 0, 0))],
        "arm_R": [(0, (-20, 0, 0)), (3, (-170, 0, -10)), (10, (-20, 0, 0))],
        "arm_L": [(0, (0, 0, 0)), (3, (-30, 0, 10)), (10, (0, 0, 0))],
    })
    for side, arm_bone, other, s in (("L", "arm_L", "arm_R", 1), ("R", "arm_R", "arm_L", -1)):
        R.clip(arm, "PL_Base_Punch" + side, 7, {     # Fists of Thunder: the torso turns into the punch
            "chest": [(0, (0, 0, 0)), (2, (6, -25 * s, 0)), (7, (0, -6 * s, 0))],
            "hips": [(0, (0, 0, 0)), (2, (0, -8 * s, 0)), (7, (0, 0, 0))],
            arm_bone: [(0, (-25, 0, 0)), (2, (-95, 0, -6 * s)), (7, (-30, 0, 0))],
            other: [(0, (-10, 0, 0)), (2, (20, 0, 0)), (7, (-10, 0, 0))],
        })
    R.clip(arm, "PL_Base_Spin", 12, {                  # whirlwind: arms out, weapon leading (the body itself spins)
        "chest": [(0, (5, 0, 0)), (12, (5, 0, 0))],
        "arm_L": [(0, (0, 0, 80)), (6, (-4, 0, 84)), (12, (0, 0, 80))],
        "arm_R": [(0, (-10, 0, -80)), (6, (-14, 0, -84)), (12, (-10, 0, -80))],
        "leg_L": [(0, (0, 0, 8)), (12, (0, 0, 8))],
        "leg_R": [(0, (0, 0, -8)), (12, (0, 0, -8))],
        "hips": [(0, (0, 0, 0), (0, 0, 0)), (6, (0, 0, 0), (0, 0, 0.03)), (12, (0, 0, 0), (0, 0, 0))],
    }, loop=True)
    R.clip(arm, "PL_Base_Dash", 12, {                  # low forward lunge, arms swept back
        "chest": [(0, (35, 0, 0)), (12, (35, 0, 0))],
        "head": [(0, (-25, 0, 0)), (12, (-25, 0, 0))],
        "hips": [(0, (0, 0, 0), (0, 0, -0.06)), (12, (0, 0, 0), (0, 0, -0.06))],
        "leg_L": [(0, (-45, 0, 0)), (12, (-45, 0, 0))],
        "leg_R": [(0, (35, 0, 0)), (12, (35, 0, 0))],
        "arm_L": [(0, (50, 0, 10)), (12, (50, 0, 10))],
        "arm_R": [(0, (50, 0, -10)), (12, (50, 0, -10))],
    }, loop=True)
    R.clip(arm, "PL_Base_Sweep", 14, {                 # tornado: level arm swept across with the torso
        "chest": [(0, (0, 45, 0)), (5, (4, -45, 0)), (14, (0, 0, 0))],
        "arm_R": [(0, (-80, 0, 0)), (5, (-85, 0, 10)), (14, (-15, 0, 0))],
        "arm_L": [(0, (-20, 0, 0)), (5, (-10, 0, 10)), (14, (0, 0, 0))],
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


def pair(prefix, fn):
    """Builds a left and a right piece: fn(sign) with sign +1 for the left (+X) side."""
    return {f"{prefix}_L": fn(1), f"{prefix}_R": fn(-1)}


def point(o, axis_value, axis=1, keep=0.15, below=True):
    """Squeezes the far end of a box (object-space coordinate past axis_value) to a point/edge."""
    for v in o.data.vertices:
        if (v.co[axis] < axis_value) if below else (v.co[axis] > axis_value):
            v.co.z *= keep
    return o


# --- weapons and off-hands ---

@item
def Dagger(M):
    """Short broad blade held point-forward, small guard."""
    hx, hy, hz = BONES["hand_R"][0]
    blade = point(box((hx, hy - 0.32, hz + 0.02), (0.06, 0.36, 0.12), M["metal"], bevel=0.01), -0.1)
    guard = box((hx, hy - 0.12, hz + 0.02), (0.09, 0.06, 0.24), M["iron"])
    grip = box((hx, hy, hz + 0.02), (0.055, 0.16, 0.055), M["leather"])
    pommel = box((hx, hy + 0.1, hz + 0.02), (0.08, 0.06, 0.08), M["iron"])
    return {"hand_R": [blade, guard, grip, pommel]}


@item
def Crossbow(M):
    """Wooden stock held forward, iron prod across the front, string and a loaded bolt."""
    hx, hy, hz = BONES["hand_R"][0]
    stock = box((hx, hy - 0.16, hz + 0.05), (0.09, 0.62, 0.1), M["wood"])
    butt = box((hx, hy + 0.12, hz + 0.02), (0.1, 0.14, 0.16), M["wood"])
    prod = [box((hx + s * 0.17, hy - 0.46 + 0.04, hz + 0.08), (0.34, 0.06, 0.07), M["iron"], rot=(0, 0, s * 12)) for s in (1, -1)]
    string = box((hx, hy - 0.38, hz + 0.08), (0.6, 0.015, 0.02), M["dark"], bevel=0)
    bolt = box((hx, hy - 0.42, hz + 0.12), (0.03, 0.4, 0.03), M["wood"], bevel=0)
    tip = box((hx, hy - 0.64, hz + 0.12), (0.06, 0.06, 0.05), M["metal"], bevel=0.01)
    return {"hand_R": [stock, butt, string, bolt, tip] + prod}


@item
def Buckler(M):
    """Small round fist shield with a big iron boss, held forward."""
    hx, hy, hz = BONES["hand_L"][0]
    disc = B.prim('cyl', (hx + 0.03, hy - 0.13, hz + 0.02), (0.42, 0.42, 0.06), (90, 0, 0), M["wood"], seg=12)
    rim = B.prim('cyl', (hx + 0.03, hy - 0.11, hz + 0.02), (0.46, 0.46, 0.03), (90, 0, 0), M["iron"], seg=12)
    boss = B.prim('ico', (hx + 0.03, hy - 0.18, hz + 0.02), (0.16, 0.12, 0.16), m=M["metal"], sub=1)
    return {"hand_L": [disc, rim, boss]}


@item
def TowerShield(M):
    """Tall slightly curved rectangle covering the left side, iron frame, wine stripe."""
    hx, hy, hz = BONES["hand_L"][0]
    cx, cy, cz = hx + 0.05, hy - 0.16, hz + 0.12
    face = box((cx, cy, cz), (0.52, 0.06, 0.96), M["wood"], bevel=0.015)
    for v in face.data.vertices:                     # curve around the bearer (object space)
        v.co.y += (v.co.x / 0.26) ** 2 * 0.05
    frame = [box((cx, cy - 0.01, cz + s * 0.47), (0.55, 0.08, 0.06), M["iron"]) for s in (1, -1)]
    frame += [box((cx + s * 0.26, cy + 0.03, cz), (0.06, 0.08, 0.98), M["iron"]) for s in (1, -1)]
    stripe = box((cx, cy - 0.035, cz), (0.14, 0.02, 0.9), M["wine"], bevel=0)
    boss = B.prim('ico', (cx, cy - 0.06, cz + 0.08), (0.14, 0.08, 0.14), m=M["metal"], sub=1)
    return {"hand_L": [face, stripe, boss] + frame}


# --- armour ---

@item
def Cap(M):
    """Leather skullcap with a band and a short wine plume at the back."""
    cap = B.prim('ico', (0, 0.01, 1.83), (0.45, 0.45, 0.32), m=M["leather"], sub=2)
    for v in cap.data.vertices:
        if v.co.z < -0.02: v.co.z = -0.02
    band = B.prim('cyl', (0, 0.0, 1.8), (0.46, 0.46, 0.06), m=M["iron"], seg=12)
    plume = box((0, 0.2, 1.95), (0.05, 0.16, 0.18), M["wine"], rot=(-30, 0, 0))
    return {"head": [cap, band, plume]}


@item
def QuiltedArmor(M):
    """Padded gambeson: thick wine jacket with dark quilting lines and a short skirt."""
    jacket = taper(box((0, 0, 1.25), (0.58, 0.37, 0.46), M["wine"], bevel=0.03), 1.08)
    collar = box((0, 0, 1.49), (0.36, 0.3, 0.07), M["wine"])
    lines = [box((0, 0, z), (0.6, 0.39, 0.018), M["dark"], bevel=0) for z in (1.12, 1.22, 1.32)]
    lines.append(box((0, -0.19, 1.25), (0.02, 0.02, 0.44), M["dark"], bevel=0))
    skirt = taper(box((0, 0, 0.88), (0.54, 0.34, 0.24), M["wine"], bevel=0.03), 0.85)
    belt = box((0, 0, 0.99), (0.52, 0.33, 0.05), M["leather"])
    return {"chest": [jacket, collar] + lines, "hips": [skirt, belt]}


@item
def FullPlateMail(M):
    """Breastplate with a centre ridge and gorget, stacked faulds over the hips."""
    plate = taper(box((0, -0.01, 1.25), (0.6, 0.39, 0.46), M["metal"], bevel=0.04), 1.1)
    ridge = box((0, -0.2, 1.25), (0.05, 0.04, 0.42), M["metal"], bevel=0.01)
    gorget = B.prim('cyl', (0, 0, 1.5), (0.36, 0.34, 0.1), m=M["iron"], seg=10)
    trim = box((0, 0, 1.05), (0.6, 0.39, 0.04), M["iron"])
    faulds = [taper(box((0, 0, z), (0.5 + i * 0.04, 0.33 + i * 0.02, 0.1), M["metal"], bevel=0.015), 0.92) for i, z in enumerate((0.95, 0.86, 0.77))]
    return {"chest": [plate, ridge, gorget, trim], "hips": faulds}


@item
def ClothLeggings(M):
    """Wine cloth leg wraps with crossing leather straps."""
    def leg(s):
        x = 0.13 * s
        wrap = taper(box((x, 0, 0.55), (0.22, 0.23, 0.62), M["wine"]), 0.85)
        straps = [box((x, 0, z), (0.235, 0.245, 0.03), M["leather"], bevel=0, rot=(0, s * 12, 0)) for z in (0.36, 0.5, 0.64)]
        return [wrap] + straps
    return pair("leg", leg)


@item
def MailChausses(M):
    """Iron mail legs with polished knee cops."""
    def leg(s):
        x = 0.13 * s
        mail = taper(box((x, 0, 0.55), (0.225, 0.235, 0.63), M["iron"]), 0.88)
        knee = box((x, -0.12, 0.5), (0.18, 0.06, 0.14), M["metal"], bevel=0.02)
        return [mail, knee]
    return pair("leg", leg)


@item
def LeatherBoots(M):
    """Tall leather boots with folded cuffs."""
    def leg(s):
        x = 0.13 * s
        boot = box((x, -0.04, 0.15), (0.24, 0.36, 0.3), M["leather"], bevel=0.04)
        cuff = box((x, 0, 0.31), (0.26, 0.26, 0.07), M["dark"], bevel=0.02)
        return [boot, cuff]
    return pair("leg", leg)


@item
def PlatedBoots(M):
    """Pointed sabatons and greaves up to the knee."""
    def leg(s):
        x = 0.13 * s
        sabaton = point(box((x, -0.06, 0.1), (0.24, 0.4, 0.2), M["metal"], bevel=0.02), -0.14, keep=0.45)
        greave = taper(box((x, 0, 0.35), (0.24, 0.25, 0.32), M["metal"], bevel=0.02), 1.08)
        rim = box((x, 0, 0.51), (0.25, 0.26, 0.03), M["iron"], bevel=0)
        return [sabaton, greave, rim]
    return pair("leg", leg)


@item
def LeatherGloves(M):
    """Oversized leather gloves with flared cuffs."""
    def arm(s):
        x = 0.42 * s
        glove = box((x, -0.12, 0.84), (0.2, 0.22, 0.2), M["leather"], bevel=0.04)
        cuff = taper(box((x, -0.08, 0.98), (0.16, 0.17, 0.1), M["leather"]), 1.25)
        return [glove, cuff]
    return pair("arm", arm)


@item
def Gauntlets(M):
    """Steel gauntlets: blocky fist, knuckle ridge, wide cuff."""
    def arm(s):
        x = 0.42 * s
        fist = box((x, -0.12, 0.84), (0.21, 0.23, 0.21), M["metal"], bevel=0.03)
        knuckles = box((x, -0.24, 0.86), (0.2, 0.04, 0.07), M["iron"], bevel=0.01)
        cuff = taper(box((x, -0.07, 0.99), (0.17, 0.18, 0.14), M["metal"], bevel=0.02), 1.3)
        return [fist, knuckles, cuff]
    return pair("arm", arm)


@item
def ClothMantle(M):
    """Shoulder mantle with a rolled hood and a short ragged cape down the back."""
    mantle = taper(box((0, 0.02, 1.44), (0.74, 0.42, 0.16), M["cloak"], bevel=0.03), 0.8)
    hood = B.prim('cyl', (0, 0.08, 1.52), (0.42, 0.34, 0.1), m=M["cloak"], seg=10)
    cape = box((0, 0.2, 1.16), (0.6, 0.05, 0.5), M["cloak"], bevel=0.01)
    for v in cape.data.vertices:                     # ragged hem (object space)
        if v.co.z < -0.2: v.co.z -= 0.06 * ((int((v.co.x + 0.3) * 20) % 2))
    clasp = box((0, -0.2, 1.43), (0.08, 0.04, 0.08), M["iron"])
    return {"chest": [mantle, hood, cape, clasp]}


@item
def PlatePauldrons(M):
    """Big rounded steel shoulder plates with two lames, on the arms so they move with them."""
    def arm(s):
        x = 0.42 * s
        dome = B.prim('ico', (x + 0.03 * s, 0, 1.48), (0.36, 0.38, 0.26), m=M["metal"], sub=1)
        for v in dome.data.vertices:
            if v.co.z < -0.02: v.co.z = -0.02
        lames = [box((x + 0.04 * s, 0, 1.4 - i * 0.07), (0.32 - i * 0.03, 0.34 - i * 0.03, 0.05), M["iron"], bevel=0.01, rot=(0, -s * 15, 0)) for i in range(2)]
        return [dome] + lames
    return pair("arm", arm)


@item
def Sash(M):
    """Wine cloth sash knotted at the left hip, tails hanging."""
    band = box((0, 0, 0.92), (0.48, 0.31, 0.12), M["wine"], bevel=0.02)
    knot = box((0.16, -0.15, 0.92), (0.1, 0.06, 0.1), M["wine"], bevel=0.02)
    tails = [box((0.16 + dx, -0.17, 0.78), (0.06, 0.03, 0.24), M["wine"], bevel=0.01, rot=(0, r, 0)) for dx, r in ((-0.03, 8), (0.04, -10))]
    return {"hips": [band, knot] + tails}


@item
def PlatedBelt(M):
    """Wide leather belt with steel plates and a square buckle."""
    belt = box((0, 0, 0.92), (0.48, 0.31, 0.1), M["leather"], bevel=0.015)
    plates = [box((x, -0.16, 0.92), (0.08, 0.03, 0.12), M["metal"], bevel=0.01) for x in (-0.17, 0.17)]
    plates += [box((s * 0.25, 0, 0.92), (0.03, 0.2, 0.12), M["metal"], bevel=0.01) for s in (1, -1)]
    buckle = box((0, -0.165, 0.92), (0.11, 0.035, 0.11), M["iron"], bevel=0.01)
    return {"hips": [belt, buckle] + plates}


@item
def GreatHelm(M):
    """Bucket helm over the whole head, eye slit and breathing holes, flat crown."""
    hx, hy, hz = BONES["head"][0]
    shell = taper(box((0, 0, 1.74), (0.46, 0.5, 0.5), M["metal"], bevel=0.03), 0.92)      # deep enough to hold the hair
    crown = box((0, 0, 2.0), (0.42, 0.46, 0.05), M["metal"], bevel=0.015)
    slit = box((0, -0.255, 1.76), (0.34, 0.03, 0.05), M["dark"], bevel=0)
    rib = box((0, -0.255, 1.66), (0.05, 0.035, 0.22), M["metal"], bevel=0.01)
    holes = [box((x, -0.25, 1.62), (0.03, 0.03, 0.03), M["dark"], bevel=0) for x in (-0.12, -0.07, 0.07, 0.12)]
    return {"head": [shell, crown, slit, rib] + holes}
