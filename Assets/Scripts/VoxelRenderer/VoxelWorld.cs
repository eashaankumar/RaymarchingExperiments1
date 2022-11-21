using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;

public class VoxelWorld : MonoBehaviour
{
    public static VoxelWorld Instance;

    public enum VoxelType
    {
        SAND, DIRT, WATER
    }

    public NativeParallelHashMap<int3, VoxelType> voxelData;

    private void Awake()
    {
        voxelData = new NativeParallelHashMap<int3, VoxelType>(10000, Allocator.Persistent);
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
        int x = UnityEngine.Random.Range(-100, 100);
        int y = UnityEngine.Random.Range(-100, 100);
        int z = UnityEngine.Random.Range(-100, 100);
        voxelData.TryAdd(new int3 ( x, y, z ), VoxelType.SAND);
    }
}
