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

//
// Glue to make data types work.
//
#define vec2 float2
#define vec3 float3
#define vec4 float4

#define FLT_MAX         3.402823466e+38F

// ---------------------------------------------------------------------------------------------- //
// Ray Tracing Structures.
// ---------------------------------------------------------------------------------------------- /

struct Ray {
  vec3 origin;
  vec3 direction;
  vec3 color;
  vec4 accumColor;
  int  bounces;
  int  material;

  vec3 PointAtParameter(float t) {
	  return origin + t * direction;
  }
};

struct HitRecord {
  vec2  uv;
  float t;
  vec3  p;
  vec3  normal;
  vec3  albedo;
  int   material;
};

struct Sphere {
  vec3  center; 
  float radius;
  int   material;
  vec3  albedo;

  bool Hit(Ray r, float tMin, float tMax, out HitRecord rec);
};

bool Sphere::Hit(Ray r, float tMin, float tMax, out HitRecord rec) {
  rec.t = tMin;
  rec.p = vec3(0,0,0);
  rec.normal = vec3(0,0,0);
  rec.uv = vec2(0,0);
  rec.albedo = albedo;
  rec.material = material;

  vec3 oc = r.origin - center;
  float a = dot(r.direction, r.direction);
  float b = dot(oc, r.direction);
  float c = dot(oc, oc) - radius * radius;
  float discriminant = b*b - a*c;

  if (discriminant <= 0)  {
    return false;
  }
    
  float temp = (-b - sqrt(b*b - a*c)) / a;
  if (temp < tMax && temp > tMin) {
    rec.t = temp;
    rec.p = r.PointAtParameter(rec.t);
    rec.normal = normalize((rec.p - center) / radius);
    return true;
  }

  temp = (-b + sqrt(b*b - a*c)) / a;
  if (temp < tMax && temp > tMin) {
    rec.t = temp;
    rec.p = r.PointAtParameter(rec.t);
    rec.normal = normalize((rec.p - center) / radius);
    return true;
  }

  return false;
}
