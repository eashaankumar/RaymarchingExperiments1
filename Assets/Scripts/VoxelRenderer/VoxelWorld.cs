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
    [SerializeField]
    int worldLeft;
    [SerializeField]
    int worldRight;
    [SerializeField]
    int worldFront;
    [SerializeField]
    int worldBack;

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

    #region Voxel Physics Logic
    bool WaterPhysics(int3 pos)
    {
        VoxelType data = voxelData[pos];
        if (!SandPhysics(pos))
        {
            // side
            if (!voxelData.ContainsKey(pos - RIGHT) && pos.x > worldLeft)
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos - RIGHT, data);
                UpdateVoxel(pos  - RIGHT);
                return true;
            }
            else if (!voxelData.ContainsKey(pos  + RIGHT) && pos.x < worldRight)
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos  + RIGHT, data);
                UpdateVoxel(pos  + RIGHT);
                return true;
            }
            else if (!voxelData.ContainsKey(pos  - FORWARD) && pos.z > worldBack)
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos  - FORWARD, data);
                UpdateVoxel(pos  - FORWARD);
                return true;
            }
            else if (!voxelData.ContainsKey(pos + FORWARD) && pos.z < worldFront)
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos  + FORWARD, data);
                UpdateVoxel(pos  + FORWARD);
                return true;
            }
            // corners
            else if (!voxelData.ContainsKey(pos  - RIGHT - FORWARD) && pos.x > worldLeft && pos.z > worldBack) // level lower left
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos  - RIGHT - FORWARD, data);
                UpdateVoxel(pos  - RIGHT - FORWARD);
                return true;
            }
            else if (!voxelData.ContainsKey(pos  + RIGHT - FORWARD) && pos.x < worldRight && pos.z > worldBack) // level lower right
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos  + RIGHT - FORWARD, data);
                UpdateVoxel(pos  + RIGHT - FORWARD);
                return true;
            }
            else if (!voxelData.ContainsKey(pos  + RIGHT + FORWARD) && pos.x < worldRight && pos.z < worldFront) // level upper right
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos  + RIGHT + FORWARD, data);
                UpdateVoxel(pos  + RIGHT + FORWARD);
                return true;
            }
            else if (!voxelData.ContainsKey(pos  + FORWARD - RIGHT) && pos.x > worldLeft && pos.z < worldFront) // level upper left
            {
                voxelData.Remove(pos);
                voxelData.TryAdd(pos  + FORWARD - RIGHT, data);
                UpdateVoxel(pos  + FORWARD - RIGHT);
                return true;
            }
        }
        return false;
    }
    bool SandPhysics(int3 pos)
    {
        VoxelType data = voxelData[pos];
        if (!voxelData.ContainsKey(pos - UP) && pos.y > worldBottom)
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP, data);
            UpdateVoxel(pos - UP);
            return true;
        }
        // side
        else if (!voxelData.ContainsKey(pos - UP - RIGHT) && pos.y > worldBottom && pos.x > worldLeft)
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP - RIGHT, data);
            UpdateVoxel(pos - UP - RIGHT);
            return true;
        }
        else if (!voxelData.ContainsKey(pos - UP + RIGHT) && pos.y > worldBottom && pos.x < worldRight)
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP + RIGHT, data);
            UpdateVoxel(pos - UP + RIGHT);
            return true;
        }
        else if (!voxelData.ContainsKey(pos - UP - FORWARD) && pos.y > worldBottom && pos.z > worldBack)
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP - FORWARD, data);
            UpdateVoxel(pos - UP - FORWARD);
            return true;
        }
        else if (!voxelData.ContainsKey(pos - UP + FORWARD) && pos.y > worldBottom && pos.z < worldFront)
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP + FORWARD, data);
            UpdateVoxel(pos - UP + FORWARD);
            return true;
        }
        // corners
        else if (!voxelData.ContainsKey(pos - UP - RIGHT - FORWARD) && pos.y > worldBottom && pos.x > worldLeft && pos.z > worldBack) // bottom lower left
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP - RIGHT - FORWARD, data);
            UpdateVoxel(pos - UP - RIGHT - FORWARD);
            return true;
        }
        else if (!voxelData.ContainsKey(pos - UP + RIGHT - FORWARD) && pos.y > worldBottom && pos.x < worldRight && pos.z > worldBack) // bottom lower right
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP + RIGHT - FORWARD, data);
            UpdateVoxel(pos - UP + RIGHT - FORWARD);
            return true;
        }
        else if (!voxelData.ContainsKey(pos - UP + RIGHT + FORWARD) && pos.y > worldBottom && pos.x < worldRight && pos.z < worldFront) // bottom upper right
        {
            voxelData.Remove(pos);
            voxelData.TryAdd(pos - UP + RIGHT + FORWARD, data);
            UpdateVoxel(pos - UP + RIGHT + FORWARD);
            return true;
        }
        else if (!voxelData.ContainsKey(pos - UP + FORWARD - RIGHT) && pos.y > worldBottom && pos.x > worldLeft && pos.z < worldFront) // bottom upper left
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
        VoxelType data = voxelData[pos];
        switch (data)
        {
            case VoxelType.WATER:
                WaterPhysics(pos);
                break;
            case VoxelType.SAND:
                // if bottom cell is empty, move there
                if (!SandPhysics(pos))
                {
                    if (voxelData.ContainsKey(pos - UP) && voxelData[pos-UP] == VoxelType.WATER)
                    {
                        VoxelType data2 = voxelData[pos - UP];
                        voxelData.Remove(pos - UP);
                        voxelData.Remove(pos);

                        voxelData.TryAdd(pos - UP, data);
                        voxelData.TryAdd(pos, data2);

                        UpdateVoxel(pos - UP);
                        UpdateVoxel(pos);
                    }
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
