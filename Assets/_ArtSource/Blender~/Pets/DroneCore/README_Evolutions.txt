DRONE CORE, the later forms: 25 %, 50 %, 75 %, 100 %
Exported 11 Oct 2026. For whoever wires these into the pet system.

WHERE THEY ARE
  In the game      Assets/_Game/Characters/Pets/DroneCore_25, _50, _75, _100
                   each: SM_Pet_DroneCore_<n>.fbx, Pet_DroneCore_<n>.prefab, AC_Pet_DroneCore_<n>.controller, materials
  Shared           Assets/_Game/Characters/Shared/Swatch/M_KN_Swatch_Synty.mat + T_KN_Swatch_Synty.png
                   (the body colour of the 25, 50 and 75; new in the game with this export)
  Packages         Downloads/Pets/DroneCore/DroneCore<n>/DroneCore<n>.unitypackage (the same files)
  Sources          Assets/_ArtSource/Blender~/Pets/DroneCore/  (rig_dronecore_25.py, rig_dronecore_stage.py, the .blend files)

WHAT IS NOT DONE
  Nothing is set up in the pet system: no pet data, no evolution links, no effects, no sounds.
  The folders were copied in as files; the game's own Editor had not imported them when this was written.
  The base form (DroneCore, S1) was not exported again: it has not changed.

THE MODELS                 cube     triangles   bones   pivot
  DroneCore_25             0.34 m      3,704       7    the cube's middle
  DroneCore_50             0.38 m      5,476       8    the cube's middle
  DroneCore_75             0.42 m      5,656      10    the cube's middle
  DroneCore_100            0.46 m     20,240      27    the cube's middle
  Generic rig, facing +Z. Leave "Bake Axis Conversion" off.
  The 100 % is heavy because it carries two bodies: see THE SWARM below.

MATERIALS
  25, 50, 75   body on the shared M_KN_Swatch_Synty. Own materials: RingGlow, Mark, MarkRed, Flash (all glow),
               and on 50 and 75 also Glass and Bezel.
  100          M_Pet_DroneCore_100 with its own texture and emission map (T_Pet_DroneCore_100.png, _Emission.png:
               the light blue lines and the yellow lights, on black, so the glow can be set in Unity).
               Own materials: Glass (glows), Bezel, Mark, MarkRed, Flash.

BONES THAT EFFECTS HANG ON
  cannon     along the gun barrel; its tip is the muzzle (50, 75, 100)
  lens       the glass; shutter = the barrel round it; eye = the white dot; eye_red = the red dot (shown when angry)
  flash      a disc in the lens, scaled up for one frame in BarrelTurn (the camera flash)
  pod.L, pod.R   75 % only: the side pods
  hull, shard.000 .. shard.212   100 % only: see THE SWARM

CLIPS, 24 frames a second. The controller has one trigger per clip, each returning to Rest.
  All four forms
    BarrelTurn         80   taking a photo: the barrel comes out, turns, the flash fires on frame 34
    EyeLook           138   the dot looks about
    AxisSpin           96   a spin on each axis in turn
    AxisSpinDiagonal   98   the same on the diagonals
    Mad                76   the red dot comes up, it squints and shakes
    FrontRoll          25   loop
  50, 75, 100
    CannonFire         60   two shots with recoil (on the 75 % the pods kick too)
  50, 75
    MinigunFire        80   the cannon spins and fires fast
  100 only
    FakeoutBlast      130
    SpinShots          91
    Disassemble        44
    SwarmHover         49   loop
    Assemble           46

ONLY THE ACTING IS IN THE CLIPS. Speech bubbles, beams, shots, muzzle flashes and sounds are for Unity to add,
on these cues (frame, then seconds from the start of the clip):

  CannonFire      a shot on 12 (0.46) and 30 (1.21), from the tip of the cannon bone
  MinigunFire     spin-up 1 to 12, winding down 61 to 80
                  a shot every 2 frames from 14 to 60: 24 shots, 12 a second (0.54 s to 2.46 s), from the cannon's tip
  FakeoutBlast    bubble "Ka" 1 (0.00)   "Me" 13 (0.50)   "Ha!" 27 (1.08; the thrust lands on 31)
                  "...just kidding" 45 (1.83)   "Die!" 65 (2.67)
                  beam ON 72 (2.96) to OFF 110 (4.54), from the cannon's tip, along the bone
  SpinShots       the barrel spins from frame 1; a shot on 26, 32, 38, 44, 50, 56, 62, 68
                  (1.04, 1.29, 1.54, 1.79, 2.04, 2.29, 2.54, 2.79 s), each from the cannon's tip
  BarrelTurn      the flash on frame 34 (1.38)

THE SWARM (100 % only)
  Disassemble, then SwarmHover (loop) for as long as wanted, then Assemble. Each starts on the frame the one before
  ends on. The burst is from frame 10 of Disassemble; the lock is on frame 39 of Assemble.
  The body is never cut. The drone's own mesh, whole, is on the bone "hull". The 18 flying blocks are a second,
  simpler body, each block shrunk to a speck at its own bone (shard.<x><y><z>) inside the hull. The swarm clips swap
  them by bone scale on one frame: Disassemble 10 (hull to nothing, blocks to full size), Assemble 36 (back again).
  So: do not strip scale curves from these three clips, and do not let anything else drive the scale of hull or
  the shard bones. In every other clip the blocks stay out of sight.
  Re-exported later on 11 Oct 2026: the file now rests assembled. (Unity takes a model's resting pose from the first
  animation in the file; that was Assemble, which starts in pieces. A clip that starts at rest now comes first.)
  The renderer's bounds are the assembled drone's (about 0.77 m), so they do NOT cover the swarm: while the
  swarm clips play, turn on Update When Offscreen or enlarge the bounds, or the pieces can vanish at the screen edge.

KNOWN LIMITS
  100 %: a few small cover faces show where the belt meets the lens and at the fin roots, in the colour beside them.
  100 %: the vent stripes are evened but not perfect; a few thin yellow slivers remain beside them.
  75 %: after the colour change (the darkest green made olive, the two front bars light grey) the body is nearly
        all one olive.
  The MinigunFire spin is hard to see on its own: the barrel is nearly round. The muzzle flashes will carry it.
