// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace GPUInstancerPro.CrowdAnimations
{
    [Unity.Burst.BurstCompile]
    internal unsafe struct GPUIApplyRootMotionJob : IJobParallelForTransform
    {
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_matrixArray; // Matrix4x4
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_crowdInstanceDataBuffer; // GPUICrowdInstanceData
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_animatorClipDataArray; // GPUICrowdAnimatorClipData
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_clipFramesAndWeightsArray; // Vector4
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_bakedRootMotionData; // GPUICrowdRootMotion
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_bakedClipDataArray; // GPUICrowdBakedClipData
        [ReadOnly] public int bakedClipDataCount;
        [ReadOnly] public int bufferStartIndex;
        [ReadOnly] public int instanceCount;
        [ReadOnly] public int boneCount;
        [ReadOnly] public float currentTime;
        [ReadOnly] public float deltaTime;

        public void Execute(int index, TransformAccess transform)
        {
            if (index >= instanceCount || boneCount <= 0)
                return;
            int renderGroupIndex = index + bufferStartIndex;
            GPUICrowdInstanceData crowdInstanceData = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdInstanceData>(p_crowdInstanceDataBuffer, renderGroupIndex, GPUICrowdInstanceData.STRIDE);
            if (!crowdInstanceData.ApplyCustomRootMotion || !transform.isValid)
                return;

            bool hasRootMotion = false;
            bool hasRotation = false;
            int animatorClipDataIndex = renderGroupIndex * GPUICrowdConstants.ANIMATOR_MAX_CLIPS;
            int clipFramesAndWeightsIndex = renderGroupIndex * 2;
            Vector4 weights = UnsafeUtility.ReadArrayElementWithStride<Vector4>(p_clipFramesAndWeightsArray, clipFramesAndWeightsIndex + 1, 16);
            Matrix4x4 transformMatrix = UnsafeUtility.ReadArrayElementWithStride<Matrix4x4>(p_matrixArray, index, 64);
            if (transformMatrix.m33 != 1)
                return;
            float3 position;
            position.x = transformMatrix.m03;
            position.y = transformMatrix.m13;
            position.z = transformMatrix.m23;
            quaternion rotation = transformMatrix.rotation;

            for (int i = 0; i < GPUICrowdConstants.ANIMATOR_MAX_CLIPS; i++)
            {
                float weight = weights[i];
                if (weight <= 0)
                    continue;
                GPUICrowdAnimatorClipData animatorClipData = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdAnimatorClipData>(p_animatorClipDataArray, animatorClipDataIndex + i, GPUICrowdAnimatorClipData.STRIDE);
                if (!animatorClipData.IsValid)
                    continue;
                float normalizedClipTime = animatorClipData.GetNormalizedTime(currentTime);
                if (!animatorClipData.IsLooping && normalizedClipTime >= 1.0f) // not looping and animation finished
                    continue;

                int bakedClipIndex = animatorClipData.BakedClipIndex;
                if (bakedClipIndex < 0 || bakedClipIndex >= bakedClipDataCount)
                    continue;
                GPUICrowdBakedClipData bakedClipData = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdBakedClipData>(p_bakedClipDataArray, bakedClipIndex, GPUICrowdBakedClipData.STRIDE);
                if (bakedClipData.bakedRootMotionIndex < 0)
                    continue;

                int currentFrame = Mathf.CeilToInt(math.frac(normalizedClipTime) * (animatorClipData.FrameCount - 1f));
                GPUICrowdRootMotion rootMotion = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdRootMotion>(p_bakedRootMotionData, bakedClipData.bakedRootMotionIndex + currentFrame, GPUICrowdRootMotion.STRIDE);
                if (rootMotion.motionType == 0)
                    continue;
                hasRootMotion = true;
                float frameRate = bakedClipData.clipFrameCount / bakedClipData.clipLength;
                float clipSpeed = bakedClipData.clipLength / animatorClipData.SpeedRelativeClipLength;
                float lerpAmount = deltaTime * frameRate * clipSpeed * weight;
                position += math.mul(rotation, (rootMotion.position * lerpAmount));
                if (rootMotion.motionType == 2)
                {
                    hasRotation = true;
                    rotation = math.slerp(rotation, math.mul(rotation, rootMotion.rotation), lerpAmount);
                }
            }

            if (hasRootMotion)
            {
                transform.position = position;
                if (hasRotation)
                    transform.rotation = rotation;
                UnsafeUtility.WriteArrayElementWithStride(p_matrixArray, index, 64, transform.localToWorldMatrix);
            }
        }
    }
}