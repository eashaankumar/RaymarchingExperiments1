using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
public class VoxelWorldNoise : MonoBehaviour
{

    [SerializeField]
    public float frequency;
    [SerializeField]
    public float tintFrequency;
    [SerializeField]
    public int mountainHeight;
    [SerializeField]
    public float terrainNoiseImpact;
    public float tintNoiseImpact;

    public static VoxelWorldNoise Instance;

    FastNoiseLite terrainNoise;
    FastNoiseLite tintNoise;

    public FastNoiseLite TerrainNoise
    {
        get { return terrainNoise; }
    }

    public FastNoiseLite TintNoise
    {
        get { return tintNoise; }
    }

    private void Awake()
    {
        Instance = this;
        terrainNoise = new FastNoiseLite();
        terrainNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        terrainNoise.SetFrequency(frequency);

        tintNoise = new FastNoiseLite();
        tintNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        tintNoise.SetFrequency(tintFrequency);
    }

    float SurfaceNoise(int3 noiseSample)
    {
        float n = math.lerp(-1f, 1f, Mathf.InverseLerp(VoxelWorld.Instance.VoxWorldDims.worldBottom, mountainHeight, noiseSample.y));        
        return n + VoxelWorldNoise.Instance.TerrainNoise.GetNoise(noiseSample.x, noiseSample.y, noiseSample.z) * terrainNoiseImpact;
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
        return VoxelWorldNoise.Instance.TintNoise.GetNoise(noiseSample.x, noiseSample.y, noiseSample.z) * tintNoiseImpact;
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
