KITSUNE SPIRIT, the 50 %, 75 % and 100 % forms
Made 12 Oct 2026 from the owner's three Tripo models. EXPORTED 12 Oct 2026 on the owner's word.

WHERE THEY ARE
  In the game   Assets/_Game/Characters/Pets/KitsuneSpirit_50, _75, _100 (new folders, copied in as files with their
                .meta files): SM_Pet_KitsuneSpirit_NN.fbx (Generic rig, faces +Z, all its clips listed in the import
                settings), Pet_KitsuneSpirit_NN.prefab, AC_Pet_KitsuneSpirit_NN.controller (loops Idle; Walk and Run
                by true/false, the rest by trigger), M_Pet_KitsuneSpirit_NN.mat (colour and glow maps), the two
                textures; the 100 % form also M_Pet_KitsuneSpirit_100_Tails.mat (the same maps, Transparent, alpha 0.6).
                              triangles   bones   clips
                    50 %      5,616       58      8
                    75 %      5,782       73      10
                    100 %     5,799       88      12
  Packages      Downloads/Pets/KitsuneSpirit/KitsuneSpirit50, 75, 100: the .fbx, a .unitypackage of the same files,
                and this file.
  NOT DONE      nothing is set up in the pet system (no pet data, no evolution links, no effects, no sounds): another
                session does that. NOT COMMITTED to git. The game's own Editor had not imported the folders when this
                was written: the import was checked in a throwaway Unity project only (rig, every clip and its length,
                controller, prefab, materials). The see-through Tails material was written by hand from what URP
                saves for a Transparent material: look at it in Unity.
  Leave "Bake Axis Conversion" OFF on the models. Each rests in Unity in the first frame of its first clip, which is
  the fox standing.
  The live Blender scene still holds only the three imports (KitsuneSpirit50% / 75% / 100%).

LAST CHECK OF THE MODELS BEFORE EXPORT (12 Oct 2026)
  - Open edges: 149 (50 %), 154 (75 %), 67 (100 %), in 13, 12 and 11 loops. ALL OF THEM CAME WITH THE IMPORTS (the
    imports have the same loops): the rims of the collar and the ears, the roots of the tails where they sink into
    the rump, the eye sockets, the paper tags. Thinning added none. They were not closed.
  - Seen from behind from outside (which is what an open edge costs): nothing on the 50 % and 75 % forms. On the 100 %
    form the paper tags were single sheets; they were given a back. Left: one face of a tag (7.5 cm2) and two small
    faces at the tails' roots (4 cm2) can still be seen from behind from a few directions.
  - No loose vertices or edges, no faces of no size, no vertex without a bone, on any of the three.
  - The eyelids are open sheets by design (they are never seen from behind).

THE OWNER'S WORD (12 Oct 2026)
  Look     keep the imported look (the texture stays); the flat-colour Synty treatment was compared and not chosen
  50 %     tails and eyes glow
  75 %     tails and eyes glow
  100 %    tails glow and are half see-through, with an ethereal light rim and fire-look tips like the 75 % form's;
           eyes glow; the flame tufts are fire: lit here, the flames a Unity effect
  Size     each form 0.1 bigger than the one before
  Rigs     a new rig each; every tail its own chain of bones, enough of them to bend well

FILES (this folder)
  SM_Pet_KitsuneSpirit_50 / _75 / _100.blend    the import (hidden, untouched), the game model, its rig
  T_Pet_KitsuneSpirit_NN.png                    colour, 2048
  T_Pet_KitsuneSpirit_NN_Emission.png           glow, on black, 2048
  rig_kitsune_NN.json                           the rig's landmarks; WRITTEN BY fit_rig_kitsune_stage.py, do not edit by hand
  prep_kitsune_stage.py, fit_rig_kitsune_stage.py, bind_kitsune_tails.py     how they are made

HOW THEY ARE MADE (the same four steps for each file; each is safe to run again, in this order)
  run_blender.py --blend SM_Pet_KitsuneSpirit_NN.blend run_scene_script.py --script .../prep_kitsune_stage.py
  run_blender.py --blend SM_Pet_KitsuneSpirit_NN.blend run_scene_script.py --script .../fit_rig_kitsune_stage.py
  run_blender.py --blend SM_Pet_KitsuneSpirit_NN.blend pet_rig.py --object SM_Pet_KitsuneSpirit_NN --landmarks .../rig_kitsune_NN.json
  run_blender.py --blend SM_Pet_KitsuneSpirit_NN.blend run_scene_script.py --script .../bind_kitsune_tails.py
  Running prep again throws the rig away (it starts from the import), so the three steps after it must follow.
  Check with rig_rest_check.py --rig SM_Pet_KitsuneSpirit_NN_Rig: all three rest true (no bone off).

