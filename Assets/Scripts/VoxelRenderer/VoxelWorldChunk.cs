using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public struct VoxelWorldChunk
{
    int3 id;
    public VoxelWorldChunk(int3 _id)
    {
        this.id = _id;
    }
}
