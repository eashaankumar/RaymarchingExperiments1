using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class VoxelWorldChunkManager : MonoBehaviour
{
    [SerializeField]
    Transform target;

    int currentChunkOffset;

    public static VoxelWorldChunkManager Instance;

    delegate void ChunkAction();
    Queue<ChunkAction> actionQueue;

    private void Awake()
    {
        Instance = this;
        actionQueue = new Queue<ChunkAction>();
    }

    private void Update()
    {
        Spawn();
        if (actionQueue.Count > 0)
        {
            actionQueue.Dequeue()();
        }
    }

    int3 to3DOffset(int i)
    {
        int zDirection = i % VoxelWorld.Instance.RenderChunks;
        int yDirection = (i / VoxelWorld.Instance.RenderChunks) % VoxelWorld.Instance.RenderChunks;
        int xDirection = i / (VoxelWorld.Instance.RenderChunks * VoxelWorld.Instance.RenderChunks);
        return new int3(xDirection, yDirection, zDirection) - (VoxelWorld.Instance.RenderChunks/2);
    }

    void Spawn()
    {
        int3 mapPos = VoxelWorld.Instance.WorldToVoxWorldPosition(target.position);
        int3 chunkPos = VoxelWorld.Instance.WorldVoxToChunkID(mapPos);
        int3 lookingAt = chunkPos + to3DOffset(currentChunkOffset);
        if (!VoxelWorld.Instance.chunks.ContainsKey(lookingAt))
        {
            actionQueue.Enqueue(() => SpawnChunk(lookingAt));
        }
        // update currentChunk
        /*currentChunkOffset.x++;
        if (currentChunkOffset.x >= VoxelWorld.Instance.RenderChunks/2)
        {
            currentChunkOffset.x = -VoxelWorld.Instance.RenderChunks / 2;
            currentChunkOffset.y++;
        }
        if (currentChunkOffset.y >= VoxelWorld.Instance.RenderChunks / 2)
        {
            currentChunkOffset.y = -VoxelWorld.Instance.RenderChunks / 2;
            currentChunkOffset.z++;
        }
        if (currentChunkOffset.y >= VoxelWorld.Instance.RenderChunks / 2)
        {
            currentChunkOffset.z = -VoxelWorld.Instance.RenderChunks / 2;
        }*/
        currentChunkOffset++;
        currentChunkOffset %= (VoxelWorld.Instance.RenderChunks * VoxelWorld.Instance.RenderChunks * VoxelWorld.Instance.RenderChunks);
    }

    void SpawnChunk(int3 id)
    {
        VoxelWorldChunk voxelWorldChunk = new VoxelWorldChunk(id);
        Load(id);
        VoxelWorld.Instance.chunks.Add(id, voxelWorldChunk);
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
                    float3 noiseSample = worldVoxPos;
                    float noise = VoxelWorldNoise.Instance.TerrainNoise.GetNoise(noiseSample.x, noiseSample.y, noiseSample.z);
                    if (noise < 0)
                    {
                        VoxelWorld.VoxelType vt = VoxelWorld.VoxelType.DIRT;
                        float tint = VoxelWorldNoise.Instance.TintNoise.GetNoise(noiseSample.x, noiseSample.y, noiseSample.z) * 0.1f;
                        //VoxelWorld.Instance.AddVoxel(worldVoxPos, new VoxelWorld.VoxelData() { t = vt, tint = tint });
                        VoxelWorld.Instance.voxelData.TryAdd(worldVoxPos, new VoxelWorld.VoxelData() { t = vt, tint = tint });
                    }
                }
            }
        }
    }

    public void UnLoad()
    {

    }
}
