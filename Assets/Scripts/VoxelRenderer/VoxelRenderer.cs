using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using System.Threading;
using System.Runtime.CompilerServices;

public class VoxelRenderer : MonoBehaviour
{
    [Header("Rendering Engine")]
    [SerializeField, Tooltip("# of ticks between each render call (for updating destination render texture); also the number of frames system gets to update voxel world")]
    int renderTicks;
    [SerializeField, Tooltip("# of resolution divisions by 2")]
    int downsamples;
    [SerializeField]
    float maxRenderDist;
    [SerializeField]
    int maxVoxStepCount;
    [SerializeField]
    ComputeShader gaussianPyramidShader;
    [SerializeField]
    int blurSize;
    [SerializeField]
    float std;

    [Header("Materials")]
    [SerializeField]
    Color sandColor, dirtColor, waterColor;

    int blurKernel, upsampleKernel;
    int ticks;

    Texture2D tex;
    RenderTexture[] pyramid;
    Camera cam;
    Light sun;

    int width, height;

    float4[] pixels;
    NativeArray<float4> pixelsArr;

    float4x4 CamToWorld, CamInvProj;

    public static VoxelRenderer Instance;

    JobHandle renderHandle;

    bool renderInProgess;
    public bool RenderInProgress
    {
        get { return renderInProgess; }
    }

    private void Awake()
    {
        Instance = this;    
    }

    void Start()
    {
        ticks = 0;

        cam = Camera.main;
        sun = FindObjectOfType<Light>();

        blurKernel = gaussianPyramidShader.FindKernel("Blur");
        upsampleKernel = gaussianPyramidShader.FindKernel("Upsample");

        StartRender();
    }

    private void OnDestroy()
    {
    }

    private void Update()
    {
        ticks++;
        if (ticks > renderTicks)
        {
            ticks = 0;

            if (!renderInProgess)
            {
                StartRender();
            }

            if (renderHandle.IsCompleted && pixelsArr.IsCreated && renderInProgess) 
            {
                FinishRender();
            }
            else
            {
            }
        }
    }

    void Init()
    {
        width = (int)(cam.pixelWidth / Mathf.Pow(2,downsamples));
        height = (int)(cam.pixelHeight / Mathf.Pow(2, downsamples));

        if (tex == null)
        {
            tex = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
            tex.filterMode = FilterMode.Trilinear;
        }

        if (pyramid == null) 
        { 
            pyramid = new RenderTexture[downsamples + 1]; 
            for(int i = 0; i < pyramid.Length; i++)
            {
                CreateTexture((int)(width * Mathf.Pow(2, i)), (int)(height * Mathf.Pow(2, i)), ref pyramid[i]);
            }
        }

        if (pixels == null || pixels.Length != width * height) pixels = new float4[width * height];

        CamInvProj = cam.projectionMatrix.inverse;
        CamToWorld = cam.cameraToWorldMatrix;

        pixelsArr = new NativeArray<float4>(pixels, Allocator.TempJob);
    }

    /// <summary>
    /// Queues job for rendering to texture
    /// </summary>
    private void StartRender()
    {
        Init();

        //renderOdds = !renderOdds;

        RenderJob job = new RenderJob()
        {
            pixels = pixelsArr,
            width = width,
            height = height,
            _CameraToWorld = cam.cameraToWorldMatrix,
            _CameraInverseProjection = cam.projectionMatrix.inverse,
            maxVoxStepCount = maxVoxStepCount,
            planetCenter = new float3(0, 0, 0),
            planetRadius = 1,
            renderOdds = renderOdds,
            voxelData = VoxelWorld.Instance.voxelData,
            sandColor = new float4(sandColor.r, sandColor.g, sandColor.b, sandColor.a),
            dirtColor = new float4(dirtColor.r, dirtColor.g, dirtColor.b, dirtColor.a),
            waterColor = new float4(waterColor.r, waterColor.g, waterColor.b, waterColor.a)
        };
        renderInProgess = true;
        renderHandle = job.Schedule(pixels.Length, 64);
    }

