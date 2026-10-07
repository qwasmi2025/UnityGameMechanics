// GPU Instancer Pro
// Copyright (c) GurBu Technologies

#ifndef GPU_INSTANCER_PRO_NOPRAGMA_INCLUDED
#define GPU_INSTANCER_PRO_NOPRAGMA_INCLUDED

#include "Packages/com.gurbu.gpui-pro/Runtime/Shaders/Include/GPUInstancerInput.hlsl"
#include "Packages/com.gurbu.gpui-pro/Runtime/Compute/Include/Matrix.hlsl"

#ifdef GPUI_PRO_ACTIVE
#ifdef unity_ObjectToWorld
    #undef unity_ObjectToWorld
#endif
#ifdef unity_WorldToObject
    #undef unity_WorldToObject
#endif
#ifdef unity_MatrixPreviousM
    #undef unity_MatrixPreviousM
#endif
#ifdef unity_MatrixPreviousMI
    #undef unity_MatrixPreviousMI
#endif

#ifdef GPUI_NO_BUFFER
float4 GetGPUIInstanceData(uint index)
{
    float indexX = ((index % maxTextureSize) + 0.5) / min(instanceDataBufferSize, maxTextureSize);
    float rowCount = ceil(instanceDataBufferSize / maxTextureSize);
    float rowIndex = floor(index / maxTextureSize) + 0.5;
    return tex2Dlod(gpuiInstanceDataBufferTexture, float4(indexX, rowIndex / rowCount, 0.0, 0.0));
}
float4x4 GetGPUITransformData(uint index)
{
    float indexX = ((index % maxTextureSize) + 0.5) / min(transformBufferSize, maxTextureSize);
    float rowCount = ceil(transformBufferSize / maxTextureSize) * 4.0;
    float rowIndex = floor(index / maxTextureSize) * 4.0 + 0.5;
    return float4x4(
        tex2Dlod(gpuiTransformBufferTexture, float4(indexX, (0.0 + rowIndex) / rowCount, 0.0, 0.0)),
        tex2Dlod(gpuiTransformBufferTexture, float4(indexX, (1.0 + rowIndex) / rowCount, 0.0, 0.0)),
        tex2Dlod(gpuiTransformBufferTexture, float4(indexX, (2.0 + rowIndex) / rowCount, 0.0, 0.0)),
        tex2Dlod(gpuiTransformBufferTexture, float4(indexX, (3.0 + rowIndex) / rowCount, 0.0, 0.0))
    );
}
#else
float4 GetGPUIInstanceData(uint index)
{
    return gpuiInstanceDataBuffer[index];
}
float4x4 GetGPUITransformData(uint index)
{
    return gpuiTransformBuffer[index];
}
#endif
#endif // GPUI_PRO_ACTIVE

void setupGPUI()
{
#ifdef GPUI_PRO_ACTIVE
    uint commandParamsIndex = commandParamsStartIndex + (unity_BaseCommandID - rsgCommandStartIndex) * 5;
    float4 baseCommandParams = GetGPUIInstanceData(commandParamsIndex);
    uint instanceDataBufferShift = round(baseCommandParams.x) * transformBufferSize;
    commandOptionalParams = float2(baseCommandParams.z, baseCommandParams.w);
    
    uint instanceIndex = unity_InstanceID + instanceDataBufferShift;
    
    float4 instanceData = GetGPUIInstanceData(instanceIndex);
    gpui_InstanceID = asuint(instanceData.x);
    unity_ObjectToWorld = GetGPUITransformData(gpui_InstanceID);
    unity_LODFade.x = instanceData.y;
    unity_LODFade.y = round(instanceData.y * 16.0);
    
    //float4x4 transformOffset;
    float4x4 transformOffset = float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1); // identity
    if (baseCommandParams.y > 0.5)
    {
        transformOffset = float4x4(
            GetGPUIInstanceData(commandParamsIndex + 1),
            GetGPUIInstanceData(commandParamsIndex + 2),
            GetGPUIInstanceData(commandParamsIndex + 3),
            float4(0, 0, 0, 1));
        unity_ObjectToWorld = mul(unity_ObjectToWorld, transformOffset);
    }
    unity_WorldToObject = GetInverseTransformMatrix(unity_ObjectToWorld);
    
#ifdef GPUI_ENABLE_OBJECT_MOTION_VECTOR
    if (hasPreviousFrameTransformBuffer == 1)
    {
        unity_MatrixPreviousM = gpuiPreviousFrameTransformBuffer[gpui_InstanceID];
        if (baseCommandParams.y > 0.5)
        {
            unity_MatrixPreviousM = mul(unity_MatrixPreviousM, transformOffset);
        }
        unity_MatrixPreviousMI = GetInverseTransformMatrix(unity_MatrixPreviousM);
    }
    else
    {
        unity_MatrixPreviousM = unity_ObjectToWorld;
        unity_MatrixPreviousMI = unity_WorldToObject;
    }
#elif defined(UNITY_PREV_MATRIX_M) && !defined(BUILTIN_TARGET_API)
    unity_MatrixPreviousM = unity_ObjectToWorld;
    unity_MatrixPreviousMI = unity_WorldToObject;
#endif // GPUI_ENABLE_OBJECT_MOTION_VECTOR
#ifdef GPUI_ENABLE_PER_INSTANCE_LIGHTPROBES
    if (hasPerInstanceLightProbes == 1)
    {
        uint shIndex = gpui_InstanceID * 8;
        unity_SHAr = gpuiPerInstanceLightProbesBuffer[shIndex];
        unity_SHAg = gpuiPerInstanceLightProbesBuffer[shIndex + 1];
        unity_SHAb = gpuiPerInstanceLightProbesBuffer[shIndex + 2];
        unity_SHBr = gpuiPerInstanceLightProbesBuffer[shIndex + 3];
        unity_SHBg = gpuiPerInstanceLightProbesBuffer[shIndex + 4];
        unity_SHBb = gpuiPerInstanceLightProbesBuffer[shIndex + 5];
        unity_SHC = gpuiPerInstanceLightProbesBuffer[shIndex + 6];
        unity_ProbesOcclusion = gpuiPerInstanceLightProbesBuffer[shIndex + 7];
    }
#endif // GPUI_ENABLE_PER_INSTANCE_LIGHTPROBES
#endif // GPUI_PRO_ACTIVE
}

// Dummy methods for Shader Graph
void gpuiDummy_float(float3 inPos, out float3 outPos)
{
    outPos = inPos;
}

void gpuiDummy_half(half3 inPos, out half3 outPos)
{
    outPos = inPos;
}

#endif // GPU_INSTANCER_PRO_NOPRAGMA_INCLUDED