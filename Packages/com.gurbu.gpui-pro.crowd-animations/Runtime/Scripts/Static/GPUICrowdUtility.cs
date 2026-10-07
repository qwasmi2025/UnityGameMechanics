// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using UnityEngine.Profiling;
using UnityEngine.Jobs;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GPUInstancerPro.CrowdAnimations
{
    public static class GPUICrowdUtility
    {
        #region Crowd Rig Methods
        private static readonly Type[] RIG_SAMPLE_COMPONENTS = new Type[] { typeof(Animator), typeof(GPUICrowdInstance), typeof(SkinnedMeshRenderer) };
        public static GPUICrowdRig CreateRig(GameObject prefab, bool forceNew = false, bool saveAsAsset = false)
        {
            if (prefab == null)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not create rig data. Prefab is null!");
                return null;
            }
            GPUICrowdInstance crowdInstance = prefab.GetComponent<GPUICrowdInstance>();
            GPUICrowdRig crowdRig = crowdInstance == null ? null : crowdInstance._crowdRig;
            if (crowdRig == null)
            {
                crowdRig = ScriptableObject.CreateInstance<GPUICrowdRig>();
                crowdRig.name = prefab.name + "_GPUIRig";
            }
            if (forceNew)
            {
                crowdRig.bones = new();
                crowdRig.skinnedMeshes = new();
                crowdRig.bindPoseDataList = new();
            }
            else
            {
                crowdRig.bones ??= new();
                crowdRig.skinnedMeshes ??= new();
                crowdRig.bindPoseDataList ??= new();
            }

            GameObject prefabInstance = prefab;
            Animator animator = prefabInstance.GetComponent<Animator>();
            if (animator == null)
            {
                prefabInstance = GPUIUtility.InstantiateWithStrippedComponents(prefabInstance, Vector3.zero, GPUIConstants.IDENTITY_Quaternion, RIG_SAMPLE_COMPONENTS);
                prefabInstance.hideFlags = HideFlags.DontSave;
                animator = prefabInstance.AddOrGetComponent<Animator>();
            }
            bool hasTransformHierarchy = animator.hasTransformHierarchy;
            if (!hasTransformHierarchy)
            {
                if (prefab == prefabInstance)
                {
                    prefabInstance = GPUIUtility.InstantiateWithStrippedComponents(prefabInstance, Vector3.zero, GPUIConstants.IDENTITY_Quaternion, RIG_SAMPLE_COMPONENTS);
                    prefabInstance.hideFlags = HideFlags.DontSave;
                }
                AnimatorUtility.DeoptimizeTransformHierarchy(prefabInstance);
            }
            else if (prefab != prefabInstance)
            {
                GPUIUtility.DestroyGeneric(prefabInstance);
                prefabInstance = prefab;
            }

            Transform prefabInstanceTransform = prefabInstance.transform;
            SkinnedMeshRenderer[] skinnedMeshes = prefabInstance.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var skinnedMeshRenderer in skinnedMeshes)
                crowdRig.AddSkinnedMesh(prefabInstanceTransform, skinnedMeshRenderer);

#if UNITY_EDITOR
            if (AssetDatabase.Contains(crowdRig))
                EditorUtility.SetDirty(crowdRig);
            else if (!Application.isPlaying && saveAsAsset)
            {
                string folderPath = GPUICrowdConstants.GetCrowdRigPath();
                string fileName = crowdRig.name + ".asset";
                GPUICrowdRig existingAsset = AssetDatabase.LoadAssetAtPath<GPUICrowdRig>(folderPath + fileName);
                if (existingAsset != null && existingAsset.IsMatchingBoneData(crowdRig))
                {
                    GPUIUtility.DestroyGeneric(crowdRig);
                    crowdRig = existingAsset;
                    foreach (var skinnedMeshRenderer in skinnedMeshes)
                        existingAsset.AddSkinnedMesh(prefabInstanceTransform, skinnedMeshRenderer);
                    EditorUtility.SetDirty(crowdRig);
                }
                else if (GPUIUtility.SaveAsAsset(crowdRig, folderPath, fileName, true, true))
                    Debug.Log(GPUIConstants.LOG_PREFIX + "Saved GPUI Crowd Rig data for " + prefab.name, crowdRig);
            }
            if (AssetDatabase.Contains(prefab) && !Application.isPlaying)
            {
                if (crowdInstance == null)
                    crowdInstance = GPUIPrefabUtility.AddOrGetComponentToPrefab<GPUICrowdInstance>(prefab);
                crowdInstance._crowdRig = crowdRig;
                if (hasTransformHierarchy)
                    crowdInstance.LoadBoneTransforms(false, false);
                EditorUtility.SetDirty(crowdInstance.gameObject);
                GPUIPrefabUtility.MergeAllPrefabInstances(prefab);
            }
            else
#endif
            if (crowdInstance == null && Application.isPlaying)
            {
                crowdInstance = prefab.AddComponent<GPUICrowdInstance>();
                crowdInstance._crowdRig = crowdRig;
                if (hasTransformHierarchy)
                    crowdInstance.LoadBoneTransforms(false, false);
            }
            else if (crowdInstance != null)
            {
                crowdInstance._crowdRig = crowdRig;
#if UNITY_EDITOR
                EditorUtility.SetDirty(crowdInstance);
#endif
            }

#if GPUIPRO_DEVMODE
            Debug.Log(GPUIConstants.LOG_PREFIX + GPUIConstants.LOG_PREFIX_DEV + "Created GPUI Crowd Rig data for " + prefab.name, prefab);
#endif

            if (!hasTransformHierarchy)
                GPUIUtility.DestroyGeneric(prefabInstance);

            return crowdRig;
        }

        public static string GenerateBoneFullPath(Transform parent, Transform boneTransform)
        {
            string path = boneTransform.name;
            Transform current = boneTransform.parent;
            while (current != null && current != parent)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }
        #endregion Crowd Rig Methods

        #region Animation Clip Methods
        #region Bake Methods
        public static void BakeAnimationClips(GPUICrowdInstance crowdInstance, int frameRate, params AnimationClip[] animationClips)
        {
            if (!CheckBakeParams(crowdInstance, frameRate, animationClips))
                return;
            var clipSampler = GPUICrowdSkinningSystem.Instance.ClipSamplerProvider.CreateClipSampler(crowdInstance);
            foreach (var animationClip in animationClips)
            {
                var bakedClipData = crowdInstance._crowdRig.GenerateBakedClipData_Internal(animationClip, frameRate);
                BakeAnimationClip_Internal(clipSampler, animationClip, bakedClipData);
            }
        }

        public static void BakeAnimationClips(GPUICrowdInstance crowdInstance, int frameRate, IEnumerable<AnimationClip> animationClips)
        {
            if (!CheckBakeParams(crowdInstance, frameRate, animationClips))
                return;
            var clipSampler = GPUICrowdSkinningSystem.Instance.ClipSamplerProvider.CreateClipSampler(crowdInstance);
            foreach (var animationClip in animationClips)
            {
                var bakedClipData = crowdInstance._crowdRig.GenerateBakedClipData_Internal(animationClip, frameRate);
                BakeAnimationClip_Internal(clipSampler, animationClip, bakedClipData);
            }
        }

        private static bool CheckBakeParams(GPUICrowdInstance crowdInstance, int frameRate, IEnumerable<AnimationClip> animationClips)
        {
            if (animationClips == null)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not bake animation clip. Given animation clip is null!");
                return false;
            }
            if (crowdInstance == null)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not bake animation clip. GPUICrowdInstance is null!");
                return false;
            }
            if (crowdInstance._crowdRig == null)
                crowdInstance.LoadRig(true);
            if (frameRate <= 0)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not bake animation clip. Frame rate must be a positive number!");
                return false;
            }
            return true;
        }

        internal static unsafe void BakeAnimationClip_Internal(GPUICrowdClipSamplerProvider.GPUICrowdClipSampler crowdClipSampler, AnimationClip animationClip, GPUICrowdBakedClipData bakedClipData)
        {
            if (animationClip.legacy)
            {
                BakeLegacyAnimationClip_Internal(crowdClipSampler, animationClip, bakedClipData);
                return;
            }
#if GPUIPRO_DEVMODE
            float startTime = Time.realtimeSinceStartup;
#endif
            Profiler.BeginSample("GPUICrowdUtility.BakeAnimationClip");
            var animationEvents = animationClip.events;
            bool hasEvents = false;
            if (animationEvents.Length > 0)
            {
                hasEvents = true;
                animationClip.events = new AnimationEvent[0]; // Remove events temporarily during baking.
            }

            try
            {
                Transform sampleTransform = crowdClipSampler.sampleGO.transform;
                var clipPlayable = AnimationClipPlayable.Create(crowdClipSampler.playableGraph, animationClip);
                crowdClipSampler.playableGraph.GetOutput(0).SetSourcePlayable(clipPlayable);

                int boneCount = crowdClipSampler.sampleCrowdInstance._crowdRig.GetBoneCount();
                float divider = (bakedClipData.clipFrameCount - 1f);
                var bakedBoneData = crowdClipSampler.sampleCrowdInstance._crowdRig.GetBakedBoneData();
                var p_bakedBoneData = bakedBoneData.GetUnsafePtr();
                GPUITransformData transformData = GPUIConstants.TRANSFORM_DATA_IDENTITY;
                GPUICrowdRootMotion[] rootMotionData = new GPUICrowdRootMotion[bakedClipData.clipFrameCount];
                GPUICrowdRootMotion rootMotion = GPUICrowdConstants.DEFAULT_ROOT_MOTION;
                bool hasRootMotion = false;
                int boneTransformsCount = crowdClipSampler.boneTransforms.Count;
                float timeStep = bakedClipData.clipLength / divider;
                float clipTime = 0f;
                int boneDataStartIndex = bakedClipData.bakedBoneDataIndex;
                for (int f = 0; f < bakedClipData.clipFrameCount; f++)
                {
                    clipPlayable.SetTime(clipTime);
                    clipTime += timeStep;
                    crowdClipSampler.playableGraph.Evaluate();

                    rootMotion.position = sampleTransform.position;
                    rootMotion.rotation = sampleTransform.rotation;
                    rootMotion.SetMotionType();
                    rootMotionData[f] = rootMotion;
                    if (rootMotion.motionType > 0)
                    {
                        hasRootMotion = true;
                        sampleTransform.SetPositionAndRotation(GPUIConstants.Vector3_ZERO, GPUIConstants.IDENTITY_Quaternion);
                    }

                    for (int b = 0; b < boneTransformsCount; b++)
                    {
                        var bt = crowdClipSampler.boneTransforms[b];
                        transformData.position = bt.boneTransform.position;
                        transformData.rotation = bt.boneTransform.rotation;
                        transformData.scale = bt.boneTransform.lossyScale;
                        UnsafeUtility.WriteArrayElementWithStride(p_bakedBoneData, boneDataStartIndex + bt.boneIndex, GPUITransformData.STRIDE, transformData);
                    }
                    boneDataStartIndex += boneCount;
                }
                if (hasRootMotion)
                    crowdClipSampler.sampleCrowdInstance._crowdRig.SetRootMotionData(animationClip, bakedClipData, rootMotionData);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            if (hasEvents)
                animationClip.events = animationEvents;

            Profiler.EndSample();
#if GPUIPRO_DEVMODE
            float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            Debug.Log(GPUIConstants.LOG_PREFIX + GPUIConstants.LOG_PREFIX_DEV + animationClip.name + " animation clip baked! Time: " + elapsedMs.ToString("0.00") + "ms", crowdClipSampler.sampleCrowdInstance._crowdRig);
#endif
        }

        private static unsafe void BakeLegacyAnimationClip_Internal(GPUICrowdClipSamplerProvider.GPUICrowdClipSampler crowdClipSampler, AnimationClip animationClip, GPUICrowdBakedClipData bakedClipData)
        {
            Profiler.BeginSample("GPUICrowdUtility.BakeLegacyAnimationClip");

            Transform sampleTransform = crowdClipSampler.sampleGO.transform;
            int boneCount = crowdClipSampler.sampleCrowdInstance._crowdRig.GetBoneCount();
            float divider = (bakedClipData.clipFrameCount - 1f);
            var bakedBoneData = crowdClipSampler.sampleCrowdInstance._crowdRig.GetBakedBoneData();
            var p_bakedBoneData = bakedBoneData.GetUnsafePtr();
            GPUITransformData transformData = GPUIConstants.TRANSFORM_DATA_IDENTITY;
            GPUICrowdRootMotion[] rootMotionData = new GPUICrowdRootMotion[bakedClipData.clipFrameCount];
            GPUICrowdRootMotion rootMotion = GPUICrowdConstants.DEFAULT_ROOT_MOTION;
            bool hasRootMotion = false;
            int boneTransformsCount = crowdClipSampler.boneTransforms.Count;
            float timeStep = bakedClipData.clipLength / divider;
            float clipTime = 0f;
            int boneDataStartIndex = bakedClipData.bakedBoneDataIndex;

            // Reset transform state
            sampleTransform.SetPositionAndRotation(Vector3.zero, GPUIConstants.IDENTITY_Quaternion);
            sampleTransform.localScale = Vector3.one;
            GameObject sampleGO;
            if (crowdClipSampler.sampleCrowdInstance._legacyAnimation != null)
                sampleGO = crowdClipSampler.sampleCrowdInstance._legacyAnimation.gameObject;
            else
            {
                crowdClipSampler.sampleCrowdInstance.AddOrGetComponent<Animation>();
                sampleGO = crowdClipSampler.sampleCrowdInstance.gameObject;
            }

            for (int f = 0; f < bakedClipData.clipFrameCount; f++)
            {
                // Sample legacy clip directly onto the transform hierarchy
                animationClip.SampleAnimation(sampleGO, clipTime);
                clipTime += timeStep;

                // Root motion capture
                rootMotion.position = sampleTransform.position;
                rootMotion.rotation = sampleTransform.rotation;
                rootMotion.SetMotionType();
                rootMotionData[f] = rootMotion;
                if (rootMotion.motionType > 0)
                {
                    hasRootMotion = true;
                    sampleTransform.SetPositionAndRotation(GPUIConstants.Vector3_ZERO, GPUIConstants.IDENTITY_Quaternion);
                }

                // Capture bone data
                for (int b = 0; b < boneTransformsCount; b++)
                {
                    var bt = crowdClipSampler.boneTransforms[b];
                    transformData.position = bt.boneTransform.position;
                    transformData.rotation = bt.boneTransform.rotation;
                    transformData.scale = bt.boneTransform.lossyScale;
                    UnsafeUtility.WriteArrayElementWithStride(p_bakedBoneData, boneDataStartIndex + bt.boneIndex, GPUITransformData.STRIDE, transformData);
                }
                boneDataStartIndex += boneCount;
            }

            if (hasRootMotion)
                crowdClipSampler.sampleCrowdInstance._crowdRig.SetRootMotionData(animationClip, bakedClipData, rootMotionData);

            Profiler.EndSample();

#if GPUIPRO_DEVMODE
	        Debug.Log(GPUIConstants.LOG_PREFIX + GPUIConstants.LOG_PREFIX_DEV + string.Format("{0} legacy animation clip baked!", animationClip.name), crowdClipSampler.sampleCrowdInstance._crowdRig);
#endif
        }
        #endregion Bake Methods

        #region Compute Animator Methods
        /// <summary>
        /// Starts playing the specified animation clip on the given crowd instance.
        /// </summary>
        /// <param name="crowdInstance">The instance on which to play the animation.</param>
        /// <param name="animationClip">The animation clip to be played.</param>
        /// <param name="normalizedClipTime">(Optional) Normalized start time of the clip, between 0f and 1f. Use a negative value to continue from where it left off (e.g., if it was already playing with a blend).</param>
        /// <param name="speed">(Optional) The animation speed.</param>
        /// <param name="transitionTime">(Optional) Transition time from the previous clip.</param>
        /// <param name="isLoopingOverride">(Optional) If null, the clip's looping setting is used. If set to true or false, it forces the animation to loop or not loop.</param>
        /// <param name="isSyncTime">(Optional) If true, synchronizes the normalized time between animation clips during transitions.</param>
        /// <returns>True if the animation starts successfully.</returns>
        public static bool StartAnimation(GPUICrowdInstance crowdInstance, AnimationClip animationClip, float normalizedClipTime = -1.0f, float speed = 1.0f, float transitionTime = 0, bool? isLoopingOverride = null, bool isSyncTime = false)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return false;
#endif
            if (!crowdInstance._hasPrefabComponent)
            {
                Debug.LogError(GPUICrowdConstants.ERROR_NO_PREFAB_COMPONENT, crowdInstance);
                return false;
            }
            if (!crowdInstance.IsProcessed)
            {
                crowdInstance._OnProcessed += () => StartAnimation(crowdInstance, animationClip, normalizedClipTime, speed, transitionTime, isLoopingOverride, isSyncTime);
                return true;
            }
            return crowdInstance.AnimatorWorkflow.StartAnimation(crowdInstance.PrefabComponent.renderKey, crowdInstance.PrefabComponent.bufferIndex, animationClip, normalizedClipTime, speed, transitionTime, isLoopingOverride, isSyncTime);
        }

        /// <summary>
        /// Starts playing the specified animation clip on the given crowd instance.
        /// </summary>
        /// <param name="rendererKey">Integer key that uniquely identifies the renderer</param>
        /// <param name="bufferIndex">The instance index on the buffer.</param>
        /// <param name="animationClip">The animation clip to be played.</param>
        /// <param name="normalizedClipTime">(Optional) Normalized start time of the clip, between 0f and 1f. Use a negative value to continue from where it left off (e.g., if it was already playing with a blend).</param>
        /// <param name="speed">(Optional) The animation speed.</param>
        /// <param name="transitionTime">(Optional) Transition time from the previous clip.</param>
        /// <param name="isLoopingOverride">(Optional) If null, the clip's looping setting is used. If set to true or false, it forces the animation to loop or not loop.</param>
        /// <param name="isSyncTime">(Optional) If true, synchronizes the normalized time between animation clips during transitions.</param>
        /// <returns>True if the animation starts successfully.</returns>
        public static bool StartAnimation(GPUIAnimatorWorkflowBase animatorWorkflow, int rendererKey, int bufferIndex, AnimationClip animationClip, float normalizedClipTime = -1.0f, float speed = 1.0f, float transitionTime = 0, bool? isLoopingOverride = null, bool isSyncTime = false)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return false;
