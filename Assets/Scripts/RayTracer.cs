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

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;

public class RayTracer : MonoBehaviour {

  public static RayTracer Instance { get; private set; }

  // The main accumulation texture.
  public RenderTexture MainTexture;

  public ComputeShader RayTraceKernels;
  public Material FullScreenResolve;

  [Range(0.001f, 100f)]
  public float FocusDistance;

  // If set, FocusDistance is ignored and instead the FocusDistance is based on the FocusObject.
  public Transform FocusObject;

  [Range(0.0001f, 2.5f)]
  public float Aperture;

  [Range(1, 64)]
  public int MaxBounces = 1;

  public MeshFilter SphericalFibDebugMesh;


  // Sample Count is only public for debugging.
  // In non-sample code this should be a read-only label.
  public int m_sampleCount;

  // -------------------------------------------------------------------------------------------- //
  // Private Member Variables.
  // -------------------------------------------------------------------------------------------- //

  // Dirty flag for scene sync.
  private bool m_sceneChanged = false;

  // The maximum number of bounces before terminating a ray.
  private int m_maxBounces = 1;

  private float m_lastAperture = -1;
  private float m_lastFocusDistance = -1;
  private Matrix4x4 m_lastCam;
  private Matrix4x4 m_lastProj;

  private int m_initCameraRaysKernel;
  private int m_rayTraceKernel;
  private int m_normalizeSamplesKernel;

  private RenderTexture m_accumulatedImage;

  private ComputeBuffer m_spheresBuffer;
  private Sphere[] m_sphereData;

  private ComputeBuffer m_raysBuffer;

  private System.Random m_rng = new System.Random();

  // The number of super sampled rays to schedule.
  private int m_superSamplingFactor = 8;

  // The number of bounces per pixel to schedule.
  // This number will be multiplied by m_superSamplingFactor on Dispatch.
  private int m_bouncesPerPixel = 8;

  private ComputeBuffer m_fibSamples;

  public struct Ray {
    public Vector3 Origin;
    public Vector3 Direction;
    public Vector3 Color;
    public int     Bounces;
  }

  public struct Sphere {
    public Vector3 Center;
    public float Radius;
    public int Material;
    public Vector3 Albedo;
  }

  void ReclaimResources() {
    if (m_accumulatedImage != null) {
      RenderTexture.Destroy(m_accumulatedImage);
      m_accumulatedImage = null;
    }
    if (m_spheresBuffer != null) {
      m_spheresBuffer.Dispose();
      m_spheresBuffer = null;
    }
    if (m_raysBuffer != null) {
      m_raysBuffer.Dispose();
      m_raysBuffer = null;
    }
    if (m_fibSamples != null) {
      m_fibSamples.Dispose();
      m_fibSamples = null;
    }
  }

  void SphericalFib(ref Vector3[] output) {
    double n = output.Length / 2;
    double pi = Mathf.PI;
    double dphi = pi * (3 - Math.Sqrt(5));
    double phi = 0;
    double dz = 1 / n;
    double z = 1 - dz / 2.0f;
    int[] indices = new int[output.Length];

    for (int j = 0; j < n; j++) {
      double zj = z;
      double thetaj = Math.Acos(zj);
      double phij = phi % (2 * pi);
      z = z - dz;
      phi = phi + dphi;

      // spherical -> cartesian, with r = 1
      output[j] = new Vector3((float)(Math.Cos(phij) * Math.Sin(thetaj)),
                              (float)(zj),
                              (float)(Math.Sin(thetaj) * Math.Sin(phij)));
      indices[j] = j;
    }

    if (SphericalFibDebugMesh == null) {
      return;
    }

    // The code above only covers a hemisphere, this mirrors it into a sphere.
    for (int i = 0; i < n; i++) {
      var vz = output[i];
      vz.y *= -1;
      output[output.Length - i - 1] = vz;
      indices[i + output.Length / 2] = i + output.Length / 2;
    }

    var m = new Mesh();
    m.vertices = output;
    m.SetIndices(indices, MeshTopology.Points, 0);
    SphericalFibDebugMesh.mesh = m;
  }

  void Awake() {
    Instance = this;
  }

