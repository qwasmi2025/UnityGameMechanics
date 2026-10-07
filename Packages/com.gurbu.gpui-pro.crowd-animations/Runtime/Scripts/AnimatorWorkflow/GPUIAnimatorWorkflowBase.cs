// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using UnityEngine;

namespace GPUInstancerPro.CrowdAnimations
{
    public abstract class GPUIAnimatorWorkflowBase : IComparable<GPUIAnimatorWorkflowBase>
    {
        /// <summary>
        /// The animator workflow ID must be unique for each workflow and also defines the workflow type.
        /// <para>0 to 100 -> Workflows that use baked animation but rely on an external animator system (e.g., Mecanim Reader or Legacy Animation Reader).</para>
        /// <para>100 to 200 -> Workflows that provide bone transform data directly (e.g., Bone Tracker).</para>
        /// <para>200 to 300 -> Workflows that use baked animation data and include an internal custom animation system (e.g., Compute Animator).</para>
        /// </summary>
        /// <returns>A unique ID for the workflow.</returns>
        public abstract int GetID();
        /// <returns>Unique name for the workflow.</returns>
        public abstract string GetName();
        /// <summary>
        /// True for workflows that has an internal custom animation system (e.g., Compute Animator).
        /// </summary>
        public bool HasInternalAnimator()
        {
            return GPUICrowdSkinningSystem.HasAnimatorWorkflowInternalAnimator(GetID());
        }
        /// <summary>
        /// True for workflows that provide bone transform data directly (e.g., Bone Tracker).
        /// </summary>
        public bool IsBoneDataProvider()
        {
            return GPUICrowdSkinningSystem.IsAnimatorWorkflowBoneDataProvider(GetID());
        }

        /// <summary>
        /// Triggered before culling operations. Can be used to modify instance transforms (e.g., Root Motion).
        /// </summary>
        public virtual void ExecuteOnPreCull() { }

        /// <summary>
        /// Triggered before GPU animator calculations. Can be used to modify compute animator data.
        /// </summary>
        public virtual void ExecuteOnPreGPUAnimator() { }

        /// <summary>
        /// Triggered after animator calculations and before draw calls. Can be used to modify bone matrices (e.g., IK).
        /// </summary>
        public virtual void ExecuteOnPreRender() { }

        public virtual bool SetupCrowdInstanceForSkinning(GPUICrowdInstance crowdInstance)
        {
            if (crowdInstance == null || !crowdInstance.IsInstanced || !GPUICrowdSkinningSystem.TryGetOrCreateCrowdRenderSource(crowdInstance.PrefabComponent.renderKey, out var crowdRenderSource))
                return false;
            crowdInstance._crowdRenderSource = crowdRenderSource;
            if (crowdInstance._hasAnimator)
            {
                if (HasInternalAnimator())
                {
                    crowdInstance._animator.enabled = false;
                }
                else
                {
                    if (crowdInstance._animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
                    {
                        crowdInstance._animator.AddOrGetComponent<SpriteRenderer>().localBounds = crowdRenderSource.crowdRSG.bounds; // Add a dummy renderer for animator culling to function
                        //crowdInstance._animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    }
                    crowdInstance._animator.enabled = true;
                }
            }
            else if (crowdInstance._hasLegacyAnimation)
            {
                if (HasInternalAnimator())
                {
                    crowdInstance._legacyAnimation.enabled = false;
                }
                else
                {
                    if (crowdInstance._legacyAnimation.cullingType != AnimationCullingType.AlwaysAnimate)
                    {
                        crowdInstance._legacyAnimation.cullingType = AnimationCullingType.AlwaysAnimate;
                        crowdInstance._legacyAnimation.AddOrGetComponent<SpriteRenderer>().localBounds = crowdRenderSource.crowdRSG.bounds; // Add a dummy renderer for animator culling to function
                        crowdInstance._legacyAnimation.cullingType = AnimationCullingType.BasedOnRenderers;
                    }
                    crowdInstance._legacyAnimation.enabled = true;
                }
            }
            crowdInstance._crowdInstanceData.animatorWorkflowID = crowdInstance._animatorWorkflowID;
            crowdInstance._crowdInstanceData._settings = 0;
            crowdInstance.IsProcessed = false;
            crowdRenderSource._crowdInstancesToProcess.Add(crowdInstance);

            return true;
        }

        public int CompareTo(GPUIAnimatorWorkflowBase other)
        {
            return GetID().CompareTo(other.GetID());
        }

        public virtual bool StartAnimation(int rendererKey, int bufferIndex, AnimationClip animationClip, float normalizedClipTime = -1.0f, float speed = 1.0f, float transitionTime = 0, bool? isLoopingOverride = null, bool isSyncTime = false)
        {
            Debug.LogError(GPUIConstants.LOG_PREFIX + string.Format(GPUICrowdConstants.ERROR_NO_ANIMATOR, GetName()));
            return false;
        }

        public virtual bool StartBlend(int rendererKey, int bufferIndex, Vector4 animationWeights, AnimationClip animationClip1, AnimationClip animationClip2, AnimationClip animationClip3 = null, AnimationClip animationClip4 = null, Vector4? normalizedClipTimes = null, Vector4? animationSpeeds = null, float transitionTime = 0, bool? isLoopingOverride = null, bool isSyncTime = true)
        {
            Debug.LogError(GPUIConstants.LOG_PREFIX + string.Format(GPUICrowdConstants.ERROR_NO_ANIMATOR, GetName()));
            return false;
        }

        public virtual void SetAnimationSpeeds(int rendererKey, int bufferIndex, Vector4 animationSpeeds)
        {
            Debug.LogError(GPUIConstants.LOG_PREFIX + string.Format(GPUICrowdConstants.ERROR_NO_ANIMATOR, GetName()));
        }
    }
}