#endif
            Vector4 startTimes = GPUICrowdConstants.DEFAULT_CLIP_START_TIMES;
            startTimes.x = normalizedClipTime;
            Vector4 speeds = GPUICrowdConstants.DEFAULT_CLIP_SPEEDS;
            speeds.x = speed;
            return StartBlend(animatorWorkflow, rendererKey, bufferIndex, GPUICrowdConstants.DEFAULT_CLIP_WEIGHT, animationClip, null, null, null, startTimes, speeds, transitionTime, isLoopingOverride, isSyncTime);
        }

        /// <summary>
        /// Starts blending the specified animation clips with the given weights on the specified crowd instance.
        /// </summary>
        /// <param name="crowdInstance">The instance on which to play the animations.</param>
        /// <param name="animationWeights">Weights of the animation clips. Must sum to 1f.</param>
        /// <param name="animationClip1">Clip 1.</param>
        /// <param name="animationClip2">Clip 2.</param>
        /// <param name="animationClip3">(Optional) Clip 3.</param>
        /// <param name="animationClip4">(Optional) Clip 4.</param>
        /// <param name="normalizedClipTimes">(Optional) Normalized start times for the clips, between 0f and 1f. Use negative values to continue from where they left off (e.g., if previously playing).</param>
        /// <param name="animationSpeeds">(Optional) Speeds of the animation clips.</param>
        /// <param name="transitionTime">(Optional) Transition time from the previous clip.</param>
        /// <param name="isLoopingOverride">(Optional) If null, uses the clips' looping settings. If set to true or false, forces looping or non-looping.</param>
        /// <param name="isSyncTime">(Optional) If true, synchronizes normalized time across clips during transitions.</param>
        /// <returns>True if blending starts successfully.</returns>
        public static bool StartBlend(GPUICrowdInstance crowdInstance, Vector4 animationWeights, AnimationClip animationClip1, AnimationClip animationClip2, AnimationClip animationClip3 = null, AnimationClip animationClip4 = null, Vector4? normalizedClipTimes = null, Vector4? animationSpeeds = null, float transitionTime = 0, bool? isLoopingOverride = null, bool isSyncTime = true)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return false;
