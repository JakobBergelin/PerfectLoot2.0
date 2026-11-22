"""Perfect Loot ambient creatures for Blender (5.x): small, chunky animals that live in a level as decoration (Unity's
AmbientLifeSet: darters, flyers and swimmers). Each model is a few faceted lumps; pivot at the feet (a fish's at the
water line), facing +Z in Unity (Blender -Y). Material slots are named by part, and BiomeKitBuilder gives them colors.

Usage inside Blender:
    import sys; sys.path.append(r"<repo>/ArtSource/Tools")
    import importlib, pl_critters as C; importlib.reload(C)
    objects = C.build_critters(collection)
    C.export_critters(objects, out_dir)     # FBX per creature and slots.txt for BiomeKitBuilder
"""
import math, os
import bmesh, bpy
from mathutils import Matrix, Vector, noise
import pl_wfc_tiles as T

SLOTS = {"Fur": "#A8683E", "FurLight": "#E8D2B0", "FurGrey": "#8C7A68", "Feather": "#5A6EA8", "FeatherLight": "#E8C8A0",
         "Beak": "#D8A040", "Eye": "#151018", "Fish": "#1E4E62"}


def materials():
    out = {}
    for name, hex_ in SLOTS.items():
        m = bpy.data.materials.get("Critter" + name) or bpy.data.materials.new("Critter" + name)
        h = hex_.lstrip('#')
        m.diffuse_color = (*(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)), 1)
        out[name] = m
    return out


def lump(b, u, v, h, rx, ry, rz, m, seed=0, dent=0.12, subdivisions=1):
    """An ellipsoid lump centered at (u, v, h) with radii rx (east), ry (forward), rz (up), dented a little by noise."""
    center = Vector((u, -v, h))
    verts = bmesh.ops.create_icosphere(b.bm, subdivisions=subdivisions, radius=1.0,
                                       matrix=Matrix.Translation(center) @ Matrix.Diagonal((rx, ry, rz)).to_4x4())["verts"]
    off = Vector((seed * 1.3, seed * 0.7, 1.9))
    for vt in verts:
        d = vt.co - center
        vt.co = center + d * (1.0 + dent * noise.noise(Vector((d.x / rx, d.y / ry, d.z / rz)) * 1.5 + off))
    idx = b.mat_index(m)
    for f in {f for vt in verts for f in vt.link_faces}: f.material_index = idx


def eyes(b, u, v, h, M):
    for s in (-1, 1): lump(b, u * s, v, h, 0.018, 0.018, 0.018, M["Eye"], dent=0.0)


def squirrel(b, M):
    lump(b, 0, 0.0, 0.15, 0.1, 0.15, 0.11, M["Fur"], 1)
    lump(b, 0, 0.04, 0.11, 0.07, 0.1, 0.07, M["FurLight"], 2)            # Belly
    lump(b, 0, 0.15, 0.25, 0.085, 0.09, 0.08, M["Fur"], 3)                # Head
    for s in (-1, 1): lump(b, 0.045 * s, 0.13, 0.33, 0.02, 0.02, 0.04, M["Fur"], 4)     # Ears
    eyes(b, 0.055, 0.21, 0.27, M)
    lump(b, 0, -0.17, 0.25, 0.08, 0.09, 0.17, M["Fur"], 5)               # Bushy tail, curling up
    lump(b, 0, -0.11, 0.42, 0.08, 0.09, 0.08, M["FurLight"], 6)


def rabbit(b, M):
    lump(b, 0, 0.0, 0.14, 0.12, 0.17, 0.13, M["FurGrey"], 1)
    lump(b, 0, 0.14, 0.24, 0.09, 0.09, 0.085, M["FurGrey"], 2)            # Head
    for s in (-1, 1): lump(b, 0.04 * s, 0.1, 0.4, 0.025, 0.03, 0.11, M["FurGrey"], 3)  # Long ears
    eyes(b, 0.06, 0.2, 0.26, M)
    lump(b, 0, -0.17, 0.17, 0.05, 0.05, 0.05, M["FurLight"], 4)          # Cotton tail


def bird(b, M):
    lump(b, 0, 0.0, 0.1, 0.07, 0.11, 0.07, M["Feather"], 1)
    lump(b, 0, 0.03, 0.08, 0.055, 0.08, 0.05, M["FeatherLight"], 2)        # Breast
    lump(b, 0, 0.09, 0.16, 0.055, 0.055, 0.055, M["Feather"], 3)           # Head
    eyes(b, 0.035, 0.13, 0.18, M)
    T_beak = [(-0.015, 0.13), (0.015, 0.13), (0.0, 0.19)]
    faces = b.surface([T_beak], 0.155, M["Beak"])
    b.skirt(faces, 0.145, M["Beak"])
    for s in (-1, 1): lump(b, 0.07 * s, -0.01, 0.12, 0.02, 0.08, 0.04, M["Feather"], 4)    # Wings
    lump(b, 0, -0.13, 0.12, 0.04, 0.06, 0.015, M["Feather"], 5)            # Tail


def fish(b, M):
    lump(b, 0, 0.0, 0.0, 0.06, 0.22, 0.02, M["Fish"], 1)
    fin = [(-0.07, -0.3), (0.07, -0.3), (0.0, -0.18)]
    b.surface([fin], 0.0, M["Fish"])


CRITTERS = [("Squirrel", squirrel), ("Rabbit", rabbit), ("Bird", bird), ("Fish", fish)]


def build_critters(coll, prefix="Critter_"):
    """Every creature in coll, side by side along X. Returns {name: object}."""
    M = materials()
    objects = {}
    for i, (name, fn) in enumerate(CRITTERS):
        b = T.Builder()
        fn(b, M)
        o = b.to_object(prefix + name, coll)
        for p in o.data.polygons: p.use_smooth = True     # Round little bodies
        o.location.x = i * 1.0
        objects[o.name] = o
    return objects


def export_critters(objects, outdir):
    """FBX per creature (mirrored in X on the way out, like the tiles) and slots.txt for BiomeKitBuilder."""
    os.makedirs(outdir, exist_ok=True)
    slots = ["# object|material slots in submesh order"]
    for name, o in sorted(objects.items()):
        x = o.location.x
        o.location.x = 0.0
        try:
            T.export_fbx(o, os.path.join(outdir, name + ".fbx"))
        finally:
            o.location.x = x
        slots.append(name + "|" + ",".join(m.name for m in o.data.materials))
    with open(os.path.join(outdir, "slots.txt"), "w") as f: f.write("\n".join(slots))
