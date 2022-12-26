using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class SPHRenderer : MonoBehaviour
{
    public float range;
    public Color waterColor;
    public float waterFoamingFalloff;
    public float viscosity = 0.018f;
    public Vector3 boundary;
    public float dampingCoefficient = -0.9f;
    public float simSpeed;
    public float maxVelocity;
    public float maxAcceleration;
    public float maxSoundSpeed;

    public Material material;

    private ComputeBuffer meshPropertiesBuffer;
    private ComputeBuffer argsBuffer;

    public Mesh mesh;
    private Bounds bounds;

    CoroutineQueue simQ;
    struct Particle
    {
        public float3 pos;
        public float3 vel;
        public float mass;
        public float pressure;
        public float density;
    }

    private NativeList<Particle> particles;
    int particleCount;
    

    // Mesh Properties struct to be read from the GPU.
    // Size() is a convenience funciton which returns the stride of the struct.
    private struct MeshProperties
    {
        public float4x4 mat;
        public float4 color;

        public static int Size()
        {
            return
                sizeof(float) * 4 * 4 + // matrix;
                sizeof(float) * 4;      // color;
        }
    }

    private void Awake()
    {
        particles = new NativeList<Particle>(Allocator.Persistent);
        simQ = new CoroutineQueue(this);
        simQ.StartLoop();
    }

    private void Start()
    {
        simQ.EnqueueAction(Sim(), "Sim");
    }

    private void OnDestroy()
    {
        if (particles.IsCreated) particles.Dispose();
        simQ.StopLoop();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(Vector3.zero, boundary);
    }

    private void OnGUI()
    {
        GUI.color = Color.black;
        GUI.Label(new Rect(10, 10, 1000, 1000), particleCount + "");
    }

    IEnumerator Sim()
    {
        // User Input
        if (Input.GetMouseButton(0))
        {
            for(int i = 0; i < 100; i++)
            {
                particles.Add(new Particle { mass = 1, pos = new float3(math.sin(i), 10, 0), vel = new float3(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value) * 10 });
            }
        }

        // Boundary surrounding the meshes we will be drawing.  Used for occlusion.
        bounds = new Bounds(transform.position, Vector3.one * (range + 1));

        // Initialize buffer with the given population.
        ParticlePressureJob pressureJob = new ParticlePressureJob()
        {
            particles = particles,
            smoothingRadius = 0.2f,
            pressureConstant = 20f,
            fluidDensity = 20f,
        };
        JobHandle handle = pressureJob.Schedule(particles.Length, 128);
        yield return new WaitUntil(() => handle.IsCompleted);
        handle.Complete();

        yield return null;

        NativeArray<MeshProperties> meshPropertiesArray = new NativeArray<MeshProperties>(particles.Length, Allocator.TempJob);
        ParticleUpdateJob updateJob = new ParticleUpdateJob()
        {
            meshPropertiesArray = meshPropertiesArray,
            particles = particles,
            accGravity = 9.81f,
            timeStepSize = Time.deltaTime * simSpeed,
            maxVel = maxVelocity,
            maxAcc = maxAcceleration,
            maxSoundSpeed = maxSoundSpeed,
            waterColor = new float4(waterColor.r, waterColor.g, waterColor.b, waterColor.a),
            waterFoamingFalloff = waterFoamingFalloff,
            smoothingRadius = 0.2f,
            pressureConstant = 20f,
            fluidDensity = 20f,
            viscosity = viscosity,
            boundary = new float3(boundary.x, boundary.y, boundary.z),
            dampingCoefficient = dampingCoefficient,
        };
        handle = updateJob.Schedule(particles.Length, 64);
        yield return new WaitUntil(() => handle.IsCompleted);
        handle.Complete();

        if (meshPropertiesArray.Length > 0)
        {
            MeshProperties[] properties = meshPropertiesArray.ToArray();
            meshPropertiesArray.Dispose();
            InitializeBuffers(properties);
        }
        else
        {
            meshPropertiesArray.Dispose();
        }

        particleCount = particles.Length;

        // restart loop
        simQ.EnqueueAction(Sim(), "Sim");
        yield return CoroutineQueueResult.PASS;
        yield break;
    }

    private void InitializeBuffers(MeshProperties[] properties)
    {
        // Argument buffer used by DrawMeshInstancedIndirect.
        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        // Arguments for drawing mesh.
        // 0 == number of triangle indices, 1 == population, others are only relevant if drawing submeshes.
        args[0] = (uint)mesh.GetIndexCount(0);
        args[1] = (uint)properties.Length;
        args[2] = (uint)mesh.GetIndexStart(0);
        args[3] = (uint)mesh.GetBaseVertex(0);
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);

        meshPropertiesBuffer = new ComputeBuffer(properties.Length, MeshProperties.Size());
        meshPropertiesBuffer.SetData(properties);
        material.SetBuffer("_Properties", meshPropertiesBuffer);
    }

    private void Update()
    {
        // rendering
        if ( argsBuffer != null)
        {
            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, argsBuffer);
        }
    }

    private void OnDisable()
    {
        // Release gracefully.
        if (meshPropertiesBuffer != null)
        {
            meshPropertiesBuffer.Release();
        }
        meshPropertiesBuffer = null;

        if (argsBuffer != null)
        {
            argsBuffer.Release();
        }
        argsBuffer = null;
    }

    [BurstCompile]
    struct ParticlePressureJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeList<Particle> particles;

        [ReadOnly] public float smoothingRadius;
        [ReadOnly] public float pressureConstant;
        [ReadOnly] public float fluidDensity;

        public void Execute(int index)
        {
            UpdateParticle(index);
        }

        public void UpdateParticle(int index)
        {
            if (index < 0 || index >= particles.Length)
            {
                return;
            }
            
            // Physics update
            Particle p = particles[index];

            // SPH: http://rlguy.com/sphfluidsim/
            float density = 0;
            /*for (int i = 0; i < particles.Length; i++)
            {
                if (i == index) continue;
                Particle neighbor = particles[i];
                float r = math.distance(p.pos, neighbor.pos);
                if (r > smoothingRadius) continue;
                density += neighbor.mass * Poly6SmoothingKernel(r);
            }*/
            if (density < fluidDensity) density = fluidDensity;
            p.density = density;
            p.pressure = pressureConstant * (density - fluidDensity);
            particles[index] = p;
        }

        public float Poly6SmoothingKernel(float r)
        {
            return (315 / (64 * math.PI * math.pow(smoothingRadius, 9))) * math.pow(smoothingRadius * smoothingRadius - r * r, 3);
        }
    }

    [BurstCompile]
    struct ParticleUpdateJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeList<Particle> particles;
        [NativeDisableParallelForRestriction]
        public NativeArray<MeshProperties> meshPropertiesArray;

        [ReadOnly] public float timeStepSize;
        [ReadOnly] public float maxVel;
        [ReadOnly] public float maxAcc;
        [ReadOnly] public float maxSoundSpeed;
        [ReadOnly] public float accGravity;
        [ReadOnly] public float4 waterColor;
        [ReadOnly] public float waterFoamingFalloff;
        [ReadOnly] public float smoothingRadius;
        [ReadOnly] public float pressureConstant;
        [ReadOnly] public float fluidDensity;
        [ReadOnly] public float viscosity;
        [ReadOnly] public float3 boundary;
        [ReadOnly] public float dampingCoefficient;

        public void Execute(int index)
        {
            UpdateParticle(index);
        }

        public void UpdateParticle(int index)
        {
            if (index < 0 || index >= particles.Length)
            {
                return;
            }

            // Physics update
            Particle p = particles[index];

            // SPH: http://rlguy.com/sphfluidsim/
            float3 accp = 0, accvis = 0;
            /*for (int i = 0; i < particles.Length; i++)
            {

                if (i == index) continue;
                Particle neighbor = particles[i];
                float r = math.distance(p.pos, neighbor.pos);
                if (r > smoothingRadius) continue;
                float3 r_v = math.normalize(p.pos - neighbor.pos);
                // acceleration caused by mass density pressure 
                accp += (neighbor.mass / p.mass) * ((p.pressure + neighbor.pressure) / (2 * neighbor.density * p.density)) * KernelGrad(r) * r_v;
                // acceleration caused by viscosity
                accvis += (neighbor.mass / p.mass) * (1 / neighbor.density) * (neighbor.vel - p.vel) * KernelGradGrad(r) * r_v;
            }*/
            accp *= -1;
            accvis *= viscosity;

        

            //float deltaTime = math.max(math.max(timeStepSize * smoothingRadius / maxVel, math.sqrt(smoothingRadius / maxAcc)), timeStepSize * smoothingRadius / maxSoundSpeed);
            float deltaTime = timeStepSize;

            p.vel += (accvis) * deltaTime;
            p.vel += (accp) * deltaTime;

            // Collision
            bool collision = false;
            if (p.pos.y <= -boundary.y/2)
            {
                p.pos.y = -boundary.y / 2;
                p.vel.y *= dampingCoefficient;
                collision = true;
            }
            if (p.pos.y >= boundary.y / 2)
            {
                p.pos.y = boundary.y / 2;
                p.vel.y *= dampingCoefficient;
                collision = true;
            }
            if (p.pos.x <= -boundary.x / 2)
            {
                p.pos.x = -boundary.x / 2;
                p.vel.x *= dampingCoefficient;
                collision = true;
            }
            if (p.pos.x >= boundary.x / 2)
            {
                p.pos.x = boundary.x / 2;
                p.vel.x *= dampingCoefficient;
                collision = true;
            }
            if (p.pos.z <= -boundary.z / 2)
            {
                p.pos.z = -boundary.z / 2;
                p.vel.z *= dampingCoefficient;
                collision = true;
            }
            if (p.pos.z >= boundary.z / 2)
            {
                p.pos.z = boundary.z / 2;
                p.vel.z *= dampingCoefficient;
                collision = true;
            }
            if (collision)
            {
                //p.vel *= 0.9f;
            }

            // Gravity
            p.vel += new float3(0, -accGravity * deltaTime, 0);
            p.vel = math.normalize(p.vel) * math.min(math.length(p.vel), maxVel);
            p.pos += p.vel * deltaTime;
            particles[index] = p;

            // Properties
            MeshProperties props = new MeshProperties();
            quaternion rotation = quaternion.identity;
            float3 scale = float3.zero + 1;

            props.mat = float4x4.TRS(p.pos, rotation, scale);

            float waterFoamColor = 1 - math.exp(-math.length(p.vel / waterFoamingFalloff));
            props.color = math.lerp(waterColor, float4.zero + 1, waterFoamColor);

            meshPropertiesArray[index] = props;
        }

        public float KernelGrad(float r)
        {
            return -(45 / (math.PI * math.pow(smoothingRadius, 6))) * math.pow(smoothingRadius - r, 2);
        }

        public float KernelGradGrad(float r)
        {
            float r3 = math.pow(r, 3);
            float r2 = math.pow(r, 2);
            float h3 = math.pow(smoothingRadius, 3);
            float h2 = math.pow(smoothingRadius, 2);

            return -(r3 / (2 * h3)) + r2 / h2 + smoothingRadius / (2 * r) - 1;
        }
    }
}
