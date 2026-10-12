"""Kitsune Spirit, the 50 %, 75 % and 100 % forms: their animations, keyed on the Rigify controls.

  run_blender.py --blend SM_Pet_KitsuneSpirit_50.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/KitsuneSpirit/anim_kitsune_stage.py
  (after bind_kitsune_tails.py; the stage is read from the file's name; running it again replaces the clips)

Every form keeps the clips of the form before it (the owner, 12 Oct 2026):

  all three   Idle 192 (loop), Walk 24 (loop), Run 12 (loop), SpinChase 52, Shake 56, Guard 72, Pounce 56:
              the 25 % form's clips, MADE AGAIN here on each rig with the same timings. The body's acting is the
              25 % form's (anim_kitsune_25.py; Walk, Run and SpinChase come from the first form's anim_kitsune.py,
              run on this rig), with every distance scaled to the rig's size; the tails are acted afresh for
              three, six and nine tails.
  all three   FoxfireFlick 60    crouches, the tails wind back, then whip forward over its back one after another
  75, 100     EmberWaltz 96      rears lightly, one full turn on the spot with the tails spread as a wheel, bows
  75, 100     Kindle 108         sits, curls one tail round its flank and turns its head back to it, breathes on the tip, watches it, stands
  100         NinefoldBlaze 132  plants its paws, the tails fold shut, then open one by one from the outside in;
                                 the head is thrown back as the middle tail rises
  100         SpiritStep 48      a dash: crouches, lunges, (is faded out and moved on by Unity), lands and skids

24 frames a second. Idle, Walk and Run loop; the others start and end on the rest pose (EmberWaltz ends one whole
turn round, which is the same pose). Frame cues and effect notes: CUES below, written out in README_Kitsune50-100.txt.

TAILS  tail K's bones are tail_K_1 .. tail_K_5, K from the fox's left to its right. NO TAIL MAY PASS THROUGH ANOTHER
       (the owner, 12 Oct 2026). So at any frame all the tails take the same sway and lift, or very nearly (tails());
       what one tail does on its own is a turn at its root straight away from the middle of the fan (tail_one's
       `out`), the one move that cannot meet a neighbour. tail_clip_check.py counts what still touches.
EYES   the eyelid control is turned about its own Y by kn_lid_turn degrees to shut the eye: lid(frame, 0 .. 1).
FLOW   flowing() puts a slow wave from root to tip on every tail for the length of a clip, so no tail is ever a stick.
"""
import math
import re
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

BASE_RIG = "SM_Pet_KitsuneSpirit_S1_Rig"
KEEP = ("Walk", "Run", "SpinChase")
STUB_TAIL = ("spine.003", "spine.002", "spine.001", "spine")
PAWS_25 = 0.1964             # front paws to hind paws on the 25 % form, metres: distances are scaled from it
WHO = {"FoxfireFlick": ("50", "75", "100"), "EmberWaltz": ("75", "100"), "Kindle": ("75", "100"),
       "NinefoldBlaze": ("100",), "SpiritStep": ("100",)}
CUES = {
    "Idle": "loop 192 (8.00 s). Head: turns left 31-48, holds to 80, turns right 80-104, holds to 132, centre by 152. "
            "Blinks: eyes shut 26-28, 92-94, 158-160. Ear twitches start 60 (left), 116 (right), 168 (left), 170 (right). "
            "The tails wag as a ripple that runs across the fan from its left to its right.",
    "Walk": "loop 24 (1.00 s), on the spot. Tails trail and sway, each a little after the one beside it.",
    "Run": "loop 12 (0.50 s), on the spot. Tails stream and bounce.",
    "SpinChase": "52 (2.17 s). Winds up 1-8, spins 8-31 (fastest 13-26), slows and settles 31-46, rest to 52. Tails curl into the turn.",
    "Shake": "56 (2.33 s). Braces 1-8; shaking 9-40 (strongest 14-34, one side-to-side every 4 frames); eyes shut 7-46, "
             "a blink 52-55; settles 41-56.",
    "Guard": "72 (3.00 s). Drops into the stance 1-12 (tails fully curled over its back by 16); holds 12-54; stands down 54-68; "
             "rest to 72. To hold the stance longer, stop on any frame from 16 to 54.",
    "Pounce": "56 (2.33 s). Crouches and loads 1-12; rump wiggle 10-18; springs 18-21 (hind paws leave the ground on 21); in the "
              "air 21-32, highest on 26-27; front paws land 32, hind paws 36; eyes shut 33-40; settles 36-50; rest to 56.",
}


