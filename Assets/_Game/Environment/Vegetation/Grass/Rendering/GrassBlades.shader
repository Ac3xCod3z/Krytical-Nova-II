// Krytical Nova stylized grass (BotW-style), drawn with Graphics.RenderMeshIndirect by GrassRenderer.
// Per-instance data comes from GrassGenerate.compute; per-type look (colours, motion) from a property block.
//  Vertex:   height-masked bending (root planted) = layered wind (rolling field waves + local gust noise +
//            blade flutter) + player/NPC push (_KN_Interactors) + random per-instance size/yaw; blades
//            rooted in a kept lawn are held inside its rectangle, which gives the lawn its trimmed edge.
//  Fragment: root→mid→tip colour gradient × per-clump variation × field patches × wetness × fake root AO,
//            lit with the comic cel band + Ben-Day dots using upward-biased (stylized) normals.
//  Motion:   a MotionVectors pass says how far each blade moved since the last frame (the same vertex maths, run for
//            last frame's wind), which temporal anti-aliasing and the upscalers (DLSS, FSR 2) need to keep swaying
//            grass sharp. It is only drawn while one of those is on.
Shader "KryticalNova/Grass Blades"
{
    Properties { }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Assets/_Game/Environment/Vegetation/Shaders/KNWind.hlsl"
    #include "GrassCommon.hlsl"

    StructuredBuffer<GrassInstance> _Instances;
    uint   _InstanceOffset;     // this draw's region in the shared instance buffer
    uint   _InstanceCapacity;
    float4 _RootColor;
    float4 _MidColor;
    float4 _TipColor;
    float4 _PatchTint;      // rgb, a strength
    float4 _WetTint;        // rgb, a strength
    float4 _GrassParams;    // x mid point, y brightness variation, z root occlusion, w normal-up
    float4 _GrassMotion;    // x wind strength, y flutter, z player bend (0 = takes no part in interaction), w mesh height
    int    _KN_GrassDebug;
    // The trample map (GrassTrampleMap): where grass round the camera has been pushed, and which way.
    TEXTURE2D(_KN_GrassTrample);
    SAMPLER(sampler_KN_GrassTrample);
    float4 _KN_GrassTrampleRect;    // xy world XZ of its corner, z 1 / its width in metres, w 1 while there is a map

    struct GrassVertexOut
    {
        float3 positionWS;
        float3 normalWS;
        float  height01;
        float  rand;
        float  moisture;
        float3 rootWS;
        uint   debug;
    };

    // The blade at a moment of the wind: the time, the summed wind strength and how far the gust cells had drifted.
    GrassVertexOut GrassVertexAt(float3 positionOS, float3 normalOS, uint instanceID, float t, float phase, float2 drift)
    {
        GrassVertexOut o = (GrassVertexOut)0;
        GrassInstance inst = _Instances[_InstanceOffset + min(instanceID, _InstanceCapacity - 1)];
        float3 root = inst.posRot.xyz;
        float scale = inst.size.x;
        float widthMul = inst.size.y;
        float rand = inst.size.z;

        float h01 = saturate(positionOS.y / max(_GrassMotion.w, 0.001));
        float s, c;
        sincos(inst.posRot.w, s, c);
        float3 local = float3(positionOS.x * scale * widthMul, positionOS.y * scale, positionOS.z * scale * widthMul);
        float3 rotated = float3(c * local.x + s * local.z, local.y, -s * local.x + c * local.z);
        float3 nrm = float3(c * normalOS.x + s * normalOS.z, normalOS.y, -s * normalOS.x + c * normalOS.z);
        float heightWS = rotated.y;
        float3 pos = root + rotated;

        // ---- Wind: three layers so neighbours never move in lockstep.
        float2 dir = KNWindDir();
        float2 side = float2(-dir.y, dir.x);
        float strength = KNWindStrength();
        float gust = KNWindGust();
        float bend = h01 * h01; // root planted, tip moves most
        // Every wave runs faster in more wind. That part of its phase comes summed up from the CPU (KNWind.hlsl):
        // time x strength would jump with every gust.
        // Large: slow waves rolling across the whole field along the wind.
        float large = sin(dot(root.xz, dir) * 0.09 - (0.8 * t + 1.8 * phase)) * 0.5 + 0.5;
        // Medium: local gust cells drifting downwind.
        float2 q = root.xz * 0.21 - drift;
        float medium = sin(q.x + sin(q.y * 1.3)) * sin(q.y * 0.9 - sin(q.x * 0.7));
        // Small: per-blade flutter (each vertex column gets its own phase).
        float small = sin(5.0 * t + 9.0 * phase + rand * 37.0 + positionOS.x * 13.0 + positionOS.z * 11.0);

        float lean = strength * (0.3 + 0.5 * large + 0.25 * gust) + medium * 0.12 * (0.25 + strength);
        float idle = sin(t * 1.3 + rand * 6.2831) * 0.05;
        float2 offset = dir * lean * _GrassMotion.x + side * (medium * 0.08 + idle) * _GrassMotion.x;
        offset += side * small * _GrassMotion.y * 0.06 * (0.3 + strength);
        pos.xz += offset * bend * heightWS;

        // ---- Interaction: push away from players / NPCs and flatten a little.
        float2 push = 0.0;
        float flatten = 0.0;
        // With a trample map every blade reads one texel of it: who pressed the ground under it and which way,
        // this frame or a moment ago. Without one (the Scene view outside Play) it asks the nearest characters.
        bool mapped = _KN_GrassTrampleRect.w > 0.5;
        if (mapped && _GrassMotion.z > 0.0)
        {
            float2 at = (root.xz - _KN_GrassTrampleRect.xy) * _KN_GrassTrampleRect.z;
            float2 toEdge = min(at, 1.0 - at);
            if (min(toEdge.x, toEdge.y) > 0.0)
            {
                float4 mark = SAMPLE_TEXTURE2D_LOD(_KN_GrassTrample, sampler_KN_GrassTrample, at, 0);
                // Nothing bends at the very rim of the map, so no line shows where it ends; and a blade well above
                // or below whoever pressed the ground there (a bridge, a floor underneath) is left alone.
                float keep = saturate(min(toEdge.x, toEdge.y) * 12.0) * step(abs(root.y - mark.a), 1.5);
                push = mark.rg * keep;
                flatten = mark.b * keep;
            }
        }
        int interactors = !mapped && _GrassMotion.z > 0.0 ? _KN_InteractorCount : 0; // filler tufts skip the loop
        [loop] for (int i = 0; i < interactors; i++)
        {
            float4 it = _KN_Interactors[i];
            float2 d = root.xz - it.xz;
            float dist = length(d);
            float f = saturate(1.0 - dist / max(it.w, 0.01)) * step(abs(root.y - it.y), 1.5);
            f *= f;
            push += (dist > 0.001 ? d / dist : float2(1.0, 0.0)) * f;
            flatten = max(flatten, f);
        }
        pos.xz += push * _GrassMotion.z * bend * heightWS * 1.2;
        pos.y -= flatten * 0.45 * _GrassMotion.z * bend * heightWS;

        // Keep blade length roughly constant: leaning lowers the tip.
        float2 moved = pos.xz - (root.xz + rotated.xz);
        float lenSq = dot(moved, moved);
        pos.y -= min(lenSq / max(heightWS * 2.0, 0.05), heightWS * 0.6);

        // Kept lawns end in a straight cut: nothing leans, sways or is pushed across the edge.
        int lawn;
        KNLawnDistance(root.xz, lawn);
        if (lawn >= 0) pos.xz = clamp(pos.xz, _KN_LawnRects[lawn].xy, _KN_LawnRects[lawn].zw);

        o.positionWS = pos;
        o.normalWS = normalize(lerp(nrm, float3(0.0, 1.0, 0.0), _GrassParams.w));
        o.height01 = h01;
        o.rand = rand;
        o.moisture = (inst.packed >> 24) / 255.0;
        o.rootWS = root;
        o.debug = inst.packed & 0x00FFFFFFu;
        return o;
    }

    GrassVertexOut GrassVertex(float3 positionOS, float3 normalOS, uint instanceID)
    {
        return GrassVertexAt(positionOS, normalOS, instanceID, _Time.y, KNWindPhase(), KNWindDrift(_Time.y));
    }

    // Where the same vertex was one frame ago: last frame's time and wind sums (what pushes it is taken as it is now).
    float3 GrassPreviousPositionWS(float3 positionOS, float3 normalOS, uint instanceID)
    {
        float t = _Time.y - unity_DeltaTime.x;
        float2 drift = _KN_WindPhase.w > 0.5 ? _KN_WindDrift.zw : KNWindDir() * (t * 1.2);
        return GrassVertexAt(positionOS, normalOS, instanceID, t, _KN_WindPhase.y, drift).positionWS;
    }

    float3 GrassAlbedo(float height01, float rand, float moisture, float3 rootWS)
    {
        float mid = _GrassParams.x;
        float3 col = height01 < mid
            ? lerp(_RootColor.rgb, _MidColor.rgb, height01 / mid)
            : lerp(_MidColor.rgb, _TipColor.rgb, (height01 - mid) / (1.0 - mid));
        // Broad field patches (dry/lush), per-clump brightness, wet ground near water.
        float2 p = rootWS.xz;
        float patch = sin(p.x * 0.07 + sin(p.y * 0.05) * 2.1) * sin(p.y * 0.083 + sin(p.x * 0.047) * 1.7) * 0.5 + 0.5;
        col = lerp(col, col * _PatchTint.rgb, _PatchTint.a * patch);
        col = lerp(col, col * _WetTint.rgb, _WetTint.a * moisture);
        col *= 1.0 + (rand - 0.5) * _GrassParams.y;
        // Fake AO grounds the blades in the terrain.
        col *= lerp(_GrassParams.z, 1.0, pow(height01, 0.6));
        return col;
    }

    float3 UnpackDebug(uint d) { return float3((d >> 16) & 255u, (d >> 8) & 255u, d & 255u) / 255.0; }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/_Game/Rendering/Shaders/Comic/ComicHalftone.hlsl"

            half _ComicShadowThreshold;
            half _ComicShadowSoftness;
            half _ComicShadowBrightness;
            half _ComicDotStart;
            half _ComicPostShadows;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; uint instanceID : SV_InstanceID; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half3  albedo     : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                nointerpolation uint debug : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                GrassVertexOut g = GrassVertex(input.positionOS.xyz, input.normalOS, input.instanceID);
                Varyings o;
                o.positionWS = g.positionWS;
                o.positionCS = TransformWorldToHClip(g.positionWS);
                o.normalWS = g.normalWS;
                o.albedo = GrassAlbedo(g.height01, g.rand, g.moisture, g.rootWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.debug = _KN_GrassDebug > 0 ? g.debug : 0u;
                return o;
            }

            half ToonBand(half intensity)
            {
                half soft = max(_ComicShadowSoftness, 0.001);
                return smoothstep(_ComicShadowThreshold - soft, _ComicShadowThreshold + soft, intensity);
            }

            half4 Frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                if (input.debug != 0u) return half4(UnpackDebug(input.debug), 1);

                half3 normalWS = normalize(input.normalWS);
                normalWS = isFrontFace ? normalWS : normalize(lerp(-normalWS, normalWS, _GrassParams.w));

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half shadow = _ComicPostShadows > 0.5 ? 1.0 : mainLight.shadowAttenuation;
                half intensity = saturate(dot(normalWS, mainLight.direction)) * shadow;
                half3 lighting = mainLight.color * lerp(_ComicShadowBrightness, 1.0, ToonBand(intensity));
                half3 ambient = SampleSH(half3(0, 1, 0));

                half3 color = input.albedo * (lighting + ambient);
                half dotStart = max(_ComicDotStart, 0.001);
                color = ComicApplyDots(color, input.positionCS.xy, saturate((dotStart - saturate(intensity)) / dotStart));
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; uint instanceID : SV_InstanceID; };
            float4 Vert(Attributes input) : SV_POSITION
            {
                return TransformWorldToHClip(GrassVertex(input.positionOS.xyz, input.normalOS, input.instanceID).positionWS);
            }
            half Frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        // How far each blade moved on screen since the last frame, for temporal anti-aliasing and the upscalers.
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; uint instanceID : SV_InstanceID; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 nowCS      : TEXCOORD0;
                float4 beforeCS   : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 now = GrassVertex(input.positionOS.xyz, input.normalOS, input.instanceID).positionWS;
                float3 before = GrassPreviousPositionWS(input.positionOS.xyz, input.normalOS, input.instanceID);
                o.positionCS = TransformWorldToHClip(now);
                // Without the camera's sub-pixel jitter: the vectors are of the scene's own motion.
                o.nowCS = mul(_NonJitteredViewProjMatrix, float4(now, 1.0));
                o.beforeCS = mul(_PrevViewProjMatrix, float4(before, 1.0));
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(CalcNdcMotionVectorFromCsPositions(input.nowCS, input.beforeCS), 0, 0);
            }
            ENDHLSL
        }

        // Needed so the ComicPost outline pass sees grass depth / (stylized, mostly-up) normals.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; uint instanceID : SV_InstanceID; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                GrassVertexOut g = GrassVertex(input.positionOS.xyz, input.normalOS, input.instanceID);
                Varyings o;
                o.positionCS = TransformWorldToHClip(g.positionWS);
                o.normalWS = g.normalWS;
                return o;
            }

            void Frag(Varyings input, bool isFrontFace : SV_IsFrontFace
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                float3 normalWS = normalize(input.normalWS);
                normalWS = isFrontFace ? normalWS : normalize(lerp(-normalWS, normalWS, _GrassParams.w));
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = saturate(PackNormalOctQuadEncode(normalWS) * 0.5 + 0.5);
                outNormalWS = half4(PackFloat2To888(oct), 0.0);
            #else
                outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
            #endif
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
}
