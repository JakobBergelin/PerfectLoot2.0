"""Perfect Loot WFC tile generator for Blender (5.x): corner-terrain tiles with real transitions.

A tile is TILE m square, pivot in the middle, floor top at 0. Its four corners (SW, SE, NE, NW in Unity terms, +X east,
+Z north) are each F(loor), W(all) or L (the tile set's "Water" slot; a crack/chasm in the cathedral).
Each terrain's region comes from bilinear corner weights plus a little noise that fades to zero at the tile border, so
along an edge the boundary only depends on that edge's two corners and sits at its middle: any two tiles whose shared
corners match line up, in every rotation.

Usage inside Blender:
    import pl_wfc_tiles as T
    T.build_all(collection, materials)     # one object per tile + collision objects (children)
    T.export_all(collection, out_dir)      # FBX per tile (+ slots.txt)
Work is done in Unity coordinates (u = x east, v = z north, h = up) and written as Blender (u, -v, h); with the
pipeline's FBX settings (axis_forward -Z, axis_up Y) Blender -Y comes out as Unity +Z.
"""
import bpy, bmesh, math, os, random
from mathutils import Vector, noise

TILE = 5.0
N = 20                      # grid cells per side (0.25 m)
STEP = TILE / N
SLAB = 5                    # grid cells per floor slab (1.25 m)
WALL_H = 3.0
CHASM_D = 2.2
TERRAINS = "FWL"
CORNERS = [(-1, -1), (1, -1), (1, 1), (-1, 1)]     # SW, SE, NE, NW as (u, v) signs


# ---------- terrain field ----------

def _bilinear(corners, u, v):
    """Weight of each terrain letter at (u, v) from the four corner letters."""
    s = (u / TILE + 0.5); t = (v / TILE + 0.5)
    w = [(1 - s) * (1 - t), s * (1 - t), s * t, (1 - s) * t]
    out = {c: 0.0 for c in TERRAINS}
    for k in range(4): out[corners[k]] += w[k]
    return out


def _falloff(u, v, width=1.0):
    d = TILE / 2 - max(abs(u), abs(v))
    x = max(0.0, min(1.0, d / width))
    return x * x * (3 - 2 * x)


class Field:
    def __init__(self, corners, seed):
        self.corners = corners
        self.seed = seed
        self.off = {c: Vector((seed * 3.7 + i * 11.3, seed * 1.9 + i * 5.1, i * 2.3)) for i, c in enumerate(TERRAINS)}
        self.present = sorted(set(corners))
        self.cache = {}

    def score(self, c, u, v):
        f = _bilinear(self.corners, u, v)[c]
        n = noise.noise(Vector((u * 0.55, v * 0.55, 0)) + self.off[c]) * 0.22 * _falloff(u, v)
        return f + n

    def g(self, c, u, v):
        """> 0 inside terrain c's region; the boundary with each other terrain is where this crosses 0."""
        key = (c, round(u, 4), round(v, 4))
        if key in self.cache: return self.cache[key]
        if c not in self.present: val = -1.0
        elif len(self.present) == 1: val = 1.0
        else:
            mine = self.score(c, u, v)
            val = mine - max(self.score(o, u, v) for o in self.present if o != c)
        self.cache[key] = val
        return val


# ---------- marching squares ----------

def _gp(i):
    return -TILE / 2 + i * STEP


def cell_polygon(fn, i, j, iso=0.0):
    """Inside part of grid cell (i, j) where fn(u, v) > iso, as a CCW list of (u, v) (empty when outside)."""
    pts = [(_gp(i), _gp(j)), (_gp(i + 1), _gp(j)), (_gp(i + 1), _gp(j + 1)), (_gp(i), _gp(j + 1))]
    vals = [fn(u, v) - iso for u, v in pts]
    if all(x > 0 for x in vals): return pts
    if all(x <= 0 for x in vals): return []
    poly = []
    for k in range(4):
        a, b = pts[k], pts[(k + 1) % 4]; va, vb = vals[k], vals[(k + 1) % 4]
        if va > 0: poly.append(a)
        if (va > 0) != (vb > 0):
            t = va / (va - vb)
            poly.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
    return poly if len(poly) >= 3 else []


