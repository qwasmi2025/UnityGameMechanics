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
    /// While the Compute Player allows you to play animation clips on a single instance, the Batch Compute Player component allows animations to be played on all instances in the scene. It enables easy playback across all instances, with options to randomize clips and their start times.
    /// </summary>
#if !UNITY_6000_3_0 && !GPUIPRO_NO_HELPURL
    [HelpURL("https://wiki.gurbu.com/index.php?title=GPU_Instancer_Pro-Crowd_Animations#GPUI_Crowd_Batch_Compute_Player")]
#endif
    [DefaultExecutionOrder(1100)]
    public class GPUICrowdBatchComputePlayer : MonoBehaviour
    {
        public GPUICrowdInstance prefab;
        public bool applyRootMotion;
        public bool bakeClipsAtStart;
        public AnimationClip[] clips;
        public bool playRandomClip;
        public bool randomStartTime;
        [Range(0f, 4f)]
        public float speed = 1.0f;
        [Range(0f, 10f)]
        public float transitionTime;
        public float repeatTime;
        public bool isForceLooping;
        public bool isSyncTime;

        private IGPUIInstanceTransformProvider _instanceTransformProvider;
        private List<GPUICrowdInstance> _crowdInstances;
        private int _playCounter;
        private float _lastPlayTime;
        private int _lastInstanceCount;
        private bool _isCrowdInstancesModified;

        private void OnEnable()
        {
            _crowdInstances = new List<GPUICrowdInstance>();
            _lastPlayTime = -repeatTime - 1f;
            _lastInstanceCount = 0;
            _playCounter = 0;
        }

        private void Start()
        {
            if (bakeClipsAtStart && prefab != null && clips != null && clips.Length > 0)
                GPUICrowdUtility.BakeAnimationClips(prefab, GPUICrowdConstants.DEFAULT_CLIP_FRAME_RATE, clips);
        }

        public void Update()
        {
            if (clips == null || clips.Length == 0)
                return;
            if (prefab == null)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "GPUICrowdBatchComputePlayer assigned prefab is null!", gameObject);
                enabled = false;
                return;
            }
            LoadCrowdInstances();

            bool isRepeat = repeatTime > 0 && Time.time - _lastPlayTime > repeatTime;
            if (isRepeat || _lastPlayTime < 0 || _isCrowdInstancesModified)
            {
                _lastPlayTime = Time.time;
                AnimationClip clip = clips[_playCounter % clips.Length];
                for (int i = 0; i < _crowdInstances.Count; i++)
                {
                    GPUICrowdInstance crowdInstance = _crowdInstances[i];
                    if (crowdInstance == null || crowdInstance._animatorWorkflowID != GPUIAWComputeAnimator.WORKFLOW_ID 
                        || (!isRepeat && crowdInstance.TryGetCrowdAnimatorClipData(0, out _, out float clipWeight) && clipWeight != 0f) // Start animations only for new instances when adding them, instead of restarting all instances.
                        )
                        continue;
                    if (playRandomClip)
                        clip = clips[Random.Range(0, clips.Length)];
                    crowdInstance.StartAnimation(clip, randomStartTime ? Random.Range(0f, 1f) : -1, speed, transitionTime, isForceLooping ? true : null, isSyncTime);
                }
                if (_crowdInstances.Count > 0)
                    _playCounter++;
                _isCrowdInstancesModified = false;
            }
        }

        private void LoadCrowdInstances()
        {
            if (_instanceTransformProvider == null)
            {
                if (!GPUIRenderingSystem.IsActive)
                    return;
                foreach (GPUIManager m in GPUIRenderingSystem.Instance.ActiveGPUIManagers)
                {
                    if (m is IGPUIInstanceTransformProvider itp)
                    {
                        _instanceTransformProvider = itp;
                        break;
                    }
                }
            }
            if (_instanceTransformProvider == null)
                return;

            int prefabID = _instanceTransformProvider.GetPrefabID(prefab.gameObject);
            int instanceCount = _instanceTransformProvider.GetInstanceCount(prefabID);
            if (instanceCount != _lastInstanceCount)
            {
                Transform[] instanceTransforms = _instanceTransformProvider.GetInstanceTransforms(prefabID);
                if (instanceTransforms == null)
                    return;
                _lastInstanceCount = instanceCount;
                _crowdInstances.Clear();
                foreach (Transform t in instanceTransforms)
                {
                    if (t != null && t.TryGetComponent(out GPUICrowdInstance crowdInstance) && crowdInstance.Animator != null)
                    {
                        _crowdInstances.Add(crowdInstance);
                        crowdInstance.ApplyRootMotion = applyRootMotion;
                    }
                }
                _isCrowdInstancesModified = true;
            }
        }

        public float GetTransitionCompletionAmount()
        {
            if (transitionTime <= 0 || _playCounter <= 1 || _lastPlayTime < 0f)
                return 1f;
            float timePassedSinceLastPlay = Time.time - _lastPlayTime;
            if (transitionTime <= timePassedSinceLastPlay)
                return 1f;
            return 1f - ((transitionTime - timePassedSinceLastPlay) / transitionTime);
        }

        public List<GPUICrowdInstance> GetCrowdInstances()
        {
            return _crowdInstances;
        }

        public void RestartAnimations()
        {
            _lastPlayTime = -repeatTime - 1f;
        }

        public void SetAnimationSpeeds()
        {
            if (_crowdInstances == null) return;
            for (int i = 0; i < _crowdInstances.Count; i++)
            {
                GPUICrowdInstance crowdInstance = _crowdInstances[i];
                if (crowdInstance == null || crowdInstance._animatorWorkflowID != GPUIAWComputeAnimator.WORKFLOW_ID) continue;
                crowdInstance.SetAnimationSpeeds(speed);
            }
        }

        private void OnValidate()
        {
            if (GPUICrowdSkinningSystem.IsActive && Application.isPlaying && isActiveAndEnabled)
                SetAnimationSpeeds();
        }
    }
}
