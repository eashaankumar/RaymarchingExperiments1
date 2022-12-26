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
    private NativeList<int3> voxels;
    private NativeParallelHashMap<int3, Voxel> voxelData;
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
        voxels = new NativeList<int3>(Allocator.Persistent);
        voxelData = new NativeParallelHashMap<int3, Voxel>(100000, Allocator.Persistent);
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
        if (voxelData.IsCreated) voxelData.Dispose();
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
        if (Input.GetMouseButton(0))
        {
            for (int i = 0; i < 5; i++)
            {
                int3 key = new int3(UnityEngine.Random.Range(-10, 10), UnityEngine.Random.Range(-10, 10), UnityEngine.Random.Range(-10, 10));
                if (voxelData.ContainsKey(key))
                    continue;
                voxelData.Add(key, new Voxel ());
            }
        }

        // Boundary surrounding the meshes we will be drawing.  Used for occlusion.
        bounds = new Bounds(transform.position, (Vector3.one) * (range + 1));
        yield return null;
        voxels.Clear();
        int width = Mathf.CeilToInt(Camera.main.pixelWidth / 3f);
        int height = Mathf.CeilToInt(Camera.main.pixelHeight / 3f);
        RenderJob renderJob = new RenderJob
        {
            voxels = voxels,
            voxelData = voxelData,
            width = width, 
            height = height,
            _CameraToWorld = Camera.main.cameraToWorldMatrix,
            _CameraInverseProjection = Camera.main.projectionMatrix.inverse,
            maxVoxStepCount = 250,
        };
        JobHandle handle = renderJob.Schedule(width * height, 64);
        yield return new WaitUntil(() => handle.IsCompleted);
        handle.Complete();

        NativeArray<MeshProperties> meshPropertiesArray = new NativeArray<MeshProperties>(voxels.Length, Allocator.TempJob);
        VoxelRendererJob updateJob = new VoxelRendererJob()
        {
            meshPropertiesArray = meshPropertiesArray,
            voxels = voxels,
        };
        handle = updateJob.Schedule(voxels.Length, 64);
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
        public NativeList<int3> voxels;
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

            int3 mapPos = voxels[index];

            // Properties
            MeshProperties props = new MeshProperties();
            quaternion rotation = quaternion.identity;
            float3 scale = float3.zero + 1;

            props.mat = float4x4.TRS(mapPos, rotation, scale);

            props.color = new float4(1, 0, 0, 1f);

            meshPropertiesArray[index] = props;
        }

    }

    [BurstCompile]
    struct RenderJob : IJobParallelFor // out -> list of voxels
    {
        [NativeDisableParallelForRestriction]
        public NativeList<int3> voxels;

        [NativeDisableParallelForRestriction]
        public NativeParallelHashMap<int3, Voxel> voxelData;

        [ReadOnly] public int width;
        [ReadOnly] public int height;
        [ReadOnly] public float4x4 _CameraToWorld;
        [ReadOnly] public float4x4 _CameraInverseProjection;
        [ReadOnly] public int maxVoxStepCount;
        void IJobParallelFor.Execute(int index)
        {
            int2 uv = to2D(index);
            float2 uvNorm = new float2(uv.x / (float)width, uv.y / (float)height);
            Ray ray = CreateCameraRay(uvNorm * 2 - 1);
            RaymarchDDAResult rs = raymarchDDA(ray.origin, ray.direction, maxVoxStepCount);
            if (!rs.miss)
            {
                voxels.Add(rs.mapPos);
            }
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

        RaymarchDDAResult raymarchDDA(float3 o, float3 dir, int maxStepCount, bool ignoreSelf = false)
        {
            // https://www.shadertoy.com/view/4dX3zl
            float3 p = o;
            // which box of the map we're in
            int3 mapPos = new int3(math.floor(p)) /*/ chunkSize*/; // vox to chunk space
            // length of ray from one xyz-side to another xyz-sideDist
            float3 deltaDist = math.abs(new float3(1, 1, 1) * math.length(dir) / dir);
            int3 rayStep = new int3(math.sign(dir));
            // length of ray from current position to next xyz-side
            float3 sideDist = (math.sign(dir) * (new float3(mapPos.x, mapPos.y, mapPos.z) - o) + (math.sign(dir) * 0.5f) + 0.5f) * deltaDist;
            bool3 mask = new bool3();
            bool hits = false;
            float pathLength = 0;
            float distThroughWater = 0;
            float disThroughChunk = 0;
            for (int i = 0; i < maxStepCount; i++)
            {

                #region hit
                if (DDAHit(mapPos))
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
                        mapPos.x += rayStep.x;

                        mask = new bool3(true, false, false);
                    }
                    else
                    {
                        pathLength = sideDist.z;
                        sideDist.z += deltaDist.z;
                        mapPos.z += rayStep.z;
                        mask = new bool3(false, false, true);
                    }
                }
                else
                {
                    if (sideDist.y < sideDist.z)
                    {
                        pathLength = sideDist.y;
                        sideDist.y += deltaDist.y;
                        mapPos.y += rayStep.y;
                        mask = new bool3(false, true, false);
                    }
                    else
                    {
                        pathLength = sideDist.z;
                        sideDist.z += deltaDist.z;
                        mapPos.z += rayStep.z;
                        mask = new bool3(false, false, true);
                    }
                }
                #endregion

                if (i == maxStepCount - 1)
                {
                    hits = false;
                }
            }

            RaymarchDDAResult result = new RaymarchDDAResult()
            {
                mapPos = mapPos,
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
