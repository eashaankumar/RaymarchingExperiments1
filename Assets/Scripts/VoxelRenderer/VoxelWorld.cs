using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using System;
using System.Threading;

public class VoxelWorld : MonoBehaviour
{

    [Header("World update settings.\nAvailable frame = frame when VoxelRenderer is not rendering")]
    [SerializeField, Tooltip("How many update requests are attempted to be processed per available frame")]
    int updateRequestsProcessCount;

    [Header("World settings")]
    [SerializeField]
    VoxelWorldDimensions dims;
    [SerializeField]
    int renderChunks;
    [SerializeField, Tooltip("Number of chunks to load per tick")]
    int numChunksPerTick;
    [SerializeField]
    int updateTicks;
    [SerializeField]
    int chunkSize;
    [SerializeField]
    Transform target;

    public int ChunkSize
    {
        get { return chunkSize; }
    }
    public int RenderChunks
    {
        get { return renderChunks; }
    }

    bool updating;

    public bool Updating
    {
        get { return updating; }
    }

    int currentUpdateTicks;

    public VoxelWorldDimensions VoxWorldDims
    {
        get { return dims; }
    }

    public static VoxelWorld Instance;
    Camera cam;

    //CoroutineQueue worldUpdateActions;
    public delegate void WorldUpdateAction();
    public struct WorldUpdateActionHolder
    {
        public WorldUpdateAction action;
    }
    
    [Serializable]
    public struct VoxelWorldDimensions
    {
        public int worldBottom;
        public int worldLeft;
        public int worldRight;
        public int worldFront;
        public int worldBack;
    }

    public enum VoxelType
    {
        SAND = 0, DIRT, WATER
    }

    public struct VoxelData
    {
        public VoxelType t;
        public float tint; // [-1, 1]
        public int staleTicks; // # updates this has stayed the same
    }

    int3 UP = new int3(0, 1, 0);
    int3 RIGHT = new int3(1, 0, 0);
    int3 FORWARD = new int3(0, 0, 1);

    public NativeParallelHashMap<int3, VoxelData> voxelData;
    public NativeParallelHashMap<int3, VoxelWorldChunk> chunks;
    public NativeQueue<int3> worldUpdateActions;

    int numActiveChunks;

    private void Awake()
    {
        Instance = this;

        worldUpdateActions = new NativeQueue<int3>(Allocator.Persistent);
        voxelData = new NativeParallelHashMap<int3, VoxelData>(100000, Allocator.Persistent);
        chunks = new NativeParallelHashMap<int3, VoxelWorldChunk>(100000, Allocator.Persistent);
    }
    void Start()
    {
        currentUpdateTicks = 0;
    }

    private void OnDestroy()
    {
        updateJobHandle.Complete();
        voxelData.Dispose();
        chunks.Dispose();
        worldUpdateActions.Dispose();
        //voxelsToUpdate.Dispose();
    }

    private void OnGUI()
    {
        GUI.color = Color.white;
        GUI.Label(new Rect(10, 10, 500, 20), "Active Chunks: " + numActiveChunks);
    }

    JobHandle updateJobHandle;

    // Update is called once per frame
    public void VoxelWorldUpdate()
    {
        StartCoroutine(PerformUpdate());
        
    }

