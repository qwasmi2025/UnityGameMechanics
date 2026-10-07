// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace GPUInstancerPro.CrowdAnimations
{
    public static class GPUICrowdAPI
    {
        #region Crowd Methods

        /// <typeparam name="T">Type of animator workflow</typeparam>
        /// <returns>The Animator workflow with the given type.</returns>
        public static T GetAnimatorWorkflow<T>() where T : GPUIAnimatorWorkflowBase
        {
            return (T)GPUICrowdSkinningSystem.GetAnimatorWorkflow(typeof(T));
        }

        /// <param name="animatorWorkflowID">Animator workflow ID</param>
        /// <returns>The Animator workflow with the given ID.</returns>
        public static GPUIAnimatorWorkflowBase GetAnimatorWorkflow(int animatorWorkflowID)
        {
            return GPUICrowdSkinningSystem.GetAnimatorWorkflow(animatorWorkflowID);
        }

        /// <summary>
        /// Sets the animator workflow for the specified instance based on the animator workflow ID.
        /// </summary>
        /// <param name="rendererKey">An integer key that uniquely identifies the renderer.</param>
        /// <param name="bufferIndex">The index of the instance in the buffer.</param>
        /// <param name="animatorWorkflowID">The animator workflow ID.</param>
        public static void SetAnimatorWorkflow(int rendererKey, int bufferIndex, int animatorWorkflowID)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            if (GPUIRenderingSystem.TryGetRenderSource(rendererKey, out var renderSource))
            {
                if (GPUICrowdSkinningSystem.TryGetCrowdRenderSourceGroup(renderSource.renderSourceGroup.Key, out var crowdRSG))
                    crowdRSG.crowdInstanceDataBuffer[renderSource.bufferStartIndex + bufferIndex] = new GPUICrowdInstanceData() { animatorWorkflowID = animatorWorkflowID };
            }
        }

        /// <summary>
        /// Sets the animator workflow for all instances associated with the specified renderer key.
        /// </summary>
        /// <param name="rendererKey">An integer key that uniquely identifies the renderer.</param>
        /// <param name="animatorWorkflow">The animator workflow.</param>
        public static void SetAnimatorWorkflowForAll(int rendererKey, GPUIAnimatorWorkflowBase animatorWorkflow)
        {
            SetAnimatorWorkflowForAll(rendererKey, animatorWorkflow.GetID());
        }

        /// <summary>
        /// Sets the animator workflow for all instances associated with the specified renderer key.
        /// </summary>
        /// <param name="rendererKey">An integer key that uniquely identifies the renderer.</param>
        /// <param name="animatorWorkflowID">The animator workflow ID.</param>
        public static void SetAnimatorWorkflowForAll(int rendererKey, int animatorWorkflowID)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            if (GPUIRenderingSystem.TryGetRenderSource(rendererKey, out var renderSource))
            {
                if (GPUICrowdSkinningSystem.TryGetCrowdRenderSourceGroup(renderSource.renderSourceGroup.Key, out var crowdRSG))
                    crowdRSG.crowdInstanceDataBuffer.SetData(renderSource.bufferStartIndex, renderSource.bufferSize, new GPUICrowdInstanceData() { animatorWorkflowID = animatorWorkflowID });
            }
        }

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
            return GPUICrowdUtility.StartAnimation(crowdInstance, animationClip, normalizedClipTime, speed, transitionTime, isLoopingOverride, isSyncTime);
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
            return GPUICrowdUtility.StartBlend(crowdInstance, animationWeights, animationClip1, animationClip2, animationClip3, animationClip4, normalizedClipTimes, animationSpeeds, transitionTime, isLoopingOverride, isSyncTime);
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
        public static bool StartAnimation(int rendererKey, int bufferIndex, AnimationClip animationClip, float normalizedClipTime = -1.0f, float speed = 1.0f, float transitionTime = 0, bool? isLoopingOverride = null, bool isSyncTime = false)
        {
            return GPUICrowdUtility.StartAnimation(GPUIAWComputeAnimator.Instance, rendererKey, bufferIndex, animationClip, normalizedClipTime, speed, transitionTime, isLoopingOverride, isSyncTime);
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
        public static bool StartBlend(int rendererKey, int bufferIndex, Vector4 animationWeights, AnimationClip animationClip1, AnimationClip animationClip2, AnimationClip animationClip3 = null, AnimationClip animationClip4 = null, Vector4? normalizedClipTimes = null, Vector4? animationSpeeds = null, float transitionTime = 0, bool? isLoopingOverride = null, bool isSyncTime = true)
        {
            return GPUICrowdUtility.StartBlend(GPUIAWComputeAnimator.Instance, rendererKey, bufferIndex, animationWeights, animationClip1, animationClip2, animationClip3, animationClip4, normalizedClipTimes, animationSpeeds, transitionTime, isLoopingOverride, isSyncTime);
        }

        /// <summary>
        /// Sets the animation speed for all active animations.
        /// </summary>
        /// <param name="rendererKey">Integer key that uniquely identifies the renderer</param>
        /// <param name="bufferIndex">The instance index on the buffer.</param>
        /// <param name="animationSpeeds">A Vector4 representing the speed values to apply.</param>
        public static void SetAnimationSpeeds(int rendererKey, int bufferIndex, Vector4 animationSpeeds)
        {
            GPUICrowdUtility.SetAnimationSpeeds(GPUIAWComputeAnimator.Instance, rendererKey, bufferIndex, animationSpeeds);
        }

        /// <summary>
        /// Bakes the specified animation clips for the given prefab.  
        /// This can be used in a loading scene to pre-bake animations and prevent performance spikes at runtime.
        /// </summary>
        /// <param name="prefab">The prefab for which the animation clips will be baked.</param>
        /// <param name="animationClips">The collection of animation clips to bake.</param>
        public static void BakeAnimationClips(GPUICrowdInstance prefab, params AnimationClip[] animationClips)
        {
            GPUICrowdUtility.BakeAnimationClips(prefab, GPUICrowdConstants.DEFAULT_CLIP_FRAME_RATE, animationClips);
        }

        /// <summary>
        /// Bakes the specified animation clips for the given prefab.  
        /// This can be used in a loading scene to pre-bake animations and prevent performance spikes at runtime.
        /// </summary>
        /// <param name="prefab">The prefab for which the animation clips will be baked.</param>
        /// <param name="animationClips">The collection of animation clips to bake.</param>
        public static void BakeAnimationClips(GPUICrowdInstance prefab, IEnumerable<AnimationClip> animationClips)
        {
            GPUICrowdUtility.BakeAnimationClips(prefab, GPUICrowdConstants.DEFAULT_CLIP_FRAME_RATE, animationClips);
        }
        #endregion
    }
}