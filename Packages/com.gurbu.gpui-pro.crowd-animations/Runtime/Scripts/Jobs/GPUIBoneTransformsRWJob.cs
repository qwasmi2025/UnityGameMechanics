// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace GPUInstancerPro.CrowdAnimations
{
    [Unity.Burst.BurstCompile]
    public unsafe struct GPUIBoneTransformsReadJob : IJobParallelForTransform
    {
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_boneRWTransformData; // GPUITransformData
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_matrixArray; // Matrix4x4
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_boneReadWriteStatusData; // int2
        [ReadOnly] public int transformCount;
        [ReadOnly] public int boneCount;
        [ReadOnly] public int instanceCount;
        [ReadOnly] public int2 invalidStatus;

        private GPUITransformData _boneTransformData;

        public void Execute(int index, TransformAccess transform)
        {
            if (index >= transformCount)
                return;
            int2 boneRWStatus = UnsafeUtility.ReadArrayElementWithStride<int2>(p_boneReadWriteStatusData, index, 8);
            int instanceIndex = boneRWStatus.x / boneCount;
            if (boneRWStatus.y != 1 || boneRWStatus.x < 0 || instanceIndex >= instanceCount)
                return;
            if (!transform.isValid)
            {
                UnsafeUtility.WriteArrayElementWithStride(p_boneReadWriteStatusData, index, 8, invalidStatus);
                return;
            }

            _boneTransformData.SetTransformRelativeToParent(UnsafeUtility.ReadArrayElementWithStride<Matrix4x4>(p_matrixArray, instanceIndex, 64), transform.localToWorldMatrix);
            UnsafeUtility.WriteArrayElementWithStride(p_boneRWTransformData, index, GPUITransformData.STRIDE, _boneTransformData);
        }
    }

    [Unity.Burst.BurstCompile]
    public unsafe struct GPUIBoneTransformsWriteJob : IJobParallelForTransform
    {
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_boneData; // GPUITransformData
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_matrixArray; // Matrix4x4
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_boneReadWriteStatusData; // int2
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_animatorClipDataArray; // GPUICrowdAnimatorClipData
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_clipFramesAndWeightsArray; // Vector4
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_bakedClipDataArray; // GPUICrowdAnimatorClipData
        [NativeDisableUnsafePtrRestriction] internal unsafe void* p_bakedBoneDataArray; // GPUITransformData
        [ReadOnly] public int transformCount;
        [ReadOnly] public int boneCount;
        [ReadOnly] public int bufferStartIndex;
        [ReadOnly] public int instanceCount;
        [ReadOnly] public int2 invalidStatus;
        [ReadOnly] public int bakedClipDataCount;
        [ReadOnly] public float currentTime;
        [ReadOnly] public float deltaTime;

        private GPUITransformData _boneTransformData;

        public void Execute(int index, TransformAccess transform)
        {
            if (index >= transformCount)
                return;
            int2 boneRWStatus = UnsafeUtility.ReadArrayElementWithStride<int2>(p_boneReadWriteStatusData, index, 8);
            int boneIndex = boneRWStatus.x;
            int instanceIndex = boneIndex / boneCount;
            if (boneRWStatus.y != 2 || boneIndex < 0 || instanceIndex >= instanceCount)
                return;
            if (!transform.isValid)
            {
                UnsafeUtility.WriteArrayElementWithStride(p_boneReadWriteStatusData, index, 8, invalidStatus);
                return;
            }
            boneIndex %= boneCount;

            int renderGroupIndex = instanceIndex + bufferStartIndex;
            int animatorClipDataIndex = renderGroupIndex * GPUICrowdConstants.ANIMATOR_MAX_CLIPS;
            int clipFramesAndWeightsIndex = renderGroupIndex * 2;
            Vector4 weights = UnsafeUtility.ReadArrayElementWithStride<Vector4>(p_clipFramesAndWeightsArray, clipFramesAndWeightsIndex + 1, 16);
            Matrix4x4 transformMatrix = UnsafeUtility.ReadArrayElementWithStride<Matrix4x4>(p_matrixArray, renderGroupIndex, 64);
            if (transformMatrix.m33 != 1)
                return;
            float3 position = float3.zero;
            quaternion rotation = quaternion.identity;
            float totalWeight = 0f;

            for (int i = 0; i < GPUICrowdConstants.ANIMATOR_MAX_CLIPS; i++)
            {
                float weight = weights[i];
                if (weight <= 0)
                    continue;
                GPUICrowdAnimatorClipData animatorClipData = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdAnimatorClipData>(p_animatorClipDataArray, animatorClipDataIndex + i, GPUICrowdAnimatorClipData.STRIDE);
                if (!animatorClipData.IsValid)
                    continue;
                float normalizedClipTime = animatorClipData.GetNormalizedTime(currentTime);
                if (!animatorClipData.IsLooping && normalizedClipTime >= 1f) // not looping and animation finished
                    continue;

                int bakedClipIndex = animatorClipData.BakedClipIndex;
                if (bakedClipIndex < 0 || bakedClipIndex >= bakedClipDataCount)
                    continue;
                GPUICrowdBakedClipData bakedClipData = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdBakedClipData>(p_bakedClipDataArray, bakedClipIndex, GPUICrowdBakedClipData.STRIDE);
                if (bakedClipData.clipLength <= 0)
                    continue;

                totalWeight += weight;

                int frameCount = animatorClipData.FrameCount - 1;
                float frameNo = math.frac(normalizedClipTime) * frameCount;
                float lerp = math.frac(frameNo);
                int currentFrame = Mathf.FloorToInt(frameNo);
                int nextFrame = (currentFrame + 1) % frameCount;

                GPUITransformData currentFrameBoneTransform = UnsafeUtility.ReadArrayElementWithStride<GPUITransformData>(p_bakedBoneDataArray, bakedClipData.bakedBoneDataIndex + currentFrame * boneCount + boneIndex, GPUITransformData.STRIDE);
                GPUITransformData nextFrameBoneTransform = UnsafeUtility.ReadArrayElementWithStride<GPUITransformData>(p_bakedBoneDataArray, bakedClipData.bakedBoneDataIndex + nextFrame * boneCount + boneIndex, GPUITransformData.STRIDE);
                position += math.lerp(currentFrameBoneTransform.position, nextFrameBoneTransform.position, lerp) * weight;
                quaternion blendedRot = math.slerp(currentFrameBoneTransform.rotation, nextFrameBoneTransform.rotation, lerp);
                rotation = math.slerp(rotation, blendedRot, weight / totalWeight);
            }
            if (totalWeight == 0f)
                return;

            quaternion instanceRotation = transformMatrix.rotation;
            position = math.mul(instanceRotation, position);
            position.x += transformMatrix.m03;
            position.y += transformMatrix.m13;
            position.z += transformMatrix.m23;
            rotation = math.mul(instanceRotation, rotation);

            transform.SetPositionAndRotation(position, rotation);
        }
    }
}