  public void NotifySceneChanged() {
    // Setup the scene.
    var objects = GameObject.FindObjectsOfType<RayTracedSphere>();

    bool reallocate = false;
    if (m_sphereData == null || m_sphereData.Length != objects.Length) {
      m_sphereData = new Sphere[objects.Length];
      reallocate = true;
    }

    for (int i = 0; i < objects.Length; i++) {
      var obj = objects[i];
      m_sphereData[i] = obj.GetSphere();
    }

    if (reallocate) {
      // Setup GPU memory for the scene.
      const int kFloatsPerSphere = 8;
      if (m_spheresBuffer != null) {
        m_spheresBuffer.Dispose();
        m_spheresBuffer = null;
      }

      if (m_sphereData.Length > 0) {
        m_spheresBuffer = new ComputeBuffer(m_sphereData.Length, sizeof(float) * kFloatsPerSphere);
        RayTraceKernels.SetBuffer(m_rayTraceKernel, "_Spheres", m_spheresBuffer);
      }
    }

    if (m_spheresBuffer != null) {
      m_spheresBuffer.SetData(m_sphereData);
    }

    m_sceneChanged = true;
  }

  void OnEnable() {
    // Force an update.
    m_lastAperture = -1;

    m_sampleCount = 0;

    // Make sure we start from a clean slate.
    // Under normal circumstances, this does nothing.
    ReclaimResources();

    // Clone the main texture example texture, but enable it to be written from compute.
    m_accumulatedImage = new RenderTexture(MainTexture.descriptor);
    m_accumulatedImage.enableRandomWrite = true;
    m_accumulatedImage.Create();

    RenderTexture.active = m_accumulatedImage;
    GL.Clear(false, true, new Color(0, 0, 0, 0));
    RenderTexture.active = null;

    // Local constants make the next lines signfinicantly more readable.
    const int kBytesPerFloat = sizeof(float);
    const int kFloatsPerRay = 13;
    int numPixels = m_accumulatedImage.width * m_accumulatedImage.height;
    int numRays = numPixels * m_superSamplingFactor;

    // IMPORTANT NOTE: the byte size below must match the shader, not C#! In this case they match.
    m_raysBuffer = new ComputeBuffer(numRays,
                                     kBytesPerFloat * kFloatsPerRay + sizeof(int) + sizeof(int),
                                     ComputeBufferType.Counter);

    var samples = new Vector3[4096];
    SphericalFib(ref samples);
    m_fibSamples = new ComputeBuffer(samples.Length, 3 * kBytesPerFloat);
    m_fibSamples.SetData(samples);

    // Populate the scene.
    NotifySceneChanged();

    // Setup the RayTrace kernel.
    m_rayTraceKernel = RayTraceKernels.FindKernel("RayTrace");
    RayTraceKernels.SetTexture(m_rayTraceKernel, "_AccumulatedImage", m_accumulatedImage);
    RayTraceKernels.SetBuffer(m_rayTraceKernel, "_Spheres", m_spheresBuffer);
    RayTraceKernels.SetBuffer(m_rayTraceKernel, "_Rays", m_raysBuffer);
    RayTraceKernels.SetBuffer(m_rayTraceKernel, "_HemisphereSamples", m_fibSamples);

    // Setup the InitCameraRays kernel.
    m_initCameraRaysKernel = RayTraceKernels.FindKernel("InitCameraRays");
    RayTraceKernels.SetBuffer(m_initCameraRaysKernel, "_Rays", m_raysBuffer);
    RayTraceKernels.SetBuffer(m_initCameraRaysKernel, "_Spheres", m_spheresBuffer);
    RayTraceKernels.SetTexture(m_initCameraRaysKernel, "_AccumulatedImage", m_accumulatedImage);
    RayTraceKernels.SetBuffer(m_initCameraRaysKernel, "_HemisphereSamples", m_fibSamples);

    // Setup the NormalizeSamples kernel.
    m_normalizeSamplesKernel = RayTraceKernels.FindKernel("NormalizeSamples");
    RayTraceKernels.SetBuffer(m_normalizeSamplesKernel, "_Rays", m_raysBuffer);
    RayTraceKernels.SetBuffer(m_normalizeSamplesKernel, "_Spheres", m_spheresBuffer);
    RayTraceKernels.SetTexture(m_normalizeSamplesKernel, "_AccumulatedImage", m_accumulatedImage);
    RayTraceKernels.SetBuffer(m_normalizeSamplesKernel, "_HemisphereSamples", m_fibSamples);

    // DOF parameter defaults.
    RayTraceKernels.SetFloat("_Aperture", 2.0f);
    RayTraceKernels.SetFloat("_FocusDistance", 5.0f);

    // Assign the texture to the main materail, to blit to screen.
    FullScreenResolve.mainTexture = m_accumulatedImage;
  }