#endif
            if (!crowdInstance._hasPrefabComponent)
            {
                Debug.LogError(GPUICrowdConstants.ERROR_NO_PREFAB_COMPONENT, crowdInstance);
                return false;
            }
            if (!crowdInstance.IsProcessed)
            {
                crowdInstance._OnProcessed += () => StartBlend(crowdInstance, animationWeights, animationClip1, animationClip2, animationClip3, animationClip4, normalizedClipTimes, animationSpeeds, transitionTime, isLoopingOverride, isSyncTime);
                return true;
            }
            return crowdInstance.AnimatorWorkflow.StartBlend(crowdInstance.PrefabComponent.renderKey, crowdInstance.PrefabComponent.bufferIndex, animationWeights, animationClip1, animationClip2, animationClip3, animationClip4, normalizedClipTimes, animationSpeeds, transitionTime, isLoopingOverride, isSyncTime);
        }

        /// <summary>
        /// Starts blending the specified animation clips with the given weights on the specified crowd instance.
        /// </summary>
        /// <param name="rendererKey">Integer key that uniquely identifies the renderer</param>
        /// <param name="bufferIndex">The instance index on the buffer.</param>
        /// <param name="animationWeights">Weights of the animation clips. Must sum to 1f.</param>
        /// <param name="animationClip1">Clip 1.</param>
        /// <param name="animationClip2">Clip 2.</param>
        /// <param name="animationClip3">(Optional) Clip 3.</param>
        /// <param name="animationClip4">(Optional) Clip 4.</param>
        /// <param name="normalizedClipTimes">(Optional) Normalized start times for the clips, between 0f and 1f. Use negative values to continue from where they left off (e.g., if previously playing).</param>
        /// <param name="animationSpeeds">(Optional) Speeds of the animation clips.</param>
        /// <param name="transitionTime">(Optional) Transition time from the previous clip.</param>
        /// <param name="isLoopingOverride">(Optional) If null, uses the clips' looping settings. If set to true or false, forces looping or non-looping.</param>
        /// <param name="isSyncTime">(Optional) If true, synchronizes normalized time across clips during transitions.</param>
        /// <returns>True if blending starts successfully.</returns>
        public static bool StartBlend(GPUIAnimatorWorkflowBase animatorWorkflow, int rendererKey, int bufferIndex, Vector4 animationWeights, AnimationClip animationClip1, AnimationClip animationClip2, AnimationClip animationClip3 = null, AnimationClip animationClip4 = null, Vector4? normalizedClipTimes = null, Vector4? animationSpeeds = null, float transitionTime = 0, bool? isLoopingOverride = null, bool isSyncTime = true)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return false;
