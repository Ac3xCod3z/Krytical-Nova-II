// Made with Amplify Shader Editor v1.9.8.1
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "KumaBeer/URP_Anime_water"
{
	Properties
	{
		[HideInInspector] _EmissionColor("Emission Color", Color) = (1,1,1,1)
		[HideInInspector] _AlphaCutoff("Alpha Cutoff ", Range(0, 1)) = 0.5
		_Maincolor("Main color", Color) = (0.2117647,0.5529412,0.5843138,1)
		_Wavestime("Waves time", Range( 0 , 1)) = 1
		_Wavestile("Waves tile", Float) = 1
		_WavesNormal("Waves Normal", 2D) = "bump" {}
		_WavesNormalscale("Waves Normal scale", Range( 0.1 , 1)) = 0.3
		_Distortion("Distortion", Float) = 0.5
		_DepthColor("Depth Color", Color) = (0,0.04313726,0.4039216,1)
		_WaterDepth("Water Depth", Float) = 0
		_WaterFalloff("Water Falloff", Float) = -3.5
		_Specular("Specular", Range( -1 , 0)) = -0.624
		[HDR]_RimColor("Rim Color", Color) = (0.1333333,0.3058824,0.3098039,0)
		_RimOffset("Rim Offset", Range( 0 , 1)) = 0.564
		_RimStr("Rim Str", Range( 0.01 , 1)) = 0.95
		_Sparklestile("Sparkles tile", Float) = 25
		_CausticSparklesStr("Caustic/Sparkles Str", Float) = 1
		_Caustictile("Caustic tile", Float) = 15
		_Causticalignmin("Caustic align min", Float) = -3
		_Causticalignmax("Caustic align max", Float) = 1
		_Foamtile("Foam tile", Float) = 10
		_Foamcolor("Foam color", Color) = (0.7372549,0.7372549,0.7372549,0)
		_FoamFalloff("Foam Falloff", Float) = 1.15
		_FoamDepth("Foam Depth", Float) = 1.5
		_Foamblend("Foam blend", Float) = 0.05
		_WaterMask("Water Mask", 2D) = "white" {}
		_Surf("Surf", Float) = 2
		_Surfclamp("Surf clamp", Range( 0 , 1)) = 0.05
		_Reflectstr("Reflect str", Range( 0 , 1)) = 0.4
		[Toggle(_USEREFLECTION_ON)] _Usereflection("Use reflection", Float) = 0


		//_TessPhongStrength( "Tess Phong Strength", Range( 0, 1 ) ) = 0.5
		//_TessValue( "Tess Max Tessellation", Range( 1, 32 ) ) = 16
		//_TessMin( "Tess Min Distance", Float ) = 10
		//_TessMax( "Tess Max Distance", Float ) = 25
		//_TessEdgeLength ( "Tess Edge length", Range( 2, 50 ) ) = 16
		//_TessMaxDisp( "Tess Max Displacement", Float ) = 25

		[HideInInspector] _QueueOffset("_QueueOffset", Float) = 0
        [HideInInspector] _QueueControl("_QueueControl", Float) = -1

        [HideInInspector][NoScaleOffset] unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}

		//[HideInInspector][ToggleUI] _AddPrecomputedVelocity("Add Precomputed Velocity", Float) = 1
		[HideInInspector][ToggleOff] _ReceiveShadows("Receive Shadows", Float) = 1.0
	}

	SubShader
	{
		LOD 0

		

		Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "UniversalMaterialType"="Unlit" }

		Cull Back
		AlphaToMask Off

		

		HLSLINCLUDE
		#pragma target 4.5
		#pragma prefer_hlslcc gles
		// ensure rendering platforms toggle list is visible

		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

		#ifndef ASE_TESS_FUNCS
		#define ASE_TESS_FUNCS
		float4 FixedTess( float tessValue )
		{
			return tessValue;
		}

		float CalcDistanceTessFactor (float4 vertex, float minDist, float maxDist, float tess, float4x4 o2w, float3 cameraPos )
		{
			float3 wpos = mul(o2w,vertex).xyz;
			float dist = distance (wpos, cameraPos);
			float f = clamp(1.0 - (dist - minDist) / (maxDist - minDist), 0.01, 1.0) * tess;
			return f;
		}

		float4 CalcTriEdgeTessFactors (float3 triVertexFactors)
		{
			float4 tess;
			tess.x = 0.5 * (triVertexFactors.y + triVertexFactors.z);
			tess.y = 0.5 * (triVertexFactors.x + triVertexFactors.z);
			tess.z = 0.5 * (triVertexFactors.x + triVertexFactors.y);
			tess.w = (triVertexFactors.x + triVertexFactors.y + triVertexFactors.z) / 3.0f;
			return tess;
		}

		float CalcEdgeTessFactor (float3 wpos0, float3 wpos1, float edgeLen, float3 cameraPos, float4 scParams )
		{
			float dist = distance (0.5 * (wpos0+wpos1), cameraPos);
			float len = distance(wpos0, wpos1);
			float f = max(len * scParams.y / (edgeLen * dist), 1.0);
			return f;
		}

		float DistanceFromPlane (float3 pos, float4 plane)
		{
			float d = dot (float4(pos,1.0f), plane);
			return d;
		}

		bool WorldViewFrustumCull (float3 wpos0, float3 wpos1, float3 wpos2, float cullEps, float4 planes[6] )
		{
			float4 planeTest;
			planeTest.x = (( DistanceFromPlane(wpos0, planes[0]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[0]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[0]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.y = (( DistanceFromPlane(wpos0, planes[1]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[1]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[1]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.z = (( DistanceFromPlane(wpos0, planes[2]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[2]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[2]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.w = (( DistanceFromPlane(wpos0, planes[3]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[3]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[3]) > -cullEps) ? 1.0f : 0.0f );
			return !all (planeTest);
		}

		float4 DistanceBasedTess( float4 v0, float4 v1, float4 v2, float tess, float minDist, float maxDist, float4x4 o2w, float3 cameraPos )
		{
			float3 f;
			f.x = CalcDistanceTessFactor (v0,minDist,maxDist,tess,o2w,cameraPos);
			f.y = CalcDistanceTessFactor (v1,minDist,maxDist,tess,o2w,cameraPos);
			f.z = CalcDistanceTessFactor (v2,minDist,maxDist,tess,o2w,cameraPos);

			return CalcTriEdgeTessFactors (f);
		}

		float4 EdgeLengthBasedTess( float4 v0, float4 v1, float4 v2, float edgeLength, float4x4 o2w, float3 cameraPos, float4 scParams )
		{
			float3 pos0 = mul(o2w,v0).xyz;
			float3 pos1 = mul(o2w,v1).xyz;
			float3 pos2 = mul(o2w,v2).xyz;
			float4 tess;
			tess.x = CalcEdgeTessFactor (pos1, pos2, edgeLength, cameraPos, scParams);
			tess.y = CalcEdgeTessFactor (pos2, pos0, edgeLength, cameraPos, scParams);
			tess.z = CalcEdgeTessFactor (pos0, pos1, edgeLength, cameraPos, scParams);
			tess.w = (tess.x + tess.y + tess.z) / 3.0f;
			return tess;
		}

		float4 EdgeLengthBasedTessCull( float4 v0, float4 v1, float4 v2, float edgeLength, float maxDisplacement, float4x4 o2w, float3 cameraPos, float4 scParams, float4 planes[6] )
		{
			float3 pos0 = mul(o2w,v0).xyz;
			float3 pos1 = mul(o2w,v1).xyz;
			float3 pos2 = mul(o2w,v2).xyz;
			float4 tess;

			if (WorldViewFrustumCull(pos0, pos1, pos2, maxDisplacement, planes))
			{
				tess = 0.0f;
			}
			else
			{
				tess.x = CalcEdgeTessFactor (pos1, pos2, edgeLength, cameraPos, scParams);
				tess.y = CalcEdgeTessFactor (pos2, pos0, edgeLength, cameraPos, scParams);
				tess.z = CalcEdgeTessFactor (pos0, pos1, edgeLength, cameraPos, scParams);
				tess.w = (tess.x + tess.y + tess.z) / 3.0f;
			}
			return tess;
		}
		#endif //ASE_TESS_FUNCS
		ENDHLSL

		
		Pass
		{
			
			Name "Forward"
			Tags { "LightMode"="UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			ZWrite Off
			ZTest LEqual
			Offset 0 , 0
			ColorMask RGB

			

			HLSLPROGRAM

			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer
			#pragma multi_compile_fog
			#define ASE_FOG 1
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define ASE_VERSION 19801
			#define ASE_SRP_VERSION 170100
			#define REQUIRE_DEPTH_TEXTURE 1
			#define REQUIRE_OPAQUE_TEXTURE 1


			#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
			#pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
			#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
			#pragma multi_compile_fragment _ DEBUG_DISPLAY

			#pragma vertex vert
			#pragma fragment frag

			#define SHADERPASS SHADERPASS_UNLIT

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging3D.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#define ASE_NEEDS_FRAG_SCREEN_POSITION
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_FRAG_WORLD_VIEW_DIR
			#define ASE_NEEDS_FRAG_WORLD_POSITION
			#pragma shader_feature_local _USEREFLECTION_ON
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile _ _FORWARD_PLUS
			#pragma multi_compile _ _LIGHT_LAYERS


			#if defined(ASE_EARLY_Z_DEPTH_OPTIMIZE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float4 texcoord : TEXCOORD0;
				float4 texcoord1 : TEXCOORD1;
				float4 texcoord2 : TEXCOORD2;
				half4 ase_tangent : TANGENT;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float4 clipPosV : TEXCOORD0;
				float3 positionWS : TEXCOORD1;
				#if defined(ASE_FOG) || defined(_ADDITIONAL_LIGHTS_VERTEX)
					half4 fogFactorAndVertexLight : TEXCOORD2;
				#endif
				#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					float4 shadowCoord : TEXCOORD3;
				#endif
				#if defined(LIGHTMAP_ON)
					float4 lightmapUVOrVertexSH : TEXCOORD4;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON)
					float2 dynamicLightmapUV : TEXCOORD5;
				#endif
				float4 ase_texcoord6 : TEXCOORD6;
				float4 ase_texcoord7 : TEXCOORD7;
				float4 ase_texcoord8 : TEXCOORD8;
				float4 ase_texcoord9 : TEXCOORD9;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _DepthColor;
			half4 _Maincolor;
			float4 _RimColor;
			half4 _Foamcolor;
			half _Wavestime;
			float _WaterDepth;
			half _Reflectstr;
			half _CausticSparklesStr;
			half _Causticalignmax;
			half _Causticalignmin;
			half _Caustictile;
			half _Sparklestile;
			half _Specular;
			float _Distortion;
			half _Surf;
			float _FoamFalloff;
			half _Foamtile;
			float _FoamDepth;
			float _RimOffset;
			float _RimStr;
			half _Surfclamp;
			half _WavesNormalscale;
			half _Wavestile;
			float _WaterFalloff;
			half _Foamblend;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _WavesNormal;
			sampler2D _WaterMask;


			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			
			inline float4 ASE_ComputeGrabScreenPos( float4 pos )
			{
				#if UNITY_UV_STARTS_AT_TOP
				float scale = -1.0;
				#else
				float scale = 1.0;
				#endif
				float4 o = pos;
				o.y = pos.w * 0.5f;
				o.y = ( pos.y - o.y ) * _ProjectionParams.x * scale + o.y;
				return o;
			}
			
			half4 CalculateShadowMask216_g1(  )
			{
				#if defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON)
				half4 shadowMask = inputData.shadowMask;
				#elif !defined (LIGHTMAP_ON)
				half4 shadowMask = unity_ProbesOcclusion;
				#else
				half4 shadowMask = half4(1, 1, 1, 1);
				#endif
				return shadowMask;
			}
			
			half3 AdditionalLightsFlatMask17x( float3 WorldPosition, half2 ScreenUV, half4 ShadowMask )
			{
				float3 Color = 0;
				#if defined(_ADDITIONAL_LIGHTS)
					#define SUM_LIGHTFLAT(Light)\
						Color += Light.color * ( Light.distanceAttenuation * Light.shadowAttenuation );
					InputData inputData = (InputData)0;
					inputData.normalizedScreenSpaceUV = ScreenUV;
					inputData.positionWS = WorldPosition;
					uint meshRenderingLayers = GetMeshRenderingLayer();
					uint pixelLightCount = GetAdditionalLightsCount();	
					#if USE_FORWARD_PLUS
					[loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
					{
						FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
						Light light = GetAdditionalLight(lightIndex, WorldPosition, ShadowMask);
						#ifdef _LIGHT_LAYERS
						if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
						#endif
						{
							SUM_LIGHTFLAT( light );
						}
					}
					#endif
					LIGHT_LOOP_BEGIN( pixelLightCount )
						Light light = GetAdditionalLight(lightIndex, WorldPosition, ShadowMask);
						#ifdef _LIGHT_LAYERS
						if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
						#endif
						{
							SUM_LIGHTFLAT( light );
						}
					LIGHT_LOOP_END
				#endif
				return Color;
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				half mulTime896 = _TimeParameters.x * _Wavestime;
				half2 temp_cast_0 = (_Wavestile).xx;
				half2 texCoord1038 = input.texcoord.xy * temp_cast_0 + float2( 0,0 );
				half2 panner475 = ( mulTime896 * float2( 0.04,0.03 ) + texCoord1038);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				half depthLinearEye1 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ), _ZBufferParams );
				half depth1119 = abs( ( depthLinearEye1 - screenPos.w ) );
				half temp_output_794_0 = ( 1.0 - depth1119 );
				half lerpResult814 = lerp( _WavesNormalscale , _Surfclamp , saturate( temp_output_794_0 ));
				half3 unpack23 = UnpackNormalScale( tex2Dlod( _WavesNormal, float4( panner475, 0, 1.0) ), lerpResult814 );
				unpack23.z = lerp( 1, unpack23.z, saturate(lerpResult814) );
				half2 panner829 = ( mulTime896 * float2( -0.03,0.04 ) + texCoord1038);
				half3 unpack17 = UnpackNormalScale( tex2Dlod( _WavesNormal, float4( panner829, 0, 1.0) ), lerpResult814 );
				unpack17.z = lerp( 1, unpack17.z, saturate(lerpResult814) );
				half3 temp_output_826_0 = BlendNormal( unpack23 , unpack17 );
				half3 temp_cast_1 = (temp_output_826_0.y).xxx;
				
				half3 ase_tangentWS = TransformObjectToWorldDir( input.ase_tangent.xyz );
				output.ase_texcoord7.xyz = ase_tangentWS;
				half3 ase_normalWS = TransformObjectToWorldNormal( input.normalOS );
				output.ase_texcoord8.xyz = ase_normalWS;
				half ase_tangentSign = input.ase_tangent.w * ( unity_WorldTransformParams.w >= 0.0 ? 1.0 : -1.0 );
				float3 ase_bitangentWS = cross( ase_normalWS, ase_tangentWS ) * ase_tangentSign;
				output.ase_texcoord9.xyz = ase_bitangentWS;
				
				output.ase_texcoord6.xy = input.texcoord.xy;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord6.zw = 0;
				output.ase_texcoord7.w = 0;
				output.ase_texcoord8.w = 0;
				output.ase_texcoord9.w = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = temp_cast_1;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				#if defined(LIGHTMAP_ON)
					OUTPUT_LIGHTMAP_UV(input.texcoord1, unity_LightmapST, output.lightmapUVOrVertexSH.xy);
				#endif
				#if defined(DYNAMICLIGHTMAP_ON)
					output.dynamicLightmapUV.xy = input.texcoord2.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
				#endif

				#if defined(ASE_FOG) || defined(_ADDITIONAL_LIGHTS_VERTEX)
					output.fogFactorAndVertexLight = 0;
					#if defined(ASE_FOG) && !defined(_FOG_FRAGMENT)
						output.fogFactorAndVertexLight.x = ComputeFogFactor(vertexInput.positionCS.z);
					#endif
					#ifdef _ADDITIONAL_LIGHTS_VERTEX
						half3 vertexLight = VertexLighting( vertexInput.positionWS, normalInput.normalWS );
						output.fogFactorAndVertexLight.yzw = vertexLight;
					#endif
				#endif

				#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					output.shadowCoord = GetShadowCoord( vertexInput );
				#endif

				output.positionCS = vertexInput.positionCS;
				output.clipPosV = vertexInput.positionCS;
				output.positionWS = vertexInput.positionWS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				float3 normalOS : NORMAL;
				half4 ase_tangent : TANGENT;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.ase_tangent = input.ase_tangent;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.ase_tangent = patch[0].ase_tangent * bary.x + patch[1].ase_tangent * bary.y + patch[2].ase_tangent * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag ( PackedVaryings input
						#ifdef ASE_DEPTH_WRITE_ON
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						#ifdef _WRITE_RENDERING_LAYERS
						, out float4 outRenderingLayers : SV_Target1
						#endif
						 ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				float3 WorldPosition = input.positionWS;
				float3 WorldViewDirection = GetWorldSpaceNormalizeViewDir( WorldPosition );
				float4 ShadowCoords = float4( 0, 0, 0, 0 );
				float4 ClipPos = input.clipPosV;
				float4 ScreenPos = ComputeScreenPos( input.clipPosV );

				float2 NormalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

				#if defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
						ShadowCoords = input.shadowCoord;
					#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
						ShadowCoords = TransformWorldToShadowCoord( WorldPosition );
					#endif
				#endif

				WorldViewDirection = SafeNormalize( WorldViewDirection );

				half mulTime896 = _TimeParameters.x * _Wavestime;
				half2 temp_cast_0 = (_Wavestile).xx;
				half2 texCoord1038 = input.ase_texcoord6.xy * temp_cast_0 + float2( 0,0 );
				half2 panner475 = ( mulTime896 * float2( 0.04,0.03 ) + texCoord1038);
				float4 ase_positionSSNorm = ScreenPos / ScreenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				half depthLinearEye1 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_positionSSNorm.xy ), _ZBufferParams );
				half depth1119 = abs( ( depthLinearEye1 - ScreenPos.w ) );
				half temp_output_794_0 = ( 1.0 - depth1119 );
				half lerpResult814 = lerp( _WavesNormalscale , _Surfclamp , saturate( temp_output_794_0 ));
				half3 unpack23 = UnpackNormalScale( tex2D( _WavesNormal, panner475 ), lerpResult814 );
				unpack23.z = lerp( 1, unpack23.z, saturate(lerpResult814) );
				half2 panner829 = ( mulTime896 * float2( -0.03,0.04 ) + texCoord1038);
				half3 unpack17 = UnpackNormalScale( tex2D( _WavesNormal, panner829 ), lerpResult814 );
				unpack17.z = lerp( 1, unpack17.z, saturate(lerpResult814) );
				half3 temp_output_826_0 = BlendNormal( unpack23 , unpack17 );
				half3 ase_tangentWS = input.ase_texcoord7.xyz;
				half3 ase_normalWS = input.ase_texcoord8.xyz;
				float3 ase_bitangentWS = input.ase_texcoord9.xyz;
				half3 tanToWorld0 = float3( ase_tangentWS.x, ase_bitangentWS.x, ase_normalWS.x );
				half3 tanToWorld1 = float3( ase_tangentWS.y, ase_bitangentWS.y, ase_normalWS.y );
				half3 tanToWorld2 = float3( ase_tangentWS.z, ase_bitangentWS.z, ase_normalWS.z );
				float3 tanNormal261 = temp_output_826_0;
				half3 worldNormal261 = float3( dot( tanToWorld0, tanNormal261 ), dot( tanToWorld1, tanNormal261 ), dot( tanToWorld2, tanNormal261 ) );
				half3 Wnorm1113 = worldNormal261;
				half dotResult240 = dot( Wnorm1113 , _MainLightPosition.xyz );
				half dotResult242 = dot( Wnorm1113 , WorldViewDirection );
				half NdotV1134 = dotResult242;
				half smoothstepResult265 = smoothstep( _RimStr , _RimOffset , ( NdotV1134 + _RimOffset ));
				half temp_output_255_0 = saturate( ( dotResult240 * smoothstepResult265 ) );
				half ase_lightIntensity = max( max( _MainLightColor.r, _MainLightColor.g ), _MainLightColor.b ) + 1e-7;
				half4 ase_lightColor = float4( _MainLightColor.rgb / ase_lightIntensity, ase_lightIntensity );
				half4 Lightcolor233 = ase_lightColor;
				half2 temp_cast_2 = (_Foamtile).xx;
				half2 texCoord106 = input.ase_texcoord6.xy * temp_cast_2 + float2( 0,0 );
				half2 panner116 = ( ( sin( _TimeParameters.x ) * ( 1.0 - dotResult242 ) ) * float2( 0.5,0.5 ) + texCoord106);
				half4 tex2DNode668 = tex2D( _WaterMask, panner116 );
				half smoothstepResult335 = smoothstep( 1.2 , 1.0 , ( ( depth1119 * _FoamDepth * tex2DNode668.b ) + _FoamFalloff ));
				half lerpResult712 = lerp( sin( ( ( (1.0 + (depth1119 - 0.0) * (0.0 - 1.0) / (1.0 - 0.0)) - ( _TimeParameters.x * 0.4 ) ) * 6.0 ) ) , 2.0 , _Surf);
				half Foam338 = saturate( ( smoothstepResult335 * lerpResult712 ) );
				float4 ase_grabScreenPos = ASE_ComputeGrabScreenPos( ScreenPos );
				half4 ase_grabScreenPosNorm = ase_grabScreenPos / ase_grabScreenPos.w;
				half3 normalizedWorldNormal = normalize( ase_normalWS );
				half dotResult1055 = dot( normalizedWorldNormal.y , WorldViewDirection.y );
				half dotResult1062 = dot( Wnorm1113 , ( WorldViewDirection * 0.5 ) );
				half2 appendResult1076 = (half2(ase_grabScreenPosNorm.r , ( saturate( ( 1.0 - ( ase_grabScreenPosNorm.g - dotResult1055 ) ) ) - saturate( dotResult1062 ) )));
				half4 fetchOpaqueVal1077 = float4( SHADERGRAPH_SAMPLE_SCENE_COLOR( ( half3( appendResult1076 ,  0.0 ) + ( half3( (temp_output_826_0).xy ,  0.0 ) * ( temp_output_826_0 * _Distortion ) ) ).xy.xy ), 1.0 );
				half3 FakeReflect1115 = (fetchOpaqueVal1077).rgb;
				half4 lerpResult841 = lerp( Lightcolor233 , half4( FakeReflect1115 , 0.0 ) , 0.6);
				half lerpResult758 = lerp( _Specular , temp_output_255_0 , 0.45);
				half clampResult750 = clamp( sign( lerpResult758 ) , 0.0 , 0.8 );
				half2 temp_cast_7 = (_Sparklestile).xx;
				half2 texCoord860 = input.ase_texcoord6.xy * temp_cast_7 + ( ScreenPos * dotResult242 ).xy;
				half2 temp_cast_9 = (_Caustictile).xx;
				half2 texCoord737 = input.ase_texcoord6.xy * temp_cast_9 + float2( 0,0 );
				half depthInv1125 = temp_output_794_0;
				half clampResult893 = clamp( depthInv1125 , _Causticalignmin , _Causticalignmax );
				half2 panner22 = ( 1.0 * _Time.y * float2( -0.03,0 ) + ( texCoord737 + clampResult893 ));
				half4 tex2DNode105 = tex2D( _WaterMask, panner22 );
				half4 lerpResult836 = lerp( float4( 0,0,0,0 ) , lerpResult841 , ( clampResult750 + saturate( (( UnpackNormalScale( tex2D( _WaterMask, texCoord860 ), 1.0f ).g * tex2DNode668.b * tex2DNode105.r * _CausticSparklesStr )*20.0 + -0.5) ) ));
				half clampResult853 = clamp( depthInv1125 , 0.05 , 1.0 );
				half clampResult724 = clamp( ( tex2DNode105.r * _CausticSparklesStr * clampResult853 * ( 1.0 - Foam338 ) * ase_lightColor.a ) , 0.0 , Lightcolor233.r );
				half4 temp_cast_11 = (0.0).xxxx;
				half4 lerpResult210 = lerp( _Maincolor , half4( FakeReflect1115 , 0.0 ) , ( NdotV1134 * _Reflectstr * saturate( depth1119 ) * saturate( ( ( ( saturate( ( ase_positionSSNorm.x / 0.5 ) ) * saturate( ( ( 1.0 - ase_positionSSNorm.x ) / 0.5 ) ) * ( 1.0 - ( ase_positionSSNorm.y / 0.67 ) ) * saturate( ( ( ase_positionSSNorm.y - 0.21 ) / 0.25 ) ) ) - 0.06 ) / 0.3 ) ) ));
				#ifdef _USEREFLECTION_ON
				half4 staticSwitch986 = lerpResult210;
				#else
				half4 staticSwitch986 = temp_cast_11;
				#endif
				half4 lerpResult93 = lerp( _DepthColor , float4( 0,0,0,0 ) , saturate( pow( ( depth1119 + _WaterDepth ) , _WaterFalloff ) ));
				half3 worldPosValue184_g1 = WorldPosition;
				half3 WorldPosition175_g1 = worldPosValue184_g1;
				half2 ScreenUV183_g1 = (ase_positionSSNorm).xy;
				half2 ScreenUV175_g1 = ScreenUV183_g1;
				half4 localCalculateShadowMask216_g1 = CalculateShadowMask216_g1();
				half4 shadowMaskValue182_g1 = localCalculateShadowMask216_g1;
				half4 ShadowMask175_g1 = shadowMaskValue182_g1;
				half3 localAdditionalLightsFlatMask17x175_g1 = AdditionalLightsFlatMask17x( WorldPosition175_g1 , ScreenUV175_g1 , ShadowMask175_g1 );
				
				float3 BakedAlbedo = 0;
				float3 BakedEmission = 0;
				float3 Color = ( ( ( temp_output_255_0 * half4( (_RimColor).rgb , 0.0 ) * Lightcolor233 ) + ( Foam338 * _Foamcolor ) + lerpResult836 + clampResult724 + staticSwitch986 ) + ( lerpResult93 * Lightcolor233 ) + half4( localAdditionalLightsFlatMask17x175_g1 , 0.0 ) ).rgb;
				float Alpha = saturate( ( depth1119 / _Foamblend ) );
				float AlphaClipThreshold = 0.5;
				float AlphaClipThresholdShadow = 0.5;

				#ifdef ASE_DEPTH_WRITE_ON
					float DepthValue = input.positionCS.z;
				#endif

				#ifdef _ALPHATEST_ON
					clip(Alpha - AlphaClipThreshold);
				#endif

				InputData inputData = (InputData)0;
				inputData.positionWS = WorldPosition;
				inputData.viewDirectionWS = WorldViewDirection;

				#ifdef ASE_FOG
					inputData.fogCoord = InitializeInputDataFog(float4(inputData.positionWS, 1.0), input.fogFactorAndVertexLight.x);
				#endif
				#ifdef _ADDITIONAL_LIGHTS_VERTEX
					inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
				#endif

				inputData.normalizedScreenSpaceUV = NormalizedScreenSpaceUV;

				#if defined(_DBUFFER)
					ApplyDecalToBaseColor(input.positionCS, Color);
				#endif

				#ifdef ASE_FOG
					#ifdef TERRAIN_SPLAT_ADDPASS
						Color.rgb = MixFogColor(Color.rgb, half3(0,0,0), inputData.fogCoord);
					#else
						Color.rgb = MixFog(Color.rgb, inputData.fogCoord);
					#endif
				#endif

				#ifdef ASE_DEPTH_WRITE_ON
					outputDepth = DepthValue;
				#endif

				#ifdef _WRITE_RENDERING_LAYERS
					uint renderingLayers = GetMeshRenderingLayer();
					outRenderingLayers = float4( EncodeMeshRenderingLayer( renderingLayers ), 0, 0, 0 );
				#endif

				return half4( Color, Alpha );
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "DepthOnly"
			Tags { "LightMode"="DepthOnly" }

			ZWrite On
			ColorMask 0
			AlphaToMask Off

			HLSLPROGRAM

			#pragma multi_compile_instancing
			#define ASE_FOG 1
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define ASE_VERSION 19801
			#define ASE_SRP_VERSION 170100
			#define REQUIRE_DEPTH_TEXTURE 1


			#pragma vertex vert
			#pragma fragment frag

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#define ASE_NEEDS_FRAG_SCREEN_POSITION


			#if defined(ASE_EARLY_Z_DEPTH_OPTIMIZE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float4 clipPosV : TEXCOORD0;
				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
					float3 positionWS : TEXCOORD1;
				#endif
				#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					float4 shadowCoord : TEXCOORD2;
				#endif
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _DepthColor;
			half4 _Maincolor;
			float4 _RimColor;
			half4 _Foamcolor;
			half _Wavestime;
			float _WaterDepth;
			half _Reflectstr;
			half _CausticSparklesStr;
			half _Causticalignmax;
			half _Causticalignmin;
			half _Caustictile;
			half _Sparklestile;
			half _Specular;
			float _Distortion;
			half _Surf;
			float _FoamFalloff;
			half _Foamtile;
			float _FoamDepth;
			float _RimOffset;
			float _RimStr;
			half _Surfclamp;
			half _WavesNormalscale;
			half _Wavestile;
			float _WaterFalloff;
			half _Foamblend;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _WavesNormal;


			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				half mulTime896 = _TimeParameters.x * _Wavestime;
				half2 temp_cast_0 = (_Wavestile).xx;
				half2 texCoord1038 = input.ase_texcoord.xy * temp_cast_0 + float2( 0,0 );
				half2 panner475 = ( mulTime896 * float2( 0.04,0.03 ) + texCoord1038);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				half depthLinearEye1 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ), _ZBufferParams );
				half depth1119 = abs( ( depthLinearEye1 - screenPos.w ) );
				half temp_output_794_0 = ( 1.0 - depth1119 );
				half lerpResult814 = lerp( _WavesNormalscale , _Surfclamp , saturate( temp_output_794_0 ));
				half3 unpack23 = UnpackNormalScale( tex2Dlod( _WavesNormal, float4( panner475, 0, 1.0) ), lerpResult814 );
				unpack23.z = lerp( 1, unpack23.z, saturate(lerpResult814) );
				half2 panner829 = ( mulTime896 * float2( -0.03,0.04 ) + texCoord1038);
				half3 unpack17 = UnpackNormalScale( tex2Dlod( _WavesNormal, float4( panner829, 0, 1.0) ), lerpResult814 );
				unpack17.z = lerp( 1, unpack17.z, saturate(lerpResult814) );
				half3 temp_output_826_0 = BlendNormal( unpack23 , unpack17 );
				half3 temp_cast_1 = (temp_output_826_0.y).xxx;
				

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = temp_cast_1;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
					output.positionWS = vertexInput.positionWS;
				#endif

				#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					output.shadowCoord = GetShadowCoord( vertexInput );
				#endif

				output.positionCS = vertexInput.positionCS;
				output.clipPosV = vertexInput.positionCS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.ase_texcoord = input.ase_texcoord;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(PackedVaryings input
						#ifdef ASE_DEPTH_WRITE_ON
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						 ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
				float3 WorldPosition = input.positionWS;
				#endif

				float4 ShadowCoords = float4( 0, 0, 0, 0 );
				float4 ClipPos = input.clipPosV;
				float4 ScreenPos = ComputeScreenPos( input.clipPosV );

				#if defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
						ShadowCoords = input.shadowCoord;
					#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
						ShadowCoords = TransformWorldToShadowCoord( WorldPosition );
					#endif
				#endif

				float4 ase_positionSSNorm = ScreenPos / ScreenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				half depthLinearEye1 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_positionSSNorm.xy ), _ZBufferParams );
				half depth1119 = abs( ( depthLinearEye1 - ScreenPos.w ) );
				

				float Alpha = saturate( ( depth1119 / _Foamblend ) );
				float AlphaClipThreshold = 0.5;

				#ifdef ASE_DEPTH_WRITE_ON
					float DepthValue = input.positionCS.z;
				#endif

				#ifdef _ALPHATEST_ON
					clip(Alpha - AlphaClipThreshold);
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#ifdef ASE_DEPTH_WRITE_ON
					outputDepth = DepthValue;
				#endif

				return 0;
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "SceneSelectionPass"
			Tags { "LightMode"="SceneSelectionPass" }

			Cull Off
			AlphaToMask Off

			HLSLPROGRAM

			#define ASE_FOG 1
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define ASE_VERSION 19801
			#define ASE_SRP_VERSION 170100
			#define REQUIRE_DEPTH_TEXTURE 1


			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _DepthColor;
			half4 _Maincolor;
			float4 _RimColor;
			half4 _Foamcolor;
			half _Wavestime;
			float _WaterDepth;
			half _Reflectstr;
			half _CausticSparklesStr;
			half _Causticalignmax;
			half _Causticalignmin;
			half _Caustictile;
			half _Sparklestile;
			half _Specular;
			float _Distortion;
			half _Surf;
			float _FoamFalloff;
			half _Foamtile;
			float _FoamDepth;
			float _RimOffset;
			float _RimStr;
			half _Surfclamp;
			half _WavesNormalscale;
			half _Wavestile;
			float _WaterFalloff;
			half _Foamblend;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _WavesNormal;


			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			

			int _ObjectId;
			int _PassValue;

			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			PackedVaryings VertexFunction(Attributes input  )
			{
				PackedVaryings output;
				ZERO_INITIALIZE(PackedVaryings, output);

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				half mulTime896 = _TimeParameters.x * _Wavestime;
				half2 temp_cast_0 = (_Wavestile).xx;
				half2 texCoord1038 = input.ase_texcoord.xy * temp_cast_0 + float2( 0,0 );
				half2 panner475 = ( mulTime896 * float2( 0.04,0.03 ) + texCoord1038);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				half depthLinearEye1 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ), _ZBufferParams );
				half depth1119 = abs( ( depthLinearEye1 - screenPos.w ) );
				half temp_output_794_0 = ( 1.0 - depth1119 );
				half lerpResult814 = lerp( _WavesNormalscale , _Surfclamp , saturate( temp_output_794_0 ));
				half3 unpack23 = UnpackNormalScale( tex2Dlod( _WavesNormal, float4( panner475, 0, 1.0) ), lerpResult814 );
				unpack23.z = lerp( 1, unpack23.z, saturate(lerpResult814) );
				half2 panner829 = ( mulTime896 * float2( -0.03,0.04 ) + texCoord1038);
				half3 unpack17 = UnpackNormalScale( tex2Dlod( _WavesNormal, float4( panner829, 0, 1.0) ), lerpResult814 );
				unpack17.z = lerp( 1, unpack17.z, saturate(lerpResult814) );
				half3 temp_output_826_0 = BlendNormal( unpack23 , unpack17 );
				half3 temp_cast_1 = (temp_output_826_0.y).xxx;
				
				output.ase_texcoord = screenPos;
				

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = temp_cast_1;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;

				float3 positionWS = TransformObjectToWorld( input.positionOS.xyz );

				output.positionCS = TransformWorldToHClip(positionWS);

				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.ase_texcoord = input.ase_texcoord;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(PackedVaryings input ) : SV_Target
			{
				SurfaceDescription surfaceDescription = (SurfaceDescription)0;

				float4 screenPos = input.ase_texcoord;
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				half depthLinearEye1 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_positionSSNorm.xy ), _ZBufferParams );
				half depth1119 = abs( ( depthLinearEye1 - screenPos.w ) );
				

				surfaceDescription.Alpha = saturate( ( depth1119 / _Foamblend ) );
				surfaceDescription.AlphaClipThreshold = 0.5;

				#if _ALPHATEST_ON
					float alphaClipThreshold = 0.01f;
					#if ALPHA_CLIP_THRESHOLD
						alphaClipThreshold = surfaceDescription.AlphaClipThreshold;
					#endif
					clip(surfaceDescription.Alpha - alphaClipThreshold);
				#endif

				half4 outColor = half4(_ObjectId, _PassValue, 1.0, 1.0);
				return outColor;
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "ScenePickingPass"
			Tags { "LightMode"="Picking" }

			AlphaToMask Off

			HLSLPROGRAM

			#define ASE_FOG 1
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define ASE_VERSION 19801
			#define ASE_SRP_VERSION 170100
			#define REQUIRE_DEPTH_TEXTURE 1


			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT

			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _DepthColor;
			half4 _Maincolor;
			float4 _RimColor;
			half4 _Foamcolor;
			half _Wavestime;
			float _WaterDepth;
			half _Reflectstr;
			half _CausticSparklesStr;
			half _Causticalignmax;
			half _Causticalignmin;
			half _Caustictile;
			half _Sparklestile;
			half _Specular;
			float _Distortion;
			half _Surf;
			float _FoamFalloff;
			half _Foamtile;
			float _FoamDepth;
			float _RimOffset;
			float _RimStr;
			half _Surfclamp;
			half _WavesNormalscale;
			half _Wavestile;
			float _WaterFalloff;
			half _Foamblend;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _WavesNormal;


			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			

			float4 _SelectionID;

			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			PackedVaryings VertexFunction(Attributes input  )
			{
				PackedVaryings output;
				ZERO_INITIALIZE(PackedVaryings, output);

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				half mulTime896 = _TimeParameters.x * _Wavestime;
				half2 temp_cast_0 = (_Wavestile).xx;
				half2 texCoord1038 = input.ase_texcoord.xy * temp_cast_0 + float2( 0,0 );
				half2 panner475 = ( mulTime896 * float2( 0.04,0.03 ) + texCoord1038);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				half depthLinearEye1 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ), _ZBufferParams );
				half depth1119 = abs( ( depthLinearEye1 - screenPos.w ) );
				half temp_output_794_0 = ( 1.0 - depth1119 );
				half lerpResult814 = lerp( _WavesNormalscale , _Surfclamp , saturate( temp_output_794_0 ));
				half3 unpack23 = UnpackNormalScale( tex2Dlod( _WavesNormal, float4( panner475, 0, 1.0) ), lerpResult814 );
				unpack23.z = lerp( 1, unpack23.z, saturate(lerpResult814) );
				half2 panner829 = ( mulTime896 * float2( -0.03,0.04 ) + texCoord1038);
				half3 unpack17 = UnpackNormalScale( tex2Dlod( _WavesNormal, float4( panner829, 0, 1.0) ), lerpResult814 );
				unpack17.z = lerp( 1, unpack17.z, saturate(lerpResult814) );
				half3 temp_output_826_0 = BlendNormal( unpack23 , unpack17 );
				half3 temp_cast_1 = (temp_output_826_0.y).xxx;
				
				output.ase_texcoord = screenPos;
				

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = temp_cast_1;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;

				float3 positionWS = TransformObjectToWorld( input.positionOS.xyz );
				output.positionCS = TransformWorldToHClip(positionWS);
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.ase_texcoord = input.ase_texcoord;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(PackedVaryings input ) : SV_Target
			{
				SurfaceDescription surfaceDescription = (SurfaceDescription)0;

				float4 screenPos = input.ase_texcoord;
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				half depthLinearEye1 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_positionSSNorm.xy ), _ZBufferParams );
				half depth1119 = abs( ( depthLinearEye1 - screenPos.w ) );
				

				surfaceDescription.Alpha = saturate( ( depth1119 / _Foamblend ) );
				surfaceDescription.AlphaClipThreshold = 0.5;

				#if _ALPHATEST_ON
					float alphaClipThreshold = 0.01f;
					#if ALPHA_CLIP_THRESHOLD
						alphaClipThreshold = surfaceDescription.AlphaClipThreshold;
					#endif
					clip(surfaceDescription.Alpha - alphaClipThreshold);
				#endif

				half4 outColor = 0;
				outColor = _SelectionID;

				return outColor;
			}

			ENDHLSL
		}

		
		Pass
		{
			
			Name "DepthNormals"
			Tags { "LightMode"="DepthNormals" }

			ZTest LEqual
			ZWrite On

			HLSLPROGRAM

        	#pragma multi_compile_instancing
        	#define ASE_FOG 1
        	#define _SURFACE_TYPE_TRANSPARENT 1
        	#define ASE_VERSION 19801
        	#define ASE_SRP_VERSION 170100
        	#define REQUIRE_DEPTH_TEXTURE 1


        	#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define VARYINGS_NEED_NORMAL_WS

			#define SHADERPASS SHADERPASS_DEPTHNORMALSONLY

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#define ASE_NEEDS_FRAG_SCREEN_POSITION


			#if defined(ASE_EARLY_Z_DEPTH_OPTIMIZE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float4 clipPosV : TEXCOORD0;
				float3 positionWS : TEXCOORD1;
				float3 normalWS : TEXCOORD2;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _DepthColor;
			half4 _Maincolor;
			float4 _RimColor;
			half4 _Foamcolor;
			half _Wavestime;
			float _WaterDepth;
			half _Reflectstr;
			half _CausticSparklesStr;
			half _Causticalignmax;
			half _Causticalignmin;
			half _Caustictile;
			half _Sparklestile;
			half _Specular;
			float _Distortion;
			half _Surf;
			float _FoamFalloff;
			half _Foamtile;
			float _FoamDepth;
			float _RimOffset;
			float _RimStr;
			half _Surfclamp;
			half _WavesNormalscale;
			half _Wavestile;
			float _WaterFalloff;
			half _Foamblend;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _WavesNormal;


			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			

			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output;
				ZERO_INITIALIZE(PackedVaryings, output);

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				half mulTime896 = _TimeParameters.x * _Wavestime;
				half2 temp_cast_0 = (_Wavestile).xx;
				half2 texCoord1038 = input.ase_texcoord.xy * temp_cast_0 + float2( 0,0 );
				half2 panner475 = ( mulTime896 * float2( 0.04,0.03 ) + texCoord1038);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				half depthLinearEye1 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ), _ZBufferParams );
				half depth1119 = abs( ( depthLinearEye1 - screenPos.w ) );
				half temp_output_794_0 = ( 1.0 - depth1119 );
				half lerpResult814 = lerp( _WavesNormalscale , _Surfclamp , saturate( temp_output_794_0 ));
				half3 unpack23 = UnpackNormalScale( tex2Dlod( _WavesNormal, float4( panner475, 0, 1.0) ), lerpResult814 );
				unpack23.z = lerp( 1, unpack23.z, saturate(lerpResult814) );
				half2 panner829 = ( mulTime896 * float2( -0.03,0.04 ) + texCoord1038);
				half3 unpack17 = UnpackNormalScale( tex2Dlod( _WavesNormal, float4( panner829, 0, 1.0) ), lerpResult814 );
				unpack17.z = lerp( 1, unpack17.z, saturate(lerpResult814) );
				half3 temp_output_826_0 = BlendNormal( unpack23 , unpack17 );
				half3 temp_cast_1 = (temp_output_826_0.y).xxx;
				
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = temp_cast_1;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = vertexInput.positionCS;
				output.clipPosV = vertexInput.positionCS;
				output.positionWS = vertexInput.positionWS;
				output.normalWS = TransformObjectToWorldNormal( input.normalOS );
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				float3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.ase_texcoord = input.ase_texcoord;
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			void frag(PackedVaryings input
						, out half4 outNormalWS : SV_Target0
						#ifdef ASE_DEPTH_WRITE_ON
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						#ifdef _WRITE_RENDERING_LAYERS
						, out float4 outRenderingLayers : SV_Target1
						#endif
						 )
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );
				float3 WorldPosition = input.positionWS;
				float3 WorldNormal = input.normalWS;
				float4 ClipPos = input.clipPosV;
				float4 ScreenPos = ComputeScreenPos( input.clipPosV );

				float4 ase_positionSSNorm = ScreenPos / ScreenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				half depthLinearEye1 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_positionSSNorm.xy ), _ZBufferParams );
				half depth1119 = abs( ( depthLinearEye1 - ScreenPos.w ) );
				

				float Alpha = saturate( ( depth1119 / _Foamblend ) );
				float AlphaClipThreshold = 0.5;

				#ifdef ASE_DEPTH_WRITE_ON
					float DepthValue = input.positionCS.z;
				#endif

				#ifdef _ALPHATEST_ON
					clip(Alpha - AlphaClipThreshold);
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#ifdef ASE_DEPTH_WRITE_ON
					outputDepth = DepthValue;
				#endif

				#if defined(_GBUFFER_NORMALS_OCT)
					float3 normalWS = normalize(input.normalWS);
					float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
					float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
					half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
					outNormalWS = half4(packedNormalWS, 0.0);
				#else
					float3 normalWS = input.normalWS;
					outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
				#endif

				#ifdef _WRITE_RENDERING_LAYERS
					uint renderingLayers = GetMeshRenderingLayer();
					outRenderingLayers = float4(EncodeMeshRenderingLayer(renderingLayers), 0, 0, 0);
				#endif
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "MotionVectors"
			Tags { "LightMode"="MotionVectors" }

			ColorMask RG

			HLSLPROGRAM

			#pragma multi_compile_instancing
			#define ASE_FOG 1
			#define _SURFACE_TYPE_TRANSPARENT 1
			#define ASE_VERSION 19801
			#define ASE_SRP_VERSION 170100
			#define REQUIRE_DEPTH_TEXTURE 1


			#pragma vertex vert
			#pragma fragment frag

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
		    #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
		    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
		    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
		    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
				#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
			#endif

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"

			

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 positionOld : TEXCOORD4;
				#if _ADD_PRECOMPUTED_VELOCITY
					float3 alembicMotionVector : TEXCOORD5;
				#endif
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float4 positionCSNoJitter : TEXCOORD0;
				float4 previousPositionCSNoJitter : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _DepthColor;
			half4 _Maincolor;
			float4 _RimColor;
			half4 _Foamcolor;
			half _Wavestime;
			float _WaterDepth;
			half _Reflectstr;
			half _CausticSparklesStr;
			half _Causticalignmax;
			half _Causticalignmin;
			half _Caustictile;
			half _Sparklestile;
			half _Specular;
			float _Distortion;
			half _Surf;
			float _FoamFalloff;
			half _Foamtile;
			float _FoamDepth;
			float _RimOffset;
			float _RimStr;
			half _Surfclamp;
			half _WavesNormalscale;
			half _Wavestile;
			float _WaterFalloff;
			half _Foamblend;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			sampler2D _WavesNormal;


			float SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD(float2 uv)
			{
				#if defined(REQUIRE_DEPTH_TEXTURE)
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				 	float rawDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, unity_StereoEyeIndex, 0).r;
				#else
				 	float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0);
				#endif
				return rawDepth;
				#endif // REQUIRE_DEPTH_TEXTURE
				return 0;
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				half mulTime896 = _TimeParameters.x * _Wavestime;
				half2 temp_cast_0 = (_Wavestile).xx;
				half2 texCoord1038 = input.ase_texcoord.xy * temp_cast_0 + float2( 0,0 );
				half2 panner475 = ( mulTime896 * float2( 0.04,0.03 ) + texCoord1038);
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				half depthLinearEye1 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH_LOD( ase_positionSSNorm.xy ), _ZBufferParams );
				half depth1119 = abs( ( depthLinearEye1 - screenPos.w ) );
				half temp_output_794_0 = ( 1.0 - depth1119 );
				half lerpResult814 = lerp( _WavesNormalscale , _Surfclamp , saturate( temp_output_794_0 ));
				half3 unpack23 = UnpackNormalScale( tex2Dlod( _WavesNormal, float4( panner475, 0, 1.0) ), lerpResult814 );
				unpack23.z = lerp( 1, unpack23.z, saturate(lerpResult814) );
				half2 panner829 = ( mulTime896 * float2( -0.03,0.04 ) + texCoord1038);
				half3 unpack17 = UnpackNormalScale( tex2Dlod( _WavesNormal, float4( panner829, 0, 1.0) ), lerpResult814 );
				unpack17.z = lerp( 1, unpack17.z, saturate(lerpResult814) );
				half3 temp_output_826_0 = BlendNormal( unpack23 , unpack17 );
				half3 temp_cast_1 = (temp_output_826_0.y).xxx;
				
				output.ase_texcoord2 = screenPos;
				

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = temp_cast_1;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				// Jittered. Match the frame.
				output.positionCS = vertexInput.positionCS;
				output.positionCSNoJitter = mul( _NonJitteredViewProjMatrix, mul( UNITY_MATRIX_M, input.positionOS ) );

				float4 prevPos = ( unity_MotionVectorsParams.x == 1 ) ? float4( input.positionOld, 1 ) : input.positionOS;

				#if _ADD_PRECOMPUTED_VELOCITY
					prevPos = prevPos - float4(input.alembicMotionVector, 0);
				#endif

				output.previousPositionCSNoJitter = mul( _PrevViewProjMatrix, mul( UNITY_PREV_MATRIX_M, prevPos ) );

				return output;
			}

			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}

			half4 frag(	PackedVaryings input  ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				float4 screenPos = input.ase_texcoord2;
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				half depthLinearEye1 = LinearEyeDepth( SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_positionSSNorm.xy ), _ZBufferParams );
				half depth1119 = abs( ( depthLinearEye1 - screenPos.w ) );
				

				float Alpha = saturate( ( depth1119 / _Foamblend ) );
				float AlphaClipThreshold = 0.5;

				#ifdef _ALPHATEST_ON
					clip(Alpha - AlphaClipThreshold);
				#endif

				#ifdef LOD_FADE_CROSSFADE
					LODFadeCrossFade( input.positionCS );
				#endif

				return float4( CalcNdcMotionVectorFromCsPositions( input.positionCSNoJitter, input.previousPositionCSNoJitter ), 0, 0 );
			}
			ENDHLSL
		}
		
	}
	
	CustomEditor "UnityEditor.ShaderGraphUnlitGUI"
	FallBack "Hidden/Shader Graph/FallbackError"
	
	Fallback "0"
}
/*ASEBEGIN
Version=19801
Node;AmplifyShaderEditor.CommentaryNode;152;-2726.7,-299.3656;Inherit;False;915.5244;296.9143;Depth Fade;5;89;3;1;1031;166;Depth;1,1,1,1;0;0
Node;AmplifyShaderEditor.ScreenPosInputsNode;166;-2698.618,-258.027;Float;False;0;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ScreenPosInputsNode;1031;-2287.955,-179.2633;Float;False;1;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ScreenDepthNode;1;-2460.499,-254.9639;Inherit;False;0;1;0;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;3;-2085.757,-250.9547;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.AbsOpNode;89;-1936.762,-242.9418;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;769;-627.9565,-453.1382;Inherit;False;1036.188;456.9082;Waves;11;818;829;814;475;813;721;17;23;896;897;1038;Waves;1,1,1,1;0;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;1119;-1768.426,-275.0044;Inherit;False;depth;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;897;-620.0594,-108.7512;Inherit;False;Property;_Wavestime;Waves time;1;0;Create;True;0;0;0;False;0;False;1;1;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;721;-619.8724,-389.1046;Inherit;False;Property;_Wavestile;Waves tile;2;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;794;-1549.111,-274.5341;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;818;-620.0889,-201.4229;Inherit;False;Property;_Surfclamp;Surf clamp;25;0;Create;True;0;0;0;False;0;False;0.05;0.05;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;896;-340.0594,-103.7512;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;813;-618.8032,-282.5838;Inherit;False;Property;_WavesNormalscale;Waves Normal scale;4;0;Create;True;0;0;0;False;0;False;0.3;0.3;0.1;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;1038;-439.3949,-418.7189;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SaturateNode;816;-1129.231,-227.0295;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;814;-124.1481,-281.8014;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode;475;-130.5865,-405.6331;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0.04,0.03;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.PannerNode;829;-136.0423,-149.5408;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;-0.03,0.04;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SamplerNode;23;65.34294,-418.2462;Inherit;True;Property;_WavesNormal2;Waves Normal 2;3;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;True;bump;Auto;True;Instance;17;Auto;Texture2D;8;0;SAMPLER2D;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;1;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SamplerNode;17;63.53008,-203.7247;Inherit;True;Property;_WavesNormal;Waves Normal;3;0;Create;True;0;0;0;False;0;False;-1;None;0e2840867afdcbc4a88cb532e35985a7;True;0;True;bump;Auto;True;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;1;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RangedFloatNode;1049;2846.769,-191.0499;Inherit;False;Property;_Foamblend;Foam blend;22;0;Create;True;0;0;0;False;0;False;0.05;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;1121;2765.091,-307.3571;Inherit;False;1119;depth;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;1118;729.6181,1061.38;Inherit;False;2542.748;513.071;FakeReflect;21;1115;224;98;400;834;97;1114;1059;1073;1067;1066;1063;1062;1058;1056;1055;1052;1051;1076;1077;1079;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;1117;-162.4539,393.6281;Inherit;False;2185.653;433.6854;screen mask;15;1080;1069;1065;1133;1068;1078;1074;1072;1071;1070;1064;1061;1060;1075;1057;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;768;1306.897,-251.5786;Inherit;False;780.754;567.5134;Reflections;8;1122;848;1116;228;226;840;210;1135;Reflections;0.2311321,1,0.8462238,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;234;-533.8716,-2591.205;Inherit;False;3071.234;674.892;Rimlight;27;843;1128;841;247;1113;253;836;886;750;888;240;251;252;753;244;255;760;758;250;265;246;238;254;242;261;249;1134;Rimlight+Specular;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;153;-2833.974,159.5769;Inherit;False;2570.978;742.7954;Foam;25;338;113;438;712;335;713;115;688;690;735;333;686;111;706;684;116;364;106;704;375;365;363;668;1123;1124;Foam;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;159;-784.3449,-949.2329;Inherit;False;790.0168;382.0366;Depth control;7;6;94;87;10;88;12;1120;Depth coloring;0.245105,0.3316393,0.6415094,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;895;530.0017,-1554.597;Inherit;False;1813.997;650.1569;Caustics and sparkles;21;890;885;729;722;724;105;22;894;893;737;736;862;860;891;872;898;899;2;979;980;1127;Caustics and sparkles;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;1048;3045.129,-317.6109;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0.1;False;1;FLOAT;0
Node;AmplifyShaderEditor.BlendNormalsNode;826;465.5646,-271.554;Inherit;False;0;3;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.ViewDirInputsCoordNode;249;-476.011,-2242.57;Float;False;World;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.WorldNormalVector;261;-437.4382,-2530.584;Inherit;False;False;1;0;FLOAT3;0,0,1;False;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.SimpleTimeNode;363;-2811.215,495.5995;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.WireNode;505;-2848.487,-23.73637;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;375;-2804.045,583.6836;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SinOpNode;365;-2531.285,495.5934;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;704;-2546.448,277.7944;Inherit;False;Property;_Foamtile;Foam tile;18;0;Create;True;0;0;0;False;0;False;10;10;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;364;-2373.919,559.4222;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;106;-2320.988,220.5851;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;10,10;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;706;-2548.576,727.6694;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.4;False;1;FLOAT;0
Node;AmplifyShaderEditor.TFHCRemapNode;684;-1963.96,529.1949;Inherit;True;5;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;3;FLOAT;1;False;4;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode;116;-2067.225,225.2435;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;0.5,0.5;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;686;-1664.034,682.9836;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;668;-1842.211,207.9578;Inherit;True;Property;_WaterMask;Water Mask;23;0;Create;True;0;0;0;False;0;False;-1;None;f8d49aff2ed9bac4c825ad2dfac48383;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RangedFloatNode;111;-1520.495,342.7661;Float;False;Property;_FoamDepth;Foam Depth;21;0;Create;True;0;0;0;False;0;False;1.5;2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;690;-1425.684,683.6314;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;6;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;735;-1328.646,348.3019;Float;False;Property;_FoamFalloff;Foam Falloff;20;0;Create;True;0;0;0;False;0;False;1.15;1.06;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;333;-1324.886,210.5276;Inherit;False;3;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;115;-1162.813,220.1345;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;1.15;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;713;-1209.122,573.4477;Inherit;False;Property;_Surf;Surf;24;0;Create;True;0;0;0;False;0;False;2;0.8;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SinOpNode;688;-1151.688,680.7766;Inherit;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;712;-956.7236,533.95;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;2;False;2;FLOAT;0.6;False;1;FLOAT;0
Node;AmplifyShaderEditor.SmoothstepOpNode;335;-1024.108,223.6589;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;1.2;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;438;-822.7691,221.6705;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;113;-655.8522,224.8192;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;338;-492.0742,218.7641;Inherit;False;Foam;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;339;2164.578,-753.6297;Inherit;False;338;Foam;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;447;1992.328,-639.1523;Inherit;False;Property;_Foamcolor;Foam color;19;0;Create;True;0;0;0;False;0;False;0.7372549,0.7372549,0.7372549,0;0.9056604,0.9056604,0.9056604,0;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;453;2406.411,-657.6507;Inherit;False;2;2;0;FLOAT;0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.BreakToComponentsNode;835;775.1506,-276.3547;Inherit;False;FLOAT3;1;0;FLOAT3;0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.RangedFloatNode;254;-245.9667,-2088.857;Float;False;Property;_RimOffset;Rim Offset;11;0;Create;True;0;0;0;False;0;False;0.564;0.416;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;736;557.1447,-1188.314;Inherit;False;Property;_Caustictile;Caustic tile;15;0;Create;True;0;0;0;False;0;False;15;15;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.WorldSpaceLightDirHlpNode;238;111.4446,-2454.887;Inherit;False;False;1;0;FLOAT;0;False;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.TextureCoordinatesNode;737;749.1449,-1220.314;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.PowerNode;87;-349.5934,-880.2133;Inherit;False;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;246;618.1282,-2159.205;Float;False;Property;_RimColor;Rim Color;10;1;[HDR];Create;True;0;0;0;False;0;False;0.1333333,0.3058824,0.3098039,0;0.1333333,0.3058824,0.3098039,0;False;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ScreenPosInputsNode;2;535.4302,-1402.207;Float;False;1;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;891;638.7505,-1489.203;Inherit;False;Property;_Sparklestile;Sparkles tile;13;0;Create;True;0;0;0;False;0;False;25;20;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;872;716.733,-1398.606;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleAddOpNode;894;1005.201,-1213.656;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SmoothstepOpNode;265;420.1218,-2289.372;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0.42;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;250;97.21306,-2066.926;Float;False;Property;_RimStr;Rim Str;12;0;Create;True;0;0;0;False;0;False;0.95;0.85;0.01;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;758;1480.963,-2506.949;Inherit;False;3;0;FLOAT;-0.7;False;1;FLOAT;1.25;False;2;FLOAT;0.45;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;760;1186,-2514.831;Inherit;False;Property;_Specular;Specular;9;0;Create;True;0;0;0;False;0;False;-0.624;-0.624;-1;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;729;1607.5,-1342.491;Inherit;False;Property;_CausticSparklesStr;Caustic/Sparkles Str;14;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;105;1359.156,-1241.477;Inherit;True;Property;_RCaustic;(R) Caustic;23;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Instance;668;Auto;Texture2D;8;0;SAMPLER2D;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;1;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SaturateNode;255;1004.677,-2478.089;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;88;-531.6058,-879.7777;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;885;1868.824,-1495.166;Inherit;False;4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;6;-749.7183,-723.6888;Float;False;Property;_WaterDepth;Water Depth;7;0;Create;True;0;0;0;False;0;False;0;0.63;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SignOpNode;753;1676.485,-2498.858;Inherit;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;10;-537.0668,-748.8902;Float;False;Property;_WaterFalloff;Water Falloff;8;0;Create;True;0;0;0;False;0;False;-3.5;-3.5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ScaleAndOffsetNode;890;2028.824,-1495.166;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;20;False;2;FLOAT;-0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;252;778.1287,-2479.205;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ComponentMaskNode;251;890.1288,-2159.205;Inherit;False;True;True;True;False;1;0;COLOR;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;722;1903.271,-1308.674;Inherit;True;5;5;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;985;2252.039,-346.5457;Inherit;False;Constant;_Float1;Float 1;29;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DotProductOpNode;240;440.5474,-2534.417;Inherit;True;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PannerNode;22;1159.592,-1214.136;Inherit;False;3;0;FLOAT2;0,0;False;2;FLOAT2;-0.03,0;False;1;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;860;852.7328,-1446.606;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;25,25;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SaturateNode;888;1973.206,-2250.151;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode;750;1869.631,-2499.601;Inherit;True;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0.8;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;886;2170.718,-2274.143;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;94;-139.6477,-881.3301;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;986;2412.04,-346.324;Inherit;False;Property;_Usereflection;Use reflection;27;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;COLOR;0,0,0,0;False;0;COLOR;0,0,0,0;False;2;COLOR;0,0,0,0;False;3;COLOR;0,0,0,0;False;4;COLOR;0,0,0,0;False;5;COLOR;0,0,0,0;False;6;COLOR;0,0,0,0;False;7;COLOR;0,0,0,0;False;8;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleAddOpNode;259;2603.865,-707.1708;Inherit;False;5;5;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;COLOR;0,0,0,0;False;3;FLOAT;0;False;4;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.ClampOpNode;853;1446.179,-607.5709;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0.05;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;12;-209.5247,-795.3911;Float;False;Property;_DepthColor;Depth Color;6;0;Create;True;0;0;0;False;0;False;0,0.04313726,0.4039216,1;0,0.04313587,0.4039196,1;False;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ClampOpNode;724;2110.232,-1308.303;Inherit;True;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;836;2333.09,-2103.787;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.LerpOp;93;490.9236,-749.451;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.WireNode;921;2462.058,-764.5395;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;231;1671.622,-738.3243;Inherit;True;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;253;1455.827,-2194.804;Inherit;True;3;3;0;FLOAT;0;False;1;FLOAT3;0,0,0;False;2;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SamplerNode;862;1092.732,-1478.606;Inherit;True;Property;_GSparkles;(G) Sparkles;23;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;True;Instance;668;Auto;Texture2D;8;0;SAMPLER2D;0,0;False;1;FLOAT2;0,0;False;2;FLOAT;1;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.GetLocalVarNode;979;1505.606,-1030.456;Inherit;False;338;Foam;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;905;2982.708,-705.8524;Inherit;False;3;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT3;0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.FunctionNode;1032;2649.173,-491.996;Inherit;False;SRP Additional Light;-1;;1;6c86746ad131a0a408ca599df5f40861;3,6,0,9,0,23,0;6;2;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;15;FLOAT3;0,0,0;False;14;FLOAT3;1,1,1;False;18;FLOAT;0.5;False;32;FLOAT4;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SaturateNode;1044;3374.309,-521.2861;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;210;1941.228,-34.39653;Inherit;False;3;0;COLOR;1,1,1,0;False;1;COLOR;1,1,1,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;1113;-225.5728,-2537.226;Inherit;False;Wnorm;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;840;1760.282,91.73495;Inherit;False;4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;226;1430.678,113.2281;Inherit;False;Property;_Reflectstr;Reflect str;26;0;Create;True;0;0;0;False;0;False;0.4;0.4;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.ScreenPosInputsNode;1057;-40.71813,437.1098;Float;True;0;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;1075;1068.583,420.0051;Inherit;True;4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ComponentMaskNode;1079;2726.469,1165.439;Inherit;False;True;True;True;False;1;0;COLOR;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.ScreenColorNode;1077;2455.05,1167.916;Inherit;False;Global;_GrabScreen1;Grab Screen 1;20;0;Create;True;0;0;0;False;0;False;Object;-1;True;False;False;False;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.DynamicAppendNode;1076;2011.746,1160.056;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.ViewDirInputsCoordNode;1051;876.9471,1431.174;Inherit;False;World;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.WorldNormalVector;1052;774.4173,1221.291;Inherit;False;True;1;0;FLOAT3;0,0,1;False;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.DotProductOpNode;1055;1207.718,1273.69;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ScaleNode;1056;1252.098,1430.653;Inherit;False;0.5;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;1058;1332.697,1236.279;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DotProductOpNode;1062;1422.487,1359.029;Inherit;False;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;1063;1508.405,1268.237;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;1066;1684.526,1352.093;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;1067;1658.825,1267.546;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;1073;1841.667,1267.306;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;1059;944.3267,1357.155;Inherit;False;1113;Wnorm;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GrabScreenPosition;1114;964.8356,1114.673;Inherit;False;0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ComponentMaskNode;834;1896.333,1365.686;Inherit;False;True;True;False;True;1;0;FLOAT3;0,0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;400;2122.25,1432.776;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;98;1949.865,1456.324;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleAddOpNode;224;2273.95,1167.758;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;1115;3021.979,1169.215;Inherit;False;FakeReflect;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;1060;332.1201,721.4232;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0.21;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;1061;326.0401,526.7104;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;1064;544.78,722.0792;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0.25;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;1070;816.8811,723.5062;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;1071;547.9601,426.459;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;1072;699.2101,614.4225;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;1074;622.3904,526.2775;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;1120;-771.6453,-877.1136;Inherit;False;1119;depth;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;1123;-1537.707,207.6925;Inherit;False;1119;depth;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;1124;-2164.623,549.8185;Inherit;False;1119;depth;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;1125;-1299.724,-355.6925;Inherit;False;depthInv;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;1126;1178.865,-571.2107;Inherit;False;1125;depthInv;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;899;642.2328,-983.4099;Inherit;False;Property;_Causticalignmax;Caustic align max;17;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode;893;851.1612,-1049.121;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;-3;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;1127;556.5107,-1119.262;Inherit;False;1125;depthInv;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;898;557.8269,-1050.087;Inherit;False;Property;_Causticalignmin;Caustic align min;16;0;Create;True;0;0;0;False;0;False;-3;-33.5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;980;1721.509,-1059.304;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.LightColorNode;232;1103.443,-859.7948;Inherit;False;0;3;COLOR;0;FLOAT3;1;FLOAT;2
Node;AmplifyShaderEditor.RegisterLocalVarNode;233;1349.966,-858.61;Inherit;False;Lightcolor;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.GetLocalVarNode;247;1185.425,-2081.397;Inherit;False;233;Lightcolor;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.LerpOp;841;2072.877,-2170.813;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0.1415094;False;1;COLOR;0
Node;AmplifyShaderEditor.GetLocalVarNode;1128;1836.614,-2083.619;Inherit;False;1115;FakeReflect;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RangedFloatNode;843;1860.324,-2015.208;Inherit;False;Constant;_Float0;Float 0;29;0;Create;True;0;0;0;False;0;False;0.6;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;228;1639.326,-201.7604;Inherit;False;Property;_Maincolor;Main color;0;0;Create;True;0;0;0;False;0;False;0.2117647,0.5529412,0.5843138,1;0.2117626,0.5529411,0.5843138,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.GetLocalVarNode;1116;1675.783,0.9460883;Inherit;False;1115;FakeReflect;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SaturateNode;848;1569.615,194.3573;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;1122;1369.027,207.175;Inherit;False;1119;depth;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;1078;1481.267,416.7225;Inherit;True;2;0;FLOAT;0;False;1;FLOAT;0.3;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;1068;515.9993,614.6095;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0.67;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;1133;1312.973,437.1151;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0.06;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;1065;495.6904,526.8774;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;1069;408.2154,425.0722;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;1080;1727.298,421.0694;Inherit;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;244;286.1038,-2299.569;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DotProductOpNode;242;-156.0119,-2306.569;Inherit;True;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;1134;59.14575,-2300.536;Inherit;False;NdotV;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;97;1759.558,1481.775;Float;False;Property;_Distortion;Distortion;5;0;Create;True;0;0;0;False;0;False;0.5;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;1135;1471.248,31.1532;Inherit;False;1134;NdotV;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;917;3601.475,-707.1378;Half;False;True;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;13;KumaBeer/URP_Anime_water;2992e84f91cbeb14eab234972e07ea9d;True;Forward;0;1;Forward;9;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Unlit;True;5;True;12;all;0;False;True;1;5;False;;10;False;;1;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;False;0;False;;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;True;2;False;;True;3;False;;True;True;0;False;;0;False;;True;1;LightMode=UniversalForward;False;False;0;0;0;0;Standard;27;Surface;1;638811731861119068;  Blend;0;0;Two Sided;1;0;Alpha Clipping;0;638811702202981251;  Use Shadow Threshold;0;0;Forward Only;0;638811705750357142;Cast Shadows;0;0;Receive Shadows;0;0;Motion Vectors;1;0;  Add Precomputed Velocity;0;0;GPU Instancing;1;0;LOD CrossFade;0;0;Built-in Fog;1;0;Meta Pass;0;638811708274551862;Extra Pre Pass;0;638811708349429110;Tessellation;0;638811716998346295;  Phong;0;638811700696935714;  Strength;0.5,False,;0;  Type;1;638811707134974904;  Tess;16,False,;0;  Min;10,False,;0;  Max;25,False,;0;  Edge Length;16,False,;0;  Max Displacement;25,False,;0;Write Depth;0;0;  Early Z;0;0;Vertex Position,InvertActionOnDeselection;1;638811700613154867;0;11;False;True;False;True;False;False;True;True;True;False;True;False;;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;919;3248.475,-732.1378;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;3;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthOnly;0;3;DepthOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;True;False;False;False;False;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;False;False;True;1;LightMode=DepthOnly;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;916;3246.475,-732.1378;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;13;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ExtraPrePass;0;0;ExtraPrePass;5;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;12;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;True;0;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;918;3248.475,-732.1378;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;3;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ShadowCaster;0;2;ShadowCaster;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;True;False;False;False;False;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;True;1;LightMode=ShadowCaster;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;920;3248.475,-732.1378;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;3;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;Meta;0;4;Meta;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Meta;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1033;3248.475,-682.1378;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;Universal2D;0;5;Universal2D;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;12;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;True;1;LightMode=Universal2D;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1034;3248.475,-682.1378;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;SceneSelectionPass;0;6;SceneSelectionPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=SceneSelectionPass;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1035;3248.475,-682.1378;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ScenePickingPass;0;7;ScenePickingPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Picking;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1036;3248.475,-682.1378;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthNormals;0;8;DepthNormals;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;True;1;LightMode=DepthNormals;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1037;3248.475,-682.1378;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthNormalsOnly;0;9;DepthNormalsOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;True;1;LightMode=DepthNormalsOnly;False;True;9;d3d11;metal;vulkan;xboxone;xboxseries;playstation;ps4;ps5;switch;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1050;3601.475,-607.1378;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;MotionVectors;0;10;MotionVectors;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;False;False;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=MotionVectors;False;False;0;;0;0;Standard;0;False;0
WireConnection;1;0;166;0
WireConnection;3;0;1;0
WireConnection;3;1;1031;4
WireConnection;89;0;3;0
WireConnection;1119;0;89;0
WireConnection;794;0;1119;0
WireConnection;896;0;897;0
WireConnection;1038;0;721;0
WireConnection;816;0;794;0
WireConnection;814;0;813;0
WireConnection;814;1;818;0
WireConnection;814;2;816;0
WireConnection;475;0;1038;0
WireConnection;475;1;896;0
WireConnection;829;0;1038;0
WireConnection;829;1;896;0
WireConnection;23;1;475;0
WireConnection;23;5;814;0
WireConnection;17;1;829;0
WireConnection;17;5;814;0
WireConnection;1048;0;1121;0
WireConnection;1048;1;1049;0
WireConnection;826;0;23;0
WireConnection;826;1;17;0
WireConnection;261;0;826;0
WireConnection;505;0;242;0
WireConnection;375;0;505;0
WireConnection;365;0;363;0
WireConnection;364;0;365;0
WireConnection;364;1;375;0
WireConnection;106;0;704;0
WireConnection;706;0;363;0
WireConnection;684;0;1124;0
WireConnection;116;0;106;0
WireConnection;116;1;364;0
WireConnection;686;0;684;0
WireConnection;686;1;706;0
WireConnection;668;1;116;0
WireConnection;690;0;686;0
WireConnection;333;0;1123;0
WireConnection;333;1;111;0
WireConnection;333;2;668;3
WireConnection;115;0;333;0
WireConnection;115;1;735;0
WireConnection;688;0;690;0
WireConnection;712;0;688;0
WireConnection;712;2;713;0
WireConnection;335;0;115;0
WireConnection;438;0;335;0
WireConnection;438;1;712;0
WireConnection;113;0;438;0
WireConnection;338;0;113;0
WireConnection;453;0;339;0
WireConnection;453;1;447;0
WireConnection;835;0;826;0
WireConnection;737;0;736;0
WireConnection;87;0;88;0
WireConnection;87;1;10;0
WireConnection;872;0;2;0
WireConnection;872;1;242;0
WireConnection;894;0;737;0
WireConnection;894;1;893;0
WireConnection;265;0;244;0
WireConnection;265;1;250;0
WireConnection;265;2;254;0
WireConnection;758;0;760;0
WireConnection;758;1;255;0
WireConnection;105;1;22;0
WireConnection;255;0;252;0
WireConnection;88;0;1120;0
WireConnection;88;1;6;0
WireConnection;885;0;862;2
WireConnection;885;1;668;3
WireConnection;885;2;105;1
WireConnection;885;3;729;0
WireConnection;753;0;758;0
WireConnection;890;0;885;0
WireConnection;252;0;240;0
WireConnection;252;1;265;0
WireConnection;251;0;246;0
WireConnection;722;0;105;1
WireConnection;722;1;729;0
WireConnection;722;2;853;0
WireConnection;722;3;980;0
WireConnection;722;4;232;2
WireConnection;240;0;1113;0
WireConnection;240;1;238;0
WireConnection;22;0;894;0
WireConnection;860;0;891;0
WireConnection;860;1;872;0
WireConnection;888;0;890;0
WireConnection;750;0;753;0
WireConnection;886;0;750;0
WireConnection;886;1;888;0
WireConnection;94;0;87;0
WireConnection;986;1;985;0
WireConnection;986;0;210;0
WireConnection;259;0;921;0
WireConnection;259;1;453;0
WireConnection;259;2;836;0
WireConnection;259;3;724;0
WireConnection;259;4;986;0
WireConnection;853;0;1126;0
WireConnection;724;0;722;0
WireConnection;724;2;247;0
WireConnection;836;1;841;0
WireConnection;836;2;886;0
WireConnection;93;0;12;0
WireConnection;93;2;94;0
WireConnection;921;0;253;0
WireConnection;231;0;93;0
WireConnection;231;1;233;0
WireConnection;253;0;255;0
WireConnection;253;1;251;0
WireConnection;253;2;247;0
WireConnection;862;1;860;0
WireConnection;905;0;259;0
WireConnection;905;1;231;0
WireConnection;905;2;1032;0
WireConnection;1044;0;1048;0
WireConnection;210;0;228;0
WireConnection;210;1;1116;0
WireConnection;210;2;840;0
WireConnection;1113;0;261;0
WireConnection;840;0;1135;0
WireConnection;840;1;226;0
WireConnection;840;2;848;0
WireConnection;840;3;1080;0
WireConnection;1075;0;1071;0
WireConnection;1075;1;1074;0
WireConnection;1075;2;1072;0
WireConnection;1075;3;1070;0
WireConnection;1079;0;1077;0
WireConnection;1077;0;224;0
WireConnection;1076;0;1114;1
WireConnection;1076;1;1073;0
WireConnection;1055;0;1052;2
WireConnection;1055;1;1051;2
WireConnection;1056;0;1051;0
WireConnection;1058;0;1114;2
WireConnection;1058;1;1055;0
WireConnection;1062;0;1059;0
WireConnection;1062;1;1056;0
WireConnection;1063;0;1058;0
WireConnection;1066;0;1062;0
WireConnection;1067;0;1063;0
WireConnection;1073;0;1067;0
WireConnection;1073;1;1066;0
WireConnection;834;0;826;0
WireConnection;400;0;834;0
WireConnection;400;1;98;0
WireConnection;98;0;826;0
WireConnection;98;1;97;0
WireConnection;224;0;1076;0
WireConnection;224;1;400;0
WireConnection;1115;0;1079;0
WireConnection;1060;0;1057;2
WireConnection;1061;0;1057;1
WireConnection;1064;0;1060;0
WireConnection;1070;0;1064;0
WireConnection;1071;0;1069;0
WireConnection;1072;0;1068;0
WireConnection;1074;0;1065;0
WireConnection;1125;0;794;0
WireConnection;893;0;1127;0
WireConnection;893;1;898;0
WireConnection;893;2;899;0
WireConnection;980;0;979;0
WireConnection;233;0;232;0
WireConnection;841;0;247;0
WireConnection;841;1;1128;0
WireConnection;841;2;843;0
WireConnection;848;0;1122;0
WireConnection;1078;0;1133;0
WireConnection;1068;0;1057;2
WireConnection;1133;0;1075;0
WireConnection;1065;0;1061;0
WireConnection;1069;0;1057;1
WireConnection;1080;0;1078;0
WireConnection;244;0;1134;0
WireConnection;244;1;254;0
WireConnection;242;0;1113;0
WireConnection;242;1;249;0
WireConnection;1134;0;242;0
WireConnection;917;2;905;0
WireConnection;917;3;1044;0
WireConnection;917;5;835;1
ASEEND*/
//CHKSM=FE93279F3CFB782A30D8743B745DE3D30A05A64D