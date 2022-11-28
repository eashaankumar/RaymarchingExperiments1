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
    [SerializeField]
    int chunkSize;

    public int ChunkSize
    {
        get { return chunkSize; }
    }
    public int RenderChunks
    {
        get { return renderChunks; }
    }

    bool updating;

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
    //public NativeParallelHashSet<int3> voxelsToUpdate;

    private void Awake()
    {
        Instance = this;

        worldUpdateActions = new NativeQueue<int3>(Allocator.Persistent);
        voxelData = new NativeParallelHashMap<int3, VoxelData>(10000, Allocator.Persistent);
        chunks = new NativeParallelHashMap<int3, VoxelWorldChunk>(10000, Allocator.Persistent);
    }
    void Start()
    {
        
    }

    private void OnDestroy()
    {
        updateJobHandle.Complete();
        voxelData.Dispose();
        chunks.Dispose();
        worldUpdateActions.Dispose();
        //voxelsToUpdate.Dispose();
    }

    JobHandle updateJobHandle;

    // Update is called once per frame
    public void VoxelWorldUpdate()
    {
        /*if (updateJobHandle.IsCompleted)
        {
            updateJobHandle.Complete();
            updating = false;
        }*/
        //if (!VoxelRenderer.Instance.RenderInProgress)
        {
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
            };
            updateJobHandle = job.Schedule();
            updateJobHandle.Complete();
            updating = true;
            VoxelRenderer.Instance.voxelQueue.Enqueue(VoxelRenderer.Instance.StartRender);
            print(worldUpdateActions.Count);
        }
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
    [BurstCompile]
    struct UpdateJob : IJob
    {
        [NativeDisableParallelForRestriction]
        public NativeParallelHashMap<int3, VoxelData> voxelData;
        [NativeDisableParallelForRestriction]
        public NativeQueue<int3> worldUpdateActions;

        [ReadOnly] public VoxelWorldDimensions dims;

        [ReadOnly] public int3 UP;
        [ReadOnly] public int3 RIGHT;
        [ReadOnly] public int3 FORWARD;
        [ReadOnly] public int updateRequestsProcessCount;

        public void Execute()
        {
            for(int i = 0; i < updateRequestsProcessCount && i < worldUpdateActions.Count; i++)
            {
                UpdateVoxelAction(worldUpdateActions.Dequeue());
            }
        }

        #region Voxel Physics Logic
        bool WaterPhysics(int3 pos, ref int3 newPos)
        {
            VoxelData data = voxelData[pos];
            if (!SandPhysics(pos))
            {
                // side
                if (!voxelData.ContainsKey(pos - RIGHT) && pos.x > dims.worldLeft)
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos - RIGHT, data);
                    newPos = pos - RIGHT;
                    QueueUpdateVoxel(pos - RIGHT);
                    return true;
                }
                else if (!voxelData.ContainsKey(pos + RIGHT) && pos.x < dims.worldRight)
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos + RIGHT, data);
                    newPos = pos + RIGHT;
                    QueueUpdateVoxel(pos + RIGHT);
                    return true;
                }
                else if (!voxelData.ContainsKey(pos - FORWARD) && pos.z > dims.worldBack)
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos - FORWARD, data);
                    newPos = pos - FORWARD;
                    QueueUpdateVoxel(pos - FORWARD);
                    return true;
                }
                else if (!voxelData.ContainsKey(pos + FORWARD) && pos.z < dims.worldFront)
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos + FORWARD, data);
                    newPos = pos + FORWARD;
                    QueueUpdateVoxel(pos + FORWARD);
                    return true;
                }
                // corners
                else if (!voxelData.ContainsKey(pos - RIGHT - FORWARD) && pos.x > dims.worldLeft && pos.z > dims.worldBack) // level lower left
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos - RIGHT - FORWARD, data);
                    newPos = pos - RIGHT - FORWARD;
                    QueueUpdateVoxel(pos - RIGHT - FORWARD);
                    return true;
                }
                else if (!voxelData.ContainsKey(pos + RIGHT - FORWARD) && pos.x < dims.worldRight && pos.z > dims.worldBack) // level lower right
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos + RIGHT - FORWARD, data);
                    newPos = pos + RIGHT - FORWARD;
                    QueueUpdateVoxel(pos + RIGHT - FORWARD);
                    return true;
                }
                else if (!voxelData.ContainsKey(pos + RIGHT + FORWARD) && pos.x < dims.worldRight && pos.z < dims.worldFront) // level upper right
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos + RIGHT + FORWARD, data);
                    newPos = pos + RIGHT + FORWARD;
                    QueueUpdateVoxel(pos + RIGHT + FORWARD);
                    return true;
                }
                else if (!voxelData.ContainsKey(pos + FORWARD - RIGHT) && pos.x > dims.worldLeft && pos.z < dims.worldFront) // level upper left
                {
                    voxelData.Remove(pos);
                    voxelData.Add(pos + FORWARD - RIGHT, data);
                    newPos = pos + FORWARD - RIGHT;
                    QueueUpdateVoxel(pos + FORWARD - RIGHT);
                    return true;
                }
            }
            newPos = pos;
            QueueUpdateVoxel(pos);
            return false;
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
            if (!voxelData.ContainsKey(pos)) return;
            VoxelData data = voxelData[pos];
            data.staleTicks = 0;
            voxelData[pos] = data;
            worldUpdateActions.Enqueue(pos);
        }

        void UpdateVoxelAction(int3 pos)
        {
            if (!voxelData.ContainsKey(pos)) return;
            VoxelData data = voxelData[pos];
            
            if (data.staleTicks < 10)
            {
                bool wasUpdated = false;
                switch (data.t)
                {
                    case VoxelType.WATER:
                        int3 newpos = pos;
                        wasUpdated = WaterPhysics(pos, ref newpos);
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

    }
    #endregion
}
