// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace GPUInstancerPro.CrowdAnimations
{
    [Unity.Burst.BurstCompile]
    internal unsafe struct GPUIApplyTransitionsJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_animatorClipDataArray; // GPUICrowdAnimatorClipData
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_clipFramesAndWeightsArray; // Vector4
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_bakedClipDataArray; // GPUICrowdBakedClipData
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_transitionsArray; // GPUICrowdTransition
        [ReadOnly] public float currentTime;
        [ReadOnly] public int clipFramesAndWeightsArraySize;

        public void Execute(int index)
        {
            GPUICrowdTransition transition = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdTransition>(p_transitionsArray, index, GPUICrowdTransition.STRIDE);
            int weightIndex = transition.bufferIndex * 2 + 1;
            if (transition.transitionLength <= 0f || weightIndex >= clipFramesAndWeightsArraySize)
                return;

            Vector4 newWeights; 
            if (transition.startTime + transition.transitionLength <= currentTime)
                newWeights = transition.targetWeights;
            else
                newWeights = Vector4.Lerp(transition.startWeights, transition.targetWeights, (currentTime - transition.startTime) / transition.transitionLength);
            UnsafeUtility.WriteArrayElementWithStride(p_clipFramesAndWeightsArray, weightIndex, 16, newWeights);

            int animationClipIndex = transition.bufferIndex * 4;
            GPUICrowdAnimatorClipData clipData = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdAnimatorClipData>(p_animatorClipDataArray, animationClipIndex, GPUICrowdAnimatorClipData.STRIDE);
            if (!clipData.IsSynched)
                return;

            float normalizedTime = clipData.GetNormalizedTime(currentTime);
            GPUICrowdBakedClipData bakedClipData = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdBakedClipData>(p_bakedClipDataArray, clipData.BakedClipIndex, GPUICrowdBakedClipData.STRIDE);
            float weightedSpeededClipLength = bakedClipData.clipLength * newWeights.x / clipData.TargetSpeed;
            int weightedCount = 1;
            for (int i = 1; i < GPUICrowdConstants.ANIMATOR_MAX_CLIPS; i++)
            {
                float weight = newWeights[i];
                if (weight <= 0f)
                    break;
                clipData = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdAnimatorClipData>(p_animatorClipDataArray, animationClipIndex + i, GPUICrowdAnimatorClipData.STRIDE);
                bakedClipData = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdBakedClipData>(p_bakedClipDataArray, clipData.BakedClipIndex, GPUICrowdBakedClipData.STRIDE);
                weightedSpeededClipLength += bakedClipData.clipLength * weight / clipData.TargetSpeed;
                weightedCount++;
            }

            for (int i = 0; i < weightedCount; i++)
            {
                clipData = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdAnimatorClipData>(p_animatorClipDataArray, animationClipIndex + i, GPUICrowdAnimatorClipData.STRIDE);
                clipData.packedSpeedRelativeLengthAndSync = weightedSpeededClipLength;
                clipData.clipStartTime = currentTime - normalizedTime * weightedSpeededClipLength;
                UnsafeUtility.WriteArrayElementWithStride(p_animatorClipDataArray, animationClipIndex + i, GPUICrowdAnimatorClipData.STRIDE, clipData);
            }
        }
    }
}