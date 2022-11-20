using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using System.Threading;

public class VoxelRenderer : MonoBehaviour
{
    [Header("Rendering Engine")]
    [SerializeField, Tooltip("# of ticks between each render call (for updating destination render texture)")]
    int renderTicks;
    [SerializeField, Tooltip("Resolution"), Range(0f, 1f)]
    float resolution;
    [SerializeField]
    float maxRenderDist;

    int ticks;

    Texture2D tex;
    Camera cam;
    Light sun;

    int width, height;
    float3 planetCenter = new float3(0, 0, 0);
    float planetRadius = 1;
    const float epsilon = 0.0001f;
    float4x4 _CamToWorld;
    float4x4 _CamInvProj;

    float4[] pixels;
    Thread renderThread;

    void Start()
    {
        ticks = 0;

        cam = Camera.main;
        sun = FindObjectOfType<Light>();
    }

    private void OnDestroy()
    {
    }


    private void Update()
    {
        
    }

    private void LateUpdate()
    {
        /*if (ticks == 0 && tex != null)
        {
            renderJobHandle.Complete();

            tex.SetPixelData<float4>(pixels, 0);
            tex.Apply();
        }*/

        ticks++;
        if (ticks > renderTicks)
        {
            ticks = 0;
            if (renderThread != null && !renderThread.IsAlive)
            {
                tex.SetPixelData<float4>(pixels, 0);
                tex.Apply();
                renderThread = null;
            }
            if (renderThread == null)
            {
                width = (int)(cam.pixelWidth * resolution);
                height = (int)(cam.pixelHeight * resolution);

                pixels = new float4[width * height];

                tex = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
                tex.filterMode = FilterMode.Trilinear;

                _CamToWorld = cam.cameraToWorldMatrix;
                _CamInvProj = cam.projectionMatrix.inverse;

                ThreadStart start = new ThreadStart(RenderTextureWithRaymarching);
                renderThread = new Thread(start);
                renderThread.IsBackground = true;
                renderThread.Start();
            }
        }
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Graphics.Blit(tex, destination);
    }

    #region Threading
    void RenderTextureWithRaymarching()
    {
        for (int i = 0; i < width * height; i++)
        {
            Execute(i);
        }
    }
    #endregion

    #region Raymarching logic
    struct Ray
    {
        public float3 origin;
        public float3 direction;
    }

    struct RaymarchResult
    {
        public bool hit;
        public float d;
    }

    public void Execute(int index)
    {
        int2 uv = to2D(index);
        float2 uvNorm = new float2(uv.x / (float)width, uv.y / (float)height);

        Ray ray = CreateCameraRay(uvNorm * 2 - 1);

        RaymarchResult rs = raymarch(ray);

        float4 color = getColor(rs, ray);
        pixels[index] = color;
    }

    float4 getColor(RaymarchResult res, Ray origin)
    {
        float4 color = new float4(0, 0, 0, 0);
        if (res.hit)
        {
            float3 hitPoint = origin.origin + origin.direction * res.d;
            float3 normal = math.normalize(hitPoint - planetCenter);
            color = new float4(normal.xyz * 0.5f + 0.5f, 1);
        }
        return color;
    }

    RaymarchResult raymarch(Ray start)
    {
        float d = 0;
        Ray current = start;
        RaymarchResult res = new RaymarchResult();
        while (d < maxRenderDist)
        {
            float3 p = current.origin + current.direction * d;
            float sdf = sdfSphere(p, planetCenter, planetRadius);
            d += sdf;
            if (sdf < epsilon)
            {
                res.hit = true;
                res.d = d;
                return res;
            }
        }
        res.hit = false;
        return res;
    }

    float sdfSphere(float3 p, float3 center, float radius)
    {
        return math.distance(p, center) - radius;
    }

    int2 to2D(int index)
    {
        return new int2(index % width, index / width);
    }

    Ray CreateRay(float3 origin, float3 direction)
    {
        Ray ray = new Ray();
        ray.origin = origin;
        ray.direction = direction;
        return ray;
    }

    Ray CreateCameraRay(float2 uv)
    {
        float3 origin = math.mul(_CamToWorld, new float4(0, 0, 0, 1)).xyz;
        float3 direction = math.mul(_CamInvProj, new float4(uv, 0, 1)).xyz;
        direction = math.mul(_CamToWorld, new float4(direction, 0)).xyz;
        direction = math.normalize(direction);
        return CreateRay(origin, direction);
    }

    #endregion
}
