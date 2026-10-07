// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using UnityEngine;

namespace GPUInstancerPro.CrowdAnimations
{
    /// <summary>
    /// Reads the transform data for each bone and uploads it to the GPU. It offers the most versatile usage but has limited performance.
    /// </summary>
    public class GPUIAWBoneTracker : GPUIAnimatorWorkflowBase
    {
        #region Animator Workflow Definition
        public const int WORKFLOW_ID = 100;
        public const string WORKFLOW_NAME = "Bone Tracker";
        public override int GetID() => WORKFLOW_ID;
        public override string GetName() => WORKFLOW_NAME;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AddAnimatorWorkflow() => GPUICrowdSkinningSystem.AddAnimatorWorkflow(new GPUIAWBoneTracker());
        #endregion Animator Workflow Definition

        public override bool SetupCrowdInstanceForSkinning(GPUICrowdInstance crowdInstance)
        {
            if (!base.SetupCrowdInstanceForSkinning(crowdInstance))
                return false;

            crowdInstance.LoadBoneTransforms(false, true); // Deoptimize bone hierarchy and load all bone transforms.
            crowdInstance.SetAllBonesRead();

            return true;
        }
    }
}