    /// <summary>
    /// Prepares Render texture by upsampling
    /// </summary>
    void FinishRender()
    {
        renderHandle.Complete();
        pixels = pixelsArr.ToArray();
        pixelsArr.Dispose();
        renderInProgess = false;

        tex.SetPixelData<float4>(pixels, 0);
        tex.Apply();



        // gaussian pyramid
        // copy
        Graphics.Blit(tex, pyramid[0]);
        // upsample
        for (int i = 1; i < pyramid.Length; i++)
        {
            Debug.Assert(pyramid[i - 1].width == pyramid[i].width / 2);
            gaussianPyramidShader.SetTexture(upsampleKernel, "_Source", pyramid[i - 1]);
            gaussianPyramidShader.SetTexture(upsampleKernel, "_Result", pyramid[i]);
            int numThreadsPerGroup = 8;
            int numThreadGroupsX = Mathf.CeilToInt(pyramid[i - 1].width / numThreadsPerGroup);
            int numThreadGroupsY = Mathf.CeilToInt(pyramid[i - 1].height / numThreadsPerGroup);
            gaussianPyramidShader.Dispatch(upsampleKernel, numThreadGroupsX, numThreadGroupsY, 1);
            // blur
            //gaussianPyramidShader.SetTexture(blurKernel, "_Source", temp);
            gaussianPyramidShader.SetTexture(blurKernel, "_Result", pyramid[i]);
            gaussianPyramidShader.SetInt("_kernelSize", blurSize);
            gaussianPyramidShader.SetFloat("_std", std);
            numThreadGroupsX = Mathf.CeilToInt(pyramid[i].width / numThreadsPerGroup);
            numThreadGroupsY = Mathf.CeilToInt(pyramid[i].height / numThreadsPerGroup);
            gaussianPyramidShader.Dispatch(blurKernel, numThreadGroupsX, numThreadGroupsY, 1);
        }
    }

    void CreateTexture(int width, int height, ref RenderTexture rt)
    {
        rt = new RenderTexture(width, height, 24);
        rt.enableRandomWrite = true;
        rt.Create();
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Graphics.Blit(pyramid[pyramid.Length-1], destination);
    }

    #region Jobs
    [BurstCompile]
    struct RenderJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<float4> pixels;

        [NativeDisableParallelForRestriction]
        public NativeParallelHashMap<int3, VoxelWorld.VoxelType> voxelData;

        [ReadOnly] public int width;
        [ReadOnly] public int height;
        [ReadOnly] public float4x4 _CameraToWorld;
        [ReadOnly] public float4x4 _CameraInverseProjection;
        [ReadOnly] public int maxVoxStepCount;
        [ReadOnly] public bool renderOdds;

        const float epsilon = 0.0001f;
        [ReadOnly] public float3 planetCenter;
        [ReadOnly] public float planetRadius;
        [ReadOnly] public float4 sandColor, dirtColor, waterColor;

        void IJobParallelFor.Execute(int index)
        {
            int2 uv = to2D(index);
            /*if (!shouldRayMarch(new uint2(uv)))
            {
                int leftI = math.clamp((index - 1), 0, pixels.Length - 1);
                int rightI = math.clamp((index + 1), 0, pixels.Length - 1);

                pixels[index] = (pixels[leftI] + pixels[rightI]);
                return;
            }*/
            float2 uvNorm = new float2(uv.x / (float)width, uv.y / (float)height);

            Ray ray = CreateCameraRay(uvNorm * 2 - 1);

            RaymarchDDAResult rs = raymarchDDA(ray.origin, ray.direction, maxVoxStepCount);

            float4 color = getColor(rs, ray);
            pixels[index] = color;
        }


        bool shouldRayMarch(uint2 id)
        {
            if (id.x == width / 2 && id.y == height / 2) return true;
            float off = math.distance(new int2(width / 2, height / 2), id.xy);
            //if (off <= _BrushSize) return true;
            bool odds = (renderOdds && !(id.x % 2 == 0 && id.y % 2 == 0));
            bool evens = (!renderOdds && (id.x % 2 == 0 && id.y % 2 == 0));
            return odds || evens;
        }

        float4 getColor(RaymarchDDAResult res, Ray origin)
        {
            float4 color = new float4(0, 0, 0, 0);
            if (!res.miss)
            {
                float hitDis = res.pathLength;
                float depth = math.exp(math.min(1 - hitDis / maxVoxStepCount, 0));
                //float3 hitPoint = origin.origin + origin.direction * hitDis;
                //float3 normal = math.normalize(hitPoint - float3.zero);
                //color = new float4(normal.xyz * 0.5f + 0.5f, 1);
                VoxelWorld.VoxelType t = voxelData[res.mapPos];
                switch(t)
                {
                    case VoxelWorld.VoxelType.SAND:
                        color = sandColor;
                        break;
                    case VoxelWorld.VoxelType.DIRT:
                        color = dirtColor;
                        break;
                    case VoxelWorld.VoxelType.WATER:
                        color = waterColor;
                        break;
                }
                color = color * depth;
            }
            return color;
        }

