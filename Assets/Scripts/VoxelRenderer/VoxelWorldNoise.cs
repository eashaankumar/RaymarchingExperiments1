using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VoxelWorldNoise : MonoBehaviour
{

    [SerializeField]
    float frequency;
    [SerializeField]
    float tintFrequency;

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

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