  void OnDisable() {
    ReclaimResources();
  }

  void SetMatrix(ComputeShader shader, string name, Matrix4x4 matrix) {
    float[] matrixFloats = new float[] { 
      matrix[0,0], matrix[1, 0], matrix[2, 0], matrix[3, 0], 
      matrix[0,1], matrix[1, 1], matrix[2, 1], matrix[3, 1], 
      matrix[0,2], matrix[1, 2], matrix[2, 2], matrix[3, 2], 
      matrix[0,3], matrix[1, 3], matrix[2, 3], matrix[3, 3] 
      };
    shader.SetFloats(name, matrixFloats);
  }

  void OnRenderObject() {
    if (FocusObject != null) {
      FocusDistance = (transform.position - FocusObject.position).magnitude;
      transform.LookAt(FocusObject.position);
    }

    var camInverse = Camera.main.cameraToWorldMatrix;
    var ProjInverse = Camera.main.projectionMatrix.inverse;

    RayTraceKernels.SetFloat("_Seed01", (float)m_rng.NextDouble());
    RayTraceKernels.SetFloat("_R0", (float)m_rng.NextDouble());
    RayTraceKernels.SetFloat("_R1", (float)m_rng.NextDouble());
    RayTraceKernels.SetFloat("_R2", (float)m_rng.NextDouble());

    if (m_sceneChanged || m_lastAperture != Aperture || m_lastFocusDistance != FocusDistance || m_maxBounces != MaxBounces || camInverse != m_lastCam || ProjInverse != m_lastProj) {
      SetMatrix(RayTraceKernels, "_Camera", Camera.main.worldToCameraMatrix);
      SetMatrix(RayTraceKernels, "_CameraI", Camera.main.cameraToWorldMatrix);
      SetMatrix(RayTraceKernels, "_ProjectionI", ProjInverse);
      SetMatrix(RayTraceKernels, "_Projection", Camera.main.projectionMatrix);
      RayTraceKernels.SetFloat("_FocusDistance", FocusDistance);
      RayTraceKernels.SetFloat("_Aperture", Aperture);

      m_sceneChanged = false;
      m_lastAperture = Aperture;
      m_lastFocusDistance = FocusDistance;
      m_lastCam = camInverse;
      m_lastProj = ProjInverse;
      var rayStart = new Vector3(0, 0, 0);
      var rayEnd = new Vector3(0, 0, 1);
      rayStart = Camera.main.projectionMatrix.inverse.MultiplyPoint(rayStart);
      rayEnd = Camera.main.projectionMatrix.inverse.MultiplyPoint(rayEnd);

      m_maxBounces = MaxBounces;
      m_sampleCount = 0;

      RayTraceKernels.Dispatch(m_normalizeSamplesKernel,
                               m_accumulatedImage.width / 8,
                               m_accumulatedImage.height / 8,
                               m_superSamplingFactor);
    }

    m_sampleCount++;
    RayTraceKernels.Dispatch(m_initCameraRaysKernel,
                              m_accumulatedImage.width / 8,
                              m_accumulatedImage.height / 8, m_superSamplingFactor);


    float t = Time.time;
    RayTraceKernels.SetVector("_Time", new Vector4(t / 20, t, t * 2, t * 3));
    RayTraceKernels.SetInt("_MaxBounces", MaxBounces);

    RayTraceKernels.Dispatch(m_rayTraceKernel,
                             m_accumulatedImage.width / 8,
                             m_accumulatedImage.height / 8,
                             m_superSamplingFactor * m_bouncesPerPixel);
  }

  void OnRenderImage(RenderTexture source, RenderTexture dest) {
    // Resolve the final color directly from the ray accumColor.
    FullScreenResolve.SetBuffer("_Rays", m_raysBuffer);

    // Blit the rays into the accumulated image.
    // This isn't necessary, though it implicitly applies a box filter to the accumulated color,
    // which reduces aliasing artifacts when the viewport size doesn't match the underlying texture
    // size (should only be a problem in-editor).
    FullScreenResolve.SetVector("_AccumulatedImageSize",
                                new Vector2(m_accumulatedImage.width, m_accumulatedImage.height));
    Graphics.Blit(dest, m_accumulatedImage, FullScreenResolve);

    // Simple copy from accumulated image to viewport, the filter is applied here.
    // This extra copy and the associated texture could be skipped by filtering the ray colors
    // directly in the full screen resolve shader.
    Graphics.Blit(m_accumulatedImage, dest);
  }
}