        float sdfSphere(float3 p, float3 center, float radius)
        {
            return math.distance(p, center) - radius;
        }

        int2 to2D(int index)
        {
            return new int2(index % width, index / width);
        }

        #region Ray creation
        Ray CreateRay(float3 origin, float3 direction)
        {
            Ray ray = new Ray();
            ray.origin = origin;
            ray.direction = direction;
            return ray;
        }

        Ray CreateCameraRay(float2 uv)
        {
            float3 origin = math.mul(_CameraToWorld, new float4(0, 0, 0, 1)).xyz;
            float3 direction = math.mul(_CameraInverseProjection, new float4(uv, 0, 1)).xyz;
            direction = math.mul(_CameraToWorld, new float4(direction, 0)).xyz;
            direction = math.normalize(direction);
            return CreateRay(origin, direction);
        }
        #endregion

        #region Voxel Traversal raymarching
        bool DDAHit(int3 mapPos)
        {
            return voxelData.ContainsKey(mapPos);
        }
        struct RaymarchDDAResult
        {
            public int3 mapPos;
            public bool miss;
            public float pathLength; // distance to solid voxel entry face (of the cube representing the voxel, not the triangles)
                                     //Triangle t;
            public float voxelD; // how far the ray travels inside solid voxel before hitting a triangle
        };

        RaymarchDDAResult raymarchDDA(float3 o, float3 dir, int maxStepCount)
        {
            // https://www.shadertoy.com/view/4dX3zl
            float3 p = o;
            // which box of the map we're in
            int3 mapPos = new int3(math.floor(p));
            // length of ray from one xyz-side to another xyz-sideDist
            float3 deltaDist = math.abs(new float3(1, 1, 1) * math.length(dir) / dir);
            int3 rayStep = new int3(math.sign(dir));
            // length of ray from current position to next xyz-side
            float3 sideDist = (math.sign(dir) * (new float3(mapPos.x, mapPos.y, mapPos.z) - o) + (math.sign(dir) * 0.5f) + 0.5f) * deltaDist;
            // bool3 mask;
            bool miss = false;
            float pathLength = 0;
            //CheckRayHitsTriangle hits;
            for (int i = 0; i < maxStepCount; i++)
            {
                /*hits = hitsSurface(mapPos, o + dir * pathLength, dir, o + getVoxelExitOffset(sideDist, deltaDist));
                if (hits.hits)
                {
                    break;
                }*/
                if (DDAHit(mapPos)) break;
                if (sideDist.x < sideDist.y)
                {
                    if (sideDist.x < sideDist.z)
                    {
                        pathLength = sideDist.x;
                        sideDist.x += deltaDist.x;
                        mapPos.x += rayStep.x;
                        //mask = bool3(true, false, false);
                    }
                    else
                    {
                        pathLength = sideDist.z;
                        sideDist.z += deltaDist.z;
                        mapPos.z += rayStep.z;
                        //mask = bool3(false, false, true);
                    }
                }
                else
                {
                    if (sideDist.y < sideDist.z)
                    {
                        pathLength = sideDist.y;
                        sideDist.y += deltaDist.y;
                        mapPos.y += rayStep.y;
                        //mask = bool3(false, true, false);
                    }
                    else
                    {
                        pathLength = sideDist.z;
                        sideDist.z += deltaDist.z;
                        mapPos.z += rayStep.z;
                        //mask = bool3(false, false, true);
                    }
                }
                if (i == maxStepCount - 1)
                {
                    miss = true;
                }
            }

            RaymarchDDAResult result = new RaymarchDDAResult()
            {
                mapPos = mapPos,
                miss = miss,
                pathLength = pathLength,
            };
            //result.normal = normalize(hits.tri.vertexA.normal + hits.tri.vertexB.normal + hits.tri.vertexC.normal);
            /*result.t = hits.tri;
            result.voxelD = hits.d;*/

            return result;
        }
        #endregion
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

    bool renderOdds;

    public void Execute(int index)
    {
        int2 uv = to2D(index);
        if (!shouldRayMarch(new uint2(uv))) return;
        float2 uvNorm = new float2(uv.x / (float)width, uv.y / (float)height);

        Ray ray = CreateCameraRay(uvNorm * 2 - 1);

        RaymarchDDAResult rs = raymarchDDA(ray.origin, ray.direction, maxVoxStepCount);

        float4 color = getColor(rs, ray);
        pixels[index] = color;
    }


