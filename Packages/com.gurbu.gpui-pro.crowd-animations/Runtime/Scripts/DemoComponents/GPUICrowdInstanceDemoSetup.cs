// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using UnityEngine;

namespace GPUInstancerPro.CrowdAnimations
{
    public class GPUICrowdInstanceDemoSetup : MonoBehaviour
    {
        [Header("Crowd Instance Setup")]
        public GPUIAnimatorWorkflowEnum animatorWorkflow;
        public bool applyRootMotion;
        public bool optimizeGameObjects;

        [Header("Transition Indicator")]
        public GPUICrowdBatchComputePlayer[] batchComputePlayers;
        public RectTransform[] transitionIndicators;

        [Header("Material Variation")]
        public Color[] colorVariations;

        private void LateUpdate()
        {
            if (batchComputePlayers != null && transitionIndicators != null && batchComputePlayers.Length == transitionIndicators.Length)
            {
                for (int i = 0; i < batchComputePlayers.Length; i++)
                {
                    var batchComputePlayer = batchComputePlayers[i];
                    var transitionIndicator = transitionIndicators[i];
                    if (batchComputePlayer != null && transitionIndicator != null && batchComputePlayer.transitionTime > 0)
                        transitionIndicator.offsetMax = new Vector2(-100f * (1f - batchComputePlayer.GetTransitionCompletionAmount()), transitionIndicator.offsetMax.y);
                }
            }
        }

        public void SetupCrowdInstance(GameObject gameObject)
        {
            if (gameObject == null || !gameObject.TryGetComponent(out GPUICrowdInstance crowdInstance))
                return;
            int workflowID = 0;
            switch (animatorWorkflow)
            {
                case GPUIAnimatorWorkflowEnum.MecanimReader:
                    workflowID = GPUIAWMecanimReader.WORKFLOW_ID;
                    break;
                case GPUIAnimatorWorkflowEnum.ComputeAnimator:
                    workflowID = GPUIAWComputeAnimator.WORKFLOW_ID;
                    break;
                case GPUIAnimatorWorkflowEnum.BoneTracker:
                    workflowID = GPUIAWBoneTracker.WORKFLOW_ID;
                    break;
            }
            crowdInstance.AnimatorWorkflowID = workflowID;
            crowdInstance.ApplyRootMotion = applyRootMotion;
            if (crowdInstance.Animator != null)
                crowdInstance.Animator.applyRootMotion = applyRootMotion;
            if (optimizeGameObjects && !GPUICrowdSkinningSystem.IsAnimatorWorkflowBoneDataProvider(workflowID))
                AnimatorUtility.OptimizeTransformHierarchy(gameObject, null);
        }

        public void SetMaterialVariation(GameObject gameObject)
        {
            if (colorVariations == null || colorVariations.Length == 0)
                return;
            if (!gameObject.TryGetComponent(out GPUIPrefabBase prefabBase))
                return;
            prefabBase.SetMaterialVariation(0, colorVariations[Random.Range(0, colorVariations.Length)]);
        }

        public void RandomizeParts(GameObject gameObject)
        {
            if (gameObject.TryGetComponent(out GPUIPrefabBase prefabBase) && prefabBase.childOptionalRenderers != null)
            {
                for (int i = 0; i < prefabBase.childOptionalRenderers.Count; i++)
                {
                    if (Random.Range(0f, 1f) < 0.5f)
                        prefabBase.childOptionalRenderers[i].gameObject.SetActive(false);
                }
            }
        }

        public enum GPUIAnimatorWorkflowEnum
        {
            MecanimReader,
            ComputeAnimator,
            BoneTracker
        }
    }
}
