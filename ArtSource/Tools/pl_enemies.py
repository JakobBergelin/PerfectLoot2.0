"""Perfect Loot common enemies for Blender (5.x): Skeleton, Zombie, Spider and training Dummy.

Rigid parts on a small rig (pl_rig) with Idle/Move/Attack/Hit/Death clips (the Dummy only Idle and Hit). Bodies follow
the style guide: chunky, readable silhouettes, lighter than the floors and in hues the floors lack.
Front is -Y (Unity +Z), left is +X, feet at z = 0.

Usage inside Blender:
    import pl_enemies as E
    arm, mesh = E.build("Skeleton", collection)
    E.export(arm, mesh, fbx_path)
"""
import bpy
from mathutils import Vector
import pl_blender as B
import pl_rig as R
from pl_player_equipment import join, box, taper

BUILDERS = {}


def enemy(fn):
    BUILDERS[fn.__name__] = fn
    return fn


def mats():
    return {
        "bone": B.mat("EN_Bone", "#E2D8BE"),
        "bone_dark": B.mat("EN_BoneDark", "#8A7E6A"),
        "rags": B.mat("CT_Cloth", "#6E2A44"),
        "iron": B.mat("CT_Iron", "#5A5E68"),
        "eye": B.mat("CT_GhostEye", "#E8F0F8", emit="#C8E0FF"),
        "skin": B.mat("EN_ZombieSkin", "#7E9A68"),
        "flesh": B.mat("EN_ZombieFlesh", "#5A3A44"),
        "shirt": B.mat("EN_ZombieRags", "#8A7A62"),
        "chitin": B.mat("EN_SpiderChitin", "#8E6A5A"),
        "mark": B.mat("EN_SpiderMark", "#C8447E", emit="#FF5FA8", strength=1.0),
        "burlap": B.mat("EN_Burlap", "#B89A6A"),
        "straw": B.mat("EN_Straw", "#D8C07A"),
        "wood": B.mat("CT_Wood", "#5C3E30"),
        "dark": B.mat("CT_DarkStone", "#3E3A4C"),
    }


def assemble(name, coll, bones, parts):
    """parts: [(object, bone)]. Rigs them on bones (pl_rig) with each part on the bone it was listed with."""
    centres = [((sum((o.matrix_world @ Vector(c) for c in o.bound_box), Vector()) / 8), bone) for o, bone in parts]
    src = join([o for o, _ in parts], name + "_Src", coll)

    def classify(c, s, m):
        return min(centres, key=lambda e: (e[0] - Vector(c)).length)[1]

    arm, mesh, counts = R.build(src, name + "_Rig", coll, bones, classify)
    bpy.data.objects.remove(src, do_unlink=True)
    mesh.name = mesh.data.name = name + "_Animated"
    return arm, mesh, counts


def build(name, coll):
    for o in list(coll.objects):
        if o.name.startswith(name + "_"): bpy.data.objects.remove(o, do_unlink=True)
    for a in [a for a in bpy.data.actions if a.name.startswith(name + "_")]: bpy.data.actions.remove(a)
    return BUILDERS[name](coll, mats())


def export(arm, mesh, path):
    R.export(arm, mesh, path)


HUMANOID = {
    "root": ((0, 0, 0), None),
    "hips": ((0, 0, 0.9), "root"),
    "leg_L": ((0.14, 0, 0.88), "hips"),
    "leg_R": ((-0.14, 0, 0.88), "hips"),
    "spine": ((0, 0, 1.0), "hips"),
    "head": ((0, -0.05, 1.55), "spine"),
    "arm_L": ((0.34, 0, 1.45), "spine"),
    "arm_R": ((-0.34, 0, 1.45), "spine"),
}


