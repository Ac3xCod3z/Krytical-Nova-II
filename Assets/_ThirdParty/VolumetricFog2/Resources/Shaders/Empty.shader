Shader "Hidden/VolumetricFog2/Empty"
{
    Properties
    {
        _Color("Color", Color) = (0.9,0.9,0.9,0.3)
    }
    SubShader
		{
			Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" }
			Blend SrcAlpha OneMinusSrcAlpha
			ZTest Always
			Cull Front
			ZWrite Off

			Pass
			{
				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
                #include "UnityCG.cginc"

                half4 _Color;

				struct appdata
				{
					float4 vertex : POSITION;
				};

				struct v2f
				{
					float4 pos     : SV_POSITION;
				};

				v2f vert(appdata v)
				{
					v2f o;
					o.pos = UnityObjectToClipPos(v.vertex.xyz);
					#if defined(UNITY_REVERSED_Z)
						o.pos.z = 1.0e-9f;
					#else
						o.pos.z = o.pos.w - 1.0e-6f;
					#endif
					return o;
				}

				half4 frag(v2f i) : SV_Target
				{ 
					return _Color;
				}
				ENDCG
			}

			Pass
			{
				Name "Meta"
				Tags { "LightMode" = "Meta" }
				Cull Off
				Blend Off
				CGPROGRAM
				#pragma vertex vert_meta
				#pragma fragment frag_meta_empty
				#include "UnityStandardMeta.cginc"

				float4 frag_meta_empty(v2f_meta i) : SV_Target
				{
					UnityMetaInput o;
					UNITY_INITIALIZE_OUTPUT(UnityMetaInput, o);
					return UnityMetaFragment(o);
				}
				ENDCG
			}

		}
}