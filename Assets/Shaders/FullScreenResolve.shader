Shader "Rtiow/FullScreenResolve"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_BlueNoise ("Texture", 2D) = "white" {}
	}
	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always
		Tags {"Queue" = "Transparent" }

		Pass
		{
			CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
        #pragma target 3.0
			
				#include "UnityCG.cginc"

				struct appdata
				{
					float4 vertex : POSITION;
					float2 uv : TEXCOORD0;
				};

				struct v2f
				{
					float2 uv : TEXCOORD0;
					float4 vertex : SV_POSITION;
				};

				v2f vert (appdata v)
				{
					v2f o;
					o.vertex = UnityObjectToClipPos(v.vertex);
					o.uv = v.uv;
					return o;
				}
			
				sampler2D _MainTex;
				float4 _MainTex_TexelSize;
				sampler2D _BlueNoise;

        float4 Sample(float2 uv, float2 offset) {
					return tex2D(_MainTex, uv + _MainTex_TexelSize.xy * offset);
        }

        void Accum(float4 append, inout float4 sum) {
          sum += (append / append.a);
        }

				float4 frag (v2f i) : SV_Target {
					float4 col0 = Sample(i.uv, float2(0,  0)); //tex2D(_MainTex, i.uv);
					float4 col2 = float4(0, 0, 0, 0);

          //
          // Ultra hacky "+" blur kernel.
          //
          Accum(Sample(i.uv, float2( 0,  1)), col2);
					Accum(Sample(i.uv, float2( 0, -1)), col2);
					Accum(Sample(i.uv, float2( 1,  0)), col2);
					Accum(Sample(i.uv, float2(-1,  0)), col2);
          Accum(.5 * Sample(i.uv, float2( 0,  1) * 2), col2);
					Accum(.5 * Sample(i.uv, float2( 0, -1) * 2), col2);
					Accum(.5 * Sample(i.uv, float2( 1,  0) * 2), col2);
					Accum(.5 * Sample(i.uv, float2(-1,  0) * 2), col2);
          
          // For col0 and col2, RGB is accumulated color and A is the sample count.
          // So RGBA / A = normalized color with A = 1.0.

          float sampleCount = col0.a;

          col0 /= col0.a;
          col2 /= col2.a;

          float blend = 1.0 / max(1, sampleCount / 20);
					return lerp(col0, col2, blend);
				}
			ENDCG
		}
	}
}
