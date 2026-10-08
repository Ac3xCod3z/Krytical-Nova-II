// The halo of light round a glowing thing (a tempered weapon, an evolved pet). Driven by GlowHaloFeature.
//
// It is drawn on the screen, not on the model: every glowing thing is first drawn into a mask, the mask is flooded
// outward so each pixel learns which glowing pixel is nearest and how far away it is, and the light is laid down
// from that distance. So the halo follows the outline exactly on any mesh (thin blades, hollow shapes, hard-edged
// low-poly models) and falls away evenly, with no bands and no gaps. The model itself is never tinted.
//
// Part of a model can be kept dark (a sword's hilt, a gun's grip, a bow's string). Those parts go into the mask too,
// marked, and are flooded alongside: a pixel whose nearest piece of the model is a dark one gets no light, so the
// glow stays with the blade and does not spill round the grip next to it.
//
// The stronger looks add to the light, in the same pass and in the light's own colour:
//   sparks     small dots with a thin four-point glint that swell and fade, in the light and on the part of the
//              model that glows
//   lightning  thin jagged arcs that crawl round the outline for an instant each
//   flame      tongues that rise from the outline and stream towards the tip
// All three are pinned to the glowing thing (its origin and the way its tip points), so they travel with it, and
// none is drawn where there is no light.
//
// Pass 0 Mask      one glowing renderer: its colour, its halo width in pixels, its depth and which pinned thing it is
// Pass 1 Seed      the mask boiled down to "this texel holds a glowing pixel at ..., a dark one at ..."
// Pass 2 Flood     one jump-flood step: each texel takes the nearest of each kind its neighbours know of
// Pass 3 Composite the light and the sparks, blended over the finished frame after the comic pass (so ink and dots
//                  stay under them)
Shader "Hidden/KryticalNova/GlowHalo"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D(_GlowAttr);       // rgb = colour x brightness, a = halo width in pixels (0 = nothing here; below 0 =
                                    // a part of a glowing thing that does not glow itself and takes no light: a grip)
        TEXTURE2D(_GlowDepth);      // r = eye depth of the glowing thing's surface, dark parts included;
                                    // g = which pinned thing it is (its place in _GlowPins + 1, 0 = a plain glow)
        TEXTURE2D(_GlowSeeds);      // rg = where the nearest glowing pixel is, ba = where the nearest dark one is;
                                    // both as a fraction of the screen (0 = none)
        float4 _GlowAttr_TexelSize;

        float4 _GlowColor;          // Mask: rgb = colour x brightness, as it breathes
        float4 _GlowWidth;          // Mask: x = width in metres, y = least width in pixels, z = most
        float4 _GlowFade;           // Mask: x = distance the halo starts to fade at, y = where it is gone
        float4 _GlowZone;           // Mask: x = 0 all of it glows, 1 only what is inside the zone, 2 none of it;
                                    //       y = 1 when it also has a dark box: what is inside that never glows
        float4x4 _GlowZoneMatrix;   // Mask: world to the thing's glow zone, which is the unit cube about the origin
        float4x4 _GlowDarkMatrix;   // Mask: world to its dark box, likewise
        float4 _GlowFlood;          // x = flood texels per mask pixel (1 or 2), y = this step's jump in flood texels
        float4 _GlowShape;          // Composite: x = softness, y = ink gap in pixels, z = solidity, w = depth fade in metres
        float4 _GlowBorder;         // Composite: x = how soft the light ends beside a dark part, as a share of the halo's width
        float _GlowPin;             // Mask: this thing's place in _GlowPins + 1, or 0 when it carries no sparks, lightning or flame
        #define GLOW_PINS 32        // the most pinned things in one view (GlowHaloFeature.MaxPins)
        float4 _GlowPins[GLOW_PINS];    // Composite: xyz = a thing's own origin in the world (a weapon's grip), w = how bright its sparks are
        float4 _GlowTips[GLOW_PINS];    // Composite: xyz = the way its tip points in the world
        float4 _GlowAuras[GLOW_PINS];   // Composite: x = how strong its lightning is, y = how much of its light is flame, z = its own number (0-1): its sparks and arcs are its own
        float _GlowClock;           // Composite: seconds, on the clock the breath runs by
        float4 _GlowTwinkle;        // Composite: x = the room one spark has in metres, y = beats a second, z = share of spots lit, w = least room in pixels
        float4 _GlowTwinkleLook;    // Composite: x = how white a spark's heart is; as shares of its room: y = its dot, z = its rays' reach, w = their thickness
        float4 _GlowBolt;           // Composite: x = the stretch one arc runs along in metres, y = strikes a second, z = how often one happens, w = its thickness in pixels
        float4 _GlowBoltLook;       // Composite: x = how bright an arc is, y = how far it wanders from the outline (share of the halo's width), z = how white it is
        float4 _GlowFlame;          // Composite: x = a tongue's length along the weapon in metres, y = metres a second towards the tip, z = how much of the light is tongues, w = their lean
        float4 _GlowFlameLook;      // Composite: x = how much brighter the heart of a tongue is

        // Parts of one weapon are never further apart than this (metres): a dark part further than that from the
        // glowing one, in depth, belongs to something else and has no say over this light.
        #define GLOW_SAME_THING 1.5

        float EyeDepth(float rawDepth)
        {
            return IsPerspectiveProjection() ? LinearEyeDepth(rawDepth, _ZBufferParams) : LinearDepthToEyeDepth(rawDepth);
        }

        struct FullVaryings
        {
            float4 positionCS : SV_POSITION;
        };

        FullVaryings FullVert(uint vertexID : SV_VertexID)
        {
            FullVaryings output;
            output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Mask"
            ZTest Always ZWrite Off Cull Back Blend Off
            // A blade a fraction of a pixel wide still lights the pixels it crosses, so its halo does not flicker far away.
            Conservative True

            HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct MaskVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            struct MaskOutput
            {
                half4 attr : SV_Target0;
                float2 depth : SV_Target1;
            };

            MaskVaryings MaskVert(Attributes input)
            {
                MaskVaryings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            MaskOutput MaskFrag(MaskVaryings input)
            {
                float eye = EyeDepth(input.positionCS.z);
                // Only what the camera can see of it glows: the part of a weapon behind its owner's back, or sunk into
                // their hip, casts no halo. The allowance is for depth rounding only (5 mm, a little more far away): a
                // looser one lets a hidden blade light the body in front of it.
                float sceneEye = EyeDepth(LoadSceneDepth(uint2(input.positionCS.xy)));
                clip(sceneEye - (eye * 0.998 - 0.005));

                float fade = 1.0 - smoothstep(_GlowFade.x, max(_GlowFade.y, _GlowFade.x + 0.01), eye);
                clip(fade - 0.001);

                // Only part of some things glows: the blade of a sword and not its hilt, the head of an axe and not
                // the shaft that runs through it, a gun but not its grip, a bow but not its string. The rest is
                // marked: it does not glow, no light is laid over it, and it keeps the light of the part beside it
                // off the air round it.
                bool unlit = _GlowZone.x > 1.5;
                if (!unlit && _GlowZone.x > 0.5)
                {
                    float3 zone = mul(_GlowZoneMatrix, float4(input.positionWS, 1.0)).xyz;
                    unlit = any(abs(zone) > 0.5);
                }
                if (!unlit && _GlowZone.y > 0.5)
                {
                    float3 zone = mul(_GlowDarkMatrix, float4(input.positionWS, 1.0)).xyz;
                    unlit = all(abs(zone) <= 0.5);
                }
                if (unlit)
                {
                    MaskOutput dark;
                    dark.attr = half4(0.0, 0.0, 0.0, -1.0);
                    dark.depth = float2(eye, 0.0);
                    return dark;
                }

                // Metres to pixels at this depth; never thinner than a few pixels, so it still reads far away.
                float pixelsPerMetre = unity_CameraProjection._m11 * 0.5 * _ScaledScreenParams.y;
                if (IsPerspectiveProjection()) pixelsPerMetre /= max(eye, 0.01);

                MaskOutput output;
                output.attr = half4(_GlowColor.rgb * fade, clamp(_GlowWidth.x * pixelsPerMetre, _GlowWidth.y, _GlowWidth.z));
                output.depth = float2(eye, _GlowPin);
                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Seed"
            ZTest Always ZWrite Off Cull Off Blend Off

            HLSLPROGRAM
            #pragma vertex FullVert
            #pragma fragment SeedFrag

            float4 SeedFrag(FullVaryings input) : SV_Target
            {
                // One flood texel stands for a block of mask pixels: any glowing pixel in it will do, and any dark
                // one, so nothing thin is lost.
                int scale = (int)_GlowFlood.x;
                int2 first = int2(input.positionCS.xy) * scale;
                float4 seeds = 0.0;
                for (int y = 0; y < scale; y++)
                {
                    for (int x = 0; x < scale; x++)
                    {
                        int2 pixel = first + int2(x, y);
                        float kind = LOAD_TEXTURE2D(_GlowAttr, pixel).a;
                        float2 at = (pixel + 0.5) * _GlowAttr_TexelSize.xy;
                        if (kind > 0.0) seeds.rg = at;
                        else if (kind < 0.0) seeds.ba = at;
                    }
                }
                return seeds;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Flood"
            ZTest Always ZWrite Off Cull Off Blend Off

            HLSLPROGRAM
            #pragma vertex FullVert
            #pragma fragment FloodFrag

            float4 FloodFrag(FullVaryings input) : SV_Target
            {
                int2 texel = int2(input.positionCS.xy);
                int jump = (int)_GlowFlood.y;
                float2 here = input.positionCS.xy * _GlowFlood.x;   // in mask pixels
                float2 size = _GlowAttr_TexelSize.zw / _GlowFlood.x;

                float2 nearest = 1e20;      // x = to the nearest glowing pixel, y = to the nearest dark one
                float4 seeds = 0.0;
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        int2 at = texel + int2(x, y) * jump;
                        if (at.x < 0 || at.y < 0 || at.x >= size.x || at.y >= size.y) continue;
                        float4 candidate = LOAD_TEXTURE2D(_GlowSeeds, at);
                        if (candidate.x + candidate.y > 0.0)
                        {
                            float2 away = candidate.rg * _GlowAttr_TexelSize.zw - here;
                            float distance = dot(away, away);
                            if (distance < nearest.x)
                            {
                                nearest.x = distance;
                                seeds.rg = candidate.rg;
                            }
                        }
                        if (candidate.z + candidate.w > 0.0)
                        {
                            float2 away = candidate.ba * _GlowAttr_TexelSize.zw - here;
                            float distance = dot(away, away);
                            if (distance < nearest.y)
                            {
                                nearest.y = distance;
                                seeds.ba = candidate.ba;
                            }
                        }
                    }
                }
                return seeds;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            ZTest Always ZWrite Off Cull Off
            // Light added to the frame, with a little of what is behind it taken away first (solidity), so the colour
            // still reads against a bright sky or a white wall instead of burning out to white.
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex FullVert
            #pragma fragment CompositeFrag

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // Smooth noise, 0 to 1.
            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(float3(cell, 0.37));
                float b = Hash(float3(cell + float2(1.0, 0.0), 0.37));
                float c = Hash(float3(cell + float2(0.0, 1.0), 0.37));
                float d = Hash(float3(cell + float2(1.0, 1.0), 0.37));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Where a pinned thing is on screen: its origin, the way its tip points, and the size of a metre there.
            struct Pin
            {
                float2 origin;          // pixels
                float2 towardsTip;      // unit, on screen
                float pixelsPerMetre;
                float sparks;           // how bright its sparks are
                float lightning;        // how strong its lightning is
                float flame;            // how much of its light is flame
                float seed;             // its own number: no two things spark or strike alike
                bool seen;
            };

            float2 PixelOf(float4 clip)
            {
                float2 at = (clip.xy / clip.w * 0.5 + 0.5) * _GlowAttr_TexelSize.zw;
                #if UNITY_UV_STARTS_AT_TOP
                at.y = _GlowAttr_TexelSize.w - at.y;
                #endif
                return at;
            }

            Pin PinOf(float which)
            {
                int index = clamp((int)(which + 0.5) - 1, 0, GLOW_PINS - 1);
                Pin pin;
                float4 clip = TransformWorldToHClip(_GlowPins[index].xyz);
                pin.seen = clip.w > 0.01;
                pin.origin = pin.seen ? PixelOf(clip) : 0.0;
                pin.pixelsPerMetre = unity_CameraProjection._m11 * 0.5 * _GlowAttr_TexelSize.w;
                if (IsPerspectiveProjection()) pin.pixelsPerMetre /= max(clip.w, 0.01);
                // The way the tip points, on screen. End-on to the camera there is no such way: any will do.
                pin.towardsTip = float2(1.0, 0.0);
                float4 ahead = TransformWorldToHClip(_GlowPins[index].xyz + _GlowTips[index].xyz * 0.5);
                if (pin.seen && ahead.w > 0.01)
                {
                    float2 way = PixelOf(ahead) - pin.origin;
                    float far = dot(way, way);
                    if (far > 1.0) pin.towardsTip = way * rsqrt(far);
                }
                pin.sparks = _GlowPins[index].w;
                pin.lightning = _GlowAuras[index].x;
                pin.flame = _GlowAuras[index].y;
                pin.seed = _GlowAuras[index].z * 97.0;
                return pin;
            }

            // Sparks: small dots with a thin four-point glint. The glowing thing is laid out in spots pinned to it (so
            // the sparks travel with it) and sized in its own metres (so they shrink with distance); each spot keeps
            // its own time, and now and then one lights: a dot swells, throws its rays, and fades, all in one beat.
            // Returns x = the whole spark (dot and rays), y = its heart.
            half2 Twinkle(Pin pin, float2 here)
            {
                if (!pin.seen || pin.sparks <= 0.0) return 0.0;
                float size = max(_GlowTwinkle.x * pin.pixelsPerMetre, _GlowTwinkle.w);
                float2 at = (here - pin.origin) / size;
                float2 spot = floor(at);
                float beat = _GlowClock * _GlowTwinkle.y + Hash(float3(spot, pin.seed)) * 7.0;
                float turn = floor(beat);
                if (Hash(float3(spot + 31.0, turn + pin.seed * 13.0)) < 1.0 - _GlowTwinkle.z) return 0.0;
                float swell = sin(frac(beat) * PI);
                swell *= swell;
                // Somewhere about the middle of its spot, a different place every time: no grid shows.
                float2 middle = 0.5 + (float2(Hash(float3(spot, turn + 3.0)), Hash(float3(spot, turn + 5.0))) - 0.5) * 0.24;
                float2 from = (frac(at) - middle) * 2.0;    // 0 at the spark, 1 at the edge of its room
                // Far away the room is a few pixels: the dot and the rays keep a pixel's worth, or they would be lost.
                float pixel = 2.0 / size;
                float dot = max(_GlowTwinkleLook.y, pixel * 1.3);
                float heart = 1.0 - smoothstep(dot * 0.35, dot, length(from));
                float2 arm = abs(from);
                float reach = max(_GlowTwinkleLook.z * swell, 0.001);
                float thin = max(_GlowTwinkleLook.w, pixel * 0.75);
                float rays = saturate(1.0 - arm.y / thin) * saturate(1.0 - arm.x / reach)
                           + saturate(1.0 - arm.x / thin) * saturate(1.0 - arm.y / reach);
                return half2(saturate(heart + rays * 0.75), heart) * (swell * pin.sparks);
            }

            // A spark's colour: the glow's own, paled so it reads against the glow, with a white heart.
            half3 Glint(half3 glowColor, half2 spark)
            {
                half brightest = max(glowColor.r, max(glowColor.g, glowColor.b));
                half3 pale = lerp(glowColor, brightest, _GlowTwinkleLook.x);
                return pale * spark.x + (brightest - pale) * spark.y;
            }

            // An arc's colour: the glow's own, pulled towards white.
            half3 Spark(half3 glowColor)
            {
                half brightest = max(glowColor.r, max(glowColor.g, glowColor.b));
                return lerp(glowColor, brightest, _GlowBoltLook.z);
            }

            // Lightning: now and then a stretch of the outline is struck, and a thin jagged arc stands a little way
            // off it for an instant, flickers and is gone. along and across are metres from the thing's origin
            // towards its tip and to its side; distancePx is how far this pixel is from the outline.
            half Bolts(float along, float across, float distancePx, float widthPx, float own)
            {
                half total = 0.0;
                // Each side of the weapon has its own weather.
                float side = across >= 0.0 ? 0.0 : 5.0;
                // The weapon is cut into stretches, twice over and half a stretch apart; each stretch keeps its own
                // time, so the arcs come and go here and there instead of all at once.
                for (int k = 0; k < 2; k++)
                {
                    float seed = own + k * 3.1 + side;
                    float place = along / _GlowBolt.x + k * 0.5;
                    float stretch = floor(place);
                    float cycle = _GlowClock * _GlowBolt.y + Hash(float3(stretch, seed, 2.0)) * 11.0;
                    float strike = floor(cycle);
                    float phase = frac(cycle);
                    if (Hash(float3(stretch, strike, seed + 4.0)) > _GlowBolt.z) continue;
                    // This strike's arc takes up part of its stretch, a different part every time.
                    float run = (frac(place) - Hash(float3(strike, stretch, seed + 5.0)) * 0.35) / 0.65;
                    if (run < 0.0 || run > 1.0) continue;
                    // Its path: knots at random distances from the outline, joined by straight lines.
                    float knots = run * 6.0;
                    float knot = floor(knots);
                    float from = Hash(float3(knot, strike + stretch * 13.0, seed));
                    float to = Hash(float3(knot + 1.0, strike + stretch * 13.0, seed));
                    float path = widthPx * (0.16 + _GlowBoltLook.y * lerp(from, to, frac(knots)));
                    float off = abs(distancePx - path);
                    float arc = saturate(1.0 - off / _GlowBolt.w) + 0.3 * saturate(1.0 - off / (_GlowBolt.w * 4.0));
                    float ends = smoothstep(0.0, 0.14, run) * (1.0 - smoothstep(0.86, 1.0, run));
                    // Struck, then two quick re-strikes dying away, all inside the first half of the beat.
                    float flash = exp(-phase * 7.0) * (0.5 + 0.5 * step(0.45, frac(phase * 8.0 + 0.3))) * step(phase, 0.5);
                    total += arc * ends * flash;
                }
                return total;
            }

            // Flame: how far the light reaches here (x, as a share of the halo's width) and how much of a tongue this
            // is (y), for tongues that rise from the outline and stream towards the tip. outward is metres from the
            // outline.
            float2 Flame(float along, float across, float outward)
            {
                float size = max(_GlowFlame.x, 0.001);
                // Further from the outline the same tongue is nearer the tip: it leans the way it flows.
                float2 at = float2((along - outward * _GlowFlame.w - _GlowClock * _GlowFlame.y) / size, across / size * 0.9);
                float n = Noise(at) * 0.65 + Noise(at * 2.3 + 11.0) * 0.35;
                float tongue = smoothstep(0.28, 0.78, n);
                float reach = 1.0 - _GlowFlame.z * 0.5 * (1.0 - tongue);
                return float2(reach, tongue);
            }

            half4 CompositeFrag(FullVaryings input) : SV_Target
            {
                int2 pixel = int2(input.positionCS.xy);
                int2 texel = _GlowFlood.x > 1.5 ? pixel >> 1 : pixel;

                float4 seeds = LOAD_TEXTURE2D(_GlowSeeds, texel);
                if (seeds.x + seeds.y <= 0.0) return 0.0;
                float2 here = input.positionCS.xy;

                // On the glowing thing itself no light is laid: it keeps its own colours and the light is all round
                // it. The part of it that glows wears the sparks; hilt, grip and string wear nothing.
                half4 own = LOAD_TEXTURE2D(_GlowAttr, pixel);
                if (own.a != 0.0)
                {
                    float wears = LOAD_TEXTURE2D(_GlowDepth, pixel).g;
                    if (own.a < 0.0 || wears < 0.5) return 0.0;
                    half3 spark = Glint(own.rgb, Twinkle(PinOf(wears), here));
                    return half4(spark, saturate(max(spark.r, max(spark.g, spark.b))) * _GlowShape.z);
                }

                // The flood ran on coarser texels than the screen: take the nearest of what this texel and the four
                // beside it found, so the halo is not blocky.
                float2 seed = seeds.rg;
                float2 away = seed * _GlowAttr_TexelSize.zw - here;
                float nearest = dot(away, away);
                float2 darkSeed = seeds.ba;
                float nearestDark = 1e20;
                if (darkSeed.x + darkSeed.y > 0.0)
                {
                    away = darkSeed * _GlowAttr_TexelSize.zw - here;
                    nearestDark = dot(away, away);
                }
                const int2 beside[4] = { int2(1, 0), int2(-1, 0), int2(0, 1), int2(0, -1) };
                for (int i = 0; i < 4; i++)
                {
                    float4 candidate = LOAD_TEXTURE2D(_GlowSeeds, texel + beside[i]);
                    if (candidate.x + candidate.y > 0.0)
                    {
                        away = candidate.rg * _GlowAttr_TexelSize.zw - here;
                        float distance = dot(away, away);
                        if (distance < nearest)
                        {
                            nearest = distance;
                            seed = candidate.rg;
                        }
                    }
                    if (candidate.z + candidate.w > 0.0)
                    {
                        away = candidate.ba * _GlowAttr_TexelSize.zw - here;
                        float distance = dot(away, away);
                        if (distance < nearestDark)
                        {
                            nearestDark = distance;
                            darkSeed = candidate.ba;
                        }
                    }
                }
                // The flood kept one glowing pixel for each block of the mask, not always the nearest in it: step from
                // it towards this pixel while the mask still glows there. Without this a slanted edge's light is
                // striped, a pixel's worth brighter and dimmer by turns.
                int2 seedPixel = int2(seed * _GlowAttr_TexelSize.zw);
                for (int hop = 0; hop < 2; hop++)
                {
                    int2 toward = int2(sign(float2(pixel - seedPixel)));
                    if (toward.x == 0 && toward.y == 0) break;
                    int2 best = seedPixel;
                    float2 gap = pixel - seedPixel;
                    float least = dot(gap, gap);
                    const int2 steps[3] = { int2(1, 1), int2(1, 0), int2(0, 1) };
                    for (int s = 0; s < 3; s++)
                    {
                        int2 at = seedPixel + toward * steps[s];
                        gap = pixel - at;
                        float distance = dot(gap, gap);
                        if (distance < least && LOAD_TEXTURE2D(_GlowAttr, at).a > 0.0)
                        {
                            least = distance;
                            best = at;
                        }
                    }
                    if (best.x == seedPixel.x && best.y == seedPixel.y) break;
                    seedPixel = best;
                    nearest = least;
                }
                float distancePx = sqrt(nearest);
                float darkPx = sqrt(nearestDark);

                // Right at the outline the exact answer matters (the ink line lives there): look at the pixels beside this one.
                if (min(distancePx, darkPx) < 2.5)
                {
                    float glowBeside = 2.0, darkBeside = 2.0;
                    const int2 around[8] = { int2(1, 0), int2(-1, 0), int2(0, 1), int2(0, -1), int2(1, 1), int2(-1, 1), int2(1, -1), int2(-1, -1) };
                    for (int n = 0; n < 8; n++)
                    {
                        float kind = LOAD_TEXTURE2D(_GlowAttr, pixel + around[n]).a;
                        float stride = n < 4 ? 1.0 : 1.4142;
                        if (kind > 0.0) glowBeside = min(glowBeside, stride);
                        else if (kind < 0.0) darkBeside = min(darkBeside, stride);
                    }
                    if (distancePx < 2.5) distancePx = glowBeside < 2.0 ? glowBeside : max(distancePx, 2.0);
                    if (darkPx < 2.5) darkPx = darkBeside < 2.0 ? darkBeside : max(darkPx, 2.0);
                }

                half4 attr = LOAD_TEXTURE2D(_GlowAttr, seedPixel);
                float2 seedDepth = LOAD_TEXTURE2D(_GlowDepth, seedPixel).rg;
                float seedEye = seedDepth.r;
                float softness = _GlowShape.x;
                float inkGap = _GlowShape.y;
                float out01 = saturate((distancePx - inkGap) / max(attr.a, 0.5));

                // What may be lit at all. The air belongs to whichever piece of the model is nearest: beside the
                // grip, the hilt or the string there is no light, and it ends softly where the two meet.
                half lit = 1.0;
                if (nearestDark < 1e19)
                {
                    float darkEye = LOAD_TEXTURE2D(_GlowDepth, int2(darkSeed * _GlowAttr_TexelSize.zw)).r;
                    if (abs(darkEye - seedEye) < GLOW_SAME_THING)
                    {
                        float border = max(attr.a * _GlowBorder.x, 1.0);
                        lit *= smoothstep(0.0, 1.0, (darkPx - distancePx) / border + 0.5);
                    }
                }
                // Whatever stands in front of the glowing thing hides its light; what is level with it or behind is lit.
                float sceneEye = EyeDepth(LoadSceneDepth(pixel));
                lit *= saturate((sceneEye - seedEye) / max(_GlowShape.w, 0.001) + 1.0);
                // The model's own ink outline sits on its edge: the light starts just outside it and leaves it black.
                lit *= smoothstep(inkGap * 0.5, inkGap * 1.5 + 0.001, distancePx);

                half glow = pow(1.0 - out01, softness);
                half3 extra = 0.0;
                if (seedDepth.g > 0.5)
                {
                    Pin pin = PinOf(seedDepth.g);
                    if (pin.seen)
                    {
                        float2 offset = (here - pin.origin) / pin.pixelsPerMetre;
                        float along = dot(offset, pin.towardsTip);
                        float across = dot(offset, float2(-pin.towardsTip.y, pin.towardsTip.x));
                        if (pin.flame > 0.0)
                        {
                            float2 flame = Flame(along, across, max(distancePx - inkGap, 0.0) / pin.pixelsPerMetre);
                            half tongues = pow(saturate(1.0 - out01 / max(flame.x, 0.05)), softness) * (1.0 + _GlowFlameLook.x * flame.y);
                            glow = lerp(glow, tongues, saturate(pin.flame));
                        }
                        if (pin.lightning > 0.0)
                            extra += Spark(attr.rgb) * Bolts(along, across, distancePx, attr.a, pin.seed) * _GlowBoltLook.x * pin.lightning;
                        // Sparks in the light itself: as bright as on the blade near it, and gone well before the
                        // light gives out, so none ever hangs in the dark beside the glow.
                        extra += Glint(attr.rgb, Twinkle(pin, here)) * smoothstep(0.12, 0.45, glow);
                    }
                }

                half3 light = (attr.rgb * glow + extra) * lit;
                half strength = saturate(max(light.r, max(light.g, light.b)));
                return half4(light, strength * _GlowShape.z);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
