TIDECALLER, the 50 %, 75 % and 100 % stages: three more clips
Made 11-12 Oct 2026. Exported 12 Oct 2026: the three packages in Downloads/Pets/Tidecaller hold them, and the
model files in the game (Assets/_Game/Characters/Pets/Tidecaller_50, _75, _100) were replaced with the new ones.
ONLY the model files were replaced in the game. Its controllers and prefabs were set up by another session and were
left alone, so THE GAME'S CONTROLLERS HAVE NO TRIGGERS FOR THE NEW CLIPS YET: add ShellSpin, Undertow and Maelstrom
there (the controllers inside the packages have them, as a pattern).

WHO GETS WHICH (the owner's word)
  50 %     ShellSpin
  75 %     ShellSpin, Undertow
  100 %    ShellSpin, Undertow, Maelstrom
  The 25 % stage and the egg get none of them.

HOW THEY ARE MADE
  more_clips_tidecaller.py, beside this file, adds them to a stage's .blend from the rig alone (the model is not built
  again). Run it on SM_Pet_Tidecaller_50 / _75 / _100.blend; running it again replaces the three clips.
  A stage built afresh with build_tidecaller_stage.py does not have them until this script is run on it.

THE CLIPS, 24 frames a second. Each starts and ends on the first frame of Idle, like TideCall, SwimFlip and Ram.
Only the acting is in them: the water, light and sound are Unity's. Frames, then seconds from the start.

  ShellSpin   100 frames, 4.17 s        50, 75, 100
    winds back      1-16     0.00-0.63   sinks a little, fins draw in, turns 22 degrees the wrong way
    lets go         16       0.63        starts to turn (to its left)
    spins up        16-28    0.63-1.13
    full spin       28-58    1.13-2.38   four turns in all, about 1.4 turns a second, rising slowly, a slow wobble
                                         like a top's; fins tucked flat; eyes shut 18-62
    throws fins out 58-61    2.38-2.50   the brake: fins and tail fins snap wide
    stops           70       2.88        swings 20 degrees past and back, twice, smaller
    settles         70-100   2.88-4.17   sinks back into place with a bob; the eyes wander, dizzy (25-75 % eyes only); blink 92
    EFFECTS  the owner: a water tornado round it as it turns.
             forms 16-28 (rising from under it as it spins up); full height 28-58, turning with it; on 60 the fins
             fly out and the tornado is flung outwards as a ring of water and spray; gone by 70.
             ideas: a ripple ring under it on 16; the light blue lines brightening with the spin's speed (emission
             up from 16, full 28-58, flash on 60); a few slow drips 70-90; a low whirr rising 16-28 and cutting on 60.

  Undertow    120 frames, 5.00 s        75, 100
    climbs          1-18     0.00-0.75   two strokes (fins down on 10 and 18), to 0.4 of its height up
    tips over       18-26    0.75-1.08   nose down
    corkscrews down 26-50    1.08-2.08   two full turns, banked, fins swept back, to just below where it started
    hangs, gathered 50-58    2.08-2.42   level, small and tight; eyes shut 48-59
    drives up       58-64    2.42-2.67   ONE great stroke: fins up on 59, down hard by 64; the body long
    rising          64-74    2.67-3.08   straight up, nose to the sky, to 0.66 of its height above its place
    crests, brakes  74-84    3.08-3.50   levels out; fins thrown wide on 80
    drifts down     84-114   3.50-4.75   sinking, not falling, with a bob at the bottom; blink 106
    EFFECTS  a whirlpool or funnel of water drawn down round it 26-50, tightest at 50; a held breath of bubbles
             and a dimming 50-58; a geyser or column of water under it 59-74 (the stroke lands on 64); a burst of
             spray and falling drops on 80; rain of drops to 100.
             ideas: emission low at 50-58 and bright at 59-64; a deep "whump" on 59; camera shake, small, on 64.

  Maelstrom   138 frames, 5.75 s        100 only
    bows            1-14     0.00-0.58   sinks, head down, fins in; eyes shut 3-18
    rears up        14-36    0.58-1.50   tall, nose up, every fin spread
    gathers         36-72    1.50-3.00   holds, leaning further and further back, trembling harder, swelling to 1.11
                                         times its size
    the clap        72-78    3.00-3.25   throws its whole body forward (0.36 of its height); front fins swept
                                         forward to meet on 78; eyes shut 77-85
    thrown back     78-88    3.25-3.67   squashed by the blow, fins flung apart, nose up
    paddles home    88-138   3.67-5.75   two slow strokes back to its place, a bob; blink 118
    EFFECTS  the owner: a wave forms behind it and launches violently forward on the clap, with a shockwave and lightning.
             the wave rises behind it 36-72, growing with the tremble; on 78 it breaks over and past the Tidecaller
             and is thrown forward; the shockwave ring leaves from the fins on 78; lightning strikes 78-82.
             ideas: emission climbing 36-72 to its brightest on 76, white flash on 78, dim 80-90; small arcs
             crawling over the shell 60-76; rumble rising 36-72, silence on 76-77, the crack on 78; camera shake 78-84;
             water raining back 84-110.

BONES FOR EFFECTS
  body        the shell: attach the tornado, the funnel and the glow here (it carries the turn and the tilt)
  root        stays put at the pet's place: attach what must NOT turn or travel with it (a ground ripple, the wave's start)
  fin.L/R     the front fins: the clap is where their tips meet, straight ahead of the body
  The clips move the body bone, not the root: ShellSpin and Undertow turn it, Undertow and Maelstrom carry it up to
  0.66 and forward up to 0.36 of the pet's height. The renderer's bounds should allow for that.

GOOD TO KNOW
  - The fins and the body do not move as one: the body leads, the front fins follow two frames later and the back
    fins three. Cue effects from the frames above, not from a fin reaching its limit.
  - ShellSpin ends four full turns round and Undertow two, which is the same pose as the start: they blend to Idle
    as the other clips do.
  - Maelstrom scales the body bone (as TideCall does), and the eyelids are scaled to show and hide: do not strip
    scale curves from the clips.
  - Undertow dips 7 % of the pet's height below its place for a moment. The pet floats above the ground already.
  - Checked as strips of stills on the 100 % stage and as GIFs, and imported in a throwaway Unity project; not in the game.

THE 75 % STAGE'S PUPILS (12 Oct 2026)
  Its pupils looked wall-eyed: each was set where its socket faces, 45 degrees outwards. On the owner's word they are
  now where the concept art has them, in the middle of the eye that shows: moved 28 degrees towards the nose and 3 down
  (17 degrees out from straight ahead). Only the pupil moved, not the ball; nothing else on any stage was changed.
  In the Blender file, in build_tidecaller_stage.py ("pupils_in"), and in the package and the game's model as
  exported 12 Oct 2026.
