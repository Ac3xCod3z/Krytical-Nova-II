# The Crucible - Unity FX and lighting list (checkpoint 022b)

Every light, fire, glow and shrine marker of the approved 022 look, with its exact place. The full data (Blender and Unity positions, rotation as euler and quaternion, linear colour, intensity, range, bake mode, shadows, flicker, VFX) is **`FX_022.json`**; lava paths, heat haze, embers and smoke are **`LAVA_022.json`**; the picture is **`LavaMap_022.jpg`** (arena) and **`LavaMap_022_full.jpg`** (whole map).

- Axes: Unity (x, y, z) = Blender (-x, z, -y), metres; the Crucible heart is (0, 0, 0) at rim level, arena floor y = -7.5. Euler in Unity order (Z, X, Y), degrees.
- Colours are LINEAR RGB (Unity colour picker in linear, or convert to sRGB for the hex field). Light intensity = URP intensity; emissive "EV" = HDR exposure stops on the emission colour.
- Marker names `FX_<Type>_<Area>_<nn>` are the empties in the blend (collection FX_Markers_For_Unity). `PreviewFlames_PREVIEW_ONLY` cones are preview stand-ins: never import them.

## Light budget (60 fps on mid hardware)

| Group | Mode | Shadows | Count | Why |
|---|---|---|---|---|
| Cloth | None | Off | 61 | banner sway (wind zone), no light |
| Fire | Baked | Off | 69 | every other fire: baked into lightmap / APV probes; flicker lives on the emissive + flame VFX |
| Fire | Realtime | Off | 10 | hero fires where players stand (gate mouths, Null gate foot, Covenant shrine, sealed stair): small unshadowed light (range <= 10 m) with flicker |
| Glow | Baked | Off | 1 | sealed-gate shimmer |
| Glow | None | Off | 81 | light strips: emissive only (bloom), they light nothing realtime |
| Lava | Baked | Off | 21 | crack and outer-flow glow: baked + emissive pulse |
| Lava | Mixed | Off | 1 | heart lava glow: realtime on characters, baked on the floor |
| Shrine | Mixed | Soft | 3 | three respawn shrines: soft shadow, low-res (256), range 18 m |
| Sky | Mixed | Soft | 1 | the moon: the one shadowed directional light |
| Sky | None | Off | 1 | sky dome (unlit) |

Rules: at most 1 shadowed directional + 3 shadowed spot/point lights in the whole map, and never more than about 8 realtime lights touching one view (URP Forward+). Use APV / light probes for the baked fires on moving characters. Flicker = animate the flame emissive and, on Realtime fires only, light intensity +-15 % at 6-10 Hz with noise. Bake with the flame emissives on (Contribute GI), strips at EV 3.

## Moon, shrines, heart lava, sealed gate

| Marker | Unity position (x, y, z) | Linear colour | Light | Intensity | Range m | Mode | Shadows | Flicker |
|---|---|---|---|---|---|---|---|---|
| FX_LavaCracks_Heart_01 | (-0.0, -6.1, -0.0) | (1.0, 0.2, 0.05) | Point | 4.0 | 25.0 | Mixed | Off |  |
| FX_SealChains_SealedNorth_01 | (-0.0, 9.0, -113.0) | (0.5, 0.3, 1.0) | Point | 2.0 | 12.0 | Baked | Off |  |
| FX_ShrineBeam_Covenant_01 | (-152.0, 5.0, -0.0) | (1.0, 0.62, 0.12) | Spot up + Point | 6.0 | 18.0 | Mixed | Soft |  |
| FX_ShrineBeam_Hegemony_01 | (152.0, 5.0, -0.0) | (0.36, 0.62, 1.0) | Spot up + Point | 6.0 | 18.0 | Mixed | Soft |  |
| FX_ShrineBeam_Null_01 | (-0.0, 5.0, 152.0) | (1.0, 0.1, 0.06) | Spot up + Point | 6.0 | 18.0 | Mixed | Soft |  |
| FX_Skybox_01 | (-0.0, 150.0, -0.0) | (0.2, 0.1, 0.4) | None | 0.0 | 0.0 | None | Off |  |
| Moon | (-0.0, 0.0, -0.0) | (0.6200000047683716, 0.6000000238418579, 1.0) | Directional | 0.35 | 0 | Mixed | Soft |  |

