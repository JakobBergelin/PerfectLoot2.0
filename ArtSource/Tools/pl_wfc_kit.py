"""Perfect Loot WFC biome kit for Blender (5.x): the tiles of a biome tile set, described by connections.

A tile names a corner connection for each corner (SW, SE, NE, NW; +X east, +Z north in Unity) and an edge connection for
each edge (S, E, N, W), like WfcTileSet. Corner connections have a kind (floor, path, overgrowth, rock, drop, liquid,
bridge) and a height: the high ground is the same kinds 2.5 m up. A tile's model sits at its lowest corner's height
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
from mathutils import Vector, noise
import pl_wfc_tiles as T

TILE, N, STEP = T.TILE, T.N, T.STEP
HIGH = 2.5

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
# name, Id, units can walk across. A ramp is a Walk Through tile with a straight cliff's edges, so it needs no edges of its own.
EDGES = [("Plain", 0, True), ("Cliff", 1, False)]
EDGE = {e[0]: e[1] for e in EDGES}
FLOORLIKE = ("floor", "path", "overgrowth")
RAMP_HALF = 1.4     # Half the width of a ramp cut through a cliff
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
}

# Material slot names: Unity puts the biome's material on each (BiomeKitBuilder)
SLOTS = ["Floor", "FloorAlt", "Joint", "Path", "Overgrowth", "Accent", "Cliff", "WallSide", "WallTop", "WallDark",
         "Bank", "Chasm", "Liquid", "Glow", "Bridge", "Spire"]
WEIGHT = {"Floor": 4.0, "Path": 1.6, "Overgrowth": 1.6, "Rock": 1.0, "Drop": 1.0, "Liquid": 0.5, "Bridge": 0.25,
          "FloorHigh": 1.8, "PathHigh": 1.0, "OvergrowthHigh": 1.0}


def materials():
    """Placeholder materials named like the slots (colors only help reading the renders)."""
    colors = {"Floor": "#4A4656", "FloorAlt": "#454152", "Joint": "#24212C", "Path": "#6A6474", "Overgrowth": "#3E5A58",
              "Accent": "#3FC4B4", "Cliff": "#5C5870", "WallSide": "#565470", "WallTop": "#8C88A8", "WallDark": "#3A3848",
              "Bank": "#2A2834", "Chasm": "#123E44", "Liquid": "#1C4A54", "Glow": "#2A6A64", "Bridge": "#6B4E3A",
              "Spire": "#3A3A4C"}
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


def bridge_surface(b, fn, h, M, R):
    """A plank deck at h over the region (a drop under it). Returns the outline faces."""
    deck = region_faces(b, fn, h, M["Bridge"])
    # Planks: thin raised strips across the deck
    for j in range(N // 2):
        cells = [(i, j * 2 + c) for c in range(2) for i in range(N)]
        polys = T.region(fn, 0.0, cells)
        if not polys: continue
        faces = b.surface(polys, h + 0.04 + R.uniform(0, 0.02), M["Bridge"], scale_about=(0.0, T._gp(j * 2) + STEP), k=0.92)
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
    # A ramp is a strip cut through the middle of a straight cliff, from the low ground (south) up to the high ground
    ramp = (lambda u, v: (v / TILE + 0.5) * HIGH) if kind == "ramp" else None
    strip = (lambda u, v: RAMP_HALF - abs(u)) if ramp else None
    if strip: g = {c: (lambda u, v, c=c: min(F.g(c, u, v), abs(u) - RAMP_HALF)) for c in F.present}

    # Surfaces; each returns faces whose outline gets the faces down to its neighbors
    outlines = {}
    helpers = []
    for c in F.present:
        k = CONN[c]["kind"]
        if k in FLOORLIKE: outlines[c] = floor_surface(b, k, g[c], h[c], S, M, R, look=look)
        elif k == "rock":
            outlines[c] = rock_surface(b, g[c], h[c], S, M, seed)
            helpers += outlines[c]
        elif k == "bridge": outlines[c] = bridge_surface(b, g[c], h[c], M, R)
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
    if "Liquid" in F.present:
        hl = h["Liquid"]
        b.surface(T.region(g["Liquid"]), hl - 1.1, M["Bank"])
        b.surface(T.region(lambda u, v: g["Liquid"](u, v) + 0.06), hl - 0.3, M["Liquid"])
        if S["glow"] > 0:
            reach = S["glow"] / (TILE / 2)
            b.surface(T.region(lambda u, v: min(g["Liquid"](u, v) + reach, -g["Liquid"](u, v) + 0.02), merge=False), hl + 0.03, M["Glow"])

    # Faces down from each outline to whatever is across it
    for c, faces in outlines.items():
        k = CONN[c]["kind"]
        for a, e in outline_edges(b, faces):
            if strip and abs(abs((uv_of(a)[0] + uv_of(e)[0]) / 2) - RAMP_HALF) < 0.06: continue   # The ramp's sides are below
            other = across(F, a, e)
            ko = CONN[other]["kind"]
            ta = a.co.z
            if k in FLOORLIKE and ko in FLOORLIKE and h[c] > h[other] + 0.01:
                wall(b, a, e, ta, h[other] - 0.03, M["Cliff"])     # Straight down: meets the lower floor on the outline
            elif k in FLOORLIKE + ("rock",) and ko == "drop":
                wall(b, a, e, ta, -depth, M["Chasm"], jitter)
            elif k in FLOORLIKE and ko == "liquid":
                wall(b, a, e, ta, h[other] - 1.1, M["Bank"])
            elif k == "bridge" and ko == "drop":
                wall(b, a, e, ta, h[c] - 0.4, M["Bridge"])

    for f in helpers:
        if f.is_valid: b.bm.faces.remove(f)

    # Bridge railing posts along the deck's edge over the drop
    if "Bridge" in F.present and "Drop" in F.present:
        for a, e in outline_edges(b, outlines["Bridge"]):
            if across(F, a, e) != "Drop" or R.random() > 0.3: continue
            (ua, va) = uv_of(a)
            box(b, ua, va, h["Bridge"], 0.09, 0.09, 0.9, M["Bridge"])

    # The ramp's sides: down from the ramp to the low ground beside it, or from the high ground down to the ramp
    if strip:
        for side in (-1, 1):
            u = side * RAMP_HALF
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
    for p, (_, m) in zip(me.polygons, faces):
        p.material_index = m
        p.use_smooth = False
    uv = me.uv_layers.new(name="UVMap")
    slot_names = [m.name for m in b.mats]
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

    def weight(corners):
        w = 1.0
        for c in corners: w *= WEIGHT[c] * (S["drop_factor"] if c == "Drop" else 1.0) * (S["liquid_factor"] / 0.5 if c == "Liquid" else 1.0)
        return w

    def add(name, corners, model=None, kind=None, factor=1.0, background=False, mirror=False, looks=0, walk_through=False):
        model = model or "K_" + name
        if model not in models: models[model] = dict(corners=corners, kind=kind, looks=looks)
        tiles.append(dict(name=name, model=model, corners=corners, edges=edge_ids(corners), weight=weight(corners) * factor,
                          background=background, mirror=mirror, walk_through=walk_through,
                          looks=[f"{model}_Look{l}" for l in range(1, looks + 1)]))

    for c in ("Floor", "Path", "Overgrowth", "Rock", "Drop", "Liquid"):
        add(c, [c] * 4, looks=2 if c == "Floor" else 0, mirror=c == "Floor")
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
    add("Ramp", ["Floor", "Floor", "FloorHigh", "FloorHigh"], kind="ramp", factor=0.5, walk_through=True)
    # Where a bridge reaches the ground (its mirror is the other side)
    add("BridgeEnd", ["Floor", "Floor", "Bridge", "Drop"], factor=4.0, mirror=True)
    # Where a cliff between low and high ground runs into a chasm: without these a plateau could only be ringed all by
    # cliffs or all by drop, and a high area beside a chasm could never get a ramp down
    for p, corners in junctions("Floor", "FloorHigh", "Drop"):
        add(f"CliffDrop_{p}", corners, factor=0.3, mirror=True)
    # Background ring: rock and drop (with spires standing in the fog)
    add("Rock_Background", ["Rock"] * 4, model="K_Rock", background=True)
    add("Drop_Background", ["Drop"] * 4, model="K_DropSpires", kind="spires", background=True, looks=2, factor=3.0)
    for p, corners in patterns("Rock", "Drop"):
        add(f"Rock_Drop_{p}_Background", corners, model=f"K_Rock_Drop_{p}", background=True)
    return tiles, models


def build_kit(S, coll):
    """Every model of the kit (and its looks) in coll. Returns {model name: object}."""
    M = materials()
    tiles, models = kit_tiles(S)
    objects = {}
    for i, (model, spec) in enumerate(sorted(models.items())):
        seed = S["seed"] * 101 + i * 13
        objects[model] = build_model(model, spec["corners"], S, M, coll, seed=seed, kind=spec["kind"])
        for l in range(1, spec["looks"] + 1):
            objects[f"{model}_Look{l}"] = build_model(f"{model}_Look{l}", spec["corners"], S, M, coll, seed=seed + l * 997, kind=spec["kind"], look=l)
    return objects


def export_kit(objects, outdir, S):
    """FBX per model (with its collider children), slots.txt and kit.json (connections and tiles) for BiomeKitBuilder."""
    os.makedirs(outdir, exist_ok=True)
    slots = ["# object|material slots in submesh order"]
    for name, o in sorted(objects.items()):
        bpy.ops.object.select_all(action='DESELECT')
        o.select_set(True)
        for c in o.children: c.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.export_scene.fbx(filepath=os.path.join(outdir, name + ".fbx"), use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                                 axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True, use_mesh_modifiers=True,
                                 mesh_smooth_type='OFF', add_leaf_bones=False, bake_anim=False, object_types={'MESH'})
        slots.append(name + "|" + ",".join(m.name for m in o.data.materials))
    with open(os.path.join(outdir, "slots.txt"), "w") as f: f.write("\n".join(slots))

    tiles, _ = kit_tiles(S)
    kit = {
        "corners": [dict(id=v["id"], name=k, terrain=v["terrain"], height=v["height"],
                         higher=CONN[v["higher"]]["id"] if v["higher"] else -1) for k, v in CONN.items()],
        "edges": [dict(id=e[1], name=e[0], walkable=e[2]) for e in EDGES],
        "tiles": [dict(name=t["name"], model=t["model"], corners=[CONN[c]["id"] for c in t["corners"]],
                       edges=[EDGE[e] for e in t["edges"]], weight=round(t["weight"], 4), background=t["background"],
                       mirror=t["mirror"], walkThrough=t["walk_through"], looks=t["looks"]) for t in tiles],
    }
    with open(os.path.join(outdir, "kit.json"), "w") as f: json.dump(kit, f, indent=1)
