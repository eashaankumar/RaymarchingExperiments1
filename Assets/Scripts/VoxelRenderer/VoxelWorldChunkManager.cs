using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System.Threading;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;

public class VoxelWorldChunkManager : MonoBehaviour
{
    [SerializeField]
    Transform target;

    public static VoxelWorldChunkManager Instance;

    delegate void ChunkAction();
    Queue<ChunkAction> actionQueue;

    private void Awake()
    {
        Instance = this;
        actionQueue = new Queue<ChunkAction>();
    }

    Vector3 targetPos;

    private void Update()
    {
        targetPos = target.position;
        /*if (actionQueue.Count > 0)
        {
            var a = actionQueue.Dequeue();
            if (a != null) a();
        }*/
    }

    int3 to3DOffset(int i)
    {
        int zDirection = i % VoxelWorld.Instance.RenderChunks;
        int yDirection = (i / VoxelWorld.Instance.RenderChunks) % VoxelWorld.Instance.RenderChunks;
        int xDirection = i / (VoxelWorld.Instance.RenderChunks * VoxelWorld.Instance.RenderChunks);
        return new int3(xDirection, yDirection, zDirection) - (VoxelWorld.Instance.RenderChunks / 2);
    }

    public void LoadChunks()
    {
        /*LoadChunkJob job = new LoadChunkJob()
        {
            targetPos = targetPos,
            voxelData = VoxelWorld.Instance.voxelData,
            chunks = VoxelWorld.Instance.chunks,
        };
        job.Schedule().Complete();*/
    }

    /*[BurstCompile]
    struct LoadChunkJob : IJob
    {
        [ReadOnly] 
        public float3 targetPos;
        [ReadOnly]
        public int renderChunks;
        [NativeDisableParallelForRestriction]
        public NativeParallelHashMap<int3, VoxelWorld.VoxelData> voxelData;
        [NativeDisableParallelForRestriction]
        public NativeParallelHashMap<int3, VoxelWorldChunk> chunks;

        public void Execute()
        {
            int3 mapPos = VoxelWorld.Instance.WorldToVoxWorldPosition(targetPos);
            int3 chunkPos = VoxelWorld.Instance.WorldVoxToChunkID(mapPos);
            for (int x = -renderChunks / 2; x <= renderChunks / 2; x++)
            {
                for (int y = -renderChunks / 2; y <= renderChunks / 2; y++)
                {
                    for (int z = -renderChunks / 2; z <= renderChunks / 2; z++)
                    {
                        //int3 offset = to3DOffset(currentChunkOffset);
                        int3 offset = new int3(x, y, z);
                        int3 lookingAt = chunkPos + offset;
                        if (!chunks.ContainsKey(lookingAt))
                        {
                            VoxelWorldChunk voxelWorldChunk = new VoxelWorldChunk()
                            {
                                id = lookingAt,
                            };
                            if (chunks.TryAdd(lookingAt, voxelWorldChunk))
                            {
                                Load(lookingAt);
                            }
                        }
                    }
                }
            }
        }

        public void Load(int3 id)
        {
            int chunkSize = VoxelWorld.Instance.ChunkSize;
            int3 chunkOriginInVoxSpace = id * chunkSize;
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        int3 localVoxPos = new int3(x, y, z);
                        int3 worldVoxPos = chunkOriginInVoxSpace + localVoxPos;
                        float noise = VoxelWorldNoise.Instance.GetTerrainNoise(worldVoxPos);
                        if (noise < 0)
                        {
                            VoxelWorld.VoxelType vt = VoxelWorld.VoxelType.DIRT;
                            float tint = VoxelWorldNoise.Instance.GetTintNoise(worldVoxPos);
                            //VoxelWorld.Instance.AddVoxel(worldVoxPos, new VoxelWorld.VoxelData() { t = vt, tint = tint });
                            voxelData.TryAdd(worldVoxPos, new VoxelWorld.VoxelData() { t = vt, tint = tint });
                        }
                    }
                }
            }
        }
    }*/
}