def region(fn, iso=0.0, cells=None, merge=True):
    """Polygons covering fn > iso, edge cells cut by the contour. merge joins fully inside cells into long quads (fewer
    triangles, but T-junctions: don't merge surfaces whose outline gets sides or whose height varies)."""
    cells = cells or [(i, j) for j in range(N) for i in range(N)]
    polys = []
    full = set()
    for (i, j) in cells:
        poly = cell_polygon(fn, i, j, iso)
        if merge and len(poly) == 4 and poly[0] == (_gp(i), _gp(j)): full.add((i, j))
        elif poly: polys.append(poly)
    if merge and len(full) == len(cells):
        us = [_gp(i) for i, _ in cells] + [_gp(i + 1) for i, _ in cells]
        vs = [_gp(j) for _, j in cells] + [_gp(j + 1) for _, j in cells]
        if len(full) == (round((max(us) - min(us)) / STEP)) * (round((max(vs) - min(vs)) / STEP)):
            return [[(min(us), min(vs)), (max(us), min(vs)), (max(us), max(vs)), (min(us), max(vs))]]
    # Merge runs of full cells along u
    for j in range(N):
        i = 0
        while i < N:
            if (i, j) in full:
                k = i
                while k + 1 < N and (k + 1, j) in full: k += 1
                polys.append([(_gp(i), _gp(j)), (_gp(k + 1), _gp(j)), (_gp(k + 1), _gp(j + 1)), (_gp(i), _gp(j + 1))])
                i = k + 1
            else:
                i += 1
    return polys


# ---------- mesh building ----------

def _on_border(a, b):
    """True when the edge a-b (Blender verts) lies on the tile border, where the neighbouring tile carries on."""
    (ua, va), (ub, vb) = (a.co.x, -a.co.y), (b.co.x, -b.co.y)
    h = TILE / 2
    return ((abs(abs(ua) - h) < 1e-4 and abs(ua - ub) < 1e-5) or
            (abs(abs(va) - h) < 1e-4 and abs(va - vb) < 1e-5))

class Builder:
    def __init__(self):
        self.bm = bmesh.new()
        self.verts = {}
        self.mats = []

    def mat_index(self, m):
        if m not in self.mats: self.mats.append(m)
        return self.mats.index(m)

    def vert(self, u, v, h):
        key = (round(u, 4), round(v, 4), round(h, 4))
        vert = self.verts.get(key)
        if vert is None:
            vert = self.bm.verts.new((u, -v, h))
            self.verts[key] = vert
        return vert

    def face(self, vs, m):
        if len(set(vs)) < 3: return None
        try:
            f = self.bm.faces.new(vs)
        except ValueError:
            return None
        f.material_index = self.mat_index(m) if m is not None else 0
        return f

    def surface(self, polys, height, m, flip=False, scale_about=None, k=1.0):
        """Flat faces at height (a function of (u, v) or a number), facing up (down with flip). Returns the faces.
        Polygons are CCW in (u, v); written as Blender (u, -v) they turn clockwise, hence the reversal."""
        hf = height if callable(height) else (lambda u, v, h=height: h)
        faces = []
        for p in polys:
            if scale_about is not None:
                cu, cv = scale_about
                p = [(cu + (u - cu) * k, cv + (v - cv) * k) for u, v in p]
            vs = [self.vert(u, v, hf(u, v)) for u, v in p]
            if not flip: vs = vs[::-1]
            f = self.face(vs, m)
            if f: faces.append(f)
        return faces

    def skirt(self, faces, bottom, m, jitter=None, contour_only=False):
        """Vertical sides to height bottom from the outline of faces, facing away from them (into the region when the
        faces face down). jitter(u, v) -> (du, dv) moves the bottom ring (rock walls). contour_only leaves out the
        tile border (use unmerged regions, or T-junction seams get sides too)."""
        fs = set(faces)
        edges = []
        for f in faces:
            for loop in f.loops:
                e = loop.edge
                if sum(1 for lf in e.link_faces if lf in fs) != 1: continue
                a, b = loop.vert, loop.link_loop_next.vert
                if contour_only and _on_border(a, b): continue
                edges.append((a, b))
        out = []
        ring = {}
        for a, b in edges:
            def low(v):
                if v not in ring:
                    u, vv = v.co.x, -v.co.y
                    du, dv = jitter(u, vv) if jitter else (0.0, 0.0)
                    ring[v] = self.vert(u + du, vv + dv, bottom)
                return ring[v]
            # Up-facing faces loop CCW seen from above, so (b, a, low a, low b) faces outward
            f = self.face([b, a, low(a), low(b)], m)
            if f: out.append(f)
        return out

    def to_object(self, name, coll):
        """Mesh with faces sorted by material (slot order = submesh order) and planar UVs, flat shaded."""
        self.bm.verts.index_update()
        verts = [v.co.copy() for v in self.bm.verts]
        faces = sorted(([v.index for v in f.verts], f.material_index) for f in self.bm.faces if f.is_valid)
        faces.sort(key=lambda f: f[1])
        self.bm.free()
        me = bpy.data.meshes.new(name)
        me.from_pydata(verts, [], [f[0] for f in faces])
        for p, (_, m) in zip(me.polygons, faces):
            p.material_index = m
            p.use_smooth = False
        uv = me.uv_layers.new(name="UVMap")
        for loop in me.loops:
            co = verts[loop.vertex_index]
            uv.data[loop.index].uv = (co.x / TILE + 0.5 + co.z * 0.2, co.y / TILE + 0.5)
        for m in self.mats: me.materials.append(m)
        me.update()
        o = bpy.data.objects.new(name, me); coll.objects.link(o)
        return o