    IEnumerator PerformUpdate()
    {
        currentUpdateTicks++;
        if (currentUpdateTicks >= updateTicks)
        {
            currentUpdateTicks = 0;
            int x = (dims.worldLeft + dims.worldRight) / 2;
            int y = (100);
            int z = (dims.worldBack + dims.worldFront) / 2;
            AddVoxel(new int3(x, y, z), new VoxelData() { t = VoxelType.SAND, tint = math.sin(Time.time * 100) * 0.1f });

            UpdateJob job = new UpdateJob()
            {
                voxelData = voxelData,
                dims = dims,
                worldUpdateActions = worldUpdateActions,
                UP = UP,
                RIGHT = RIGHT,
                FORWARD = FORWARD,
                updateRequestsProcessCount = updateRequestsProcessCount,
                chunks = chunks,
                targetPos = target.position,
                renderChunks = renderChunks,
                chunkSize = chunkSize,
                numChunksPerTick = numChunksPerTick,
                random = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(1, 100000)),
                terrainNoiseParams = new NoiseParams { scale=VoxelWorldNoise.Instance.terrainNoiseImpact, 
                    frequency=VoxelWorldNoise.Instance.frequency },
                tintNoiseParams = new NoiseParams
                {
                    scale = VoxelWorldNoise.Instance.tintNoiseImpact,
                    frequency = VoxelWorldNoise.Instance.tintFrequency
                },
                mountainHeight = VoxelWorldNoise.Instance.mountainHeight,
            };
            updating = true;
            updateJobHandle = job.Schedule();
            yield return new WaitUntil(() => updateJobHandle.IsCompleted);
            updateJobHandle.Complete();
            numActiveChunks = chunks.Count();
            updating = false;
        }
        VoxelRenderer.Instance.voxelQueue.Enqueue(VoxelRenderer.Instance.StartRender);
    }

    #region Math
    public int3 WorldToVoxWorldPosition(float3 pos)
    {
        return new int3(pos);
    }
    public int3 WorldVoxToChunkID(int3 mapPos)
    {
        return mapPos / ChunkSize;
    }
    #endregion

    #region World Update Methods
    public void AddVoxel(int3 pos, VoxelData t)
    {
        if (voxelData.TryAdd(pos, t))
        {
            worldUpdateActions.Enqueue(pos);
        }
    }
    #endregion

    #region Update Jobs
    struct NoiseParams
    {
        public float scale;
        public float frequency;
        public int octaves;
    }
    [BurstCompile]
    struct UpdateJob : IJob
    {
        [NativeDisableParallelForRestriction]
        public NativeParallelHashMap<int3, VoxelData> voxelData;
        [NativeDisableParallelForRestriction]
        public NativeQueue<int3> worldUpdateActions;
        [NativeDisableParallelForRestriction]
        public NativeParallelHashMap<int3, VoxelWorldChunk> chunks;

        public Unity.Mathematics.Random random;
        [ReadOnly] public int mountainHeight;
        [ReadOnly] public NoiseParams terrainNoiseParams;
        [ReadOnly] public NoiseParams tintNoiseParams;

        [ReadOnly] public VoxelWorldDimensions dims;

        [ReadOnly] public int3 UP;
        [ReadOnly] public int3 RIGHT;
        [ReadOnly] public int3 FORWARD;
        [ReadOnly] public int updateRequestsProcessCount;

        [ReadOnly] public int renderChunks;
        [ReadOnly] public float3 targetPos;
        [ReadOnly] public int chunkSize;
        [ReadOnly] public int numChunksPerTick;

        public void Execute()
        {
            int3 mapPos = WorldToVoxWorldPosition(targetPos);
            int3 chunkPos = WorldVoxToChunkID(mapPos);
            LoadChunks(chunkPos);
            UnloadChunks(chunkPos);
            for (int i = 0; i < updateRequestsProcessCount && i < worldUpdateActions.Count; i++)
            {
                UpdateVoxelAction(worldUpdateActions.Dequeue());
            }
        }

        public int3 WorldToVoxWorldPosition(float3 pos)
        {
            return new int3(math.floor(pos));
        }
        public int3 WorldVoxToChunkID(int3 mapPos)
        {
            return mapPos / chunkSize;
        }

        void UnloadChunks(int3 chunkPos)
        {
            int chunksRemoved = 0;
            foreach (int3 id in chunks.GetKeyArray(Allocator.Temp))
            {
                //if (chunksRemoved > numChunksPerTick) return;
                if (math.distance(chunkPos, id) > renderChunks)
                {
                    Unload(id);
                    chunks.Remove(id);
                }
                chunksRemoved++;
            }
        }

        void LoadChunks(int3 chunkPos)
        {
            int chunksCreated = 0;
            for (int x = -renderChunks / 2; x <= renderChunks / 2; x++)
            {
                for (int y = -renderChunks / 2; y <= renderChunks / 2; y++)
                {
                    for (int z = -renderChunks / 2; z <= renderChunks / 2; z++)
                    {
                        if (chunksCreated > numChunksPerTick) return;
                        int3 offset = new int3(x, y, z);
                        if (math.length(offset) > renderChunks) return;
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
                                chunksCreated++;
                            }
                        }
                    }
                }
            }
        }

        public void Load(int3 id)
        {
            int3 chunkOriginInVoxSpace = id * chunkSize;
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        int3 localVoxPos = new int3(x, y, z);
                        int3 worldVoxPos = chunkOriginInVoxSpace + localVoxPos;
                        //float noise = VoxelWorldNoise.Instance.GetTerrainNoise(worldVoxPos);
                        float noise = GetTerrainNoise(worldVoxPos);
                        if (noise < 0)
                        {
                            VoxelWorld.VoxelType vt = VoxelWorld.VoxelType.DIRT;
                            float tint = GetTintNoise(worldVoxPos);
                            //float tint = math.sin(x + y + z) * 0.1f;
                            //VoxelWorld.Instance.AddVoxel(worldVoxPos, new VoxelWorld.VoxelData() { t = vt, tint = tint });
                            voxelData.TryAdd(worldVoxPos, new VoxelWorld.VoxelData() { t = vt, tint = tint });
                        }
                    }
                }
            }
        }

        public void Unload(int3 id)
        {
            int3 chunkOriginInVoxSpace = id * chunkSize;
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        int3 localVoxPos = new int3(x, y, z);
                        int3 worldVoxPos = chunkOriginInVoxSpace + localVoxPos;
                        voxelData.Remove(worldVoxPos);
                    }
                }
            }
        }

        #region Voxel Physics Logic
        bool WaterPhysics(int3 pos)
        {
            if (!voxelData.ContainsKey(pos)) return false;
            VoxelData data = voxelData[pos];
            if (!SandPhysics(pos))
            {
                // side
                if (!voxelData.ContainsKey(pos - RIGHT) && pos.x > dims.worldLeft)
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos - RIGHT, data);
                    QueueUpdateVoxel(pos - RIGHT);
                    return true;
                }
                else if (!voxelData.ContainsKey(pos + RIGHT) && pos.x < dims.worldRight)
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos + RIGHT, data);
                    QueueUpdateVoxel(pos + RIGHT);
                    return true;
                }
                else if (!voxelData.ContainsKey(pos - FORWARD) && pos.z > dims.worldBack)
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos - FORWARD, data);
                    QueueUpdateVoxel(pos - FORWARD);
                    return true;
                }
                else if (!voxelData.ContainsKey(pos + FORWARD) && pos.z < dims.worldFront)
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos + FORWARD, data);
                    QueueUpdateVoxel(pos + FORWARD);
                    return true;
                }
                // corners
                else if (!voxelData.ContainsKey(pos - RIGHT - FORWARD) && pos.x > dims.worldLeft && pos.z > dims.worldBack) // level lower left
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos - RIGHT - FORWARD, data);
                    QueueUpdateVoxel(pos - RIGHT - FORWARD);
                    return true;
                }
                else if (!voxelData.ContainsKey(pos + RIGHT - FORWARD) && pos.x < dims.worldRight && pos.z > dims.worldBack) // level lower right
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos + RIGHT - FORWARD, data);
                    QueueUpdateVoxel(pos + RIGHT - FORWARD);
                    return true;
                }
                else if (!voxelData.ContainsKey(pos + RIGHT + FORWARD) && pos.x < dims.worldRight && pos.z < dims.worldFront) // level upper right
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos + RIGHT + FORWARD, data);
                    QueueUpdateVoxel(pos + RIGHT + FORWARD);
                    return true;
                }
                else if (!voxelData.ContainsKey(pos + FORWARD - RIGHT) && pos.x > dims.worldLeft && pos.z < dims.worldFront) // level upper left
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos + FORWARD - RIGHT, data);
                    QueueUpdateVoxel(pos + FORWARD - RIGHT);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }
        bool SandPhysics(int3 pos)
        {
            if (!voxelData.ContainsKey(pos)) return false;
            VoxelData data = voxelData[pos];
            if (!voxelData.ContainsKey(pos - UP) && pos.y > dims.worldBottom)
            {
                voxelData.Remove(pos);
                voxelData.Add(pos - UP, data);
                QueueUpdateVoxel(pos - UP);
                return true;
            }
            // side
            else if (!voxelData.ContainsKey(pos - UP - RIGHT) && pos.y > dims.worldBottom && pos.x > dims.worldLeft)
            {
                voxelData.Remove(pos);
                voxelData.Add(pos - UP - RIGHT, data);
                QueueUpdateVoxel(pos - UP - RIGHT);
                return true;
            }
            else if (!voxelData.ContainsKey(pos - UP + RIGHT) && pos.y > dims.worldBottom && pos.x < dims.worldRight)
            {
                voxelData.Remove(pos);
                voxelData.Add(pos - UP + RIGHT, data);
                QueueUpdateVoxel(pos - UP + RIGHT);
                return true;
            }
            else if (!voxelData.ContainsKey(pos - UP - FORWARD) && pos.y > dims.worldBottom && pos.z > dims.worldBack)
            {
                voxelData.Remove(pos);
                voxelData.Add(pos - UP - FORWARD, data);
                QueueUpdateVoxel(pos - UP - FORWARD);
                return true;
            }
            else if (!voxelData.ContainsKey(pos - UP + FORWARD) && pos.y > dims.worldBottom && pos.z < dims.worldFront)
            {
                voxelData.Remove(pos);
                voxelData.Add(pos - UP + FORWARD, data);
                QueueUpdateVoxel(pos - UP + FORWARD);
                return true;
            }
            // corners
            else if (!voxelData.ContainsKey(pos - UP - RIGHT - FORWARD) && pos.y > dims.worldBottom && pos.x > dims.worldLeft && pos.z > dims.worldBack) // bottom lower left
            {
                voxelData.Remove(pos);
                voxelData.Add(pos - UP - RIGHT - FORWARD, data);
                QueueUpdateVoxel(pos - UP - RIGHT - FORWARD);
                return true;
            }
            else if (!voxelData.ContainsKey(pos - UP + RIGHT - FORWARD) && pos.y > dims.worldBottom && pos.x < dims.worldRight && pos.z > dims.worldBack) // bottom lower right
            {
                voxelData.Remove(pos);
                voxelData.Add(pos - UP + RIGHT - FORWARD, data);
                QueueUpdateVoxel(pos - UP + RIGHT - FORWARD);
                return true;
            }
            else if (!voxelData.ContainsKey(pos - UP + RIGHT + FORWARD) && pos.y > dims.worldBottom && pos.x < dims.worldRight && pos.z < dims.worldFront) // bottom upper right
            {
                voxelData.Remove(pos);
                voxelData.Add(pos - UP + RIGHT + FORWARD, data);
                QueueUpdateVoxel(pos - UP + RIGHT + FORWARD);
                return true;
            }
            else if (!voxelData.ContainsKey(pos - UP + FORWARD - RIGHT) && pos.y > dims.worldBottom && pos.x > dims.worldLeft && pos.z < dims.worldFront) // bottom upper left
            {
                voxelData.Remove(pos);
                voxelData.Add(pos - UP + FORWARD - RIGHT, data);
                QueueUpdateVoxel(pos - UP + FORWARD - RIGHT);
                return true;
            }
            return false;
        }
        #endregion

        void QueueUpdateVoxel(int3 pos)
        {
            //for(int x = -1; x <= 1; x++)
            {
                //for (int y = -1; y <= 1; y++)
                {
                    //for (int z = -1; z <= 1; z++)
                    {
                        //int3 offset = new int3(x, y, z);
                        int3 offset = int3.zero;
                        int3 p = pos + offset;
                        if (!voxelData.ContainsKey(p)) return;
                        VoxelData data = voxelData[p];
                        data.staleTicks = 0;
                        voxelData[p] = data;
                        worldUpdateActions.Enqueue(p);
                    }
                }
            }
        }

        void UpdateVoxelAction(int3 pos)
        {
            if (!voxelData.ContainsKey(pos)) return;
            VoxelData data = voxelData[pos];
            
            if (data.staleTicks < 100 || random.NextFloat(0f, 1f) < 0.5f)
            {
                bool wasUpdated = false;
                switch (data.t)
                {
                    case VoxelType.WATER:
                        wasUpdated = WaterPhysics(pos);
                        break;
                    case VoxelType.SAND:
                        // if bottom cell is empty, move there
                        if (!SandPhysics(pos))
                        {
                            if (voxelData.ContainsKey(pos - UP) && voxelData[pos - UP].t == VoxelType.WATER)
                            {
                                VoxelData data2 = voxelData[pos - UP];
                                voxelData.Remove(pos - UP);
                                voxelData.Remove(pos);

                                voxelData.TryAdd(pos - UP, data);
                                voxelData.TryAdd(pos, data2);

                                QueueUpdateVoxel(pos - UP);
                                QueueUpdateVoxel(pos);
                                wasUpdated = true;
                            }
                        }
                        else
                        {
                            wasUpdated = true;
                        }
                        break;
                }
                if (!wasUpdated)
                {
                    data.staleTicks++;
                    voxelData[pos] = data;
                }
            }
        }

        #region Noise
        float invLerp(float from, float to, float value)
        {
            return (value - from) / (to - from);
        }
        float SurfaceNoise(int3 ns)
        {
            float3 noiseSample = ns;
            float n = math.lerp(-1f, 1f, invLerp(dims.worldBottom, mountainHeight, noiseSample.y));
            return n + Unity.Mathematics.noise.snoise(noiseSample * terrainNoiseParams.frequency) * terrainNoiseParams.scale;
        }

        public float GetTerrainNoise(int3 worldVoxPos)
        {
            if (worldVoxPos.y > mountainHeight) return 1;
            int3 noiseSample = worldVoxPos;
            return SurfaceNoise(noiseSample);
        }

        public float GetTintNoise(int3 worldVoxPos)
        {
            float3 noiseSample = worldVoxPos;
            return Unity.Mathematics.noise.snoise(noiseSample * tintNoiseParams.frequency) * tintNoiseParams.scale;
        }
        #endregion
    }
    #endregion
}
