"""Emberwing: rig and animations. Run after build_emberwing.py (which cuts the wings and the beak free).

  run_blender.py --blend SM_Pet_Emberwing_S1.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/Emberwing/rig_emberwing.py

Bones
  root                  the whole bird
  foot.L, foot.R        each foot, turning at the ankle; they hang from root, so the feet stay planted while
                        the body moves, and the legs stretch a little in between
  body                  hips to chest; the upper legs stretch a little between it and the perch
  neck, head, jaw       the jaw's hinge is the corner of the mouth
  tail, tail.001
  wing.L, wing.L.001 ... wing.L.004 (and .R)   upper arm, forearm, hand, and two sections of wing tip: the arm of
                        the wing, laid out like a bird's. Each can rise and fall, sweep back in the wing's own
                        plane (how a wing folds) and twist about its length (how a wing bites the air)
  feather.L, feather.L.001 ... .003 (and .R)   the flight feathers: the trailing half of the wing behind each of the
                        first four sections (inner feathers, secondaries on the forearm, primaries on the hand).
                        They bend up and down behind the arm, so the trailing edge follows a beat late and the
                        wing is not a stiff board

The rest pose is the spread pose build_emberwing.py makes. FOLD is the turn of a wing's first bone that lays
the wing back along the body; the perched animations hold it.

Actions, 24 frames a second
  Idle        loop, 96 frames: perched, wings folded, breathing, looking about, the tail shifting
  Move        loop, 80 frames: the movement loop. Level in the air, beak ahead, feet up, flapping; in the third
              of its four beats it folds its wings and rolls once round its own length
  MoveStart   20 frames: from Idle's first frame to Move's first frame
  MoveStop    22 frames: from Move's first frame back to Idle's first frame
  (every action blinks)
  Fly         loop, 60 frames (three beats): a broad downstroke at full stretch, a tight half-folded upstroke, each section
              and then the feathers following a little later than the one before; the body rides the beat
  Squawk      44 frames, from perched: leans in, beak wide, wings flared, back
  BarrelRoll  53 frames, from the spread pose (in the air): tips level, nose a little down, wings and feet tucked,
              beak straight ahead, two rolls about its own length gathering speed, then opens out again
  WingSpread  74 frames, from perched: both wings swing out in one movement, rise a little past level,
              settle, hold with a slow flex, and fold away

Safe to run again: the rig and the actions are rebuilt.
"""
import math

import bpy
from mathutils import Quaternion, Vector

NAME = "SM_Pet_Emberwing_S1"
SPINE = {                               # bone: (head, tail), metres; the bird faces -Y
    "body": ((0.0, 0.035, 0.135), (0.0, -0.035, 0.265)),
    "neck": ((0.0, -0.035, 0.265), (0.0, -0.085, 0.325)),
    "head": ((0.0, -0.085, 0.325), (0.0, -0.125, 0.385)),
    "tail": ((0.0, 0.085, 0.165), (0.0, 0.150, 0.095)),
    "tail.001": ((0.0, 0.150, 0.095), (0.0, 0.212, 0.020)),
}
GAPE = 24.0                             # degrees the lower beak drops when the beak is wide open
MOVE_DOWN = 4.0                         # degrees below level the bird points while it moves
DIVE_DOWN = 9.0                         # degrees below level the bird points in its dive
TAIL_UP = 20.0                          # degrees the tail is carried above its sculpted droop, so it clears the ground
SECTIONS = 5                            # bones along each wing
FEATHERS = 4                            # of those, how many (from the shoulder out) carry a feather bone
FEATHER_FROM = 0.34                     # how far back across the wing the feathers start (0 leading edge, 1 trailing)
FEATHER_BLEND = 0.40                    # ... and over how much of the wing's width the arm hands over to them
JOINTS = (0.20, 0.42, 0.62, 0.81)             # where along the wing the elbow, the wrist and the tip joint are
LEG_BLEND = (0.068, 0.135)              # below the first height a leg belongs to the perch, above the second to the body