THE MODELS (metres; the fox faces -Y, its left is +X, Z is up; it stands on the ground at the origin)
            triangles   wide x long x high        nose to rump   tails   deforming bones
  50 %      5,328       0.36 x 0.85 x 0.55        0.50           3       63
  75 %      5,494       0.60 x 0.86 x 0.70        0.60           6       78
  100 %     5,443       0.75 x 1.15 x 0.90        0.70           9       93
  SIZE: the owner, 12 Oct 2026: each form is 0.1 bigger than the one before. Taken as the body's length, nose to
  rump (the 25 % form's is 0.40): "body_m" in prep_kitsune_stage.py. The tails add the rest of the length and height.
  Thinned to half the import's triangles. UV map "map1" is Blender's Smart UV Project: many small islands, fine
  for the game, poor for painting by hand. The 25 % form's painter's layout was not made for these (not asked for).
  EYES AND BLINKING (12 Oct 2026). The eyes are the model's own, painted on the head: there are no eyeballs. Each
  eye has an eyelid: a smooth eye-shaped sheet, a little larger than the painted eye, draped on the head over it, in
  the colour of the fur round the eye. It hangs on an axle that runs from corner to corner of the eye inside the
  head (the bone eyelid.L / eyelid.R, child of the head, lies along it), and rests swung up about that axle, out of
  sight inside the head.
    TO BLINK  turn the eyelid control about its own Y by kn_lid_turn_L / _R degrees (a property of the mesh object;
              70 to 100 degrees, each eye its own): the lid comes out from under the brow and slides down over the
              eye. 0 = open, the full turn = shut, anything between = half shut (narrowed eyes).
    A blink in the clips: shut two frames after it starts, held one to three frames, open again four frames later.
    The shut lid is one flat fur colour and is not in the glow map, so a blink puts the eye's glow out.
    Known flaw: on the 50 % form a thin line of the eye's glow still shows along the edge of the shut lid.
    (The owner turned down the first eyelid, which snapped shut in one frame: "jank". This one replaced it.)

MATERIALS FOR UNITY
  Body     every stage. Base map T_Pet_KitsuneSpirit_NN.png; emission map T_Pet_KitsuneSpirit_NN_Emission.png, emission
           colour white, strength to taste (the map is already dark where nothing glows; the Blender preview uses 2).
  Tails    the 100 % form only: the tails are a second material slot so they can be drawn see-through. Same two
           maps; surface Transparent, alpha about 0.6 (the Blender preview uses 0.6). Nine see-through tails overlap:
           if they sort badly against each other, try alpha-to-coverage / dithered fade, or a depth pre-pass.
  WHAT THE GLOW MAP HOLDS
    tails      50 % and 75 %: the tail's own colour, faint at the rump (12 %) rising to full at the tip.
               100 % (the owner, 12 Oct 2026: an ethereal light rim glow, and fire-look emission at the tips as the
               75 % form has): a faint cool light all over the tail, and FIRE on the last third or so of each tail,
               deep orange where it starts, pale yellow at the very tip, with a ragged edge. The fire is in the glow
               map only; the colour map's tails are still the import's grey.
               THE RIM ITSELF IS NOT IN ANY MAP: a rim is lit by the angle of view, so it is the Tails material's
               job in Unity. Wanted: thin and soft, pale blue-white (0.72, 0.88, 1.0), only where the tail's surface
               turns well away from the eye (Fresnel power about 3, strength about 0.5). The Blender file's Tails
               material previews exactly that.
    eyes       50 %: its painted eyes are a pale green and picking the glow by colour lit them ragged, with holes
               and spill onto the fur under the eye. So the 50 % form's eye glow is DRAWN: the eye's outline (the
               left eye's, mirrored for the right and kept off the fur), with a soft edge, in the eye's own colour,
               palest in the middle and a deeper, dimmer yellow towards the edge, as the other two forms' painted
               eyes are. Known flaw: a thin dark crease across the fox's right eye, where that eye's loose eyeball
               meets the socket in the model.
               75 %, 100 %: the eye's own colour, in full. Found by colour on the head, so check them in Unity close up:
               on the 75 % form the mask was tightened to keep the gold ear rims out.
    fire       100 % only (the owner, 12 Oct 2026, with a reference picture: the fire painted on the body glows too).
               The six flame tufts, at least 65 % lit, and every patch of fire painted on the body: the sweep from
               each shoulder tuft down over the shoulder to the chest, down the back of each foreleg under its
               tuft, round each hock, and on the haunches. Found by colour (brighter and yellower than the fur, not
               the white of the ruff, never on the head) and warmed towards orange, as the paint itself is pale.
               Some pale edges of the chest ruff are caught with it.

THE RIGS
  Rigify, the 25 % form's body rig carried over to each model's own joints (measured: paws, nose, back, ear tips).
  Controls are named as on the 25 % form (root, torso, chest, hips, neck, head, ear.L/R, foot_ik.L/R,
  front_foot_ik.L/R ...). The spine's bones are plain, not bendy (see rig_kitsune_25.json for why).
  TAILS  every tail is a chain of FIVE bones, each with its own control: tail_K_1 (at the rump) .. tail_K_5 (the tip).
         K counts from the fox's LEFT to its RIGHT. Parent of tail_K_1 is spine.004 (the hips). No tail is mirrored
         and no tail bone's name ends in .L / .R. Each tail's skin follows only its own chain, shared across each
         joint so it bends in a curve. The 25 % form's clips name its tails tail_1.L .. tail_4.R: they do not fit.
         Tested by posing: all tails curled, drawn together, and each waving on its own, with the body in a stride.
  Where each tail is, tip (x, y, z) and length:
    50 %    1  left, level   ( 0.161, 0.377, 0.291)  0.35      2  right, upper  (-0.131, 0.397, 0.366)  0.37
            3  right, low    (-0.176, 0.423, 0.183)  0.42
    75 %    1  left, low     ( 0.249, 0.398, 0.241)  0.37      2  left, middle  ( 0.215, 0.362, 0.459)  0.36
            3  left, top     ( 0.118, 0.412, 0.594)  0.42      4  right, top    (-0.118, 0.412, 0.594)  0.42
            5  right, middle (-0.260, 0.358, 0.502)  0.40      6  right, low    (-0.270, 0.405, 0.272)  0.39
    100 %   1  left, lowest  ( 0.377, 0.532, 0.390)  0.55      2  left          ( 0.367, 0.571, 0.532)  0.66
            3  left          ( 0.313, 0.465, 0.741)  0.61      4  left, high    ( 0.154, 0.361, 0.822)  0.59
            5  the middle    ( 0.002, 0.291, 0.851)  0.54      6  right, high   (-0.165, 0.354, 0.816)  0.58
            7  right         (-0.311, 0.465, 0.741)  0.63      8  right         (-0.366, 0.574, 0.532)  0.67
            9  right, lowest (-0.375, 0.537, 0.397)  0.58
  NOT FUR  collar, talisman, headband, paper tags and a loose eyeball are rigid with the nearest neck or head bone.

THE 100 % FORM'S FIRE: HOW TO HANDLE IT
  Three pairs of flame tufts stand out of the body (the ones the owner circled). Each is modelled and painted as
  flame, is lit in the glow map, and rides WHOLE on one bone, so an effect hung on that bone stays on it in every clip.
  In the mesh they are the vertex group "kn_fire".
  In Unity: make a flame effect a CHILD OF THE BONE below and put it at the tuft. Positions are at rest, in the
  Blender file's measures (metres; +X the fox's left, +Y behind it, +Z up; Unity's axes differ, so place by eye
  from these and check). The exported skeleton keeps only the deforming bones; look the names up in the exported
  file (in Blender they carry a DEF- prefix: DEF-spine.007 and so on).

    tuft            bone                  middle of the tuft        it reaches (y / z)                 about
    shoulder, L     spine.007             ( 0.033, -0.145, 0.480)   -0.22..-0.06 / 0.42..0.58          15 cm tall, rising and sweeping back
    shoulder, R     spine.007             (-0.037, -0.148, 0.477)   the same                           off the withers; the largest
    foreleg, L      front_thigh.L.001     ( 0.078, -0.173, 0.186)   -0.21..-0.14 / 0.12..0.25          11 cm, behind the elbow, pointing
    foreleg, R      front_thigh.R.001     (-0.081, -0.172, 0.187)   the same                           down and back
    hind leg, L     shin.L.001            ( 0.080,  0.100, 0.177)    0.06..0.14 / 0.11..0.25           11 cm, behind the hock, pointing
    hind leg, R     shin.R.001            (-0.077,  0.099, 0.174)   the same                           up and back
    (each tuft stands out sideways from x = 0.00 to about 0.10 from the middle on its own side)

  The same middles in each bone's own space (x, y along the bone, z):
    shoulder L ( 0.033, 0.013, -0.140)   R (-0.037, 0.015, -0.137)      spine.007 runs (0, -0.142, 0.339) to (0, -0.201, 0.343)
    foreleg  L (-0.020, 0.069, -0.055)   R ( 0.023, 0.069, -0.056)      front_thigh.L.001 runs (0.056, -0.243, 0.240) to (0.057, -0.228, 0.181)
    hind leg L ( 0.021, 0.054,  0.081)   R (-0.018, 0.056,  0.078)      shin.L.001 runs (0.059, 0.005, 0.153) to (0.058, 0.051, 0.112)
  Ideas: small upward flame particles from the root of each tuft to its tip, in the tuft's own orange to pale
  yellow; the mesh tuft under it as the solid core (or hide it by scaling the effect over it); a point light of low
  range at each shoulder tuft only (six lights on a pet is too many); embers shed from the leg tufts when it runs.
  The eyes are on the head bone, spine.011 (it runs (0, -0.343, 0.456) to (0, -0.499, 0.487)).

GOOD TO KNOW
  - The models were checked as stills: looks, eyes open and shut, glow maps on the model, an Eevee preview of the glow, bones over the mesh, three test poses. Not
    exported, not seen in Unity. Skin weights on the legs and body are the 25 % form's rules scaled up and have only
    been seen in one stride pose.
  - prep_kitsune_stage.py finds the tails itself (each tail is a piece of mesh of its own, or two or three joined
    at the rump) and stops if it does not find 3, 6 and 9.
  - The flame tufts are found by boxes given in prep_kitsune_stage.py ("fire"), in the import's own measures. The
    shoulder box also takes in a strip of the back's skin under the tuft.

THE CLIPS (built 12 Oct 2026 on the owner's go-ahead; exported the same day)
  Made by anim_kitsune_stage.py (run it on each .blend after bind_kitsune_tails.py; running it again replaces them).
  24 frames a second. Frames, then seconds. Only the acting is in the clips: fire, light, fading and sound are Unity's.
  Every form keeps all the clips of the form before it:
    50 %    Idle, Walk, Run, SpinChase, Shake, Guard, Pounce + FoxfireFlick                         8
    75 %    those 8 + EmberWaltz, Kindle                                                           10
    100 %   those 10 + NinefoldBlaze, SpiritStep                                                   12
  Idle, Walk and Run loop. The others start and end on the rest pose (EmberWaltz ends one whole turn round: the
  same pose).
  The seven the 25 % form has were MADE AGAIN on each rig: the same timings and the same acting of the body, every
  distance scaled to the rig (50 %: x1.22, 75 % and 100 %: x1.48 of the 25 % form), the tails acted afresh for
  three, six and nine tails. Walk and Run are on the spot; one Walk loop is a stride of 0.15 m x that scale, one
  Run loop 0.41 m x that scale (50 %: 0.18 / 0.50 m; 75 %, 100 %: 0.22 / 0.60 m): match the pet's speed to that.
  Foxfire Flick, Ember Waltz and Ninefold Blaze were my proposals; the owner named Kindle and Spirit Step.

  THE SEVEN FROM THE 25 % FORM (all three forms; see README_Kitsune25.txt for more effect ideas)
    Idle       loop 192, 8.00 s   head turns left 31-48, holds to 80, turns right 80-104, holds to 132, centre by 152;
                                  eyes shut 26-28, 92-94, 158-160; ear twitches start 60 (L), 116 (R), 168 (L), 170 (R);
                                  the tails wag as a ripple running across the fan from its left to its right
    Walk       loop 24, 1.00 s    tails trail and sway, each a little after the one beside it
    Run        loop 12, 0.50 s    tails stream and bounce.                 EFFECT a short ember trail off the tail tips
    SpinChase  52, 2.17 s         winds up 1-8, spins 8-31 (fastest 13-26), slows and settles 31-46, rest to 52.
                                  EFFECT ribbon trails off the tail tips 11-40 (three, six or nine ribbons)
    Shake      56, 2.33 s         braces 1-8; shaking 9-40 (strongest 14-34, one side-to-side every 4 frames); eyes shut
                                  7-46, a blink 52-55; settles 41-56.     EFFECT sparks or embers flung off 12-38
    Guard      72, 3.00 s         drops into the stance 1-12; tails curled over its back, tips at its head, by 16; holds
                                  12-54; stands down 54-68; rest to 72. To hold longer, stop on any frame 16-54.
                                  eyes narrowed 11-54
                                  EFFECT a low ring of foxfire on the ground 12-54; tail tips brighter while held
    Pounce     56, 2.33 s         crouches and loads 1-12; rump wiggle 10-18; springs 18-21 (hind paws leave on 21); in the
                                  air 21-32, highest 26-27; front paws land 32, hind paws 36; eyes shut 33-40; settles
                                  36-50; rest to 56.                      EFFECT dust or embers on 32; a streak in the air 21-32

  FOXFIRE FLICK   60 frames, 2.50 s        50, 75, 100
    crouches, winds back   1-15    0.00-0.63   sinks, draws its weight back, the tails go down a little
    the whip               16-22   0.63-0.92   ALL the tails whip up and forward over its back together, the fan
                                               opening; the whip runs out along the tails, tips furthest forward about 21
    the flicks             20-30   0.83-1.25   held over its back, each tail flicks outwards in turn, from its LEFT to its
                                               RIGHT:  50 %: frames 20, 25, 30      75 %: 20, 22, 24, 26, 28, 30
                                               100 %: 20, 21, 22, 24, 25, 26, 28, 29, 30
    held, then down        30-50   1.25-2.08   the fan comes back down; a blink 48-51
    rest                   50-60
    EFFECTS  a wisp of foxfire thrown forward from each tail tip on ITS flick frame: three, six or nine wisps in a
             volley; a short ribbon trail off every tip during the whip (16-22); each tip's glow flaring on its flick.
             Ideas: a soft "whup" per wisp, rising in pitch across the volley; the wisps home on the target.
    BONES    the tip of tail K is the far end of tail_K_5.
    (First made with the tails whipping one after another; a lower tail then passed through the one above it.)

  EMBER WALTZ     96 frames, 4.00 s        75, 100
    gathers                1-8     0.00-0.33
    rears                  8-18    0.33-0.75   onto its hind legs, front paws tucked
    turns                  18-62   0.75-2.58   ONE FULL TURN to its LEFT on the spot, fastest about frame 40; small steps
                                               of the hind paws; it looks where it is going; the tails open into a wheel
                                               (22-56) and are left behind by the turn
    comes down             62-70   2.58-2.92   front paws land on 70
    bows                   70-84   2.92-3.50   deepest 76-84; eyes shut 73-83; the tails swing on past and close
    rises                  84-93   3.50-3.88   rest to 96
    EFFECTS  the tail tips draw a ring of fire as it turns (trails off every tip 18-62; the ring is whole at 62); on
             70 the ring bursts outward as embers; embers drift down through the bow.
             Ideas: the ring on the ground under it instead of in the air; a rising hum 18-62, a soft burst on 70.
    NOTE     the turn is on the "root" control, which ends 360 degrees round: the same pose as the start.

  KINDLE          108 frames, 4.50 s       75, 100
    sits                   6-20    0.25-0.83   rump down on its haunches, front legs straight; the tails settle on the ground
    tail comes round       22-42   0.92-1.75   its outermost LEFT tail (tail 1) swings out and curls round beside its left
                                               flank, tip up
    head turns to it       28-44   1.17-1.83   back and down, to its left; a blink 36-38
    draws breath           45-51   1.88-2.13
    the puff               54      2.25        eyes shut 52-56
    watches                56-80   2.33-3.33   the tip flickers; eyes soft (a quarter shut) 60-80
    tail goes back         80-96   3.33-4.00   stands 86-100; a blink 90-92; rest to 108
    EFFECTS  a small flame takes on the tip of that tail on 55 and burns 55-80, guttering out 80-88; a puff of breath
             (a few sparks) from the muzzle on 54; a warm light on its face 55-80.
    BONES    the flame sits at the far end of tail_1_5; the breath leaves the head bone
             spine.011 at its far end (the muzzle).

  NINEFOLD BLAZE  132 frames, 5.50 s       100 only
    plants its paws        1-14    0.00-0.58   wide stance, head lowered; a blink 22-24
    tails sink             14-30   0.58-1.25   the whole fan sinks and its tips curl down, together (not folded shut:
                                               closing a fan is how its tails meet)
    tails open             36-73   1.50-3.04   from the outside in, a beat apart: frames 36 (tails 1 and 9), 44 (2 and 8),
                                               52 (3 and 7), 60 (4 and 6), 70 (5, the middle). Each snaps OUTWARDS, away
                                               from the middle of the fan, 3 frames after its frame; the fan rises again
                                               through the beats; the body takes each beat
    head thrown back       70-76   2.92-3.17   eyes shut 74-95
    holds, trembling       76-96   3.17-4.00
    comes down             96-124  4.00-5.17   a blink 122-124; rest to 132
    EFFECTS  each tail tip lights as it snaps open (frame + 3): nine lights in five beats, the pair of tails together;
             the body's fire flares with every beat and is at its fullest from 73; on 76 a pillar of fire goes up
             from it and a shockwave ring leaves along the ground; embers rain 80-124.
             Ideas: glow strength up in steps 36-70, white flash on 76, down by 110; a drum beat per pair and a
             roar on 76; camera shake 76-84.
    BONES    tail tips: far ends of tail_1_5 .. tail_9_5. The pillar: on root (it must not tip with the head).

  SPIRIT STEP     48 frames, 2.00 s        100 only
    crouches               1-7     0.00-0.29
    lunges                 7-12    0.29-0.50   forward 0.50 m and a little up, stretched out; eyes shut 11-19
    UNSEEN                 12-18   0.50-0.75   (the clip holds the stretched pose, then jumps to 0.30 m behind its place)
    arrives                18-22   0.75-0.92   low on its front paws
    skids and rises        22-36   0.92-1.50   the tails, left behind, stream past and settle 20-40; rest to 48
    HOW IT IS MEANT TO WORK  the clip ends where it began. To make it a real dash Unity fades the pet OUT on 12, moves it
             one body-length forward (0.70 m) while it is unseen, and fades it IN on 18. Without that it reads as a
             blink away and back on the spot.
    EFFECTS  an afterimage left where it was on 12 (a still copy of the pose fading over 8 frames); a wisp trail
             along the line from the old place to the new; a small burst of foxfire where it lands on 18; the
             tails' fire tips streaming 18-30.
             Ideas: a soft whoosh down on 12 and up on 18; the see-through tails fade a beat after the body.

  THE TAILS IN EVERY CLIP (the owner, 12 Oct 2026: they must flow, and no tail may pass through another)
  - Every tail carries a slow wave from root to tip for the whole of every clip, so a tail is never a stick.
  - The tails move as ONE FAN: at any frame they all take the same swing, most of it at the roots (which are carried
    round one middle point so the fan turns as a solid thing) and a part of it as a bend along the tail. The more they
    bend, the wider the fan opens. What one tail does on its own is a turn straight out from the middle of the fan.
    Bending every tail far along its length, or moving one tail across the others, is what made them cross.
  - CHECKED with tail_clip_check.py (every clip, every frame or every second frame; each tail's faces against every
    other tail's and against the body's, beyond each tail's first two bones, where they are joined as modelled):
        50 %     nothing touches at rest, and nothing in any of its 8 clips
        75 %     3 face pairs touch at rest (tails 3 and 4, the two top ones, as modelled). Nine clips never go over
                 that; FoxfireFlick has 4 on one frame (23)
        100 %    30 face pairs touch at rest (neighbouring tails, as modelled). Eleven clips never go over that;
                 NinefoldBlaze has 31 on two frames (47, 49)
        no tail touches the body in any clip on any form
    Run it again after any change to a clip:  blender -b SM_Pet_KitsuneSpirit_NN.blend -P tail_clip_check.py
  - The price: the tails now move together more than they did. Guard lifts the fan over its back as one; it no longer
    curls every tail right over.

  GOOD TO KNOW ABOUT THE CLIPS
  - Checked as videos and frame strips (flat light, no glow, tails not see-through; the blinks also close up with the
    glow). Not seen in the game.
  - Controls a clip does not move are keyed at rest on its first and last frame, as on the 25 % form.
  - The tail roots (tail_K_1) are MOVED as well as turned (that is how the fan turns as one): keep their position curves.
