// Made with Amplify Shader Editor v1.9.9.9
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "CTFX2/Billboard"
{
	Properties
	{
		_Cutoff( "Cutoff", Range( 0, 1 ) ) = 0.1
		_DitherFade( "Dither Fade", Range( 0, 1 ) ) = 1
		_Dither( "Dither", Range( 0, 1 ) ) = 1
		_SoftParticles( "Soft Particles", Range( 0.01, 1 ) ) = 0.5
		_Overlay( "Overlay", 2D ) = "black" {}
		_OverlayTiling( "Overlay Tiling", Float ) = 1
		_OverlayRotation( "Overlay Rotation", Float ) = 0
		_OverlayColor( "Overlay Color", Color ) = ( 0, 1, 0, 0.2 )
		_OverlayOffset( "Overlay Offset", Vector ) = ( 0, 0, 0, 0 )
		_Albedo( "Albedo", 2D ) = "white" {}
		_Dissolve( "Dissolve", 2D ) = "white" {}
		_Flipbook( "Flipbook", Vector ) = ( 1, 1, 0, 0 )
		[Toggle( _ANIMATE_ON )] _Animate( "Animate", Float ) = 0
		[Toggle( _RANDOMSTARTFRAME_ON )] _RandomStartFrame( "Random Start Frame", Float ) = 0
		[Toggle( _USEMESHINDEX_ON )] _UseMeshIndex( "Use Mesh Index", Float ) = 0
		_AnimationSpeed( "Animation Speed", Float ) = 0


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

		[HideInInspector][ToggleUI] _AddPrecomputedVelocity("Add Precomputed Velocity", Float) = 1
		//[HideInInspector][ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0

		[HideInInspector] _XRMotionVectorsPass("_XRMotionVectorsPass", Float) = 1

		[HideInInspector] _AlphaClip("__clip", Float) = 0.0
	}

	SubShader
	{
Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "UniversalMaterialType"="Unlit" }

	LOD 0

		Cull Back
		AlphaToMask Off

		

		HLSLINCLUDE
		#pragma target 3.5
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
			Tags { "LightMode"="UniversalForwardOnly" }

			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			ZWrite Off
			ZTest LEqual
			Offset 0 , 0
			ColorMask RGBA

			

			HLSLPROGRAM

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _ALPHATEST_ON
			#define UNLIT_DEFAULT_DECAL_BLENDING 1
			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer
			#pragma multi_compile _ LOD_FADE_CROSSFADE
			#define _RECEIVE_SHADOWS_OFF
			#define ASE_VERSION 19909
			#define ASE_SRP_VERSION 170004
			#define REQUIRE_DEPTH_TEXTURE 1


			#pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3

			#pragma multi_compile_fragment _ DEBUG_DISPLAY

			#pragma vertex vert
			#pragma fragment frag

			// Option "Keep Lighting Variants" @david please keep this note for future changes in Unlit  lighting
			//#define UNLIT_REALTIME_LIGHTING 1
			//#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			//#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			//#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			//#pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
			//#pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
			//#pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
			//#pragma multi_compile _ REFLECTION_PROBE_ROTATION
			//#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			//#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			//#pragma multi_compile _ SHADOWS_SHADOWMASK
			//#pragma multi_compile_fragment _ _LIGHT_LAYERS
			//#pragma multi_compile_fragment _ _LIGHT_COOKIES
			//#pragma multi_compile _ _CLUSTER_LIGHT_LOOP

			#define SHADERPASS SHADERPASS_UNLIT

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#if defined( _WRITE_RENDERING_LAYERS )
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#endif
			#if ( UNITY_VERSION >= 60010000 )
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
			#else
			#pragma multi_compile_fog
			#endif
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#if UNITY_2023_1_OR_NEWER
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
			#endif
            #if UNITY_2023_1_OR_NEWER
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
            #endif
			#if UNITY_2023_1_OR_NEWER
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#endif
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging3D.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#include "Sub/HLSL/MobileSwitch.hlsl"
			#include "Sub/HLSL/WebGLSwitch.hlsl"
			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_TEXTURE_COORDINATES2
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES2
			#define ASE_NEEDS_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED
			#define ASE_NEEDS_VERT_POSITION
			#define ASE_NEEDS_FRAG_COLOR
			#pragma shader_feature_local _ANIMATE_ON
			#pragma shader_feature_local _USEMESHINDEX_ON
			#pragma shader_feature_local _RANDOMSTARTFRAME_ON
			#pragma multi_compile __ CTFX_IS_MOBILE_SHADER
			#pragma multi_compile __ CTFX_IS_WEBGL_SHADER


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
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float4 positionWSAndFogFactor : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
				half4 tangentWS : TEXCOORD2;
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_texcoord4 : TEXCOORD4;
				float4 ase_texcoord5 : TEXCOORD5;
				float4 ase_color : COLOR;
				float4 ase_texcoord6 : TEXCOORD6;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _OverlayOffset;
			float4 _OverlayColor;
			float2 _Flipbook;
			float _OverlayRotation;
			float _OverlayTiling;
			float _AnimationSpeed;
			float _SoftParticles;
			float _Dither;
			float _DitherFade;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _Overlay;
			sampler2D _Albedo;
			sampler2D _Dissolve;


			float4 ASEScreenPositionNormalizedToPixel( float4 screenPosNorm, float4 screenParams )
			{
				float4 screenPosPixel = screenPosNorm * float4( screenParams.xy, 1, 1 );
				#if UNITY_UV_STARTS_AT_TOP
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x < 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#else
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x > 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#endif
				return screenPosPixel;
			}
			
			inline float Dither4x4Bayer( uint x, uint y )
			{
				const float dither[ 16 ] = {
				     1,  9,  3, 11,
				    13,  5, 15,  7,
				     4, 12,  2, 10,
				    16,  8, 14,  6 };
				uint r = y * 4 + x;
				return dither[ min( r, 15 ) ] / 16; // same # of instructions as pre-dividing due to compiler magic
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float3 objectToViewPos = TransformWorldToView( TransformObjectToWorld( input.positionOS.xyz ) );
				float eyeDepth = -objectToViewPos.z;
				output.ase_texcoord6.x = eyeDepth;
				
				output.ase_texcoord3 = input.ase_texcoord;
				output.ase_texcoord4 = input.ase_texcoord2;
				output.ase_texcoord5 = input.ase_texcoord1;
				output.ase_color = input.ase_color;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord6.yzw = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );
				VertexNormalInputs normalInput = GetVertexNormalInputs( input.normalOS, input.tangentOS );

				float fogFactor = 0;
				#if defined(ASE_FOG) && !defined(_FOG_FRAGMENT)
					fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
				#endif

				output.positionCS = vertexInput.positionCS;
				output.positionWSAndFogFactor = float4( vertexInput.positionWS, fogFactor );
				output.normalWS = normalInput.normalWS;
				output.tangentWS = half4( normalInput.tangentWS, ( input.tangentOS.w > 0.0 ? 1.0 : -1.0 ) * GetOddNegativeScale() );;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_color : COLOR;

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
				output.tangentOS = input.tangentOS;
				output.ase_texcoord = input.ase_texcoord;
				output.ase_texcoord2 = input.ase_texcoord2;
				output.ase_texcoord1 = input.ase_texcoord1;
				output.ase_color = input.ase_color;
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
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				output.ase_texcoord2 = patch[0].ase_texcoord2 * bary.x + patch[1].ase_texcoord2 * bary.y + patch[2].ase_texcoord2 * bary.z;
				output.ase_texcoord1 = patch[0].ase_texcoord1 * bary.x + patch[1].ase_texcoord1 * bary.y + patch[2].ase_texcoord1 * bary.z;
				output.ase_color = patch[0].ase_color * bary.x + patch[1].ase_color * bary.y + patch[2].ase_color * bary.z;
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
						#if defined( ASE_DEPTH_WRITE_ON )
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						#ifdef _WRITE_RENDERING_LAYERS
						#if ( UNITY_VERSION >= 60020000 )
						, out uint outRenderingLayers : SV_Target1
						#else
						, out float4 outRenderingLayers : SV_Target1
						#endif
						#endif
						 ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				#if defined( _SURFACE_TYPE_TRANSPARENT )
					const bool isTransparent = true;
				#else
					const bool isTransparent = false;
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					float4 shadowCoord = TransformWorldToShadowCoord( input.positionWSAndFogFactor.xyz );
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				// @diogo: mikktspace compliant
				float renormFactor = 1.0 / max( FLT_MIN, length( input.normalWS ) );

				float3 PositionWS = input.positionWSAndFogFactor.xyz;
				float3 PositionRWS = GetCameraRelativePositionWS( PositionWS );
				half3 ViewDirWS = GetWorldSpaceNormalizeViewDir( PositionWS );
				float4 ShadowCoord = shadowCoord;
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );
				float3 TangentWS = input.tangentWS.xyz * renormFactor;
				float3 BitangentWS = cross( input.normalWS, input.tangentWS.xyz ) * input.tangentWS.w * renormFactor;
				float3 NormalWS = input.normalWS * renormFactor;

				float temp_output_21_0_g63750 = radians( _OverlayRotation );
				float temp_output_22_0_g63750 = cos( temp_output_21_0_g63750 );
				float temp_output_25_0_g63750 = sin( temp_output_21_0_g63750 );
				float2 appendResult24_g63750 = (float2(temp_output_22_0_g63750 , -temp_output_25_0_g63750));
				float2 appendResult26_g63750 = (float2(temp_output_25_0_g63750 , temp_output_22_0_g63750));
				float2 temp_output_32_0_g63750 = float2( 0.5,0.5 );
				float temp_output_74_0_g63747 = ( _Flipbook.x * _Flipbook.y );
				float temp_output_53_0_g63747 = ( temp_output_74_0_g63747 * input.ase_texcoord3.w );
				#ifdef _USEMESHINDEX_ON
				float staticSwitch85_g63747 = input.ase_texcoord4.z;
				#else
				float staticSwitch85_g63747 = temp_output_53_0_g63747;
				#endif
				float temp_output_87_0_g63747 = input.ase_texcoord3.z;
				float temp_output_82_0_g63747 = ( _AnimationSpeed * temp_output_87_0_g63747 );
				#ifdef _RANDOMSTARTFRAME_ON
				float staticSwitch84_g63747 = ( temp_output_82_0_g63747 + temp_output_53_0_g63747 );
				#else
				float staticSwitch84_g63747 = temp_output_82_0_g63747;
				#endif
				#ifdef _ANIMATE_ON
				float staticSwitch76_g63747 = ( temp_output_74_0_g63747 * staticSwitch84_g63747 );
				#else
				float staticSwitch76_g63747 = staticSwitch85_g63747;
				#endif
				// *** BEGIN Flipbook UV Animation vars ***
				// Total tiles of Flipbook Texture
				float fbtotaltiles10_g63747 = _Flipbook.x * _Flipbook.y;
				// Offsets for cols and rows of Flipbook Texture
				float fbcolsoffset10_g63747 = 1.0f / _Flipbook.x;
				float fbrowsoffset10_g63747 = 1.0f / _Flipbook.y;
				// Speed of animation
				float fbspeed10_g63747 = _Time[ 1 ] * 0.0;
				// UV Tiling (col and row offset)
				float2 fbtiling10_g63747 = float2(fbcolsoffset10_g63747, fbrowsoffset10_g63747);
				// UV Offset - calculate current tile linear index, and convert it to (X * coloffset, Y * rowoffset)
				// Calculate current tile linear index
				float fbcurrenttileindex10_g63747 = floor( fmod( fbspeed10_g63747 + ( staticSwitch76_g63747 + 0.01 ), fbtotaltiles10_g63747) );
				fbcurrenttileindex10_g63747 += ( fbcurrenttileindex10_g63747 < 0) ? fbtotaltiles10_g63747 : 0;
				// Obtain Offset X coordinate from current tile linear index
				float fblinearindextox10_g63747 = round ( fmod ( fbcurrenttileindex10_g63747, _Flipbook.x ) );
				// Multiply Offset X by coloffset
				float fboffsetx10_g63747 = fblinearindextox10_g63747 * fbcolsoffset10_g63747;
				// Obtain Offset Y coordinate from current tile linear index
				float fblinearindextoy10_g63747 = round( fmod( ( fbcurrenttileindex10_g63747 - fblinearindextox10_g63747 ) / _Flipbook.x, _Flipbook.y ) );
				// Reverse Y to get tiles from Top to Bottom
				fblinearindextoy10_g63747 = (int)(_Flipbook.y-1) - fblinearindextoy10_g63747;
				// Multiply Offset Y by rowoffset
				float fboffsety10_g63747 = fblinearindextoy10_g63747 * fbrowsoffset10_g63747;
				// UV Offset
				float2 fboffset10_g63747 = float2(fboffsetx10_g63747, fboffsety10_g63747);
				// Flipbook UV
				float2 fbuv10_g63747 = input.ase_texcoord3.xy * fbtiling10_g63747 + fboffset10_g63747;
				// *** END Flipbook UV Animation vars ***
				int flipbookFrame10_g63747 = ( ( int )fbcurrenttileindex10_g63747);
				float4 tex2DNode17_g63747 = tex2D( _Albedo, fbuv10_g63747 );
				float smoothstepResult46_g63747 = smoothstep( temp_output_87_0_g63747 , 1.0 , tex2D( _Dissolve, fbuv10_g63747 ).a);
				float4 color62_g63747 = IsGammaSpace() ? float4( 1, 1, 1, 1 ) : float4( 1, 1, 1, 1 );
				float4 temp_output_61_0_g63747 = color62_g63747;
				float temp_output_9_0_g63751 = ( tex2DNode17_g63747.a * smoothstepResult46_g63747 * (temp_output_61_0_g63747).a * (input.ase_texcoord5).w );
				float temp_output_14_0_g63752 = ( temp_output_9_0_g63751 * input.ase_color.a );
				float Mobile13_g63752 = temp_output_14_0_g63752;
				float webgl4_g63754 = 1.0;
				float screenDepth2_g63754 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ),_ZBufferParams);
				float distanceDepth2_g63754 = saturate( ( screenDepth2_g63754 - LinearEyeDepth( ScreenPosNorm.z,_ZBufferParams ) ) / ( _SoftParticles ) );
				float d4_g63754 = saturate( distanceDepth2_g63754 );
				float localSwitchWebGL4_g63754 = SwitchWebGL( webgl4_g63754 , d4_g63754 );
				float4 ase_positionSS_Pixel = ASEScreenPositionNormalizedToPixel( ScreenPosNorm, _ScaledScreenParams );
				float4 ditherScreenPos15_g63753 = ( ase_positionSS_Pixel * float4( 0.5,0.5,0.5,0.5 ) );
				float2 ditherScreenPosPixel15_g63753 = ditherScreenPos15_g63753.xy * _ScaledScreenParams.xy;
				float dither15_g63753 = Dither4x4Bayer( fmod( ditherScreenPosPixel15_g63753.x, 4 ), fmod( ditherScreenPosPixel15_g63753.y, 4 ) );
				float temp_output_17_0_g63753 = _Dither;
				float eyeDepth = input.ase_texcoord6.x;
				float cameraDepthFade10_g63753 = (( eyeDepth -_ProjectionParams.y - temp_output_17_0_g63753 ) / temp_output_17_0_g63753);
				dither15_g63753 = step( dither15_g63753, saturate( cameraDepthFade10_g63753 * 1.00001 ) );
				float lerpResult13_g63753 = lerp( 1.0 , dither15_g63753 , _DitherFade);
				float temp_output_15_0_g63752 = ( localSwitchWebGL4_g63754 * lerpResult13_g63753 * temp_output_9_0_g63751 * input.ase_color.a );
				float PC13_g63752 = temp_output_15_0_g63752;
				float localSwitch13_g63752 = Switch( Mobile13_g63752 , PC13_g63752 );
				float temp_output_269_0 = localSwitch13_g63752;
				float temp_output_20_0_g63749 = ( tex2D( _Overlay, ( mul( float2x2( appendResult24_g63750, appendResult26_g63750 ), ( (input.ase_texcoord3.xy*_OverlayTiling + ( ( ( _OverlayTiling - 1.0 ) / -2.0 ) + (_OverlayOffset).xy )) - temp_output_32_0_g63750 ) ) + temp_output_32_0_g63750 ) ).a * _OverlayColor.a * temp_output_269_0 );
				
				float3 BakedAlbedo = 0;
				float3 BakedEmission = 0;
				float3 Color = ( float4( ( temp_output_20_0_g63749 * _OverlayColor.rgb ) , 0.0 ) + ( ( 1.0 - temp_output_20_0_g63749 ) * float4( ( (input.ase_color).rgb * tex2DNode17_g63747.rgb * (input.ase_texcoord5).xyz * (temp_output_61_0_g63747).rgb ) , 0.0 ) ) ).xyz;
				float Alpha = temp_output_269_0;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
					float AlphaClipThresholdShadow = 0.5;
				#endif


				#if defined( ASE_DEPTH_WRITE_ON )
					input.positionCS.z = input.positionCS.z;
				#endif

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_CHANGES_WORLD_POS)
					ShadowCoord = TransformWorldToShadowCoord( PositionWS );
				#endif

				InputData inputData = (InputData)0;
				inputData.positionWS = PositionWS;
				inputData.positionCS = input.positionCS;
				inputData.normalizedScreenSpaceUV = ScreenPosNorm.xy;
				inputData.normalWS = NormalWS;
				inputData.viewDirectionWS = ViewDirWS;

				#if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT) && defined(UNLIT_DEFAULT_SSAO)
					float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
					AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(normalizedScreenSpaceUV);
					Color.rgb *= aoFactor.directAmbientOcclusion;
				#endif

				#ifdef ASE_FOG
					inputData.fogCoord = InitializeInputDataFog(float4(inputData.positionWS, 1.0), input.positionWSAndFogFactor.w);
				#endif

				#if defined(_DBUFFER) && defined(UNLIT_DEFAULT_DECAL_BLENDING)
					ApplyDecalToBaseColor(input.positionCS, Color);
				#endif

				#ifdef ASE_FOG
					#ifdef TERRAIN_SPLAT_ADDPASS
						Color.rgb = MixFogColor(Color.rgb, half3(0,0,0), inputData.fogCoord);
					#else
						Color.rgb = MixFog(Color.rgb, inputData.fogCoord);
					#endif
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					outputDepth = input.positionCS.z;
				#endif

				#ifdef _WRITE_RENDERING_LAYERS
					#if ( UNITY_VERSION >= 60020000 )
					outRenderingLayers = EncodeMeshRenderingLayer();
					#else
					uint renderingLayers = GetMeshRenderingLayer();
					outRenderingLayers = float4( EncodeMeshRenderingLayer( renderingLayers ), 0, 0, 0 );
					#endif
				#endif

				#if defined( ASE_OPAQUE_KEEP_ALPHA )
					return half4( Color, Alpha );
				#else
					return half4( Color, OutputAlpha( Alpha, isTransparent ) );
				#endif
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

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _ALPHATEST_ON
			#pragma multi_compile_instancing
			#pragma multi_compile _ LOD_FADE_CROSSFADE
			#define _RECEIVE_SHADOWS_OFF
			#define ASE_VERSION 19909
			#define ASE_SRP_VERSION 170004
			#define REQUIRE_DEPTH_TEXTURE 1


			#pragma vertex vert
			#pragma fragment frag

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #if UNITY_2023_1_OR_NEWER
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #endif
            #if UNITY_2023_1_OR_NEWER
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
            #endif
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #if UNITY_2023_1_OR_NEWER
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
            #endif
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#include "Sub/HLSL/MobileSwitch.hlsl"
			#include "Sub/HLSL/WebGLSwitch.hlsl"
			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_TEXTURE_COORDINATES2
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES2
			#define ASE_NEEDS_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED
			#define ASE_NEEDS_VERT_POSITION
			#define ASE_NEEDS_FRAG_COLOR
			#pragma shader_feature_local _ANIMATE_ON
			#pragma shader_feature_local _USEMESHINDEX_ON
			#pragma shader_feature_local _RANDOMSTARTFRAME_ON
			#pragma multi_compile __ CTFX_IS_MOBILE_SHADER
			#pragma multi_compile __ CTFX_IS_WEBGL_SHADER


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
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_color : COLOR;
				float4 ase_texcoord3 : TEXCOORD3;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _OverlayOffset;
			float4 _OverlayColor;
			float2 _Flipbook;
			float _OverlayRotation;
			float _OverlayTiling;
			float _AnimationSpeed;
			float _SoftParticles;
			float _Dither;
			float _DitherFade;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _Albedo;
			sampler2D _Dissolve;


			float4 ASEScreenPositionNormalizedToPixel( float4 screenPosNorm, float4 screenParams )
			{
				float4 screenPosPixel = screenPosNorm * float4( screenParams.xy, 1, 1 );
				#if UNITY_UV_STARTS_AT_TOP
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x < 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#else
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x > 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#endif
				return screenPosPixel;
			}
			
			inline float Dither4x4Bayer( uint x, uint y )
			{
				const float dither[ 16 ] = {
				     1,  9,  3, 11,
				    13,  5, 15,  7,
				     4, 12,  2, 10,
				    16,  8, 14,  6 };
				uint r = y * 4 + x;
				return dither[ min( r, 15 ) ] / 16; // same # of instructions as pre-dividing due to compiler magic
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float3 objectToViewPos = TransformWorldToView( TransformObjectToWorld( input.positionOS.xyz ) );
				float eyeDepth = -objectToViewPos.z;
				output.ase_texcoord3.x = eyeDepth;
				
				output.ase_texcoord = input.ase_texcoord;
				output.ase_texcoord1 = input.ase_texcoord2;
				output.ase_texcoord2 = input.ase_texcoord1;
				output.ase_color = input.ase_color;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord3.yzw = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = vertexInput.positionCS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_color : COLOR;

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
				output.ase_texcoord2 = input.ase_texcoord2;
				output.ase_texcoord1 = input.ase_texcoord1;
				output.ase_color = input.ase_color;
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
				output.ase_texcoord2 = patch[0].ase_texcoord2 * bary.x + patch[1].ase_texcoord2 * bary.y + patch[2].ase_texcoord2 * bary.z;
				output.ase_texcoord1 = patch[0].ase_texcoord1 * bary.x + patch[1].ase_texcoord1 * bary.y + patch[2].ase_texcoord1 * bary.z;
				output.ase_color = patch[0].ase_color * bary.x + patch[1].ase_color * bary.y + patch[2].ase_color * bary.z;
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
						#if defined( ASE_DEPTH_WRITE_ON )
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						 ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );

				float temp_output_74_0_g63747 = ( _Flipbook.x * _Flipbook.y );
				float temp_output_53_0_g63747 = ( temp_output_74_0_g63747 * input.ase_texcoord.w );
				#ifdef _USEMESHINDEX_ON
				float staticSwitch85_g63747 = input.ase_texcoord1.z;
				#else
				float staticSwitch85_g63747 = temp_output_53_0_g63747;
				#endif
				float temp_output_87_0_g63747 = input.ase_texcoord.z;
				float temp_output_82_0_g63747 = ( _AnimationSpeed * temp_output_87_0_g63747 );
				#ifdef _RANDOMSTARTFRAME_ON
				float staticSwitch84_g63747 = ( temp_output_82_0_g63747 + temp_output_53_0_g63747 );
				#else
				float staticSwitch84_g63747 = temp_output_82_0_g63747;
				#endif
				#ifdef _ANIMATE_ON
				float staticSwitch76_g63747 = ( temp_output_74_0_g63747 * staticSwitch84_g63747 );
				#else
				float staticSwitch76_g63747 = staticSwitch85_g63747;
				#endif
				// *** BEGIN Flipbook UV Animation vars ***
				// Total tiles of Flipbook Texture
				float fbtotaltiles10_g63747 = _Flipbook.x * _Flipbook.y;
				// Offsets for cols and rows of Flipbook Texture
				float fbcolsoffset10_g63747 = 1.0f / _Flipbook.x;
				float fbrowsoffset10_g63747 = 1.0f / _Flipbook.y;
				// Speed of animation
				float fbspeed10_g63747 = _Time[ 1 ] * 0.0;
				// UV Tiling (col and row offset)
				float2 fbtiling10_g63747 = float2(fbcolsoffset10_g63747, fbrowsoffset10_g63747);
				// UV Offset - calculate current tile linear index, and convert it to (X * coloffset, Y * rowoffset)
				// Calculate current tile linear index
				float fbcurrenttileindex10_g63747 = floor( fmod( fbspeed10_g63747 + ( staticSwitch76_g63747 + 0.01 ), fbtotaltiles10_g63747) );
				fbcurrenttileindex10_g63747 += ( fbcurrenttileindex10_g63747 < 0) ? fbtotaltiles10_g63747 : 0;
				// Obtain Offset X coordinate from current tile linear index
				float fblinearindextox10_g63747 = round ( fmod ( fbcurrenttileindex10_g63747, _Flipbook.x ) );
				// Multiply Offset X by coloffset
				float fboffsetx10_g63747 = fblinearindextox10_g63747 * fbcolsoffset10_g63747;
				// Obtain Offset Y coordinate from current tile linear index
				float fblinearindextoy10_g63747 = round( fmod( ( fbcurrenttileindex10_g63747 - fblinearindextox10_g63747 ) / _Flipbook.x, _Flipbook.y ) );
				// Reverse Y to get tiles from Top to Bottom
				fblinearindextoy10_g63747 = (int)(_Flipbook.y-1) - fblinearindextoy10_g63747;
				// Multiply Offset Y by rowoffset
				float fboffsety10_g63747 = fblinearindextoy10_g63747 * fbrowsoffset10_g63747;
				// UV Offset
				float2 fboffset10_g63747 = float2(fboffsetx10_g63747, fboffsety10_g63747);
				// Flipbook UV
				float2 fbuv10_g63747 = input.ase_texcoord.xy * fbtiling10_g63747 + fboffset10_g63747;
				// *** END Flipbook UV Animation vars ***
				int flipbookFrame10_g63747 = ( ( int )fbcurrenttileindex10_g63747);
				float4 tex2DNode17_g63747 = tex2D( _Albedo, fbuv10_g63747 );
				float smoothstepResult46_g63747 = smoothstep( temp_output_87_0_g63747 , 1.0 , tex2D( _Dissolve, fbuv10_g63747 ).a);
				float4 color62_g63747 = IsGammaSpace() ? float4( 1, 1, 1, 1 ) : float4( 1, 1, 1, 1 );
				float4 temp_output_61_0_g63747 = color62_g63747;
				float temp_output_9_0_g63751 = ( tex2DNode17_g63747.a * smoothstepResult46_g63747 * (temp_output_61_0_g63747).a * (input.ase_texcoord2).w );
				float temp_output_14_0_g63752 = ( temp_output_9_0_g63751 * input.ase_color.a );
				float Mobile13_g63752 = temp_output_14_0_g63752;
				float webgl4_g63754 = 1.0;
				float screenDepth2_g63754 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ),_ZBufferParams);
				float distanceDepth2_g63754 = saturate( ( screenDepth2_g63754 - LinearEyeDepth( ScreenPosNorm.z,_ZBufferParams ) ) / ( _SoftParticles ) );
				float d4_g63754 = saturate( distanceDepth2_g63754 );
				float localSwitchWebGL4_g63754 = SwitchWebGL( webgl4_g63754 , d4_g63754 );
				float4 ase_positionSS_Pixel = ASEScreenPositionNormalizedToPixel( ScreenPosNorm, _ScaledScreenParams );
				float4 ditherScreenPos15_g63753 = ( ase_positionSS_Pixel * float4( 0.5,0.5,0.5,0.5 ) );
				float2 ditherScreenPosPixel15_g63753 = ditherScreenPos15_g63753.xy * _ScaledScreenParams.xy;
				float dither15_g63753 = Dither4x4Bayer( fmod( ditherScreenPosPixel15_g63753.x, 4 ), fmod( ditherScreenPosPixel15_g63753.y, 4 ) );
				float temp_output_17_0_g63753 = _Dither;
				float eyeDepth = input.ase_texcoord3.x;
				float cameraDepthFade10_g63753 = (( eyeDepth -_ProjectionParams.y - temp_output_17_0_g63753 ) / temp_output_17_0_g63753);
				dither15_g63753 = step( dither15_g63753, saturate( cameraDepthFade10_g63753 * 1.00001 ) );
				float lerpResult13_g63753 = lerp( 1.0 , dither15_g63753 , _DitherFade);
				float temp_output_15_0_g63752 = ( localSwitchWebGL4_g63754 * lerpResult13_g63753 * temp_output_9_0_g63751 * input.ase_color.a );
				float PC13_g63752 = temp_output_15_0_g63752;
				float localSwitch13_g63752 = Switch( Mobile13_g63752 , PC13_g63752 );
				float temp_output_269_0 = localSwitch13_g63752;
				

				float Alpha = temp_output_269_0;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					input.positionCS.z = input.positionCS.z;
				#endif

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					outputDepth = input.positionCS.z;
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

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _ALPHATEST_ON
			#define _RECEIVE_SHADOWS_OFF
			#define ASE_VERSION 19909
			#define ASE_SRP_VERSION 170004
			#define REQUIRE_DEPTH_TEXTURE 1


			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#if defined( _WRITE_RENDERING_LAYERS )
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#endif
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#if UNITY_2023_1_OR_NEWER
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
			#endif
            #if UNITY_2023_1_OR_NEWER
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
            #endif
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #if UNITY_2023_1_OR_NEWER
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
            #endif
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#include "Sub/HLSL/MobileSwitch.hlsl"
			#include "Sub/HLSL/WebGLSwitch.hlsl"
			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_TEXTURE_COORDINATES2
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES2
			#define ASE_NEEDS_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES1
			#define ASE_NEEDS_VERT_POSITION
			#define ASE_NEEDS_FRAG_COLOR
			#pragma shader_feature_local _ANIMATE_ON
			#pragma shader_feature_local _USEMESHINDEX_ON
			#pragma shader_feature_local _RANDOMSTARTFRAME_ON
			#pragma multi_compile __ CTFX_IS_MOBILE_SHADER
			#pragma multi_compile __ CTFX_IS_WEBGL_SHADER


			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_color : COLOR;
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_texcoord4 : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _OverlayOffset;
			float4 _OverlayColor;
			float2 _Flipbook;
			float _OverlayRotation;
			float _OverlayTiling;
			float _AnimationSpeed;
			float _SoftParticles;
			float _Dither;
			float _DitherFade;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _Albedo;
			sampler2D _Dissolve;


			float4 ASEScreenPositionNormalizedToPixel( float4 screenPosNorm, float4 screenParams )
			{
				float4 screenPosPixel = screenPosNorm * float4( screenParams.xy, 1, 1 );
				#if UNITY_UV_STARTS_AT_TOP
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x < 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#else
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x > 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#endif
				return screenPosPixel;
			}
			
			inline float Dither4x4Bayer( uint x, uint y )
			{
				const float dither[ 16 ] = {
				     1,  9,  3, 11,
				    13,  5, 15,  7,
				     4, 12,  2, 10,
				    16,  8, 14,  6 };
				uint r = y * 4 + x;
				return dither[ min( r, 15 ) ] / 16; // same # of instructions as pre-dividing due to compiler magic
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

				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				output.ase_texcoord3 = screenPos;
				float3 objectToViewPos = TransformWorldToView( TransformObjectToWorld( input.positionOS.xyz ) );
				float eyeDepth = -objectToViewPos.z;
				output.ase_texcoord4.x = eyeDepth;
				
				output.ase_texcoord = input.ase_texcoord;
				output.ase_texcoord1 = input.ase_texcoord2;
				output.ase_texcoord2 = input.ase_texcoord1;
				output.ase_color = input.ase_color;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord4.yzw = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = vertexInput.positionCS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_color : COLOR;

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
				output.ase_texcoord2 = input.ase_texcoord2;
				output.ase_texcoord1 = input.ase_texcoord1;
				output.ase_color = input.ase_color;
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
				output.ase_texcoord2 = patch[0].ase_texcoord2 * bary.x + patch[1].ase_texcoord2 * bary.y + patch[2].ase_texcoord2 * bary.z;
				output.ase_texcoord1 = patch[0].ase_texcoord1 * bary.x + patch[1].ase_texcoord1 * bary.y + patch[2].ase_texcoord1 * bary.z;
				output.ase_color = patch[0].ase_color * bary.x + patch[1].ase_color * bary.y + patch[2].ase_color * bary.z;
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

				float temp_output_74_0_g63747 = ( _Flipbook.x * _Flipbook.y );
				float temp_output_53_0_g63747 = ( temp_output_74_0_g63747 * input.ase_texcoord.w );
				#ifdef _USEMESHINDEX_ON
				float staticSwitch85_g63747 = input.ase_texcoord1.z;
				#else
				float staticSwitch85_g63747 = temp_output_53_0_g63747;
				#endif
				float temp_output_87_0_g63747 = input.ase_texcoord.z;
				float temp_output_82_0_g63747 = ( _AnimationSpeed * temp_output_87_0_g63747 );
				#ifdef _RANDOMSTARTFRAME_ON
				float staticSwitch84_g63747 = ( temp_output_82_0_g63747 + temp_output_53_0_g63747 );
				#else
				float staticSwitch84_g63747 = temp_output_82_0_g63747;
				#endif
				#ifdef _ANIMATE_ON
				float staticSwitch76_g63747 = ( temp_output_74_0_g63747 * staticSwitch84_g63747 );
				#else
				float staticSwitch76_g63747 = staticSwitch85_g63747;
				#endif
				// *** BEGIN Flipbook UV Animation vars ***
				// Total tiles of Flipbook Texture
				float fbtotaltiles10_g63747 = _Flipbook.x * _Flipbook.y;
				// Offsets for cols and rows of Flipbook Texture
				float fbcolsoffset10_g63747 = 1.0f / _Flipbook.x;
				float fbrowsoffset10_g63747 = 1.0f / _Flipbook.y;
				// Speed of animation
				float fbspeed10_g63747 = _Time[ 1 ] * 0.0;
				// UV Tiling (col and row offset)
				float2 fbtiling10_g63747 = float2(fbcolsoffset10_g63747, fbrowsoffset10_g63747);
				// UV Offset - calculate current tile linear index, and convert it to (X * coloffset, Y * rowoffset)
				// Calculate current tile linear index
				float fbcurrenttileindex10_g63747 = floor( fmod( fbspeed10_g63747 + ( staticSwitch76_g63747 + 0.01 ), fbtotaltiles10_g63747) );
				fbcurrenttileindex10_g63747 += ( fbcurrenttileindex10_g63747 < 0) ? fbtotaltiles10_g63747 : 0;
				// Obtain Offset X coordinate from current tile linear index
				float fblinearindextox10_g63747 = round ( fmod ( fbcurrenttileindex10_g63747, _Flipbook.x ) );
				// Multiply Offset X by coloffset
				float fboffsetx10_g63747 = fblinearindextox10_g63747 * fbcolsoffset10_g63747;
				// Obtain Offset Y coordinate from current tile linear index
				float fblinearindextoy10_g63747 = round( fmod( ( fbcurrenttileindex10_g63747 - fblinearindextox10_g63747 ) / _Flipbook.x, _Flipbook.y ) );
				// Reverse Y to get tiles from Top to Bottom
				fblinearindextoy10_g63747 = (int)(_Flipbook.y-1) - fblinearindextoy10_g63747;
				// Multiply Offset Y by rowoffset
				float fboffsety10_g63747 = fblinearindextoy10_g63747 * fbrowsoffset10_g63747;
				// UV Offset
				float2 fboffset10_g63747 = float2(fboffsetx10_g63747, fboffsety10_g63747);
				// Flipbook UV
				float2 fbuv10_g63747 = input.ase_texcoord.xy * fbtiling10_g63747 + fboffset10_g63747;
				// *** END Flipbook UV Animation vars ***
				int flipbookFrame10_g63747 = ( ( int )fbcurrenttileindex10_g63747);
				float4 tex2DNode17_g63747 = tex2D( _Albedo, fbuv10_g63747 );
				float smoothstepResult46_g63747 = smoothstep( temp_output_87_0_g63747 , 1.0 , tex2D( _Dissolve, fbuv10_g63747 ).a);
				float4 color62_g63747 = IsGammaSpace() ? float4( 1, 1, 1, 1 ) : float4( 1, 1, 1, 1 );
				float4 temp_output_61_0_g63747 = color62_g63747;
				float temp_output_9_0_g63751 = ( tex2DNode17_g63747.a * smoothstepResult46_g63747 * (temp_output_61_0_g63747).a * (input.ase_texcoord2).w );
				float temp_output_14_0_g63752 = ( temp_output_9_0_g63751 * input.ase_color.a );
				float Mobile13_g63752 = temp_output_14_0_g63752;
				float webgl4_g63754 = 1.0;
				float4 screenPos = input.ase_texcoord3;
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float screenDepth2_g63754 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth2_g63754 = saturate( ( screenDepth2_g63754 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _SoftParticles ) );
				float d4_g63754 = saturate( distanceDepth2_g63754 );
				float localSwitchWebGL4_g63754 = SwitchWebGL( webgl4_g63754 , d4_g63754 );
				float4 ase_positionSS_Pixel = ASEScreenPositionNormalizedToPixel( ase_positionSSNorm, _ScaledScreenParams );
				float4 ditherScreenPos15_g63753 = ( ase_positionSS_Pixel * float4( 0.5,0.5,0.5,0.5 ) );
				float2 ditherScreenPosPixel15_g63753 = ditherScreenPos15_g63753.xy * _ScaledScreenParams.xy;
				float dither15_g63753 = Dither4x4Bayer( fmod( ditherScreenPosPixel15_g63753.x, 4 ), fmod( ditherScreenPosPixel15_g63753.y, 4 ) );
				float temp_output_17_0_g63753 = _Dither;
				float eyeDepth = input.ase_texcoord4.x;
				float cameraDepthFade10_g63753 = (( eyeDepth -_ProjectionParams.y - temp_output_17_0_g63753 ) / temp_output_17_0_g63753);
				dither15_g63753 = step( dither15_g63753, saturate( cameraDepthFade10_g63753 * 1.00001 ) );
				float lerpResult13_g63753 = lerp( 1.0 , dither15_g63753 , _DitherFade);
				float temp_output_15_0_g63752 = ( localSwitchWebGL4_g63754 * lerpResult13_g63753 * temp_output_9_0_g63751 * input.ase_color.a );
				float PC13_g63752 = temp_output_15_0_g63752;
				float localSwitch13_g63752 = Switch( Mobile13_g63752 , PC13_g63752 );
				float temp_output_269_0 = localSwitch13_g63752;
				

				surfaceDescription.Alpha = temp_output_269_0;
				#if defined( _ALPHATEST_ON )
					surfaceDescription.AlphaClipThreshold = _Cutoff;
				#endif

				#ifdef _ALPHATEST_ON
					clip(surfaceDescription.Alpha - surfaceDescription.AlphaClipThreshold);
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

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _ALPHATEST_ON
			#define _RECEIVE_SHADOWS_OFF
			#define ASE_VERSION 19909
			#define ASE_SRP_VERSION 170004
			#define REQUIRE_DEPTH_TEXTURE 1


			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT

			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#if defined( _WRITE_RENDERING_LAYERS )
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#endif
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#if UNITY_2023_1_OR_NEWER
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
			#endif
            #if UNITY_2023_1_OR_NEWER
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
            #endif
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #if UNITY_2023_1_OR_NEWER
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
            #endif
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#include "Sub/HLSL/MobileSwitch.hlsl"
			#include "Sub/HLSL/WebGLSwitch.hlsl"
			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_TEXTURE_COORDINATES2
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES2
			#define ASE_NEEDS_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES1
			#define ASE_NEEDS_VERT_POSITION
			#define ASE_NEEDS_FRAG_COLOR
			#pragma shader_feature_local _ANIMATE_ON
			#pragma shader_feature_local _USEMESHINDEX_ON
			#pragma shader_feature_local _RANDOMSTARTFRAME_ON
			#pragma multi_compile __ CTFX_IS_MOBILE_SHADER
			#pragma multi_compile __ CTFX_IS_WEBGL_SHADER


			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_color : COLOR;
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_texcoord4 : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _OverlayOffset;
			float4 _OverlayColor;
			float2 _Flipbook;
			float _OverlayRotation;
			float _OverlayTiling;
			float _AnimationSpeed;
			float _SoftParticles;
			float _Dither;
			float _DitherFade;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _Albedo;
			sampler2D _Dissolve;


			float4 ASEScreenPositionNormalizedToPixel( float4 screenPosNorm, float4 screenParams )
			{
				float4 screenPosPixel = screenPosNorm * float4( screenParams.xy, 1, 1 );
				#if UNITY_UV_STARTS_AT_TOP
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x < 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#else
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x > 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#endif
				return screenPosPixel;
			}
			
			inline float Dither4x4Bayer( uint x, uint y )
			{
				const float dither[ 16 ] = {
				     1,  9,  3, 11,
				    13,  5, 15,  7,
				     4, 12,  2, 10,
				    16,  8, 14,  6 };
				uint r = y * 4 + x;
				return dither[ min( r, 15 ) ] / 16; // same # of instructions as pre-dividing due to compiler magic
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

				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				output.ase_texcoord3 = screenPos;
				float3 objectToViewPos = TransformWorldToView( TransformObjectToWorld( input.positionOS.xyz ) );
				float eyeDepth = -objectToViewPos.z;
				output.ase_texcoord4.x = eyeDepth;
				
				output.ase_texcoord = input.ase_texcoord;
				output.ase_texcoord1 = input.ase_texcoord2;
				output.ase_texcoord2 = input.ase_texcoord1;
				output.ase_color = input.ase_color;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord4.yzw = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = vertexInput.positionCS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_color : COLOR;

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
				output.ase_texcoord2 = input.ase_texcoord2;
				output.ase_texcoord1 = input.ase_texcoord1;
				output.ase_color = input.ase_color;
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
				output.ase_texcoord2 = patch[0].ase_texcoord2 * bary.x + patch[1].ase_texcoord2 * bary.y + patch[2].ase_texcoord2 * bary.z;
				output.ase_texcoord1 = patch[0].ase_texcoord1 * bary.x + patch[1].ase_texcoord1 * bary.y + patch[2].ase_texcoord1 * bary.z;
				output.ase_color = patch[0].ase_color * bary.x + patch[1].ase_color * bary.y + patch[2].ase_color * bary.z;
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

				float temp_output_74_0_g63747 = ( _Flipbook.x * _Flipbook.y );
				float temp_output_53_0_g63747 = ( temp_output_74_0_g63747 * input.ase_texcoord.w );
				#ifdef _USEMESHINDEX_ON
				float staticSwitch85_g63747 = input.ase_texcoord1.z;
				#else
				float staticSwitch85_g63747 = temp_output_53_0_g63747;
				#endif
				float temp_output_87_0_g63747 = input.ase_texcoord.z;
				float temp_output_82_0_g63747 = ( _AnimationSpeed * temp_output_87_0_g63747 );
				#ifdef _RANDOMSTARTFRAME_ON
				float staticSwitch84_g63747 = ( temp_output_82_0_g63747 + temp_output_53_0_g63747 );
				#else
				float staticSwitch84_g63747 = temp_output_82_0_g63747;
				#endif
				#ifdef _ANIMATE_ON
				float staticSwitch76_g63747 = ( temp_output_74_0_g63747 * staticSwitch84_g63747 );
				#else
				float staticSwitch76_g63747 = staticSwitch85_g63747;
				#endif
				// *** BEGIN Flipbook UV Animation vars ***
				// Total tiles of Flipbook Texture
				float fbtotaltiles10_g63747 = _Flipbook.x * _Flipbook.y;
				// Offsets for cols and rows of Flipbook Texture
				float fbcolsoffset10_g63747 = 1.0f / _Flipbook.x;
				float fbrowsoffset10_g63747 = 1.0f / _Flipbook.y;
				// Speed of animation
				float fbspeed10_g63747 = _Time[ 1 ] * 0.0;
				// UV Tiling (col and row offset)
				float2 fbtiling10_g63747 = float2(fbcolsoffset10_g63747, fbrowsoffset10_g63747);
				// UV Offset - calculate current tile linear index, and convert it to (X * coloffset, Y * rowoffset)
				// Calculate current tile linear index
				float fbcurrenttileindex10_g63747 = floor( fmod( fbspeed10_g63747 + ( staticSwitch76_g63747 + 0.01 ), fbtotaltiles10_g63747) );
				fbcurrenttileindex10_g63747 += ( fbcurrenttileindex10_g63747 < 0) ? fbtotaltiles10_g63747 : 0;
				// Obtain Offset X coordinate from current tile linear index
				float fblinearindextox10_g63747 = round ( fmod ( fbcurrenttileindex10_g63747, _Flipbook.x ) );
				// Multiply Offset X by coloffset
				float fboffsetx10_g63747 = fblinearindextox10_g63747 * fbcolsoffset10_g63747;
				// Obtain Offset Y coordinate from current tile linear index
				float fblinearindextoy10_g63747 = round( fmod( ( fbcurrenttileindex10_g63747 - fblinearindextox10_g63747 ) / _Flipbook.x, _Flipbook.y ) );
				// Reverse Y to get tiles from Top to Bottom
				fblinearindextoy10_g63747 = (int)(_Flipbook.y-1) - fblinearindextoy10_g63747;
				// Multiply Offset Y by rowoffset
				float fboffsety10_g63747 = fblinearindextoy10_g63747 * fbrowsoffset10_g63747;
				// UV Offset
				float2 fboffset10_g63747 = float2(fboffsetx10_g63747, fboffsety10_g63747);
				// Flipbook UV
				float2 fbuv10_g63747 = input.ase_texcoord.xy * fbtiling10_g63747 + fboffset10_g63747;
				// *** END Flipbook UV Animation vars ***
				int flipbookFrame10_g63747 = ( ( int )fbcurrenttileindex10_g63747);
				float4 tex2DNode17_g63747 = tex2D( _Albedo, fbuv10_g63747 );
				float smoothstepResult46_g63747 = smoothstep( temp_output_87_0_g63747 , 1.0 , tex2D( _Dissolve, fbuv10_g63747 ).a);
				float4 color62_g63747 = IsGammaSpace() ? float4( 1, 1, 1, 1 ) : float4( 1, 1, 1, 1 );
				float4 temp_output_61_0_g63747 = color62_g63747;
				float temp_output_9_0_g63751 = ( tex2DNode17_g63747.a * smoothstepResult46_g63747 * (temp_output_61_0_g63747).a * (input.ase_texcoord2).w );
				float temp_output_14_0_g63752 = ( temp_output_9_0_g63751 * input.ase_color.a );
				float Mobile13_g63752 = temp_output_14_0_g63752;
				float webgl4_g63754 = 1.0;
				float4 screenPos = input.ase_texcoord3;
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float screenDepth2_g63754 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_positionSSNorm.xy ),_ZBufferParams);
				float distanceDepth2_g63754 = saturate( ( screenDepth2_g63754 - LinearEyeDepth( ase_positionSSNorm.z,_ZBufferParams ) ) / ( _SoftParticles ) );
				float d4_g63754 = saturate( distanceDepth2_g63754 );
				float localSwitchWebGL4_g63754 = SwitchWebGL( webgl4_g63754 , d4_g63754 );
				float4 ase_positionSS_Pixel = ASEScreenPositionNormalizedToPixel( ase_positionSSNorm, _ScaledScreenParams );
				float4 ditherScreenPos15_g63753 = ( ase_positionSS_Pixel * float4( 0.5,0.5,0.5,0.5 ) );
				float2 ditherScreenPosPixel15_g63753 = ditherScreenPos15_g63753.xy * _ScaledScreenParams.xy;
				float dither15_g63753 = Dither4x4Bayer( fmod( ditherScreenPosPixel15_g63753.x, 4 ), fmod( ditherScreenPosPixel15_g63753.y, 4 ) );
				float temp_output_17_0_g63753 = _Dither;
				float eyeDepth = input.ase_texcoord4.x;
				float cameraDepthFade10_g63753 = (( eyeDepth -_ProjectionParams.y - temp_output_17_0_g63753 ) / temp_output_17_0_g63753);
				dither15_g63753 = step( dither15_g63753, saturate( cameraDepthFade10_g63753 * 1.00001 ) );
				float lerpResult13_g63753 = lerp( 1.0 , dither15_g63753 , _DitherFade);
				float temp_output_15_0_g63752 = ( localSwitchWebGL4_g63754 * lerpResult13_g63753 * temp_output_9_0_g63751 * input.ase_color.a );
				float PC13_g63752 = temp_output_15_0_g63752;
				float localSwitch13_g63752 = Switch( Mobile13_g63752 , PC13_g63752 );
				float temp_output_269_0 = localSwitch13_g63752;
				

				surfaceDescription.Alpha = temp_output_269_0;
				#if defined( _ALPHATEST_ON )
					surfaceDescription.AlphaClipThreshold = _Cutoff;
				#endif

				#ifdef _ALPHATEST_ON
					clip(surfaceDescription.Alpha - surfaceDescription.AlphaClipThreshold);
				#endif

				half4 outColor = 0;
				outColor = unity_SelectionID;

				return outColor;
			}

			ENDHLSL
		}

		
		Pass
		{
			
			Name "DepthNormals"
			Tags { "LightMode"="DepthNormalsOnly" }

			ZTest LEqual
			ZWrite On

			HLSLPROGRAM

        	#define _SURFACE_TYPE_TRANSPARENT 1
        	#define _ALPHATEST_ON
        	#pragma multi_compile_instancing
        	#pragma multi_compile _ LOD_FADE_CROSSFADE
        	#define _RECEIVE_SHADOWS_OFF
        	#define ASE_VERSION 19909
        	#define ASE_SRP_VERSION 170004
        	#define REQUIRE_DEPTH_TEXTURE 1


        	#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define VARYINGS_NEED_NORMAL_WS

			#define SHADERPASS SHADERPASS_DEPTHNORMALSONLY

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#if defined( _WRITE_RENDERING_LAYERS )
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#endif
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#if UNITY_2023_1_OR_NEWER
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
			#endif
            #if UNITY_2023_1_OR_NEWER
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
            #endif
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#if UNITY_2023_1_OR_NEWER
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#endif
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#include "Sub/HLSL/MobileSwitch.hlsl"
			#include "Sub/HLSL/WebGLSwitch.hlsl"
			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_TEXTURE_COORDINATES2
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES2
			#define ASE_NEEDS_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED
			#define ASE_NEEDS_VERT_POSITION
			#define ASE_NEEDS_FRAG_COLOR
			#pragma shader_feature_local _ANIMATE_ON
			#pragma shader_feature_local _USEMESHINDEX_ON
			#pragma shader_feature_local _RANDOMSTARTFRAME_ON
			#pragma multi_compile __ CTFX_IS_MOBILE_SHADER
			#pragma multi_compile __ CTFX_IS_WEBGL_SHADER


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
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				half3 normalWS : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_color : COLOR;
				float4 ase_texcoord4 : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _OverlayOffset;
			float4 _OverlayColor;
			float2 _Flipbook;
			float _OverlayRotation;
			float _OverlayTiling;
			float _AnimationSpeed;
			float _SoftParticles;
			float _Dither;
			float _DitherFade;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			sampler2D _Albedo;
			sampler2D _Dissolve;


			float4 ASEScreenPositionNormalizedToPixel( float4 screenPosNorm, float4 screenParams )
			{
				float4 screenPosPixel = screenPosNorm * float4( screenParams.xy, 1, 1 );
				#if UNITY_UV_STARTS_AT_TOP
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x < 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#else
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x > 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#endif
				return screenPosPixel;
			}
			
			inline float Dither4x4Bayer( uint x, uint y )
			{
				const float dither[ 16 ] = {
				     1,  9,  3, 11,
				    13,  5, 15,  7,
				     4, 12,  2, 10,
				    16,  8, 14,  6 };
				uint r = y * 4 + x;
				return dither[ min( r, 15 ) ] / 16; // same # of instructions as pre-dividing due to compiler magic
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

				float3 objectToViewPos = TransformWorldToView( TransformObjectToWorld( input.positionOS.xyz ) );
				float eyeDepth = -objectToViewPos.z;
				output.ase_texcoord4.x = eyeDepth;
				
				output.ase_texcoord1 = input.ase_texcoord;
				output.ase_texcoord2 = input.ase_texcoord2;
				output.ase_texcoord3 = input.ase_texcoord1;
				output.ase_color = input.ase_color;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord4.yzw = 0;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );
				VertexNormalInputs normalInput = GetVertexNormalInputs( input.normalOS );

				output.positionCS = vertexInput.positionCS;
				output.normalWS = normalInput.normalWS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_color : COLOR;

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
				output.ase_texcoord2 = input.ase_texcoord2;
				output.ase_texcoord1 = input.ase_texcoord1;
				output.ase_color = input.ase_color;
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
				output.ase_texcoord2 = patch[0].ase_texcoord2 * bary.x + patch[1].ase_texcoord2 * bary.y + patch[2].ase_texcoord2 * bary.z;
				output.ase_texcoord1 = patch[0].ase_texcoord1 * bary.x + patch[1].ase_texcoord1 * bary.y + patch[2].ase_texcoord1 * bary.z;
				output.ase_color = patch[0].ase_color * bary.x + patch[1].ase_color * bary.y + patch[2].ase_color * bary.z;
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
						#if defined( ASE_DEPTH_WRITE_ON )
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						#ifdef _WRITE_RENDERING_LAYERS
						#if ( UNITY_VERSION >= 60020000 )
						, out uint outRenderingLayers : SV_Target1
						#else
						, out float4 outRenderingLayers : SV_Target1
						#endif
						#endif
						 )
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				half3 NormalWS = normalize( input.normalWS );
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );

				float temp_output_74_0_g63747 = ( _Flipbook.x * _Flipbook.y );
				float temp_output_53_0_g63747 = ( temp_output_74_0_g63747 * input.ase_texcoord1.w );
				#ifdef _USEMESHINDEX_ON
				float staticSwitch85_g63747 = input.ase_texcoord2.z;
				#else
				float staticSwitch85_g63747 = temp_output_53_0_g63747;
				#endif
				float temp_output_87_0_g63747 = input.ase_texcoord1.z;
				float temp_output_82_0_g63747 = ( _AnimationSpeed * temp_output_87_0_g63747 );
				#ifdef _RANDOMSTARTFRAME_ON
				float staticSwitch84_g63747 = ( temp_output_82_0_g63747 + temp_output_53_0_g63747 );
				#else
				float staticSwitch84_g63747 = temp_output_82_0_g63747;
				#endif
				#ifdef _ANIMATE_ON
				float staticSwitch76_g63747 = ( temp_output_74_0_g63747 * staticSwitch84_g63747 );
				#else
				float staticSwitch76_g63747 = staticSwitch85_g63747;
				#endif
				// *** BEGIN Flipbook UV Animation vars ***
				// Total tiles of Flipbook Texture
				float fbtotaltiles10_g63747 = _Flipbook.x * _Flipbook.y;
				// Offsets for cols and rows of Flipbook Texture
				float fbcolsoffset10_g63747 = 1.0f / _Flipbook.x;
				float fbrowsoffset10_g63747 = 1.0f / _Flipbook.y;
				// Speed of animation
				float fbspeed10_g63747 = _Time[ 1 ] * 0.0;
				// UV Tiling (col and row offset)
				float2 fbtiling10_g63747 = float2(fbcolsoffset10_g63747, fbrowsoffset10_g63747);
				// UV Offset - calculate current tile linear index, and convert it to (X * coloffset, Y * rowoffset)
				// Calculate current tile linear index
				float fbcurrenttileindex10_g63747 = floor( fmod( fbspeed10_g63747 + ( staticSwitch76_g63747 + 0.01 ), fbtotaltiles10_g63747) );
				fbcurrenttileindex10_g63747 += ( fbcurrenttileindex10_g63747 < 0) ? fbtotaltiles10_g63747 : 0;
				// Obtain Offset X coordinate from current tile linear index
				float fblinearindextox10_g63747 = round ( fmod ( fbcurrenttileindex10_g63747, _Flipbook.x ) );
				// Multiply Offset X by coloffset
				float fboffsetx10_g63747 = fblinearindextox10_g63747 * fbcolsoffset10_g63747;
				// Obtain Offset Y coordinate from current tile linear index
				float fblinearindextoy10_g63747 = round( fmod( ( fbcurrenttileindex10_g63747 - fblinearindextox10_g63747 ) / _Flipbook.x, _Flipbook.y ) );
				// Reverse Y to get tiles from Top to Bottom
				fblinearindextoy10_g63747 = (int)(_Flipbook.y-1) - fblinearindextoy10_g63747;
				// Multiply Offset Y by rowoffset
				float fboffsety10_g63747 = fblinearindextoy10_g63747 * fbrowsoffset10_g63747;
				// UV Offset
				float2 fboffset10_g63747 = float2(fboffsetx10_g63747, fboffsety10_g63747);
				// Flipbook UV
				float2 fbuv10_g63747 = input.ase_texcoord1.xy * fbtiling10_g63747 + fboffset10_g63747;
				// *** END Flipbook UV Animation vars ***
				int flipbookFrame10_g63747 = ( ( int )fbcurrenttileindex10_g63747);
				float4 tex2DNode17_g63747 = tex2D( _Albedo, fbuv10_g63747 );
				float smoothstepResult46_g63747 = smoothstep( temp_output_87_0_g63747 , 1.0 , tex2D( _Dissolve, fbuv10_g63747 ).a);
				float4 color62_g63747 = IsGammaSpace() ? float4( 1, 1, 1, 1 ) : float4( 1, 1, 1, 1 );
				float4 temp_output_61_0_g63747 = color62_g63747;
				float temp_output_9_0_g63751 = ( tex2DNode17_g63747.a * smoothstepResult46_g63747 * (temp_output_61_0_g63747).a * (input.ase_texcoord3).w );
				float temp_output_14_0_g63752 = ( temp_output_9_0_g63751 * input.ase_color.a );
				float Mobile13_g63752 = temp_output_14_0_g63752;
				float webgl4_g63754 = 1.0;
				float screenDepth2_g63754 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ),_ZBufferParams);
				float distanceDepth2_g63754 = saturate( ( screenDepth2_g63754 - LinearEyeDepth( ScreenPosNorm.z,_ZBufferParams ) ) / ( _SoftParticles ) );
				float d4_g63754 = saturate( distanceDepth2_g63754 );
				float localSwitchWebGL4_g63754 = SwitchWebGL( webgl4_g63754 , d4_g63754 );
				float4 ase_positionSS_Pixel = ASEScreenPositionNormalizedToPixel( ScreenPosNorm, _ScaledScreenParams );
				float4 ditherScreenPos15_g63753 = ( ase_positionSS_Pixel * float4( 0.5,0.5,0.5,0.5 ) );
				float2 ditherScreenPosPixel15_g63753 = ditherScreenPos15_g63753.xy * _ScaledScreenParams.xy;
				float dither15_g63753 = Dither4x4Bayer( fmod( ditherScreenPosPixel15_g63753.x, 4 ), fmod( ditherScreenPosPixel15_g63753.y, 4 ) );
				float temp_output_17_0_g63753 = _Dither;
				float eyeDepth = input.ase_texcoord4.x;
				float cameraDepthFade10_g63753 = (( eyeDepth -_ProjectionParams.y - temp_output_17_0_g63753 ) / temp_output_17_0_g63753);
				dither15_g63753 = step( dither15_g63753, saturate( cameraDepthFade10_g63753 * 1.00001 ) );
				float lerpResult13_g63753 = lerp( 1.0 , dither15_g63753 , _DitherFade);
				float temp_output_15_0_g63752 = ( localSwitchWebGL4_g63754 * lerpResult13_g63753 * temp_output_9_0_g63751 * input.ase_color.a );
				float PC13_g63752 = temp_output_15_0_g63752;
				float localSwitch13_g63752 = Switch( Mobile13_g63752 , PC13_g63752 );
				float temp_output_269_0 = localSwitch13_g63752;
				

				float Alpha = temp_output_269_0;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					input.positionCS.z = input.positionCS.z;
				#endif

				#ifdef _ALPHATEST_ON
					clip(Alpha - AlphaClipThreshold);
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					outputDepth = input.positionCS.z;
				#endif

				#if defined(_GBUFFER_NORMALS_OCT)
					float2 octNormalWS = PackNormalOctQuadEncode(NormalWS);
					float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
					half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
					outNormalWS = half4(packedNormalWS, 0.0);
				#else
					outNormalWS = half4(NormalizeNormalPerPixel( NormalWS ), 0.0);
				#endif

				#ifdef _WRITE_RENDERING_LAYERS
					#if ( UNITY_VERSION >= 60020000 )
					outRenderingLayers = EncodeMeshRenderingLayer();
					#else
					uint renderingLayers = GetMeshRenderingLayer();
					outRenderingLayers = float4( EncodeMeshRenderingLayer( renderingLayers ), 0, 0, 0 );
					#endif
				#endif
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "GBuffer"
			Tags { "LightMode"="UniversalGBuffer" }

			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			ZWrite Off
			ZTest LEqual
			Offset 0 , 0
			ColorMask RGBA

			

			HLSLPROGRAM

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _ALPHATEST_ON
			#define UNLIT_DEFAULT_DECAL_BLENDING 1
			#define _RECEIVE_SHADOWS_OFF
			#define ASE_VERSION 19909
			#define ASE_SRP_VERSION 170004
			#define REQUIRE_DEPTH_TEXTURE 1


			// Deferred Rendering Path does not support the OpenGL-based graphics API:
			// Desktop OpenGL, OpenGL ES 3.0, WebGL 2.0.
			#pragma exclude_renderers glcore gles3 

			#pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
			#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
			#pragma multi_compile_fragment _ _RENDER_PASS_ENABLED

			#pragma vertex vert
			#pragma fragment frag

			#define SHADERPASS SHADERPASS_GBUFFER

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#if defined( _WRITE_RENDERING_LAYERS )
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#endif
			#if defined( _USE_APV_PROBE_OCCLUSION )
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
			#endif
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#if UNITY_2023_1_OR_NEWER
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
			#endif
            #if UNITY_2023_1_OR_NEWER
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
            #endif
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#if UNITY_2023_1_OR_NEWER
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#endif
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#if defined(UNITY_INSTANCING_ENABLED) && defined(_TERRAIN_INSTANCED_PERPIXEL_NORMAL)
				#define ENABLE_TERRAIN_PERPIXEL_NORMAL
			#endif

			#include "Sub/HLSL/MobileSwitch.hlsl"
			#include "Sub/HLSL/WebGLSwitch.hlsl"
			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_TEXTURE_COORDINATES2
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES2
			#define ASE_NEEDS_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED
			#define ASE_NEEDS_VERT_POSITION
			#define ASE_NEEDS_FRAG_COLOR
			#pragma shader_feature_local _ANIMATE_ON
			#pragma shader_feature_local _USEMESHINDEX_ON
			#pragma shader_feature_local _RANDOMSTARTFRAME_ON
			#pragma multi_compile __ CTFX_IS_MOBILE_SHADER
			#pragma multi_compile __ CTFX_IS_WEBGL_SHADER


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
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
				half4 tangentWS : TEXCOORD2;
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_texcoord4 : TEXCOORD4;
				float4 ase_texcoord5 : TEXCOORD5;
				float4 ase_color : COLOR;
				float4 ase_texcoord6 : TEXCOORD6;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _OverlayOffset;
			float4 _OverlayColor;
			float2 _Flipbook;
			float _OverlayRotation;
			float _OverlayTiling;
			float _AnimationSpeed;
			float _SoftParticles;
			float _Dither;
			float _DitherFade;
			float _AlphaClip;
			float _Cutoff;
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

			sampler2D _Overlay;
			sampler2D _Albedo;
			sampler2D _Dissolve;


			#if ( UNITY_VERSION >= 60010000 )
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutput.hlsl"
			#else
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
			#endif

			float4 ASEScreenPositionNormalizedToPixel( float4 screenPosNorm, float4 screenParams )
			{
				float4 screenPosPixel = screenPosNorm * float4( screenParams.xy, 1, 1 );
				#if UNITY_UV_STARTS_AT_TOP
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x < 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#else
					screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x > 0 ) ? screenParams.y - screenPosPixel.y : screenPosPixel.y );
				#endif
				return screenPosPixel;
			}
			
			inline float Dither4x4Bayer( uint x, uint y )
			{
				const float dither[ 16 ] = {
				     1,  9,  3, 11,
				    13,  5, 15,  7,
				     4, 12,  2, 10,
				    16,  8, 14,  6 };
				uint r = y * 4 + x;
				return dither[ min( r, 15 ) ] / 16; // same # of instructions as pre-dividing due to compiler magic
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float3 objectToViewPos = TransformWorldToView( TransformObjectToWorld( input.positionOS.xyz ) );
				float eyeDepth = -objectToViewPos.z;
				output.ase_texcoord6.x = eyeDepth;
				
				output.ase_texcoord3 = input.ase_texcoord;
				output.ase_texcoord4 = input.ase_texcoord2;
				output.ase_texcoord5 = input.ase_texcoord1;
				output.ase_color = input.ase_color;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord6.yzw = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );
				VertexNormalInputs normalInput = GetVertexNormalInputs( input.normalOS, input.tangentOS );

				output.positionCS = vertexInput.positionCS;
				output.positionWS = vertexInput.positionWS;
				output.normalWS = normalInput.normalWS;
				output.tangentWS = half4( normalInput.tangentWS, ( input.tangentOS.w > 0.0 ? 1.0 : -1.0 ) * GetOddNegativeScale() );;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_color : COLOR;

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
				output.tangentOS = input.tangentOS;
				output.ase_texcoord = input.ase_texcoord;
				output.ase_texcoord2 = input.ase_texcoord2;
				output.ase_texcoord1 = input.ase_texcoord1;
				output.ase_color = input.ase_color;
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
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				output.ase_texcoord2 = patch[0].ase_texcoord2 * bary.x + patch[1].ase_texcoord2 * bary.y + patch[2].ase_texcoord2 * bary.z;
				output.ase_texcoord1 = patch[0].ase_texcoord1 * bary.x + patch[1].ase_texcoord1 * bary.y + patch[2].ase_texcoord1 * bary.z;
				output.ase_color = patch[0].ase_color * bary.x + patch[1].ase_color * bary.y + patch[2].ase_color * bary.z;
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

		#if ( UNITY_VERSION >= 60010000 )
			GBufferFragOutput frag ( PackedVaryings input
		#else
			FragmentOutput frag ( PackedVaryings input
		#endif
								#if defined( ASE_DEPTH_WRITE_ON )
								,out float outputDepth : ASE_SV_DEPTH
								#endif
								 )
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				// @diogo: mikktspace compliant
				float renormFactor = 1.0 / max( FLT_MIN, length( input.normalWS ) );

				float3 PositionWS = input.positionWS;
				float3 ViewDirWS = GetWorldSpaceNormalizeViewDir( PositionWS );
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );
				float3 TangentWS = input.tangentWS.xyz * renormFactor;
				float3 BitangentWS = cross( input.normalWS, input.tangentWS.xyz ) * input.tangentWS.w * renormFactor;
				float3 NormalWS = input.normalWS * renormFactor;

				float temp_output_21_0_g63750 = radians( _OverlayRotation );
				float temp_output_22_0_g63750 = cos( temp_output_21_0_g63750 );
				float temp_output_25_0_g63750 = sin( temp_output_21_0_g63750 );
				float2 appendResult24_g63750 = (float2(temp_output_22_0_g63750 , -temp_output_25_0_g63750));
				float2 appendResult26_g63750 = (float2(temp_output_25_0_g63750 , temp_output_22_0_g63750));
				float2 temp_output_32_0_g63750 = float2( 0.5,0.5 );
				float temp_output_74_0_g63747 = ( _Flipbook.x * _Flipbook.y );
				float temp_output_53_0_g63747 = ( temp_output_74_0_g63747 * input.ase_texcoord3.w );
				#ifdef _USEMESHINDEX_ON
				float staticSwitch85_g63747 = input.ase_texcoord4.z;
				#else
				float staticSwitch85_g63747 = temp_output_53_0_g63747;
				#endif
				float temp_output_87_0_g63747 = input.ase_texcoord3.z;
				float temp_output_82_0_g63747 = ( _AnimationSpeed * temp_output_87_0_g63747 );
				#ifdef _RANDOMSTARTFRAME_ON
				float staticSwitch84_g63747 = ( temp_output_82_0_g63747 + temp_output_53_0_g63747 );
				#else
				float staticSwitch84_g63747 = temp_output_82_0_g63747;
				#endif
				#ifdef _ANIMATE_ON
				float staticSwitch76_g63747 = ( temp_output_74_0_g63747 * staticSwitch84_g63747 );
				#else
				float staticSwitch76_g63747 = staticSwitch85_g63747;
				#endif
				// *** BEGIN Flipbook UV Animation vars ***
				// Total tiles of Flipbook Texture
				float fbtotaltiles10_g63747 = _Flipbook.x * _Flipbook.y;
				// Offsets for cols and rows of Flipbook Texture
				float fbcolsoffset10_g63747 = 1.0f / _Flipbook.x;
				float fbrowsoffset10_g63747 = 1.0f / _Flipbook.y;
				// Speed of animation
				float fbspeed10_g63747 = _Time[ 1 ] * 0.0;
				// UV Tiling (col and row offset)
				float2 fbtiling10_g63747 = float2(fbcolsoffset10_g63747, fbrowsoffset10_g63747);
				// UV Offset - calculate current tile linear index, and convert it to (X * coloffset, Y * rowoffset)
				// Calculate current tile linear index
				float fbcurrenttileindex10_g63747 = floor( fmod( fbspeed10_g63747 + ( staticSwitch76_g63747 + 0.01 ), fbtotaltiles10_g63747) );
				fbcurrenttileindex10_g63747 += ( fbcurrenttileindex10_g63747 < 0) ? fbtotaltiles10_g63747 : 0;
				// Obtain Offset X coordinate from current tile linear index
				float fblinearindextox10_g63747 = round ( fmod ( fbcurrenttileindex10_g63747, _Flipbook.x ) );
				// Multiply Offset X by coloffset
				float fboffsetx10_g63747 = fblinearindextox10_g63747 * fbcolsoffset10_g63747;
				// Obtain Offset Y coordinate from current tile linear index
				float fblinearindextoy10_g63747 = round( fmod( ( fbcurrenttileindex10_g63747 - fblinearindextox10_g63747 ) / _Flipbook.x, _Flipbook.y ) );
				// Reverse Y to get tiles from Top to Bottom
				fblinearindextoy10_g63747 = (int)(_Flipbook.y-1) - fblinearindextoy10_g63747;
				// Multiply Offset Y by rowoffset
				float fboffsety10_g63747 = fblinearindextoy10_g63747 * fbrowsoffset10_g63747;
				// UV Offset
				float2 fboffset10_g63747 = float2(fboffsetx10_g63747, fboffsety10_g63747);
				// Flipbook UV
				float2 fbuv10_g63747 = input.ase_texcoord3.xy * fbtiling10_g63747 + fboffset10_g63747;
				// *** END Flipbook UV Animation vars ***
				int flipbookFrame10_g63747 = ( ( int )fbcurrenttileindex10_g63747);
				float4 tex2DNode17_g63747 = tex2D( _Albedo, fbuv10_g63747 );
				float smoothstepResult46_g63747 = smoothstep( temp_output_87_0_g63747 , 1.0 , tex2D( _Dissolve, fbuv10_g63747 ).a);
				float4 color62_g63747 = IsGammaSpace() ? float4( 1, 1, 1, 1 ) : float4( 1, 1, 1, 1 );
				float4 temp_output_61_0_g63747 = color62_g63747;
				float temp_output_9_0_g63751 = ( tex2DNode17_g63747.a * smoothstepResult46_g63747 * (temp_output_61_0_g63747).a * (input.ase_texcoord5).w );
				float temp_output_14_0_g63752 = ( temp_output_9_0_g63751 * input.ase_color.a );
				float Mobile13_g63752 = temp_output_14_0_g63752;
				float webgl4_g63754 = 1.0;
				float screenDepth2_g63754 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ScreenPosNorm.xy ),_ZBufferParams);
				float distanceDepth2_g63754 = saturate( ( screenDepth2_g63754 - LinearEyeDepth( ScreenPosNorm.z,_ZBufferParams ) ) / ( _SoftParticles ) );
				float d4_g63754 = saturate( distanceDepth2_g63754 );
				float localSwitchWebGL4_g63754 = SwitchWebGL( webgl4_g63754 , d4_g63754 );
				float4 ase_positionSS_Pixel = ASEScreenPositionNormalizedToPixel( ScreenPosNorm, _ScaledScreenParams );
				float4 ditherScreenPos15_g63753 = ( ase_positionSS_Pixel * float4( 0.5,0.5,0.5,0.5 ) );
				float2 ditherScreenPosPixel15_g63753 = ditherScreenPos15_g63753.xy * _ScaledScreenParams.xy;
				float dither15_g63753 = Dither4x4Bayer( fmod( ditherScreenPosPixel15_g63753.x, 4 ), fmod( ditherScreenPosPixel15_g63753.y, 4 ) );
				float temp_output_17_0_g63753 = _Dither;
				float eyeDepth = input.ase_texcoord6.x;
				float cameraDepthFade10_g63753 = (( eyeDepth -_ProjectionParams.y - temp_output_17_0_g63753 ) / temp_output_17_0_g63753);
				dither15_g63753 = step( dither15_g63753, saturate( cameraDepthFade10_g63753 * 1.00001 ) );
				float lerpResult13_g63753 = lerp( 1.0 , dither15_g63753 , _DitherFade);
				float temp_output_15_0_g63752 = ( localSwitchWebGL4_g63754 * lerpResult13_g63753 * temp_output_9_0_g63751 * input.ase_color.a );
				float PC13_g63752 = temp_output_15_0_g63752;
				float localSwitch13_g63752 = Switch( Mobile13_g63752 , PC13_g63752 );
				float temp_output_269_0 = localSwitch13_g63752;
				float temp_output_20_0_g63749 = ( tex2D( _Overlay, ( mul( float2x2( appendResult24_g63750, appendResult26_g63750 ), ( (input.ase_texcoord3.xy*_OverlayTiling + ( ( ( _OverlayTiling - 1.0 ) / -2.0 ) + (_OverlayOffset).xy )) - temp_output_32_0_g63750 ) ) + temp_output_32_0_g63750 ) ).a * _OverlayColor.a * temp_output_269_0 );
				

				float3 Color = ( float4( ( temp_output_20_0_g63749 * _OverlayColor.rgb ) , 0.0 ) + ( ( 1.0 - temp_output_20_0_g63749 ) * float4( ( (input.ase_color).rgb * tex2DNode17_g63747.rgb * (input.ase_texcoord5).xyz * (temp_output_61_0_g63747).rgb ) , 0.0 ) ) ).xyz;
				float Alpha = temp_output_269_0;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
					float AlphaClipThresholdShadow = 0.5;
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					input.positionCS.z = input.positionCS.z;
				#endif

				#ifdef _ALPHATEST_ON
					clip(Alpha - AlphaClipThreshold);
				#endif

				InputData inputData = (InputData)0;
				inputData.positionWS = PositionWS;
				inputData.positionCS = input.positionCS;
				inputData.normalizedScreenSpaceUV = ScreenPosNorm.xy;
				inputData.normalWS = NormalWS;
				inputData.viewDirectionWS = ViewDirWS;

				#if defined(_DBUFFER) && defined(UNLIT_DEFAULT_DECAL_BLENDING)
					ApplyDecalToBaseColor(input.positionCS, Color);
				#endif

				#if defined( ASE_DEPTH_WRITE_ON )
					outputDepth = input.positionCS.z;
				#endif

				SurfaceData surfaceData = (SurfaceData)0;
				surfaceData.albedo = Color;
				surfaceData.alpha = Alpha;

			#if defined( _SCREEN_SPACE_OCCLUSION ) // GBuffer never has transparents
				float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV( input.positionCS );
				AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion( normalizedScreenSpaceUV );
				surfaceData.occlusion = aoFactor.directAmbientOcclusion;
			#else
				surfaceData.occlusion = 1;
			#endif

			#if ( UNITY_VERSION >= 60010000 )
				return PackGBuffersSurfaceData( surfaceData, inputData, float3( 0, 0, 0 ) );
			#else
				return SurfaceDataToGbuffer( surfaceData, inputData, float3( 0, 0, 0 ), kLightingInvalid );
			#endif
			}

			ENDHLSL
		}
		
	}
	

	

	CustomEditor "UnityEditor.ShaderGraphUnlitGUI"
	FallBack "Hidden/Shader Graph/FallbackError"
	
	Fallback Off
}
/*ASEBEGIN
Version=19909
Node;AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;268;1088,-1600;Inherit;False;Albedo;11;;63747;422b49a3d7d94bcaa17b3e6560231195;3,20,0,30,1,70,1;3;11;FLOAT2;0,0;False;61;COLOR;0,0,0,0;False;68;FLOAT;0;False;4;FLOAT3;0;FLOAT;23;COLOR;69;FLOAT3;22
Node;AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;269;1296,-1504;Inherit;False;Alpha;1;;63751;93be6c9c36574dbf9aaa0b4ac8c0b952;0;1;9;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;22;1136,-1440;Float;False;Property;_Cutoff;Cutoff;0;0;Create;True;0;0;0;True;0;False;0.1;0.1;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;262;1264,-1600;Inherit;False;Overlay;5;;63749;df00fbdde6b940c1a1a3d2d514c83f78;0;2;26;FLOAT4;0,0,0,0;False;27;FLOAT;1;False;1;FLOAT4;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;11;0,0;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;19;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ShadowCaster;0;2;ShadowCaster;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;True;False;False;False;False;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;False;True;1;LightMode=ShadowCaster;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;12;0,0;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;19;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthOnly;0;3;DepthOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;True;False;False;False;False;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;False;False;False;True;1;LightMode=DepthOnly;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;13;0,0;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;19;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;Meta;0;4;Meta;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Meta;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;14;0,0;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;19;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;Universal2D;0;5;Universal2D;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;1;LightMode=Universal2D;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;15;0,0;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;19;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;SceneSelectionPass;0;6;SceneSelectionPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=SceneSelectionPass;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;16;0,0;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;19;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ScenePickingPass;0;7;ScenePickingPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Picking;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;17;0,0;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;19;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthNormals;0;8;DepthNormals;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;False;True;1;LightMode=DepthNormalsOnly;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;18;0,0;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;19;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthNormalsOnly;0;9;DepthNormalsOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;False;True;1;LightMode=DepthNormalsOnly;False;True;11;d3d11;metal;vulkan;xboxone;xboxseries;playstation;ps4;ps5;switch;switch2;webgpu;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;19;0,0;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;19;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;MotionVectors;0;10;MotionVectors;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;False;False;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=MotionVectors;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;20;0,0;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;19;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;XRMotionVectors;0;11;XRMotionVectors;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;True;1;False;;255;False;;1;False;;7;False;;3;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;1;LightMode=XRMotionVectors;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;21;0,0;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;19;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;GBuffer;0;12;GBuffer;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;True;1;5;False;;10;False;;1;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;2;False;;True;3;False;;True;True;0;False;;0;False;;False;True;1;LightMode=UniversalGBuffer;False;True;12;d3d11;gles;metal;vulkan;xboxone;xboxseries;playstation;ps4;ps5;switch;switch2;webgpu;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;9;-1008,-1568;Float;False;False;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;19;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ExtraPrePass;0;0;ExtraPrePass;6;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;5;True;14;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;False;True;0;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;10;1440,-1600;Float;False;True;-1;3;UnityEditor.ShaderGraphUnlitGUI;0;24;CTFX2/Billboard;2992e84f91cbeb14eab234972e07ea9d;True;Forward;0;1;Forward;10;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Unlit;True;3;True;14;all;0;False;True;1;5;False;;10;False;;1;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;True;True;2;False;;True;3;False;;True;True;0;False;;0;False;;False;True;1;LightMode=UniversalForwardOnly;False;False;3;Include;;False;;Native;False;0;0;;Pragma;multi_compile __ CTFX_IS_MOBILE_SHADER;False;;Custom;False;0;0;;Pragma;multi_compile __ CTFX_IS_WEBGL_SHADER;False;;Custom;False;0;0;;;0;0;Standard;31;Surface;1;639141949145940784;  Keep Alpha;1;639141949121372797;  Blend;0;0;Two Sided;1;0;Alpha Clipping;1;0;  Use Shadow Threshold;0;0;Forward Only;0;0;Cast Shadows;0;638998680220771092;Receive Shadows;0;638998680231130686;Receive SSAO;0;638998680260769909;Default Decal Blending;1;0;Motion Vectors;0;638998680300467038;  Add Precomputed Velocity;0;0;  XR Motion Vectors;0;0;GPU Instancing;1;0;LOD CrossFade;1;0;Built-in Fog;0;638998680348909989;Meta Pass;0;0;Extra Pre Pass;0;0;Tessellation;0;0;  Phong;0;0;  Strength;0.5,False,;0;  Type;0;0;  Tess;16,False,;0;  Min;10,False,;0;  Max;25,False,;0;  Edge Length;16,False,;0;  Max Displacement;25,False,;0;Write Depth;0;639135125663047573;  Early Z;0;639135125450048670;Vertex Position;1;0;0;13;False;True;False;True;False;False;True;True;True;False;False;False;True;False;;False;0
WireConnection;269;9;268;23
WireConnection;262;26;268;0
WireConnection;262;27;269;0
WireConnection;10;2;262;0
WireConnection;10;3;269;0
WireConnection;10;4;22;0
ASEEND*/
//CHKSM=C841F2771C478A5F17445C23FF4B1DD8A98835FA