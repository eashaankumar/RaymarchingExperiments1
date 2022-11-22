using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;

public class VoxelWorld : MonoBehaviour
{

    [Header("World update settings.\nAvailable frame = frame when VoxelRenderer is not rendering")]
    [SerializeField, Tooltip("How many update requests are attempted to be processed per available frame")]
    int updateRequestsProcessCount;

    [Header("World settings")]
    [SerializeField]
    int worldBottom;

    public static VoxelWorld Instance;

    //CoroutineQueue worldUpdateActions;
    delegate void WorldUpdateAction();
    Queue<WorldUpdateAction> worldUpdateActions;

    public enum VoxelType
    {
        SAND = 0, DIRT, WATER
    }

    public NativeParallelHashMap<int3, VoxelType> voxelData;
    //public NativeParallelHashSet<int3> voxelsToUpdate;

    static int3 UP = new int3(0, 1, 0);
    static int3 RIGHT = new int3(1, 0, 0);
    static int3 FORWARD = new int3(0, 0, 1);

    private void Awake()
    {
        voxelData = new NativeParallelHashMap<int3, VoxelType>(10000, Allocator.Persistent);
        //voxelsToUpdate = new NativeParallelHashSet<int3>(10000, Allocator.Persistent);

        worldUpdateActions = new Queue<WorldUpdateAction>();
        Instance = this;
    }
    // Start is called before the first frame update
    void Start()
    {
        
    }

    private void OnDestroy()
    {
        voxelData.Dispose();
        //voxelsToUpdate.Dispose();
    }

    // Update is called once per frame
    void Update()
    {
        int processed = 0;
        while (!VoxelRenderer.Instance.RenderInProgress && worldUpdateActions.Count > 0 && processed < updateRequestsProcessCount)
        {
            worldUpdateActions.Dequeue()();
            processed++;
        }

        for (int i = 0; i < 5; i++)
        {
            int x = UnityEngine.Random.Range(-100, 100);
            int y = UnityEngine.Random.Range(-100, 100);
            int z = UnityEngine.Random.Range(-100, 100);
            AddVoxel(new int3(x, y, z), (VoxelType)UnityEngine.Random.Range(0, 3));
        }
    }

    #region World Update Methods
    public void UpdateVoxel(int3 pos)
    {
        if (voxelData.ContainsKey(pos))
            worldUpdateActions.Enqueue(() => UpdateVoxelAction(pos));
    }
    void UpdateVoxelAction(int3 pos)
    {
        if (!voxelData.ContainsKey(pos)) return;
        if (pos.y == worldBottom) return;
        VoxelType data = voxelData[pos];
        switch (data)
        {
            case VoxelType.WATER:
            case VoxelType.SAND:
                // if bottom cell is empty, move there
                if (!voxelData.ContainsKey(pos - UP))
                {
                    voxelData.Remove(pos);
                    voxelData.TryAdd(pos - UP, data);
                    UpdateVoxel(pos - UP);
                }
                else if (!voxelData.ContainsKey(pos - UP - RIGHT))
                {
                    voxelData.Remove(pos);
                    voxelData.TryAdd(pos - UP - RIGHT, data);
                    UpdateVoxel(pos - UP - RIGHT);
                }
                else if (!voxelData.ContainsKey(pos - UP + RIGHT))
                {
                    voxelData.Remove(pos);
                    voxelData.TryAdd(pos - UP + RIGHT, data);
                    UpdateVoxel(pos - UP + RIGHT);
                }
                else if (!voxelData.ContainsKey(pos - UP - FORWARD))
                {
                    voxelData.Remove(pos);
                    voxelData.TryAdd(pos - UP - FORWARD, data);
                    UpdateVoxel(pos - UP - FORWARD);
                }
                else if (!voxelData.ContainsKey(pos - UP + FORWARD))
                {
                    voxelData.Remove(pos);
                    voxelData.TryAdd(pos - UP + FORWARD, data);
                    UpdateVoxel(pos - UP + FORWARD);
                }
                break;
        }
    }


    public void AddVoxel(int3 pos, VoxelType t)
    {
        worldUpdateActions.Enqueue(() => AddVoxelAction(pos, t));
    }
    void AddVoxelAction(int3 pos, VoxelType t)
    {
        if (voxelData.TryAdd(pos, t))
        {
            switch (t)
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