@enemy
def Skeleton(coll, M):
    """Fast and frail: a big grinning skull, chunky rib bars, a rusty blade and a scrap of wine loincloth."""
    P = []
    for s in (1, -1):
        x = 0.14 * s
        P += [(box((x, 0, 0.62), (0.09, 0.09, 0.46), M["bone"]), f"leg_{'L' if s > 0 else 'R'}"),
              (box((x, 0, 0.36), (0.13, 0.12, 0.1), M["bone_dark"]), f"leg_{'L' if s > 0 else 'R'}"),
              (box((x, 0, 0.18), (0.08, 0.08, 0.3), M["bone"]), f"leg_{'L' if s > 0 else 'R'}"),
              (box((x, -0.06, 0.04), (0.14, 0.24, 0.08), M["bone"]), f"leg_{'L' if s > 0 else 'R'}")]
        a = f"arm_{'L' if s > 0 else 'R'}"
        P += [(box((0.36 * s, -0.02, 1.25), (0.08, 0.08, 0.38), M["bone"]), a),
              (box((0.37 * s, -0.08, 0.96), (0.07, 0.07, 0.28), M["bone"]), a),
              (box((0.37 * s, -0.12, 0.78), (0.13, 0.14, 0.13), M["bone"]), a)]
    P.append((box((0, 0, 0.92), (0.34, 0.18, 0.12), M["bone_dark"]), "hips"))                        # pelvis
    P.append((box((0, -0.11, 0.8), (0.24, 0.04, 0.24), M["rags"], rot=(6, 0, 0)), "hips"))            # loincloth
    P.append((box((0, 0.02, 1.2), (0.07, 0.07, 0.42), M["bone_dark"]), "spine"))                      # spine
    for i, z in enumerate((1.08, 1.18, 1.28, 1.38)):
        P.append((box((0, -0.02, z), (0.44 - i * 0.02 if i < 3 else 0.42, 0.26, 0.05), M["bone"]), "spine"))  # ribs
    P.append((box((0, 0, 1.46), (0.6, 0.18, 0.07), M["bone"]), "spine"))                              # collarbones
    skull = box((0, -0.04, 1.72), (0.34, 0.36, 0.3), M["bone"], bevel=0.05)
    jaw = box((0, -0.1, 1.55), (0.26, 0.22, 0.08), M["bone"], bevel=0.02)
    P += [(skull, "head"), (jaw, "head")]
    for s in (1, -1):
        P.append((box((0.08 * s, -0.22, 1.74), (0.09, 0.03, 0.08), M["eye"], bevel=0), "head"))
    P.append((box((0, -0.225, 1.64), (0.05, 0.02, 0.05), M["dark"], bevel=0), "head"))
    blade = box((-0.37, -0.42, 0.8), (0.05, 0.6, 0.12), M["iron"], bevel=0.01, rot=(-10, 0, 0))       # rusty sword
    guard = box((-0.37, -0.14, 0.78), (0.08, 0.05, 0.24), M["iron"])
    P += [(blade, "arm_R"), (guard, "arm_R")]
    arm, mesh, counts = assemble("EN_Skeleton", coll, HUMANOID, P)
    humanoid_clips(arm, "EN_Skeleton", speed=1.3, hunch=4)
    return arm, mesh