## Fires (every brazier, fire pit, fire basket and purple flame pillar)

| Marker | Unity position (x, y, z) | Linear colour | Light | Intensity | Range m | Mode | Shadows | Flicker |
|---|---|---|---|---|---|---|---|---|
| FX_BrazierFlame_Covenant_01 | (-112.4, 6.107, -7.6) | (1.0, 0.42, 0.1) | Point | 2.5 | 10.0 | Realtime | Off | yes |
| FX_BrazierFlame_Covenant_02 | (-112.4, 6.107, 7.6) | (1.0, 0.42, 0.1) | Point | 2.5 | 10.0 | Realtime | Off | yes |
| FX_BrazierFlame_Covenant_03 | (-150.0, 7.409, -36.4) | (1.0, 0.42, 0.1) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Covenant_04 | (-150.0, 7.409, 36.4) | (1.0, 0.42, 0.1) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Covenant_05 | (-158.9, 17.4, -0.0) | (1.0, 0.42, 0.1) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Covenant_06 | (-156.3, 7.409, 8.0) | (1.0, 0.42, 0.1) | Point | 2.5 | 10.0 | Realtime | Off | yes |
| FX_BrazierFlame_Covenant_07 | (-156.3, 7.409, -8.0) | (1.0, 0.42, 0.1) | Point | 2.5 | 10.0 | Realtime | Off | yes |
| FX_BrazierFlame_Null_01 | (-7.4, 3.0, 112.6) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Realtime | Off | yes |
| FX_BrazierFlame_Null_02 | (7.4, 3.0, 112.6) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Realtime | Off | yes |
| FX_BrazierFlame_Null_03 | (-8.5, 19.0, 115.9) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_04 | (-41.0, 19.0, 161.0) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_05 | (-41.0, 19.0, 116.5) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_06 | (-19.5, 3.4, 128.0) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_07 | (-19.5, 3.4, 150.0) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_08 | (-38.6, 13.6, 128.0) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_09 | (-38.6, 13.6, 139.0) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_10 | (-38.6, 13.6, 150.0) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_11 | (8.5, 19.0, 115.9) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_12 | (41.0, 19.0, 161.0) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_13 | (41.0, 19.0, 116.5) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_14 | (19.5, 3.4, 128.0) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_15 | (19.5, 3.4, 150.0) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_16 | (38.6, 13.6, 128.0) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_17 | (38.6, 13.6, 139.0) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_18 | (38.6, 13.6, 150.0) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_19 | (24.0, 13.6, 158.6) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_20 | (8.0, 13.6, 158.6) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_21 | (-8.0, 13.6, 158.6) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_22 | (-24.0, 13.6, 158.6) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_23 | (9.6, 3.0, 125.2) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Realtime | Off | yes |
| FX_BrazierFlame_Null_24 | (-9.6, 3.0, 125.2) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Realtime | Off | yes |
| FX_BrazierFlame_Null_25 | (12.5, 2.8, 150.5) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_Null_26 | (-12.5, 2.8, 150.5) | (1.0, 0.12, 0.03) | Point | 2.5 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_01 | (-77.9, 0.152, 9.9) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_02 | (-106.5, 7.652, 8.8) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_03 | (-77.9, 0.152, -9.9) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_04 | (-106.5, 7.652, -8.8) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_05 | (-78.56, 7.652, -72.054) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_06 | (-72.054, 7.652, -78.56) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_07 | (-9.9, 0.152, -77.9) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_08 | (-8.8, 7.652, -106.5) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_09 | (9.9, 0.152, -77.9) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_10 | (8.8, 7.652, -106.5) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_11 | (72.054, 7.652, -78.56) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_12 | (78.56, 7.652, -72.054) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_13 | (77.9, 0.152, -9.9) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_14 | (106.5, 7.652, -8.8) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_15 | (77.9, 0.152, 9.9) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_16 | (106.5, 7.652, 8.8) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_17 | (78.56, 7.652, 72.054) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_18 | (72.054, 7.652, 78.56) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_19 | (9.9, 0.152, 77.9) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_20 | (8.8, 7.652, 106.5) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_21 | (-9.9, 0.152, 77.9) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_22 | (-8.8, 7.652, 106.5) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_23 | (-72.054, 7.652, 78.56) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_24 | (-78.56, 7.652, 72.054) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_25 | (-69.537, 1.152, -25.309) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_26 | (-52.326, 1.152, -52.326) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_27 | (-25.309, 1.152, -69.537) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_28 | (25.309, 1.152, -69.537) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_29 | (52.326, 1.152, -52.326) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_30 | (69.537, 1.152, -25.309) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_31 | (69.537, 1.152, 25.309) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_32 | (52.326, 1.152, 52.326) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_33 | (25.309, 1.152, 69.537) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_34 | (-25.309, 1.152, 69.537) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_35 | (-52.326, 1.152, 52.326) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleArena_36 | (-69.537, 1.152, 25.309) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurplePerimeter_01 | (-116.5, 7.652, 79.0) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurplePerimeter_02 | (-116.5, 7.652, -79.0) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurplePerimeter_03 | (-71.5, 7.652, -116.5) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurplePerimeter_04 | (71.5, 7.652, -116.5) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurplePerimeter_05 | (116.5, 7.652, -79.0) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurplePerimeter_06 | (116.5, 7.652, 79.0) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurplePerimeter_07 | (79.0, 7.652, 116.5) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurplePerimeter_08 | (-79.0, 7.652, 116.5) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Baked | Off |  |
| FX_BrazierFlame_PurpleSealed_01 | (8.8, 7.052, -110.6) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Realtime | Off | yes |
| FX_BrazierFlame_PurpleSealed_02 | (-8.8, 7.052, -110.6) | (0.55, 0.14, 1.0) | Point | 2.0 | 10.0 | Realtime | Off | yes |

