// GPU Instancer Pro
// Copyright (c) GurBu Technologies

#include "Packages/com.gurbu.gpui-pro/Runtime/Shaders/Include/GPUInstancerInput.hlsl"

#ifdef __INTELLISENSE__
#define SHADER_STAGE_VERTEX
//#define GPUI_NO_BUFFER
#endif // __INTELLISENSE__

#ifndef GPUI_PRO_CROWD_INPUT_INCLUDED
#define GPUI_PRO_CROWD_INPUT_INCLUDED

#if defined(GPUI_PRO_ACTIVE) && defined(SHADER_STAGE_VERTEX)
#define GPUI_PRO_CROWD_ACTIVE

uniform float4 gpuiSkinningValues;
int gpuiCrowdVertexID;
#define GPUI_totalNumberOfBones gpuiSkinningValues.x
#define GPUI_boneDataSize gpuiSkinningValues.y
#define GPUI_boneBufferAnimStartIndex gpuiSkinningValues.z
#define GPUI_boneBufferWidth gpuiSkinningValues.w
#define GPUI_vertexBoneDataIndex commandOptionalParams.x
#define GPUI_bindPoseNo commandOptionalParams.y

#ifdef GPUI_NO_BUFFER
uniform Texture2D<float4> shaderBoneBuffer;

float4x4 gpuiProGetBoneMatrix(uint boneIndex, float weight)
{
    uint boneBufferWidth = GPUI_boneBufferWidth;
    
    uint startIndex = GPUI_boneBufferAnimStartIndex;
    uint animIndex = (gpui_InstanceID * GPUI_totalNumberOfBones + boneIndex + GPUI_bindPoseNo * GPUI_boneDataSize) * 3 + startIndex;
    int3 idx = int3(animIndex % boneBufferWidth, (animIndex / boneBufferWidth), 0);

    return mul(
        float4x4(
            shaderBoneBuffer.Load(int3((animIndex + 0) % boneBufferWidth, ((animIndex + 0) / boneBufferWidth), 0)),
            shaderBoneBuffer.Load(int3((animIndex + 1) % boneBufferWidth, ((animIndex + 1) / boneBufferWidth), 0)),
            shaderBoneBuffer.Load(int3((animIndex + 2) % boneBufferWidth, ((animIndex + 2) / boneBufferWidth), 0)),
            float4(0, 0, 0, 1)), 
        float4x4(weight, 0, 0, 0, 0, weight, 0, 0, 0, 0, weight, 0, 0, 0, 0, weight));
}

float4 gpuiProGetBoneData(uint vertexID)
{
    uint boneBufferWidth = GPUI_boneBufferWidth;
    uint dataIndex = vertexID + GPUI_vertexBoneDataIndex;
    
    return shaderBoneBuffer.Load(int3(dataIndex % boneBufferWidth, (dataIndex / boneBufferWidth), 0));
}
#else
uniform StructuredBuffer<float4> shaderBoneBuffer;

float4x4 gpuiProGetBoneMatrix(uint boneIndex, float weight)
{
    uint startIndex = GPUI_boneBufferAnimStartIndex;
    uint animIndex = (gpui_InstanceID * GPUI_totalNumberOfBones + boneIndex + GPUI_bindPoseNo * GPUI_boneDataSize) * 3 + startIndex;
    return mul(
        float4x4(
            shaderBoneBuffer[animIndex],
            shaderBoneBuffer[animIndex + 1],
            shaderBoneBuffer[animIndex + 2],
            float4(0, 0, 0, 1)),
        float4x4(weight, 0, 0, 0, 0, weight, 0, 0, 0, 0, weight, 0, 0, 0, 0, weight));
}

float4 gpuiProGetBoneData(uint vertexID)
{
    return shaderBoneBuffer[vertexID + GPUI_vertexBoneDataIndex];
}
#endif

float4x4 gpuiProGetAnimationMatrix(uint vertexID)
{
    float4 boneData = gpuiProGetBoneData(vertexID);
    float4 boneIndex = floor(boneData);
    float4 boneWeight = frac(boneData) * 2.0;
        
#if defined(GPUI_CROWD_SKIN_WEIGHTS_1)
        // 1-bone path
        float4x4 animMatrix = gpuiProGetBoneMatrix(boneIndex.x, 1.0);
#elif defined(GPUI_CROWD_SKIN_WEIGHTS_2)
        // 2-bone path
        // Assume weights sum to 1 for boneWeight.x and boneWeight.y (normalize just in case)
        float w0 = boneWeight.x;
        float w1 = boneWeight.y;
        float total = w0 + w1;
        w0 /= total;
        w1 /= total;
        
        float4x4 animMatrix = gpuiProGetBoneMatrix(boneIndex.x, w0);
        animMatrix += gpuiProGetBoneMatrix(boneIndex.y, w1);
#else
        // 4-bone path
    float4x4 animMatrix = gpuiProGetBoneMatrix(boneIndex.x, boneWeight.x);
    if (boneWeight.y > 0.0)
        animMatrix += gpuiProGetBoneMatrix(boneIndex.y, boneWeight.y);
    if (boneWeight.z > 0.0)
        animMatrix += gpuiProGetBoneMatrix(boneIndex.z, boneWeight.z);
    if (boneWeight.w > 0.0)
        animMatrix += gpuiProGetBoneMatrix(boneIndex.w, boneWeight.w);
#endif
    
    return animMatrix;
}

void gpuiProCrowdSkinning(uint vertexID, inout float3 vertex, inout float3 normal, inout float3 tangent)
{
    if (GPUI_totalNumberOfBones > 0)
    {
        float4x4 animMatrix = gpuiProGetAnimationMatrix(vertexID);
        float3x3 animRot = (float3x3) animMatrix;
        vertex = mul(animRot, vertex) + animMatrix._14_24_34;
        normal = mul(animRot, normal);
        tangent = mul(animRot, tangent);
    }
}

void gpuiProCrowdSkinning(uint vertexID, inout float3 vertex, inout float3 normal)
{
    if (GPUI_totalNumberOfBones > 0)
    {
        float4x4 animMatrix = gpuiProGetAnimationMatrix(vertexID);
        float3x3 animRot = (float3x3) animMatrix;
        vertex = mul(animRot, vertex) + animMatrix._14_24_34;
        normal = mul(animRot, normal);
    }
}

void gpuiProCrowdSkinning(uint vertexID, inout float3 vertex)
{
    if (GPUI_totalNumberOfBones > 0)
    {
        float4x4 animMatrix = gpuiProGetAnimationMatrix(vertexID);
        float3x3 animRot = (float3x3) animMatrix;
        vertex = mul(animRot, vertex) + animMatrix._14_24_34;
    }
}

#else // GPUI_PRO_ACTIVE

void gpuiProCrowdSkinning(uint vertexID, inout float3 vertex, inout float3 normal, inout float3 tangent) {}
void gpuiProCrowdSkinning(uint vertexID, inout float3 vertex, inout float3 normal) {}
void gpuiProCrowdSkinning(uint vertexID, inout float3 vertex) {}

#endif // GPUI_PRO_ACTIVE

#endif // GPUI_PRO_CROWD_INPUT_INCLUDED