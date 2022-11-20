// https://www.youtube.com/watch?v=1GMdV6EbFtw

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class OctreeComponent : MonoBehaviour
{
    [SerializeField]
    float nodeMinSize = 1;

    Octree octree;

    public static float MIN_SIZE;

    void Start()
    {
        octree = new Octree(FindObjectsOfType<Collider>());
    }

    private void Update()
    {
        MIN_SIZE = nodeMinSize;
    }

    void OnDrawGizmos()
    {
        if (Application.isPlaying)
        {
            octree.rootNode.Draw();
        }
    }

    #region Helper classes
    public class Octree
    {
        public OctreeNode rootNode;

        public Octree(Collider[] worldObjects)
        {
            Bounds bounds = new Bounds();
            foreach(Collider obj in worldObjects)
            {
                bounds.Encapsulate(obj.bounds);
            }

            float maxSize = math.max(bounds.size.x, math.max(bounds.size.y, bounds.size.z));
            Vector3 sizeVector = new float3(maxSize, maxSize, maxSize);
            bounds.SetMinMax(bounds.center - sizeVector * 0.5f, bounds.center + sizeVector * 0.5f);
            rootNode = new OctreeNode(bounds.size.x, bounds.center);
            AddObjects(worldObjects);
        }

        public void AddObjects(Collider[] objects)
        {
            foreach(Collider go in objects)
            {
                rootNode.AddObject(go.bounds.center, go.bounds.size.x);
            }
        }
    }

    public struct OctreeBound
    {
        public float size;
        public float3 center;
    }

    public class OctreeNode
    {
        OctreeBound bounds;

        /// <summary>
        /// Top Half:
        /// A, B, C, D: back left, front left, front right, back right
        /// Bottom Half:
        /// E,F,G,H (same as top half)
        /// </summary>
        OctreeBound[] childBounds;
        OctreeNode[] children;

        public OctreeNode(float _size, float3 _center)
        {
            bounds = new OctreeBound()
            {
                size = _size,
                center = _center,
            };

            float quarter = bounds.size / 4.0f;
            float childLength = bounds.size / 2.0f;
            childBounds = new OctreeBound[8];
            childBounds[0] = new OctreeBound() { size = childLength, center = bounds.center + new float3(-1, 1, -1) * quarter };
            childBounds[1] = new OctreeBound() { size = childLength, center = bounds.center + new float3(-1, 1, 1) * quarter };
            childBounds[2] = new OctreeBound() { size = childLength, center = bounds.center + new float3(1, 1, 1) * quarter };
            childBounds[3] = new OctreeBound() { size = childLength, center = bounds.center + new float3(1, 1, -1) * quarter };

            childBounds[0] = new OctreeBound() { size = childLength, center = bounds.center + new float3(-1, -1, -1) * quarter };
            childBounds[1] = new OctreeBound() { size = childLength, center = bounds.center + new float3(-1, -1, 1) * quarter };
            childBounds[2] = new OctreeBound() { size = childLength, center = bounds.center + new float3(1, -1, 1) * quarter };
            childBounds[3] = new OctreeBound() { size = childLength, center = bounds.center + new float3(1, -1, -1) * quarter };
        }

        public void AddObject(float3 center, float size)
        {
            DivideAndAdd(center, size);
        }

        private void DivideAndAdd(float3 center, float size)
        {
            if (bounds.size <= MIN_SIZE)
            {
                return;
            }
            if (children == null)
                children = new OctreeNode[8];

            bool dividing = false;
            OctreeBound b = new OctreeBound() { center = center, size = size };
            for(int i = 0; i < 8; i++)
            {
                if (children[i] == null)
                {
                    children[i] = new OctreeNode(childBounds[i].size, childBounds[i].center);
                }
                if (Intersects(childBounds[i], b))
                {
                    dividing = true;
                    children[i].DivideAndAdd(b.center, b.size);
                    Debug.Log("Dividing");
                }
            }
            if (!dividing)
            {
                children = null; // TODO: Figure out a way to never create children in the first place
            }
        }

        private bool Intersects(OctreeBound bound1, OctreeBound bound2)
        {
            float3 amin = bound1.center - bound1.size / 2f;
            float3 amax = bound1.center + bound1.size / 2f;
            float3 bmin = bound2.center - bound2.size / 2f;
            float3 bmax = bound2.center + bound2.size / 2f;

            return amin.x <= bmax.x &&
                   amax.x >= bmin.x &&
                   amin.y <= bmax.y &&
                   amax.y >= bmin.y &&
                   amin.z <= bmax.z &&
                   amax.z >= bmin.z;
        }

        public void Draw()
        {
            Gizmos.color = new Color(0, 1, 0, 0.1f);
            Gizmos.DrawCube(bounds.center, Vector3.one * bounds.size);
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(bounds.center, Vector3.one * bounds.size);

            if (children != null)
            {
                foreach(var child in children)
                {
                    if (child != null)
                        child.Draw();
                }
            }
        }
    }
    #endregion
}


