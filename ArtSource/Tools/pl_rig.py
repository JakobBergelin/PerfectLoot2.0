"""Perfect Loot rigid-rig + keyframe helpers for Blender (5.x).

Models built from separate rigid pieces get one bone per piece group (rigid skinning).
All bones point straight up (+Z) with roll 0, so pose-space axes are predictable:
    rot (pitch, yaw, roll) in degrees:
        pitch  > 0 tips the part above the joint forward (toward -Y, the model's front)
                   and swings parts below the joint backward
        yaw    > 0 turns left (counter-clockwise seen from above)
        roll   > 0 tilts toward -X
    loc (x, y, z) in metres, world-style axes (z = up, -y = forward)
    scl uniform float

Usage inside Blender:
    import pl_rig as R
    arm, mesh = R.build(src_obj, "CT_Revenant_Rig", coll, bones, classify)
    R.clip(arm, "CT_Revenant_Idle", 48, {"spine": [(0, (0,0,0)), (24, (3,0,0)), (48, (0,0,0))]}, loop=True)
    R.export(arm, mesh, path)
"""
import bpy, bmesh, math
from mathutils import Vector

UP = 0.25  # bone length


def islands(o):
    """[(vertex indices, centroid, size, material names)] for each loose part of a mesh."""
    bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
    seen = set(); out = []
    for v in bm.verts:
        if v.index in seen: continue
        stack = [v]; grp = []; seen.add(v.index)
        while stack:
            x = stack.pop(); grp.append(x)
            for e in x.link_edges:
                y = e.other_vert(x)
                if y.index not in seen: seen.add(y.index); stack.append(y)
        cs = [g.co for g in grp]
        mn = Vector([min(c[i] for c in cs) for i in range(3)]); mx = Vector([max(c[i] for c in cs) for i in range(3)])
        mats = {o.data.materials[f.material_index].name for g in grp for f in g.link_faces}
        out.append(([g.index for g in grp], (mn + mx) / 2, mx - mn, mats))
    bm.free()
    return out


def build(src, name, coll, bones, classify):
    """Copy src mesh, build an armature from bones {name: (head xyz, parent or None)},
    assign every loose part to classify(centroid, size, mats) -> bone name. Returns (armature, mesh)."""
    for n in (name, name + "_Mesh"):
        o = bpy.data.objects.get(n)
        if o: bpy.data.objects.remove(o, do_unlink=True)
    me = src.data.copy(); me.name = name + "_Mesh"
    mesh = bpy.data.objects.new(name + "_Mesh", me); coll.objects.link(mesh)
    ad = bpy.data.armatures.new(name); arm = bpy.data.objects.new(name, ad); coll.objects.link(arm)
    arm.show_in_front = True
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = arm; arm.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    for bn, (head, parent) in bones.items():
        b = ad.edit_bones.new(bn); b.head = Vector(head); b.tail = Vector(head) + Vector((0, 0, UP)); b.roll = 0
    for bn, (head, parent) in bones.items():
        if parent: ad.edit_bones[bn].parent = ad.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    groups = {bn: mesh.vertex_groups.new(name=bn) for bn in bones}
    counts = {}
    for idx, c, s, mats in islands(mesh):
        bn = classify(c, s, mats)
        groups[bn].add(idx, 1.0, 'REPLACE'); counts[bn] = counts.get(bn, 0) + 1
    mesh.parent = arm
    md = mesh.modifiers.new("Armature", 'ARMATURE'); md.object = arm
    for pb in arm.pose.bones: pb.rotation_mode = 'XYZ'
    return arm, mesh, counts


def _apply(pb, rot=None, loc=None, scl=None):
    if rot is not None:
        p, y, r = rot
        pb.rotation_euler = (math.radians(p), math.radians(y), math.radians(r))
    if loc is not None:
        x, y, z = loc
        pb.location = (x, z, -y)  # bone-local: X = world X, Y = world Z, Z = world -Y
    if scl is not None:
        pb.scale = (scl, scl, scl)


def clip(arm, name, length, keys, loop=False, linear=()):
    """keys: {bone: [(frame, rot, loc=None, scl=None), ...]}. Every bone gets rest keys at 0 and length
    unless given. Bones listed in `linear` get linear interpolation (spins)."""
    ad = arm.animation_data or arm.animation_data_create()
    act = bpy.data.actions.get(name)
    if act: bpy.data.actions.remove(act)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    ad.action = act
    for pb in arm.pose.bones:
        _apply(pb, (0, 0, 0), (0, 0, 0), 1.0)
    for pb in arm.pose.bones:
        ks = keys.get(pb.name, [])
        frames = [k[0] for k in ks]
        rest = [] if 0 in frames else [(0, (0, 0, 0), (0, 0, 0), 1.0)]
        end = [] if length in frames else [(length, (0, 0, 0), (0, 0, 0), 1.0)] if loop or not ks else []
        for k in rest + list(ks) + end:
            f = k[0]; rot = k[1] if len(k) > 1 else None; loc = k[2] if len(k) > 2 else None; scl = k[3] if len(k) > 3 else None
            _apply(pb, rot if rot is not None else (0, 0, 0), loc if loc is not None else (0, 0, 0), scl if scl is not None else 1.0)
            pb.keyframe_insert("rotation_euler", frame=f)
            pb.keyframe_insert("location", frame=f)
            pb.keyframe_insert("scale", frame=f)
    for fc in _fcurves(act):
        lin = any(f'"{b}"' in fc.data_path for b in linear)
        for kp in fc.keyframe_points:
            kp.interpolation = 'LINEAR' if lin else 'BEZIER'
    act.frame_range = (0, length)
    ad.action = None
    track = ad.nla_tracks.new(); track.name = name
    st = track.strips.new(name, 0, act); st.name = name
    return act


def _fcurves(act):
    try:
        return list(act.fcurves)
    except Exception:
        out = []
        for layer in act.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    out += list(bag.fcurves)
        return out


def export(arm, mesh, path):
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True); mesh.select_set(True); bpy.context.view_layer.objects.active = arm
    loc = arm.location.copy(); arm.location = (0, 0, 0)
    for t in arm.animation_data.nla_tracks: t.mute = False
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=False,
                             object_types={'ARMATURE', 'MESH'}, use_mesh_modifiers=False, mesh_smooth_type='OFF',
                             add_leaf_bones=False, use_armature_deform_only=False, primary_bone_axis='Y', secondary_bone_axis='X',
                             bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True,
                             bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    arm.location = loc