@enemy
def Zombie(coll, M):
    """Slow and heavy: hunched, one long arm reaching out, torn shirt, a sagging head."""
    P = []
    for s in (1, -1):
        leg = f"leg_{'L' if s > 0 else 'R'}"
        P += [(taper(box((0.15 * s, 0, 0.5), (0.2, 0.21, 0.64), M["shirt"]), 0.85), leg),
              (box((0.15 * s, -0.04, 0.08), (0.22, 0.3, 0.16), M["skin"], bevel=0.03), leg)]
    P.append((box((0, 0.02, 0.9), (0.44, 0.28, 0.2), M["shirt"]), "hips"))
    torso = taper(box((0, 0.04, 1.22), (0.56, 0.36, 0.48), M["shirt"], bevel=0.03), 1.15)
    P.append((torso, "spine"))
    P.append((box((0.06, -0.15, 1.2), (0.2, 0.08, 0.2), M["flesh"]), "spine"))                        # torn-open belly
    P.append((box((0.06, -0.19, 1.2), (0.16, 0.02, 0.03), M["bone"], bevel=0), "spine"))              # ribs showing
    P.append((box((0.06, -0.19, 1.26), (0.16, 0.02, 0.03), M["bone"], bevel=0), "spine"))
    P.append((box((0, 0.02, 1.47), (0.66, 0.32, 0.12), M["shirt"]), "spine"))
    head = box((0.03, -0.12, 1.66), (0.32, 0.32, 0.34), M["skin"], bevel=0.05)
    P.append((head, "head"))
    P.append((box((0.03, -0.27, 1.56), (0.22, 0.06, 0.07), M["flesh"]), "head"))                      # slack jaw
    for s in (1, -1):
        P.append((box((0.03 + 0.08 * s, -0.285, 1.7), (0.08, 0.03, 0.06), M["eye"], bevel=0), "head"))
    # Left arm reaching forward, right arm hanging
    P += [(box((0.38, -0.25, 1.38), (0.13, 0.5, 0.13), M["shirt"]), "arm_L"),
          (box((0.39, -0.58, 1.34), (0.11, 0.3, 0.1), M["skin"]), "arm_L"),
          (box((0.39, -0.78, 1.33), (0.16, 0.16, 0.13), M["skin"], bevel=0.03), "arm_L"),
          (box((-0.38, 0, 1.2), (0.13, 0.13, 0.46), M["shirt"]), "arm_R"),
          (box((-0.39, -0.03, 0.88), (0.1, 0.1, 0.24), M["skin"]), "arm_R"),
          (box((-0.39, -0.05, 0.7), (0.16, 0.16, 0.15), M["skin"], bevel=0.03), "arm_R")]
    bones = dict(HUMANOID)
    bones["arm_L"] = ((0.36, 0, 1.45), "spine")
    bones["arm_R"] = ((-0.36, 0, 1.45), "spine")
    arm, mesh, counts = assemble("EN_Zombie", coll, bones, P)
    humanoid_clips(arm, "EN_Zombie", speed=0.7, hunch=14)
    return arm, mesh


