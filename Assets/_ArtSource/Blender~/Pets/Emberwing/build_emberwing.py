"""Emberwing: cut the folded wings off the body, close both sides, spread the wings, open the beak.

  run_blender.py --blend SM_Pet_Emberwing_S1.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/Emberwing/build_emberwing.py

The Tripo model is one closed skin: the wings are folded against the body and fused to it, and the beak is
shut and solid. A bird like that cannot be rigged: a wing bone would drag the flank with it. So:

  1. each wing is sliced off with an upright cut along the flank, just where the wing leaves the body;
  2. the hole left in the body and the open side of the wing are both closed with a flat face;
  3. each wing is swung out about its shoulder until it lies flat and points straight out sideways, its
     outer face up and its feathers trailing: the spread pose a wing is rigged in. kn_wing_fold is the turn
     of the wing's first bone that lays it back along the body (close to how it was sculpted, not identical);
  4. the head: the loose scraps behind the crest are removed; each side gets one clean almond-shaped eye on
     the artist's outline; and the beak is cut along the mouth line the artist drew (low on the tip, up and
     back to the corner of the mouth). What is dark under that line is the lower beak: it is freed, left
     joined at the back, and the roof and floor of the mouth are closed black;
  5. the twig and the feet clenched round it are cut away, and each leg is carried on down, vertex to vertex,
     as a shank into a flat foot built on the artist's outline (rounded heel, broad pad, three pointed toes),
     in the legs' own colour; then every remaining open edge on the bird is closed;
  6. the whole bird is set down so that it stands on its feet (the tail tip, which hung below the twig, ends up
     a little under the ground in the rest pose; the animations hold the tail higher).

Run pet_finish.py afterwards (--pivot keep, no --size): it puts every new face on its swatch colour.

Every run starts again from the uncut model, kept in the file as "<name>_uncut_source" (hidden), so the numbers
below can be changed and the script run again. Metres; the bird faces -Y and its left is +X.
What it leaves on the object for the rig script (custom properties): kn_shoulder_L/R, kn_wing_tip_L/R,
kn_wing_fold (the turn that folds a wing back, as a quaternion), kn_jaw_hinge, kn_ankle_L/R, kn_foot_L/R, kn_drop;
vertex groups wing.L, wing.R, jaw, eye, eyelid.L, eyelid.R, foot.L, foot.R.
"""
import bmesh
import bpy
from mathutils import Matrix, Vector

NAME = "SM_Pet_Emberwing_S1"
FLANK = 0.060               # how far from the middle the upright cut runs (the body is this wide at the chest)
WING_FLOOR = 0.055          # nothing below this height is wing (the perch and the feet are wider than the body)
# All of these are in metres on the uncut model, before the bird is set down; it faces -Y.
# The mouth line as the artist drew it on the side of the beak: (y, z) low on the tip, and at the corner of the mouth.
MOUTH = {"tip": (-0.214, 0.3225), "corner": (-0.172, 0.3365)}
# The box the lower beak lies in. "joined": how much of it in front of the corner stays attached (the hinge).
# "lip": edges nearer the mouth line than this are the lips.
BEAK = {"half_width": 0.030, "front": -0.2075, "low": 0.312, "high": 0.345, "joined": 0.004, "lip": 0.006}
# "front": the lower beak ends here, behind and under the hooked point of the upper beak, which hangs in front of it.
# One eye, on each side of the head: where its middle is, how long and how high the almond is, how far its back end
# is tipped up, how far it stands off the head, and how far round it the scan's own eye marks are cleared.
EYE = {"y": -0.1615, "z": 0.3665, "length": 0.027, "height": 0.0105, "tilt": 28.0, "lift": 0.0022, "clear": 0.02}
# Where the loose scraps behind the crest are.
CREST = {"y": (-0.075, -0.020), "z": (0.340, 0.400), "half_width": 0.03}
ANKLE = 0.066               # the legs are cut off at this height; the perch and the feet are all below it
FEET_BEHIND = 0.035         # ... and in front of this (y): the tail hangs lower than the ankles, further back
FOOT_MATERIAL = "Colour 02"  # the legs' own dark colour
# The foot, on the artist's outline. Seen from above, going round: (how far out to the side, how far forward of the
# ankle), and what each point is. Heights are above the sole.
FOOT = {
    # Three toes forward, a little splayed and parted more deeply than at first, and the short back toe
    # (the hallux) every perching bird stands on: the usual layout of a bird's foot.
    "outline": [(0.000, -0.031), (0.0055, -0.016), (0.013, -0.013), (0.018, -0.001), (0.0205, 0.016), (0.0225, 0.031),
                (0.0235, 0.058), (0.0105, 0.033), (0.000, 0.066), (-0.0105, 0.033), (-0.0235, 0.058),
                (-0.0225, 0.031), (-0.0205, 0.016), (-0.018, -0.001), (-0.013, -0.013), (-0.0055, -0.016)],
    "kind": ["tip", "heel", "heel", "side", "side", "side", "tip", "notch", "tip", "notch", "tip", "side", "side", "side",
             "heel", "heel"],
    "narrow": 0.74,                     # the whole foot is this much narrower than the outline above
    "length": 0.066, "centre": 0.016,
    "high": (0.013, 0.0035),            # how thick the foot is at the ankle, and at the toe tips
    "instep": 0.020,                    # where the shank meets the top of the foot
    "shank": 0.034,                     # ankle cut to sole
    "shank_r": 0.0085, "forward": 0.004,
}
LID_SIZE = (1.14, 1.3)      # the eyelid, as a share of the eye's length and height
LID_FLAT = 0.03             # how flat the eyelid is squashed while the eye is open
LID_OUT = 0.0011            # how far it lies over the eye
MARK_BACK = 0.0012          # how deep the back of a closed surface mark (a feather mark, an eye) is tucked in