## Lava point lights (baked)

| Marker | Unity position (x, y, z) | Linear colour | Light | Intensity | Range m | Mode | Shadows | Flicker |
|---|---|---|---|---|---|---|---|---|
| FX_LavaCracks_ArenaFloor_01 | (-36.955, -7.4, -15.307) | (1.0, 0.2, 0.05) | Point | 2.0 | 14.0 | Baked | Off |  |
| FX_LavaCracks_ArenaFloor_02 | (-15.307, -7.4, -36.955) | (1.0, 0.2, 0.05) | Point | 2.0 | 14.0 | Baked | Off |  |
| FX_LavaCracks_ArenaFloor_03 | (15.307, -7.4, -36.955) | (1.0, 0.2, 0.05) | Point | 2.0 | 14.0 | Baked | Off |  |
| FX_LavaCracks_ArenaFloor_04 | (36.955, -7.4, -15.307) | (1.0, 0.2, 0.05) | Point | 2.0 | 14.0 | Baked | Off |  |
| FX_LavaCracks_ArenaFloor_05 | (36.955, -7.4, 15.307) | (1.0, 0.2, 0.05) | Point | 2.0 | 14.0 | Baked | Off |  |
| FX_LavaCracks_ArenaFloor_06 | (15.307, -7.4, 36.955) | (1.0, 0.2, 0.05) | Point | 2.0 | 14.0 | Baked | Off |  |
| FX_LavaCracks_ArenaFloor_07 | (-15.307, -7.4, 36.955) | (1.0, 0.2, 0.05) | Point | 2.0 | 14.0 | Baked | Off |  |
| FX_LavaCracks_ArenaFloor_08 | (-36.955, -7.4, 15.307) | (1.0, 0.2, 0.05) | Point | 2.0 | 14.0 | Baked | Off |  |
| FX_LavaFlow_OuterGround_01 | (-175.305, -9.8, -4.663) | (1.0, 0.28, 0.06) | Point | 3.0 | 30.0 | Baked | Off |  |
| FX_LavaFlow_OuterGround_02 | (-143.049, -9.8, -78.896) | (1.0, 0.28, 0.06) | Point | 3.0 | 30.0 | Baked | Off |  |
| FX_LavaFlow_OuterGround_03 | (-108.504, -9.8, -143.592) | (1.0, 0.28, 0.06) | Point | 3.0 | 30.0 | Baked | Off |  |
| FX_LavaFlow_OuterGround_04 | (-11.178, -9.8, -163.498) | (1.0, 0.28, 0.06) | Point | 3.0 | 30.0 | Baked | Off |  |
| FX_LavaFlow_OuterGround_05 | (72.174, -9.8, -162.697) | (1.0, 0.28, 0.06) | Point | 3.0 | 30.0 | Baked | Off |  |
| FX_LavaFlow_OuterGround_06 | (131.538, -9.8, -123.498) | (1.0, 0.28, 0.06) | Point | 3.0 | 30.0 | Baked | Off |  |
| FX_LavaFlow_OuterGround_07 | (166.559, -9.8, -39.986) | (1.0, 0.28, 0.06) | Point | 3.0 | 30.0 | Baked | Off |  |
| FX_LavaFlow_OuterGround_08 | (167.337, -9.8, 34.907) | (1.0, 0.28, 0.06) | Point | 3.0 | 30.0 | Baked | Off |  |
| FX_LavaFlow_OuterGround_09 | (135.736, -9.8, 122.736) | (1.0, 0.28, 0.06) | Point | 3.0 | 30.0 | Baked | Off |  |
| FX_LavaFlow_OuterGround_10 | (54.019, -9.8, 151.337) | (1.0, 0.28, 0.06) | Point | 3.0 | 30.0 | Baked | Off |  |
| FX_LavaFlow_OuterGround_11 | (-28.189, -9.8, 164.629) | (1.0, 0.28, 0.06) | Point | 3.0 | 30.0 | Baked | Off |  |
| FX_LavaFlow_OuterGround_12 | (-91.965, -9.8, 154.954) | (1.0, 0.28, 0.06) | Point | 3.0 | 30.0 | Baked | Off |  |
| FX_LavaFlow_OuterGround_13 | (-146.051, -9.8, 68.023) | (1.0, 0.28, 0.06) | Point | 3.0 | 30.0 | Baked | Off |  |