    bool shouldRayMarch(uint2 id)
    {
        if (id.x == width / 2 && id.y == height / 2) return true;
        float off = math.distance(new int2(width / 2, height / 2), id.xy);
        //if (off <= _BrushSize) return true;
        bool odds = (renderOdds && !(id.x % 2 == 0 && id.y % 2 == 0));
        bool evens = (!renderOdds && (id.x % 2 == 0 && id.y % 2 == 0));
        return odds || evens;
    }

    float4 getColor(RaymarchDDAResult res, Ray origin)
    {
        float4 color = new float4(0, 0, 0, 0);
        if (!res.miss)
        {
            float hitDis = res.pathLength;
            float3 hitPoint = origin.origin + origin.direction * hitDis;
            float3 normal = math.normalize(hitPoint - float3.zero);
            color = new float4(normal.xyz * 0.5f + 0.5f, 1);
        }
        return color;
    }

    float sdfSphere(float3 p, float3 center, float radius)
    {
        return math.distance(p, center) - radius;
    }

    int2 to2D(int index)
    {
        return new int2(index % width, index / width);
    }

    #region Ray creation
    Ray CreateRay(float3 origin, float3 direction)
    {
        Ray ray = new Ray();
        ray.origin = origin;
        ray.direction = direction;
        return ray;
    }

    Ray CreateCameraRay(float2 uv)
    {
        float3 origin = math.mul(CamToWorld, new float4(0, 0, 0, 1)).xyz;
        float3 direction = math.mul(CamInvProj, new float4(uv, 0, 1)).xyz;
        direction = math.mul(CamToWorld, new float4(direction, 0)).xyz;
        direction = math.normalize(direction);
        return CreateRay(origin, direction);
    }
    #endregion

    #region Voxel Traversal raymarching
    bool DDAHit(int3 mapPos)
    {
        return VoxelWorld.Instance.voxelData.ContainsKey(mapPos);
    }
    struct RaymarchDDAResult
    {
        public int3 mapPos;
        public bool miss;
        public float pathLength; // distance to solid voxel entry face (of the cube representing the voxel, not the triangles)
        //Triangle t;
        public float voxelD; // how far the ray travels inside solid voxel before hitting a triangle
    };

    RaymarchDDAResult raymarchDDA(float3 o, float3 dir, int maxStepCount)
    {
        // https://www.shadertoy.com/view/4dX3zl
        float3 p = o;
        // which box of the map we're in
        int3 mapPos = new int3(math.floor(p));
        // length of ray from one xyz-side to another xyz-sideDist
        float3 deltaDist = math.abs(new float3(1, 1, 1) * math.length(dir) / dir);
        int3 rayStep = new int3(math.sign(dir));
        // length of ray from current position to next xyz-side
        float3 sideDist = (math.sign(dir) * (new float3(mapPos.x, mapPos.y, mapPos.z) - o) + (math.sign(dir) * 0.5f) + 0.5f) * deltaDist;
        // bool3 mask;
        bool miss = false;
        float pathLength = 0;
        //CheckRayHitsTriangle hits;
        for (int i = 0; i < maxStepCount; i++)
        {
            /*hits = hitsSurface(mapPos, o + dir * pathLength, dir, o + getVoxelExitOffset(sideDist, deltaDist));
            if (hits.hits)
            {
                break;
            }*/
            if (DDAHit(mapPos)) break;
            if (sideDist.x < sideDist.y)
            {
                if (sideDist.x < sideDist.z)
                {
                    pathLength = sideDist.x;
                    sideDist.x += deltaDist.x;
                    mapPos.x += rayStep.x;
                    //mask = bool3(true, false, false);
                }
                else
                {
                    pathLength = sideDist.z;
                    sideDist.z += deltaDist.z;
                    mapPos.z += rayStep.z;
                    //mask = bool3(false, false, true);
                }
            }
            else
            {
                if (sideDist.y < sideDist.z)
                {
                    pathLength = sideDist.y;
                    sideDist.y += deltaDist.y;
                    mapPos.y += rayStep.y;
                    //mask = bool3(false, true, false);
                }
                else
                {
                    pathLength = sideDist.z;
                    sideDist.z += deltaDist.z;
                    mapPos.z += rayStep.z;
                    //mask = bool3(false, false, true);
                }
            }
            if (i == maxStepCount - 1)
            {
                miss = true;
            }
        }

        RaymarchDDAResult result = new RaymarchDDAResult()
        {
            mapPos = mapPos,
            miss = miss,
            pathLength = pathLength,
        };
        //result.normal = normalize(hits.tri.vertexA.normal + hits.tri.vertexB.normal + hits.tri.vertexC.normal);
        /*result.t = hits.tri;
        result.voxelD = hits.d;*/

        return result;
    }
    #endregion
    #endregion
}
