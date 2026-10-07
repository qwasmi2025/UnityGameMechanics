// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using UnityEngine;
using UnityEngine.Profiling;

namespace GPUInstancerPro.CrowdAnimations
{
    /// <summary>
    /// Performant, GPU based animator that can be used with API calls.
    /// </summary>
    public class GPUIAWComputeAnimator : GPUIAnimatorWorkflowBase
    {
        #region Animator Workflow Definition
        public const int WORKFLOW_ID = 200;
        public const string WORKFLOW_NAME = "Compute Animator";
        public override int GetID() => WORKFLOW_ID;
        public override string GetName() => WORKFLOW_NAME;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AddAnimatorWorkflow() => GPUICrowdSkinningSystem.AddAnimatorWorkflow(new GPUIAWComputeAnimator());
        #endregion Animator Workflow Definition
        public static GPUIAWComputeAnimator Instance;
        public GPUIAWComputeAnimator() => Instance = this;

        private float _lastRootMotionExecutionTime = 0f;

        public override bool SetupCrowdInstanceForSkinning(GPUICrowdInstance crowdInstance)
        {
            if (!base.SetupCrowdInstanceForSkinning(crowdInstance))
                return false;

            if (crowdInstance.ApplyRootMotion)
            {
                var crowdInstanceData = crowdInstance._crowdInstanceData;
                crowdInstanceData.ApplyCustomRootMotion = true;
                crowdInstance._crowdInstanceData = crowdInstanceData;
            }

            return true;
        }

        public override void ExecuteOnPreCull()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            Profiler.BeginSample("GPUIAWComputeAnimator.CalculateRootMotion");
            float currentTime = Time.time;
            float deltaRootMotionTime = currentTime - _lastRootMotionExecutionTime;
            foreach (var crowdRenderSource in GPUICrowdSkinningSystem.Instance.RenderSourceProvider.Values)
            {
                if (crowdRenderSource.HasComputeRootMotion)
                    GPUICrowdUtility.CalculateRootMotion(crowdRenderSource, deltaRootMotionTime);
            }
            _lastRootMotionExecutionTime = currentTime;
            Profiler.EndSample();
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
        public override bool StartAnimation(int rendererKey, int bufferIndex, AnimationClip animationClip, float normalizedClipTime = -1.0f, float speed = 1.0f, float transitionTime = 0, bool? isLoopingOverride = null, bool isSyncTime = false)
        {
            return GPUICrowdUtility.StartAnimation(this, rendererKey, bufferIndex, animationClip, normalizedClipTime, speed, transitionTime, isLoopingOverride, isSyncTime);
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
        public override bool StartBlend(int rendererKey, int bufferIndex, Vector4 animationWeights, AnimationClip animationClip1, AnimationClip animationClip2, AnimationClip animationClip3 = null, AnimationClip animationClip4 = null, Vector4? normalizedClipTimes = null, Vector4? animationSpeeds = null, float transitionTime = 0, bool? isLoopingOverride = null, bool isSyncTime = true)
        {
            return GPUICrowdUtility.StartBlend(this, rendererKey, bufferIndex, animationWeights, animationClip1, animationClip2, animationClip3, animationClip4, normalizedClipTimes, animationSpeeds, transitionTime, isLoopingOverride, isSyncTime);
        }

        /// <summary>
        /// Sets the animation speed for all active animations.
        /// </summary>
        /// <param name="rendererKey">Integer key that uniquely identifies the renderer</param>
        /// <param name="bufferIndex">The instance index on the buffer.</param>
        /// <param name="animationSpeeds">Speed value to apply.</param>
        public override void SetAnimationSpeeds(int rendererKey, int bufferIndex, Vector4 animationSpeeds)
        {
            GPUICrowdUtility.SetAnimationSpeeds(this, rendererKey, bufferIndex, animationSpeeds);
        }
    }
}
