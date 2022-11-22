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

    public VoxelWorldDimensions VoxWorldDims
    {
        get { return dims; }
    }

    public static VoxelWorld Instance;

    //CoroutineQueue worldUpdateActions;
    delegate void WorldUpdateAction();
    Queue<WorldUpdateAction> worldUpdateActions;
    
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

    public NativeParallelHashMap<int3, VoxelData> voxelData;
    //public NativeParallelHashSet<int3> voxelsToUpdate;

    static int3 UP = new int3(0, 1, 0);
    static int3 RIGHT = new int3(1, 0, 0);
    static int3 FORWARD = new int3(0, 0, 1);

    private void Awake()
    {
        voxelData = new NativeParallelHashMap<int3, VoxelData>(10000, Allocator.Persistent);
        //voxelsToUpdate = new NativeParallelHashSet<int3>(10000, Allocator.Persistent);

        worldUpdateActions = new Queue<WorldUpdateAction>();
        Instance = this;
    }
    // Start is called before the first frame update
    void Start()
    {
        int x = (dims.worldLeft + dims.worldRight) / 2;
        int y = (dims.worldBottom);
        int z = (dims.worldBack + dims.worldFront) / 2;

        AddVoxel(new int3(x, y, z), new VoxelData (){ t=VoxelType.SAND, tint = 0});

        for (int i = 0; i < 1; i++)
        {
            Thread updateWorldThread = new Thread(() => ThreadedUpdate());
            updateWorldThread.IsBackground = true;
            updateWorldThread.Priority = System.Threading.ThreadPriority.Highest;
            updateWorldThread.Start();
        }
    }

    private void OnDestroy()
    {
        voxelData.Dispose();
        //voxelsToUpdate.Dispose();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private System.Object thisLock = new System.Object();
    void ThreadedUpdate()
    {
        while(true)
        {
            int processed = 0;
            VoxelRenderer.Instance.hasLock = false;
            while (!VoxelRenderer.Instance.RenderInProgress && worldUpdateActions.Count > 0 && processed < updateRequestsProcessCount)
            {
                lock (thisLock)
                {
                    WorldUpdateAction a = worldUpdateActions.Dequeue();
                    if (a != null) a();
                }
                processed++;
            }
            VoxelRenderer.Instance.hasLock = true;
            Thread.Sleep(10);
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
                voxelData.TryAdd(pos - RIGHT, data);
                newPos = pos - RIGHT;
                UpdateVoxel(pos  - RIGHT);
                return true;
            }
            else if (!voxelData.ContainsKey(pos  + RIGHT) && pos.x < dims.worldRight)
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos  + RIGHT, data);
                newPos = pos + RIGHT;
                UpdateVoxel(pos  + RIGHT);
                return true;
            }
            else if (!voxelData.ContainsKey(pos  - FORWARD) && pos.z > dims.worldBack)
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos  - FORWARD, data);
                newPos = pos - FORWARD;
                UpdateVoxel(pos  - FORWARD);
                return true;
            }
            else if (!voxelData.ContainsKey(pos + FORWARD) && pos.z < dims.worldFront)
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos  + FORWARD, data);
                newPos = pos + FORWARD;
                UpdateVoxel(pos  + FORWARD);
                return true;
            }
            // corners
            else if (!voxelData.ContainsKey(pos  - RIGHT - FORWARD) && pos.x > dims.worldLeft && pos.z > dims.worldBack) // level lower left
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos  - RIGHT - FORWARD, data);
                newPos = pos - RIGHT - FORWARD;
                UpdateVoxel(pos  - RIGHT - FORWARD);
                return true;
            }
            else if (!voxelData.ContainsKey(pos  + RIGHT - FORWARD) && pos.x < dims.worldRight && pos.z > dims.worldBack) // level lower right
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos  + RIGHT - FORWARD, data);
                newPos = pos + RIGHT - FORWARD;
                UpdateVoxel(pos  + RIGHT - FORWARD);
                return true;
            }
            else if (!voxelData.ContainsKey(pos  + RIGHT + FORWARD) && pos.x < dims.worldRight && pos.z < dims.worldFront) // level upper right
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos  + RIGHT + FORWARD, data);
                newPos = pos + RIGHT + FORWARD;
                UpdateVoxel(pos  + RIGHT + FORWARD);
                return true;
            }
            else if (!voxelData.ContainsKey(pos  + FORWARD - RIGHT) && pos.x > dims.worldLeft && pos.z < dims.worldFront) // level upper left
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos  + FORWARD - RIGHT, data);
                newPos = pos + FORWARD - RIGHT;
                UpdateVoxel(pos  + FORWARD - RIGHT);
                return true;
            }
        }
        newPos = pos;
        UpdateVoxel(pos);
        return false;
    }
    bool SandPhysics(int3 pos)
    {
        if (!voxelData.ContainsKey(pos)) return false;
        VoxelData data = voxelData[pos];
        if (!voxelData.ContainsKey(pos - UP) && pos.y > dims.worldBottom)
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP, data);
            UpdateVoxel(pos - UP);
            return true;
        }
        // side
        else if (!voxelData.ContainsKey(pos - UP - RIGHT) && pos.y > dims.worldBottom && pos.x > dims.worldLeft)
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP - RIGHT, data);
            UpdateVoxel(pos - UP - RIGHT);
            return true;
        }
        else if (!voxelData.ContainsKey(pos - UP + RIGHT) && pos.y > dims.worldBottom && pos.x < dims.worldRight)
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP + RIGHT, data);
            UpdateVoxel(pos - UP + RIGHT);
            return true;
        }
        else if (!voxelData.ContainsKey(pos - UP - FORWARD) && pos.y > dims.worldBottom && pos.z > dims.worldBack)
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP - FORWARD, data);
            UpdateVoxel(pos - UP - FORWARD);
            return true;
        }
        else if (!voxelData.ContainsKey(pos - UP + FORWARD) && pos.y > dims.worldBottom && pos.z < dims.worldFront)
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP + FORWARD, data);
            UpdateVoxel(pos - UP + FORWARD);
            return true;
        }
        // corners
        else if (!voxelData.ContainsKey(pos - UP - RIGHT - FORWARD) && pos.y > dims.worldBottom && pos.x > dims.worldLeft && pos.z > dims.worldBack) // bottom lower left
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP - RIGHT - FORWARD, data);
            UpdateVoxel(pos - UP - RIGHT - FORWARD);
            return true;
        }
        else if (!voxelData.ContainsKey(pos - UP + RIGHT - FORWARD) && pos.y > dims.worldBottom && pos.x < dims.worldRight && pos.z > dims.worldBack) // bottom lower right
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP + RIGHT - FORWARD, data);
            UpdateVoxel(pos - UP + RIGHT - FORWARD);
            return true;
        }
        else if (!voxelData.ContainsKey(pos - UP + RIGHT + FORWARD) && pos.y > dims.worldBottom && pos.x < dims.worldRight && pos.z < dims.worldFront) // bottom upper right
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP + RIGHT + FORWARD, data);
            UpdateVoxel(pos - UP + RIGHT + FORWARD);
            return true;
        }
        else if (!voxelData.ContainsKey(pos - UP + FORWARD - RIGHT) && pos.y > dims.worldBottom && pos.x > dims.worldLeft && pos.z < dims.worldFront) // bottom upper left
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP + FORWARD - RIGHT, data);
            UpdateVoxel(pos - UP + FORWARD - RIGHT);
            return true;
        }
        return false;
    }
    #endregion

    #region World Update Methods
    public void UpdateVoxel(int3 pos)
    {
        if (voxelData.ContainsKey(pos))
            worldUpdateActions.Enqueue(() => UpdateVoxelAction(pos));
    }
    void UpdateVoxelAction(int3 pos)
    {
        if (!voxelData.ContainsKey(pos)) return;
        VoxelData data = voxelData[pos];
        bool wasUpdated = false;
        switch (data.t)
        {
            case VoxelType.WATER:
                int3 newpos = pos;
                wasUpdated = WaterPhysics(pos, ref newpos);
                if (!wasUpdated) 
                {
                    /*data.staleTicks++;
                    voxelData[newpos] = data;
                    if (data.staleTicks < 10)
                    {
                        UpdateVoxel(newpos);
                    }*/
                }
                else
                {
                    /*VoxelData temp = voxelData[newpos];
                    temp.staleTicks = 0;
                    voxelData[newpos] = temp;*/
                    //UpdateVoxel(newpos);
                }
                break;
            case VoxelType.SAND:
                // if bottom cell is empty, move there
                if (!SandPhysics(pos))
                {
                    if (voxelData.ContainsKey(pos - UP) && voxelData[pos-UP].t == VoxelType.WATER)
                    {
                        VoxelData data2 = voxelData[pos - UP];
                        voxelData.Remove(pos - UP);
                        voxelData.Remove(pos);

                        voxelData.TryAdd(pos - UP, data);
                        voxelData.TryAdd(pos, data2);

                        //UpdateVoxel(pos - UP);
                        //UpdateVoxel(pos);
                        wasUpdated = true;
                    }
                    //UpdateVoxel(pos);
                }
                else
                {
                    wasUpdated = true;
                }
                break;
        }
        /*if (wasUpdated)
        {
            for(int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        int3 p = pos + new int3(x, y, z);
                        if (voxelData.ContainsKey(p)) UpdateVoxel(p);
                    }
                }
            }
        }*/
    }


    public void AddVoxel(int3 pos, VoxelData t)
    {
        worldUpdateActions.Enqueue(() => AddVoxelAction(pos, t));
    }
    void AddVoxelAction(int3 pos, VoxelData t)
    {
        if (voxelData.TryAdd(pos, t))
        {
            switch (t.t)
            {
                case VoxelType.WATER:
                case VoxelType.SAND:
                    UpdateVoxel(pos);
                    break;
            }
        }
    }
    #endregion

    #region Update Jobs
    [BurstCompile]
    struct RenderJob : IJobParallelFor
    {
        public void Execute(int index)
        {
            
        }
    }
    #endregion
}