def humanoid_clips(arm, n, speed=1.0, hunch=0):
    """Idle sway, a walk (fast and light, or slow and dragging), an overhead chop, a recoil and a collapse."""
    h = hunch
    R.clip(arm, n + "_Idle", 48, {
        "spine": [(0, (h, 0, 0)), (24, (h + 3, 3, 0)), (48, (h, 0, 0))],
        "head": [(0, (-h * 0.6, 0, 0)), (24, (-h * 0.6 + 4, -6, 3)), (48, (-h * 0.6, 0, 0))],
        "arm_L": [(0, (0, 0, 0)), (24, (-4, 0, 3)), (48, (0, 0, 0))],
        "arm_R": [(0, (0, 0, 0)), (24, (-4, 0, -3)), (48, (0, 0, 0))],
        "hips": [(0, (0, 0, 0), (0, 0, 0)), (24, (0, 0, 0), (0, 0, -0.02)), (48, (0, 0, 0), (0, 0, 0))],
    }, loop=True)
    a = 34 if speed > 1 else 22
    R.clip(arm, n + "_Move", 24 if speed > 1 else 36, {
        "hips": [(0, (0, 5, 0), (0, 0, 0)), (6 if speed > 1 else 9, (0, 0, 0), (0, 0, 0.04)), (12 if speed > 1 else 18, (0, -5, 0), (0, 0, 0)),
                 (18 if speed > 1 else 27, (0, 0, 0), (0, 0, 0.04)), (24 if speed > 1 else 36, (0, 5, 0), (0, 0, 0))],
        "spine": [(0, (h + 8, -6, 0)), (12 if speed > 1 else 18, (h + 8, 6, 0)), (24 if speed > 1 else 36, (h + 8, -6, 0))],
        "leg_L": [(0, (a, 0, 0)), (12 if speed > 1 else 18, (-a, 0, 0)), (24 if speed > 1 else 36, (a, 0, 0))],
        "leg_R": [(0, (-a, 0, 0)), (12 if speed > 1 else 18, (a, 0, 0)), (24 if speed > 1 else 36, (-a, 0, 0))],
        "arm_L": [(0, (-a * 0.7, 0, 0)), (12 if speed > 1 else 18, (a * 0.5, 0, 0)), (24 if speed > 1 else 36, (-a * 0.7, 0, 0))],
        "arm_R": [(0, (a * 0.5, 0, 0)), (12 if speed > 1 else 18, (-a * 0.7, 0, 0)), (24 if speed > 1 else 36, (a * 0.5, 0, 0))],
        "head": [(0, (-h * 0.6, 4, 0)), (12 if speed > 1 else 18, (-h * 0.6, -4, 0)), (24 if speed > 1 else 36, (-h * 0.6, 4, 0))],
    }, loop=True)
    R.clip(arm, n + "_Attack", 24, {
        "spine": [(0, (h, 0, 0)), (8, (h - 14, 20, 0)), (12, (h + 22, -16, 0)), (24, (h, 0, 0))],
        "arm_R": [(0, (0, 0, 0)), (8, (-165, 0, -10)), (12, (-60, 0, 0)), (24, (0, 0, 0))],
        "arm_L": [(0, (0, 0, 0)), (8, (-40, 0, 10)), (12, (-80, 0, 0)), (24, (0, 0, 0))],
        "hips": [(0, (0, 0, 0), (0, 0, 0)), (12, (0, 0, 0), (0, -0.08, -0.04)), (24, (0, 0, 0), (0, 0, 0))],
    })
    R.clip(arm, n + "_Hit", 12, {
        "spine": [(0, (h, 0, 0)), (3, (h - 18, 8, 0)), (12, (h, 0, 0))],
        "head": [(0, (0, 0, 0)), (3, (-14, -10, 0)), (12, (0, 0, 0))],
        "hips": [(0, (0, 0, 0), (0, 0, 0)), (3, (0, 0, 0), (0, 0.07, 0)), (12, (0, 0, 0), (0, 0, 0))],
    })
    R.clip(arm, n + "_Death", 40, {
        "hips": [(0, (0, 0, 0), (0, 0, 0)), (16, (0, 0, 0), (0, 0.1, -0.5)), (40, (0, 0, 0), (0, 0.15, -0.82))],
        "spine": [(0, (h, 0, 0)), (16, (h + 40, 10, 0)), (40, (h + 80, 15, 0))],
        "head": [(0, (0, 0, 0)), (20, (30, 20, 0)), (40, (40, 25, 0))],
        "leg_L": [(0, (0, 0, 0)), (16, (-60, 0, 0)), (40, (-90, 0, 10))],
        "leg_R": [(0, (0, 0, 0)), (16, (-50, 0, 0)), (40, (-85, 0, -10))],
        "arm_L": [(0, (0, 0, 0)), (20, (-60, 0, 30)), (40, (-80, 0, 50))],
        "arm_R": [(0, (0, 0, 0)), (20, (-50, 0, -30)), (40, (-70, 0, -50))],
    })


