Shader "VolumetricLights/Simple"
{
	Properties
	{
		[HideInInspector] _MainTex("Main Texture", 2D) = "white" {}
		[HideInInspector] _Color("Color", Color) = (1,1,1)
		[HideInInspector] _Density("Density", Float) = 1.0
		[HideInInspector] _BoundsCenter("Bounds Center", Vector) = (0,0,0)
		[HideInInspector] _BoundsExtents("Bounds Size", Vector) = (0,0,0)
		[HideInInspector] _MeshBoundsCenter("Non transformed Bounds Center", Vector) = (0,0,0)
		[HideInInspector] _MeshBoundsExtents("Non transformed Bounds Size", Vector) = (0,0,0)
		[HideInInspector] _ConeTipData("Cone Tip Data", Vector) = (0,0,0,0.1)
		[HideInInspector] _ExtraGeoData("Extra Geometry Data", Vector) = (1.0, 0, 0)
        [HideInInspector] _Border("Border", Float) = 0.1
        [HideInInspector] _DistanceFallOff("Length Falloff", Float) = 0
        [HideInInspector] _NearClipDistance("Near Clip Distance", Float) = 0
        [HideInInspector] _FallOff("FallOff Physical", Vector) = (1.0, 2.0, 1.0)
        [HideInInspector] _ConeAxis("Cone Axis", Vector) = (0,0,0,0.5)
        [HideInInspector] _AreaExtents("Area Extents", Vector) = (0,0,0,1)
        [HideInInspector] _LightColor("Light Color", Color) = (1,1,1)
        [HideInInspector] _ToLightDir("To Light Dir", Vector) = (1,1,1,0)
        [HideInInspector] _BlendSrc("Blend Src", Int) = 1
        [HideInInspector] _BlendDest("Blend Dest", Int) = 1
		[HideInInspector] _BlendOp("Blend Op", Int) = 0
		[HideInInspector] _Cookie2D("Cookie (2D)", 2D) = "black" {}
		[HideInInspector] _Cookie2D_SS("Cookie (Scale and Speed)", Vector) = (1,1,0,0)
		[HideInInspector] _Cookie2D_Offset("Cookie (Offset)", Vector) = (0,0,0,0)
        [HideInInspector] _ShadowIntensity("Shadow Intensity", Vector) = (0,1,0,0)
		[HideInInspector] _ShadowCubemap("Shadow Texture (Cubemap)", Any) = "" {}
		[HideInInspector] _FlipDepthTexture("Flip Depth Texture", Int) = 0
		[HideInInspector] _DirectLightData("Direct Light Data", Vector) = (1, 8, 4)
	}
		SubShader
		{
			Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" "DisableBatching" = "True" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
			BlendOp [_BlendOp]
			Blend [_BlendSrc] [_BlendDest]
			ZTest Always
			Cull Front
			ZWrite Off

			Pass
			{
				Name "Volumetric Light Forward Pass"
				Tags { "LightMode" = "UniversalForward" }
				HLSLPROGRAM
				#pragma prefer_hlslcc gles
				#pragma exclude_renderers d3d11_9x
				#pragma target 3.0
				#pragma vertex vert
				#pragma fragment frag
				#pragma multi_compile _ VF2_DEPTH_PREPASS  // reuses same keyword than in Volumetric Fog & Mist 2 so only one render feature is needed
                #pragma multi_compile_local_fragment VL_SPOT VL_SPOT_COOKIE VL_POINT VL_AREA_RECT VL_AREA_DISC
                #pragma multi_compile_local_fragment _ VL_CAST_DIRECT_LIGHT_ADDITIVE VL_CAST_DIRECT_LIGHT_BLEND
				#pragma multi_compile_local_fragment _ VL_PHYSICAL_ATTEN
				#pragma multi_compile _ _GBUFFER_NORMALS_OCT
                #pragma shader_feature_local_fragment VL_CUSTOM_BOUNDS

				#include "CommonsURP.hlsl"
				#include "Primitives.hlsl"
                #include "ShadowOcclusion.hlsl"
				#include "Lighting.hlsl"

				struct appdata
				{
					float4 vertex : POSITION;
					UNITY_VERTEX_INPUT_INSTANCE_ID
				};

				struct v2f
				{
					float4 pos     : SV_POSITION;
					float4 scrPos  : TEXCOORD0;
			        float3 wpos    : TEXCOORD1;
					UNITY_VERTEX_INPUT_INSTANCE_ID
					UNITY_VERTEX_OUTPUT_STEREO
				};

				int _ForcedLightInvisible;

				inline float3 ProjectOnPlane(float3 v, float3 planeNormal) {
					float sqrMag = dot(planeNormal, planeNormal);
					float dt = dot(v, planeNormal);
					return v - planeNormal * dt / sqrMag;
				}

				inline float3 GetRayStart(float3 wpos) {
					float3 cameraPosition = GetCameraPositionWS();
					#if defined(ORTHO_SUPPORT)
						float3 cameraForward = UNITY_MATRIX_V[2].xyz;
						float3 rayStart = ProjectOnPlane(wpos - cameraPosition, cameraForward) + cameraPosition;
						return lerp(cameraPosition, rayStart, unity_OrthoParams.w);
					#else
						return cameraPosition;
					#endif
				}

				v2f vert(appdata v)
				{
					v2f o;

					UNITY_SETUP_INSTANCE_ID(v);
					UNITY_TRANSFER_INSTANCE_ID(v, o);
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

					o.pos = TransformObjectToHClip(v.vertex.xyz);
					o.wpos = TransformObjectToWorld(v.vertex.xyz);
					o.scrPos = ComputeScreenPos(o.pos);
					#if defined(UNITY_REVERSED_Z)
						o.pos.z = o.pos.w * UNITY_NEAR_CLIP_VALUE * 0.99995; //  0.99995 avoids precision issues on some Android devices causing unexpected clipping of light mesh
					#else
						o.pos.z = o.pos.w - 0.000005;
					#endif

					if (_ForcedLightInvisible == 1) {
						o.pos.xy = -10000;
                    }

					return o;
				}


				half4 frag(v2f i) : SV_Target
				{
					UNITY_SETUP_INSTANCE_ID(i);
					UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

					float3 rayStart = GetRayStart(i.wpos);
					float3 ray = i.wpos - rayStart;
                    float  t1 = length(ray);
					float3 rayDir = ray / t1;
					float  t0 = ComputeIntersection(rayStart, rayDir);

                    #if VL_CUSTOM_BOUNDS
                        float b0, b1;
                        BoundsIntersection(rayStart, rayDir, b0, b1);
                        t0 = max(t0, b0);
                        t1 = min(t1, b1);
                    #endif

					float2 uv = i.scrPos.xy / i.scrPos.w;

					float tfar = t1;
					CLAMP_RAY_DEPTH(rayStart, uv, t1);
                    if (t0>=t1) return 0;

                    // Simple analytic integration approximation
                    float dist = t1 - t0;
                    if (dist <= 1e-5) return 0;
                    float3 midPos = rayStart + rayDir * (t0 + dist * 0.5);

                    half geoAtten = DistanceAttenuation(midPos);
                    if (geoAtten <= 0) return 0;

                    half alpha = saturate(_Density * dist * geoAtten);
                    if (alpha <= 0) return 0;

					half4 color = half4(_LightColor.rgb * _Color.rgb, alpha);

                    color.rgb *= color.a;

					#if VL_CAST_DIRECT_LIGHT
						if (t1 < tfar) {
							AddDirectLighting(rayStart + t1 * rayDir, uv, color);
						}
					#endif

					return color;
				}
				ENDHLSL
			}

		}
}
