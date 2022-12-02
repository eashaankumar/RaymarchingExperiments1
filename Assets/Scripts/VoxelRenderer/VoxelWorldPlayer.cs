using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class VoxelWorldPlayer : MonoBehaviour
{
    [SerializeField]
    float moveSpeed;
    [SerializeField]
    float jumpSpeed;
    [SerializeField]
    float gravityAcc = 9.81f;
    [SerializeField, Tooltip("Player height in voxels")]
    int playerHeight;

    Vector3 velocity;

    #region helper constants
    int3 DOWN = new int3(0, -1, 0);
    #endregion

    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        float forwardInput = Input.GetAxis("Vertical");
        float lateralInput = Input.GetAxis("Horizontal");
        Vector3 moveDir = (transform.forward * forwardInput + transform.right * lateralInput).normalized;
        Vector3 moveVel = moveDir * moveSpeed;
        int3 playerEyesPosInVoxelSpace = ToInt3(transform.position);
        int3 playerFeetPosInVoxelSpace = playerEyesPosInVoxelSpace + DOWN * (playerHeight-1);
        Vector3 accelerationDueToGravity = Vector3.down * gravityAcc;
        bool isOnGround = false;
        int3 correctFeetVox = playerFeetPosInVoxelSpace;
        if (VoxelRenderer.Instance.RenderInProgress || VoxelWorld.Instance.Updating)
        {
            
        }
        else
        {
            for(int i = 0; i < playerHeight; i++)
            {
                bool found = false;
                correctFeetVox = playerEyesPosInVoxelSpace + DOWN * i;
                if (VoxelWorld.Instance.voxelData.ContainsKey(correctFeetVox))
                {
                    switch (VoxelWorld.Instance.voxelData[correctFeetVox].t)
                    {
                        case VoxelWorld.VoxelType.SAND:
                        case VoxelWorld.VoxelType.DIRT:
                            isOnGround = true;
                            found = true;
                            break;
                        default:
                            isOnGround = false;
                            break;
                    }
                    if(found) break;
                }
            }
        }
        if (isOnGround)
        {
            velocity.y = 0;
            if (Input.GetKey(KeyCode.Space))
            {
                velocity.y = jumpSpeed;
            }
            Vector3 newPos = transform.position + (velocity + moveVel) * Time.deltaTime;
            newPos.y = ToVec3(correctFeetVox - DOWN * (playerHeight)).y;
            transform.position = newPos;
            
        }
        else
        {
            velocity = (velocity + accelerationDueToGravity * Time.deltaTime);
            transform.position += (velocity + moveVel) * Time.deltaTime;
        }

    }

    #region helpers
    int3 ToInt3(Vector3 v)
    {
        return new int3(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), Mathf.FloorToInt(v.z));
    }

    Vector3 ToVec3(int3 p)
    {
        return new Vector3(p.x, p.y, p.z);
    }
    #endregion
}