#endif
            if (!GPUIRenderingSystem.Instance.RenderSourceProvider.TryGetData(rendererKey, out var renderSource))
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + string.Format(GPUICrowdConstants.ERROR_NO_RENDER_KEY, rendererKey));
                return false;
            }
            if (!GPUICrowdSkinningSystem.Instance.RenderSourceGroupProvider.TryGetData(renderSource.renderSourceGroup.Key, out var crowdRSG))
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + string.Format(GPUICrowdConstants.ERROR_NO_CROWD_DATA, rendererKey));
                return false;
            }
            if (bufferIndex >= renderSource.bufferSize)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + string.Format(GPUICrowdConstants.ERROR_BUFFER_INDEX_OUT_OF_BOUNDS, bufferIndex, renderSource.bufferSize));
                return false;
            }

            if (animationClip1 == null)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + GPUICrowdConstants.ERROR_NULL_ANIMATION_CLIP);
                return false;
            }
            var clips = GPUICrowdConstants.CLIPS_ARRAY;
            clips[0] = animationClip1;
            clips[1] = animationClip2;
            clips[2] = animationClip3;
            clips[3] = animationClip4;
            return StartBlend(animatorWorkflow, crowdRSG, bufferIndex + renderSource.bufferStartIndex, animationWeights, clips, normalizedClipTimes, animationSpeeds, transitionTime, isLoopingOverride, isSyncTime);
        }

        internal static bool StartBlend(GPUIAnimatorWorkflowBase animatorWorkflow, GPUICrowdRenderSourceGroup crowdRSG, int rsgBufferIndex, Vector4 animationWeights, AnimationClip[] animationClips, Vector4? normalizedClipTimes = null, Vector4? animationSpeeds = null, float transitionTime = 0, bool? isLoopingOverride = null, bool isSyncTime = true)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return false;
