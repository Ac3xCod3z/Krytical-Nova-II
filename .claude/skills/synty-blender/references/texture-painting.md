# Painting a texture onto a model's own UV map

For a model that arrives with a UV map and no texture (or a texture to be replaced), and reference
pictures of how it should look. Worked out on the Drone Core 100 %, October 2026, over many rounds of
the artist's mark-up. The working example is
`Assets/_ArtSource/Blender~/Pets/DroneCore/paint_dronecore_100.py`: copy it for the next model and
change the data at its top (names, colours, `STENCILS`); the painting code below the data is general.

## The method, in the order it is done

1. **Parts first (the ID map).** Split the mesh into parts along its hard edges (faces joined across an
   edge flatter than `HARD` degrees are one part). Each part gets ONE flat colour, filled edge to edge
   on the UV map. The colour is the one most of the part shows in the reference pictures that see it.
   This is the look of the Synty-treated pets: no gradients, no specks.
2. **Lights are placed by hand, as data.** Never let the script decide where a light is by reading
   picture colours. Trace every light as a shape (a line with corner points and a width, a box, an
   arc) on a gridded enlargement of the straight-on reference picture, in that picture's pixels.
3. **A traced shape does not paint. It selects.** A picture is never the mesh to the pixel, so a shape
   drawn from it lands beside the model's channels. The shape only says *which* channel is lit; the
   light itself is drawn from the mesh:
   - **Creases** (front and sides of the drone): an inward fold of the mesh that runs along a traced
     shape, close beside it, gets a band of fixed width centred on the fold. Of several folds beside
     one stretch of a line, only the nearest run is taken. A fold goes to the *nearest* shape, not the
     first in the list. A box selects every fold inside it that runs its long way (the valleys of a vent).
   - **Whole small parts** (the drone's back): a slot floor or a slat that a shape covers becomes that
     light whole, if it is about as wide as the shape.
   - **On the bare surface**, as traced, only for a light the mesh has no channel for.
4. **Emission map.** The same fill, with everything that does not glow left black.
5. **Check close up, against the reference, before showing it.** Render close-ups of every lit area
   (not only whole-model views) with back-face culling on, and compare each with the same area of the
   reference. Whole-model sheets hide exactly the faults the artist sees first.

## What went wrong on the way, so it is not repeated

| Tried | What the artist saw | Why |
|---|---|---|
| Copying the pictures onto the surface point by point | Splotches, blue and yellow outside the lines | The glow round each light is copied too, and the picture never lines up exactly |
| Letting a part be a light when most of it looked lit | A whole vent box yellow, lines missing | Thin lines are not parts; big parts catch the glow |
| Drawing the traced shapes straight onto the surface | Close, but lights beside the channels, not in them | The reference picture and the mesh differ by a few pixels everywhere |
| Lighting whole small parts under a shape, on every side | Yellow filled edge to edge, fat blue frame strips | A channel on a panel is a crease, not a part of its own |
| First shape in the list wins a fold | The yellow bar on the fold below its slot, the slot itself blue | Two shapes were both in reach; the nearer one must win |
| Round ends on a crease's band | Yellow drips over the top edge of the vent | The band spilled past the end of its fold onto the next face |

## Rules of thumb

- Ask for straight-on pictures of every side. An angled picture can be matched to the model (turn it
  until the outlines agree) well enough to pick colours for parts, not to place small lights.
- With no straight-on picture of a side, set its shapes on the model itself: render that side clay,
  straight on, with a measuring grid, and read the slots and slats off it.
- The artist's own mark-up on a clay render (lines drawn where the lights go) outranks the reference
  picture: it shows which channels of the *mesh* are meant.
- Keep the numbers that tune the snapping (reach, crease angle, widths) as named constants at the top.
- Report what each shape found: how many creases or channels it lit, and which shapes found none.
- A game draws one side of a face. Check the finished model with back-face culling on; a mesh can have
  no open edges and still be inside out.

## Not done yet

- The painter is a per-model script, not yet a tool that takes the data as a file.
- Lights on curved runs (the brow, the ring round the lens, the arc over the cannon) are only lit where
  the mesh has a clear fold; where the channel is a gentle dip they are missing.
