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
  Walk        a loop, on the spot: the four-beat walk of a fox            24 frames
  Run         a loop, on the spot: a gallop, back flexing each stride      12 frames

24 frames a second. Idle, Walk and Run loop; every other action starts and ends on the rest pose. Run after pet_rig.py:
  run_blender.py --blend SM_Pet_KitsuneSpirit_S1.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/KitsuneSpirit/anim_kitsune.py
Running it again replaces these eight actions (hand edits to them are lost; rename an action to keep it).
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

    # ---- Walk and Run, both on the spot: the body stays over the origin and each paw slides back along the
    # ground at one steady speed while it carries weight, so the clip matches a pet moving at that speed
    # (printed below). A paw's step: set down ahead, carry back, the heel peels up over the toes at the end
    # of the push, the paw folds under as it lifts, swings forward and opens again to land.
    nose_down = {n: sense(n, "x") for n in ("torso", "chest", "hips", "head", "neck")}
    turn_left = {n: sense(n, "z") for n in ("chest", "hips", "head", "neck") + TAIL[:3]}

    def cyc(beats, u):
        """A value that runs smoothly round a loop through (phase, value) beats."""
        u %= 1.0
        pts = sorted(beats)
        pts = [(pts[-1][0] - 1.0, pts[-1][1])] + pts + [(pts[0][0] + 1.0, pts[0][1])]
        for (p0, v0), (p1, v1) in zip(pts[:-1], pts[1:]):
            if p0 <= u <= p1:
                t = (u - p0) / max(p1 - p0, 1e-9)
                return v0 + (v1 - v0) * (0.5 - 0.5 * math.cos(math.pi * t))
        return pts[0][1]

    def flow(beats, u):
        """Like cyc, but it passes through the beats without pausing on them (a spline round the loop)."""
        u %= 1.0
        pts = sorted(beats)
        n = len(pts)
        ring = [(pts[(i - 1) % n][0] + (i - 1) // n, pts[(i - 1) % n][1]) for i in range(n + 3)]
        for i in range(1, n + 1):
            (pa, va), (p0, v0), (p1, v1), (pb_, vb) = ring[i - 1], ring[i], ring[i + 1], ring[i + 2]
            if p0 <= u <= p1:
                span = max(p1 - p0, 1e-9)
                t = (u - p0) / span
                m0 = (v1 - va) / max(p1 - pa, 1e-9) * span
                m1 = (vb - v0) / max(pb_ - p0, 1e-9) * span
                return ((2 * t ** 3 - 3 * t ** 2 + 1) * v0 + (t ** 3 - 2 * t ** 2 + t) * m0
                        + (-2 * t ** 3 + 3 * t ** 2) * v1 + (t ** 3 - t ** 2) * m1)
        return pts[0][1]

    def gait(name, frames, paws, body):
        """paws: control -> (phase it lands at, share of the loop on the ground, reach ahead, reach behind,
        lift, fold in degrees, heel peel in degrees). body(u) keys everything else for loop position u."""
        action = start(name)
        for frame in range(1, frames + 2):
            u = ((frame - 1) / frames) % 1.0
            for control, (lands, duty, ahead, behind, lift, fold, heel) in paws.items():
                p = (u - lands) % 1.0
                air = 1.0 - duty
                # the paw's tilt is one curve round the whole step, so it never changes speed with a jerk:
                # flat while it carries, heel peeling up, folded under early in the swing, opening to land
                pitch = flow([(0.0, 0.0), (0.12 * duty, 0.0), (0.62 * duty, 0.0), (duty, heel),
                              (duty + 0.36 * air, fold), (duty + 0.78 * air, -6.0)], p)
                if p < duty:                                    # carrying weight: straight back along the ground
                    s = p / duty
                    along = -ahead + (ahead + behind) * s
                    height = 0.0
                    pitch = max(0.0, pitch)
                else:                                           # in the air
                    t = (p - duty) / air
                    # leaves and meets the ground already moving back at the ground's speed, and rises and
                    # comes down with no vertical speed at either end: no stop-and-go, no slap
                    drift = min((ahead + behind) / duty * air, 0.25 * (ahead + behind))   # more would throw the paw past its marks
                    along = ((2 * t ** 3 - 3 * t ** 2 + 1) * behind + (t ** 3 - 2 * t ** 2 + t) * drift
                             + (-2 * t ** 3 + 3 * t ** 2) * -ahead + (t ** 3 - t ** 2) * drift)
                    height = lift * math.sin(math.pi * t) ** 2
                # forward is -Y: a paw set down ahead of home is at a smaller y
                shift(control, frame, (0.0, along, height))
                turn(control, frame, x=pitch)
            body(u, frame)
        finish(action, 1, frames + 1)
        made.append((name, frames))
        return action

    def paw_report(name, frames, paws):
        stride = max((ahead + behind) / duty for (_l, duty, ahead, behind, *_r) in paws.values())
        print("KITSUNE %s: one loop is %d frames (%.2f s), covers %.3f m: matches a pet moving at %.2f m/s"
              % (name, frames, frames / 24.0, stride, stride / (frames / 24.0)))

    # ---- Walk: the four-beat walk of a fox. Left hind, left front, right hind, right front, a quarter of
    # the loop apart; each paw is on the ground about two thirds of the time, so two or three always are.
    # The back bends from side to side with the steps, the shoulder over a carrying front leg rides high,
    # the head is held low and level with a small nod on each front step, the tail trails and sways.
    WALK = 24
    walk_paws = {"foot_ik.L":       (0.00, 0.62, 0.046, 0.046, 0.022, 26.0, 18.0),
                 "front_foot_ik.L": (0.25, 0.62, 0.046, 0.046, 0.028, 38.0, 20.0),
                 "foot_ik.R":       (0.50, 0.62, 0.046, 0.046, 0.022, 26.0, 18.0),
                 "front_foot_ik.R": (0.75, 0.62, 0.046, 0.046, 0.028, 38.0, 20.0)}

    def walk_body(u, frame):
        swing = math.cos(2 * math.pi * u)                     # +1 as the left hind lands: left side bunched up
        shift("torso", frame, (0.0, 0.0, -0.011 + 0.0018 * math.cos(4 * math.pi * (u - 0.10))))
        turn("hips", frame, z=-4.0 * swing * turn_left["hips"], y=-1.8 * math.cos(2 * math.pi * (u - 0.32)))
        turn("chest", frame, z=3.0 * swing * turn_left["chest"], y=2.0 * math.cos(2 * math.pi * (u - 0.57)))
        turn("neck", frame, z=-1.2 * swing * turn_left["neck"], x=5.0 * nose_down["neck"])
        turn("head", frame, z=-1.8 * swing * turn_left["head"],
             x=(-2.0 + 1.5 * math.cos(4 * math.pi * (u - 0.05))) * nose_down["head"])
        for i, bone in enumerate(TAIL[:3]):
            turn(bone, frame, z=(4.0 + 1.5 * i) * math.cos(2 * math.pi * (u - 0.12 * (i + 1))) * turn_left[bone],
                 x=-3.0 + 1.5 * math.cos(4 * math.pi * (u - 0.1 * i)))
        for side in ("L", "R"):
            turn("ear." + side, frame, x=2.0 * math.cos(4 * math.pi * (u - 0.2)))

    gait("Walk", WALK, walk_paws, walk_body)
    paw_report("Walk", WALK, walk_paws)

    # ---- Run: a gallop. The hind paws land one just after the other, well forward under the belly, and
    # drive back; the body flies stretched out; the front paws land one after the other and pull through;
    # the body flies again, gathered, with all four paws under it, as the back arches to throw the hind
    # paws forward for the next stride. The back flexes and stretches once a stride, the head stays level,
    # the ears lie back and the tail streams out behind, lagging the hips.
    RUN = 12
    run_paws = {"foot_ik.L":       (0.00, 0.30, 0.066, 0.056, 0.032, 40.0, 30.0),
                "foot_ik.R":       (0.08, 0.30, 0.066, 0.056, 0.032, 40.0, 30.0),
                "front_foot_ik.R": (0.46, 0.30, 0.060, 0.062, 0.042, 60.0, 30.0),
                "front_foot_ik.L": (0.55, 0.30, 0.060, 0.062, 0.042, 60.0, 30.0)}
    # The body is lowest while legs carry it and comes down onto a landing, so a reaching leg is never
    # asked to be longer than it is.
    #             phase  height mm  nose down  back arched (+) or stretched (-)
    run_beats = [(0.00,  -11.0,     -4.0,      1.0),      # hind paws land under the belly, back arched
                 (0.20,  -14.0,     -2.0,      0.1),      # hind legs driving
                 (0.39,  -5.0,      1.0,       -1.0),     # off the ground, stretched out
                 (0.50,  -12.0,     6.0,       -0.6),     # front paws land
                 (0.66,  -15.0,     4.5,       0.2),      # front legs pulling through
                 (0.86,  -1.0,      -2.5,      1.0)]      # off the ground, gathered

    def run_body(u, frame):
        height = flow([(p, h) for p, h, _n, _a in run_beats], u)
        pitch = flow([(p, n) for p, _h, n, _a in run_beats], u)
        arch = flow([(p, a) for p, _h, _n, a in run_beats], u)
        shift("torso", frame, (0.0, 0.0, height / 1000.0))
        turn("torso", frame, x=pitch * nose_down["torso"])
        turn("hips", frame, x=-11.0 * arch * nose_down["hips"])        # arched: the rump curls under
        turn("chest", frame, x=7.0 * arch * nose_down["chest"])
        turn("neck", frame, x=(9.0 - 4.0 * arch) * nose_down["neck"])
        turn("head", frame, x=(-6.0 - 3.0 * arch - pitch) * nose_down["head"])
        for i, bone in enumerate(TAIL[:3]):
            late = flow([(p, a) for p, _h, _n, a in run_beats], u - 0.14 * (i + 1))
            turn(bone, frame, x=5.0 - 3.0 * i + 3.5 * late)
        for side in ("L", "R"):
            turn("ear." + side, frame, x=-16.0 + 3.0 * arch)

    gait("Run", RUN, run_paws, run_body)
    paw_report("Run", RUN, run_paws)

    scene.frame_set(1)
    print("KITSUNE animations:", ", ".join(f"{n} ({f} frames)" for n, f in made))


main()
