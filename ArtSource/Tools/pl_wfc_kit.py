"""Perfect Loot WFC biome kit for Blender (5.x): the tiles of a biome tile set, described by connections.

A tile names a corner connection for each corner (SW, SE, NE, NW; +X east, +Z north in Unity) and an edge connection for
each edge (S, E, N, W), like WfcTileSet. Corner connections have a kind (floor, path, overgrowth, rock, drop, liquid,
bridge) and a height: the high ground is the same kinds HIGH m up. A tile's model sits at its lowest corner's height
(Unity raises it), so a cliff or ramp starts at its low side and a high-ground tile reuses the low tile's model.

Each connection's region inside a tile comes from bilinear corner weights plus noise that fades to zero at the border
(pl_wfc_tiles.Field, generalised), so any two tiles whose shared corners match line up in every rotation. Where two
regions meet, the higher side gets a face down to the lower one: a cliff between heights, a chasm wall into a drop, a bank
into a pool, a beam under a bridge deck.

Usage inside Blender:
    import sys; sys.path.append(r"<repo>/ArtSource/Tools")
    import importlib, pl_wfc_kit as K; importlib.reload(K)
    objects = K.build_kit(K.STYLES["AbyssalRuins"], collection)
    K.export_kit(objects, out_dir, K.STYLES["AbyssalRuins"])   # FBX per model, slots.txt and kit.json for Unity
"""
import bpy, bmesh, json, math, os, random
from mathutils import Matrix, Vector, noise
import pl_wfc_tiles as T

TILE, N, STEP = T.TILE, T.N, T.STEP
# Waist high: clearly a ledge (units step up at most 0.4 m) without hiding much floor or making ramps steep.
# Abyssal Ruins' exported tiles are still 2.5 m until the kit is exported again.
HIGH = 1.25

# name, kind, height, Unity terrain, Id, the connection one step up. Ids 0-2 are the old Floor / Wall / Water.
CONNECTIONS = [
    ("Floor", "floor", 0.0, "Walkable", 0, "FloorHigh"),
    ("Rock", "rock", 0.0, "NotWalkable", 1, None),
    ("Liquid", "liquid", 0.0, "Water", 2, None),
    ("Drop", "drop", 0.0, "NotWalkable", 3, None),
    ("Path", "path", 0.0, "Walkable", 4, "PathHigh"),
    ("Overgrowth", "overgrowth", 0.0, "Walkable", 5, "OvergrowthHigh"),
    ("Bridge", "bridge", 0.0, "Walkable", 6, None),
    ("FloorHigh", "floor", HIGH, "Walkable", 7, None),
    ("PathHigh", "path", HIGH, "Walkable", 8, None),
    ("OvergrowthHigh", "overgrowth", HIGH, "Walkable", 9, None),
]
CONN = {c[0]: {"kind": c[1], "height": c[2], "terrain": c[3], "id": c[4], "higher": c[5]} for c in CONNECTIONS}
# name, Id, units can walk across. A narrow ramp is a Walk Through tile with a straight cliff's edges; a wide ramp's open
# side is a Slope edge, which only another ramp's open side matches.
EDGES = [("Plain", 0, True), ("Cliff", 1, False), ("Slope", 2, True)]
EDGE = {e[0]: e[1] for e in EDGES}
FLOORLIKE = ("floor", "path", "overgrowth")
RAMP_HALF = 1.4     # Half the width of a narrow ramp cut through a cliff
# Where each kind of ramp tile's slope runs across the tile (u from, to): a narrow ramp has walls on both sides; a wide
# ramp is a RampSide with its open (Slope) side against a mirrored RampSide or a RampMid, so neighbouring ramps merge
OPEN = TILE / 2 + 0.01
RAMP_SPANS = {"ramp": (-RAMP_HALF, RAMP_HALF), "ramp_side": (-RAMP_HALF, OPEN), "ramp_mid": (-OPEN, OPEN)}
RAMP_WEIGHT = {"Ramp": 0.15, "RampSide": 0.35, "RampMid": 0.15}   # Factors on a straight cliff's weight
LIP = 0.35 / (TILE / 2)     # Width of the stone lip along cliff tops and chasm rims (in Field.g units: meters / half a tile)
# How strongly a connection claims a tile's area against the others (a bridge deck is narrower than half a tile)
BIAS = {"Bridge": 0.72}

# Per biome: shapes only (colors are set in Unity). depth: how far a drop goes down; slab: floor slab size in grid cells.
STYLES = {
    "AbyssalRuins": dict(name="AbyssalRuins", seed=41, wall_h=2.4, wall_jitter=0.7, depth=12.0, slab=5, cliff_jitter=0.16,
                         accent="runes", glow=0.9, spires=True, liquid_factor=0.5, drop_factor=1.2),
    "EmberDepths": dict(name="EmberDepths", seed=7, wall_h=3.2, wall_jitter=0.55, depth=10.0, slab=5, cliff_jitter=0.22,
                        accent="magma", glow=1.3, spires=True, liquid_factor=0.75, drop_factor=1.0),
    "DrownedMire": dict(name="DrownedMire", seed=19, wall_h=2.3, wall_jitter=0.4, depth=8.0, slab=4, cliff_jitter=0.2,
                        accent="mushrooms", glow=0.9, spires=True, liquid_factor=1.0, drop_factor=0.6),
    "FrozenHollow": dict(name="FrozenHollow", seed=29, wall_h=3.0, wall_jitter=0.6, depth=10.0, slab=4, cliff_jitter=0.12,
                         accent="crystals", glow=0.0, spires=True, liquid_factor=0.6, drop_factor=1.0),
    # A bright forest: grass and dirt instead of slabs, tree walls instead of rock, low water crossed by rope bridges, no
    # drop and no high ground (the optional keys below; a style without them is built as before)
    "BrightwaterHollow": dict(name="BrightwaterHollow", seed=53, wall_h=3.0, wall_jitter=0.0, depth=8.0, slab=5,
                              cliff_jitter=0.0, accent="flowers", glow=0.0, spires=False, liquid_factor=1.0, drop_factor=0.0,
                              connections=["Floor", "Path", "Overgrowth", "Rock", "Liquid", "Bridge"],
                              prefix="BH_", ground="grass", wall="trees", bridge="rope", water=(-0.45, -1.0),
                              weights={"Floor": 2.5, "Path": 0.15, "Overgrowth": 1.2, "Rock": 4.0, "Bridge": 0.05}),
}