@enemy
def Spider(coll, M):
    """Swarm creature: a squat chitin body, a rose-marked abdomen, eight chunky legs in two alternating groups,
    big fangs. Built at full size; the definition scales it to 0.5."""
    bones = {
        "root": ((0, 0, 0), None),
        "body": ((0, 0, 0.55), "root"),
        "abdomen": ((0, 0.25, 0.6), "body"),
        "fangs": ((0, -0.42, 0.5), "body"),
        "legs_A": ((0, 0, 0.55), "body"),   # front-left, second-right, third-left, back-right
        "legs_B": ((0, 0, 0.55), "body"),
    }
    P = [(box((0, -0.1, 0.58), (0.56, 0.6, 0.3), M["chitin"], bevel=0.06), "body")]
    abdomen = B.prim('ico', (0, 0.48, 0.7), (0.9, 1.0, 0.72), m=M["chitin"], sub=1)
    mark = box((0, 0.5, 1.04), (0.3, 0.42, 0.06), M["mark"], bevel=0.02)
    P += [(abdomen, "abdomen"), (mark, "abdomen")]
    for s in (1, -1):
        P.append((box((0.12 * s, -0.46, 0.4), (0.08, 0.08, 0.24), M["bone"], bevel=0.01, rot=(-15, 0, 0)), "fangs"))
        P.append((box((0.1 * s, -0.41, 0.66), (0.08, 0.03, 0.06), M["eye"], bevel=0), "body"))
        P.append((box((0.05 * s, -0.41, 0.72), (0.05, 0.03, 0.04), M["eye"], bevel=0), "body"))
    angles = (-45, -15, 15, 45)   # front to back
    for i, ang in enumerate(angles):
        for s in (1, -1):
            group = "legs_A" if (i + (s < 0)) % 2 == 0 else "legs_B"
            import math
            a = math.radians(ang)
            dx, dy = s * math.cos(a), math.sin(a)
            hip = Vector((0.25 * s, -0.1 + dy * 0.2, 0.6))
            knee = Vector((hip.x + dx * 0.45, hip.y + dy * 0.45, 0.95))
            foot = Vector((hip.x + dx * 0.95, hip.y + dy * 0.95, 0.04))
            for p0, p1, w in ((hip, knee, 0.11), (knee, foot, 0.09)):
                mid = (p0 + p1) / 2
                seg = box(mid, (w, w, (p1 - p0).length + 0.06), M["chitin"] if p0 is hip else M["dark"], bevel=0.01)
                seg.rotation_mode = 'QUATERNION'
                seg.rotation_quaternion = Vector((0, 0, 1)).rotation_difference((p1 - p0).normalized())
                bpy.context.view_layer.update()
                seg.data.transform(seg.matrix_basis)
                seg.matrix_basis.identity()
                P.append((seg, group))
    arm, mesh, counts = assemble("EN_Spider", coll, bones, P)
    R.clip(arm, "EN_Spider_Idle", 24, {
        "body": [(0, (0, 0, 0), (0, 0, 0)), (12, (2, 0, 0), (0, 0, -0.03)), (24, (0, 0, 0), (0, 0, 0))],
        "abdomen": [(0, (0, 0, 0)), (12, (-6, 0, 0)), (24, (0, 0, 0))],
        "fangs": [(0, (0, 0, 0)), (6, (10, 0, 0)), (12, (0, 0, 0)), (18, (10, 0, 0)), (24, (0, 0, 0))],
    }, loop=True)
    R.clip(arm, "EN_Spider_Move", 8, {          # quick alternating tripod scuttle
        "legs_A": [(0, (12, 10, 0), (0, 0, 0.06)), (4, (-12, -10, 0), (0, 0, 0)), (8, (12, 10, 0), (0, 0, 0.06))],
        "legs_B": [(0, (-12, -10, 0), (0, 0, 0)), (4, (12, 10, 0), (0, 0, 0.06)), (8, (-12, -10, 0), (0, 0, 0))],
        "body": [(0, (0, 0, 0), (0, 0, 0.02)), (2, (0, 0, 0), (0, 0, 0.05)), (4, (0, 0, 0), (0, 0, 0.02)), (6, (0, 0, 0), (0, 0, 0.05)), (8, (0, 0, 0), (0, 0, 0.02))],
        "abdomen": [(0, (0, 4, 0)), (4, (0, -4, 0)), (8, (0, 4, 0))],
    }, loop=True)
    R.clip(arm, "EN_Spider_Attack", 14, {       # rear up and bite
        "body": [(0, (0, 0, 0), (0, 0, 0)), (5, (-30, 0, 0), (0, 0.1, 0.15)), (8, (15, 0, 0), (0, -0.25, 0)), (14, (0, 0, 0), (0, 0, 0))],
        "fangs": [(0, (0, 0, 0)), (5, (-30, 0, 0)), (8, (35, 0, 0)), (14, (0, 0, 0))],
        "abdomen": [(0, (0, 0, 0)), (5, (25, 0, 0)), (14, (0, 0, 0))],
    })
    R.clip(arm, "EN_Spider_Hit", 8, {
        "body": [(0, (0, 0, 0), (0, 0, 0)), (2, (-12, 8, 0), (0, 0.08, 0.05)), (8, (0, 0, 0), (0, 0, 0))],
        "abdomen": [(0, (0, 0, 0)), (2, (15, 0, 0)), (8, (0, 0, 0))],
    })
    R.clip(arm, "EN_Spider_Death", 24, {        # flips over and curls its legs
        "body": [(0, (0, 0, 0), (0, 0, 0)), (8, (0, 0, 120), (0, 0, 0.35)), (24, (0, 0, 180), (0, 0, 0.3))],
        "legs_A": [(0, (0, 0, 0)), (24, (40, 0, 0), (0, 0, -0.2), 0.6)],
        "legs_B": [(0, (0, 0, 0)), (24, (-40, 0, 0), (0, 0, -0.2), 0.6)],
        "abdomen": [(0, (0, 0, 0)), (24, (20, 0, 0))],
    })
    return arm, mesh