def components(faces):
    left, out = set(faces), []
    while left:
        stack = [left.pop()]
        group = [stack[0]]
        while stack:
            cur = stack.pop()
            for e in cur.edges:
                for n in e.link_faces:
                    if n in left:
                        left.discard(n)
                        stack.append(n)
                        group.append(n)
        out.append(group)
    return out


def main():
    ob = bpy.data.objects[NAME]
    keep = bpy.data.objects.get(NAME + "_uncut_source")
    if keep is None:
        keep = ob.copy()
        keep.data = ob.data.copy()
        keep.name = NAME + "_uncut_source"
        keep.data.name = NAME + "_uncut_source"
        bpy.context.scene.collection.objects.link(keep)
    else:
        old = ob.data
        ob.data = keep.data.copy()
        ob.data.name = NAME
        if old.users == 0:
            bpy.data.meshes.remove(old)
    keep.hide_set(True)
    keep.hide_render = True
    for g in list(ob.vertex_groups):
        ob.vertex_groups.remove(g)
    for m in list(ob.modifiers):
        ob.modifiers.remove(m)
    ob.parent = None
    me = ob.data
    uncut_lo = Vector([min(v.co[i] for v in me.vertices) for i in range(3)])
    uncut_hi = Vector([max(v.co[i] for v in me.vertices) for i in range(3)])

    bm = bmesh.new()
    bm.from_mesh(me)
    report = {}
    wing_verts, shoulders, tips = {}, {}, {}

    for side, sign in (("L", 1.0), ("R", -1.0)):
        # ---- 1. the cut: an upright plane along the flank, through everything above the perch on that side
        geom = [f for f in bm.faces if any(sign * v.co.x > FLANK - 0.02 and v.co.z > WING_FLOOR for v in f.verts)]
        geom = list({e for f in geom for e in f.edges} | {v for f in geom for v in f.verts} | set(geom))
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=(sign * FLANK, 0, 0), plane_no=(sign, 0, 0))
        outer = [f for f in bm.faces if sign * f.calc_center_median().x > FLANK and f.calc_center_median().z > WING_FLOOR]
        parts = components(outer)
        wing = max(parts, key=len)                      # the wing itself; small marks lying on it come along
        wing_set = set(wing)
        wing_points = [v.co.copy() for v in {v for f in wing for v in f.verts}]
        for part in parts:
            if part is not wing and len(part) < 40:
                mid = sum((f.calc_center_median() for f in part), Vector()) / len(part)
                if min((mid - c).length for c in wing_points) < 0.012:      # only marks that really lie on the wing
                    wing_set.update(part)
        seam = [e for e in bm.edges if len(e.link_faces) == 2
                and (e.link_faces[0] in wing_set) != (e.link_faces[1] in wing_set)]
        bmesh.ops.split_edges(bm, edges=seam)

        # ---- 2. close both sides. After the split every open edge on the cut plane belongs to one side or the other.
        capped = 0
        for of_wing in (True, False):
            rim = [e for e in bm.edges if len(e.link_faces) == 1
                   and all(abs(sign * v.co.x - FLANK) < 1e-4 for v in e.verts)
                   and (e.link_faces[0] in wing_set) == of_wing]
            if not rim:
                continue
            paint = max({f.material_index for e in rim for f in e.link_faces},
                        key=lambda m: sum(1 for e in rim for f in e.link_faces if f.material_index == m))
            filled = bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=rim)
            for f in (g for g in filled["geom"] if isinstance(g, bmesh.types.BMFace)):
                f.material_index = paint
                f.smooth = False
                if of_wing:
                    wing_set.add(f)
                capped += 1
        verts = {v for f in wing_set for v in f.verts}
        wing_verts[side] = verts

        # ---- 3. spread it. The wing's length runs from the shoulder to the tip of its longest feather.
        on_cut = [v.co for v in verts if abs(sign * v.co.x - FLANK) < 1e-4]
        top = max(v.z for v in on_cut)
        shoulder = sum((v for v in on_cut if v.z > top - 0.04), Vector()) / max(sum(1 for v in on_cut if v.z > top - 0.04), 1)
        tip = max((v.co for v in verts), key=lambda c: (c - shoulder).length).copy()
        along = (tip - shoulder).normalized()
        out_dir = Vector((sign, 0, 0))
        face = (out_dir - along * out_dir.dot(along)).normalized()       # the wing's outer face, square to its length
        chord = along.cross(face)
        was = Matrix((along, face, chord)).transposed()                   # columns: where the wing's own axes point now
        new_along, new_face = Vector((sign, 0, 0)), Vector((0, 0, 1))
        # A folded wing is not a spread wing turned round: the hand of a real wing swings out the other way as
        # it opens. Turned as one stiff piece, this wing would lie with its feathers pointing forward. So it is
        # turned and flipped front to back: outer face up, straight edge leading, feathers trailing.
        now = Matrix((new_along, new_face, -new_along.cross(new_face))).transposed()
        turn = now @ was.inverted()
        fold_turn = was @ Matrix((new_along, new_face, new_along.cross(new_face))).transposed().inverted()
        for v in verts:
            v.co = shoulder + turn @ (v.co - shoulder)
        shoulders[side] = shoulder.copy()
        tips[side] = shoulder + turn @ (tip - shoulder)
        if side == "L":
            report["fold"] = fold_turn.to_quaternion()
        report[side] = (len(wing_set), capped, (tip - shoulder).length)

    # ---- 4a. the head. Loose scraps the conversion left behind the crest are removed; the scan's eye marks
    #       (a few dark scraps and faces) are removed and each side gets one clean almond-shaped eye, drawn
    #       to the artist's outline: low at the front, rising towards the back.
    dark_slots = {i for i, m in enumerate(me.materials) if m and m.name.rsplit(" ", 2)[-2:] in (["Colour", "02"], ["Colour", "05"])}
    brown = max((i for i in range(len(me.materials)) if i not in dark_slots),
                key=lambda i: sum(1 for f in bm.faces if f.material_index == i and f.calc_center_median().z > 0.33))
    eye_mid = Vector((0, EYE["y"], EYE["z"]))

    def near_eye(co, reach):
        return abs(co.x) > 0.012 and (Vector((0, co.y, co.z)) - eye_mid).length < reach

    body_faces = max(components(list(bm.faces)), key=len)
    main = set(body_faces)
    scraps = 0
    for part in components([f for f in bm.faces if f not in main]):
        mid = sum((f.calc_center_median() for f in part), Vector()) / len(part)
        behind_crest = (CREST["y"][0] < mid.y < CREST["y"][1] and CREST["z"][0] < mid.z < CREST["z"][1]
                        and abs(mid.x) < CREST["half_width"])
        if len(part) < 60 and (behind_crest or near_eye(mid, EYE["clear"])):
            bmesh.ops.delete(bm, geom=part, context="FACES")
            scraps += 1
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    for f in bm.faces:
        if f.material_index in dark_slots and near_eye(f.calc_center_median(), EYE["clear"]):
            f.material_index = brown

    from mathutils.bvhtree import BVHTree
    import math
    skin = BVHTree.FromBMesh(bm)
    eye_slot = next((i for i, m in enumerate(me.materials) if m and m.name == NAME + " Eye"), None)
    if eye_slot is None:
        mat = bpy.data.materials.get(NAME + " Eye") or bpy.data.materials.new(NAME + " Eye")
        mat.diffuse_color = (0, 0, 0, 1)
        mat.use_nodes = True
        shader = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if shader:
            shader.inputs["Base Color"].default_value = (0, 0, 0, 1)
            shader.inputs["Roughness"].default_value = 1.0
        me.materials.append(mat)
        eye_slot = len(me.materials) - 1
    tilt = math.radians(EYE["tilt"])
    long_axis = Vector((0, math.cos(tilt), math.sin(tilt)))          # towards the back of the head and up
    short_axis = Vector((0, -math.sin(tilt), math.cos(tilt)))
    # The two eyes are exact mirror images of one another. The head itself is not quite the same on both
    # sides, so each point of the eye is put as far out as the fuller side needs, on both sides.
    eye_verts = []
    steps = 12
    offsets = []
    for i in range(steps):
        t = 2 * math.pi * i / steps
        along = math.cos(t) * EYE["length"] / 2                   # an almond: pointed at both ends of its long axis
        across = math.sin(t) * EYE["height"] / 2 * (1 - abs(math.cos(t)) ** 2.2)
        offsets.append(long_axis * along + short_axis * across)
    offsets.append(Vector())                                      # the middle

    def how_far_out(sign, offset):
        hit = skin.ray_cast(eye_mid + offset + Vector((sign * 0.3, 0, 0)), Vector((-sign, 0, 0)))[0]
        return abs(hit.x) if hit is not None else 0.03

    out = [max(how_far_out(1.0, o), how_far_out(-1.0, o)) + EYE["lift"] for o in offsets]
    out[-1] += 0.0008
    for sign in (1.0, -1.0):
        made = [bm.verts.new(Vector((sign * x, eye_mid.y + o.y, eye_mid.z + o.z))) for o, x in zip(offsets, out)]
        ring, middle = made[:-1], made[-1]
        for i in range(steps):
            f = bm.faces.new((middle, ring[i], ring[(i + 1) % steps]) if sign > 0 else (middle, ring[(i + 1) % steps], ring[i]))
            f.material_index = eye_slot
            f.smooth = False
        eye_verts += ring + [middle]

    # Eyelids, so it can blink. Each is a lid the colour of the head, a little larger than the eye, lying just
    # over it. In the model it is squashed flat along the eye's upper edge, where it cannot be seen; its bone
    # pulls it down over the eye (the bone scales it along its own length, by 1 / LID_FLAT to shut it).
    lid_verts = {"L": [], "R": []}
    lid_top = short_axis * (EYE["height"] / 2 * LID_SIZE[1])
    for side, sign in (("L", 1.0), ("R", -1.0)):
        made = []
        for o, x in zip(offsets, out):
            along, across = o.dot(long_axis) * LID_SIZE[0], o.dot(short_axis) * LID_SIZE[1]
            flat = long_axis * along + lid_top + short_axis * ((across - EYE["height"] / 2 * LID_SIZE[1]) * LID_FLAT)
            made.append(bm.verts.new(Vector((sign * (x + LID_OUT), eye_mid.y + flat.y, eye_mid.z + flat.z))))
        ring, middle = made[:-1], made[-1]
        for i in range(steps):
            f = bm.faces.new((middle, ring[i], ring[(i + 1) % steps]) if sign > 0 else (middle, ring[(i + 1) % steps], ring[i]))
            f.material_index = brown
            f.smooth = False
        lid_verts[side] = made
        report["lid_top_" + side] = Vector((sign * (out[-1] + LID_OUT), eye_mid.y + lid_top.y, eye_mid.z + lid_top.z))

    # ---- 4b. the beak, on the artist's line. Seen from the side the mouth runs from low on the tip of the beak
    #       up and back to the corner of the mouth. Everything dark under that line is the lower beak; the dark
    #       faces above it at the tip stay with the upper beak. The beak is cut along the line, the lower beak
    #       freed round the front and along both sides, left joined at the back, and the roof of the mouth and
    #       the floor of the lower beak are closed black.
    (y0, z0), (y1, z1) = MOUTH["tip"], MOUTH["corner"]
    line = Vector((0, y1 - y0, z1 - z0)).normalized()
    up = Vector((1, 0, 0)).cross(line)
    if up.z < 0:
        up = -up
    origin = Vector((0, y0, z0))
    hinge = Vector((0, y1, z1))

    def in_beak(co):
        return (abs(co.x) < BEAK["half_width"] and BEAK["front"] < co.y < y1
                and BEAK["low"] < co.z < BEAK["high"])

    reach = [f for f in bm.faces if f.material_index != eye_slot and any(in_beak(v.co) for v in f.verts)]
    geom = list({e for f in reach for e in f.edges} | {v for f in reach for v in f.verts} | set(reach))
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=origin, plane_no=up)
    lower = [f for f in bm.faces if f.material_index in dark_slots and in_beak(f.calc_center_median())
             and (f.calc_center_median() - origin).dot(up) < 0]
    parts = components(lower)
    lower = set(max(parts, key=len)) if parts else set()
    for part in parts:
        if len(part) >= 2:
            lower.update(part)
    seam = [e for e in bm.edges if len(e.link_faces) == 2
            and (e.link_faces[0] in lower) != (e.link_faces[1] in lower)
            and (e.verts[0].co.y + e.verts[1].co.y) / 2 < y1 - BEAK["joined"]
            and all((v.co - origin).dot(up) > -BEAK["lip"] for v in e.verts)]
    black = next((i for i, m in enumerate(me.materials) if m and m.name == NAME + " Mouth"), None)
    if black is None:
        mat = bpy.data.materials.get(NAME + " Mouth") or bpy.data.materials.new(NAME + " Mouth")
        mat.diffuse_color = (0, 0, 0, 1)
        mat.use_nodes = True
        shader = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if shader:
            shader.inputs["Base Color"].default_value = (0, 0, 0, 1)
            shader.inputs["Roughness"].default_value = 1.0
        me.materials.append(mat)
        black = len(me.materials) - 1
    jaw_faces = 0
    jaw = set()
    if seam and lower:
        bmesh.ops.split_edges(bm, edges=seam)
        lips = {v for e in bm.edges if len(e.link_faces) == 1 for v in e.verts
                if in_beak(v.co) and abs((v.co - origin).dot(up)) < BEAK["lip"]}
        for v in lips:                                      # both lips exactly on the line: shut, they meet
            v.co -= up * (v.co - origin).dot(up)
        for of_lower in (True, False):
            rim = [e for e in bm.edges if len(e.link_faces) == 1 and (e.link_faces[0] in lower) == of_lower
                   and all(v in lips for v in e.verts)]
            if not rim:
                continue
            ends = [v for v in {v for e in rim for v in e.verts} if sum(1 for e in v.link_edges if e in rim) == 1]
            if len(ends) == 2:
                rim.append(bm.edges.get(ends) or bm.edges.new(ends))
            filled = bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=rim)
            for f in (g for g in filled["geom"] if isinstance(g, bmesh.types.BMFace)):
                f.material_index = black
                f.smooth = False
                jaw_faces += 1
                if of_lower:
                    lower.add(f)
        jaw = {v for f in lower for v in f.verts}

    # ---- 5. the feet. The twig and the feet clenched round it are cut away at the ankle. Each leg is carried
    #      on down as a shank into a foot built on the artist's outline: a rounded heel, a broad pad, and three
    #      pointed toes with a notch between each, flat on the ground. The foot grows out of the leg's own cut
    #      edge, vertex to vertex, so leg, shank and foot are one closed surface with no seam and no gap. It
    #      has the legs' own colour.
    import math

    dark = next(i for i, m in enumerate(me.materials) if m and m.name == NAME + " " + FOOT_MATERIAL)
    low = [f for f in bm.faces if any(v.co.z < ANKLE for v in f.verts) and all(v.co.y < FEET_BEHIND for v in f.verts)]
    geom = list({e for f in low for e in f.edges} | {v for f in low for v in f.verts} | set(low))
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=(0, 0, ANKLE), plane_no=(0, 0, 1), clear_inner=True)
    stray = [v for v in bm.verts if v.co.z < ANKLE - 1e-5 and v.co.y < FEET_BEHIND]
    if stray:
        bmesh.ops.delete(bm, geom=stray, context="VERTS")
    bmesh.ops.remove_doubles(bm, verts=[v for v in bm.verts if abs(v.co.z - ANKLE) < 1e-5], dist=0.0004)
    rim = [e for e in bm.edges if len(e.link_faces) == 1 and all(abs(v.co.z - ANKLE) < 1e-5 for v in e.verts)]

    def loops_of(edges):
        left, out = set(edges), []
        while left:
            ring = [left.pop()]
            grew = True
            while grew:
                grew = False
                for e in list(left):
                    if any(v in e.verts for r in ring for v in r.verts):
                        ring.append(e)
                        left.discard(e)
                        grew = True
            out.append(ring)
        return out

    def stitch(ring_a, ring_b, centre, slot):
        """Join two rings of vertices that both go round the same upright line, with triangles."""
        def angle(v):
            return math.atan2(v.co.y - centre.y, v.co.x - centre.x)
        a_, b_ = sorted(ring_a, key=angle), sorted(ring_b, key=angle)
        i = j = 0
        made = []
        while i < len(a_) or j < len(b_):
            a0, b0 = a_[i % len(a_)], b_[j % len(b_)]
            a1, b1 = a_[(i + 1) % len(a_)], b_[(j + 1) % len(b_)]
            step_a = (i + 1) / len(a_) if i < len(a_) else 9.0
            step_b = (j + 1) / len(b_) if j < len(b_) else 9.0
            tri = (a0, a1, b0) if step_a <= step_b else (a0, b1, b0)
            if step_a <= step_b:
                i += 1
            else:
                j += 1
            if len(set(tri)) == 3 and bm.faces.get(tri) is None:
                f = bm.faces.new(tri)
                f.material_index = slot
                f.smooth = False
                made.append(f)
        return made

    foot_verts, sole_z = {}, ANKLE - FOOT["shank"]
    for side, sign in (("L", 1.0), ("R", -1.0)):
        rings = [r for r in loops_of([e for e in rim if sign * (e.verts[0].co.x + e.verts[1].co.x) > 0])]
        if not rings:
            continue
        leg = max(rings, key=lambda r: sum(e.calc_length() for e in r))
        for other in rings:                                 # any small extra opening at the cut is simply closed
            if other is not leg:
                for f in bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=other)["geom"]:
                    if isinstance(f, bmesh.types.BMFace):
                        f.material_index = dark
        leg_ring = list({v for e in leg for v in e.verts})
        mid = sum((v.co for v in leg_ring), Vector()) / len(leg_ring)
        base = Vector((mid.x, mid.y - FOOT["forward"], sole_z))      # the point of the sole under the ankle

        def at(x, f, z):                                    # x: towards the bird's outside, f: forward
            return Vector((base.x + sign * x, base.y - f, sole_z + z))

        new = []

        def vert(co):
            v = bm.verts.new(co)
            new.append(v)
            return v

        outline = [(x * FOOT["narrow"], f) for x, f in FOOT["outline"]]
        bottom = [vert(at(x, f, 0.0)) for x, f in outline]
        top = []
        for (x, f), kind in zip(outline, FOOT["kind"]):
            far = max(0.0, min(1.0, f / FOOT["length"]))
            z = FOOT["high"][0] + (FOOT["high"][1] - FOOT["high"][0]) * far
            if kind == "notch":
                z *= 0.40                                   # the top dips between the toes: that is what parts them
            if kind == "tip":
                z = FOOT["high"][1] * 0.8                   # a toe ends thin, in a claw-like point
            if kind == "tip":                               # a tip stays on its toe's middle line
                top.append(vert(at(x, f - (0.002 if f > 0 else -0.002), z)))
            else:
                top.append(vert(at(x * 0.86, FOOT["centre"] + (f - FOOT["centre"]) * 0.86, z)))
        ankle_ring = [vert(at(FOOT["shank_r"] * math.cos(2 * math.pi * k / 8), FOOT["shank_r"] * math.sin(2 * math.pi * k / 8) * 1.15,
                              FOOT["instep"])) for k in range(8)]
        # a ridge down the middle of each toe, from its knuckle to its tip: the knuckle sits on the toe's own
        # middle line (half-way between the two points either side of the toe), so every toe runs straight
        knuckles = []
        for i, ((x, f), kind) in enumerate(zip(outline, FOOT["kind"])):
            if kind != "tip":
                continue
            (bx, bf), (ax, af) = outline[i - 1], outline[(i + 1) % len(outline)]
            base_x, base_f = (bx + ax) / 2, (bf + af) / 2
            kx, kf = base_x + (x - base_x) * 0.12, base_f + (f - base_f) * 0.12
            far = max(0.0, min(1.0, kf / FOOT["length"]))
            knuckles.append(vert(at(kx, kf, (FOOT["high"][0] + (FOOT["high"][1] - FOOT["high"][0]) * far) * 1.5)))
        n = len(outline)
        made = []
        for i in range(n):                                  # the edge of the foot
            made.append(bm.faces.new((bottom[i], bottom[(i + 1) % n], top[(i + 1) % n], top[i])))
        under = vert(at(0.0, FOOT["centre"], 0.0))
        for i in range(n):                                  # the sole
            made.append(bm.faces.new((under, bottom[(i + 1) % n], bottom[i])))
        for f in made:
            f.material_index = dark
            f.smooth = False
        # the top of the foot: from its edge up over the toe ridges to the instep, then the shank up to the leg
        inner = [v for v, kind in zip(top, FOOT["kind"]) if kind != "tip"] + knuckles
        toe_ridges = [(v, k) for v, k in zip([t for t, kind in zip(top, FOOT["kind"]) if kind == "tip"], knuckles)]
        for tip_v, knuckle in toe_ridges:                         # each toe: a ridge from the knuckle out to the tip
            i = top.index(tip_v)
            before, after = top[(i - 1) % n], top[(i + 1) % n]
            for tri in ((before, tip_v, knuckle), (tip_v, after, knuckle)):
                f = bm.faces.new(tri)
                f.material_index = dark
                f.smooth = False
        stitch(inner, ankle_ring, at(0.0, 0.0, 0.0), dark)
        stitch(ankle_ring, leg_ring, at(0.0, 0.0, 0.0), dark)
        foot_verts[side] = new
        report["ankle_" + side] = Vector((mid.x, mid.y, ANKLE))
        report["hub_" + side] = at(0.0, FOOT["centre"], 0.0)

    # ---- 5b. no open edges anywhere. Small flat pieces that lie on the surface (the feather marks on the wings,
    #      the eyes) are closed with a shallow back, tucked in under them; any other opening is filled.
    bm.normal_update()
    main_parts = components(list(bm.faces))
    closed_marks = 0
    for part in main_parts:
        part_edges = {e for f in part for e in f.edges}
        open_rim = [e for e in part_edges if len(e.link_faces) == 1]
        if not open_rim or len(part) > 150:
            continue
        inward = -sum((f.normal * f.calc_area() for f in part), Vector())
        inward = inward.normalized() if inward.length > 1e-12 else Vector((0, 0, -1))
        members = [grp for grp in list(wing_verts.values()) + [jaw] if any(v in grp for f in part for v in f.verts)]
        is_eye = any(v in eye_verts for f in part for v in f.verts)
        lid_side = next((k for k, found in lid_verts.items() if any(v in found for f in part for v in f.verts)), None)
        if lid_side:
            inward = Vector((-1.0 if lid_side == "L" else 1.0, 0, 0))
        if is_eye:                                          # an eye's back goes towards the middle of the head, whatever its faces say
            side_x = sum(f.calc_center_median().x for f in part)
            inward = Vector((-1.0 if side_x > 0 else 1.0, 0, 0))
        for ring in loops_of(open_rim):
            pts = list({v for e in ring for v in e.verts})
            apex = bm.verts.new(sum((v.co for v in pts), Vector()) / len(pts) + inward * (MARK_BACK * (0.3 if lid_side else 1.0)))
            if lid_side:
                lid_verts[lid_side].append(apex)
            for grp in members:
                grp.add(apex)
            if is_eye:
                eye_verts.append(apex)
            for e in ring:
                f = bm.faces.new((e.verts[0], e.verts[1], apex))
                f.material_index = part[0].material_index
                f.smooth = False
            closed_marks += 1
    leftover = [e for e in bm.edges if len(e.link_faces) == 1]
    if leftover:
        bmesh.ops.holes_fill(bm, edges=leftover, sides=0)
    leftover = [e for e in bm.edges if len(e.link_faces) == 1]
    if leftover:
        bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=leftover)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    report["closed_marks"] = closed_marks

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.verts.index_update()
    foot_index = {side: [v.index for v in verts] for side, verts in foot_verts.items()}
    eye_index = [v.index for v in eye_verts if v.is_valid]
    lid_index = {side: [v.index for v in found if v.is_valid] for side, found in lid_verts.items()}
    index = {side: [v.index for v in verts] for side, verts in wing_verts.items()}
    jaw_index = [v.index for v in jaw]
    report["open_all"] = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    bm.to_mesh(me)
    bm.free()
    me.update()

    # 6. set the bird down on its feet. The tail hangs lower than the feet did on the twig, so in this spread
    #    rest pose its tip is under the ground; the animations carry the tail higher (TAIL_UP in the rig script).
    drop = min(me.vertices[i].co.z for found in foot_index.values() for i in found)
    down = Vector((0, 0, drop))
    for v in me.vertices:
        v.co.z -= drop
    me.update()
    hinge = hinge - down
    for side in ("L", "R"):
        shoulders[side] = shoulders[side] - down
        tips[side] = tips[side] - down
        report["ankle_" + side] = report["ankle_" + side] - down
        report["hub_" + side] = report["hub_" + side] - down
    ob["kn_drop"] = drop
    for side in ("L", "R"):
        ob.vertex_groups.new(name="wing." + side).add(index[side], 1.0, "REPLACE")
        ob["kn_shoulder_" + side] = list(shoulders[side])
        ob["kn_wing_tip_" + side] = list(tips[side])
    ob.vertex_groups.new(name="jaw").add(jaw_index, 1.0, "REPLACE")
    ob.vertex_groups.new(name="eye").add(eye_index, 1.0, "REPLACE")
    for side, found in lid_index.items():
        ob.vertex_groups.new(name="eyelid." + side).add(found, 1.0, "REPLACE")
        ob["kn_lid_top_" + side] = list(report["lid_top_" + side] - down)
    ob["kn_lid_down"] = list(-short_axis)
    ob["kn_lid_length"] = EYE["height"] * LID_SIZE[1]
    ob["kn_lid_shut"] = 1.0 / LID_FLAT
    for side, found in foot_index.items():
        ob.vertex_groups.new(name="foot." + side).add(found, 1.0, "REPLACE")
        ob["kn_ankle_" + side] = list(report["ankle_" + side])
        ob["kn_foot_" + side] = list(report["hub_" + side])
    ob["kn_wing_fold"] = list(report["fold"])
    ob["kn_jaw_hinge"] = list(hinge)
    ob["kn_mouth_line"] = list(line)
    lowest = min(v.co.z for v in me.vertices)
    print("EMBERWING feet: %d built on the outline; bird set down by %.1f mm; lowest point now %.4f m; surface marks closed: %d; "
          "open edges left in the whole bird: %d" % (len(foot_index), drop * 1000, lowest, report["closed_marks"], report["open_all"]))
    print("EMBERWING cut: wing L %d faces (%d cap faces, %.3f m long), wing R %d faces (%d cap faces, %.3f m long); "
          "mouth faces filled %d, lower beak vertices %d; triangles now %d; size %s"
          % (report["L"][0], report["L"][1], report["L"][2], report["R"][0], report["R"][1], report["R"][2],
             jaw_faces, len(jaw_index), sum(len(p.vertices) - 2 for p in me.polygons), tuple(round(d, 3) for d in ob.dimensions)))


main()