#endif
            if (!animatorWorkflow.HasInternalAnimator())
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + string.Format(GPUICrowdConstants.ERROR_NO_ANIMATOR, animatorWorkflow.GetName()));
                return false;
            }
            var bakedClipIndexes = GPUICrowdConstants.BAKED_CLIP_INDEXES;
            for (var i = 0; i < GPUICrowdConstants.ANIMATOR_MAX_CLIPS; i++)
            {
                AnimationClip animationClip = animationClips[i];
                if (animationClip == null)
                    bakedClipIndexes[i] = -1;
                else
                    bakedClipIndexes[i] = crowdRSG.rig.GetOrCreateBakedClipIndex(crowdRSG.renderSourceGroup, animationClip);
            }
            return StartAnimationBlend(crowdRSG, rsgBufferIndex, animationWeights, bakedClipIndexes, normalizedClipTimes, animationSpeeds, transitionTime, isLoopingOverride, isSyncTime);
        }

        public static void SetAnimationSpeeds(GPUICrowdInstance crowdInstance, Vector4 animationSpeeds)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            if (!crowdInstance._hasPrefabComponent)
            {
                Debug.LogError(GPUICrowdConstants.ERROR_NO_PREFAB_COMPONENT, crowdInstance);
                return;
            }
            if (!crowdInstance.IsProcessed)
            {
                crowdInstance._OnProcessed += () => SetAnimationSpeeds(crowdInstance, animationSpeeds);
                return;
            }
            crowdInstance.AnimatorWorkflow.SetAnimationSpeeds(crowdInstance.PrefabComponent.renderKey, crowdInstance.PrefabComponent.bufferIndex, animationSpeeds);
        }

        public static void SetAnimationSpeeds(GPUIAnimatorWorkflowBase animatorWorkflow, int rendererKey, int bufferIndex, Vector4 animationSpeeds)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            if (!GPUIRenderingSystem.Instance.RenderSourceProvider.TryGetData(rendererKey, out var renderSource))
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + string.Format(GPUICrowdConstants.ERROR_NO_RENDER_KEY, rendererKey));
                return;
            }
            if (!GPUICrowdSkinningSystem.Instance.RenderSourceGroupProvider.TryGetData(renderSource.renderSourceGroup.Key, out var crowdRSG))
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + string.Format(GPUICrowdConstants.ERROR_NO_CROWD_DATA, rendererKey));
                return;
            }
            if (bufferIndex >= renderSource.bufferSize)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + string.Format(GPUICrowdConstants.ERROR_BUFFER_INDEX_OUT_OF_BOUNDS, bufferIndex, renderSource.bufferSize));
                return;
            }

            SetAnimationSpeeds(animatorWorkflow, crowdRSG, bufferIndex + renderSource.bufferStartIndex, animationSpeeds);
        }

        internal static void SetAnimationSpeeds(GPUIAnimatorWorkflowBase animatorWorkflow, GPUICrowdRenderSourceGroup crowdRSG, int rsgBufferIndex, Vector4 animationSpeeds)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            if (!animatorWorkflow.HasInternalAnimator())
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + string.Format(GPUICrowdConstants.ERROR_NO_ANIMATOR, animatorWorkflow.GetName()));
                return;
            }
            SetAnimationSpeeds(crowdRSG, rsgBufferIndex, animationSpeeds);
        }
        #endregion Compute Animator Methods
        #endregion Animation Clip Methods

        #region Buffer Methods

        public static List<Vector4> GenerateVertexBoneData(Mesh originalMesh, GPUICrowdSkinnedMeshData smd)
        {
            List<Vector4> boneIndexAndWeights = new List<Vector4>();
            Vector4 boneIndexAndWeightVector = Vector4.zero;
            float weightDivider = 2f;
            foreach (BoneWeight boneWeight in originalMesh.boneWeights)
            {
                if (smd.boneIndexes.Length == 0)
                {
                    boneIndexAndWeightVector.x = boneWeight.boneIndex0;
                    boneIndexAndWeightVector.y = boneWeight.boneIndex1;
                    boneIndexAndWeightVector.z = boneWeight.boneIndex2;
                    boneIndexAndWeightVector.w = boneWeight.boneIndex3;
                }
                else
                {
                    boneIndexAndWeightVector.x = smd.boneIndexes[boneWeight.boneIndex0];
                    boneIndexAndWeightVector.y = smd.boneIndexes[boneWeight.boneIndex1];
                    boneIndexAndWeightVector.z = smd.boneIndexes[boneWeight.boneIndex2];
                    boneIndexAndWeightVector.w = smd.boneIndexes[boneWeight.boneIndex3];
                }

                boneIndexAndWeightVector.x += boneWeight.weight0 / weightDivider;
                boneIndexAndWeightVector.y += boneWeight.weight1 / weightDivider;
                boneIndexAndWeightVector.z += boneWeight.weight2 / weightDivider;
                boneIndexAndWeightVector.w += boneWeight.weight3 / weightDivider;

                boneIndexAndWeights.Add(boneIndexAndWeightVector);
            }

            return boneIndexAndWeights;
        }

        public static void CopyBoneDataBufferToMatrixBuffer(GPUICrowdRenderSourceGroup crowdRSG)
        {
            int bindPoseCount = crowdRSG.rig.bindPoseDataList.Count;
            if (bindPoseCount == 0)
                return;
            int count = crowdRSG.boneDataBuffer.count;

            ComputeShader cs = GPUICrowdConstants.CS_BoneBufferUtility;
            int kernelIndex = 0;
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_boneDataBuffer, crowdRSG.boneDataBuffer);
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_shaderBoneBuffer, crowdRSG.shaderBoneBuffer.Buffer);
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_bindPoseBuffer, crowdRSG.bindPoseBuffer);
            cs.SetInt(GPUIConstants.PROP_startIndex, crowdRSG._shaderBoneBufferAnimStartIndex);
            cs.SetInt(GPUIConstants.PROP_count, count);
            cs.SetInt(GPUICrowdConstants.PROP_boneCount, crowdRSG.rig.bones.Count);
            for (int i = 0; i < bindPoseCount; i++)
            {
                cs.SetInt(GPUICrowdConstants.PROP_bindPoseNo, i);
                cs.DispatchX(kernelIndex, count);
            }
            crowdRSG.shaderBoneBuffer.OnDataModified();
        }

        public static void SetBindPoseBufferData(GPUICrowdRenderSourceGroup crowdRSG)
        {
            int boneCount = crowdRSG.rig.bones.Count;
            if (boneCount == 0)
                return;
            int bindPoseCount = crowdRSG.rig.bindPoseDataList.Count;
            if (bindPoseCount == 0)
                return;

            if (crowdRSG.bindPoseBuffer != null)
                crowdRSG.bindPoseBuffer.Dispose();
            crowdRSG.bindPoseBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, boneCount * bindPoseCount, 4 * 4 * 4);
            for (int i = 0; i < bindPoseCount; i++)
            {
                if (crowdRSG.rig.bindPoseDataList[i].bindPoses.Length != boneCount)
                    Array.Resize(ref crowdRSG.rig.bindPoseDataList[i].bindPoses, boneCount);
                crowdRSG.bindPoseBuffer.SetData(crowdRSG.rig.bindPoseDataList[i].bindPoses, 0, i * boneCount, boneCount);
            }

            if (crowdRSG.inverseBindPoseBuffer != null)
                crowdRSG.inverseBindPoseBuffer.Dispose();
            crowdRSG.inverseBindPoseBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, boneCount * bindPoseCount, GPUITransformData.STRIDE);
            for (int i = 0; i < bindPoseCount; i++)
            {
                crowdRSG.inverseBindPoseBuffer.SetData(crowdRSG.rig.bindPoseDataList[i].GetInverseTransformDataArray(), 0, i * boneCount, boneCount);
            }
        }

        public static void SetDefaultBoneDataFromBindPose(GPUICrowdRenderSourceGroup crowdRSG, int startInstanceIndex)
        {
            if (crowdRSG.rig.bindPoseDataList == null || crowdRSG.rig.bindPoseDataList.Count == 0)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not find bind pose data!");
                return;
            }
            int boneCount = crowdRSG.rig.bones.Count;
            if (crowdRSG.rig.bindPoseDataList[0].bindPoses.Length != boneCount)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Bind pose count does not match bone count! Bind pose count: " + crowdRSG.rig.bindPoseDataList[0].bindPoses.Length + " Bone Count: " + boneCount);
                return;
            }
            int startIndex = startInstanceIndex * boneCount;
            int count = crowdRSG.boneDataBuffer.count - startIndex;

            ComputeShader cs = GPUICrowdConstants.CS_BoneBufferUtility;
            int kernelIndex = 2;
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_boneDataBuffer, crowdRSG.boneDataBuffer);
            cs.SetBuffer(kernelIndex, GPUIConstants.PROP_sourceBuffer, crowdRSG.inverseBindPoseBuffer);
            cs.SetInt(GPUIConstants.PROP_startIndex, startIndex);
            cs.SetInt(GPUIConstants.PROP_count, count);
            cs.SetInt(GPUICrowdConstants.PROP_boneCount, crowdRSG.rig.bones.Count);
            cs.DispatchX(kernelIndex, count);
        }

        public static void SetDefaultBoneDataFromBindPose(GPUICrowdRenderSourceGroup crowdRSG, List<int> indexFilter)
        {
            if (indexFilter == null)
                return;

            int bufferSize = crowdRSG.renderSourceGroup.BufferSize;
            int filterCount = indexFilter.Count;
            for (int i = 0; i < filterCount; i++)
            {
                int index = indexFilter[i];
                if (index < 0 || index >= bufferSize)
                {
                    indexFilter.RemoveAt(i);
                    filterCount--;
                    i--;
                }
            }

            if (filterCount == 0)
                return;

            if (crowdRSG.rig.bindPoseDataList == null || crowdRSG.rig.bindPoseDataList.Count == 0)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not find bind pose data!");
                return;
            }

            int boneCount = crowdRSG.rig.bones.Count;
            if (crowdRSG.rig.bindPoseDataList[0].bindPoses.Length != boneCount)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Bind pose count does not match bone count! Bind pose count: " + crowdRSG.rig.bindPoseDataList[0].bindPoses.Length + " Bone Count: " + boneCount);
                return;
            }

            GraphicsBuffer filterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, filterCount, 16);
            filterBuffer.SetData(indexFilter);
            int count = filterCount * boneCount;

            ComputeShader cs = GPUICrowdConstants.CS_BoneBufferUtility;
            int kernelIndex = 3;
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_boneDataBuffer, crowdRSG.boneDataBuffer);
            cs.SetBuffer(kernelIndex, GPUIConstants.PROP_sourceBuffer, crowdRSG.inverseBindPoseBuffer);
            cs.SetBuffer(kernelIndex, GPUIConstants.PROP_filterBuffer, filterBuffer);
            cs.SetInt(GPUIConstants.PROP_count, count);
            cs.SetInt(GPUICrowdConstants.PROP_boneCount, crowdRSG.rig.bones.Count);
            cs.DispatchX(kernelIndex, count);
        }
        #endregion Buffer Methods

        #region Render Source Methods

        public static unsafe void WriteToBoneTransforms(GPUICrowdRenderSource crowdRenderSource, float deltaTime)
        {
            if (!crowdRenderSource.HasBoneWrite)
                return;
            int transformCount = crowdRenderSource.GetBoneRWTransformCount();
            int boneCount = crowdRenderSource.boneCount;
            int bakedClipCount = crowdRenderSource.crowdRSG.rig.GetBakedClipCount();
            if (transformCount == 0 || bakedClipCount == 0 || crowdRenderSource.renderSource.instanceCount == 0 || crowdRenderSource.getTransformMatrixDelegate == null || boneCount <= 0)
                return;
//#if GPUIPRO_DEVMODE
//            Debug.Log(GPUIConstants.LOG_PREFIX + "WriteToBoneTransforms " + transformCount);
//#endif

            TransformAccessArray boneRWTAA = crowdRenderSource.GetBoneRWTAA();
            NativeArray<int2> boneRWStatusData = crowdRenderSource.GetBoneReadWriteStatusData();
            NativeArray<Matrix4x4> matrixArray = crowdRenderSource.getTransformMatrixDelegate.Invoke();
            if (!boneRWTAA.isCreated || !matrixArray.IsCreated || !boneRWStatusData.IsCreated)
                return;
            GPUIDataBuffer<GPUITransformData> boneRWTransformData = crowdRenderSource.GetBoneRWTransformData();
            var bakedClipDataArray = crowdRenderSource.crowdRSG.rig.GetBakedClipDataArray();
            GPUIBoneTransformsWriteJob writeToTransformsJob = new GPUIBoneTransformsWriteJob()
            {
                p_boneData = boneRWTransformData.GetUnsafeNativeArrayPtr(),
                p_matrixArray = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(matrixArray),
                p_boneReadWriteStatusData = crowdRenderSource.GetUnsafeBoneReadWriteStatusDataPtr(),
                p_animatorClipDataArray = crowdRenderSource.crowdRSG.GetUnsafeAnimatorClipDataArrayPtr(true),
                p_clipFramesAndWeightsArray = crowdRenderSource.crowdRSG.GetUnsafeClipFramesAndWeightsArrayPtr(true),
                p_bakedBoneDataArray = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(crowdRenderSource.crowdRSG.rig.GetBakedBoneData(true)),
                p_bakedClipDataArray = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bakedClipDataArray),
                transformCount = transformCount,
                boneCount = boneCount,
                bufferStartIndex = crowdRenderSource.renderSource.bufferStartIndex,
                instanceCount = crowdRenderSource.renderSource.instanceCount,
                invalidStatus = new int2(-1, -1),
                bakedClipDataCount = bakedClipCount,
                currentTime = Time.time,
                deltaTime = deltaTime,
            };
            writeToTransformsJob.ScheduleByRef(boneRWTAA).Complete();
        }

        public static unsafe void ReadFromBoneTransforms(GPUICrowdRenderSource crowdRenderSource)
        {
            if (!crowdRenderSource.HasBoneRead)
                return;
            int transformCount = crowdRenderSource.GetBoneRWTransformCount();
            int boneCount = crowdRenderSource.boneCount;
            if (transformCount == 0 || crowdRenderSource.renderSource.instanceCount == 0 || crowdRenderSource.getTransformMatrixDelegate == null || boneCount <= 0)
                return;
            TransformAccessArray boneRWTAA = crowdRenderSource.GetBoneRWTAA();
            NativeArray<int2> boneRWStatusData = crowdRenderSource.GetBoneReadWriteStatusData();
            NativeArray<Matrix4x4> matrixArray = crowdRenderSource.getTransformMatrixDelegate.Invoke();
            if (!boneRWTAA.isCreated || !matrixArray.IsCreated || !boneRWStatusData.IsCreated)
                return;
            GPUIDataBuffer<GPUITransformData> boneRWTransformData = crowdRenderSource.GetBoneRWTransformData();
            GPUIBoneTransformsReadJob readTransformsToMatrixArrayJob = new GPUIBoneTransformsReadJob()
            {
                p_boneRWTransformData = boneRWTransformData.GetUnsafeNativeArrayPtr(),
                p_matrixArray = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(matrixArray),
                p_boneReadWriteStatusData = crowdRenderSource.GetUnsafeBoneReadWriteStatusDataPtr(),
                transformCount = transformCount,
                boneCount = boneCount,
                instanceCount = crowdRenderSource.renderSource.instanceCount,
                invalidStatus = new int2(-1, -1),
            };
            readTransformsToMatrixArrayJob.ScheduleReadOnlyByRef(boneRWTAA, boneCount).Complete();

            boneRWTransformData.SetBufferData(0, transformCount);

            ComputeShader cs = GPUICrowdConstants.CS_BoneBufferUtility;
            int kernelIndex = 1;
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_boneDataBuffer, crowdRenderSource.crowdRSG.boneDataBuffer);
            cs.SetBuffer(kernelIndex, GPUIConstants.PROP_sourceBuffer, boneRWTransformData.Buffer);
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_statusBuffer, crowdRenderSource.GetBoneReadWriteStatusBuffer());
            cs.SetInt(GPUIConstants.PROP_count, transformCount);
            cs.SetInt(GPUIConstants.PROP_startIndex, crowdRenderSource.renderSource.bufferStartIndex * boneCount);
            cs.DispatchX(kernelIndex, transformCount);
        }

        public static unsafe void CalculateRootMotion(GPUICrowdRenderSource crowdRenderSource, float deltaTime)
        {
            if (crowdRenderSource.renderSource.instanceCount == 0 || crowdRenderSource.getTransformAccessArrayDelegate == null)
                return;
            var transformAccessArray = crowdRenderSource.getTransformAccessArrayDelegate.Invoke();
            if (!transformAccessArray.isCreated || transformAccessArray.length == 0)
                return;
            var matrixArray = crowdRenderSource.getTransformMatrixDelegate.Invoke();
            if (!matrixArray.IsCreated)
                return;
            var bakedRootMotionData = crowdRenderSource.crowdRSG.rig.GetBakedRootMotionData();
            if (!bakedRootMotionData.IsCreated)
                return;
            var bakedClipDataArray = crowdRenderSource.crowdRSG.rig.GetBakedClipDataArray();
            var applyRootMotionJob = new GPUIApplyRootMotionJob()
            {
                p_matrixArray = NativeArrayUnsafeUtility.GetUnsafePtr(matrixArray),
                p_crowdInstanceDataBuffer = crowdRenderSource.crowdRSG.crowdInstanceDataBuffer.GetUnsafeReadOnlyNativeArrayPtr(),
                p_animatorClipDataArray = crowdRenderSource.crowdRSG.GetUnsafeAnimatorClipDataArrayPtr(true),
                p_clipFramesAndWeightsArray = crowdRenderSource.crowdRSG.GetUnsafeClipFramesAndWeightsArrayPtr(true),
                p_bakedRootMotionData = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(crowdRenderSource.crowdRSG.rig.GetBakedRootMotionData()),
                p_bakedClipDataArray = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bakedClipDataArray),
                bakedClipDataCount = crowdRenderSource.crowdRSG.rig.GetBakedClipCount(),
                bufferStartIndex = crowdRenderSource.renderSource.bufferStartIndex,
                instanceCount = crowdRenderSource.renderSource.instanceCount,
                boneCount = crowdRenderSource.crowdRSG.rig.bones.Count,
                currentTime = Time.time,
                deltaTime = deltaTime
            };
            applyRootMotionJob.Schedule(transformAccessArray).Complete();
            crowdRenderSource.setTransformMatrixModifiedDelegate.Invoke();
        }

        public unsafe static bool StartAnimationBlend(GPUICrowdRenderSourceGroup crowdRSG, int rsgBufferIndex, Vector4 animationWeights, int[] bakedClipIndexes, Vector4? normalizedClipTimes = null, Vector4? animationSpeeds = null, float transitionTime = 0, bool? isLoopingOverride = null, bool isSyncTime = true)
        {
            int boneCount = crowdRSG.rig.bones.Count;
            if (boneCount == 0)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not play animation without bone data.");
                return false;
            }
            if (animationWeights.x < 0 || animationWeights.y < 0 || animationWeights.z < 0 || animationWeights.w < 0)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not set negative weights for animation clips.");
                return false;
            }

            void* p_clipFramesAndWeightsArray = crowdRSG.GetUnsafeClipFramesAndWeightsArrayPtr();
            int weightIndex = rsgBufferIndex * 2 + 1;
            float weightTotal = animationWeights.x + animationWeights.y + animationWeights.z + animationWeights.w;
            if (weightTotal == 0)
            {
                UnsafeUtility.WriteArrayElementWithStride(p_clipFramesAndWeightsArray, weightIndex, 16, animationWeights);
                return true;
            }

            Vector4 normalizedTimes = normalizedClipTimes == null ? GPUICrowdConstants.DEFAULT_CLIP_START_TIMES : normalizedClipTimes.Value;
            Vector4 speeds = animationSpeeds == null ? GPUICrowdConstants.DEFAULT_CLIP_SPEEDS : animationSpeeds.Value;
            int weightedClipCount = 0;
            // Fix weights and ignore no-weight clips
            for (int i = 0; i < GPUICrowdConstants.ANIMATOR_MAX_CLIPS; i++)
            {
                float weight = animationWeights[i];
                if (weight > 0 && bakedClipIndexes[i] >= 0)
                {
                    animationWeights[weightedClipCount] = weight / weightTotal;
                    if (weightedClipCount != i)
                    {
                        bakedClipIndexes[weightedClipCount] = bakedClipIndexes[i];
                        normalizedTimes[weightedClipCount] = normalizedTimes[i];
                    }
                    speeds[weightedClipCount] = Mathf.Max(speeds[i], GPUICrowdConstants.MIN_CLIP_SPEED);
                    weightedClipCount++;
                }
            }
            for (int i = weightedClipCount; i < GPUICrowdConstants.ANIMATOR_MAX_CLIPS; i++)
                animationWeights[i] = 0f;

            if (weightedClipCount == 0)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not find animation clips with weights.");
                return false;
            }
            Vector4 targetWeight = animationWeights;

            crowdRSG.CompleteTransition(rsgBufferIndex);

            float currentTime = Time.time;
            int previousClipCount = 0;
            var previousAnimatorClipDataValues = GPUICrowdConstants.PREVIOUS_ANIMATOR_CLIP_DATA_VALUES;
            Vector4 previousWeights = UnsafeUtility.ReadArrayElementWithStride<Vector4>(p_clipFramesAndWeightsArray, weightIndex, 16);
            void* p_animatorClipDataArray = crowdRSG.GetUnsafeAnimatorClipDataArrayPtr();
            int animatorClipDataIndex = rsgBufferIndex * GPUICrowdConstants.ANIMATOR_MAX_CLIPS;
            bool4 isClipPreviouslyExisted = false;
            bool hasTransition = false;

            // Load previous clip data
            for (int i = 0; i < GPUICrowdConstants.ANIMATOR_MAX_CLIPS && weightedClipCount + previousClipCount <= GPUICrowdConstants.ANIMATOR_MAX_CLIPS; i++)
            {
                if (previousWeights[i] <= 0f)
                    break;
                var previousClipData = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdAnimatorClipData>(p_animatorClipDataArray, animatorClipDataIndex + i, GPUICrowdAnimatorClipData.STRIDE);
                if (!previousClipData.IsValid)
                    break;
                bool existsInNewClips = false;
                for (int j = 0; j < weightedClipCount; j++)
                {
                    if (bakedClipIndexes[j] == previousClipData.BakedClipIndex) // Clip existed previously
                    {
                        if (normalizedTimes[j] < 0f) // Set the start time to continue the animation
                            normalizedTimes[j] = previousClipData.GetNormalizedTime(currentTime) % 1f;
                        existsInNewClips = true;
                        isClipPreviouslyExisted[j] = true;
                        if (transitionTime > 0f) // Set the previous weight to current if transitioning
                        {
                            animationWeights[j] = previousWeights[i];
                            hasTransition = true;
                        }
                        break;
                    }
                }
                if (!existsInNewClips && transitionTime > 0f)
                {
                    previousAnimatorClipDataValues[previousClipCount] = previousClipData;
                    previousWeights[previousClipCount] = previousWeights[i];
                    previousClipCount++;
                    hasTransition = true;
                }
            }

            var animatorClipDataValues = GPUICrowdConstants.ANIMATOR_CLIP_DATA_VALUES;
            Vector4 clipLengths = Vector4.zero;
            var bakedClipDataArray = crowdRSG.rig.GetBakedClipDataArray();

            // Set initial clip data
            for (int i = 0; i < weightedClipCount; i++)
            {
                int bakedClipIndex = bakedClipIndexes[i];

                GPUICrowdBakedClipData bakedClipData = bakedClipDataArray[bakedClipIndex];
                int isLoopingMultiplier = bakedClipData.isLoopingMultiplier;
                if (isLoopingOverride != null)
                    isLoopingMultiplier = isLoopingOverride.Value ? 1 : -1;
                clipLengths[i] = bakedClipData.clipLength;
                GPUICrowdAnimatorClipData clipData = new();
                clipData.SetFrameStartIndexAndBakedClipIndex(bakedClipData.bakedBoneDataIndex, boneCount, bakedClipIndex);
                clipData.SetFrameCountSpeedAndLoop(bakedClipData.clipFrameCount, speeds[i], isLoopingMultiplier == 1);
                animatorClipDataValues[i] = clipData;
            }

            // Modify data for transition
            if (previousClipCount > 0)
            {
                for (int i = 0; i < weightedClipCount; i++)
                {
                    if (!isClipPreviouslyExisted[i]) // If new clip, set starting weights low
                        animationWeights[i] = 0.01f;
                }
                for (int i = 0; i < previousClipCount; i++)
                {
                    animationWeights[i + weightedClipCount] = previousWeights[i] * 0.99f;
                    var previousClipData = previousAnimatorClipDataValues[i];
                    clipLengths[i + weightedClipCount] = bakedClipDataArray[previousClipData.BakedClipIndex].clipLength;
                    speeds[i + weightedClipCount] = previousClipData.TargetSpeed;
                    normalizedTimes[i + weightedClipCount] = previousClipData.GetNormalizedTime(currentTime);
                    animatorClipDataValues[i + weightedClipCount] = previousClipData;
                }
                weightedClipCount += previousClipCount;

                // Fix weights after adjustments
                weightTotal = animationWeights.x + animationWeights.y + animationWeights.z + animationWeights.w;
                for (int i = 0; i < weightedClipCount; i++)
                    animationWeights[i] /= weightTotal;
            }


            // Set clipStartTime and clipLength
            if (isSyncTime)
            {
                float uniformNormalizedTime = 0f;
                float totalNormalizedTimeWeight = 0f;
                float weightedSpeededClipLength = 0f;
                for (int i = 0; i < weightedClipCount; i++)
                {
                    float weight = animationWeights[i];
                    if (normalizedTimes[i] >= 0)
                    {
                        uniformNormalizedTime += normalizedTimes[i] * weight;
                        totalNormalizedTimeWeight += weight;
                    }
                    weightedSpeededClipLength += clipLengths[i] * weight / speeds[i];
                }
                if (totalNormalizedTimeWeight > 0f)
                    uniformNormalizedTime /= totalNormalizedTimeWeight;

                for (int i = 0; i < weightedClipCount; i++)
                {
                    animatorClipDataValues[i].packedSpeedRelativeLengthAndSync = weightedSpeededClipLength;
                    animatorClipDataValues[i].clipStartTime = currentTime - uniformNormalizedTime * weightedSpeededClipLength;
                }
            }
            else
            {
                for (int i = 0; i < weightedClipCount; i++)
                {
                    float speededClipLength = clipLengths[i] / speeds[i];
                    animatorClipDataValues[i].packedSpeedRelativeLengthAndSync = -speededClipLength; // negative when not synched blend
                    if (normalizedTimes[i] < 0f)
                        animatorClipDataValues[i].clipStartTime = currentTime;
                    else
                        animatorClipDataValues[i].clipStartTime = currentTime - normalizedTimes[i] * speededClipLength;
                }
            }

            // Start transition
            if (hasTransition)
                crowdRSG.AddTransition(rsgBufferIndex, currentTime, transitionTime, animationWeights, targetWeight);

            // Set new animation data
            for (int i = 0; i < weightedClipCount; i++)
                UnsafeUtility.WriteArrayElementWithStride(p_animatorClipDataArray, animatorClipDataIndex + i, GPUICrowdAnimatorClipData.STRIDE, animatorClipDataValues[i]);
            UnsafeUtility.WriteArrayElementWithStride(p_clipFramesAndWeightsArray, weightIndex, 16, animationWeights);
            return true;
        }

        public unsafe static void SetAnimationSpeeds(GPUICrowdRenderSourceGroup crowdRSG, int rsgBufferIndex, Vector4 animationSpeeds)
        {
            int previousClipCount = 0;
            var previousAnimatorClipDataValues = GPUICrowdConstants.PREVIOUS_ANIMATOR_CLIP_DATA_VALUES;
            void* p_animatorClipDataArray = crowdRSG.GetUnsafeAnimatorClipDataArrayPtr();
            int animatorClipDataIndex = rsgBufferIndex * GPUICrowdConstants.ANIMATOR_MAX_CLIPS;
            var bakedClipDataArray = crowdRSG.rig.GetBakedClipDataArray();
            float currentTime = Time.time;

            // Load previous clip data
            for (int i = 0; i < GPUICrowdConstants.ANIMATOR_MAX_CLIPS; i++)
            {
                var previousClipData = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdAnimatorClipData>(p_animatorClipDataArray, animatorClipDataIndex + i, GPUICrowdAnimatorClipData.STRIDE);
                if (!previousClipData.IsValid)
                    break;
                previousClipData.SetSpeed(Mathf.Max(animationSpeeds[i], GPUICrowdConstants.MIN_CLIP_SPEED), bakedClipDataArray[previousClipData.BakedClipIndex].clipLength, currentTime);
                previousAnimatorClipDataValues[previousClipCount] = previousClipData;
                previousClipCount++;
            }

            for (int i = 0; i < previousClipCount; i++)
                UnsafeUtility.WriteArrayElementWithStride(p_animatorClipDataArray, animatorClipDataIndex + i, GPUICrowdAnimatorClipData.STRIDE, previousAnimatorClipDataValues[i]);
        }

        public unsafe static void ClearAnimatorClipData(GPUICrowdRenderSourceGroup crowdRSG, int rsgBufferIndex)
        {
#if GPUIPRO_DEVMODE
            Debug.Log(GPUIConstants.LOG_PREFIX + GPUIConstants.LOG_PREFIX_DEV + "ClearAnimatorClipData for index: " + rsgBufferIndex);
#endif
            void* p_animatorClipDataArray = crowdRSG.GetUnsafeAnimatorClipDataArrayPtr();
            int animatorClipDataIndex = rsgBufferIndex * GPUICrowdConstants.ANIMATOR_MAX_CLIPS;
            for (int i = 0; i < GPUICrowdConstants.ANIMATOR_MAX_CLIPS; i++)
                UnsafeUtility.WriteArrayElementWithStride(p_animatorClipDataArray, animatorClipDataIndex + i, GPUICrowdAnimatorClipData.STRIDE, GPUICrowdConstants.DEFAULT_CLIP_DATA);

            void* p_clipFramesAndWeightsArray = crowdRSG.GetUnsafeClipFramesAndWeightsArrayPtr();
            int framesAndWeightsIndex = rsgBufferIndex * 2;
            UnsafeUtility.WriteArrayElementWithStride(p_clipFramesAndWeightsArray, framesAndWeightsIndex, 64, GPUIConstants.Vector4_ZERO);
            UnsafeUtility.WriteArrayElementWithStride(p_clipFramesAndWeightsArray, framesAndWeightsIndex + 1, 64, GPUIConstants.Vector4_ZERO);

            crowdRSG.indexesToBindPoseReset.Add(rsgBufferIndex);
        }

        #endregion Render Source Methods

        #region Mecanim Extensions
        public static bool HasParameter(this Animator animator, string paramName)
        {
            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                if (param.name == paramName)
                    return true;
            }
            return false;
        }

        public static bool HasParameter(this Animator animator, int paramNameHash)
        {
            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                if (param.nameHash == paramNameHash)
                    return true;
            }
            return false;
        }
        #endregion Mecanim Extensions

        #region Editor Methods
