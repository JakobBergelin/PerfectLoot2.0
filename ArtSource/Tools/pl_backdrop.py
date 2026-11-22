"""Perfect Loot backdrop pieces for Blender (5.x): large, low-detail scenery that stands in the abyss far outside a level
(LevelBackdrop in Unity scatters them around the background ring and sinks them below the ground).

Shapes follow the biome kit: layered rock drums with light stone ledges, crooked spires, broken ruin towers, an arch
fragment and floating islands. Every piece has its pivot at its top center (Unity places a piece by how far its top is
below the ground, so none ever rises over the level). Material slots are the kit's (pl_wfc_kit.SLOTS), so Unity gives
them the biome's materials.

Usage inside Blender:
    import sys; sys.path.append(r"<repo>/ArtSource/Tools")
    import importlib, pl_backdrop as BD; importlib.reload(BD)
    objects = BD.build_backdrop(collection, seed=41)
    BD.export_backdrop(objects, out_dir)    # FBX per piece and slots.txt for BiomeKitBuilder
"""
import math, os, random
import pl_wfc_tiles as T
import pl_wfc_kit as K


def column(b, rings, M, seed, sides=6, side="Cliff", top="WallTop", wobble=0.28, turn=0.18):
    """A chunky drum stack around a vertical axis. rings: (h, radius) or (h, radius, du, dv) or (h, radius, du, dv,
    top_jitter), bottom to top; each vertex keeps its own radius factor all the way up, so the faces stay broad, and each
    new height turns a little (turn, radians), so the sides break into jagged facets. Two rings at the same height make a
    flat step: an inset (radius shrinking) reads as a light ledge (WallTop) seen from above."""
    rnd = random.Random(seed)
    jit = [1.0 + rnd.uniform(-wobble, wobble) for _ in range(sides)]
    twist = rnd.uniform(0.0, 2.0 * math.pi)
    loops = []
    for i, ring in enumerate(rings):
        h, r = ring[0], ring[1]
        if i > 0 and abs(h - rings[i - 1][0]) > 1e-4: twist += rnd.uniform(-turn, turn)
        du, dv = (ring[2], ring[3]) if len(ring) > 3 else (0.0, 0.0)
        hj = ring[4] if len(ring) > 4 else 0.0
        loops.append([b.vert(du + math.cos(twist + k * 2 * math.pi / sides) * r * jit[k],
                             dv + math.sin(twist + k * 2 * math.pi / sides) * r * jit[k],
                             h + (rnd.uniform(-hj, hj) if hj else 0.0)) for k in range(sides)])
    b.face(loops[0], M[side])                       # Bottom cap, facing down
    for i in range(len(loops) - 1):
        a, c = loops[i], loops[i + 1]
        flat = abs(rings[i][0] - rings[i + 1][0]) < 1e-4 and rings[i + 1][1] < rings[i][1]
        for k in range(sides):
            b.face([a[k], a[(k + 1) % sides], c[(k + 1) % sides], c[k]][::-1], M[top] if flat else M[side])
    b.face(loops[-1][::-1], M[top])                 # Top cap, facing up


def spire_a(b, M, seed):
    """Tall layered rock drum, tapering to a point (like the kit's pillars, much bigger)."""
    column(b, [(-36, 4.6), (-20, 5.0), (-20, 4.0), (-10, 4.4), (-10, 3.2), (-3, 3.5), (-3, 2.0), (0, 0.8)], M, seed, sides=6)


def spire_b(b, M, seed):
    """Crooked, leaning spire."""
    column(b, [(-30, 3.6), (-18, 3.2, 0.8, 0.3), (-18, 2.6, 0.8, 0.3), (-8, 2.2, 1.6, 0.9), (-8, 1.6, 1.6, 0.9),
               (0, 0.5, 2.4, 1.2)], M, seed, sides=5)


def pillar(b, M, seed):
    """Flat-topped rock pillar with a lip, rising out of the abyss."""
    column(b, [(-28, 3.0), (-2, 3.3), (-2, 3.8), (0, 3.6)], M, seed, sides=7)


def tower(b, M, seed):
    """Broken ruin tower: dark masonry with a stone course and a jagged top."""
    column(b, [(-24, 4.0), (-6, 4.0), (-6, 4.5), (-5.2, 4.5), (-5.2, 4.0), (0, 4.0, 0, 0, 1.4)], M, seed, sides=8,
           side="Spire", wobble=0.05, turn=0.04)


def arch(b, M, seed):
    """Arch fragment: two masonry pillars, the lintel broken off short of the second."""
    column(b, [(-20, 1.9, -4, 0), (-1.5, 1.9, -4, 0), (-1.5, 2.3, -4, 0), (0, 2.3, -4, 0)], M, seed, sides=4, side="Spire", wobble=0.03, turn=0.0)
    column(b, [(-20, 1.9, 4, 0), (-4.5, 1.9, 4, 0), (-4.5, 2.3, 4, 0), (-3.0, 2.3, 4, 0, 0.6)], M, seed + 1, sides=4,
           side="Spire", wobble=0.03, turn=0.0)
    K.box(b, -1.0, 0.0, -1.5, 3.4, 1.1, 1.5, M["Spire"])


def island_a(b, M, seed):
    """Floating island: a jagged rock cone with a flat top and a broken pillar on it."""
    column(b, [(-10, 0.6), (-6, 3.2), (-2.5, 5.6), (-1.0, 6.6), (0, 6.4)], M, seed, sides=7, top="Floor")
    column(b, [(0, 1.0, 2.5, 1.0), (3.0, 0.9, 2.5, 1.0, 0.7)], M, seed + 3, sides=5, side="Spire")


def island_b(b, M, seed):
    """Small floating island with a rock shard on it."""
    column(b, [(-7, 0.5), (-4, 2.6), (-1, 4.2), (0, 4.0)], M, seed, sides=6, top="Floor")
    K.prism(b, -1.0, 0.5, 0.0, 1.4, 3.0, M["Cliff"], sides=5)


PIECES = [("SpireA", spire_a), ("SpireB", spire_b), ("Pillar", pillar), ("Tower", tower), ("Arch", arch),
          ("IslandA", island_a), ("IslandB", island_b)]


def build_backdrop(coll, seed=41):
    """Every backdrop piece in coll, side by side along X. Returns {name: object}."""
    M = K.materials()
    objects = {}
    for i, (name, fn) in enumerate(PIECES):
        b = T.Builder()
        fn(b, M, seed * 31 + i * 7)
        o = b.to_object("Backdrop_" + name, coll)
        o.location.x = i * 16.0
        objects[o.name] = o
    return objects


def export_backdrop(objects, outdir):
    """FBX per piece (mirrored in X on the way out, like the tiles) and slots.txt for BiomeKitBuilder."""
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
