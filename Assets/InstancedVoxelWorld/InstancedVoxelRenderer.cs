using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class InstancedVoxelRenderer : MonoBehaviour
{
    #region Public variables
    public Mesh mesh;
    public Material material;
    public float range;
    #endregion

    #region Private vars
    private ComputeBuffer meshPropertiesBuffer;
    private ComputeBuffer argsBuffer;
    private Bounds bounds;
    CoroutineQueue simQ;
    private NativeList<Voxel> voxels;
    int particleCount;
    #endregion

    #region Structs
    struct Voxel
    {
        public float3 pos;
        public float3 vel;
        public float mass;
        public float pressure;
        public float density;
    }
    struct RaymarchDDAResult
    {
        public int3 mapPos;
        public bool miss;
        public float pathLength; // distance to solid voxel entry face (of the cube representing the voxel, not the triangles)
        public float distThroughWater;
        public float voxelD; // how far the ray travels inside solid voxel before hitting a triangle
    };

    // Mesh Properties struct to be read from the GPU.
    // Size() is a convenience funciton which returns the stride of the struct.
    private struct MeshProperties
    {
        public float4x4 mat;
        public float4 color;

        public static int Size()
        {
            return
                sizeof(float) * 4 * 4 + // matrix;
                sizeof(float) * 4;      // color;
        }
    }
    #endregion

    #region Mono Behaviors
    private void Awake()
    {
        voxels = new NativeList<Voxel>(Allocator.Persistent);
        simQ = new CoroutineQueue(this);
        simQ.StartLoop();
    }

    private void Start()
    {
        simQ.EnqueueAction(Sim(), "Sim");
    }

    private void OnDestroy()
    {
        if (voxels.IsCreated) voxels.Dispose();
        simQ.StopLoop();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one * range);
    }

    private void OnGUI()
    {
        GUI.color = Color.black;
        GUI.Label(new Rect(10, 10, 1000, 1000), particleCount + "");
    }

    private void Update()
    {
        // rendering
        if (argsBuffer != null)
        {
            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, argsBuffer);
        }
    }

    private void OnDisable()
    {
        // Release gracefully.
        if (meshPropertiesBuffer != null)
        {
            meshPropertiesBuffer.Release();
        }
        meshPropertiesBuffer = null;

        if (argsBuffer != null)
        {
            argsBuffer.Release();
        }
        argsBuffer = null;
    }

    #endregion

    #region Logic
    IEnumerator Sim()
    {
        // User Input
        /*if (Input.GetMouseButton(0))
        {
            for (int i = 0; i < 100; i++)
            {
                voxels.Add(new Voxel { mass = 1, pos = new float3(UnityEngine.Random.Range(-range, range), UnityEngine.Random.Range(-range, range), UnityEngine.Random.Range(-range, range)), vel = new float3(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value) * 10 });
            }
        }*/

        // Boundary surrounding the meshes we will be drawing.  Used for occlusion.
        bounds = new Bounds(transform.position, (Vector3.one) * (range + 1));
        yield return null;

        NativeArray<MeshProperties> meshPropertiesArray = new NativeArray<MeshProperties>(voxels.Length, Allocator.TempJob);
        VoxelRendererJob updateJob = new VoxelRendererJob()
        {
            meshPropertiesArray = meshPropertiesArray,
            voxels = voxels,
        };
        JobHandle handle = updateJob.Schedule(voxels.Length, 64);
        yield return new WaitUntil(() => handle.IsCompleted);
        handle.Complete();

        if (meshPropertiesArray.Length > 0)
        {
            MeshProperties[] properties = meshPropertiesArray.ToArray();
            meshPropertiesArray.Dispose();
            InitializeBuffers(properties);
        }
        else
        {
            meshPropertiesArray.Dispose();
        }

        particleCount = voxels.Length;

        // restart loop
        simQ.EnqueueAction(Sim(), "Sim");
        yield return CoroutineQueueResult.PASS;
        yield break;
    }

    private void InitializeBuffers(MeshProperties[] properties)
    {
        // Argument buffer used by DrawMeshInstancedIndirect.
        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        // Arguments for drawing mesh.
        // 0 == number of triangle indices, 1 == population, others are only relevant if drawing submeshes.
        args[0] = (uint)mesh.GetIndexCount(0);
        args[1] = (uint)properties.Length;
        args[2] = (uint)mesh.GetIndexStart(0);
        args[3] = (uint)mesh.GetBaseVertex(0);
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);

        meshPropertiesBuffer = new ComputeBuffer(properties.Length, MeshProperties.Size());
        meshPropertiesBuffer.SetData(properties);
        material.SetBuffer("_Properties", meshPropertiesBuffer);
    }

    #endregion

    #region Jobs
    [BurstCompile]
    struct VoxelRendererJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeList<Voxel> voxels;
        [NativeDisableParallelForRestriction]
        public NativeArray<MeshProperties> meshPropertiesArray;

        public void Execute(int index)
        {
            UpdateParticle(index);
        }

        public void UpdateParticle(int index)
        {
            if (index < 0 || index >= voxels.Length)
            {
                return;
            }

            Voxel v = voxels[index];

            // Properties
            MeshProperties props = new MeshProperties();
            quaternion rotation = quaternion.identity;
            float3 scale = float3.zero + 1;

            props.mat = float4x4.TRS(v.pos, rotation, scale);

            props.color = new float4(1, 0, 0, 1f);

            meshPropertiesArray[index] = props;
        }

    }

    [BurstCompile]
    struct RenderJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<float4> pixels;

        [NativeDisableParallelForRestriction]
        public NativeParallelHashMap<int3, VoxelWorld.VoxelData> voxelData;
        [NativeDisableParallelForRestriction]
        public NativeParallelHashMap<int3, VoxelWorldChunk> chunks;
        [NativeDisableParallelForRestriction]
        public NativeList<int3> updateQueue;

        [ReadOnly] public int width;
        [ReadOnly] public int height;
        [ReadOnly] public float4x4 _CameraToWorld;
        [ReadOnly] public float4x4 _CameraInverseProjection;
        [ReadOnly] public float3 sunDir;
        [ReadOnly] public int maxVoxStepCount;
        [ReadOnly] public int maxChunkStepCount;
        [ReadOnly] public bool renderOdds;
        [ReadOnly] public int chunkSize;

        const float epsilon = 0.0001f;
        [ReadOnly] public float3 planetCenter;
        [ReadOnly] public float planetRadius;
        [ReadOnly] public float4 sandColor, dirtColor, waterColor, skyboxBlueDark, skyboxBlueLight, skyboxRed;
        [ReadOnly] public int terraform;
        [ReadOnly] public int BrushSize;
        [ReadOnly] public VoxelWorld.VoxelType terraformType;
        [ReadOnly] public VoxelWorld.VoxelWorldDimensions VoxWorldDims;
        void IJobParallelFor.Execute(int index)
        {
            int2 uv = to2D(index);
            /*if (!shouldRayMarch(new uint2(uv)))
            {
                int leftI = math.clamp((index - 1), 0, pixels.Length - 1);
                //int rightI = math.clamp((index + 1), 0, pixels.Length - 1);

                pixels[index] = pixels[leftI];
                return;
            }*/
            float2 uvNorm = new float2(uv.x / (float)width, uv.y / (float)height);

            Ray ray = CreateCameraRay(uvNorm * 2 - 1);

            RaymarchDDAResult rs = raymarchDDA(ray.origin, ray.direction, maxChunkStepCount);

            float4 color = getColor(rs, ray);
            pixels[index] = color;

            if (terraform != 0)
            {
                Terraform(rs.mapPos, uv, width, height);
            }
        }

        bool IsVoxInBounds(int3 v)
        {
            return v.x >= VoxWorldDims.worldLeft && v.x <= VoxWorldDims.worldRight &&
                    v.y >= VoxWorldDims.worldBottom &&
                    v.z >= VoxWorldDims.worldBack && v.z <= VoxWorldDims.worldFront;
        }

        void Terraform(int3 mapPos, int2 id, int width, int height)
        {
            if (id.x == width / 2 && id.y == height / 2)
            {
                for (int x = -BrushSize; x <= BrushSize; x++)
                {
                    for (int y = -BrushSize; y <= BrushSize; y++)
                    {
                        for (int z = -BrushSize; z <= BrushSize; z++)
                        {
                            int3 offset = new int3(x, y, z);
                            int3 newMapPos = mapPos + offset;
                            if (terraform == -1)
                            {
                                if (/*!IsVoxInBounds(newMapPos) ||*/ voxelData.ContainsKey(newMapPos)) continue;
                                voxelData.Add(newMapPos, new VoxelWorld.VoxelData() { t = terraformType, tint = math.sin(newMapPos.x + newMapPos.y + newMapPos.z) * 0.1f });
                                updateQueue.Add(newMapPos);
                            }
                            else if (terraform == 1)
                            {
                                if (!voxelData.ContainsKey(newMapPos)) continue;
                                voxelData.Remove(newMapPos);
                            }

                        }
                    }
                }
            }
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

        float invLerp(float from, float to, float value)
        {
            return (value - from) / (to - from);
        }

        float4 getColor(RaymarchDDAResult res, Ray origin)
        {
            float4 color = new float4(0, 0, 0, 0);
            float4 albedo = new float4(0, 0, 0, 0);
            float4 skybox = new float4(0, 0, 0, 0);
            float4 shadowColor = new float4(0, 0, 0, 0);

            if (!res.miss && voxelData.ContainsKey(res.mapPos))
            {
                //float3 hitPoint = origin.origin + origin.direction * hitDis;
                //float3 normal = math.normalize(hitPoint - float3.zero);
                //color = new float4(normal.xyz * 0.5f + 0.5f, 1);
                VoxelWorld.VoxelData d = voxelData[res.mapPos];
                switch (d.t)
                {
                    case VoxelWorld.VoxelType.SAND:
                        albedo = sandColor;
                        break;
                    case VoxelWorld.VoxelType.DIRT:
                        albedo = dirtColor;
                        break;
                }
                albedo = math.saturate(albedo + d.tint);
                skybox = float4.zero;
            }
            else
            {
                float viewAngle = math.dot(origin.direction, new float3(0, 1, 0));
                if (viewAngle > 0)
                {
                    skybox = math.lerp(skyboxBlueLight, skyboxBlueDark, viewAngle);
                }
                else
                {
                    skybox = math.lerp(skyboxBlueLight, new float4(0, 0, 0, 0), -viewAngle);
                }
            }
            if (res.distThroughWater > 0)
            {
                albedo = waterColor + albedo * (1 - math.exp(-1f / res.distThroughWater));
            }
            float hitDis = res.pathLength;

            if (IsInShadow(res.mapPos)) // use res.mapPos for per-vox shadows
            {
                shadowColor = new float4(1, 1, 1, 1) * -0.25f;
            }

            //float depth = math.exp(-1f / hitDis);
            color = albedo + skybox + shadowColor;
            return color;
        }

        bool IsInShadow(float3 origin)
        {
            Ray shadowRay = CreateRay(origin, -sunDir);
            //float3 deltaDist = math.abs(new float3(1, 1, 1) * math.length(shadowRay.direction) / shadowRay.direction);
            shadowRay.origin += shadowRay.direction * epsilon * 2;
            RaymarchDDAResult res = raymarchDDA(shadowRay.origin, shadowRay.direction, maxChunkStepCount / 4, true);
            return !res.miss;
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
            return voxelData.ContainsKey(mapPos) && voxelData[mapPos].t != VoxelWorld.VoxelType.WATER;
        }

        bool IsWater(int3 mapPos)
        {
            return voxelData.ContainsKey(mapPos) && voxelData[mapPos].t == VoxelWorld.VoxelType.WATER;
        }

        bool IsVoxelInChunk(int3 vox, int3 chunk)
        {
            /*int3 chunkVox = chunk * chunkSize;
            return (vox.x >= chunkVox.x && vox.x < chunkVox.x + chunkSize &&
                    vox.y >= chunkVox.y && vox.y < chunkVox.y + chunkSize &&
                    vox.z >= chunkVox.z && vox.z < chunkVox.z + chunkSize
            );*/
            int3 chunkID = vox / chunkSize;
            bool3 eq = chunkID == chunk;
            return eq.x && eq.y && eq.z;
        }

        RaymarchDDAResult raymarchDDA(float3 o, float3 dir, int maxStepCount, bool ignoreSelf = false)
        {
            // https://www.shadertoy.com/view/4dX3zl
            float3 p = o;
            // which box of the map we're in
            int3 chunkSpacePos = new int3(math.floor(p)) /*/ chunkSize*/; // vox to chunk space
            // length of ray from one xyz-side to another xyz-sideDist
            float3 deltaDist = math.abs(new float3(1, 1, 1) * math.length(dir) / dir);
            int3 rayStep = new int3(math.sign(dir));
            // length of ray from current position to next xyz-side
            float3 sideDist = (math.sign(dir) * (new float3(chunkSpacePos.x, chunkSpacePos.y, chunkSpacePos.z) - o) + (math.sign(dir) * 0.5f) + 0.5f) * deltaDist;
            bool3 mask = new bool3();
            bool hits = false;
            float pathLength = 0;
            float distThroughWater = 0;
            float disThroughChunk = 0;
            int3 voxelMapPos = int3.zero;
            for (int i = 0; i < maxStepCount; i++)
            {

                #region hit
                if (DDAHit(chunkSpacePos))
                {
                    hits = true;
                }
                if (hits) break;
                #endregion

                #region chunk update
                if (sideDist.x < sideDist.y)
                {
                    if (sideDist.x < sideDist.z)
                    {
                        pathLength = sideDist.x;
                        sideDist.x += deltaDist.x;
                        chunkSpacePos.x += rayStep.x;

                        mask = new bool3(true, false, false);
                    }
                    else
                    {
                        pathLength = sideDist.z;
                        sideDist.z += deltaDist.z;
                        chunkSpacePos.z += rayStep.z;
                        mask = new bool3(false, false, true);
                    }
                }
                else
                {
                    if (sideDist.y < sideDist.z)
                    {
                        pathLength = sideDist.y;
                        sideDist.y += deltaDist.y;
                        chunkSpacePos.y += rayStep.y;
                        mask = new bool3(false, true, false);
                    }
                    else
                    {
                        pathLength = sideDist.z;
                        sideDist.z += deltaDist.z;
                        chunkSpacePos.z += rayStep.z;
                        mask = new bool3(false, false, true);
                    }
                }
                if (IsWater(chunkSpacePos))
                {
                    if (mask.x) distThroughWater += deltaDist.x;
                    if (mask.y) distThroughWater += deltaDist.y;
                    if (mask.z) distThroughWater += deltaDist.z;
                }
                #endregion

                if (i == maxStepCount - 1)
                {
                    hits = false;
                }
            }

            RaymarchDDAResult result = new RaymarchDDAResult()
            {
                mapPos = chunkSpacePos,
                miss = !hits,
                pathLength = pathLength + disThroughChunk,
                distThroughWater = distThroughWater,
            };
            //result.normal = normalize(hits.tri.vertexA.normal + hits.tri.vertexB.normal + hits.tri.vertexC.normal);
            /*result.t = hits.tri;
            result.voxelD = hits.d;*/

            return result;
        }
        #endregion
    }
    #endregion
}
