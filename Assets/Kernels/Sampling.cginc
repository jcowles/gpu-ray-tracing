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

float4 _Time;

float _Seed01;
float _R0;
float _R1;
float _R2;

StructuredBuffer<float3> _HemisphereSamples;

// ---------------------------------------------------------------------------------------------- //
// Noise & Sampling Functions
// ---------------------------------------------------------------------------------------------- /

//
// Interleaved Gradient Noise by Jorge Jimenez.
// http://www.iryoku.com/next-generation-post-processing-in-call-of-duty-advanced-warfare
//
float GradNoise(vec2 xy) {
  return frac(52.9829189f * frac(0.06711056f*float(xy.x) + 0.00583715f*float(xy.y)));
}

//
// Noise entry point, to abstract away details of the underlying noise function.
//
float Noise(vec2 uv) {
  return GradNoise(floor(fmod(uv, 1024)) + _Seed01 * _Time.y);
}

//
// Select a random point on a unit sphere, ideally with uniform distrobution.
//
vec3 RandomInUnitSphere(vec2 uv) {
  vec3 p;
  // Should be a loop until a point on unit sphere is found.
  // Then normalize wouldn't be needed.
  p = 2.0 * normalize(vec3(Noise(uv * 2000 * (1+_R0)) * 2 - .5,
                  Noise(uv * 2000 * (1+_R1)) * 2 - .5,
                  Noise(uv * 2000 * (1+_R2)) * 2 - .5)) - vec3(1,1,1);

  p = 2.0 * normalize(vec3(_R0, _R1, _R2)) - vec3(1,1,1);
  p = _HemisphereSamples[(Noise(uv * 2000) * 392901) % 4096];
  return p;
}

vec3 RandomInUnitDisk(vec2 uv) {
  return RandomInUnitSphere(uv);
}
