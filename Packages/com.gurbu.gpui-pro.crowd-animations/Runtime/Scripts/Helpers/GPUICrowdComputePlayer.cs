// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GPUInstancerPro.CrowdAnimations
{
    /// <summary>
    /// The GPUI Crowd Compute Player component exposes parameters that allow testing animations using Compute Animator. You can easily experiment with different clips, weights, speeds, transitions, and more to find the ideal setup.
    /// </summary>
#if !UNITY_6000_3_0 && !GPUIPRO_NO_HELPURL
    [HelpURL("https://wiki.gurbu.com/index.php?title=GPU_Instancer_Pro-Crowd_Animations#GPUI_Crowd_Compute_Player")]
#endif
    [DefaultExecutionOrder(1100)]
    [RequireComponent(typeof(GPUICrowdInstance))]
    public class GPUICrowdComputePlayer : MonoBehaviour
    {
        public GPUICrowdInstance crowdInstance;
        public AnimationClip clip1;
        public AnimationClip clip2;
        public AnimationClip clip3;
        public AnimationClip clip4;
        public Vector4 weights = new Vector4(1f, 0f, 0f, 0f);
        public Vector4 speeds = Vector4.one;
        public float transitionTime;
        public bool forceLooping;
        public bool isSyncTime;

        private void Awake()
        {
            if (crowdInstance == null)
                crowdInstance = GetComponent<GPUICrowdInstance>();
        }

        public void Reset()
        {
            if (crowdInstance == null)
                crowdInstance = GetComponent<GPUICrowdInstance>();
#if UNITY_EDITOR
            EditorUtility.SetDirty(gameObject);
#endif
        }

        private void Start()
        {
            Play();
        }

        public void Play()
        {
            if (clip1 == null)
                return;

            if (clip2 == null)
                weights.y = 0f;
            if (clip3 == null)
                weights.z = 0f;
            if (clip4 == null)
                weights.w = 0f;

            float weightTotal = weights.x + weights.y + weights.z + weights.w;
            if (weightTotal <= 0f)
                weights = new Vector4(1f, 0f, 0f, 0f);
            else if (weightTotal != 1f)
            {
                weights.x /= weightTotal;
                weights.y /= weightTotal;
                weights.z /= weightTotal;
                weights.w /= weightTotal;
            }

            crowdInstance.StartBlend(weights, clip1, clip2, clip3, clip4, null, speeds, transitionTime, forceLooping ? true : null, isSyncTime);
        }
    }
}