def main():
    scene = bpy.context.scene
    scene.render.fps = 24
    ob = bpy.data.objects[NAME]
    me = ob.data
    for o in list(bpy.data.objects):
        if o.type == "ARMATURE" and o.name == NAME + "_Rig":
            bpy.data.objects.remove(o, do_unlink=True)
    for m in list(ob.modifiers):
        ob.modifiers.remove(m)
    ob.parent = None

    drop = float(ob.get("kn_drop", 0.0))                 # build_emberwing.py set the bird down by this much
    spine = {name: tuple((p[0], p[1], p[2] - drop) for p in seg) for name, seg in SPINE.items()}
    leg_blend = (LEG_BLEND[0] - drop, LEG_BLEND[1] - drop)
    shoulder = {s: Vector(ob["kn_shoulder_" + s]) for s in "LR"}
    tip = {s: Vector(ob["kn_wing_tip_" + s]) for s in "LR"}
    hinge = Vector(ob["kn_jaw_hinge"])
    mouth_line = Vector(ob["kn_mouth_line"])            # from the beak's tip towards the corner of the mouth
    fold_l = Quaternion(ob["kn_wing_fold"])

    groups = {g.name: g for g in ob.vertex_groups}
    chord = {}                                           # each wing's leading and trailing edge (y), in the spread pose

    def members(name):
        g = groups.get(name)
        return {v.index for v in me.vertices if g is not None and any(e.group == g.index for e in v.groups)}

    wing = {s: members("wing." + s) for s in "LR"}
    jaw = members("jaw")
    foot = {s: members("foot." + s) for s in "LR"}
    eyes = members("eye")
    lids = {s: members("eyelid." + s) for s in "LR"}
    lid_top = {s: Vector(ob["kn_lid_top_" + s]) for s in "LR"}
    lid_down, lid_length, LID_SHUT = Vector(ob["kn_lid_down"]), float(ob["kn_lid_length"]), float(ob["kn_lid_shut"])
    ankle = {s: Vector(ob["kn_ankle_" + s]) for s in "LR"}
    foot_mid = {s: Vector(ob["kn_foot_" + s]) for s in "LR"}

    for side_name, found in wing.items():
        ys = [me.vertices[i].co.y for i in found]
        chord[side_name] = (min(ys), max(ys))

    # ---- bones
    arm = bpy.data.armatures.new(NAME + "_Rig")
    rig = bpy.data.objects.new(NAME + "_Rig", arm)
    scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    for o in bpy.context.view_layer.objects:
        o.select_set(o is rig)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.edit_bones

    def bone(name, head, tail, parent=None):
        b = eb.new(name)
        b.head, b.tail = head, tail
        b.roll = 0.0
        if parent:
            b.parent = eb[parent]
        return b

    bone("root", (0, 0, 0), (0, 0.06, 0))
    bone("body", *spine["body"], parent="root")
    bone("neck", *spine["neck"], parent="body")
    bone("head", *spine["head"], parent="neck")
    bone("jaw", hinge, hinge - mouth_line * 0.045, parent="head")
    bone("tail", *spine["tail"], parent="body")
    bone("tail.001", *spine["tail.001"], parent="tail")
    for s in "LR":
        bone(f"eyelid.{s}", lid_top[s], lid_top[s] + lid_down * lid_length, parent="head")
        bone(f"foot.{s}", ankle[s], foot_mid[s], parent="root")
        a, b = shoulder[s], tip[s]
        cuts = (0.0,) + JOINTS + (1.0,)
        for k in range(SECTIONS):
            name = f"wing.{s}" + ("" if k == 0 else f".{k:03d}")
            parent = "body" if k == 0 else f"wing.{s}" + ("" if k == 1 else f".{k - 1:03d}")
            bone(name, a + (b - a) * cuts[k], a + (b - a) * cuts[k + 1], parent=parent)
            if k < FEATHERS:                             # a feather bone runs back from the arm to the trailing edge
                middle = a + (b - a) * (cuts[k] + cuts[k + 1]) / 2
                bone(f"feather.{s}" + ("" if k == 0 else f".{k:03d}"),
                     (middle.x, chord[s][0] + (chord[s][1] - chord[s][0]) * FEATHER_FROM, middle.z),
                     (middle.x, chord[s][1], middle.z), parent=name)
    bpy.ops.object.mode_set(mode="OBJECT")

    # ---- weights
    def smooth(t):
        t = max(0.0, min(1.0, t))
        return t * t * (3 - 2 * t)

    def to_segment(p, seg):
        a, b = Vector(seg[0]), Vector(seg[1])
        ab = b - a
        t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
        return (p - (a + ab * t)).length

    weights = {}
    for v in me.vertices:
        p, i = v.co, v.index
        if i in foot["L"] or i in foot["R"]:
            weights[i] = {"foot.L" if i in foot["L"] else "foot.R": 1.0}
            continue
        side = "L" if i in wing["L"] else "R" if i in wing["R"] else None
        if side:                                         # three stiff sections with a short blend at each joint
            a, b = shoulder[side], tip[side]
            t = (p - a).dot(b - a) / (b - a).length_squared
            steps_in = [smooth((t - j + 0.04) / 0.08) for j in JOINTS]       # how far past each joint this point is
            share = []
            for k in range(SECTIONS):
                x = 1.0
                for j in range(k):
                    x *= steps_in[j]
                if k < SECTIONS - 1:
                    x *= 1.0 - steps_in[k]
                share.append(x)
            across = (p.y - chord[side][0]) / max(chord[side][1] - chord[side][0], 1e-6)
            feather = smooth((across - FEATHER_FROM) / FEATHER_BLEND)       # 0 on the arm, 1 out on the feathers
            w = {}
            for k, x in enumerate(share):
                arm_name = f"wing.{side}" + ("" if k == 0 else f".{k:03d}")
                if k < FEATHERS:
                    w[arm_name] = x * (1 - feather)
                    w[f"feather.{side}" + ("" if k == 0 else f".{k:03d}")] = x * feather
                else:
                    w[arm_name] = x
            weights[i] = w
            continue
        near = sorted((to_segment(p, seg), name) for name, seg in spine.items())[:2]
        closest = max(near[0][0], 1e-5)
        w = {name: (closest / max(d, 1e-5)) ** 4 for d, name in near}
        total = sum(w.values())
        w = {name: x / total for name, x in w.items()}
        if p.y < 0.045 and p.z < leg_blend[1]:           # the legs: with the foot at the ankle, with the body higher up
            up = smooth((p.z - leg_blend[0]) / (leg_blend[1] - leg_blend[0]))
            w = {"foot.L" if p.x > 0 else "foot.R": 1 - up, "body": up}
        weights[i] = w
    # soften the joins of the body (not the wings: they were cut free and must stay free)
    edges = {}
    for e in me.edges:
        a, b = e.vertices
        edges.setdefault(a, []).append(b)
        edges.setdefault(b, []).append(a)
    free = wing["L"] | wing["R"] | foot["L"] | foot["R"] | lids["L"] | lids["R"]
    for _ in range(2):
        new = {}
        for i, w in weights.items():
            if i in free:
                new[i] = w
                continue
            acc = {k: x * 2.0 for k, x in w.items()}
            for n in edges.get(i, []):
                if n in free:
                    continue
                for k, x in weights[n].items():
                    acc[k] = acc.get(k, 0.0) + x
            total = sum(acc.values())
            new[i] = {k: x / total for k, x in acc.items()}
        weights = new
    # the eyes do not bend, and neither does the patch of head they sit on: otherwise the skin moves a little
    # differently from the eye when the head turns and comes through it (the right eye used to vanish)
    eye_points = [me.vertices[i].co.copy() for i in eyes]
    for v in me.vertices:
        if v.index in eyes or (v.index not in free and v.index not in jaw
                               and any((v.co - c).length < 0.022 for c in eye_points)):
            weights[v.index] = {"head": 1.0}
    # the lower beak: stiff at its tip, easing into the head at the hinge
    for i in jaw:
        back = (me.vertices[i].co - hinge).dot(-mouth_line)          # how far forward of the hinge
        k = smooth(back / 0.012)
        w = {name: x * (1 - k) for name, x in weights[i].items()}
        w["jaw"] = k
        weights[i] = w
    for s in "LR":
        for i in lids[s]:
            weights[i] = {f"eyelid.{s}": 1.0}
    for g in list(ob.vertex_groups):
        ob.vertex_groups.remove(g)
    made = {}
    for i, w in weights.items():
        for name, x in w.items():
            if x > 0.005:
                made.setdefault(name, ob.vertex_groups.new(name=name) if name not in made else made[name]).add([i], x, "REPLACE")
    ob.parent = rig
    ob.modifiers.new("Armature", "ARMATURE").object = rig

    # ---- animation helpers. Turns are given in the bird's own directions and turned into each bone's.
    pb = rig.pose.bones
    for b in pb:
        b.rotation_mode = "QUATERNION"
    X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
    last = {}

    def about(axis, degrees):
        return Quaternion(axis, math.radians(degrees))

    def key(name, frame, q=None, move=None):
        b = pb[name]
        rest = arm.bones[name].matrix_local.to_3x3()
        q = q or Quaternion()
        local = Quaternion(rest.inverted() @ Vector(q.axis), q.angle) if q.angle > 1e-7 else Quaternion()
        if name in last and last[name].dot(local) < 0:
            local.negate()
        last[name] = local.copy()
        b.rotation_quaternion = local
        b.keyframe_insert("rotation_quaternion", frame=frame)
        if move is not None:
            b.location = rest.inverted() @ Vector(move)
            b.keyframe_insert("location", frame=frame)

    def start(name):
        old = bpy.data.actions.get(name)
        if old:
            bpy.data.actions.remove(old)
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        rig.animation_data_create()
        rig.animation_data.action = action
        last.clear()
        return action

    def finish(action, end):
        action.use_frame_range = True
        action.frame_start, action.frame_end = 1, end
        rig.animation_data.action = None
        for b in pb:
            b.rotation_quaternion = (1, 0, 0, 0)
            b.location = (0, 0, 0)
            b.scale = (1, 1, 1)

    def blink(at, hold=2):
        """Both eyelids shut and open again, starting at frame `at`. Seven frames or so."""
        for s in "LR":
            for frame, shut in ((at, 1.0), (at + 2, LID_SHUT), (at + 2 + hold, LID_SHUT), (at + 5 + hold, 1.0)):
                pb[f"eyelid.{s}"].scale = (1.0, shut, 1.0)
                pb[f"eyelid.{s}"].keyframe_insert("scale", frame=frame)

    def eyes_open(*frames):
        for s in "LR":
            for frame in frames:
                pb[f"eyelid.{s}"].scale = (1.0, 1.0, 1.0)
                pb[f"eyelid.{s}"].keyframe_insert("scale", frame=frame)

    fold = {"L": fold_l, "R": Quaternion((fold_l.w, fold_l.x, -fold_l.y, -fold_l.z))}      # the right is the mirror image

    def along_wing(values, count):
        """Spread a few values given from shoulder to tip over `count` sections."""
        if not isinstance(values, (tuple, list)):
            return [values] + [0.0] * (count - 1)
        if len(values) == count:
            return list(values)
        out = []
        for k in range(count):
            x = k * (len(values) - 1) / max(count - 1, 1)
            lo = min(int(x), len(values) - 2) if len(values) > 1 else 0
            out.append(values[lo] + (values[min(lo + 1, len(values) - 1)] - values[lo]) * (x - lo))
        return out

    def wings(frame, open_share=0.0, lift=(0, 0, 0, 0), sweep=(0, 0, 0, 0), twist=(0, 0, 0, 0), flex=(0, 0, 0, 0)):
        """open_share: 0 folded .. 1 spread (the upper arm). Per section, shoulder to tip, in degrees:
        lift (up is +), sweep (back, in the wing's own plane, is +), twist (leading edge down is +).
        flex: how far the feathers behind each section are bent up (+) or down (-)."""
        lift, sweep, twist = (along_wing(v, SECTIONS) for v in (lift, sweep, twist))
        flex = along_wing(flex, FEATHERS) if isinstance(flex, (tuple, list)) else [flex] * FEATHERS
        for s, sign in (("L", 1.0), ("R", -1.0)):
            q = Quaternion().slerp(fold[s], 1.0 - open_share)
            for k in range(SECTIONS):
                name = f"wing.{s}" + ("" if k == 0 else f".{k:03d}")
                turn = about(Y, -sign * lift[k]) @ about(Z, sign * sweep[k]) @ about(X, twist[k])
                key(name, frame, turn @ q if k == 0 else turn)
            for k in range(FEATHERS):
                key(f"feather.{s}" + ("" if k == 0 else f".{k:03d}"), frame, about(X, flex[k] * open_share))

    def wave(frame, period, phase=0.0):
        return math.sin(2 * math.pi * (frame - 1) / period + phase)

    # ---- Idle
    a = start("Idle")
    LOOP = 96
    looks = [(1, 0, 0), (14, 0, 0), (20, 38, -4), (38, 38, -4), (46, -30, 6), (62, -30, 6), (70, 8, -10), (80, 8, -10), (97, 0, 0)]
    for frame in range(1, LOOP + 2, 4):
        breath = wave(frame, 48)
        key("body", frame, about(X, 1.2 * breath), move=(0, 0, 0.0015 * breath))
        key("tail", frame, about(X, TAIL_UP + 2.0 * wave(frame, 48, 0.8)) @ about(Z, 2.5 * wave(frame, 96, 0.4)))
        key("tail.001", frame, about(X, 2.5 * wave(frame, 48, 0.3)))
        wings(frame, 0.0, lift=1.2 * breath)
    for frame, yaw, nod in looks:
        key("head", frame, about(Z, yaw * 0.65) @ about(X, nod))
        key("neck", frame, about(Z, yaw * 0.35))
    key("jaw", 1)
    key("jaw", LOOP + 1)
    eyes_open(1, LOOP + 1)
    blink(26)
    blink(66, hold=1)
    blink(72, hold=1)                                    # a double blink
    finish(a, LOOP + 1)

    # ---- Fly. Built on how a wing beat is usually described by animators and riggers:
    #  - the downstroke is the broad, powerful half: the wing at full stretch, pressing down on the air;
    #  - the upstroke is a tight movement that offers the air nothing: elbow and wrist bend, the hand sweeps back
    #    and the wing comes up half folded, then snaps open at the top;
    #  - nothing moves at once: the shoulder leads, each section further out follows a little later, and the
    #    flight feathers trail last of all, bent up by the air on the way down and hanging on the way up;
    #  - the body answers: pressed up by each downstroke, sinking on each upstroke, head held steadier.
    a = start("Fly")
    BEAT = 20
    DOWN = 0.46

    def stroke(u):
        """Where the upper arm is: +1 fully up, -1 fully down."""
        u %= 1.0
        return math.cos(math.pi * u / DOWN) if u < DOWN else -math.cos(math.pi * (u - DOWN) / (1 - DOWN))

    def recover(u):
        """0 on the downstroke, rising to 1 in the middle of the upstroke and back to 0."""
        u %= 1.0
        return 0.0 if u < DOWN else math.sin(math.pi * (u - DOWN) / (1 - DOWN)) ** 1.4

    def pressing(u):
        """+1 while that part of the wing is moving down fastest, -1 while it is moving up fastest."""
        return stroke(u + 0.25)

    LATE = [0.0, 0.05, 0.10, 0.15, 0.20]                  # each section starts its beat this much after the shoulder

    def flap(u):
        """One moment of the wing beat: lift, sweep, twist per section and flex per feather bone."""
        lift, sweep, twist = [], [], []
        for k in range(SECTIONS):
            uk = u - LATE[k]
            give = recover(uk)
            if k == 0:
                lift.append(4 + 40 * stroke(uk))
                sweep.append(-3 + 6 * give)                   # a touch forward on the way down, back on the way up
                twist.append(7 * max(pressing(uk), 0) - 9 * give)
            else:
                bow = (5 + 3 * k) * max(pressing(uk), 0)      # the air bows the outer wing up as it presses down
                lift.append((6 + 2 * k) * stroke(uk) + bow - (9 + 4 * k) * give)
                sweep.append((10 + 9 * k if k < 4 else 26) * give)
                twist.append(2 * max(pressing(uk), 0) - (3 + k) * give)
        flex = [(9 + 4 * k) * pressing(u - LATE[k] - 0.07) - 5 * recover(u - LATE[k]) for k in range(FEATHERS)]
        return lift, sweep, twist, flex

    FLY_BEATS = 3
    for frame in range(1, BEAT * FLY_BEATS + 2):
        u = (frame - 1) / BEAT
        lift, sweep, twist, flex = flap(u)
        wings(frame, 1.0, lift=lift, sweep=sweep, twist=twist, flex=flex)
        ride = -stroke(u - 0.10)
        key("body", frame, about(X, -16 + 3.0 * ride), move=(0, 0, 0.014 * ride))
        key("neck", frame, about(X, 9 - 2.2 * ride))
        key("head", frame, about(X, 8 - 1.4 * ride))
        key("tail", frame, about(X, 10 + 5 * stroke(u - 0.15)))
        key("tail.001", frame, about(X, 6 + 6 * stroke(u - 0.30)))
        key("jaw", frame)
        for s in "LR":                                   # legs tucked back under the tail, riding with the body
            key(f"foot.{s}", frame, about(X, 62), move=(0, 0.014, 0.022 + 0.014 * ride))
    eyes_open(1, BEAT * FLY_BEATS + 1)
    blink(34)
    finish(a, BEAT * FLY_BEATS + 1)

    # ---- Squawk
    a = start("Squawk")
    #        frame lean  gape  flare
    cry = [(1, 0.0, 0.0, 0.0), (6, -0.25, 0.0, 0.05), (12, 1.0, 1.0, 0.45), (15, 0.9, 0.7, 0.40), (18, 1.0, 1.0, 0.45),
           (21, 0.9, 0.65, 0.40), (26, 1.0, 0.95, 0.42), (34, 0.2, 0.0, 0.10), (44, 0.0, 0.0, 0.0)]
    for frame, lean, gape, flare in cry:
        key("body", frame, about(X, -6 * lean))
        key("neck", frame, about(X, -16 * lean))
        key("head", frame, about(X, 22 * lean))
        key("jaw", frame, about(X, GAPE * gape))          # the lower beak drops
        key("tail", frame, about(X, TAIL_UP - 6 * lean))
        wings(frame, flare * 0.5, lift=(10 * flare, -4 * flare, -2 * flare, 0))
    eyes_open(1, 44)
    blink(3)
    blink(31)
    finish(a, 44)

    # ---- WingSpread: one clean movement. Both wings swing out together, the outer sections trailing a touch,
    # rise a little past level, settle, hold with a slow flex, and fold away the same way.
    a = start("WingSpread")
    #       frame  out   lift at the shoulder, then how far each section further out hangs behind (degrees)
    steps = [(1,   0.00, 0,   0),
             (6,   0.00, 2,   0),
             (16,  0.45, 6,   -7),
             (26,  1.00, 12,  -4),
             (31,  1.00, 16,  3),
             (37,  1.00, 9,   0),
             (46,  1.00, 12,  2),
             (55,  1.00, 8,   0),
             (63,  0.50, 5,   -6),
             (70,  0.04, 1,   -1),
             (74,  0.00, 0,   0)]
    for frame, out, lift, hang in steps:
        wings(frame, out, lift=(lift, hang, hang * 0.8, hang * 0.6))
        key("body", frame, about(X, -5 * out), move=(0, 0, 0.004 * out))
        key("neck", frame, about(X, -4 * out))
        key("head", frame, about(X, 9 * out))
        key("tail", frame, about(X, TAIL_UP - 5 * out))
    eyes_open(1, 74)
    blink(9)
    blink(57)
    finish(a, 74)

    # ---- BarrelRoll: a stooping dive. From the spread pose it tips over until it lies level, nose a little down,
    # folds its wings in against its body, draws its feet up, stretches its neck so the beak points straight
    # ahead, and spins about its own length like a bullet, faster and faster; then it throws its wings out and
    # comes upright again.
    a = start("BarrelRoll")
    ROLL = 52
    body_bone = arm.bones["body"]
    long_axis = (body_bone.tail_local - body_bone.head_local).normalized()
    upright = math.degrees(math.atan2(-long_axis.y, long_axis.z))      # how far the body already leans forward
    level = 90.0 + DIVE_DOWN - upright                                 # the turn that lays it level, nose a little down
    pivot = (body_bone.head_local + body_bone.tail_local) / 2
    for frame in range(1, ROLL + 2):
        u = (frame - 1) / ROLL
        tucked = smooth(u / 0.20) * smooth((1.0 - u) / 0.20)           # 0 spread and upright .. 1 level and tucked
        spin = max(0.0, min(1.0, (u - 0.14) / 0.70))
        spin = spin * spin * (1.6 - 0.6 * spin)                        # gathers speed, eases only at the very end
        q = about(X, level * tucked) @ Quaternion(long_axis, math.radians(720.0 * spin))
        key("root", frame, q, move=pivot - q @ pivot)                  # it turns about its own middle
        wings(frame, 1.0 - tucked, lift=(-3 * tucked, 0, 0, 0), flex=0.0)
        key("body", frame)
        key("neck", frame, about(X, -(90.0 - upright) * 0.48 * tucked))   # neck and head in line with the body:
        key("head", frame, about(X, -(90.0 - upright) * 0.52 * tucked))   # the beak points where it is going
        key("jaw", frame)
        key("tail", frame, about(X, 4 * tucked))
        key("tail.001", frame, about(X, 3 * tucked))
        for s in "LR":                                                 # feet drawn up against the belly
            key(f"foot.{s}", frame, about(X, 78 * tucked), move=(0, 0.020 * tucked, 0.034 * tucked))
    eyes_open(1, ROLL + 1)
    blink(4)
    blink(ROLL - 8)
    finish(a, ROLL + 1)

    # ---- The movement set: MoveStart, Move, MoveStop. Moving, the bird lies level in the air, beak straight ahead,
    # feet drawn up, and flaps; once in every loop it folds its wings and rolls right round its own length.
    # MoveStart begins exactly on Idle's first frame and ends exactly on Move's first frame; MoveStop begins on
    # Move's first frame and ends on Idle's first frame. So the game can go Idle -> MoveStart -> Move (looping)
    # -> MoveStop -> Idle with no jump anywhere, as long as it leaves Move at the end of a loop.
    body_bone = arm.bones["body"]
    long_axis = (body_bone.tail_local - body_bone.head_local).normalized()
    upright = math.degrees(math.atan2(-long_axis.y, long_axis.z))
    level = 90.0 + MOVE_DOWN - upright
    pivot = (body_bone.head_local + body_bone.tail_local) / 2
    idle_tail = about(X, TAIL_UP + 2.0 * wave(1, 48, 0.8)) @ about(Z, 2.5 * wave(1, 96, 0.4))     # Idle, frame 1
    idle_tail_end = about(X, 2.5 * wave(1, 48, 0.3))

    def mix(q_idle, q_move, p):
        return q_idle.slerp(q_move, p)

    def move_pose(frame, p, u, tuck=0.0, spin=0.0):
        """p: 0 perched as in Idle .. 1 flying level. u: where in the wing beat. tuck: 0 flapping .. 1 wings
        folded for the roll. spin: degrees rolled about its own length."""
        lift, sweep, twist, flex = flap(u)
        ride = -stroke(u - 0.10) * (1 - tuck)
        q = about(X, level * p) @ Quaternion(long_axis, math.radians(spin))
        key("root", frame, q, move=(pivot - q @ pivot) + Vector((0, 0, 0.012 * ride * p - 0.02 * math.sin(math.pi * p) * (1 - p))))
        key("body", frame)
        back = -(90.0 - upright)
        key("neck", frame, about(X, back * 0.48 * p))
        key("head", frame, about(X, back * 0.52 * p - 1.5 * ride * p))
        key("jaw", frame)
        key("tail", frame, mix(idle_tail, about(X, 4 + 4 * stroke(u - 0.15) * (1 - tuck)), p))
        key("tail.001", frame, mix(idle_tail_end, about(X, 3 + 5 * stroke(u - 0.30) * (1 - tuck)), p))
        for s, sign in (("L", 1.0), ("R", -1.0)):
            key(f"foot.{s}", frame, about(X, 78 * p), move=(0, 0.020 * p, 0.034 * p))
            live = 1 - tuck
            # flying level, the body lies forward, so the wings are turned back up to lie flat on the air
            flying = about(X, -level) @ about(Y, -sign * lift[0]) @ about(Z, sign * sweep[0]) @ about(X, twist[0])
            arm_q = mix(fold[s], mix(flying, fold[s], tuck), p)
            key(f"wing.{s}", frame, arm_q)
            for k in range(1, SECTIONS):
                key(f"wing.{s}.{k:03d}", frame,
                    about(Y, -sign * lift[k] * live * p) @ about(Z, sign * sweep[k] * live * p) @ about(X, twist[k] * live * p))
            for k in range(FEATHERS):
                key(f"feather.{s}" + ("" if k == 0 else f".{k:03d}"), frame, about(X, flex[k] * live * p))

    MOVE_BEATS = 4
    MOVE = BEAT * MOVE_BEATS
    a = start("Move")
    for frame in range(1, MOVE + 2):
        u = (frame - 1) / BEAT
        w = (frame - 1) / MOVE                                         # where in the whole loop
        tuck = smooth((w - 0.50) / 0.07) * smooth((0.80 - w) / 0.07)   # wings in for the roll, in the third beat
        turn = max(0.0, min(1.0, (w - 0.53) / 0.24))
        move_pose(frame, 1.0, u, tuck=tuck, spin=360.0 * turn * turn * (3 - 2 * turn))
    eyes_open(1, MOVE + 1)
    blink(18)
    blink(MOVE - 10)
    finish(a, MOVE + 1)

    START = 20
    a = start("MoveStart")
    for frame in range(1, START + 2):
        t = (frame - 1) / START
        p = smooth(t)
        move_pose(frame, p, (t - 1.0) * 1.0)                           # it is already beating as it leaves; lands on beat 0
    eyes_open(1, START + 1)
    blink(3)
    finish(a, START + 1)

    STOP = 22
    a = start("MoveStop")
    for frame in range(1, STOP + 2):
        t = (frame - 1) / STOP
        p = 1.0 - smooth(t)
        move_pose(frame, p, t * 1.0)
    eyes_open(1, STOP + 1)
    blink(STOP - 8)
    finish(a, STOP + 1)

    scene.frame_set(1)
    print("EMBERWING rigged: %d bones, %d vertices weighted, wings %d + %d vertices, lower beak %d | "
          "actions Idle 96 (loop), Fly 60 (loop), Move 80 (loop), MoveStart 20, MoveStop 22, Squawk 44, WingSpread 74, BarrelRoll 53"
          % (len(arm.bones), len(weights), len(wing["L"]), len(wing["R"]), len(jaw)))


main()