def main():
    stage = re.search(r"_(\d+)\.blend$", bpy.data.filepath).group(1)
    name_ = "SM_Pet_KitsuneSpirit_" + stage
    rig = bpy.data.objects[name_ + "_Rig"]
    mesh = bpy.data.objects[name_]
    pb = rig.pose.bones
    scene = bpy.context.scene
    scene.render.fps = 24
    S = abs(pb["front_foot_ik.L"].bone.head_local.y - pb["foot_ik.L"].bone.head_local.y) / PAWS_25

    # ---- the tails, and where each stands in the fan
    TAILS = []
    k = 1
    while ("tail_%d_1" % k) in pb:
        TAILS.append(["tail_%d_%d" % (k, j) for j in range(1, 6)])
        k += 1
    N = len(TAILS)
    roots = [rig.data.bones[t[0]].head_local for t in TAILS]
    tips = [rig.data.bones[t[-1]].tail_local for t in TAILS]
    root_z = sum(r.z for r in roots) / N
    far = max(math.hypot(t.x, t.z - root_z) for t in tips)
    SIDE = [(t.x / far, (t.z - root_z) / far) for t in tips]               # (to its left, upwards), each -1 .. 1
    MOVED = ("torso", "front_foot_ik.L", "front_foot_ik.R", "foot_ik.L", "foot_ik.R", "root")
    HELD = MOVED + ("hips", "chest", "neck", "head", "ear.L", "ear.R", "eyelid.L", "eyelid.R") + tuple(n for t in TAILS for n in t)
    mine = [n for n in ("Idle", "Shake", "Guard", "Pounce") + tuple(WHO) if n in ("Idle", "Shake", "Guard", "Pounce") or stage in WHO[n]]

    # ---- Walk, Run and SpinChase: the first form's script, run on this rig
    source = (Path(__file__).parent / "anim_kitsune.py").read_text(encoding="utf-8")
    quoted = 'RIG = "%s"' % BASE_RIG
    if quoted not in source:
        raise SystemExit("anim_kitsune.py no longer names its rig the way this script expects.")
    for action in list(bpy.data.actions):
        if not action.name.startswith("WGT"):
            bpy.data.actions.remove(action)
    exec(compile(source.replace(quoted, 'RIG = "%s"' % rig.name), "anim_kitsune.py", "exec"), {"__name__": "kitsune_base"})
    for action in list(bpy.data.actions):
        if action.name not in KEEP and not action.name.startswith("WGT"):
            bpy.data.actions.remove(action)
    for action in bpy.data.actions:
        if action.name not in KEEP:
            continue
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for fc in list(bag.fcurves):
                        if any('pose.bones["%s"]' % n in fc.data_path for n in STUB_TAIL + ("eyelid.L", "eyelid.R")):
                            bag.fcurves.remove(fc)             # the stub tail in the rump, and the first form's sliding eyelids
                        elif fc.data_path.endswith(".location"):
                            for kp in fc.keyframe_points:      # strides and bobs to this rig's size
                                kp.co[1] *= S
                                kp.handle_left[1] *= S
                                kp.handle_right[1] *= S

    touched = set()
    last_euler = {}

    def use(action):
        rig.animation_data_create()
        rig.animation_data.action = action
        if getattr(rig.animation_data, "action_slot", None) is None and getattr(action, "slots", None):
            rig.animation_data.action_slot = action.slots[0]

    def start(name):
        flow["on"] = None
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        rig.animation_data_create()
        rig.animation_data.action = action
        last_euler.clear()
        return action

    def rest():
        for name in touched:
            b = pb[name]
            b.location = (0, 0, 0)
            b.rotation_mode = "XYZ"
            b.rotation_euler = (0, 0, 0)
            b.scale = (1, 1, 1)

    def finish(action, first, last):
        action.use_frame_range = True
        action.frame_start, action.frame_end = first, last
        rig.animation_data.action = None
        rest()

    def swing(name, frame, nose_down=0.0, roll=0.0, left=0.0):
        """Turn a control by angles given the way the fox would feel them, whatever way the control's own axes
        lie: nose_down (about its left-to-right line; for a tail, positive lifts it up and over towards the head),
        roll (about its nose-to-tail line), left (about the upright line: a turn to its left)."""
        b = pb[name]
        touched.add(name)
        m = b.bone.matrix_local.to_3x3()
        world = (Matrix.Rotation(math.radians(left), 3, "Z") @ Matrix.Rotation(math.radians(roll), 3, "Y")
                 @ Matrix.Rotation(math.radians(nose_down), 3, "X"))
        local = m.inverted() @ world @ m
        b.rotation_mode = "XYZ"
        e = local.to_euler("XYZ", last_euler[name]) if name in last_euler else local.to_euler("XYZ")
        last_euler[name] = e.copy()
        b.rotation_euler = e
        b.keyframe_insert("rotation_euler", frame=frame)

    def shift(name, frame, world):
        """Move a control by a distance given in the scene's own directions (x to its left, -y forward, z up), in
        the 25 % form's measures: it is scaled to this rig's size."""
        b = pb[name]
        touched.add(name)
        b.location = (rig.matrix_world @ b.bone.matrix_local).to_3x3().inverted() @ (Vector(world) * S)
        b.keyframe_insert("location", frame=frame)

    lid_turn = {side: float(mesh["kn_lid_turn_" + side]) for side in ("L", "R")}

    def lid(frame, shut):
        """Eyelids: 0 open, 1 shut, anything between half shut. Each lid swings down over the eye about its axle."""
        for side in ("L", "R"):
            b = pb["eyelid." + side]
            touched.add(b.name)
            b.rotation_mode = "XYZ"
            b.rotation_euler = (0.0, math.radians(lid_turn[side] * shut), 0.0)
            b.keyframe_insert("rotation_euler", frame=frame)

    def blink(at, hold=1):
        """A blink: shut two frames after `at`, held, open again by at + 6 + hold."""
        lid(at, 0.0)
        lid(at + 2, 1.0)
        lid(at + 2 + hold, 1.0)
        lid(at + 6 + hold, 0.0)

    def ear(side, frame, back=0.0, out=0.0):
        swing("ear." + side, frame, nose_down=-back, roll=out * (1 if side == "L" else -1))

    def flick(side, at, size=1.0):
        for offset, amount in ((0, 0.0), (2, 1.0), (4, -0.35), (6, 0.12), (8, 0.0)):
            ear(side, at + offset, back=24.0 * size * amount, out=7.0 * size * amount)

    def along(values, j):
        """Four values, base to tip, read for bone j of five."""
        u = j * 3 / 4.0
        i0 = min(int(u), 2)
        return values[i0] + (values[i0 + 1] - values[i0]) * (u - i0)

    flow = {"on": None}
    ROOT_SHARE, BEND_KEPT, LIFT_MOST, OPEN_PER_BEND = 0.50, 0.26, 36.0, 0.15
    FAN_MIDDLE = sum((r.copy() for r in roots), Vector()) / N

    def flowing(first, last, period, loop=False, size=1.0):
        """From here on every tail carries a slow wave that runs from its root to its tip, so that a tail is never
        a stick: on in this clip from `first` to `last` (fading in and out over 8 frames unless the clip loops)."""
        flow["on"] = (first, last, period, loop, size)

    def tail_one(k, frame, sway=(0, 0, 0, 0), lift=(0, 0, 0, 0), base_left=0.0, base_up=0.0, out=0.0, bendy=False, more_sway=None, more_lift=None):
        """One tail (0 is the leftmost). sway: each bone's turn to the fox's left, base to tip, as four values.
        lift: each bone's turn up and over towards the head. base_left / base_up: more turn at the root alone.
        out: the root turned straight away from the middle of the fan (+), which is the one way a tail can move
        on its own and never meet a neighbour.
        RULE (the owner, 12 Oct 2026: no tail may pass through another): give every tail the SAME sway and lift
        at a given frame, or very nearly; what differs from tail to tail goes in `out`."""
        sx, sz = SIDE[k]
        wave_s, wave_l = [0.0] * 5, [0.0] * 5
        if flow["on"]:
            first, last, period, loop, size = flow["on"]
            fade = 1.0 if loop else max(0.0, min(1.0, (frame - first) / 8.0, (last - frame) / 8.0))
            for j in range(5):
                phase = 2 * math.pi * (frame - first) / period - 0.85 * j - 0.10 * k
                wave_s[j] = size * fade * (1.2 + 1.5 * j) * math.sin(phase)
                wave_l[j] = size * fade * (0.6 + 0.8 * j) * math.sin(phase * 1.0 + 1.3)
        want_s = [along(sway, j) * 0.8 for j in range(5)]
        want_l = [along(lift, j) * 0.8 for j in range(5)]
        if not bendy:
            # Bending every tail far along its length is what makes neighbours cross: the bends carry each tail a
            # different way. So most of the move is made at the root, where all the tails turn as one fan about one
            # place, and only a part of the bend is kept along the tail. The tip travels about as far as before.
            for want in (want_s, want_l):
                total = sum(want)
                want[:] = [ROOT_SHARE * total] + [BEND_KEPT * x for x in want[1:]]
            want_l[0] = max(-40.0, min(LIFT_MOST, want_l[0]))      # not down through its legs, not forward into its back
        # the more the tails are bent or lifted, the wider the fan is opened: bending is what brings neighbours together
        out = out + OPEN_PER_BEND * (sum(abs(x) for x in want_s[1:]) + sum(abs(x) for x in want_l[1:]) + abs(want_l[0]) + abs(want_s[0]))
        own_s = [along(more_sway, j) for j in range(5)] if more_sway else [0.0] * 5      # this tail's own bend, on top of the fan's
        own_l = [along(more_lift, j) for j in range(5)] if more_lift else [0.0] * 5
        for j, name in enumerate(TAILS[k]):
            swing(name, frame, nose_down=want_l[j] + own_l[j] + wave_l[j] + ((base_up + out * sz) if j == 0 else 0.0),
                  left=want_s[j] + own_s[j] + wave_s[j] + ((base_left - out * sx) if j == 0 else 0.0))
        # ... and the roots are carried round one middle point as they turn, so the fan turns as one solid thing.
        # (Each root turning about its own place slid the tails along one another: two that stand one above the
        # other, a hand apart at the root, were pushed through each other by a big lift.)
        root = pb[TAILS[k][0]]
        turned = Matrix.Rotation(math.radians(want_s[0]), 3, "Z") @ Matrix.Rotation(math.radians(want_l[0]), 3, "X")
        carried_ = (turned - Matrix.Identity(3)) @ (root.bone.head_local - FAN_MIDDLE) if not bendy else Vector((0.0, 0.0, 0.0))
        root.location = root.bone.matrix_local.to_3x3().inverted() @ carried_
        root.keyframe_insert("location", frame=frame)

    def tails(frame, sway=(0, 0, 0, 0), lift=(0, 0, 0, 0), spread=0.0, sway_r=None, lift_r=None):
        """All the tails as one: every tail takes the same sway and lift (the mean of the left's and the right's
        when both are given). spread: the fan opened at the roots (+); it is never closed past rest by more than
        a little, or the tails would meet."""
        s = sway if sway_r is None else [(a_ + b_) / 2.0 for a_, b_ in zip(sway, sway_r)]
        l = lift if lift_r is None else [(a_ + b_) / 2.0 for a_, b_ in zip(lift, lift_r)]
        for k in range(N):
            tail_one(k, frame, s, l, out=max(spread, -2.0))

    def wave(frame, period, phase=0.0):
        return math.sin(2 * math.pi * (frame - 1) / period + phase)

    def ease(t):
        t = max(0.0, min(1.0, t))
        return t * t * (3 - 2 * t)

    def track(points, frame):
        if frame <= points[0][0]:
            return points[0][1]
        for (f0, v0), (f1, v1) in zip(points[:-1], points[1:]):
            if f0 <= frame <= f1:
                return v0 + (v1 - v0) * ease((frame - f0) / max(f1 - f0, 1e-9))
        return points[-1][1]

    def through(points, frame):
        """A value through (frame, value) points on a smooth curve that does not stop at each one."""
        pts = sorted(points)
        if frame <= pts[0][0]:
            return pts[0][1]
        if frame >= pts[-1][0]:
            return pts[-1][1]
        for i in range(len(pts) - 1):
            (f0, v0), (f1, v1) = pts[i], pts[i + 1]
            if f0 <= frame <= f1:
                fa, va = pts[i - 1] if i > 0 else (f0 - (f1 - f0), v0)
                fb, vb = pts[i + 2] if i + 2 < len(pts) else (f1 + (f1 - f0), v1)
                span = f1 - f0
                t = (frame - f0) / span
                m0 = (v1 - va) / (f1 - fa) * span
                m1 = (vb - v0) / (fb - f0) * span
                return ((2 * t ** 3 - 3 * t ** 2 + 1) * v0 + (t ** 3 - 2 * t ** 2 + t) * m0
                        + (-2 * t ** 3 + 3 * t ** 2) * v1 + (t ** 3 - t ** 2) * m1)

    def swing_past(frame, since, size, beat=7.0, fade=4.5):
        t = frame - since
        return size * math.exp(-t / fade) * math.sin(2 * math.pi * t / beat) if t > 0 else 0.0

    front_y = abs(pb["front_foot_ik.L"].bone.head_local.y)
    hind_y = abs(pb["foot_ik.L"].bone.head_local.y)
    middle = 0.17 * S                                        # how high its middle is, standing

    def carried(tip):
        """Where the root must be for the fox to tip by `tip` about the paws that carry it, and where that puts its middle."""
        lift = (hind_y if tip < 0 else front_y) * abs(math.sin(math.radians(tip)))
        return lift, Vector((0.0, -middle * math.sin(math.radians(tip)), middle * math.cos(math.radians(tip)) + lift))

    def shift_m(name, frame, world):
        """As shift, but in this rig's own metres."""
        b = pb[name]
        touched.add(name)
        b.location = (rig.matrix_world @ b.bone.matrix_local).to_3x3().inverted() @ Vector(world)
        b.keyframe_insert("location", frame=frame)

    # ---- Idle
    a = start("Idle")
    LOOP = 192
    look = [(1, 0), (30, 0), (48, 34), (80, 34), (104, -32), (132, -32), (152, 0), (LOOP + 1, 0)]
    for frame in range(1, LOOP + 2, 3):
        for k in range(N):
            lag = 0.10 * k                                    # the wag runs across the fan from its left to its right, gently
            wag = [(8.0 + 3.0 * i) * wave(frame, 48, -0.85 * i - lag) + (3.5 + i) * wave(frame, 96, 0.7 - 0.5 * i - lag * 0.6)
                   + 2.0 * wave(frame, 192, 1.9 - lag) for i in range(4)]
            rise = [2.5 * wave(frame, 96, 1.2 - 0.45 * i - lag) + 1.5 * wave(frame, 192, 0.3 - lag) for i in range(4)]
            tail_one(k, frame, wag, rise, out=2.5 + 1.0 * wave(frame, 192, 0.5))      # the fan held a little open
        shift("torso", frame, (0.0, 0.0, 0.0013 * wave(frame, 48)))
        swing("chest", frame, nose_down=0.7 * wave(frame, 48, 0.4))
        yaw = track(look, frame)
        swing("head", frame, left=yaw * 0.72, nose_down=1.2 * wave(frame, 96, 1.0) - 0.05 * abs(yaw), roll=0.10 * yaw)
        swing("neck", frame, left=yaw * 0.28)
        swing("hips", frame, left=-0.5 * wave(frame, 48, -0.3))
    for side in ("L", "R"):
        ear(side, 1)
        ear(side, LOOP + 1)
    flick("L", 60)
    flick("R", 116, 0.8)
    flick("L", 168, 0.6)
    flick("R", 170, 0.9)
    lid(1, 0.0)
    lid(LOOP + 1, 0.0)
    blink(24)
    blink(90, hold=1)
    blink(156, hold=2)
    finish(a, 1, LOOP + 1)

    # ---- Shake
    a = start("Shake")
    hard = [(1, 0.0), (8, 0.0), (14, 1.0), (34, 1.0), (40, 0.25), (44, 0.0), (56, 0.0)]
    brace = [(1, 0.0), (8, 1.0), (38, 1.0), (48, -0.25), (56, 0.0)]
    flowing(1, 56, 28)
    for frame in range(1, 57):
        k_, low = track(hard, frame), track(brace, frame)

        def shake(lag):
            return math.sin(2 * math.pi * (frame - 8 - lag) / 4.0) * track(hard, frame - lag)

        shift("torso", frame, (0.0, 0.0, -0.010 * max(low, 0.0) + 0.004 * max(-low, 0.0)))
        swing("head", frame, roll=30.0 * shake(0.0), left=7.0 * shake(0.4), nose_down=7.0 * max(low, 0.0) - 4.0 * max(-low, 0.0))
        swing("neck", frame, roll=14.0 * shake(0.5))
        swing("chest", frame, roll=13.0 * shake(1.0), left=3.0 * shake(1.3))
        swing("hips", frame, roll=-11.0 * shake(2.0), left=-4.0 * shake(2.3))
        for k in range(N):                                    # the shake runs out along the tails, the fan held open
            tail_one(k, frame, [(12.0 + 4.5 * i) * shake(2.6 + 0.7 * i + 0.04 * k) for i in range(4)], [5.0 * k_] * 4,
                     out=7.0 * min(1.0, 4.0 * k_) + 3.0 * through([(1, 0), (36, 0.0), (44, 1.0), (50, 1.0), (56, 0.0)], frame))
        for side in ("L", "R"):
            ear(side, frame, back=10.0 * k_, out=-16.0 * shake(0.8) * (1 if side == "L" else -1))
    for frame, shut in ((1, 0.0), (5, 0.0), (7, 1.0), (43, 1.0), (47, 0.0), (50, 0.0), (52, 1.0), (53, 1.0), (56, 0.0)):
        lid(frame, shut)
    finish(a, 1, 56)

    # ---- Guard
    a = start("Guard")
    CURL = (3.0 / N) ** 0.5                                 # many tails curl less far: a full curl packs them into one mass
    stance = [(1, 0.0), (12, 1.0), (54, 1.0), (68, 0.0), (72, 0.0)]
    curl = [(1, 0.0), (5, -0.05), (16, 1.0), (54, 1.0), (66, -0.03), (72, 0.0)]
    flowing(1, 72, 36, size=0.7)
    for frame in list(range(1, 73, 2)) + [72]:
        s, c = track(stance, frame), track(curl, frame)
        breath = wave(frame, 36) * s
        for side, way in (("L", 1.0), ("R", -1.0)):
            shift("front_foot_ik." + side, frame, (0.024 * s * way, -0.010 * s, 0.0))
            shift("foot_ik." + side, frame, (0.020 * s * way, 0.010 * s, 0.0))
            swing("front_foot_ik." + side, frame, left=-9.0 * s * way)
            swing("foot_ik." + side, frame, left=-7.0 * s * way)
            ear(side, frame, back=20.0 * s, out=6.0 * s)
        shift("torso", frame, (0.0, 0.004 * s, -0.020 * s + 0.0012 * breath))
        swing("chest", frame, nose_down=7.0 * s + 0.6 * breath)
        swing("hips", frame, nose_down=-3.0 * s)
        swing("neck", frame, nose_down=9.0 * s)
        swing("head", frame, nose_down=-14.0 * s)
        tip = 3.0 * wave(frame, 24, 0.5) * s
        for k in range(N):                                    # the whole fan curled up over its back as one, and opened
            tail_one(k, frame, (0.0, 0.0, 0.0, 0.0), (CURL * 40.0 * c, CURL * 44.0 * c, CURL * 46.0 * c + tip, CURL * 38.0 * c + 2 * tip),
                     out=9.0 * c)
    lid(1, 0.0)
    for frame, shut in ((8, 0.0), (11, 0.42), (54, 0.42), (60, 0.0), (72, 0.0)):       # eyes narrowed
        lid(frame, shut)
    finish(a, 1, 72)

    # ---- Pounce (see anim_kitsune_25.py for how the flight is worked out)
    a = start("Pounce")
    flowing(1, 56, 28, size=0.6)
    OFF, DOWN, HIND_DOWN = 21, 32, 36
    TIP_OFF, TIP_DOWN = -24.0, 27.0
    _l, left_at = carried(TIP_OFF)
    _l, land_at = carried(TIP_DOWN)
    air = (DOWN - OFF) / 24.0
    rise = 9.81 * air * air / 8.0
    crouch = [(1, 0.0), (7, 0.55), (12, 1.0), (17, 1.18), (19, 0.6), (OFF, -0.25), (24, 0.0), (DOWN, 0.0), (34, 1.05), (HIND_DOWN, 0.7),
              (38, 1.15), (42, -0.12), (46, 0.05), (50, 0.0), (56, 0.0)]
    for frame in range(1, 57):
        if frame <= 18:
            tip, root_at = 0.0, Vector((0, 0, 0))
        elif frame <= OFF:
            tip = TIP_OFF * ((frame - 18) / (OFF - 18)) ** 1.6
            root_at = Vector((0, 0, carried(tip)[0]))
        elif frame < DOWN:
            t = (frame - OFF) / (DOWN - OFF)
            tip = TIP_OFF + (TIP_DOWN - TIP_OFF) * t
            at = left_at.lerp(land_at, t) + Vector((0, 0, rise * 4 * t * (1 - t)))
            root_at = at - Vector((0.0, -middle * math.sin(math.radians(tip)), middle * math.cos(math.radians(tip))))
        elif frame <= HIND_DOWN:
            t = (frame - DOWN) / (HIND_DOWN - DOWN)
            tip = TIP_DOWN * (1 - t) ** 1.5
            root_at = Vector((0, 0, carried(tip)[0]))
        else:
            tip, root_at = 0.0, Vector((0, 0, 0))
        swing("root", frame, nose_down=tip)
        shift_m("root", frame, root_at)
        c = through(crouch, frame)
        back = through([(1, 0), (12, 1.0), (17, 1.25), (OFF, 0.0), (56, 0.0)], frame)
        shift("torso", frame, (0.0, 0.010 * back, -0.027 * c))
        stretch = through([(1, 0), (18, 0), (OFF, -1.0), (24, -0.5), (27, 0.9), (30, 0.3), (DOWN, -0.4), (34, 0.9), (38, 0.3), (44, 0.0), (56, 0)], frame)
        swing("chest", frame, nose_down=8.0 * max(c, 0.0) + 9.0 * stretch)
        wiggle = math.sin(2 * math.pi * (frame - 10) / max(4.6 - 0.2 * (frame - 10), 2.6)) * through([(9, 0), (11, 1.0), (16, 1.0), (18, 0)], frame)
        swing("hips", frame, nose_down=-6.0 * max(c, 0.0) - 8.0 * stretch, left=8.0 * wiggle, roll=3.0 * wiggle)
        aim = through([(1, 0), (8, 12), (17, 16), (OFF, 14), (DOWN, 10), (40, 0), (56, 0)], frame)
        nod = swing_past(frame, DOWN, 9.0, beat=9.0) + swing_past(frame, HIND_DOWN, 4.0, beat=8.0)
        swing("neck", frame, nose_down=0.4 * aim - 0.30 * tip + 0.4 * nod)
        swing("head", frame, nose_down=0.6 * aim - 0.38 * tip + 0.6 * nod, left=-1.5 * wiggle)
        ears_ = through([(1, 0), (8, -9), (18, -11), (OFF, 6), (25, 24), (30, 26), (DOWN, 20), (56, 0)], frame) if frame <= DOWN else 0.0
        if frame > DOWN:
            ears_ = 20.0 * math.exp(-(frame - DOWN) / 3.0) - swing_past(frame, DOWN, 16.0, beat=8.0, fade=5.0)
        for side in ("L", "R"):
            ear(side, frame, back=ears_)
        drag = through([(1, 0), (10, 5), (18, 8), (OFF, -16), (24, -20), (27, 4), (30, 26), (DOWN, 34), (56, 0)], frame) if frame <= DOWN else 0.0
        if frame > DOWN:
            drag = 34.0 * math.exp(-(frame - DOWN) / 5.0) + swing_past(frame, DOWN, 24.0, beat=11.0, fade=7.0)
        apart = through([(1, 0), (10, 2), (18, 3), (OFF, 2), (27, 6), (DOWN, 9), (38, 7), (46, 2), (52, 0), (56, 0)], frame)
        tails(frame, lift=(drag * 0.45, drag * 0.8, drag * 1.0, drag * 0.9), spread=apart,
              sway=tuple(-9.0 * wiggle * (0.6 + 0.3 * i) for i in range(4)))
        hind_y_ = through([(18, 0), (OFF, 0.0), (23, -0.034), (26, 0.016), (29, 0.040), (DOWN, 0.034), (HIND_DOWN, 0.0), (56, 0)], frame)
        hind_z_ = through([(18, 0), (OFF, 0.0), (23, 0.010), (26, 0.046), (29, 0.050), (DOWN, 0.030), (HIND_DOWN, 0.0), (56, 0)], frame)
        front_y_ = through([(18, 0), (OFF, 0.014), (24, 0.022), (27, 0.004), (30, 0.040), (DOWN, 0.018), (35, 0.0), (56, 0)], frame)
        front_z_ = through([(18, 0), (OFF, 0.034), (24, 0.052), (27, 0.046), (30, 0.016), (DOWN, 0.0), (56, 0)], frame)
        for side in ("L", "R"):
            shift("foot_ik." + side, frame, (0.0, -hind_y_, max(hind_z_, 0.0)))
            shift("front_foot_ik." + side, frame, (0.0, -front_y_, max(front_z_, 0.0)))
            swing("foot_ik." + side, frame, nose_down=through([(18, 0), (OFF, 22), (24, 30), (28, -6), (HIND_DOWN, 0), (56, 0)], frame))
            swing("front_foot_ik." + side, frame, nose_down=through([(18, 0), (OFF, 16), (25, 26), (30, -14), (DOWN, 0), (56, 0)], frame))
    for frame, shut in ((1, 0.0), (13, 0.0), (15, 0.4), (19, 0.0), (DOWN - 1, 0.0), (DOWN + 1, 1.0), (37, 1.0), (41, 0.0), (56, 0.0)):
        lid(frame, shut)
    finish(a, 1, 56)

    # ---- FoxfireFlick: the tails wind back, then whip forward over its back TOGETHER (as one they cannot meet; whipped
    # one at a time a lower tail went through the one above it). The whip runs out along the tails, tips last. What
    # goes one after another is a flick outwards at the top of the whip, running across the fan from its left to
    # its right: that is when each tail's wisp leaves.
    a = start("FoxfireFlick")
    flowing(1, 60, 30, size=0.6)
    WHIP = 16
    flick_at = [WHIP + 4.0 + 10.0 * k / max(N - 1, 1) for k in range(N)]
    for frame in range(1, 61):
        low = through([(1, 0), (10, 1.0), (15, 1.15), (22, 0.5), (30, 0.2), (46, -0.08), (54, 0.0), (60, 0.0)], frame)
        pulse = math.exp(-((frame - WHIP - 3.0) / 2.5) ** 2)
        shift("torso", frame, (0.0, 0.012 * low - 0.012 * pulse, -0.022 * low))
        swing("chest", frame, nose_down=6.0 * low + 5.0 * pulse)
        swing("hips", frame, nose_down=-5.0 * low - 6.0 * pulse)
        swing("neck", frame, nose_down=6.0 * low - 7.0 * pulse)
        swing("head", frame, nose_down=-8.0 * low + 6.0 * pulse)
        for side in ("L", "R"):
            ear(side, frame, back=14.0 * low + 14.0 * pulse)
        lift = []
        for i in range(4):
            late = 1.1 * i
            if frame <= WHIP + late * 0.3:
                lift.append(through([(1, 0), (9, -5), (WHIP - 1 + late * 0.3, -8 - 1 * i)], min(frame, WHIP - 1 + late * 0.3)))
            else:
                t = frame - WHIP - late
                over = 30.0 + 6.0 * i
                lift.append(through([(-late, -8 - 1 * i), (3.5, over), (9, over * 0.7), (17, over * 0.45), (24, -6.0 - 2 * i), (31, 3.0), (38, 0.0)], t) if t < 38 else 0.0)
        for k in range(N):
            kick = 5.0 * math.exp(-((frame - flick_at[k]) / 2.4) ** 2)
            opened = through([(1, 0), (10, 1.0), (WHIP, 2.0), (WHIP + 6, 6.0), (36, 5.0), (46, 1.0), (54, 0.0), (60, 0.0)], frame)
            tail_one(k, frame, (0, 0, 0, 0), lift, out=opened + kick)
    lid(1, 0.0)
    blink(46)
    lid(60, 0.0)
    finish(a, 1, 60)
    CUES["FoxfireFlick"] = (
        "60 (2.50 s). Crouches and winds the tails back 1-15; the tails whip forward over its back together 16-22 (tips furthest "
        "forward about 21); held over its back, fan open, 22-36, and across it each tail flicks outwards in turn from its left to "
        "its right: tail K flicks on frame %s (its wisp leaves then); tails come back down 36-50; a blink 48-51; rest to 60."
        % ", ".join("%d" % round(w) for w in flick_at))

    if stage in WHO["EmberWaltz"]:
        # ---- EmberWaltz: rears lightly onto its hind legs, turns once on the spot (to its left), lands and bows.
        # The whole fox rides on the root: it tips back about the hind paws and turns about the upright. The tails
        # open into a wheel and are left behind by the turn; on the landing they swing on past and close.
        a = start("EmberWaltz")
        flowing(1, 96, 32, size=0.8)
        UP, TURN0, TURN1, LAND = 14, 18, 62, 70
        for frame in range(1, 97):
            rear = through([(1, 0), (8, 3.0), (UP + 4, -26.0), (40, -30.0), (TURN1 - 2, -24.0), (LAND, 0.0), (96, 0.0)], frame) if frame <= LAND else 0.0
            t = max(0.0, min(1.0, (frame - TURN0) / (TURN1 - TURN0)))
            turn = 360.0 * (t * t * (3 - 2 * t))
            speed = 6.0 * t * (1 - t)                         # how fast it is turning, 0 .. 1.5
            swing("root", frame, nose_down=rear, left=turn)
            shift_m("root", frame, (0.0, 0.0, carried(rear)[0] if rear < 0 else 0.0))
            sink = through([(1, 0), (8, 1.0), (UP, 0.2), (TURN1, 0.0), (LAND + 2, 1.3), (LAND + 8, 0.8), (84, 0.9), (92, 0.0), (96, 0.0)], frame)
            bow = through([(1, 0), (LAND, 0.0), (LAND + 6, 1.0), (84, 1.0), (93, 0.0), (96, 0.0)], frame)
            shift("torso", frame, (0.0, -0.006 * bow, -0.016 * sink))
            swing("chest", frame, nose_down=-8.0 * (-rear / 30.0) + 16.0 * bow)
            swing("hips", frame, nose_down=6.0 * (-rear / 30.0) - 5.0 * bow)
            swing("neck", frame, nose_down=10.0 * (-rear / 30.0) + 10.0 * bow, left=10.0 * speed)
            swing("head", frame, nose_down=12.0 * (-rear / 30.0) + 6.0 * bow, left=14.0 * speed)     # it looks where it is going
            up = max(0.0, -rear / 30.0)
            for side, way in (("L", 1.0), ("R", -1.0)):
                paw = 0.5 + 0.5 * math.sin(2 * math.pi * (frame - UP) / 14.0 + (0 if side == "L" else math.pi))
                shift("front_foot_ik." + side, frame, (0.0, 0.020 * up + 0.012 * bow * -1.0, 0.070 * up + 0.012 * up * paw))
                swing("front_foot_ik." + side, frame, nose_down=38.0 * up)
                step = max(0.0, math.sin(2 * math.pi * (frame - TURN0) / 11.0 + (0 if side == "L" else math.pi))) * min(1.0, speed * 2.0)
                shift("foot_ik." + side, frame, (0.0, 0.0, 0.014 * step))
                ear(side, frame, back=16.0 * speed + 8.0 * bow)
            open_ = through([(1, 0), (10, 1.0), (TURN0 + 4, 11.0), (TURN1 - 6, 12.0), (LAND + 3, 3.0), (LAND + 12, 2.0), (92, 0.0), (96, 0.0)], frame)
            left_behind = -13.0 * speed + swing_past(frame, TURN1, 14.0, beat=12.0, fade=8.0)     # the turn is to its left
            for k in range(N):
                tail_one(k, frame, [left_behind * (0.5 + 0.25 * i) for i in range(4)], [(5.0 + 2.0 * i) * up for i in range(4)],
                         out=max(open_, -2.0))
        lid(1, 0.0)
        lid(LAND, 0.0)
        lid(LAND + 3, 1.0)
        lid(82, 1.0)
        lid(86, 0.0)
        lid(96, 0.0)
        finish(a, 1, 96)
        CUES["EmberWaltz"] = (
            "96 (4.00 s). Gathers 1-8; rears onto its hind legs 8-18; turns ONE FULL TURN to its left 18-62 (fastest about 40), "
            "tails opened into a wheel 22-56 and left behind by the turn; comes down 62-70 (front paws land on 70); bows 70-84 "
            "(deepest 76-84), eyes shut 73-83; rises 84-93; rest to 96. The root control ends 360 degrees round: the same pose.")

        # ---- Kindle: sits, brings one tail round to its muzzle, breathes on the tip, watches it, stands.
        a = start("Kindle")
        flowing(1, 108, 36, size=0.7)
        pet_tail = 0                                          # its outermost left tail: no other tail lies in its way round
        side_way = 1.0                                                                  # it comes round the fox's left side
        for frame in range(1, 109):
            sit = through([(1, 0), (6, -0.08), (20, 1.0), (86, 1.0), (100, -0.05), (108, 0.0)], frame)
            come = through([(1, 0), (14, 0.0), (22, -0.10), (42, 1.0), (80, 1.0), (96, -0.06), (104, 0.0), (108, 0.0)], frame)
            look = through([(1, 0), (28, 0.0), (44, 1.0), (82, 1.0), (96, 0.0), (108, 0.0)], frame)
            breath = math.exp(-((frame - 54.0) / 3.2) ** 2)                               # the puff
            draw = math.exp(-((frame - 48.0) / 3.0) ** 2)                                 # the breath drawn in before it
            # sitting: the rump goes down and back onto the hind paws, the front legs stay straight under the chest
            # (the whole trunk tips back about its middle, and is let down by as much as that lifts the shoulders, so
            # the chest stays where it was over the front paws and only the rump comes down)
            shift("torso", frame, (0.0, 0.018 * sit, -0.036 * sit))
            swing("torso", frame, nose_down=-24.0 * sit)
            swing("hips", frame, nose_down=-12.0 * sit)
            swing("chest", frame, nose_down=4.0 * sit - 3.0 * draw + 2.0 * breath, left=8.0 * look * side_way)
            swing("neck", frame, nose_down=14.0 * sit + 6.0 * look - 4.0 * draw + 5.0 * breath, left=28.0 * look * side_way)
            swing("head", frame, nose_down=10.0 * sit + 12.0 * look - 5.0 * draw + 7.0 * breath, left=40.0 * look * side_way,
                  roll=6.0 * look * side_way)
            for side, way in (("L", 1.0), ("R", -1.0)):
                shift("foot_ik." + side, frame, (0.008 * sit * way, -0.030 * sit, 0.0))
                ear(side, frame, back=-8.0 * look + 12.0 * breath)
            for k in range(N):
                settle = [-(5.0 + 1.5 * i) * sit for i in range(4)]     # every tail let down behind it, alike
                if k == pet_tail:                             # ... and this one out and round the side of the body
                    flicker = 2.5 * math.sin(0.9 * frame) * through([(1, 0), (56, 0), (62, 1.0), (78, 1.0), (84, 0.0), (108, 0)], frame)
                    # (out to the side first, clear of the haunch, and the bend kept for its far half; it stays as
                    # low as the others, under the tail that stands above it, and only its tip comes up)
                    tail_one(k, frame, (0, 0, 0, 0), settle, base_left=-34.0 * come * side_way, out=4.0 * come + 2.0 * sit + 4.0 * through([(1, 0), (84, 0.0), (94, 1.0), (100, 1.0), (108, 0.0)], frame),
                             more_sway=[-b_ * come * side_way + flicker * (i / 3.0) for i, b_ in enumerate((5.0, 18.0, 36.0, 46.0))],
                             more_lift=[u_ * come + flicker * (i / 3.0) for i, u_ in enumerate((0.0, 0.0, 5.0, 12.0))])
                else:
                    tail_one(k, frame, (0, 0, 0, 0), settle, out=2.0 * sit + (4.0 * through([(1, 0), (84, 0.0), (94, 1.0), (100, 1.0), (108, 0.0)], frame) if k == 1 else 0.0))
        lid(1, 0.0)
        blink(34)
        lid(50, 0.0)
        lid(52, 1.0)                                          # eyes shut for the puff
        lid(55, 1.0)
        lid(58, 0.0)
        for frame_ in (60, 80):
            lid(frame_, 0.25)                                  # watching it, eyes soft
        lid(84, 0.0)
        blink(88)
        lid(108, 0.0)
        finish(a, 1, 108)
        CUES["Kindle"] = (
            "108 (4.50 s). Sits 6-20; tail %d (counted from its left) curls round beside its left flank 22-42, tip up; "
            "head turns back and down to it 28-44; a blink 36-38; draws breath 45-51; PUFFS on 54 (eyes shut 52-56): the flame takes on the tip "
            "on 55; watches it 56-80, the tip flickering; tail goes back 80-96; stands 86-100; a blink 90-92; rest to 108. "
            "The flame sits on the tail bone tail_%d_5, at its far end." % (pet_tail + 1, pet_tail + 1))

    if stage in WHO["NinefoldBlaze"]:
        # ---- NinefoldBlaze: plants its paws and lowers its head; the tails fold shut; then they open one by one from
        # the outside in, a beat apart, each snapping out past its place and settling; the head is thrown back as
        # the last, the middle one, rises; it holds, trembling, and comes down.
        a = start("NinefoldBlaze")
        flowing(1, 132, 33, size=0.7)
        order = sorted(range(N), key=lambda k: (-abs(SIDE[k][0]), SIDE[k][0]))          # outermost first, the middle one last
        beats = {}
        beat_frames = []
        f_ = 36.0
        i = 0
        while i < len(order):
            pair = [order[i]]
            if i + 1 < len(order) and abs(abs(SIDE[order[i + 1]][0]) - abs(SIDE[order[i]][0])) < 0.12:
                pair.append(order[i + 1])
            for k in pair:
                beats[k] = f_
            beat_frames.append(int(round(f_)))
            i += len(pair)
            f_ += 8.0 if i < len(order) - 1 else 10.0
        LAST = max(beats.values())                             # the middle tail
        HOWL = LAST + 6
        for frame in range(1, 133):
            plant = through([(1, 0), (12, 1.0), (HOWL + 20, 1.0), (118, 0.0), (132, 0.0)], frame)
            bowed = through([(1, 0), (14, 1.0), (LAST - 2, 1.1), (HOWL, -1.0), (HOWL + 18, -1.0), (112, 0.1), (124, 0.0), (132, 0.0)], frame)
            tremble = math.sin(2 * math.pi * frame / 3.3) * through([(1, 0), (HOWL, 0), (HOWL + 3, 1.0), (HOWL + 16, 1.0), (HOWL + 24, 0), (132, 0)], frame)
            throb = sum(math.exp(-((frame - b) / 1.8) ** 2) for b in beat_frames)        # the body takes each tail's opening
            for side, way in (("L", 1.0), ("R", -1.0)):
                shift("front_foot_ik." + side, frame, (0.020 * plant * way, -0.008 * plant, 0.0))
                shift("foot_ik." + side, frame, (0.022 * plant * way, 0.008 * plant, 0.0))
                swing("front_foot_ik." + side, frame, left=-8.0 * plant * way)
                swing("foot_ik." + side, frame, left=-6.0 * plant * way)
                ear(side, frame, back=18.0 * max(bowed, 0.0) + 26.0 * max(-bowed, 0.0), out=5.0 * plant)
            shift("torso", frame, (0.0, 0.004 * plant, -0.018 * plant - 0.004 * throb + 0.006 * max(-bowed, 0.0) + 0.0015 * tremble))
            swing("chest", frame, nose_down=9.0 * max(bowed, 0.0) - 12.0 * max(-bowed, 0.0) + 1.5 * throb + 0.6 * tremble)
            swing("hips", frame, nose_down=-4.0 * plant - 2.0 * throb)
            swing("neck", frame, nose_down=14.0 * max(bowed, 0.0) - 22.0 * max(-bowed, 0.0) + 0.8 * tremble)
            swing("head", frame, nose_down=10.0 * max(bowed, 0.0) - 34.0 * max(-bowed, 0.0) + 1.2 * tremble)
            # together: the fan sinks and its tips curl down (never closed: closing is how tails meet), then rises
            # again through the beats to stand a little higher and wider than at rest for the howl
            sunk = through([(1, 0), (14, 0.0), (30, 1.0), (36, 1.0), (LAST + 3, -0.35), (HOWL + 20, -0.35), (116, 0.05), (126, 0.0), (132, 0.0)], frame)
            for k in range(N):
                b = beats[k]
                snap = through([(b - 1, 0.0), (b + 3, 0.68), (b + 8, 0.6), (HOWL + 20, 0.6), (118, 0.0), (132, 0.0)], frame) if frame > b - 1 else 0.0
                quiver = tremble * 1.2 * snap
                tail_one(k, frame, (0, 0, 0, 0), [-(4.0 + 3.0 * i) * sunk + quiver * (i / 3.0) for i in range(4)],
                         base_up=-5.0 * max(sunk, 0.0), out=1.0 + 12.0 * snap)
        lid(1, 0.0)
        blink(20)
        lid(HOWL - 5, 0.0)
        lid(HOWL - 2, 1.0)
        lid(HOWL + 17, 1.0)
        lid(HOWL + 21, 0.0)
        blink(118)
        lid(132, 0.0)
        finish(a, 1, 132)
        CUES["NinefoldBlaze"] = (
            "132 (5.50 s). Plants its paws and lowers its head 1-14; a blink 22-24; the tails fold shut 14-30; they open from the "
            "outside in, on frames %s (each snaps out furthest 3 frames after its frame: light its tip then); the last, the middle "
            "tail, on %d; head thrown back %d-%d, eyes shut %d-%d; holds, trembling, to %d; comes down %d-124; a blink 122-124; rest to 132."
            % (", ".join(str(b) for b in beat_frames), int(LAST), int(LAST), int(HOWL), int(HOWL - 2), int(HOWL + 19), int(HOWL + 20), int(HOWL + 20)))

    if stage in WHO["SpiritStep"]:
        # ---- SpiritStep: a dash through nothing. It crouches and lunges; from frame 12 to 18 it is meant to be
        # unseen (Unity fades it out on 12, moves it a body-length on, and fades it in on 18); it arrives low on
        # its front paws, skids, and the tails, left behind, come streaming past and settle.
        a = start("SpiritStep")
        flowing(1, 48, 24, size=0.6)
        GONE, BACK = 12, 18
        for frame in range(1, 49):
            low = through([(1, 0), (7, 1.0), (10, 0.5), (GONE, 0.0), (BACK, 0.9), (22, 1.2), (28, 0.5), (38, -0.06), (44, 0.0), (48, 0.0)], frame)
            if frame < BACK:                                  # away: forward and a little up, stretched out
                go = through([(1, 0), (7, 0.06), (GONE, -0.30), (BACK - 1, -0.34)], frame)
                lift = through([(1, 0), (8, 0.0), (GONE, 0.035), (BACK - 1, 0.045)], frame)
                lean = through([(1, 0), (7, 5.0), (GONE, -10.0), (BACK - 1, -8.0)], frame)
            else:                                             # arriving from behind where it will stand
                go = through([(BACK, 0.20), (22, 0.03), (27, -0.012), (34, 0.0), (48, 0.0)], frame)
                lift = through([(BACK, 0.030), (21, 0.0), (48, 0.0)], frame)
                lean = through([(BACK, 12.0), (22, 6.0), (28, -2.0), (36, 0.0), (48, 0.0)], frame)
            shift_m("root", frame, (0.0, go * S * 1.0, max(lift, 0.0) * S))
            swing("root", frame, nose_down=lean)
            shift("torso", frame, (0.0, 0.0, -0.024 * low))
            stretch = through([(1, 0), (7, -0.3), (GONE, 1.0), (BACK - 1, 1.0), (BACK, 0.6), (23, -0.5), (30, 0.1), (40, 0.0), (48, 0.0)], frame)
            swing("chest", frame, nose_down=7.0 * max(low, 0.0) - 8.0 * stretch)
            swing("hips", frame, nose_down=-6.0 * max(low, 0.0) + 7.0 * stretch)
            swing("neck", frame, nose_down=8.0 * stretch - 0.3 * lean)
            swing("head", frame, nose_down=-10.0 * stretch - 0.3 * lean + swing_past(frame, 22, 7.0, beat=9.0))
            for side in ("L", "R"):
                ear(side, frame, back=24.0 * max(stretch, 0.0) + 8.0 * max(low, 0.0) - swing_past(frame, 22, 14.0, beat=8.0, fade=5.0))
                reach = through([(1, 0), (8, 0.0), (GONE, 1.0), (BACK - 1, 1.0), (BACK, 0.8), (21, 0.0), (48, 0.0)], frame)
                trail_ = through([(1, 0), (8, 0.0), (GONE, 1.0), (BACK - 1, 1.0), (BACK, 0.7), (24, 0.0), (48, 0.0)], frame)
                shift("front_foot_ik." + side, frame, (0.0, -0.040 * reach, 0.022 * reach))
                swing("front_foot_ik." + side, frame, nose_down=-18.0 * reach)
                shift("foot_ik." + side, frame, (0.0, 0.044 * trail_, 0.020 * trail_))
                swing("foot_ik." + side, frame, nose_down=26.0 * trail_)
            stream = through([(1, 0), (7, 6.0), (GONE, -20.0), (BACK - 1, -22.0)], frame) if frame < BACK else \
                -22.0 * math.exp(-(frame - BACK) / 4.0) + 30.0 * math.exp(-((frame - 24.0) / 3.5) ** 2) + swing_past(frame, 27, 12.0, beat=11.0, fade=7.0)
            wide = through([(1, 0), (GONE, 1.0), (BACK, 1.0), (26, 5.0), (36, 1.0), (44, 0.0), (48, 0.0)], frame)
            for k in range(N):
                tail_one(k, frame, (0, 0, 0, 0), [stream * (0.4 + 0.2 * i) for i in range(4)], out=wide)
        lid(1, 0.0)
        lid(GONE - 3, 0.0)
        lid(GONE - 1, 1.0)
        lid(BACK, 1.0)
        lid(BACK + 3, 0.0)
        lid(48, 0.0)
        finish(a, 1, 48)
        CUES["SpiritStep"] = (
            "48 (2.00 s). Crouches 1-7; lunges 7-12 (it travels %.2f m forward and stretches out); UNSEEN 12-18; arrives 18-22, low "
            "on its front paws, from %.2f m behind its place; skids and rises 22-36; tails stream past and settle 20-40; rest to 48. "
            "Eyes shut 11-19. The clip ends where it began: to make it a real dash, Unity fades the pet out on 12, moves it one "
            "body-length forward (0.70 m) while it is unseen, and fades it in on 18." % (0.34 * S, 0.20 * S))

    # ---- the tails in the clips taken over from the first form
    use(bpy.data.actions["Walk"])
    last_euler.clear()
    flowing(1, 25, 24, loop=True, size=0.6)
    for frame in range(1, 26):
        u = (frame - 1) / 24.0
        for k in range(N):
            lag = 0.012 * k
            tail_one(k, frame, [(5.0 + 2.0 * i) * math.cos(2 * math.pi * (u - 0.12 * (i + 1) - lag)) for i in range(4)],
                     [-3.0 + 2.0 * math.cos(4 * math.pi * (u - 0.1 * i - lag * 0.6)) for i in range(4)], out=3.0)
    rig.animation_data.action = None
    rest()

    use(bpy.data.actions["Run"])
    last_euler.clear()
    flowing(1, 13, 12, loop=True, size=0.5)
    for frame in range(1, 14):
        u = (frame - 1) / 12.0
        for k in range(N):
            lag = 0.008 * k
            tail_one(k, frame, [2.0 * math.cos(2 * math.pi * (u - 0.1 * i - lag)) for i in range(4)],
                     [4.0 + (5.0 + 1.5 * i) * math.cos(2 * math.pi * (u - 0.10 * (i + 1) - lag)) for i in range(4)], out=1.0)
    rig.animation_data.action = None
    rest()

    use(bpy.data.actions["SpinChase"])
    last_euler.clear()
    flowing(1, 52, 26, size=0.7)
    #                 frame  curl (as in the first form's clip)
    for frame in range(1, 53):
        c = through([(1, 0.0), (5, -0.2), (8, -0.3), (11, 0.7), (13, 1.0), (17, 1.0), (21, 1.0), (26, 1.0), (31, 0.8), (36, 0.5),
                     (41, 0.2), (46, 0.0), (52, 0.0)], frame)
        tails(frame, sway=tuple(14.5 * c for _ in range(4)), lift=tuple(9.0 * c for _ in range(4)), spread=4.0 * max(c, 0.0))
    rig.animation_data.action = None
    rest()
    flow["on"] = None

    # every clip holds every control it does not move at rest, on its first and last frame (see anim_kitsune_25.py)
    names = mine + list(KEEP)
    for name in names:
        action = bpy.data.actions[name]
        keyed = {fc.data_path for layer in action.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves}
        use(action)
        first, last = int(action.frame_range[0]), int(action.frame_range[1])
        for b in pb:
            if b.name not in HELD:
                continue
            b.rotation_mode = "XYZ"
            b.location, b.rotation_euler, b.scale = (0, 0, 0), (0, 0, 0), (1, 1, 1)
            for path in ("location", "rotation_euler", "scale"):
                if path == "location" and b.name not in MOVED:
                    continue
                if path == "scale" and not b.name.startswith("eyelid"):
                    continue
                if 'pose.bones["%s"].%s' % (b.name, path) not in keyed:
                    for frame in (first, last):
                        b.keyframe_insert(path, frame=frame)
        rig.animation_data.action = None
    rest()

    for b in pb:                                              # the file is left standing at rest, whatever was keyed last
        if b.name.startswith(("MCH-", "ORG-", "DEF-", "VIS_")):
            continue      # never the rig's inner bones: Rigify keeps a starting bend on its IK bones
        b.location = (0, 0, 0)
        b.rotation_euler = (0, 0, 0)
        b.rotation_quaternion = (1, 0, 0, 0)
        b.scale = (1, 1, 1)
    scene.frame_set(1)
    mesh["kn_cues"] = "\n".join("%s: %s" % (n, CUES[n]) for n in ("Idle", "Walk", "Run", "SpinChase", "Shake", "Guard", "Pounce") + tuple(WHO) if n in names)
    print("KITSUNE %s clips (size x%.2f of the 25 %% form, %d tails):" % (stage, S, N))
    for n in ("Idle", "Walk", "Run", "SpinChase", "Shake", "Guard", "Pounce") + tuple(WHO):
        if n in names:
            x = bpy.data.actions[n]
            print("  CUE %s | %s" % (n, CUES[n]))


main()
