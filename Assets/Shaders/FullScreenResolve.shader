// Copyright 2018 Google Inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

Shader "GpuRayTracing/FullScreenResolve"
{
  Properties
  {
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
        #pragma target 5.0
      
        #include "UnityCG.cginc"
        #include "../Kernels/Structures.cginc"

        StructuredBuffer<Ray> _Rays;

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
      
        float2 _AccumulatedImageSize;

        float4 frag (v2f i) : SV_Target {
          float2 size = _AccumulatedImageSize;
          int2 xy = i.uv * size;

          uint rayCount, stride;
          _Rays.GetDimensions(rayCount, stride);

          float4 color = float4(0, 0, 0, 0);

          for (int z = 0; z < 8; z++) {
            int rayIndex = xy.x * size.y
                         + xy.y
                         + size.x * size.y * (z);

            color += _Rays[rayIndex % rayCount].accumColor;
          }

          // Note that the blur from the blog post is no longer applied, but could
          // be done here or in the transfer from accumulated image to screen.

          // Normalize by sample count.
          return color / color.a;
        }
      ENDCG
    }
  }
}
