using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System.Threading;

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

        /*for (int i = 0; i < 1; i++)
        {
            Thread t = new Thread(() => UpdateChunks());
            t.IsBackground = true;
            t.Priority = System.Threading.ThreadPriority.Highest;
            t.Start();
        }*/
    }

    Vector3 targetPos;

    private void Update()
    {
        targetPos = target.position;
    }

    private void UpdateChunks()
    {
        while (true)
        {
            Spawn();
            while (actionQueue.Count > 0)
            {
                var a = actionQueue.Dequeue();
                if (a != null) a();
            }
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
        int3 mapPos = VoxelWorld.Instance.WorldToVoxWorldPosition(targetPos);
        int3 chunkPos = VoxelWorld.Instance.WorldVoxToChunkID(mapPos);
        for (int x = -VoxelWorld.Instance.RenderChunks / 2; x <= VoxelWorld.Instance.RenderChunks / 2; x++)
        {
            for (int y = -VoxelWorld.Instance.RenderChunks / 2; y <= VoxelWorld.Instance.RenderChunks / 2; y++)
            {
                for (int z = -VoxelWorld.Instance.RenderChunks / 2; z <= VoxelWorld.Instance.RenderChunks / 2; z++)
                {
                    //int3 offset = to3DOffset(currentChunkOffset);
                    int3 offset = new int3(x, y, z);
                    int3 lookingAt = chunkPos + offset;
                    if (!VoxelWorld.Instance.chunks.ContainsKey(lookingAt))
                    {
                        actionQueue.Enqueue(() => SpawnChunk(lookingAt));
                    }
                }
            }
        }
       
        //currentChunkOffset++;
        //currentChunkOffset %= (VoxelWorld.Instance.RenderChunks * VoxelWorld.Instance.RenderChunks * VoxelWorld.Instance.RenderChunks);
    }

    void SpawnChunk(int3 id)
    {
        VoxelWorldChunk voxelWorldChunk = new VoxelWorldChunk(id);
        Load(id);
        VoxelWorld.Instance.chunks.TryAdd(id, voxelWorldChunk);
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
                        VoxelWorld.Instance.AddVoxel(worldVoxPos, new VoxelWorld.VoxelData() { t = vt, tint = tint });
                        //VoxelWorld.Instance.voxelData.TryAdd(worldVoxPos, new VoxelWorld.VoxelData() { t = vt, tint = tint });
                    }
                }
            }
        }
    }

    public void UnLoad()
    {

    }
}