## Light strips and banners (no realtime light)

| Type | Area | Count | Linear colour | Emissive EV | Positions |
|---|---|---|---|---|---|
| BannerSway | Covenant | 20 | (0, 0, 0) | 0.0 | FX_022.json (FX_BannerSway_Covenant_01 .. FX_BannerSway_Covenant_20) |
| BannerSway | Hegemony | 21 | (0, 0, 0) | 0.0 | FX_022.json (FX_BannerSway_Hegemony_01 .. FX_BannerSway_Hegemony_21) |
| BannerSway | Null | 20 | (0, 0, 0) | 0.0 | FX_022.json (FX_BannerSway_Null_01 .. FX_BannerSway_Null_20) |
| StripGlow | Covenant | 33 | (1.0, 0.62, 0.12) | 3.0 | FX_022.json (FX_StripGlow_Covenant_01 .. FX_StripGlow_Covenant_33) |
| StripGlow | Hegemony | 46 | (0.36, 0.62, 1.0) | 3.0 | FX_022.json (FX_StripGlow_Hegemony_01 .. FX_StripGlow_Hegemony_46) |
| StripGlow | Null | 2 | (1.0, 0.1, 0.06) | 3.0 | FX_022.json (FX_StripGlow_Null_01 .. FX_StripGlow_Null_02) |

## Lava, heat haze, embers, smoke (LAVA_022.json)

- 807 crack paths read from the emission maps the floors use (arena floor, heart dais, outer ground; exact to 4-8 cm): each is a polyline in Unity axes with width (m), linear emission colour and HDR intensity. Region totals: {'ArenaFloor': 116, 'HeartDais': 49, 'OuterGround': 642}.
- Heat haze: 291 boxes (16 m cells over the cracks, 3 m tall) = the cheap set; 904 capsules that hug each crack for a finer look.
- 72 ember spawn points (every 3 m on the arena and dais cracks) and 190 smoke spawn points (every 14 m on the outer flows).
- The floor itself already glows from its _Emit map; the paths are for VFX, decals, haze and gameplay (damage zones if wanted).

## Sky (022b)
- The skybox is gone: an unlit aurora sky dome, radius 500 m (`SkyDome_R500`), with the far peaks as geometry inside it (`Outer_Peak_*`, 330-410 m out, 95-190 m tall; `Outer_Crag_*` 215-290 m). Camera far clip at least 600 m.