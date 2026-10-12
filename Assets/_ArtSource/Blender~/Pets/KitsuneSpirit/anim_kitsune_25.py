"""Kitsune Spirit, the 25 % form: its animations, keyed on the Rigify controls so they can be adjusted by hand.

  Idle       a loop, 8 s: both tails wag as waves running from base to tip, never quite in step;   192 frames
             the chest breathes; the head turns one way, waits, turns the other way, waits, and
             comes back; three blinks; ear twitches at uneven moments (frame 193 = frame 1)
  Walk       a loop, on the spot: the first form's walk, with both tails trailing and swaying       24 frames
  Run        a loop, on the spot: the first form's gallop, tails streaming and bouncing             12 frames
  SpinChase  the first form's spin after its tail; both tails curl into the turn                    52 frames
  Shake      shakes itself dry like a dog out of the bath: braces, the shake runs from the head     56 frames
             down the back and out along the tails, eyes shut, then it settles
  Guard      a defence stance: drops low with its paws spread wider, head down and level, ears      72 frames
             back, both tails curled up over its back with the tips pointing at its head; holds;
             stands down
  Pounce     a fox's mousing pounce, on the spot: crouches, wiggles its rump, springs high off its   56 frames
             hind legs, tips over at the top and dives nose-first onto its front paws, hind end
             coming down after; tails stream out behind in the air and fan as it lands

24 frames a second. Idle, Walk and Run loop; the others start and end on the rest pose.
Timings for effects and for blending are in README_Kitsune25.txt beside this file (CUES below is their source).

Walk, Run and SpinChase are not written again here: the first form's script (anim_kitsune.py) is run on this rig,
which has the same controls, and its three clips are kept; this script then adds the two tails to them.

Run after pet_rig.py:
  run_blender.py --blend SM_Pet_KitsuneSpirit_25.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/KitsuneSpirit/anim_kitsune_25.py
Running it again replaces these six actions (hand edits to them are lost; rename an action to keep it).
"""
import math
from pathlib import Path

import bpy
from mathutils import Euler, Matrix, Vector

RIG = "SM_Pet_KitsuneSpirit_25_Rig"
BASE_RIG = "SM_Pet_KitsuneSpirit_S1_Rig"
KEEP = ("Walk", "Run", "SpinChase")                       # the first form's clips this form takes over
STUB_TAIL = ("spine.003", "spine.002", "spine.001", "spine")
TAILS = {"L": ("tail_1.L", "tail_2.L", "tail_3.L", "tail_4.L"), "R": ("tail_1.R", "tail_2.R", "tail_3.R", "tail_4.R")}
MOVED = ("torso", "front_foot_ik.L", "front_foot_ik.R", "foot_ik.L", "foot_ik.R", "eyelid.L", "eyelid.R")       # controls that are slid as well as turned
HELD = MOVED + ("root", "hips", "chest", "neck", "head", "ear.L", "ear.R") + tuple(n for side in TAILS.values() for n in side)
IDLE_LAG = 0.25              # Idle: how far the right tail's wag runs behind the left's (radians of the wave)
IDLE_OPEN = (1.4, 0.5)      # Idle: degrees each tail bone is held away from the other tail: at the base, and more a bone
CUES = {
    "Idle": "loop 192. Head: turns left 31-48, holds to 80, turns right 80-104, holds to 132, centre by 152. "
            "Blinks shut on 26, 92, 158. Ear twitches start 60 (left), 116 (right), 168 (left) and 170 (right).",
    "Shake": "56. Braces 1-8; shaking 9-40 (strongest 14-34, one side-to-side every 4 frames); eyes shut 7-43; "
             "settles 41-56. Spray of drops: 12 to 38.",
    "Pounce": "56. Crouches and loads 1-12; rump wiggle 10-18; springs 18-21 (hind paws leave the ground on 21); in the air "
              "21-32, highest on 26-27; front paws land 32, hind paws 36; takes the landing and settles 36-50; rest to 56.",
    "Guard": "72. Drops into the stance 1-12 (tails are fully curled by 16); holds 12-54; stands down 54-68; rest to 72. "
             "To hold the stance longer, stop on any frame from 16 to 54.",
}


