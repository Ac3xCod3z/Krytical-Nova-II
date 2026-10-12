KITSUNE SPIRIT, the 25 % form
Made 11 Oct 2026 from the artist's Tripo model. Exported 12 Oct 2026.

WHERE IT IS
  In the game   Assets/_Game/Characters/Pets/KitsuneSpirit_25/
                SM_Pet_KitsuneSpirit_25.fbx (4,805 triangles, 51 bones, Generic rig, faces +Z), Pet_KitsuneSpirit_25.prefab,
                AC_Pet_KitsuneSpirit_25.controller (loops Idle; Walk and Run by true/false, the rest by trigger),
                M_Pet_KitsuneSpirit_25.mat (texture and emission), M_Pet_KitsuneSpirit_25_Eye.mat, the two textures
  Package       Downloads/Pets/KitsuneSpirit/KitsuneSpirit25/KitsuneSpirit25.unitypackage (the same files)
  NOT DONE      nothing is set up in the pet system (no pet data, no evolution link from the first form, no effects,
                no sounds). The folder was copied in as files; the game's own Editor had not imported it when this was
                written. The import was checked in a throwaway Unity project only.
  Leave "Bake Axis Conversion" OFF on the model. Its resting pose in Unity is the first frame of Guard, which is
  the fox standing.

The model is the artist's own, as imported: same mesh, same texture. (A rebuilt flat-colour version was tried the
same day and turned down by the owner: "use the one I imported, just unwrap the UV, I will try to fix it, and
leave the eye fix".)

FILES (Assets/_ArtSource/Blender~/Pets/KitsuneSpirit/)
  SM_Pet_KitsuneSpirit_25.blend          the model, its rig and its six clips (the Tripo model is kept inside, hidden)
  T_Pet_KitsuneSpirit_25.png             its texture on the new UV map, 2048 x 2048: THE FILE TO PAINT
  T_Pet_KitsuneSpirit_25_Emission.png    the tail tips on black: their glow, to be set in Unity
  T_Pet_KitsuneSpirit_25_UVLayout.png    the UV islands drawn over the texture, to find one's way when painting
  prep_kitsune_25.py                     size, the new UV map, the texture carried over, the eyes and eyelids
  rig_kitsune_25.json                    where the rig's bones sit on this model (read by pet_rig.py)
  anim_kitsune_25.py                     the clips
  Order: prep_kitsune_25.py, pet_rig.py --landmarks rig_kitsune_25.json, anim_kitsune_25.py.
         Not pet_finish.py: it would put the model on the swatch and its texture would be lost.
         Not pet_eyes.py: its flat eye plates did not follow this model's eye shape (see THE EYES).

PAINTING THE TEXTURE
  The UV map is new ("map1"). Tripo's own was hundreds of scraps; this one is cut the way a painter would:
    each leg     one sleeve, cut down its inner side (the wraps run straight across it)
    each tail    one island, cut along its underside
    each ear     one island, cut up its back
    head         one island, cut under the jaw
    body         one island, cut along the belly
    scarf, talisman   by angle (a few small islands each)
  33 islands, all at one scale. The texture was carried over texel for texel, nothing repainted, with the colour
  bled 12 texels past each island's edge so seams do not show.
  Paint T_Pet_KitsuneSpirit_25.png and save it over the same file, or paint on the model in Blender (Texture Paint).
  WARNING: running prep_kitsune_25.py again makes the texture afresh from the Tripo model. Keep a copy of painted work.
  If the tail tips are repainted, the emission picture should be made again to match (say so).

THE MODEL
  0.66 m long (the first form is 0.60 m), 0.38 m to the ear tips, 4,805 triangles, pivot on the ground under it.
  Thinned on the owner's word (11 Oct 2026) from the 9,971 it came with: the body keeps half its triangles, the
  eyeballs under a third. Its shape and its texture are kept (the texture is read from the full model).
  The mesh is as it came: it has 135 open edges of its own (it is not a closed mesh).