#if UNITY_EDITOR
        public static ModelImporter GetModelImporter(GameObject obj)
        {
            if (obj == null)
                return null;

            // Check for Animator component and get the avatar reference
            ModelImporter importer = GetModelImporter(obj.GetComponent<Animator>());
            if (importer != null)
                return importer;

            // Check for SkinnedMeshRenderer and get the mesh reference
            importer = GetModelImporter(obj.GetComponentsInChildren<SkinnedMeshRenderer>());

            return importer;
        }

        public static ModelImporter GetModelImporter(Animator animator)
        {
            if (animator != null && animator.avatar != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(animator.avatar);
                ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer != null)
                    return importer;
            }
            return null;
        }

        public static ModelImporter GetModelImporter(SkinnedMeshRenderer[] skinnedMeshRenderers)
        {
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                if (skinnedMeshRenderer.sharedMesh != null)
                {
                    string assetPath = AssetDatabase.GetAssetPath(skinnedMeshRenderer.sharedMesh);
                    ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                    if (importer != null)
                        return importer;
                }
            }
            return null;
        }

        public static bool DeoptimizeTransformHierarchyOnModel(GPUICrowdInstance crowdInstance)
        {
            bool result = DeoptimizeTransformHierarchyOnModel(crowdInstance.Animator);
            if (result && GPUIPrefabUtility.IsPrefabAsset(crowdInstance.gameObject, out GameObject prefabObject, false) && prefabObject.TryGetComponent(out GPUICrowdInstance prefabCrowdInstance))
            {
                prefabCrowdInstance.LoadBoneTransforms(false, true);
                EditorUtility.SetDirty(prefabObject);
                GPUIPrefabUtility.MergeAllPrefabInstances(prefabObject);
            }
            return result;
        }

        public static bool DeoptimizeTransformHierarchyOnModel(Animator animator)
        {
            if (Application.isPlaying)
                return false;

            ModelImporter modelImporter = GetModelImporter(animator);
            if (modelImporter != null && modelImporter.optimizeGameObjects)
            {
#if GPUIPRO_DEVMODE
                Debug.Log(GPUIConstants.LOG_PREFIX + GPUIConstants.LOG_PREFIX_DEV + "Deoptimizing GameObjects for : " + animator.gameObject.name, modelImporter);
#endif
                modelImporter.optimizeGameObjects = false;
                modelImporter.SaveAndReimport();
                return true;
            }

            return false;
        }

        public static bool IsReadWriteEnabledOnModel(GameObject obj)
        {
            ModelImporter modelImporter = GetModelImporter(obj);
            if (modelImporter != null && !modelImporter.isReadable)
                return false;
            return true;
        }

        public static bool EnableReadWriteOnModel(GameObject obj)
        {
            if (Application.isPlaying)
                return false;

            ModelImporter modelImporter = GetModelImporter(obj);
            if (modelImporter != null && !modelImporter.isReadable)
            {
                Debug.Log(GPUIConstants.LOG_PREFIX + "Enabling Read/Write for : " + obj.name, modelImporter);
                modelImporter.isReadable = true;
                modelImporter.SaveAndReimport();
                return true;
            }

            return false;
        }
#endif
        #endregion Editor Methods
    }
}