# Set pieces: multi-cell models made from the kit's own cells (build_piece), as corner maps, north row first. Letters:
# F floor, P path, O overgrowth, R rock (trees), L liquid, B bridge. A river's corners on a piece's side are water, so
# the solver carries it on.
PIECE_LETTERS = {"F": "Floor", "P": "Path", "O": "Overgrowth", "R": "Rock", "L": "Liquid", "B": "Bridge"}
STYLES["BrightwaterHollow"]["pieces"] = {
    # A river across the level with a rope bridge in the middle and short dirt approaches; it steps up a row on one side,
    # and the solver doesn't carry water on by itself, so the piece spans the level (turned around, it bends the other way)
    "River": ["FFFFFFFPPPFFFFFFF",
              "LLLLLFFPPPFFFFFFF",
              "LLLLLLLLBLLLLLLLL",
              "FFFFLLLLBLLLLLLLL",
              "FFFFFFFPPPFFFFFFF"],
    # The boss's clearing: open grass ringed by forest, a wide way in from the south
    "BossArena": ["RRRRRRRR",
                  "RFFFFFFR",
                  "RFFFFFFR",
                  "RFFFFFFR",
                  "RFFFFFFR",
                  "RFFFFFFR",
                  "RRFFFFRR"],
    # Water the seed scatters (landmarks)
    "Pond": ["FFFF",
             "FLLF",
             "FLLF",
             "FFFF"],
    "Lake": ["FFFFFF",
             "FLLLLF",
             "FLLLLF",
             "FFLLFF",
             "FFFFFF"],
}

# Material slot names: Unity puts the biome's material on each (BiomeKitBuilder)
SLOTS = ["Floor", "FloorAlt", "Joint", "Path", "Overgrowth", "Accent", "Cliff", "WallSide", "WallTop", "WallDark",
         "Bank", "Chasm", "Liquid", "Glow", "Bridge", "Spire", "Flower", "Trunk", "Birch", "Canopy", "CanopyLight",
         "CanopyWarm", "Bush", "Undergrowth", "Rope"]
SMOOTH = ("Canopy", "CanopyLight", "CanopyWarm", "Bush")    # Round lumps: smooth shading, so the comic shader's light split curves
WEIGHT = {"Floor": 4.0, "Path": 1.6, "Overgrowth": 1.6, "Rock": 1.0, "Drop": 1.0, "Liquid": 0.5, "Bridge": 0.25,
          "FloorHigh": 1.8, "PathHigh": 1.0, "OvergrowthHigh": 1.0}
# Placeholder colors for the renders (Unity sets the real ones); a style's "colors" override these
COLORS = {"Floor": "#4A4656", "FloorAlt": "#454152", "Joint": "#24212C", "Path": "#6A6474", "Overgrowth": "#3E5A58",
          "Accent": "#3FC4B4", "Cliff": "#5C5870", "WallSide": "#565470", "WallTop": "#8C88A8", "WallDark": "#3A3848",
          "Bank": "#2A2834", "Chasm": "#123E44", "Liquid": "#1C4A54", "Glow": "#2A6A64", "Bridge": "#6B4E3A",
          "Spire": "#3A3A4C", "Flower": "#F2EEDC", "Trunk": "#6A4A36", "Birch": "#E6E0CE", "Canopy": "#3F8A6A",
          "CanopyLight": "#6AB07C", "CanopyWarm": "#B4C060", "Bush": "#3A7A58", "Undergrowth": "#2E5A44", "Rope": "#D8C08A"}
STYLES["BrightwaterHollow"]["colors"] = {
    "Floor": "#5A9A5E", "FloorAlt": "#47804E", "Path": "#B08A5E", "Overgrowth": "#64A46C", "Accent": "#E06A9A",
    "Cliff": "#9A968C", "Bank": "#6A4E3A", "Liquid": "#4AB0C8", "Bridge": "#A0703F"}


def materials(S=None):
    """Placeholder materials named like the slots (colors only help reading the renders)."""
    colors = dict(COLORS, **(S.get("colors", {}) if S else {}))
    out = {}
    for name in SLOTS:
        m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        h = colors[name].lstrip('#')
        m.diffuse_color = (*(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)), 1)
        out[name] = m
    return out


# ---------- field ----------

def _weights(u, v):
    s = u / TILE + 0.5; t = v / TILE + 0.5
    return [(1 - s) * (1 - t), s * (1 - t), s * t, (1 - s) * t]


class Field:
    """Which connection owns each point of a tile with these 4 corner connections."""

    def __init__(self, corners, seed):
        self.corners = corners
        self.present = sorted(set(corners))
        self.off = {c: Vector((seed * 3.7 + i * 11.3, seed * 1.9 + i * 5.1, i * 2.3)) for i, c in enumerate(self.present)}
        self.cache = {}

    def score(self, c, u, v):
        w = _weights(u, v)
        f = sum(w[k] for k in range(4) if self.corners[k] == c) * BIAS.get(c, 1.0)
        n = noise.noise(Vector((u * 0.55, v * 0.55, 0)) + self.off[c]) * 0.22 * T._falloff(u, v)
        return f + n

    def g(self, c, u, v):
        """> 0 inside c's region; 0 on its outline."""
        key = (c, round(u, 4), round(v, 4))
        if key in self.cache: return self.cache[key]
        if c not in self.present: val = -1.0
        elif len(self.present) == 1: val = 1.0
        else: val = self.score(c, u, v) - max(self.score(o, u, v) for o in self.present if o != c)
        self.cache[key] = val
        return val

    def winner(self, u, v):
        return max(self.present, key=lambda c: self.g(c, u, v))


# ---------- mesh helpers ----------

def outline_edges(b, faces):
    """Edges on the outline of faces, not on the tile border: (a, b) with the faces on the left, seen from above."""
    fs = set(faces)
    out = []
    for f in faces:
        for loop in f.loops:
            e = loop.edge
            if sum(1 for lf in e.link_faces if lf in fs) != 1: continue
            a, c = loop.vert, loop.link_loop_next.vert
            if T._on_border(a, c): continue
            out.append((a, c))
    return out


def uv_of(v):
    return v.co.x, -v.co.y      # Unity (u, v) of a Blender vertex


def across(F, a, c, eps=0.06):
    """The connection on the other side of an outline edge a-c (faces to its left when walking a to c, CCW from above)."""
    (ua, va), (uc, vc) = uv_of(a), uv_of(c)
    mu, mv = (ua + uc) / 2, (va + vc) / 2
    du, dv = uc - ua, vc - va
    length = math.hypot(du, dv) or 1.0
    # Blender verts are (u, -v): the stored loop runs clockwise in (u, v), so the outside is to the left of a->c
    nu, nv = -dv / length, du / length
    return F.winner(mu + nu * eps, mv + nv * eps)


def wall(b, a, c, top, bottom, m, jitter=None):
    """A vertical face down from the outline edge a-c (at height top) to bottom, facing out of the region."""
    (ua, va), (uc, vc) = uv_of(a), uv_of(c)
    def at(u, v, h):
        du, dv = jitter(u, v) if jitter and h < top - 1e-4 else (0.0, 0.0)
        return b.vert(u + du, v + dv, h)
    ta, tc = b.vert(ua, va, top), b.vert(uc, vc, top)
    return b.face([tc, ta, at(ua, va, bottom), at(uc, vc, bottom)], m)


def region_faces(b, fn, height, m, flip=False):
    return b.surface(T.region(fn, merge=False), height, m, flip=flip)