THE EYES (redone 11 Oct 2026: "align your eyes and eyelids better to the original eye shape")
  The eyes are the model's own eyeballs, left in their sockets, so their shape is exactly the model's. They have one
  flat near-black material (Eye).
  Each eyelid is a segment of a shell round the eyeball, from one corner of the eye to the other, in the fur's colour.
  Open, it sits inside the head just above the socket; it shuts by turning about the eye's long axis (38 degrees).
  Bones eyelid.L / eyelid.R: turn the control about its own length (its local Y). The turn that shuts each eye is
  stored on the mesh object as kn_lid_turn_L / kn_lid_turn_R.
  Materials: SM_Pet_KitsuneSpirit_25 Body (the texture and its emission), SM_Pet_KitsuneSpirit_25 Eye (black).
  At export:  pet_export.py --own-materials, then make_unitypackage.py --own-texture T_Pet_KitsuneSpirit_25.png
              --emission-texture T_Pet_KitsuneSpirit_25_Emission.png --material "... Body=texture" --material "... Eye=#000000"
  The talisman is the model's own, untouched (its mark sits where Tripo painted it).

THE RIG
  The same Rigify quadruped rig as the first form, moved to this model's joints, so the first form's clips run on it.
  Two tails, one each side: four bones each, tail_1 .. tail_4 with .L and .R, each with its own control.
  Ears ear.L / ear.R, eyelids eyelid.L / eyelid.R (turn a lid's control about its own length to shut the eye: see THE EYES).
  In the open Blender scene this form's clips are named "Idle (Kitsune 25)" and so on, because the first form's
  Idle, Walk, Run and SpinChase have the same names; in this pet's own file they are plain Idle, Walk ...

CLIPS, 24 frames a second
  Idle        192   loop (8.0 s)
  Walk         24   loop (1.0 s), on the spot: matches a pet moving at 0.15 m/s
  Run          12   loop (0.5 s), on the spot: matches a pet moving at 0.81 m/s
  SpinChase    52   2.17 s, starts and ends at rest
  Shake        56   2.33 s, starts and ends at rest
  Guard        72   3.0 s, starts and ends at rest
  Pounce       56   2.33 s, starts and ends at rest

TIMINGS (frame, then seconds from the start of the clip)

  Idle
    tails       wagging the whole loop as a pair: waves from base to tip, about one wag every 2 s, the right tail a
                moment behind the left. They come close but never pass through each other (measured on every second
                frame of all six clips: no tail vertex inside the other tail)
    head        turns to its left 31-48 (1.25-1.96 s), holds to 80 (3.29), turns to its right 80-104 (3.29-4.29),
                holds to 132 (5.46), back to the middle by 152 (6.29)
    blinks      shut on 26 (1.04), 92 (3.79), 158 (6.54)
    ear twitch  left 60 (2.46), right 116 (4.79), left 168 (6.96) with right 170 (7.04); each lasts 8 frames
    breathing   one breath every 48 frames (2 s)

  Walk, Run     the first form's gaits. Paw order in the walk: left hind 1, left front 7, right hind 13, right front 19.
                Run: hind paws land 1 and 2, front paws 6 and 7 (footstep sounds).

  SpinChase     dips onto its front legs 1-8, pushes off 8-13 (front paws land 13), spins, a full turn is reached on 40,
                overshoots and settles 41-52. Turns to its left.

  Shake
    braces      1-8 (0.00-0.29 s): sinks a little, head down
    shaking     9-40 (0.33-1.63 s), strongest 14-34; one side-to-side every 4 frames (6 a second). The shake starts at
                the head and runs down the back to the tails, each part a little later than the one ahead of it
    eyes        shut 7-43, one more blink 50-56
    settles     41-56
    effect      a spray of drops fits 12 to 38 (0.46-1.54 s), thrown from the neck and back

  Guard
    drops in    1-12 (0.00-0.46 s): paws spread wider, body lower, head low and level, ears back, eyes narrowed
    tails       curl up over its back 5-16, tips pointing at its head; fully curled from 16 (0.63 s)
    holds       12-54 (0.46-2.21 s). To hold the stance longer, stop on any frame from 16 to 54
    stands down 54-68 (2.21-2.79 s); at rest to 72

RIG NOTES (fixed 11 Oct 2026, after the owner saw bad front legs and an oddly bending back)
  front legs   the first landmarks made Rigify fold the paws back at rest (34 mm off). The middle bone of the leg
               must stand upright, as on the first form. rig_rest_check.py now passes.
  back         the hips bone was a bendy bone steered by the metarig's single tail, here a stub inside the rump.
               That bent the hips 57 mm out of shape standing still, and the bone ahead threw the skin about on the
               smallest turn. The spine's bones are plain bones now ("plain" in rig_kitsune_25.json).
  weights      leg bones hold only the skin near them ("reach", "hold_below"), the rump does not follow the tails
               ("only"), the stub tail holds nothing ("give_to").
  clips        every clip keys every control at rest on its first and last frame, so a pose left by the clip shown
               before it does not carry over when clips are switched in Blender.
  Never set rotations on the rig's inner bones (MCH-, ORG-, DEF-): Rigify keeps a starting bend on its IK bones.

  Pounce        a fox's mousing pounce, on the spot (the game does not have to move the pet)
    crouches    1-12 (0.00-0.46 s): sinks and draws its weight back onto its haunches, head down on its target, ears pricked
    wiggle      10-18 (0.38-0.71 s): rump and tails side to side, quickening; head still; a half-blink on 15
    springs     18-21 (0.71-0.83 s): tips nose-up over its hind paws; hind paws leave the ground on 21 (0.83 s)
    in the air  21-32 (0.83-1.29 s): 11 frames, and it flies the path a thrown thing flies in that time: highest on
                26-27 (1.06 s), a quarter of a metre above the line from take-off to landing; it turns over nose-down
                at a steady rate about its own middle; tails left behind at first, streaming up behind in the dive
    lands       front paws 32 (1.29 s), hind paws 36 (1.46 s); eyes shut 33-37; the head nods, the ears flop forward
                and the tails are thrown forward over its back, each swinging past and settling
    settles     36-50 (1.46-2.04 s): takes the landing in its legs twice (front, then hind), rises a touch too far, rests
                at rest to 56

EFFECT IDEAS (for the owner's VFX in Unity; none of this is in the clips)
  Tail tips glow in every clip (the emission picture). Their bones for trails: tail_4.L and tail_4.R (the tips).
  Idle        slow embers or motes drifting up off the tail tips, one or two at a time; a soft pulse of the tips' glow
              with each breath (every 48 frames); a glint in the eye on each ear twitch (60, 116, 168)
  Walk        faint paw-print glows that fade, on the paw frames (1, 7, 13, 19); the tips leave short soft trails
  Run         longer trails off both tail tips, a little dust or sparks on the hind paws (1, 2) and front paws (6, 7);
              a speed-line or two along the body
  SpinChase   the owner's: a line trail showing the movement. A ribbon off each tail tip (two rings, one inside the
              other) from 11 to 40, brightest 13-31, fading as it slows 31-46; a ring of dust under it on the push-off
              (8-13); a small spark where the trail closes on itself at the full turn (40)
  Shake       a spray of drops or spirit-sparks thrown off 12-38, most 14-34, from the neck and back, in time with the
              side-to-side (every 4 frames); a few last drops off the tail tips 40-46; ears and tails blur-streaks
  Guard       a ward: a ring or dome rising round it 5-16 as the tails curl, held 16-54, dropping 54-68; the tail tips
              flare as they stop over its back (16); small sparks jumping between the two tips while it holds
  Pounce      dust puff under the hind paws on the spring (20-21); an arc trail off the tail tips through the air
              (21-32); a target mark on the ground ahead from the crouch (8) until the landing; on 32 a burst where
              the front paws strike (dust ring, sparks, or the hit effect), a smaller one on 36; a slow-in on 26-27 at
              the top if the game wants the pounce to read

KNOWN LIMITS
  Only stills and small GIFs were looked at: no clip has been watched full size, and nothing was checked in the game.
  The size (0.66 m) and the Idle, Shake and Guard timings are first passes: say what to change.
  Walk and Run strides were tuned on the first form (0.60 m); on this one the paws may slide very slightly.
  The leg wraps and the talisman are as Tripo painted them: the new UV map is there so they can be fixed by hand.
