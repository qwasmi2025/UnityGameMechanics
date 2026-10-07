// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;

namespace GPUInstancerPro.CrowdAnimations
{
    /// <summary>
    /// Reads the animator states from Unity's Mecanim Animator and uses the baked clip data to modify bones in the GPU.
    /// </summary>
    public class GPUIAWMecanimReader : GPUIAnimatorWorkflowBase
    {
        #region Animator Workflow Definition
        public const int WORKFLOW_ID = 0;
        public const string WORKFLOW_NAME = "Mecanim Reader";
        public override int GetID() => WORKFLOW_ID;
        public override string GetName() => WORKFLOW_NAME;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AddAnimatorWorkflow() => GPUICrowdSkinningSystem.AddAnimatorWorkflow(new GPUIAWMecanimReader());
        #endregion Animator Workflow Definition

        public override void ExecuteOnPreGPUAnimator()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            ReadMecanimAnimator();
        }

        /// <summary>
        /// Reads clip info from Mecanim Animator on all instances.
        /// </summary>
        public static void ReadMecanimAnimator()
        {
            Profiler.BeginSample("GPUIAWMecanimReader.ReadMecanimAnimator");
            var clipInfoList = GPUICrowdConstants.ANIMATOR_CLIP_INFO_LIST;
            AnimationClip[] animationClips = GPUICrowdConstants.CLIPS_ARRAY;
            int layerIndex = 0;
            int frameCount = Time.frameCount;
            int maxDelay = Math.Max(GPUICrowdRuntimeSettings.Instance.mecanimReaderMaxDelay, 1);
            int minReadCount = Math.Max(GPUICrowdRuntimeSettings.Instance.mecanimReaderMinReadCount, 1);
            var computeAnimator = GPUIAWComputeAnimator.Instance;
            foreach (var crowdRenderSource in GPUICrowdSkinningSystem.Instance.RenderSourceProvider.Values)
            {
                if (crowdRenderSource._mecanimReaderInstances.Count == 0 || crowdRenderSource.renderSource.bufferSize == 0 || crowdRenderSource.renderSource.bufferStartIndex < 0)
                    continue;

                int readsThisFrame = 0;
                int instanceCount = crowdRenderSource._mecanimReaderInstances.Count;
                int maxReadCount = Math.Max(Mathf.CeilToInt(instanceCount / (float)maxDelay), minReadCount);
                int startIndex = Time.frameCount % instanceCount;
                for (int j = 0; j < instanceCount; j++)
                {
                    int i = (startIndex + j) % instanceCount; // Randomizing the update order to avoid starvation when the maxReadCount is low

                    GPUICrowdInstance crowdInstance = crowdRenderSource._mecanimReaderInstances[i];

                    bool wasRecentlyChecked = crowdInstance._lastMecanimStateCheck > 0 && frameCount - crowdInstance._lastMecanimStateCheck < maxDelay;

                    if (readsThisFrame >= maxReadCount && wasRecentlyChecked)
                        continue;

                    GPUIPrefabBase prefabComponent = crowdInstance.PrefabComponent;
                    if (prefabComponent == null)
                    {
                        crowdRenderSource._mecanimReaderInstances.RemoveAtSwapBack(i);
                        instanceCount--;
                        j--;
                        continue;
                    }
                    int bufferIndex = prefabComponent.bufferIndex;
                    if (bufferIndex < 0 || bufferIndex >= crowdRenderSource.renderSource.bufferSize)
                        continue;

                    Profiler.BeginSample("GPUIAWMecanimReader.ReadMecanimAnimator.GetCurrentAnimatorStateInfo");
                    Animator animator = crowdInstance._animator;
                    int activeClipCount = animator.GetCurrentAnimatorClipInfoCount(layerIndex);
                    if (activeClipCount == 0)
                        continue;

                    AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);
                    bool isInTransition = animator.IsInTransition(layerIndex);
                    crowdInstance._lastMecanimStateCheck = frameCount;
                    readsThisFrame++;
                    Profiler.EndSample();

                    if (!isInTransition && activeClipCount == 1 && crowdInstance._currentMecanimActiveClipCount == activeClipCount && crowdInstance._currentMecanimStateHash == stateInfo.fullPathHash)
                        continue;

                    Profiler.BeginSample("GPUIAWMecanimReader.ReadMecanimAnimator.StateUpdate");
                    crowdInstance._currentMecanimStateHash = stateInfo.fullPathHash;
                    crowdInstance._lastMecanimStateCheck = -1;
                    crowdInstance._currentMecanimActiveClipCount = activeClipCount;

                    Vector4 weights = Vector4.zero;
                    Vector4 normalizedClipTimes = Vector4.zero;
                    Vector4 animationSpeeds = Vector4.one;

                    animator.GetCurrentAnimatorClipInfo(layerIndex, clipInfoList);

                    for (int c = 0; c < GPUICrowdConstants.ANIMATOR_MAX_CLIPS; c++)
                    {
                        if (c < activeClipCount)
                        {
                            AnimatorClipInfo clipInfo = clipInfoList[c];
                            animationClips[c] = clipInfo.clip;
                            weights[c] = clipInfo.weight;
                            float normalizedTime = stateInfo.normalizedTime % 1.0f;
                            normalizedClipTimes[c] = normalizedTime;
                            animationSpeeds[c] = stateInfo.speed;
                        }
                        else
                            animationClips[c] = null;
                    }

                    #region Transition
                    if (isInTransition && activeClipCount < GPUICrowdConstants.ANIMATOR_MAX_CLIPS)
                    {
                        stateInfo = animator.GetNextAnimatorStateInfo(layerIndex);
                        animator.GetNextAnimatorClipInfo(layerIndex, clipInfoList);
                        int transitioningClipCount = clipInfoList.Count;
                        for (int c = 0; c < transitioningClipCount; c++)
                        {
                            int index = c + activeClipCount;
                            if (index >= 4)
                                break;
                            AnimatorClipInfo clipInfo = clipInfoList[c];
                            animationClips[index] = clipInfo.clip;
                            weights[index] = clipInfo.weight;
                            float normalizedTime = stateInfo.normalizedTime % 1.0f;
                            normalizedClipTimes[index] = normalizedTime;
                            animationSpeeds[index] = stateInfo.speed;
                        }
                    }
                    #endregion Transition
                    Profiler.EndSample();

                    Profiler.BeginSample("GPUIAWMecanimReader.ReadMecanimAnimator.StartBlend");
                    GPUICrowdUtility.StartBlend(computeAnimator, crowdRenderSource.crowdRSG, bufferIndex + crowdRenderSource.renderSource.bufferStartIndex, weights, animationClips, normalizedClipTimes, animationSpeeds, 0, stateInfo.loop ? true : null, false);
                    Profiler.EndSample();
                }
            }
            Profiler.EndSample();
        }
    }
}
