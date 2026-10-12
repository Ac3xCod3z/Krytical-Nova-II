"""Tidecaller, the later stages: three more clips, made from the rig alone (the model is not built again).

  run_blender.py --blend SM_Pet_Tidecaller_50.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/Tidecaller/more_clips_tidecaller.py
  (and the same on the 75 and 100 files)

Who gets which (the owner, 11 Oct 2026): the 50 % stage one, the 75 % two, the 100 % all three.

  ShellSpin  100   50, 75, 100   draws its fins in and winds back, spins flat like a top (four turns) with a slow
                                 wobble, throws its fins wide to brake, swings past and settles, a little dizzy
  Undertow   120   75, 100       climbs with two strokes, tips over, corkscrews down nose-first (two turns), hangs
                                 gathered at the bottom, then drives straight up with one great stroke, crests,
                                 opens its fins to brake and drifts back down into place
  Maelstrom  138   100           bows, rears up tall with every fin spread, and holds there leaning back, trembling
                                 harder and swelling as it gathers; throws its whole body forward and claps its front
                                 fins together; is thrown back by the clap; paddles home

24 frames a second. Each starts and ends on the first frame of Idle, as the other clips do.
Only the acting is in the clips. The water, light and sound are Unity's: see README_Evolutions.txt for the cues.

How they are made to move like a creature in water, not a machine:
  - every value runs on a smooth curve through its marks, never stopping dead on one, keyed on every second frame
  - nothing starts at once: the body leads, the front fins follow two frames later, the back fins a frame after that
  - before each big move it first goes a little the other way (sinks before it rises, winds back before it spins)
  - after each big move it swings past and comes back, smaller each time, before it is still
  - water holds it up: it never drops, it sinks; its fins open to brake and it bobs when it stops

The eyelids' poses are read from Idle's own blink (what "hidden", "half" and "shut" are on this model), so this
script needs the stage's Idle to be in the file. Running it again replaces these clips.
"""
import math

import bpy
from mathutils import Quaternion, Vector

HEIGHT = {"25": 0.45, "50": 0.50, "75": 0.55, "100": 0.60}
WHO = {"ShellSpin": ("50", "75", "100"), "Undertow": ("75", "100"), "Maelstrom": ("100",)}
IDLE_BLINK = 22                  # Idle's first blink starts here: lids hidden on it, half on the next frame, shut on the one after
FIN_LAG, TAIL_LAG = 2, 3         # frames the front fins, and the back fins, run behind the body


def curves_of(action):
    return [fc for layer in action.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves]


def through(points, frame):
    """A value through (frame, value) marks on a smooth curve that does not stop at each one."""
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


