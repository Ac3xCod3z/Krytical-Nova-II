"""Kitsune Spirit: first-pass animations, keyed on the Rigify controls so they can be adjusted by hand.

  Idle        a loop: tail sways, chest breathes, two blinks, ear       144 frames
              flicks at uneven moments (frame 145 = frame 1)
  Blink       both eyelids shut and open                                 12 frames
  LookAround  head turns left, holds, right, holds, back                 60 frames
  PawLift     left front paw lifts, holds, sets down                     36 frames
  TailCurl    tail curls up, holds, lets go                              32 frames
  SpinChase   dips onto its front legs, pushes off them into a fast   52 frames
              spin after its tail (head leading, stepping on planted feet)
              that slows as it comes round, and settles into rest

24 frames a second. Idle loops; every other action starts and ends on the rest pose. Run after pet_rig.py:
  run_blender.py --blend SM_Pet_KitsuneSpirit_S1.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/KitsuneSpirit/anim_kitsune.py
Running it again replaces these six actions (hand edits to them are lost; rename an action to keep it).
"""
import math

import bpy
from mathutils import Euler, Vector

RIG = "SM_Pet_KitsuneSpirit_S1_Rig"
TAIL = ("spine.003", "spine.002", "spine.001", "spine")     # base to tip


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
    touched = set()

    def start(name):
        old = bpy.data.actions.get(name)
        if old:
            bpy.data.actions.remove(old)
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        rig.animation_data_create()
        rig.animation_data.action = action
        return action

    def rest():
        for name in touched:
            b = pb[name]
            b.location = (0, 0, 0)
            b.rotation_mode = "XYZ"
            b.rotation_euler = (0, 0, 0)

    def turn(name, frame, x=0.0, y=0.0, z=0.0):
        b = pb[name]
        touched.add(name)
        b.rotation_mode = "XYZ"
        b.rotation_euler = Euler((math.radians(x), math.radians(y), math.radians(z)))
        b.keyframe_insert("rotation_euler", frame=frame)

    def slide(name, frame, along_self):
        """Move a control along its own length (eyelids)."""
        b = pb[name]
        touched.add(name)
        b.location = (0, b.bone.length * along_self, 0)
        b.keyframe_insert("location", frame=frame)

    def shift(name, frame, world):
        """Move a control by a distance given in the scene's own directions (z up, -y forward)."""
        b = pb[name]
        touched.add(name)
        b.location = (rig.matrix_world @ b.bone.matrix_local).to_3x3().inverted() @ Vector(world)
        b.keyframe_insert("location", frame=frame)

    def finish(action, first, last, straight=()):
        action.frame_range = (first, last) if hasattr(action, "frame_range") else None
        try:
            action.use_frame_range = True
            action.frame_start, action.frame_end = first, last
        except Exception:
            pass
        for fc in curves_of(action):
            if any(('"%s"' % name) in fc.data_path for name in straight):
                for key in fc.keyframe_points:
                    key.interpolation = "LINEAR"
        rig.animation_data.action = None
        rest()

    made = []

    # ---- Idle: a 6 second loop. The tail sways as a wave running from its base to its tip, the chest
    # breathes, the head drifts a little, the eyes blink twice and the ears flick now and then, at uneven
    # moments so it does not read as a metronome. Frame 145 is the same pose as frame 1, so it loops.
    a = start("Idle")
    LOOP = 144

    def wave(frame, period, phase=0.0):
        return math.sin(2 * math.pi * (frame - 1) / period + phase)

    def yaw_sense(name):
        return 1.0 if (pb[name].bone.matrix_local.to_3x3() @ Vector((0, 0, 1))).z >= 0 else -1.0

    for frame in range(1, LOOP + 2, 4):
        # tail: each control lags the one before it, so the sway travels down the tail; a slower, smaller
        # swing on top keeps one sway from looking exactly like the next
        for i, name in enumerate(TAIL[:3]):
            side = (6.0 + i) * wave(frame, 72, -0.9 * i) + 2.0 * wave(frame, 144, 0.6 - 0.5 * i)
            lift = 1.8 * wave(frame, 144, 1.3 - 0.4 * i)
            turn(name, frame, z=side * yaw_sense(name), x=lift)
        # breathing, and the head riding on it
        shift("torso", frame, (0.0, 0.0, 0.0012 * wave(frame, 48)))
        turn("chest", frame, x=0.7 * wave(frame, 48, 0.4))
        turn("head", frame, z=3.0 * wave(frame, 144, 0.8) * yaw_sense("head"), x=1.4 * wave(frame, 72, 1.0))
        turn("neck", frame, z=1.2 * wave(frame, 144, 0.5) * yaw_sense("neck"))

    def flick(name, at, size=1.0):
        """An ear twitch: snaps back, springs past, settles. Eight frames."""
        for offset, amount in ((0, 0.0), (2, -1.0), (4, 0.35), (6, -0.12), (8, 0.0)):
            turn(name, at + offset, x=22.0 * size * amount, z=6.0 * size * amount)

    for name in ("ear.L", "ear.R"):
        turn(name, 1)
        turn(name, LOOP + 1)
    flick("ear.L", 21)
    flick("ear.R", 58, 0.8)
    flick("ear.L", 96, 0.5)
    flick("ear.R", 119)
    flick("ear.L", 121, 0.9)          # both, a beat apart

    def blink(at, hold=2):
        for side in ("L", "R"):
            slide("eyelid." + side, at, 0.0)
            slide("eyelid." + side, at + 2, 1.0)
            slide("eyelid." + side, at + 2 + hold, 1.0)
            slide("eyelid." + side, at + 6 + hold, 0.0)

    for side in ("L", "R"):
        slide("eyelid." + side, 1, 0.0)
        slide("eyelid." + side, LOOP + 1, 0.0)
    blink(40)
    blink(104, hold=1)
    finish(a, 1, LOOP + 1)
    made.append(("Idle", LOOP))

    # ---- Blink
    a = start("Blink")
    for side in ("L", "R"):
        for frame, shut in ((1, 0.0), (3, 1.0), (6, 1.0), (10, 0.0), (12, 0.0)):
            slide("eyelid." + side, frame, shut)
    finish(a, 1, 12)
    made.append(("Blink", 12))

    # ---- LookAround
    a = start("LookAround")
    for frame, yaw in ((1, 0), (14, 40), (22, 40), (38, -40), (46, -40), (60, 0)):
        turn("head", frame, z=yaw)
        turn("neck", frame, z=yaw * 0.35)
    finish(a, 1, 60)
    made.append(("LookAround", 60))

    # ---- PawLift
    a = start("PawLift")
    for frame, lift in ((1, 0.0), (10, 1.0), (20, 1.0), (30, 0.0), (36, 0.0)):
        shift("front_foot_ik.L", frame, (0.0, -0.03 * lift, 0.05 * lift))
        turn("chest", frame, y=-4 * lift)
        turn("head", frame, x=8 * lift)
    finish(a, 1, 36)
    made.append(("PawLift", 36))

    # ---- TailCurl
    a = start("TailCurl")
    for frame, curl in ((1, 0.0), (10, 1.0), (20, 1.0), (28, -0.15), (32, 0.0)):
        for i, name in enumerate(TAIL[:3]):
            turn(name, frame, x=(26 + 6 * i) * curl)
    finish(a, 1, 32)
    made.append(("TailCurl", 32))

    # ---- SpinChase: the tail hangs to the fox's left, so it chases it round to the left.
    # It dips onto its front legs and pushes off them: the front end hops up and round, which gives the turn
    # its speed, fast at first and slowing as it comes round. The head leads into the turn the whole way.
    # The body turns about its hindquarters. The feet do not ride round with the body: each stays planted
    # while the body turns over it, then lifts and swings round to land ahead again; the faster the body is
    # turning, the quicker the steps. Diagonal pairs step together: left-front with right-hind, then
    # right-front with left-hind.
    a = start("SpinChase")
    from mathutils import Matrix

    def sense(name, axis):
        """+1 or -1: which way a positive turn of this control about its own axis goes, seen from the scene.
        axis "z" is compared with up (a left turn), axis "x" with the fox's left-to-right line (nose down)."""
        m = pb[name].bone.matrix_local.to_3x3()
        if axis == "z":
            return 1.0 if (m @ Vector((0, 0, 1))).z >= 0 else -1.0
        return 1.0 if (m @ Vector((1, 0, 0))).x >= 0 else -1.0

    left = {n: sense(n, "z") for n in ("root", "chest", "hips", "head", "neck") + TAIL[:3]}
    down = {n: sense(n, "x") for n in ("chest", "head")}

    #          frame  spin  curl  look  dip (front end down +, up -)
    beats = [(1,      0,    0.0,  0.0,  0.0),
             (5,     -6,    -0.2, 0.5,  0.7),       # sinks onto the front legs, already looking round
             (8,     -10,   -0.3, 0.9,  1.0),       # lowest: loaded
             (11,     50,   0.7,  1.0,  -0.9),      # pushes off: front end up and round
             (13,     95,   1.0,  1.0,  -0.2),      # front paws land
             (17,    165,   1.0,  1.0,  0.0),
             (21,    225,   1.0,  1.0,  0.0),
             (26,    280,   1.0,  1.0,  0.0),
             (31,    322,   0.8,  0.9,  0.0),
             (36,    350,   0.5,  0.6,  0.0),
             (41,    366,   0.2,  0.2,  0.0),       # overshoots over planted feet
             (46,    357,   0.0,  -0.05, 0.0),
             (52,    360,   0.0,  0.0,  0.0)]
    for frame, spin, curl, look, dip in beats:
        turn("root", frame, z=spin * left["root"])
        turn("chest", frame, z=22 * curl * left["chest"], x=13 * dip * down["chest"])
        turn("hips", frame, z=-22 * curl * left["hips"])
        turn("head", frame, z=40 * look * left["head"], x=(6 * look - 8 * dip) * down["head"])
        turn("neck", frame, z=16 * look * left["neck"])
        for i, name in enumerate(TAIL[:3]):
            turn(name, frame, z=16 * curl * left[name], x=10 * curl)
        shift("torso", frame, (0.0, 0.0, -0.012 * max(dip, 0.0) + 0.016 * max(-dip, 0.0)))
    for fc in curves_of(a):
        if '"root"' in fc.data_path:
            for key in fc.keyframe_points:
                key.interpolation = "LINEAR"

    FIRST, LAST = 1, 52
    PUSH, LAND = 8, 13                        # the front paws leave the ground and come down again
    HIND_HOP = 10                             # the hind paws skip round a little later
    STEP_TURN = 72.0                          # degrees of body turn per full step of one foot
    FULL = 360.0
    feet = {"front_foot_ik.L": (0.0, 0.030, True), "foot_ik.R": (0.0, 0.016, False),   # (step offset, lift, front?)
            "front_foot_ik.R": (0.5, 0.030, True), "foot_ik.L": (0.5, 0.016, False)}

    def spin_at(frame):
        """The body's turn in degrees at a frame, straight lines between the beats."""
        frame = max(FIRST, min(LAST, frame))
        for (f0, s0, *_), (f1, s1, *_) in zip(beats[:-1], beats[1:]):
            if f0 <= frame <= f1:
                return s0 + (s1 - s0) * (frame - f0) / (f1 - f0)
        return beats[-1][1]

    def ease(t):
        t = max(0.0, min(1.0, t))
        return t * t * (3 - 2 * t)

    turn_at_landing = spin_at(LAND)
    spin_done = next(f for f in range(LAND, LAST + 1) if spin_at(f) >= FULL)
    root_rest = pb["root"].bone.matrix_local.copy()
    scene_frame = scene.frame_current
    root_pose = {}
    for frame in range(FIRST, LAST + 1):
        scene.frame_set(frame)
        root_pose[frame] = pb["root"].matrix.copy()        # where the root really is, in the rig's space

    for name, (offset, lift, front) in feet.items():
        rest_m = pb[name].bone.matrix_local.copy()
        home = rest_m.translation.copy()
        radius, angle0 = math.hypot(home.x, home.y), math.atan2(home.y, home.x)
        touched.add(name)
        pb[name].rotation_mode = "XYZ"

        def planted(step):
            """Where the foot is set down on a given step: half a stance ahead of the body."""
            return max(0.0, min(FULL, turn_at_landing + (step + 0.75 - offset) * STEP_TURN))

        # after the hop one pair is straight into its next step, the other is mid-stance: land each accordingly
        first_landing = planted(0) if offset else turn_at_landing
        leave = PUSH if front else HIND_HOP
        track, value, swing_from, in_swing, finish_from = {}, 0.0, 0.0, None, None
        for frame in range(FIRST, LAST + 1):
            height = 0.0
            if frame <= leave:
                value = 0.0                                             # planted while it winds up
            elif frame <= LAND:
                t = (frame - leave) / (LAND - leave)                    # the push-off hop
                value = first_landing * ease(t)
                height = (0.055 if front else 0.022) * math.sin(math.pi * t)
            elif frame <= spin_done:
                phase = (min(spin_at(frame), FULL) - turn_at_landing) / STEP_TURN + offset
                step, part = math.floor(phase), phase - math.floor(phase)
                if part < 0.5:                                          # in the air
                    if in_swing != step:
                        in_swing, swing_from = step, value
                    t = part / 0.5
                    value = swing_from + (planted(step) - swing_from) * ease(t)
                    height = lift * math.sin(math.pi * t)
                else:
                    value = planted(step)
            else:
                if finish_from is None:
                    finish_from = value
                t = (frame - spin_done) / 3.0                           # last small step home
                value = finish_from + (FULL - finish_from) * ease(t)
                height = lift * 0.6 * math.sin(math.pi * min(t, 1.0)) if abs(FULL - finish_from) > 1.0 else 0.0
            track[frame] = (value, height)

        for frame in range(FIRST, LAST + 1):
            turned, height = track[frame]
            turned = math.radians(turned) * left["root"]
            ang = angle0 + turned
            world = Matrix.Translation((radius * math.cos(ang), radius * math.sin(ang), home.z + height)) \
                @ Matrix.Rotation(turned, 4, "Z") @ rest_m.to_3x3().to_4x4()
            # the control rides on the root; take the root's motion back out to get what to key
            local = rest_m.inverted() @ root_rest @ root_pose[frame].inverted() @ world
            b = pb[name]
            b.location = local.translation
            b.rotation_euler = local.to_euler("XYZ")
            b.keyframe_insert("location", frame=frame)
            b.keyframe_insert("rotation_euler", frame=frame)

    for frame, back in ((1, 0), (9, 1), (36, 1), (46, 0), (52, 0)):
        for side in ("L", "R"):
            turn("ear." + side, frame, x=-14 * back)

    # check: with these keys, do the feet really stay put while they are on the ground?
    slip = {}
    for name in feet:
        prev, worst_slip = None, 0.0
        for frame in range(FIRST, LAST + 1):
            scene.frame_set(frame)
            here = pb[name].matrix.translation.copy()
            on_ground = here.z - pb[name].bone.matrix_local.translation.z < 0.0015
            if prev is not None and on_ground and prev[1]:
                worst_slip = max(worst_slip, (here - prev[0]).length)
            prev = (here, on_ground)
        slip[name] = round(worst_slip * 1000, 1)
    scene.frame_set(scene_frame)
    print("KITSUNE SpinChase: spin reaches a full turn at frame", spin_done,
          "| most a planted foot slides between frames (mm):", slip,
          "| turn senses", {k: int(v) for k, v in left.items() if k in ("root", "head", "chest")})
    finish(a, FIRST, LAST)
    made.append(("SpinChase", 52))

    scene.frame_set(1)
    print("KITSUNE animations:", ", ".join(f"{n} ({f} frames)" for n, f in made))


main()
