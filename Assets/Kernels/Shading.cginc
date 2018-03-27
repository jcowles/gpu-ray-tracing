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

#define kMaterialInvalid     0
#define kMaterialLambertian  1
#define kMaterialMetal       2
#define kMaterialDielectric  3

bool Refract(vec3 v, vec3 n, float niOverNt, out vec3 refracted) {
  vec3 uv = normalize(v);
  float dt = dot(uv, n);
  float discriminant = 1.0 - niOverNt * niOverNt * (1-dt*dt);
  if (discriminant > 0) {
    refracted = niOverNt * (v - n*dt) - n*sqrt(discriminant);
    return true;
  }
  return false;
}

float Schlick(float cosine, float refIdx) {
  float r0 = (1 - refIdx) / (1 + refIdx);
  r0 = r0 * r0;
  return r0 + (1 - r0) * pow((1 - cosine), 5);
}

vec3 EnvColor(Ray r, vec2 uv) {
  vec3 unitDirection = normalize(r.direction);
  float t = 0.5 * (unitDirection.y + 1.0);
  return 1.0 * ((1.0 - t) * vec3(1.0, 1.0, 1.0) + t * vec3(0.5, 0.7, 1.0));
}

bool ScatterLambertian(Ray rIn, HitRecord rec, inout vec3 attenuation, inout Ray scattered) {
  if (rec.material != kMaterialLambertian) { return false; }

  vec3 target = rec.p + rec.normal + RandomInUnitSphere(rec.uv);
  
  scattered.origin = rec.p + .001 * rec.normal;
  scattered.direction = target - rec.p;
  scattered.color = rIn.color;
  scattered.bounces = rIn.bounces;
  scattered.material = kMaterialLambertian;
  
  attenuation = rec.albedo;
  return true;
}

bool ScatterMetal(Ray rIn, HitRecord rec, inout vec3 attenuation, inout Ray scattered) {
  if (rec.material != kMaterialMetal) { return false; }
  
  // Fuzz should be a material parameter.
  const float kFuzz = .00;

  vec3 reflected = reflect(normalize(rIn.direction), rec.normal);
  
  scattered.direction = reflected + kFuzz * RandomInUnitSphere(rec.uv);
  scattered.origin = rec.p + .001 * scattered.direction;
  scattered.color = rIn.color;
  scattered.bounces = rIn.bounces;
  scattered.material = kMaterialMetal;

  attenuation = rec.albedo;
  return dot(scattered.direction, rec.normal) > 0;
}

bool ScatterDielectric(Ray rIn, HitRecord rec, inout vec3 attenuation, inout Ray scattered) {
  if (rec.material != kMaterialDielectric) { return false; }

  const float refIdx = 1.5;
  vec3 outwardNormal;
  float niOverNt;
  vec3 refracted;
  float cosine;

  if (dot(rIn.direction, rec.normal) > 0) {
    outwardNormal = -rec.normal;
    niOverNt = refIdx;
    cosine = refIdx * dot(rIn.direction, rec.normal) / length(rIn.direction);
  } else {
    outwardNormal = rec.normal;
    niOverNt = 1.0 / refIdx;
    cosine = -dot(rIn.direction, rec.normal) / length(rIn.direction);
  }

  // HLSL has a built-in refract function, but used the version for the book for readability.
  float reflectProb = lerp(1.0, Schlick(cosine, refIdx),
                           Refract(rIn.direction, outwardNormal, niOverNt, refracted));

  attenuation = vec3(1,1,1);
  scattered.color = rIn.color;
  scattered.bounces = rIn.bounces;
  scattered.origin = rec.p;
  scattered.material = kMaterialDielectric;

  if (Noise(rec.uv) < reflectProb) {
    scattered.direction = normalize(reflect(rIn.direction, rec.normal));
  } else {
    scattered.direction = refracted;
  }

  return true;
}