def curves_of(action):
    try:
        found = list(action.fcurves)
    except Exception:
        found = []
    if not found and hasattr(action, "layers"):
        found = [fc for layer in action.layers for strip in layer.strips
                 for bag in getattr(strip, "channelbags", []) for fc in bag.fcurves]
    return found


def main():
    rig = bpy.data.objects[RIG]
    pb = rig.pose.bones
    scene = bpy.context.scene
    scene.render.fps = 24

    # ---- the first form's clips, made on this rig by its own script
    source = (Path(__file__).parent / "anim_kitsune.py").read_text(encoding="utf-8")
    quoted = 'RIG = "%s"' % BASE_RIG
    if quoted not in source:
        raise SystemExit("anim_kitsune.py no longer names its rig the way this script expects.")
    for name in ("Idle", "Shake", "Guard", "Pounce") + KEEP:
        old = bpy.data.actions.get(name)
        if old:
            bpy.data.actions.remove(old)
    exec(compile(source.replace(quoted, 'RIG = "%s"' % RIG), "anim_kitsune.py", "exec"), {"__name__": "kitsune_base"})
    for action in list(bpy.data.actions):
        if action.name in ("Idle", "Blink", "LookAround", "PawLift", "TailCurl"):
            bpy.data.actions.remove(action)
    # the first form's clips swing its one tail. Here that tail is a stub inside the rump: its keys go, or the rump twitches
    for action in bpy.data.actions:
        if action.name in KEEP:
            for layer in action.layers:
                for strip in layer.strips:
                    for bag in strip.channelbags:
                        for fc in [fc for fc in bag.fcurves if any('pose.bones["%s"]' % n in fc.data_path for n in STUB_TAIL)]:
                            bag.fcurves.remove(fc)

    touched = set()
    last_euler = {}

    def use(action):
        rig.animation_data_create()
        rig.animation_data.action = action
        if getattr(rig.animation_data, "action_slot", None) is None and getattr(action, "slots", None):
            rig.animation_data.action_slot = action.slots[0]

    def start(name):
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
        """Move a control by a distance given in the scene's own directions (x to its left, -y forward, z up)."""
        b = pb[name]
        touched.add(name)
        b.location = (rig.matrix_world @ b.bone.matrix_local).to_3x3().inverted() @ Vector(world)
        b.keyframe_insert("location", frame=frame)

    lid_turn = {side: float(bpy.data.objects[RIG[:-4]]["kn_lid_turn_" + side]) for side in ("L", "R")}

    def lid(frame, shut):
        """Eyelids: 0 open, 1 shut. Each lid is a shell that rolls down over the eyeball about the eye's long axis."""
        for side in ("L", "R"):
            b = pb["eyelid." + side]
            touched.add(b.name)
            b.rotation_mode = "XYZ"
            b.rotation_euler = (0.0, math.radians(lid_turn[side] * shut), 0.0)
            b.keyframe_insert("rotation_euler", frame=frame)

    def blink(at, hold=2):
        lid(at, 0.0)
        lid(at + 2, 1.0)
        lid(at + 2 + hold, 1.0)
        lid(at + 6 + hold, 0.0)

    def ear(side, frame, back=0.0, out=0.0):
        swing("ear." + side, frame, nose_down=-back, roll=out * (1 if side == "L" else -1))

    def flick(side, at, size=1.0):
        """An ear twitch: snaps back, springs past, settles. Eight frames."""
        for offset, amount in ((0, 0.0), (2, 1.0), (4, -0.35), (6, 0.12), (8, 0.0)):
            ear(side, at + offset, back=24.0 * size * amount, out=7.0 * size * amount)

    def tails(frame, sway=(0, 0, 0, 0), lift=(0, 0, 0, 0), spread=0.0, sway_r=None, lift_r=None):
        """Both tails. sway: each bone's turn to the fox's left, base to tip. lift: each bone's turn up and over
        towards the head. spread: the two tails opened apart (+) or closed (-) at the base. The right tail takes
        sway_r / lift_r when given, so the two need not move as one."""
        for side, names in TAILS.items():
            s = sway if side == "L" or sway_r is None else sway_r
            l = lift if side == "L" or lift_r is None else lift_r
            for i, name in enumerate(names):
                apart = spread * (1 if side == "L" else -1) if i == 0 else 0.0
                swing(name, frame, nose_down=l[i], left=s[i] - apart)

    def wave(frame, period, phase=0.0):
        return math.sin(2 * math.pi * (frame - 1) / period + phase)

    def ease(t):
        t = max(0.0, min(1.0, t))
        return t * t * (3 - 2 * t)

    def track(points, frame):
        """A value through (frame, value) points, eased between them."""
        if frame <= points[0][0]:
            return points[0][1]
        for (f0, v0), (f1, v1) in zip(points[:-1], points[1:]):
            if f0 <= frame <= f1:
                return v0 + (v1 - v0) * ease((frame - f0) / max(f1 - f0, 1e-9))
        return points[-1][1]

    # ---- Idle
    a = start("Idle")
    LOOP = 192
    look = [(1, 0), (30, 0), (48, 34), (80, 34), (104, -32), (132, -32), (152, 0), (LOOP + 1, 0)]      # head to its left (+) and right (-)
    for frame in range(1, LOOP + 2, 3):
        # tails: a wave that runs down each tail (every bone lags the one before), with a slower swing on top so no
        # two wags are the same. The two tails wag as a pair: the right one the same wave a moment later. They may
        # come close but never pass through each other: a moment's lag alone would close them onto one another, so
        # each bone is also held open, away from the other tail, by a little more than the lag can close it.
        def wag(lag):
            return [(9.0 + 3.5 * i) * wave(frame, 48, -0.85 * i - lag) + (4.0 + i) * wave(frame, 96, 0.7 - 0.5 * i - lag * 0.6)
                    + 2.0 * wave(frame, 192, 1.9 - lag) for i in range(4)]

        def rise(lag):
            return tuple(2.5 * wave(frame, 96, 1.2 - 0.45 * i - lag) + 1.5 * wave(frame, 192, 0.3 - lag) for i in range(4))

        apart = [IDLE_OPEN[0] + IDLE_OPEN[1] * i + 0.6 * wave(frame, 192, 0.5) for i in range(4)]
        # (a tail points backwards, so a turn "to the left" swings its tip to the fox's right: the left tail opens with a minus)
        tails(frame, sway=tuple(w - o for w, o in zip(wag(0.0), apart)), sway_r=tuple(w + o for w, o in zip(wag(IDLE_LAG), apart)),
              lift=rise(0.0), lift_r=rise(0.8))
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
    blink(156, hold=3)
    finish(a, 1, LOOP + 1)

    # ---- Shake
    a = start("Shake")
    #              frame  how hard
    hard = [(1, 0.0), (8, 0.0), (14, 1.0), (34, 1.0), (40, 0.25), (44, 0.0), (56, 0.0)]
    brace = [(1, 0.0), (8, 1.0), (38, 1.0), (48, -0.25), (56, 0.0)]
    for frame in range(1, 57):
        k, low = track(hard, frame), track(brace, frame)

        def shake(lag):
            """One side-to-side every 4 frames; each part of the body a little later than the part ahead of it."""
            return math.sin(2 * math.pi * (frame - 8 - lag) / 4.0) * track(hard, frame - lag)

        shift("torso", frame, (0.0, 0.0, -0.010 * max(low, 0.0) + 0.004 * max(-low, 0.0)))
        swing("head", frame, roll=30.0 * shake(0.0), left=7.0 * shake(0.4), nose_down=7.0 * max(low, 0.0) - 4.0 * max(-low, 0.0))
        swing("neck", frame, roll=14.0 * shake(0.5))
        swing("chest", frame, roll=13.0 * shake(1.0), left=3.0 * shake(1.3))
        swing("hips", frame, roll=-11.0 * shake(2.0), left=-4.0 * shake(2.3))
        tails(frame, sway=tuple((13.0 + 5.0 * i) * shake(2.6 + 0.7 * i) for i in range(4)),
              sway_r=tuple((13.0 + 5.0 * i) * shake(2.9 + 0.7 * i) for i in range(4)),
              lift=tuple(5.0 * k for _ in range(4)), spread=8.0 * min(1.0, 4.0 * k))      # held apart while they whip, home at rest
        for side in ("L", "R"):                               # the ears flop against the shake
            ear(side, frame, back=10.0 * k, out=-16.0 * shake(0.8) * (1 if side == "L" else -1))
    for frame, shut in ((1, 0.0), (5, 0.0), (7, 1.0), (43, 1.0), (47, 0.0), (50, 0.0), (52, 1.0), (53, 1.0), (56, 0.0)):
        lid(frame, shut)
    finish(a, 1, 56)

    # ---- Guard
    a = start("Guard")
    stance = [(1, 0.0), (12, 1.0), (54, 1.0), (68, 0.0), (72, 0.0)]
    curl = [(1, 0.0), (5, -0.12), (16, 1.0), (54, 1.0), (66, -0.06), (72, 0.0)]
    for frame in list(range(1, 73, 2)) + [72]:
        s, c = track(stance, frame), track(curl, frame)
        breath = wave(frame, 36) * s
        for side, way in (("L", 1.0), ("R", -1.0)):          # paws spread wider, front ones a little forward
            shift("front_foot_ik." + side, frame, (0.024 * s * way, -0.010 * s, 0.0))
            shift("foot_ik." + side, frame, (0.020 * s * way, 0.010 * s, 0.0))
            swing("front_foot_ik." + side, frame, left=-9.0 * s * way)
            swing("foot_ik." + side, frame, left=-7.0 * s * way)
            ear(side, frame, back=20.0 * s, out=6.0 * s)
        shift("torso", frame, (0.0, 0.004 * s, -0.020 * s + 0.0012 * breath))
        swing("chest", frame, nose_down=7.0 * s + 0.6 * breath)
        swing("hips", frame, nose_down=-3.0 * s)
        swing("neck", frame, nose_down=9.0 * s)
        swing("head", frame, nose_down=-14.0 * s)                        # head low but looking ahead
        tip = 3.0 * wave(frame, 24, 0.5) * s                             # the tips never quite still
        tails(frame, lift=(40.0 * c, 44.0 * c, 46.0 * c + tip, 38.0 * c + 2 * tip), spread=7.0 * c,
              sway=(0.0, 0.0, 1.5 * tip, 2.5 * tip), sway_r=(0.0, 0.0, -1.5 * tip, -2.5 * tip))
    lid(1, 0.0)
    for frame, shut in ((8, 0.0), (11, 0.42), (54, 0.42), (60, 0.0), (72, 0.0)):       # eyes narrowed
        lid(frame, shut)
    finish(a, 1, 72)

    # ---- Pounce. The whole fox rides on the root, which carries the paws with it.
    # On the ground the root tips about the paws that carry the weight (hind paws to spring, front paws to land).
    # In the air nothing holds it up, so its middle follows the path a thrown thing follows: steady forward, and up
    # and down as gravity has it (the height it reaches is what 11 frames in the air allows, a quarter of a metre),
    # while it turns over at a steady rate about its own middle, not about the ground.
    # Around that: it sinks and loads before it springs, stretches out as it leaves, tucks at the top, reaches for
    # the ground, and takes the landing in its legs, front then hind, with the head, ears and tails each arriving
    # a little late and swinging past before they settle.
    a = start("Pounce")
    OFF, DOWN, HIND_DOWN = 21, 32, 36                       # hind paws leave; front paws land; hind paws land
    front_y = abs(pb["front_foot_ik.L"].bone.head_local.y)
    hind_y = abs(pb["foot_ik.L"].bone.head_local.y)
    middle = 0.17                                           # how high its middle is, standing
    TIP_OFF, TIP_DOWN = -24.0, 27.0                         # nose up as it springs, nose down as it lands

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
        """Something that arrives late and swings past: a fading swing that starts at `since`."""
        t = frame - since
        return size * math.exp(-t / fade) * math.sin(2 * math.pi * t / beat) if t > 0 else 0.0

    def carried(tip):
        """Where the root must be for the fox to tip by `tip` about the paws that carry it, and where that puts its middle."""
        lift = (hind_y if tip < 0 else front_y) * abs(math.sin(math.radians(tip)))
        return lift, Vector((0.0, -middle * math.sin(math.radians(tip)), middle * math.cos(math.radians(tip)) + lift))

    _l, left_at = carried(TIP_OFF)                          # its middle as the hind paws leave, and as the front paws land
    _l, land_at = carried(TIP_DOWN)
    air = (DOWN - OFF) / 24.0
    rise = 9.81 * air * air / 8.0                           # how far above the straight line between the two it flies

    crouch = [(1, 0.0), (7, 0.55), (12, 1.0), (17, 1.18), (19, 0.6), (OFF, -0.25), (24, 0.0), (DOWN, 0.0), (34, 1.05), (HIND_DOWN, 0.7),
              (38, 1.15), (42, -0.12), (46, 0.05), (50, 0.0), (56, 0.0)]
    for frame in range(1, 57):
        if frame <= 18:
            tip, root_at = 0.0, Vector((0, 0, 0))
        elif frame <= OFF:                                  # springing: over the hind paws
            tip = TIP_OFF * ((frame - 18) / (OFF - 18)) ** 1.6
            root_at = Vector((0, 0, carried(tip)[0]))
        elif frame < DOWN:                                  # in the air
            t = (frame - OFF) / (DOWN - OFF)
            tip = TIP_OFF + (TIP_DOWN - TIP_OFF) * t
            at = left_at.lerp(land_at, t) + Vector((0, 0, rise * 4 * t * (1 - t)))
            root_at = at - Vector((0.0, -middle * math.sin(math.radians(tip)), middle * math.cos(math.radians(tip))))
        elif frame <= HIND_DOWN:                            # landing: over the front paws, the hind end coming down
            t = (frame - DOWN) / (HIND_DOWN - DOWN)
            tip = TIP_DOWN * (1 - t) ** 1.5
            root_at = Vector((0, 0, carried(tip)[0]))
        else:
            tip, root_at = 0.0, Vector((0, 0, 0))
        swing("root", frame, nose_down=tip)
        shift("root", frame, root_at)
        c = through(crouch, frame)
        back = through([(1, 0), (12, 1.0), (17, 1.25), (OFF, 0.0), (56, 0.0)], frame)              # weight drawn back onto the haunches
        shift("torso", frame, (0.0, 0.010 * back, -0.027 * c))
        stretch = through([(1, 0), (18, 0), (OFF, -1.0), (24, -0.5), (27, 0.9), (30, 0.3), (DOWN, -0.4), (34, 0.9), (38, 0.3), (44, 0.0), (56, 0)], frame)
        swing("chest", frame, nose_down=8.0 * max(c, 0.0) + 9.0 * stretch)          # arched back at the top, long as it leaves and reaches
        wiggle = math.sin(2 * math.pi * (frame - 10) / max(4.6 - 0.2 * (frame - 10), 2.6)) * through([(9, 0), (11, 1.0), (16, 1.0), (18, 0)], frame)
        swing("hips", frame, nose_down=-6.0 * max(c, 0.0) - 8.0 * stretch, left=8.0 * wiggle, roll=3.0 * wiggle)
        # the head stays on the spot it is pouncing at: it undoes most of the body's tipping, and nods as it lands
        aim = through([(1, 0), (8, 12), (17, 16), (OFF, 14), (DOWN, 10), (40, 0), (56, 0)], frame)
        nod = swing_past(frame, DOWN, 9.0, beat=9.0) + swing_past(frame, HIND_DOWN, 4.0, beat=8.0)
        swing("neck", frame, nose_down=0.4 * aim - 0.30 * tip + 0.4 * nod)
        swing("head", frame, nose_down=0.6 * aim - 0.38 * tip + 0.6 * nod, left=-1.5 * wiggle)
        # ears: pricked forward at the target, laid back by the air, flopping forward on the landing
        ears_ = through([(1, 0), (8, -9), (18, -11), (OFF, 6), (25, 24), (30, 26), (DOWN, 20), (56, 0)], frame) if frame <= DOWN else 0.0
        if frame > DOWN:
            ears_ = 20.0 * math.exp(-(frame - DOWN) / 3.0) - swing_past(frame, DOWN, 16.0, beat=8.0, fade=5.0)
        for side in ("L", "R"):
            ear(side, frame, back=ears_)
        # tails: counterweights in the wiggle; left behind as it springs; floating up over the top; streaming up behind
        # in the dive; thrown forward over its back as it stops, then swinging to rest
        drag = through([(1, 0), (10, 5), (18, 8), (OFF, -16), (24, -20), (27, 4), (30, 26), (DOWN, 34), (56, 0)], frame) if frame <= DOWN else 0.0
        if frame > DOWN:
            drag = 34.0 * math.exp(-(frame - DOWN) / 5.0) + swing_past(frame, DOWN, 24.0, beat=11.0, fade=7.0)
        apart = through([(1, 0), (10, 2), (18, 3), (OFF, 2), (27, 6), (DOWN, 9), (38, 7), (46, 2), (52, 0), (56, 0)], frame)
        tails(frame, lift=(drag * 0.45, drag * 0.8, drag * 1.0, drag * 0.9), spread=apart,
              sway=tuple(-9.0 * wiggle * (0.6 + 0.3 * i) for i in range(4)))
        # paws: hind ones push off stretched out behind, then tuck; front ones tuck, then reach down and ahead for the ground
        hind_y_ = through([(18, 0), (OFF, 0.0), (23, -0.034), (26, 0.016), (29, 0.040), (DOWN, 0.034), (HIND_DOWN, 0.0), (56, 0)], frame)
        hind_z_ = through([(18, 0), (OFF, 0.0), (23, 0.010), (26, 0.046), (29, 0.050), (DOWN, 0.030), (HIND_DOWN, 0.0), (56, 0)], frame)
        front_y_ = through([(18, 0), (OFF, 0.014), (24, 0.022), (27, 0.004), (30, 0.040), (DOWN, 0.018), (35, 0.0), (56, 0)], frame)
        front_z_ = through([(18, 0), (OFF, 0.034), (24, 0.052), (27, 0.046), (30, 0.016), (DOWN, 0.0), (56, 0)], frame)
        for side in ("L", "R"):
            shift("foot_ik." + side, frame, (0.0, -hind_y_, max(hind_z_, 0.0)))
            shift("front_foot_ik." + side, frame, (0.0, -front_y_, max(front_z_, 0.0)))
            swing("foot_ik." + side, frame, nose_down=through([(18, 0), (OFF, 22), (24, 30), (28, -6), (HIND_DOWN, 0), (56, 0)], frame))
            swing("front_foot_ik." + side, frame, nose_down=through([(18, 0), (OFF, 16), (25, 26), (30, -14), (DOWN, 0), (56, 0)], frame))
    lid(1, 0.0)
    for frame, shut in ((13, 0.0), (15, 0.4), (19, 0.0), (DOWN - 1, 0.0), (DOWN + 1, 1.0), (37, 1.0), (41, 0.0), (56, 0.0)):
        lid(frame, shut)
    finish(a, 1, 56)
    print("KITSUNE25 Pounce: in the air %d frames (%.2f s), rising %.3f m above the line from take-off to landing; its middle travels %.3f m forward"
          % (DOWN - OFF, air, rise, left_at.y - land_at.y))

    # ---- the two tails in the clips taken over from the first form
    walk = bpy.data.actions["Walk"]
    use(walk)
    last_euler.clear()
    for frame in range(1, 26):
        u = (frame - 1) / 24.0

        def trail(lag):
            return tuple((5.0 + 2.0 * i) * math.cos(2 * math.pi * (u - 0.12 * (i + 1) - lag)) for i in range(4))

        tails(frame, sway=trail(0.0), sway_r=trail(0.14),
              lift=tuple(-3.0 + 2.0 * math.cos(4 * math.pi * (u - 0.1 * i)) for i in range(4)),
              lift_r=tuple(-3.0 + 2.0 * math.cos(4 * math.pi * (u - 0.1 * i - 0.08)) for i in range(4)), spread=4.0)
    rig.animation_data.action = None
    rest()

    run = bpy.data.actions["Run"]
    use(run)
    last_euler.clear()
    for frame in range(1, 14):
        u = (frame - 1) / 12.0
        tails(frame, lift=tuple(4.0 + (5.0 + 1.5 * i) * math.cos(2 * math.pi * (u - 0.10 * (i + 1))) for i in range(4)),
              lift_r=tuple(4.0 + (5.0 + 1.5 * i) * math.cos(2 * math.pi * (u - 0.10 * (i + 1) - 0.07)) for i in range(4)),
              sway=tuple(2.0 * math.cos(2 * math.pi * (u - 0.1 * i)) for i in range(4)), spread=1.5)
    rig.animation_data.action = None
    rest()

    spin = bpy.data.actions["SpinChase"]
    use(spin)
    last_euler.clear()
    #                 frame  curl (as in the first form's clip)
    for frame, c in ((1, 0.0), (5, -0.2), (8, -0.3), (11, 0.7), (13, 1.0), (17, 1.0), (21, 1.0), (26, 1.0), (31, 0.8), (36, 0.5),
                     (41, 0.2), (46, 0.0), (52, 0.0)):
        tails(frame, sway=tuple(16.0 * c for _ in range(4)), sway_r=tuple(13.0 * c for _ in range(4)),
              lift=tuple(9.0 * c for _ in range(4)), spread=5.0 * c)
    rig.animation_data.action = None
    rest()

    # every clip holds every control it does not move at rest, on its first and last frame. Otherwise a control keeps
    # the pose the clip shown before it left behind: play Idle after Walk in Blender and it stands on a walking paw.
    for name in ("Idle", "Shake", "Guard", "Pounce") + KEEP:
        action = bpy.data.actions[name]
        keyed = {fc.data_path for layer in action.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves}
        use(action)
        first, last = int(action.frame_range[0]), int(action.frame_range[1])
        for b in pb:
            if b.name not in HELD:
                continue
            b.rotation_mode = "XYZ"
            b.location, b.rotation_euler = (0, 0, 0), (0, 0, 0)
            for path in ("location", "rotation_euler"):
                if 'pose.bones["%s"].%s' % (b.name, path) not in keyed and not (path == "location" and b.name not in MOVED):
                    for frame in (first, last):
                        b.keyframe_insert(path, frame=frame)
        rig.animation_data.action = None
    rest()

    for b in pb:                                              # the file is left standing at rest, whatever was keyed last
        if b.name.startswith(("MCH-", "ORG-", "DEF-", "VIS_")):
            continue      # never the rig's inner bones: Rigify keeps a starting bend on its IK bones, and wiping it folds the paws at rest
        b.location = (0, 0, 0)
        b.rotation_euler = (0, 0, 0)
        b.rotation_quaternion = (1, 0, 0, 0)
        b.scale = (1, 1, 1)
    scene.frame_set(1)
    print("KITSUNE25 clips:", {x.name: int(x.frame_range[1] - x.frame_range[0]) + (0 if x.name in ("Idle", "Walk", "Run") else 1)
                               for x in bpy.data.actions if x.name in ("Idle", "Shake", "Guard", "Pounce") + KEEP})


main()