def scatter(R, fn, count, margin=0.4):
    """Up to count random points inside fn's region, away from the tile border."""
    out = []
    for _ in range(count * 6):
        u = R.uniform(-TILE / 2 + margin, TILE / 2 - margin); v = R.uniform(-TILE / 2 + margin, TILE / 2 - margin)
        if fn(u, v) > 0.08: out.append((u, v))
        if len(out) >= count: break
    return out


def prism(b, u, v, h, radius, height, m, sides=5, twist=0.0, taper=0.35):
    """A jagged rock / crystal: a tapered prism standing on h."""
    base = [b.vert(u + math.cos(twist + k * 2 * math.pi / sides) * radius, v + math.sin(twist + k * 2 * math.pi / sides) * radius, h)
            for k in range(sides)]
    top = [b.vert(u + math.cos(twist + 0.3 + k * 2 * math.pi / sides) * radius * taper,
                  v + math.sin(twist + 0.3 + k * 2 * math.pi / sides) * radius * taper, h + height) for k in range(sides)]
    for k in range(sides):
        b.face([base[k], base[(k + 1) % sides], top[(k + 1) % sides], top[k]][::-1], m)
    b.face(top[::-1], m)       # (u, v) rings turn clockwise once stored as Blender (u, -v): reversed to face up


def box(b, u, v, h, su, sv, sh, m):
    p = [(u - su, v - sv), (u + su, v - sv), (u + su, v + sv), (u - su, v + sv)]
    faces = b.surface([p], h + sh, m)
    b.skirt(faces, h, m)


def blob(b, u, v, h, radius, squash, m, seed, lump=0.2):
    """A chunky low-poly lump (canopy, bush): an icosphere centered at (u, v, h), squashed and dented by noise."""
    center = Vector((u, -v, h))
    scale = Matrix.Diagonal((radius, radius, radius * squash)).to_4x4()
    verts = bmesh.ops.create_icosphere(b.bm, subdivisions=1, radius=1.0, matrix=Matrix.Translation(center) @ scale)["verts"]
    off = Vector((seed * 1.7, seed * 0.9, 3.1))
    for vt in verts:
        d = vt.co - center
        vt.co = center + d * (1.0 + lump * noise.noise(d * (1.6 / radius) + off))
    idx = b.mat_index(m)
    for f in {f for vt in verts for f in vt.link_faces}: f.material_index = idx


def flower(b, u, v, h, size, m):
    """A flat five-petal flower a little above the ground."""
    pts = [(u + math.cos(a) * size * (1.0 if k % 2 == 0 else 0.45), v + math.sin(a) * size * (1.0 if k % 2 == 0 else 0.45))
           for k, a in ((k, k * math.pi / 5 + 0.3) for k in range(10))]
    b.surface([pts], h, m)


def flat_region(fn):
    """fn's region as polygons for a flat surface: one quad when it covers the whole tile, else a polygon per grid cell
    (merged runs would leave T-junctions, and skirts along them)."""
    cells = T.region(fn, merge=False)
    full = len(cells) == N * N and all(len(p) == 4 for p in cells)
    return [[(-TILE / 2, -TILE / 2), (TILE / 2, -TILE / 2), (TILE / 2, TILE / 2), (-TILE / 2, TILE / 2)]] if full else cells


def grass_surface(b, kind, fn, h, S, M, R, slope=None, look=0):
    """Natural ground (the "grass" style): grass, a dirt path a little lower, or a flower meadow; flat and calm, with sparse
    tufts, pebbles and flowers, and a short side down to the bed so no gap shows where it meets lower ground. Returns the
    bed: the region's outline for the faces down to water, removed afterwards (build_model's helpers)."""
    hf = (lambda u, v: h + slope(u, v)) if slope else (lambda u, v: h)
    bed = region_faces(b, fn, lambda u, v: hf(u, v) - 0.06, M["FloorAlt"])
    if kind == "path":
        top = b.surface(flat_region(fn), lambda u, v: hf(u, v) - 0.02, M["Path"])
        b.skirt(top, lambda u, v: hf(u, v) - 0.06, M["FloorAlt"], contour_only=True)
        for (u, v) in scatter(R, fn, 3, margin=0.5):
            prism(b, u, v, hf(u, v) - 0.03, R.uniform(0.06, 0.12), R.uniform(0.05, 0.1), M["Cliff"], sides=5, twist=R.uniform(0, 3), taper=0.6)
        return bed
    meadow = kind == "overgrowth"
    top = b.surface(flat_region(fn), lambda u, v: hf(u, v) + 0.03, M["Overgrowth"] if meadow else M["Floor"])
    b.skirt(top, lambda u, v: hf(u, v) - 0.06, M["FloorAlt"], contour_only=True)
    # Tufts: small tapered clumps, a few per tile
    for (u, v) in scatter(R, fn, 6 if meadow else (5 if look == 1 else 2), margin=0.4):
        for k in range(3):
            prism(b, u + R.uniform(-0.1, 0.1), v + R.uniform(-0.1, 0.1), hf(u, v), R.uniform(0.05, 0.08),
                  R.uniform(0.18, 0.32), M["FloorAlt"], sides=3, twist=R.uniform(0, 3), taper=0.1)
    # Flowers: a meadow is full of them, plain grass gets a few on one look
    count = 9 if meadow else (4 if look == 2 else 0)
    for (u, v) in scatter(R, fn, count, margin=0.4):
        flower(b, u, v, hf(u, v) + 0.1 + R.uniform(0, 0.06), R.uniform(0.14, 0.2), M["Accent"] if R.random() < 0.45 else M["Flower"])
    return bed


def stone(b, u, v, h, size, M, R):
    """A faceted stone sunk a little into the ground."""
    blob(b, u, v, h + size * 0.2, size, R.uniform(0.5, 0.75), M["Cliff"], R.randint(0, 999), lump=0.3)


def stone_size(R, largest=2):
    """Mostly small stones, some medium, a few big ones (size class 0, 1 or 2 at most)."""
    roll = R.random()
    if roll < 0.55 or largest == 0: return R.uniform(0.1, 0.18)
    if roll < 0.88 or largest == 1: return R.uniform(0.22, 0.36)
    return R.uniform(0.45, 0.75)


