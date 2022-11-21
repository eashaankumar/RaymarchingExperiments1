using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;

public class VoxelWorld : MonoBehaviour
{
    public static VoxelWorld Instance;

    //CoroutineQueue worldUpdateActions;
    delegate void WorldUpdateAction();
    Queue<WorldUpdateAction> worldUpdateActions;

    public enum VoxelType
    {
        SAND = 0, DIRT, WATER
    }

    [NativeDisableParallelForRestriction]
    public NativeParallelHashMap<int3, VoxelType> voxelData;

    private void Awake()
    {
        voxelData = new NativeParallelHashMap<int3, VoxelType>(10000, Allocator.Persistent);
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
    }

    // Update is called once per frame
    void Update()
    {
        if (!VoxelRenderer.Instamce.RenderInProgress && worldUpdateActions.Count > 0)
        {
            worldUpdateActions.Dequeue()();
        }
        int x = UnityEngine.Random.Range(-100, 100);
        int y = UnityEngine.Random.Range(-100, 100);
        int z = UnityEngine.Random.Range(-100, 100);
        AddVoxel(new int3 ( x, y, z ), (VoxelType)UnityEngine.Random.Range(0, 3));
    }

    #region World Update Methods
    public void AddVoxel(int3 pos, VoxelType t)
    {
        worldUpdateActions.Enqueue(() => AddVoxelAction(pos, t));
    }
    void AddVoxelAction(int3 pos, VoxelType t)
    {
        voxelData.TryAdd(pos, t);
    }
    #endregion
}