def swing_past(frame, since, size, beat=12.0, fade=8.0):
    """Arriving late and swinging past: a fading swing that starts at `since`."""
    t = frame - since
    return size * math.exp(-t / fade) * math.sin(2 * math.pi * t / beat) if t > 0 else 0.0


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def main():
    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE" and o.name.startswith("SM_Pet_Tidecaller_") and o.name.endswith("_Rig"))
    stage = rig.name.split("_")[3]
    H = HEIGHT[stage]
    arm, pb = rig.data, rig.pose.bones
    fins = [b.name for b in pb if b.name.startswith(("fin.", "tail."))]
    eyeballs = [b.name for b in pb if b.name.startswith("eyeball.")]
    X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
    last = {}
    for b in pb:
        b.rotation_mode = "QUATERNION"

    # ---- what the lids do in Idle's blink: hidden, half, shut
    idle = bpy.data.actions.get("Idle")
    if idle is None:
        raise SystemExit("This file has no Idle: build the stage first (build_tidecaller_stage.py).")
    lid_pose = {}
    for b in pb:
        if b.name.startswith("lid."):
            for state, frame in (("hidden", IDLE_BLINK), ("half", IDLE_BLINK + 1), ("shut", IDLE_BLINK + 2)):
                q, sc = [1.0, 0.0, 0.0, 0.0], [1.0, 1.0, 1.0]
                for fc in curves_of(idle):
                    if fc.data_path == 'pose.bones["%s"].rotation_quaternion' % b.name:
                        q[fc.array_index] = fc.evaluate(frame)
                    elif fc.data_path == 'pose.bones["%s"].scale' % b.name:
                        sc[fc.array_index] = fc.evaluate(frame)
                lid_pose[(b.name, state)] = (Quaternion(q), tuple(sc))

    def about(axis, degrees):
        return Quaternion(axis, math.radians(degrees))

    def key(name, frame, q=None, move=None, scale=None):
        b = pb[name]
        rest = arm.bones[name].matrix_local.to_3x3()
        if q is not None:
            local = Quaternion(rest.inverted() @ Vector(q.axis), q.angle) if abs(q.angle) > 1e-7 else Quaternion()
            if name in last and last[name].dot(local) < 0:
                local.negate()
            last[name] = local.copy()
            b.rotation_quaternion = local
            b.keyframe_insert("rotation_quaternion", frame=frame)
        if move is not None:
            b.location = rest.inverted() @ Vector(move)
            b.keyframe_insert("location", frame=frame)
        if scale is not None:
            b.scale = (scale, scale, scale)
            b.keyframe_insert("scale", frame=frame)

    def lids(frame, state="hidden"):
        for b in pb:
            if b.name.startswith("lid."):
                q, sc = lid_pose[(b.name, state)]
                if b.name in last and last[b.name].dot(q) < 0:
                    q = -q
                last[b.name] = q.copy()
                b.rotation_quaternion = q
                b.scale = sc
                b.keyframe_insert("rotation_quaternion", frame=frame)
                b.keyframe_insert("scale", frame=frame)

    def shut_eyes(begin, end):
        for frame, state in ((begin, "hidden"), (begin + 1, "half"), (begin + 2, "shut"), (end, "shut"), (end + 1, "half"), (end + 2, "hidden")):
            lids(frame, state)

    def blink(at):
        for frame, state in ((at, "hidden"), (at + 1, "half"), (at + 2, "shut"), (at + 4, "shut"), (at + 6, "half"), (at + 7, "hidden")):
            lids(frame, state)

    def look(frame, right=0.0, up=0.0):
        for name in eyeballs:
            key(name, frame, about(Z, right) @ about(X, up))

    def paddle(frame, up=0.0, back=0.0, tail=0.0, splay=0.0, lean=0.0):
        """Front fins: degrees raised (+) or lowered, swept back (+) or forward; `lean` raises the left and lowers
        the right. Back fins: degrees lowered (+) or raised, and spread (+)."""
        for name in fins:
            sign = 1.0 if name.endswith(".L") else -1.0
            if name.startswith("fin"):
                key(name, frame, about(Z, sign * back) @ about(Y, -sign * (up + sign * lean)))
            else:
                key(name, frame, about(X, tail) @ about(Z, sign * splay))

    def body(frame, pitch=0.0, roll=0.0, turn=0.0, lift=0.0, ahead=0.0, size=None, tilt=0.0, tilt_towards=0.0):
        """pitch: nose down (+). roll: tipped to its left (+). turn: about its height. lift, ahead: shares of its height.
        tilt: its whole axis leant over by this much, towards the compass bearing `tilt_towards` (a top's wobble)."""
        lean = Quaternion(Vector((math.cos(math.radians(tilt_towards)), math.sin(math.radians(tilt_towards)), 0.0)), math.radians(tilt))
        key("root", frame, Quaternion(), move=(0, 0, 0))
        key("body", frame, lean @ about(Z, turn) @ about(Y, roll) @ about(X, pitch), move=(0, -H * ahead, H * lift), scale=size)

    # the first frame of Idle: where every clip starts and ends
    HOME = {"up": 0.0, "back": -9.0, "tail": 0.0, "splay": 4.0, "pitch": 1.5 * math.sin(-0.6), "lift": 0.014 * math.sin(-2.2)}

    def start(name):
        old = bpy.data.actions.get(name)
        if old is not None:
            bpy.data.actions.remove(old)
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        rig.animation_data_create()
        rig.animation_data.action = action
        last.clear()
        return action

    def finish(action, end):
        for fc in curves_of(action):                          # a lid appears and goes in one step, never half grown
            if "lid." in fc.data_path and fc.data_path.endswith("scale"):
                for kp in fc.keyframe_points:
                    kp.interpolation = "CONSTANT"
        action.use_frame_range = True
        action.frame_start, action.frame_end = 1, end
        rig.animation_data.action = None
        for b in pb:
            b.rotation_quaternion = (1, 0, 0, 0)
            b.location = (0, 0, 0)
            b.scale = (1, 1, 1)

    def play(end, marks, extra=None, every=2):
        """marks: name -> [(frame, value), ..]. Body values are read on the frame; fins a little behind it."""
        frames = sorted(set(list(range(1, end + 1, every)) + [end]))
        for frame in frames:
            def at(name, lag=0):
                points = marks.get(name)
                if points is None:
                    return HOME.get(name, 0.0)
                return through(points, min(max(frame - lag, 1), end) if lag else frame)
            more = extra(frame) if extra else {}
            paddle(frame, up=at("up", FIN_LAG) + more.get("up", 0.0), back=at("back", FIN_LAG) + more.get("back", 0.0),
                   tail=at("tail", TAIL_LAG) + more.get("tail", 0.0), splay=at("splay", TAIL_LAG) + more.get("splay", 0.0), lean=more.get("lean", 0.0))
            body(frame, pitch=at("pitch") + more.get("pitch", 0.0), roll=at("roll") + more.get("roll", 0.0), turn=at("turn") + more.get("turn", 0.0),
                 lift=at("lift") + more.get("lift", 0.0), ahead=at("ahead") + more.get("ahead", 0.0),
                 size=(marks["size"] and through(marks["size"], frame)) if "size" in marks else 1.0,
                 tilt=more.get("tilt", 0.0), tilt_towards=more.get("tilt_towards", 0.0))
        # the fins and the body are exactly home on the first and last frame, whatever the lag left
        for frame in (1, end):
            paddle(frame, up=HOME["up"], back=HOME["back"], tail=HOME["tail"], splay=HOME["splay"])
            body(frame, pitch=HOME["pitch"], lift=HOME["lift"], turn=marks["turn"][-1][1] if frame == end and "turn" in marks else 0.0, size=1.0)
            lids(frame)
            look(frame)

    made = []

    # ---- ShellSpin
    if stage in WHO["ShellSpin"]:
        END, TURNS = 100, 4
        WIND, UP, FAST, BRAKE, STILL = 6, 16, 28, 58, 70      # winds back from; lets go; at full speed; throws its fins out; stopped
        speed = [smooth((f - UP) / (FAST - UP)) if f < BRAKE else 1.0 - smooth((f - BRAKE) / (STILL - BRAKE)) for f in range(1, END + 1)]
        speed = [0.0 if i + 1 < UP else v for i, v in enumerate(speed)]
        total, turned = sum(speed), []
        run = 0.0
        for v in speed:
            turned.append(360.0 * TURNS * run / total)
            run += v
        a = start("ShellSpin")
        marks = {
            "up": [(1, 0), (WIND, 8), (UP, -14), (FAST, -30), (BRAKE - 2, -30), (BRAKE + 3, 44), (STILL, 26), (STILL + 10, 8), (END, 0)],
            "back": [(1, -9), (WIND, -16), (UP, 12), (FAST, 34), (BRAKE - 2, 34), (BRAKE + 3, -16), (STILL, -12), (END, -9)],
            "tail": [(1, 0), (UP, 8), (FAST, 15), (BRAKE - 2, 15), (BRAKE + 3, -10), (STILL + 6, 2), (END, 0)],
            "splay": [(1, 4), (UP, 2), (FAST, 0), (BRAKE - 2, 0), (BRAKE + 3, 14), (STILL + 6, 6), (END, 4)],
            "pitch": [(1, HOME["pitch"]), (WIND, -4), (UP, 5), (FAST, 0), (BRAKE, 0), (BRAKE + 5, -7), (STILL + 6, 3), (END, HOME["pitch"])],
            "lift": [(1, HOME["lift"]), (WIND + 4, -0.045), (UP, -0.030), (FAST, 0.050), (BRAKE, 0.120), (BRAKE + 6, 0.150), (STILL + 8, 0.040), (END, HOME["lift"])],
            "turn": [(1, 0.0), (END, 360.0 * TURNS)],
        }

        def spin(frame):
            wound = -22.0 * smooth((frame - 2) / (WIND + 4)) * (1.0 - smooth((frame - UP + 2) / 6.0))      # first a little the other way
            fast = speed[frame - 1]
            return {"turn": turned[frame - 1] - through([(1, 0.0), (END, 360.0 * TURNS)], frame) + wound + swing_past(frame, STILL - 2, 20.0, beat=14.0, fade=7.0),
                    "tilt": 4.5 * fast + swing_past(frame, BRAKE, 5.0, beat=10.0, fade=6.0), "tilt_towards": turned[frame - 1] * 0.22,
                    "lift": swing_past(frame, STILL, 0.030, beat=16.0, fade=9.0), "lean": swing_past(frame, BRAKE + 3, 9.0, beat=9.0, fade=6.0)}

        play(END, marks, spin)
        shut_eyes(UP + 2, BRAKE + 4)
        for frame, right in ((BRAKE + 8, 0), (BRAKE + 12, 12), (BRAKE + 17, -11), (BRAKE + 22, 8), (BRAKE + 27, -4), (BRAKE + 32, 0)):   # a little dizzy
            look(frame, right, 2.0)
        blink(BRAKE + 34)
        finish(a, END)
        made.append("ShellSpin")

    # ---- Undertow
    if stage in WHO["Undertow"]:
        END = 120
        TOP, OVER, DEEP, HANG, CREST, OPEN = 18, 26, 50, 58, 72, 80
        a = start("Undertow")
        marks = {
            # two climbing strokes, tucked for the dive, gathered at the bottom, ONE great stroke, spread wide to brake
            "up": [(1, 0), (5, 24), (10, -22), (14, 22), (TOP, -18), (OVER, -26), (DEEP, -28), (HANG - 2, 12), (HANG + 1, 40), (HANG + 6, -42),
                   (CREST - 4, -30), (OPEN, 40), (OPEN + 10, 20), (OPEN + 22, -10), (END, 0)],
            "back": [(1, -9), (5, -18), (10, 14), (14, -16), (TOP, 14), (OVER, 30), (DEEP, 32), (HANG - 2, -6), (HANG + 1, -22), (HANG + 6, 34),
                     (CREST - 4, 30), (OPEN, -14), (OPEN + 22, 2), (END, -9)],
            "tail": [(1, 0), (10, 10), (TOP, 6), (OVER, 12), (DEEP, 12), (HANG, -8), (HANG + 6, 16), (CREST, 8), (OPEN, -10), (END, 0)],
            "splay": [(1, 4), (TOP, 6), (OVER, 0), (DEEP, 0), (HANG, 10), (HANG + 6, 0), (CREST, 2), (OPEN, 14), (END, 4)],
            "pitch": [(1, HOME["pitch"]), (6, -10), (TOP, -14), (OVER, 62), (DEEP - 8, 74), (DEEP, 50), (HANG - 2, 6), (HANG + 2, -30), (HANG + 7, -82),
                      (CREST - 3, -70), (CREST + 3, -18), (OPEN + 4, 9), (OPEN + 16, -3), (END, HOME["pitch"])],
            "lift": [(1, HOME["lift"]), (4, -0.030), (10, 0.160), (14, 0.200), (TOP, 0.380), (OVER, 0.420), (DEEP - 6, 0.060), (DEEP, -0.045), (HANG - 2, -0.070),
                     (HANG + 2, -0.050), (CREST - 4, 0.560), (CREST + 2, 0.660), (OPEN + 4, 0.560), (OPEN + 22, 0.140), (END - 6, -0.016), (END, HOME["lift"])],
            "ahead": [(1, 0), (TOP, -0.03), (OVER, 0.08), (DEEP, 0.02), (HANG, 0.0), (CREST, -0.04), (OPEN + 10, 0.03), (END, 0)],
            "turn": [(1, 0.0), (OVER - 4, 0.0), (OVER + 4, 90.0), (DEEP - 6, 610.0), (DEEP + 2, 708.0), (HANG, 720.0), (END, 720.0)],
            "roll": [(1, 0), (OVER - 2, 0), (OVER + 6, 24), (DEEP - 6, 24), (DEEP + 2, 4), (HANG, 0), (HANG + 6, 0), (END, 0)],      # banked into the spiral
            "size": [(1, 1.0), (DEEP, 1.0), (HANG - 1, 0.955), (HANG + 3, 1.05), (CREST, 1.0), (END, 1.0)],                      # gathered, then long
        }

        def bob(frame):
            return {"lift": swing_past(frame, OPEN + 18, 0.026, beat=18.0, fade=10.0), "pitch": swing_past(frame, OPEN + 4, 5.0, beat=16.0, fade=8.0),
                    "roll": swing_past(frame, CREST, 5.0, beat=13.0, fade=7.0)}

        play(END, marks, bob)
        shut_eyes(DEEP - 2, HANG + 1)
        for frame, up in ((HANG + 4, 0), (HANG + 8, 8), (CREST, 6), (OPEN + 6, -6), (OPEN + 18, 0)):
            look(frame, 0.0, up)
        blink(OPEN + 26)
        finish(a, END)
        made.append("Undertow")

    # ---- Maelstrom
    if stage in WHO["Maelstrom"]:
        END = 138
        BOW, REAR, HOLD, CLAP, THROWN = 14, 36, 72, 78, 88
        a = start("Maelstrom")
        marks = {
            "up": [(1, 0), (BOW, -18), (BOW + 8, 6), (REAR, 44), (HOLD - 10, 50), (HOLD, 56), (CLAP - 2, 30), (CLAP, 4), (CLAP + 4, 26), (THROWN, 34),
                   (THROWN + 14, -12), (THROWN + 28, 10), (END, 0)],
            "back": [(1, -9), (BOW, -24), (REAR, 4), (HOLD - 10, 12), (HOLD, 26), (CLAP - 2, -20), (CLAP, -62), (CLAP + 4, -6), (THROWN, 16),
                     (THROWN + 14, 12), (THROWN + 28, -14), (END, -9)],
            "tail": [(1, 0), (BOW, 8), (REAR, -12), (HOLD, -16), (CLAP, 16), (THROWN, -6), (THROWN + 16, 8), (END, 0)],
            "splay": [(1, 4), (BOW, 2), (REAR, 13), (HOLD, 16), (CLAP, 2), (THROWN, 12), (END, 4)],
            "pitch": [(1, HOME["pitch"]), (BOW, 11), (REAR, -22), (HOLD - 10, -28), (HOLD, -36), (CLAP - 2, 6), (CLAP, 28), (CLAP + 4, 8), (THROWN, -12),
                      (THROWN + 12, 4), (THROWN + 26, -2), (END, HOME["pitch"])],
            "lift": [(1, HOME["lift"]), (BOW, -0.050), (REAR, 0.200), (HOLD - 10, 0.235), (HOLD, 0.260), (CLAP, 0.090), (CLAP + 4, 0.120), (THROWN, 0.160),
                     (THROWN + 16, 0.040), (THROWN + 30, -0.018), (END, HOME["lift"])],
            "ahead": [(1, 0), (BOW, 0.02), (REAR, -0.04), (HOLD - 10, -0.08), (HOLD, -0.13), (CLAP, 0.36), (CLAP + 4, 0.30), (THROWN, 0.12), (THROWN + 14, 0.09),
                      (THROWN + 30, 0.02), (END, 0)],
            "size": [(1, 1.0), (BOW, 0.985), (REAR, 1.05), (HOLD, 1.11), (CLAP - 1, 1.08), (CLAP + 1, 0.95), (CLAP + 6, 1.03), (THROWN + 10, 1.0), (END, 1.0)],
        }

        def gather(frame):
            hard = smooth((frame - REAR + 4) / (HOLD - REAR + 4.0)) * (1.0 - smooth((frame - HOLD) / 3.0))       # the tremble, harder and harder
            shake = (1.0 if frame % 4 < 2 else -1.0) * hard
            return {"turn": 2.6 * shake + swing_past(frame, CLAP + 1, 7.0, beat=9.0, fade=6.0), "roll": 1.4 * shake,
                    "up": 3.0 * shake, "lift": swing_past(frame, THROWN + 26, 0.022, beat=18.0, fade=10.0),
                    "pitch": swing_past(frame, THROWN, 6.0, beat=15.0, fade=8.0)}

        play(END, marks, gather)
        shut_eyes(3, BOW + 4)
        shut_eyes(CLAP - 1, CLAP + 7)
        blink(THROWN + 30)
        finish(a, END)
        made.append("Maelstrom")

    bpy.context.scene.frame_set(1)
    print("TIDECALLER %s%% more clips: %s; all clips now %s" % (stage, made, sorted(x.name for x in bpy.data.actions)))


main()