def tree(b, u, v, h, scale, M, R):
    """One chunky tree at (u, v), scale times a normal one: a broadleaf (a crown of one to four lumps in a mix of greens),
    a tall narrow one (lumps stacked up) or a birch (a pale trunk under a lighter crown)."""
    roll = R.random()
    kind = "birch" if roll < 0.2 else ("tall" if roll < 0.3 else "broad")
    green = lambda: M[R.choice(["Canopy"] * 5 + ["CanopyLight"] * 4 + ["CanopyWarm"])]
    birch = kind == "birch"
    height = scale * (R.uniform(2.2, 3.2) if kind != "broad" else R.uniform(1.4, 2.6))
    prism(b, u, v, h, scale ** 0.5 * (R.uniform(0.15, 0.2) if birch else R.uniform(0.22, 0.36)), height + 0.6 * scale,
          M["Birch"] if birch else M["Trunk"], sides=6, twist=R.uniform(0, 3), taper=0.7)
    main = M["CanopyLight"] if birch else green()
    if kind == "tall":
        radius = scale * R.uniform(0.8, 1.05)
        for k in range(R.randint(2, 3)):
            blob(b, u + R.uniform(-0.15, 0.15), v + R.uniform(-0.15, 0.15), h + height + radius * (0.4 + 0.75 * k),
                 radius * (1.0 - 0.18 * k), 1.15, main, R.randint(0, 999))
        return
    radius = scale * (R.uniform(0.8, 1.15) if birch else R.uniform(1.0, 1.7))
    blob(b, u, v, h + height + radius * 0.5, radius, R.uniform(0.7, 0.95), main, R.randint(0, 999))
    for k in range(R.randint(1, 2) if birch else R.randint(1, 3)):
        a = R.uniform(0, 2 * math.pi); d = radius * R.uniform(0.45, 0.85)
        blob(b, u + math.cos(a) * d, v + math.sin(a) * d, h + height + radius * R.uniform(0.3, 1.0),
             radius * R.uniform(0.45, 0.7), 0.8, R.choice([main, M["CanopyLight"]]), R.randint(0, 999))


def trees_surface(b, fn, h, S, M, R, seed):
    """A tree wall (the "trees" style): dark undergrowth, and a slope of growth up from its edge: bushes, stones and small
    trees there, bigger trees deeper in, some overhanging the edge. Returns the outline faces (hidden base)."""
    floor = b.surface(flat_region(fn), h + 0.04, M["Undergrowth"])
    b.skirt(floor, h - 0.06, M["Undergrowth"], contour_only=True)
    edge = lambda u, v: min(fn(u, v), 0.35 - fn(u, v))
    for (u, v) in scatter(R, edge, 7, margin=0.3):
        blob(b, u, v, h + 0.2, R.uniform(0.45, 0.9), R.uniform(0.6, 0.85), M["Bush"], R.randint(0, 999))
    for (u, v) in scatter(R, edge, 3, margin=0.3):
        stone(b, u, v, h, stone_size(R), M, R)
    for (u, v) in scatter(R, lambda u, v: min(fn(u, v) - 0.06, 0.3 - fn(u, v)), 2, margin=0.4):     # Young trees at the edge
        tree(b, u, v, h, R.uniform(0.45, 0.7), M, R)
    n = 3
    for j in range(n):
        for i in range(n):
            u = -TILE / 2 + (i + 0.5) * TILE / n + R.uniform(-0.8, 0.8)
            v = -TILE / 2 + (j + 0.5) * TILE / n + R.uniform(-0.8, 0.8)
            depth = fn(u, v)
            if depth < 0.1 or R.random() < 0.2: continue
            tree(b, u, v, h, min(1.3, 0.55 + depth * 1.5) * R.uniform(0.85, 1.15), M, R)
    return region_faces(b, fn, h - 0.02, M["Undergrowth"])     # Hidden base: only its outline is used, then it's removed


def forest_fringe(b, F, c, fn, h, M, R):
    """Low bushes and stones on walkable ground along a tree wall (about a meter and a half wide), so the forest doesn't
    rise straight up from the grass. Kept low enough to walk over: they have no colliders."""
    def band(u, v):
        if fn(u, v) <= 0: return -1.0
        other = max((o for o in F.present if o != c), key=lambda o: F.score(o, u, v))
        return min(fn(u, v), 0.6 - fn(u, v)) if other == "Rock" else -1.0
    for (u, v) in scatter(R, band, 5, margin=0.3):
        near = 1.0 - min(1.0, fn(u, v) / 0.6)     # Bigger close to the trees
        blob(b, u, v, h + 0.1, R.uniform(0.3, 0.45) + 0.2 * near, R.uniform(0.65, 0.85), M["Bush"], R.randint(0, 999))
    for (u, v) in scatter(R, band, 4, margin=0.3):
        stone(b, u, v, h, stone_size(R, largest=1), M, R)


def rope_rails(b, F, faces, h, M, R):
    """A rope bridge's railing: posts every 1.25 m and two ropes along the deck's edge over the water, and big posts where
    the deck meets the bank."""
    edges = outline_edges(b, faces)
    over_water = [(a, e) for a, e in edges if across(F, a, e) == "Liquid"]
    for a, e in over_water:
        (ua, va), (ue, ve) = uv_of(a), uv_of(e)
        length = math.hypot(ue - ua, ve - va) or 1.0
        nu, nv = -(ve - va) / length * 0.04, (ue - ua) / length * 0.04
        for y in (0.45, 0.85):
            ring = lambda u, v: [b.vert(u - nu, v - nv, h + y - 0.04), b.vert(u + nu, v + nv, h + y - 0.04),
                                 b.vert(u + nu, v + nv, h + y + 0.04), b.vert(u - nu, v - nv, h + y + 0.04)]
            ra, re = ring(ua, va), ring(ue, ve)
            tube = [b.face([ra[k], ra[(k + 1) % 4], re[(k + 1) % 4], re[k]], M["Rope"]) for k in range(4)]
            bmesh.ops.recalc_face_normals(b.bm, faces=[f for f in tube if f])
    # Posts on the 1.25 m grid lines (not on the tile border, where the neighbour has one)
    on_line = lambda x: any(abs(x - T._gp(i)) < 1e-3 for i in (5, 10, 15))
    posts = {a for a, e in over_water} | {e for a, e in over_water}
    for p in posts:
        u, v = uv_of(p)
        if on_line(u) or on_line(v): box(b, u, v, h, 0.08, 0.08, 1.0, M["Bridge"])
    # End posts: where the deck's edge turns from the water to the bank
    sides = {}
    for a, e in edges:
        kind = "water" if across(F, a, e) == "Liquid" else "bank"
        for p in (a, e): sides.setdefault(p, set()).add(kind)
    for p, kinds in sides.items():
        if len(kinds) == 2:
            u, v = uv_of(p)
            box(b, u, v, h, 0.15, 0.15, 1.35, M["Trunk"])
            box(b, u, v, h + 0.95, 0.17, 0.17, 0.12, M["Rope"])


# ---------- surfaces per kind ----------

