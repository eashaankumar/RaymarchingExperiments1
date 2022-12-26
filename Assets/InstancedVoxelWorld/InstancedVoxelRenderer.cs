using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class InstancedVoxelRenderer : MonoBehaviour
{
    public Vector3 boundary;
    public Mesh mesh;
    public Material material;
    public float range;

    private ComputeBuffer meshPropertiesBuffer;
    private ComputeBuffer argsBuffer;

    private Bounds bounds;

    CoroutineQueue simQ;
    struct Voxel
    {
        public float3 pos;
        public float3 vel;
        public float mass;
        public float pressure;
        public float density;
    }

    private NativeList<Voxel> voxels;
    int particleCount;


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
        Gizmos.DrawWireCube(Vector3.zero, boundary);
    }

    private void OnGUI()
    {
        GUI.color = Color.black;
        GUI.Label(new Rect(10, 10, 1000, 1000), particleCount + "");
    }

    IEnumerator Sim()
    {
        // User Input
        if (Input.GetMouseButton(0))
        {
            for (int i = 0; i < 100; i++)
            {
                voxels.Add(new Voxel { mass = 1, pos = new float3(math.sin(i), 10, 0), vel = new float3(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value) * 10 });
            }
        }

        // Boundary surrounding the meshes we will be drawing.  Used for occlusion.
        bounds = new Bounds(transform.position, Vector3.one * (range + 1));
        yield return null;

        NativeArray<MeshProperties> meshPropertiesArray = new NativeArray<MeshProperties>(voxels.Length, Allocator.TempJob);
        VoxelRendererJob updateJob = new VoxelRendererJob()
        {
            meshPropertiesArray = meshPropertiesArray,
            voxels = voxels,
            boundary = new float3(boundary.x, boundary.y, boundary.z),
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

    [BurstCompile]
    struct VoxelRendererJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeList<Voxel> voxels;
        [NativeDisableParallelForRestriction]
        public NativeArray<MeshProperties> meshPropertiesArray;

        [ReadOnly] public float3 boundary;

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
}