# ---------- tiles ----------

def _wall_top(seed):
    off = Vector((seed * 2.1, 7.7, seed * 0.3))
    def top(u, v):
        n = noise.noise(Vector((u * 0.9, v * 0.9, 0)) + off)
        return WALL_H + (0.55 * n - 0.1) * _falloff(u, v, 1.2)
    return top


def _jitter(seed, amount=0.18):
    off = Vector((seed * 1.3, 3.3, seed * 0.7))
    def j(u, v):
        f = _falloff(u, v, 0.8)
        return (noise.noise(Vector((u * 1.7, v * 1.7, 0)) + off) * amount * f,
                noise.noise(Vector((u * 1.7, v * 1.7, 5)) + off) * amount * f)
    return j


def build_tile(name, corners, M, coll, seed=0):
    """Visual object (floor slabs, walls, chasm) plus collision objects parented to it. M maps role -> material."""
    F = Field(corners, seed)
    gF = lambda u, v: F.g('F', u, v)
    gW = lambda u, v: F.g('W', u, v)
    gL = lambda u, v: F.g('L', u, v)
    R = random.Random(seed * 7919 + 17)
    b = Builder()

    # Floor: dark joint bed, then 1.25 m slabs with gaps, each a little higher or lower
    if 'F' in F.present:
        bed = b.surface(region(gF), -0.03, M['joint'])
        for sj in range(N // SLAB):
            for si in range(N // SLAB):
                cells = [(si * SLAB + a, sj * SLAB + c) for c in range(SLAB) for a in range(SLAB)]
                polys = region(gF, 0.0, cells)
                if not polys: continue
                if len(polys) > 1: polys = region(gF, 0.0, cells, merge=False)   # Cut slab: no T-junctions
                cu = _gp(si * SLAB) + SLAB * STEP / 2; cv = _gp(sj * SLAB) + SLAB * STEP / 2
                top = R.uniform(0.0, 0.035)
                tilt = (R.uniform(-0.012, 0.012), R.uniform(-0.012, 0.012))
                height = lambda u, v, cu=cu, cv=cv, top=top, tilt=tilt: top + (u - cu) * tilt[0] + (v - cv) * tilt[1]
                faces = b.surface(polys, height, M['slab'], scale_about=(cu, cv), k=(SLAB * STEP - 0.07) / (SLAB * STEP))
                b.skirt(faces, -0.03, M['joint'])

    # Walls: plinth, ruined body with a jagged top, cornice band
    if 'W' in F.present:
        plinth = b.surface(region(gW, -0.10, merge=False), 0.45, M['wall_dark'])
        b.skirt(plinth, 0.0, M['wall_dark'], contour_only=True)
        body = b.surface(region(gW, merge=False), _wall_top(seed), M['wall_top'])
        b.skirt(body, 0.0, M['wall'], contour_only=True)
        band = b.surface(region(gW, -0.05, merge=False), 2.2, M['wall_dark'])
        b.skirt(band, 2.0, M['wall_dark'], contour_only=True)

    # Crack: rock walls down into the dark (seen from inside the crack), a thin ruby glow along its deepest part
    if 'L' in F.present:
        rim = b.surface(region(gL, merge=False), 0.0, M['rock'], flip=True)
        b.skirt(rim, -CHASM_D, M['rock'], jitter=_jitter(seed), contour_only=True)
        for f in rim: b.bm.faces.remove(f)   # The opening itself stays open
        b.surface(region(gL), -CHASM_D, M['rock'])
        glow = region(gL, 0.42)
        if glow: b.surface(glow, -CHASM_D + 0.05, M['glow'])

    vis = b.to_object(name, coll)

    # Colliders (no materials): ground everywhere but the crack, wall volume, crack surface (aimable) + unit blocker
    def collider(suffix, fn, low, high, closed=True):
        cb = Builder()
        polys = region(fn, merge=not closed)
        if not polys: return None
        cap = cb.surface(polys, high, None)
        if closed:
            cb.skirt(cap, low, None, contour_only=True)
            cb.surface(polys, low, None, flip=True)
        o = cb.to_object(f"{name}_{suffix}", coll)
        o.data.materials.clear()
        o.parent = vis
        return o

    notL = lambda u, v: -gL(u, v) if 'L' in F.present else 1.0
    collider("ColGround", notL, -0.2, 0.0)
    if 'W' in F.present: collider("ColWall", gW, 0.0, WALL_H)
    if 'L' in F.present:
        collider("ColCrackSurface", gL, -0.45, -0.35)
        collider("ColCrackBlocker", gL, 0.0, 2.0)
    return vis


def build_arena(name, M, coll, size=2, seed=0):
    """size x size tile crossing: slab floor with a raised round dais (pillars are props placed in Unity)."""
    R = random.Random(seed)
    b = Builder()
    half = size * TILE / 2
    b.surface([[(-half, -half), (half, -half), (half, half), (-half, half)]], -0.03, M['joint'])
    n = int(size * TILE / (SLAB * STEP))
    w = SLAB * STEP
    radius = 2.6
    for j in range(n):
        for i in range(n):
            u0, v0 = -half + i * w, -half + j * w
            cu, cv = u0 + w / 2, v0 + w / 2
            if math.hypot(cu, cv) < radius - 0.2: continue   # Under the dais
            top = R.uniform(0.0, 0.035)
            g = 0.035
            faces = b.surface([[(u0 + g, v0 + g), (u0 + w - g, v0 + g), (u0 + w - g, v0 + w - g), (u0 + g, v0 + w - g)]], top, M['slab'])
            b.skirt(faces, -0.03, M['joint'])
    # Dais: two stepped discs and an inlaid ring
    for r, h, m in ((radius, 0.16, M['wall_dark']), (radius - 0.45, 0.28, M['wall']), (radius - 1.3, 0.3, M['slab'])):
        ring = [(math.cos(a) * r, math.sin(a) * r) for a in (2 * math.pi * k / 32 for k in range(32))]
        faces = b.surface([ring], h, m)
        b.skirt(faces, 0.0, m)
    vis = b.to_object(name, coll)

    cb = Builder()
    cap = cb.surface([[(-half, -half), (half, -half), (half, half), (-half, half)]], 0.0, None)
    cb.skirt(cap, -0.2, None)
    col = cb.to_object(f"{name}_ColGround", coll)
    col.data.materials.clear()
    col.parent = vis
    return vis


def canonical_corners():
    """Every corner combination that isn't a rotation of another (the tile set rotates them), named like the placeholders."""
    seen = set(); out = []
    for code in range(3 ** 4):
        c = [TERRAINS[(code // 3 ** i) % 3] for i in range(4)]
        rots = [tuple(c[(j + r) % 4] for j in range(4)) for r in range(4)]
        codes = [sum(TERRAINS.index(x) * 3 ** i for i, x in enumerate(rc)) for rc in rots]
        if min(codes) != code: continue
        out.append("".join(c))
    return out


def export_tiles(objs, outdir):
    """FBX per tile (with its collision children) + slots.txt."""
    os.makedirs(outdir, exist_ok=True)
    slots = ["# object|material slots in submesh order"]
    for o in objs:
        bpy.ops.object.select_all(action='DESELECT')
        o.select_set(True)
        for c in o.children: c.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.export_scene.fbx(filepath=os.path.join(outdir, o.name + ".fbx"), use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                                 axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True, use_mesh_modifiers=True,
                                 mesh_smooth_type='OFF', add_leaf_bones=False, bake_anim=False, object_types={'MESH'})
        slots.append(o.name + "|" + ",".join(m.name for m in o.data.materials))
    open(os.path.join(outdir, "slots.txt"), "w").write("\n".join(slots))