def floor_surface(b, kind, fn, h, S, M, R, slope=None, look=0):
    """Walkable ground over fn's region at height h (or h + slope(u, v) on a ramp). Returns the outline faces (the bed)."""
    hf = (lambda u, v: h + slope(u, v)) if slope else (lambda u, v: h)
    bed = region_faces(b, fn, lambda u, v: hf(u, v) - 0.03, M["Joint"])
    if kind == "overgrowth":
        top = b.surface(T.region(fn, merge=False), lambda u, v: hf(u, v) + 0.02 + 0.05 * noise.noise(Vector((u * 1.3, v * 1.3, 3.0))), M["Overgrowth"])
        for (u, v) in scatter(R, fn, 5):
            prism(b, u, v, hf(u, v), R.uniform(0.12, 0.22), R.uniform(0.15, 0.4), M["Accent"], sides=4, twist=R.uniform(0, 3))
        return bed
    slab = S["slab"] if kind == "floor" else 2
    mat = M["Path"] if kind == "path" else (M["FloorAlt"] if look % 2 else M["Floor"])
    for sj in range(N // slab):
        for si in range(N // slab):
            cells = [(si * slab + a, sj * slab + c) for c in range(slab) for a in range(slab)]
            polys = T.region(fn, 0.0, cells)
            if not polys: continue
            if len(polys) > 1: polys = T.region(fn, 0.0, cells, merge=False)
            cu = T._gp(si * slab) + slab * STEP / 2; cv = T._gp(sj * slab) + slab * STEP / 2
            top = R.uniform(0.0, 0.035 if kind == "floor" else 0.06)
            tilt = (R.uniform(-0.012, 0.012), R.uniform(-0.012, 0.012))
            height = lambda u, v, cu=cu, cv=cv, top=top, tilt=tilt: hf(u, v) + top + (u - cu) * tilt[0] + (v - cv) * tilt[1]
            gap = 0.07 if kind == "floor" else 0.09
            faces = b.surface(polys, height, mat, scale_about=(cu, cv), k=(slab * STEP - gap) / (slab * STEP))
            b.skirt(faces, lambda u, v: hf(u, v) - 0.03, M["Joint"])     # Follows a ramp's slope
    if kind == "floor" and look == 2:
        accent_feature(b, fn, h, S, M, R)
    return bed


def accent_feature(b, fn, h, S, M, R):
    """A flat glowing feature on a plain floor look (decoration only)."""
    if S["accent"] == "runes":
        for k in range(28):
            a0 = k * 2 * math.pi / 28; a1 = (k + 1) * 2 * math.pi / 28
            ring = [(math.cos(a0) * 1.55, math.sin(a0) * 1.55), (math.cos(a1) * 1.55, math.sin(a1) * 1.55),
                    (math.cos(a1) * 1.7, math.sin(a1) * 1.7), (math.cos(a0) * 1.7, math.sin(a0) * 1.7)]
            b.surface([ring], h + 0.05, M["Accent"])
    else:
        for (u, v) in scatter(R, fn, 6, margin=0.8):
            a = R.uniform(0, math.pi); du, dv = math.cos(a) * 0.45, math.sin(a) * 0.45
            w = 0.06
            b.surface([[(u - du - dv * w, v - dv + du * w), (u + du - dv * w, v + dv + du * w),
                        (u + du + dv * w, v + dv - du * w), (u - du + dv * w, v - dv - du * w)]], h + 0.05, M["Accent"])


def rock_surface(b, fn, h, S, M, seed):
    """A wall: plinth, a body with a jagged top and a darker band. Returns the outline faces (at the base)."""
    top = T._wall_top(seed)
    plinth = b.surface(T.region(fn, -0.10, merge=False), h + 0.45, M["WallDark"])
    b.skirt(plinth, h, M["WallDark"], contour_only=True)
    body = b.surface(T.region(fn, merge=False), lambda u, v: h + S["wall_h"] / T.WALL_H * top(u, v), M["WallTop"])
    b.skirt(body, h, M["WallSide"], contour_only=True)
    band = b.surface(T.region(fn, -0.05, merge=False), h + S["wall_h"] * 0.72, M["WallDark"])
    b.skirt(band, h + S["wall_h"] * 0.66, M["WallDark"], contour_only=True)
    return region_faces(b, fn, h - 0.02, M["WallDark"])   # Hidden base: only its outline is used, then it's removed


def bridge_surface(b, fn, h, M, R, planks_along_v=False):
    """A plank deck at h over the region (a drop or water under it). Returns the outline faces. Planks run along u, or
    along v for a deck that runs along u."""
    deck = region_faces(b, fn, h, M["Bridge"])
    # Planks: thin raised strips across the deck
    for j in range(N // 2):
        cells = [(j * 2 + c, i) if planks_along_v else (i, j * 2 + c) for c in range(2) for i in range(N)]
        polys = T.region(fn, 0.0, cells)
        if not polys: continue
        about = (T._gp(j * 2) + STEP, 0.0) if planks_along_v else (0.0, T._gp(j * 2) + STEP)
        faces = b.surface(polys, h + 0.04 + R.uniform(0, 0.02), M["Bridge"], scale_about=about, k=0.92)
        b.skirt(faces, h, M["Bridge"])
    return deck


# ---------- tiles ----------

def edge_ids(corners):
    """S, E, N, W edge connections: Cliff where the corners are at different heights, Plain where not."""
    hs = [CONN[c]["height"] for c in corners]
    return ["Cliff" if hs[k] != hs[(k + 1) % 4] else "Plain" for k in range(4)]


def build_model(name, corners, S, M, coll, seed=0, kind=None, look=0):
    """One tile model: visual object with collider children (Unity names them by suffix)."""
    F = Field(corners, seed)
    R = random.Random(seed * 7919 + 17)
    b = T.Builder()
    base = min(CONN[c]["height"] for c in corners)
    h = {c: CONN[c]["height"] - base for c in F.present}
    depth = S["depth"]
    g = {c: (lambda u, v, c=c: F.g(c, u, v)) for c in F.present}
    jitter = T._jitter(seed, S["cliff_jitter"])
    # A ramp is a strip cut through a straight cliff, from the low ground (south) up to the high ground, between u = lo and
    # hi. Its sides have walls (a narrow ramp), or a wide ramp's open side runs on into the next ramp tile.
    lo, hi = RAMP_SPANS.get(kind, (0.0, 0.0))
    walls = [u for u in (lo, hi) if abs(u) < TILE / 2]
    ramp = (lambda u, v: (v / TILE + 0.5) * HIGH) if kind in RAMP_SPANS else None
    strip = (lambda u, v: min(u - lo, hi - u)) if ramp else None
    if strip: g = {c: (lambda u, v, c=c: min(F.g(c, u, v), max(lo - u, u - hi))) for c in F.present}

    # Surfaces; each returns faces whose outline gets the faces down to its neighbors
    outlines = {}
    helpers = []
    grass = S.get("ground") == "grass"
    for c in F.present:
        k = CONN[c]["kind"]
        if k in FLOORLIKE:
            outlines[c] = (grass_surface if grass else floor_surface)(b, k, g[c], h[c], S, M, R, look=look)
            if grass: helpers += outlines[c]    # Grass hides its bed under the surface: only its outline is used
        elif k == "rock":
            outlines[c] = trees_surface(b, g[c], h[c], S, M, R, seed) if S.get("wall") == "trees" else rock_surface(b, g[c], h[c], S, M, seed)
            helpers += outlines[c]
        elif k == "bridge": outlines[c] = bridge_surface(b, g[c], h[c], M, R, planks_along_v=kind == "planks_v")
    if S.get("wall") == "trees" and "Rock" in F.present:
        for c in F.present:
            if CONN[c]["kind"] in ("floor", "overgrowth"): forest_fringe(b, F, c, g[c], h[c], M, R)
    if strip: floor_surface(b, "floor", strip, 0.0, S, M, R, slope=ramp)

    # A lip of edge stones along the top of each cliff and chasm rim, so high ground reads as a plateau
    if len(F.present) > 1:
        for c in F.present:
            if CONN[c]["kind"] not in FLOORLIKE: continue
            def above(u, v, c=c):
                o = max((o for o in F.present if o != c), key=lambda o: F.score(o, u, v))
                return CONN[o]["kind"] == "drop" or (CONN[o]["kind"] in FLOORLIKE and h[o] < h[c] - 0.01)
            lip = lambda u, v, c=c: min(g[c](u, v), LIP - g[c](u, v)) if above(u, v) else -1.0
            faces = b.surface(T.region(lip, merge=False), h[c] + 0.16, M["Cliff"])
            b.skirt(faces, h[c] - 0.03, M["Cliff"], contour_only=True)

    # Drop: the bottom of the chasm under the drop and any bridge; spires standing in it for the background
    if "Drop" in F.present:
        under = lambda u, v: max(F.g("Drop", u, v), F.g("Bridge", u, v) if "Bridge" in F.present else -1.0)
        b.surface(T.region(under), -depth, M["Chasm"])
        if kind == "spires":
            for (u, v) in scatter(R, g["Drop"], R.randint(1, 3), margin=0.9):
                prism(b, u, v, -depth, R.uniform(0.5, 0.95), depth + R.uniform(-3.5, 1.5), M["Spire"], sides=R.randint(4, 6), twist=R.uniform(0, 3), taper=0.25)

    # Liquid: bed, surface (UV.x: meters from the shore) and a glow band on the ground beside it
    surface_h, bed_h = S.get("water", (-0.3, -1.1))
    if "Liquid" in F.present:
        hl = h["Liquid"]
        # The water carries on under a bridge deck
        wet = (lambda u, v: max(F.g("Liquid", u, v), F.g("Bridge", u, v))) if "Bridge" in F.present else g["Liquid"]
        b.surface(T.region(wet), hl + bed_h, M["Bank"])
        b.surface(T.region(lambda u, v: wet(u, v) + 0.06), hl + surface_h, M["Liquid"])
        if S["glow"] > 0:
            reach = S["glow"] / (TILE / 2)
            b.surface(T.region(lambda u, v: min(g["Liquid"](u, v) + reach, -g["Liquid"](u, v) + 0.02), merge=False), hl + 0.03, M["Glow"])

    # Faces down from each outline to whatever is across it
    for c, faces in outlines.items():
        k = CONN[c]["kind"]
        for a, e in outline_edges(b, faces):
            if strip and any(abs((uv_of(a)[0] + uv_of(e)[0]) / 2 - w) < 0.06 for w in walls): continue   # The ramp's sides are below
            other = across(F, a, e)
            ko = CONN[other]["kind"]
            ta = a.co.z
            if k in FLOORLIKE and ko in FLOORLIKE and h[c] > h[other] + 0.01:
                wall(b, a, e, ta, h[other] - 0.03, M["Cliff"])     # Straight down: meets the lower floor on the outline
            elif k in FLOORLIKE + ("rock",) and ko == "drop":
                wall(b, a, e, ta, -depth, M["Chasm"], jitter)
            elif (k in FLOORLIKE or (k == "rock" and grass)) and ko == "liquid":
                wall(b, a, e, ta, h[other] + bed_h, M["Bank"])
                if grass and k in FLOORLIKE and R.random() < 0.1:     # Stones on the bank's rim
                    (u, v) = uv_of(a)
                    stone(b, u, v, ta - 0.15, stone_size(R), M, R)
            elif k == "bridge" and ko in ("drop", "liquid"):
                wall(b, a, e, ta, h[c] - 0.4, M["Bridge"])

    for f in helpers:
        if f.is_valid: b.bm.faces.remove(f)

    if S.get("bridge") == "rope" and "Bridge" in F.present and "Liquid" in F.present:
        rope_rails(b, F, outlines["Bridge"], h["Bridge"], M, R)

    # Bridge railing posts along the deck's edge over the drop
    if "Bridge" in F.present and "Drop" in F.present:
        for a, e in outline_edges(b, outlines["Bridge"]):
            if across(F, a, e) != "Drop" or R.random() > 0.3: continue
            (ua, va) = uv_of(a)
            box(b, ua, va, h["Bridge"], 0.09, 0.09, 0.9, M["Bridge"])

    # The ramp's sides: down from the ramp to the low ground beside it, or from the high ground down to the ramp
    if strip:
        for u in walls:
            side = 1 if u > 0 else -1
            for j in range(N):
                v0, v1 = T._gp(j), T._gp(j + 1)
                beside = h[F.winner(u + side * 0.05, (v0 + v1) / 2)]
                r0, r1 = ramp(u, v0), ramp(u, v1)
                low_side = beside < (r0 + r1) / 2
                top = (r0, r1) if low_side else (beside, beside)
                bottom = (beside - 0.03, beside - 0.03) if low_side else (r0, r1)
                vs = [b.vert(u, v0, top[0]), b.vert(u, v1, top[1]), b.vert(u, v1, bottom[1]), b.vert(u, v0, bottom[0])]
                # Facing +u as listed: out of the strip on the low side, into it beside the high ground
                faces_out = low_side == (side > 0)
                b.face(vs if faces_out else vs[::-1], M["Cliff"])

    vis = to_object(b, name, coll, F)
    colliders(name, F, g, h, vis, coll, S, strip, ramp)
    return vis


def to_object(b, name, coll, F):
    """The mesh with faces sorted by material, flat shaded, with UVs the biome shaders read."""
    b.bm.verts.index_update()
    verts = [v.co.copy() for v in b.bm.verts]
    faces = sorted(([v.index for v in f.verts], f.material_index) for f in b.bm.faces if f.is_valid)
    faces.sort(key=lambda f: f[1])
    b.bm.free()
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], [f[0] for f in faces])
    slot_names = [m.name for m in b.mats]
    for p, (_, m) in zip(me.polygons, faces):
        p.material_index = m
        p.use_smooth = m < len(slot_names) and slot_names[m] in SMOOTH
    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        slot = slot_names[poly.material_index] if poly.material_index < len(slot_names) else ""
        for li in poly.loop_indices:
            co = verts[me.loops[li].vertex_index]
            u, v, hh = co.x, -co.y, co.z
            if slot in ("Floor", "FloorAlt", "Path", "Overgrowth", "Bridge"):
                uv.data[li].uv = (u / TILE + 0.5, v / TILE + 0.5)
            elif slot == "Chasm":
                # Depth below the ground (0: a drop is always a tile's lowest corner), the same in every tile: fades into fog
                uv.data[li].uv = ((u + v) * 0.3, max(0.0, -hh))
            elif slot == "Liquid":
                g = F.g("Liquid", u, v) if "Liquid" in F.present else 0.0
                uv.data[li].uv = (max(0.0, g) * TILE / 2, 0.0)              # Meters from the shore
            elif slot == "Glow":
                g = F.g("Liquid", u, v) if "Liquid" in F.present else 0.0
                uv.data[li].uv = (max(0.0, -g) * TILE / 2, 0.0)             # Meters from the pool's edge
            else:
                uv.data[li].uv = ((u + v) * 0.3, hh * 0.3)
    for m in b.mats: me.materials.append(m)
    me.update()
    o = bpy.data.objects.new(name, me)
    coll.objects.link(o)
    return o


def colliders(name, F, g, h, vis, coll, S, strip, ramp):
    """Ground (walkable and rock area, aimable), Wall (rock, cliffs), Water (drop and liquid: surface + unit blocker).
    g holds each connection's region (kept out of a ramp's strip); the ramp's strip gets ground along its slope."""
    def solid(suffix, fn, low, high, top_fn=None):
        cb = T.Builder()
        polys = T.region(fn, merge=False)
        if not polys: return
        cap = cb.surface(polys, top_fn or high, None)
        cb.skirt(cap, low, None, contour_only=True)
        cb.surface(polys, low, None, flip=True)
        o = cb.to_object(f"{name}_{suffix}", coll)
        o.data.materials.clear()
        o.parent = vis

    walkable = [c for c in F.present if CONN[c]["kind"] in FLOORLIKE + ("bridge", "rock")]
    top_h = max(h.values())
    for c in walkable:
        solid(f"ColGround{CONN[c]['id']}", g[c], h[c] - 0.2, h[c])
        if CONN[c]["kind"] == "rock":
            solid(f"ColWall{CONN[c]['id']}", g[c], h[c], h[c] + S["wall_h"])
    if strip: solid("ColGroundRamp", strip, -0.2, None, top_fn=ramp)
    # High ground beside lower ground in the same tile: a cliff that blocks units below (not on the ramp's strip)
    lows = [c for c in walkable if CONN[c]["kind"] != "rock"]
    if len({h[c] for c in lows}) > 1:
        for c in lows:
            if h[c] > min(h[o] for o in lows):
                solid(f"ColCliff{CONN[c]['id']}", g[c], -0.2, h[c] - 0.1)
    for c in [c for c in F.present if CONN[c]["kind"] in ("drop", "liquid")]:
        solid(f"ColWaterSurface{CONN[c]['id']}", g[c], h[c] - 0.45, h[c] - 0.35)
        solid(f"ColWaterBlocker{CONN[c]['id']}", g[c], h[c] - 0.4, top_h + 2.0)


# ---------- the kit ----------

def junctions(a, b, c):
    """The ways three connections share a tile's corners, one of them twice (rotations and mirroring give the rest):
    the doubled one on two neighboring corners, or on two opposite ones."""
    out = []
    letter = {a: "A", b: "B", c: "C"}
    for x, y, z in ((a, b, c), (b, a, c), (c, a, b)):
        out.append((letter[x] * 2 + letter[y] + letter[z], [x, x, y, z]))   # Doubled side by side (the mirror swaps y and z)
        out.append((letter[x] + letter[y] + letter[x] + letter[z], [x, y, x, z]))   # Doubled across
    return out


def patterns(a, b):
    """The 4 ways two connections share a tile's corners (rotations give the rest): SW, SE, NE, NW."""
    return [("1", [a, a, a, b]), ("2", [a, a, b, b]), ("D", [a, b, a, b]), ("3", [a, b, b, b])]


def kit_tiles(S):
    """Every tile of the kit: name, model, corners, edges, weight, background, mirror, extra looks."""
    tiles = []
    models = {}
    used = set(S.get("connections", CONN))      # A tile is only in the kit when all its corners are
    forest = S.get("wall") == "trees"           # A tree wall repeats a lot: it gets extra looks
    weights = dict(WEIGHT, **S.get("weights", {}))

    def weight(corners):
        w = 1.0
        for c in corners: w *= weights[c] * (S["drop_factor"] if c == "Drop" else 1.0) * (S["liquid_factor"] / 0.5 if c == "Liquid" else 1.0)
        return w

    def add(name, corners, model=None, kind=None, factor=1.0, background=False, mirror=False, looks=0, walk_through=False, edges=None):
        if not set(corners) <= used: return
        # Each kit's own prefix, so two kits' models never share names in one .blend (Blender would add .001)
        model = S.get("prefix", "K_") + (model or "K_" + name)[2:]
        if model not in models: models[model] = dict(corners=corners, kind=kind, looks=looks)
        tiles.append(dict(name=name, model=model, corners=corners, edges=edges or edge_ids(corners), weight=weight(corners) * factor,
                          background=background, mirror=mirror, walk_through=walk_through,
                          looks=[f"{model}_Look{l}" for l in range(1, looks + 1)]))

    for c in ("Floor", "Path", "Overgrowth", "Rock", "Drop", "Liquid"):
        add(c, [c] * 4, looks=2 if c == "Floor" or (c == "Rock" and forest) else 0, mirror=c == "Floor")
    for c, low in (("FloorHigh", "Floor"), ("PathHigh", "Path"), ("OvergrowthHigh", "Overgrowth")):
        add(c, [c] * 4, model="K_" + low, looks=2 if c == "FloorHigh" else 0, mirror=c == "FloorHigh")
    for a, b in (("Floor", "Path"), ("Floor", "Overgrowth"), ("Floor", "Rock"), ("Floor", "Drop"), ("Floor", "Liquid"),
                 ("Rock", "Drop"), ("Drop", "Bridge"), ("FloorHigh", "Drop")):
        for p, corners in patterns(a, b):
            add(f"{a}_{b}_{p}", corners, factor=0.5 if p == "D" else 1.0)
    for a, b, la, lb in (("FloorHigh", "PathHigh", "Floor", "Path"), ("FloorHigh", "OvergrowthHigh", "Floor", "Overgrowth")):
        for p, corners in patterns(a, b):
            add(f"{a}_{b}_{p}", corners, model=f"K_{la}_{lb}_{p}", factor=0.5 if p == "D" else 1.0)
    # Between heights: cliffs, and a ramp cut through a straight cliff (units walk through it from low to high)
    # Corner and diagonal cliffs are rarer than straight ones, so cliffs run long instead of zigzagging
    for p, corners in patterns("Floor", "FloorHigh"):
        add(f"Cliff_{p}", corners, factor={"2": 1.0, "D": 0.25}.get(p, 0.5))
    # Narrow ramps, and wide ones (a RampSide on each side, RampMids between): neighbouring ramps mostly merge.
    # Wide ramps aren't Walk Through: their walkable Slope edges already join low and high ground, and the connectivity
    # repair, which asks for Walk Through tiles, then only asks for narrow ramps (a wide one needs ramp neighbours,
    # which a carved corridor can't give it)
    ramp = ["Floor", "Floor", "FloorHigh", "FloorHigh"]
    add("Ramp", ramp, kind="ramp", factor=RAMP_WEIGHT["Ramp"], walk_through=True)
    add("RampSide", ramp, kind="ramp_side", factor=RAMP_WEIGHT["RampSide"], mirror=True, edges=["Plain", "Slope", "Plain", "Cliff"])
    add("RampMid", ramp, kind="ramp_mid", factor=RAMP_WEIGHT["RampMid"], edges=["Plain", "Slope", "Plain", "Slope"])
    # Where a bridge reaches the ground (its mirror is the other side)
    add("BridgeEnd", ["Floor", "Floor", "Bridge", "Drop"], factor=4.0, mirror=True)
    # Where a cliff between low and high ground runs into a chasm: without these a plateau could only be ringed all by
    # cliffs or all by drop, and a high area beside a chasm could never get a ramp down
    for p, corners in junctions("Floor", "FloorHigh", "Drop"):
        add(f"CliffDrop_{p}", corners, factor=0.3, mirror=True)
    # Rivers and lakes crossed by bridges: water beside trees and paths, where three meet (a painted river runs into the
    # tree wall or past a path), bridge decks over the water and their ends on the bank
    if S.get("bridge") == "rope":
        for a, b in (("Path", "Liquid"), ("Rock", "Liquid"), ("Liquid", "Bridge")):
            for p, corners in patterns(a, b):
                add(f"{a}_{b}_{p}", corners, factor=0.5 if p == "D" else 1.0, kind="planks_v" if b == "Bridge" else None)
        for a, b, c in (("Floor", "Path", "Liquid"), ("Floor", "Rock", "Liquid")):
            for p, corners in junctions(a, b, c):
                add(f"{a}{b}{c}_{p}", corners, factor=0.3, mirror=True)
        for ground in ("Floor", "Path"):
            add(f"BridgeEnd{'' if ground == 'Floor' else ground}", [ground, ground, "Bridge", "Liquid"], factor=4.0, mirror=True)
    # Background ring: rock and drop (with spires standing in the fog)
    add("Rock_Background", ["Rock"] * 4, model="K_Rock", background=True, looks=2 if forest else 0)
    add("Drop_Background", ["Drop"] * 4, model="K_DropSpires", kind="spires", background=True, looks=2, factor=3.0)
    for p, corners in patterns("Rock", "Drop"):
        add(f"Rock_Drop_{p}_Background", corners, model=f"K_Rock_Drop_{p}", background=True)
    return tiles, models


def build_kit(S, coll):
    """Every model of the kit (and its looks) in coll. Returns {model name: object}."""
    M = materials(S)
    tiles, models = kit_tiles(S)
    objects = {}
    for i, (model, spec) in enumerate(sorted(models.items())):
        seed = S["seed"] * 101 + i * 13
        objects[model] = build_model(model, spec["corners"], S, M, coll, seed=seed, kind=spec["kind"])
        for l in range(1, spec["looks"] + 1):
            objects[f"{model}_Look{l}"] = build_model(f"{model}_Look{l}", spec["corners"], S, M, coll, seed=seed + l * 997, kind=spec["kind"], look=l)
    for i, (name, rows) in enumerate(sorted(S.get("pieces", {}).items())):
        model = S.get("prefix", "K_") + name
        objects[model] = build_piece(model, rows, S, M, coll, seed=S["seed"] * 211 + i * 31)
    return objects


def piece_corners(rows):
    """A piece's corner connections, row by row from the south-west (WfcPiece.CornerIds order), and its size in cells."""
    corners = [PIECE_LETTERS[ch] for row in reversed(rows) for ch in row]
    return corners, (len(rows[0]) - 1, len(rows) - 1)


def build_piece(name, rows, S, M, coll, seed=0):
    """A set piece from the kit's cells: each cell built like a tile with its four corners, moved into place, then joined
    into one model (pivot in the footprint's middle) with one collider object per collider kind."""
    corners, (w, h) = piece_corners(rows)
    at = lambda x, y: corners[x + y * (w + 1)]
    parts, colliders = [], {}
    for y in range(h):
        for x in range(w):
            cell = [at(x, y), at(x + 1, y), at(x + 1, y + 1), at(x, y + 1)]
            o = build_model(f"{name}_cell{x}_{y}", cell, S, M, coll, seed=seed + x * 17 + y * 131, look=(x + 2 * y) % 3)
            offset = Matrix.Translation(((x + 0.5 - w / 2) * TILE, -(y + 0.5 - h / 2) * TILE, 0.0))
            o.data.transform(offset)
            for child in list(o.children):
                child.data.transform(offset)
                child.parent = None
                suffix = child.name.split("_Col", 1)[1]
                colliders.setdefault("Col" + suffix, []).append(child)
            parts.append(o)

    def join(objects, new_name):
        with bpy.context.temp_override(active_object=objects[0], selected_editable_objects=objects, selected_objects=objects):
            bpy.ops.object.join()
        joined = objects[0]
        joined.name = new_name
        joined.data.name = new_name
        return joined

    vis = join(parts, name)
    for suffix, objects in colliders.items():
        col = join(objects, f"{name}_{suffix}")
        col.parent = vis
    return vis


def export_kit(objects, outdir, S):
    """FBX per model (with its collider children), slots.txt and kit.json (connections and tiles) for BiomeKitBuilder."""
    os.makedirs(outdir, exist_ok=True)
    slots = ["# object|material slots in submesh order"]
    for name, o in sorted(objects.items()):
        T.export_fbx(o, os.path.join(outdir, name + ".fbx"))     # Mirrored in X on the way out: Unity's import flips it back
        slots.append(name + "|" + ",".join(m.name for m in o.data.materials))
    with open(os.path.join(outdir, "slots.txt"), "w") as f: f.write("\n".join(slots))

    tiles, _ = kit_tiles(S)
    used = S.get("connections", CONN)
    kit = {
        "corners": [dict(id=v["id"], name=k, terrain=v["terrain"], height=v["height"],
                         higher=CONN[v["higher"]]["id"] if v["higher"] in used else -1) for k, v in CONN.items() if k in used],
        "edges": [dict(id=e[1], name=e[0], walkable=e[2]) for e in EDGES],
        "tiles": [dict(name=t["name"], model=t["model"], corners=[CONN[c]["id"] for c in t["corners"]],
                       edges=[EDGE[e] for e in t["edges"]], weight=round(t["weight"], 4), background=t["background"],
                       mirror=t["mirror"], walkThrough=t["walk_through"], looks=t["looks"]) for t in tiles],
        "pieces": [dict(name=name, model=S.get("prefix", "K_") + name, size=list(piece_corners(rows)[1]),
                        corners=[CONN[c]["id"] for c in piece_corners(rows)[0]]) for name, rows in sorted(S.get("pieces", {}).items())],
    }
    with open(os.path.join(outdir, "kit.json"), "w") as f: json.dump(kit, f, indent=1)