@enemy
def Dummy(coll, M):
    """Training dummy: a straw-stuffed burlap sack on a post with a wooden crossbar and a painted target."""
    bones = {"root": ((0, 0, 0), None), "post": ((0, 0, 0), "root"), "body": ((0, 0, 0.75), "post"), "head": ((0, 0, 1.55), "body")}
    P = [(box((0, 0, 0.4), (0.12, 0.12, 0.8), M["wood"]), "post"),
         (box((0, 0, 0.04), (0.6, 0.18, 0.08), M["wood"]), "post"),
         (box((0, 0, 0.04), (0.18, 0.6, 0.08), M["wood"]), "post")]
    P += [(taper(box((0, 0, 1.12), (0.56, 0.4, 0.72), M["burlap"], bevel=0.05), 0.9), "body"),
          (box((0, 0, 1.38), (1.2, 0.12, 0.1), M["wood"]), "body"),                                    # crossbar arms
          (box((0, 0, 0.8), (0.4, 0.32, 0.08), M["straw"]), "body"),                                   # straw poking out
          (box((0, -0.21, 1.15), (0.3, 0.02, 0.3), M["rags"], bevel=0), "body"),                        # target
          (box((0, -0.22, 1.15), (0.14, 0.02, 0.14), M["straw"], bevel=0), "body")]
    for s in (1, -1):
        P.append((box((0.62 * s, 0, 1.38), (0.08, 0.2, 0.2), M["straw"]), "body"))
    P += [(B.prim('ico', (0, 0, 1.72), (0.38, 0.36, 0.38), m=M["burlap"], sub=1), "head"),
          (box((0, 0, 1.94), (0.2, 0.2, 0.08), M["straw"]), "head")]
    for s in (1, -1):
        P.append((box((0.08 * s, -0.19, 1.74), (0.06, 0.02, 0.06), M["dark"], bevel=0), "head"))      # stitched eyes
    arm, mesh, counts = assemble("EN_Dummy", coll, bones, P)
    R.clip(arm, "EN_Dummy_Idle", 24, {"body": [(0, (0, 0, 0)), (24, (0, 0, 0))]}, loop=True)
    R.clip(arm, "EN_Dummy_Hit", 20, {              # wobbles back and forth on its post
        "body": [(0, (0, 0, 0)), (3, (-14, 0, 4)), (8, (9, 0, -3)), (13, (-5, 0, 2)), (20, (0, 0, 0))],
        "head": [(0, (0, 0, 0)), (4, (-10, 6, 0)), (10, (6, -4, 0)), (20, (0, 0, 0))],
    })
    return arm, mesh
