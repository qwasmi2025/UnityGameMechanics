// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;

namespace GPUInstancerPro.CrowdAnimations
{
    /// <summary>
    /// Reads the animation states from Unity's Legacy Animation and uses the baked clip data to modify bones in the GPU.
    /// </summary>
    public class GPUIAWLegacyAnimationReader : GPUIAnimatorWorkflowBase
    {
        #region Animator Workflow Definition
        public const int WORKFLOW_ID = 1;
        public const string WORKFLOW_NAME = "Legacy Animation Reader";
        public override int GetID() => WORKFLOW_ID;
        public override string GetName() => WORKFLOW_NAME;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AddAnimatorWorkflow() => GPUICrowdSkinningSystem.AddAnimatorWorkflow(new GPUIAWLegacyAnimationReader());
        #endregion Animator Workflow Definition

        public override void ExecuteOnPreGPUAnimator()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            ReadLegacyAnimation();
        }

        /// <summary>
        /// Reads clip info from Legacy Animation on all instances.
        /// </summary>
        public static void ReadLegacyAnimation()
        {
            Profiler.BeginSample("GPUIAWLegacyAnimationReader.ReadLegacyAnimation");
            AnimationClip[] animationClips = GPUICrowdConstants.CLIPS_ARRAY;
            int frameCount = Time.frameCount;
            int maxDelay = Math.Max(GPUICrowdRuntimeSettings.Instance.legacyReaderMaxDelay, 1);
            int minReadCount = Math.Max(GPUICrowdRuntimeSettings.Instance.legacyReaderMinReadCount, 1);
            var computeAnimator = GPUIAWComputeAnimator.Instance;
            foreach (var crowdRenderSource in GPUICrowdSkinningSystem.Instance.RenderSourceProvider.Values)
            {
                if (crowdRenderSource._legacyReaderInstances.Count == 0 || crowdRenderSource.renderSource.bufferSize == 0 || crowdRenderSource.renderSource.bufferStartIndex < 0)
                    continue;

                int readsThisFrame = 0;
                int instanceCount = crowdRenderSource._legacyReaderInstances.Count;
                int maxReadCount = Math.Max(Mathf.CeilToInt(instanceCount / (float)maxDelay), minReadCount);
                int startIndex = Time.frameCount % instanceCount;
                for (int j = 0; j < instanceCount; j++)
                {
                    int i = (startIndex + j) % instanceCount; // Randomizing the update order to avoid starvation when the maxReadCount is low

                    GPUICrowdInstance crowdInstance = crowdRenderSource._legacyReaderInstances[i];

                    bool wasRecentlyChecked = crowdInstance._lastLegacyStateCheck > 0 && frameCount - crowdInstance._lastLegacyStateCheck < maxDelay;

                    if (readsThisFrame >= maxReadCount && wasRecentlyChecked)
                        continue;

                    GPUIPrefabBase prefabComponent = crowdInstance.PrefabComponent;
                    if (prefabComponent == null)
                    {
                        crowdRenderSource._legacyReaderInstances.RemoveAtSwapBack(i);
                        instanceCount--;
                        j--;
                        continue;
                    }
                    int bufferIndex = prefabComponent.bufferIndex;
                    if (bufferIndex < 0 || bufferIndex >= crowdRenderSource.renderSource.bufferSize)
                        continue;

                    Profiler.BeginSample("GPUIAWLegacyAnimationReader.ReadLegacyAnimation.GetAnimationState");

                    Vector4 weights = Vector4.zero;
                    Vector4 normalizedClipTimes = Vector4.zero;
                    Vector4 animationSpeeds = Vector4.one;
                    int clipCount = 0;
                    foreach (AnimationState state in crowdInstance._legacyStates)
                    {
                        if (state.enabled)
                        {
                            animationClips[clipCount] = state.clip;
                            weights[clipCount] = state.weight;
                            normalizedClipTimes[clipCount] = state.normalizedTime;
                            animationSpeeds[clipCount] = state.speed;
                            clipCount++;
                            if (clipCount == 4)
                                break;
                        }
                    }
                    crowdInstance._lastLegacyStateCheck = frameCount;
                    readsThisFrame++;
                    if (clipCount == 0)
                        continue;

                    GPUICrowdUtility.StartBlend(computeAnimator, crowdRenderSource.crowdRSG, bufferIndex + crowdRenderSource.renderSource.bufferStartIndex, weights, animationClips, normalizedClipTimes, animationSpeeds, 0, true, false);
                    Profiler.EndSample();
                }
            }
            Profiler.EndSample();
        }
    }
}
