// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using UnityEngine;
using System.Collections.Generic;
using Unity.Collections;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GPUInstancerPro.CrowdAnimations
{
    /// <summary>
    /// This component is automatically attached to prefabs that are used as crowd prototypes to identify them
    /// </summary>
#if !UNITY_6000_3_0 && !GPUIPRO_NO_HELPURL
    [HelpURL("https://wiki.gurbu.com/index.php?title=GPU_Instancer_Pro-Crowd_Animations#GPUI_Crowd_Instance")]
#endif
    [DefaultExecutionOrder(-210)]
    public class GPUICrowdInstance : GPUISkinningBase
    {
        #region Serialized Properties
        [SerializeField]
        internal int _animatorWorkflowID;
        [SerializeField]
        internal GPUICrowdRig _crowdRig;
        [SerializeField]
        internal List<BoneTransformReference> _boneTransforms;
        [SerializeField]
        internal Animator _animator;
        [SerializeField]
        internal Animation _legacyAnimation;
        [SerializeField]
        internal GPUIPrefabBase _gpuiPrefab;
        [SerializeField]
        private bool _applyRootMotion;
        #endregion Serialized Properties

        #region Runtime Properties
        [NonSerialized]
        internal GPUICrowdInstanceData _crowdInstanceData;

        public GPUICrowdRig Rig => _crowdRig;
        public List<BoneTransformReference> BoneTransforms => _boneTransforms;

        public int AnimatorWorkflowID
        {
            get => _animatorWorkflowID;
            set
            {
                if (_animatorWorkflowID == value)
                    return;
                //var animatorWorkflow = GPUICrowdSkinningSystem.GetAnimatorWorkflow(value);
                //if (animatorWorkflow == null)
                //{
                //    Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not find an Animator Workflow with ID: " + _animatorWorkflowID);
                //    return;
                //}
                if (_animatorWorkflowID == GPUIAWBoneTracker.WORKFLOW_ID) // If previous workflow was Bone Tracker, clear read bone status.
                    ClearAllBoneReadWrite();
                _animatorWorkflowID = value;
                OnInstancingStatusModified();
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    EditorUtility.SetDirty(gameObject);
                    if (PrefabUtility.IsPartOfPrefabInstance(this))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(this);
                    if (PrefabUtility.IsPartOfPrefabAsset(this))
                        GPUIPrefabUtility.MergeAllPrefabInstances(gameObject);
                }
#endif
            }
        }

        public GPUIAnimatorWorkflowBase AnimatorWorkflow => GPUICrowdSkinningSystem.GetAnimatorWorkflow(_animatorWorkflowID);

        public GPUIPrefabBase PrefabComponent => _gpuiPrefab;
        public bool IsInstanced => _hasPrefabComponent && _gpuiPrefab.renderKey != 0;
        public Animator Animator => _animator;
        public bool HasTransformHierarchy => _animator == null || _animator.hasTransformHierarchy;
        public Animation LegacyAnimation => _legacyAnimation;
        private bool _isProcessed;
        public bool IsProcessed 
        { 
            get => _isProcessed; 
            internal set
            {
                if (_isProcessed == value)
                    return;
                _isProcessed = value;
                if (_isProcessed && _OnProcessed != null)
                {
                    _OnProcessed.Invoke();
                    _OnProcessed = null;
                }
            } 
        }
        internal Action _OnProcessed;

        public bool ApplyRootMotion
        {
            get => _applyRootMotion;
            set
            {
                if (_applyRootMotion == value)
                    return;
                _applyRootMotion = value;
                OnInstancingStatusModified();
            }
        }

        // null check is slow
        internal bool _hasPrefabComponent; 
        internal bool _hasAnimator;
        internal bool _hasLegacyAnimation;

        internal int _lastMecanimStateCheck;
        internal int _currentMecanimStateHash;
        internal int _currentMecanimActiveClipCount;

        internal int _lastLegacyStateCheck;
        internal AnimationState[] _legacyStates;

        [NonSerialized]
        internal GPUICrowdRenderSource _crowdRenderSource;
        [NonSerialized]
        internal bool _hasAssignedBoneReferences;
        #endregion Runtime Properties

        #region MonoBehaviour Methods
        private void Awake()
        {
            if (_gpuiPrefab == null)
                _gpuiPrefab = GetComponent<GPUIPrefabBase>();
            if (_gpuiPrefab != null)
            {
                _gpuiPrefab.OnInstancingStatusModified -= OnInstancingStatusModified;
                _gpuiPrefab.OnInstancingStatusModified += OnInstancingStatusModified;
                _gpuiPrefab.OnBufferIndexModified -= OnBufferIndexModified;
                _gpuiPrefab.OnBufferIndexModified += OnBufferIndexModified;
                _hasPrefabComponent = true;
            }
            else
                _hasPrefabComponent = false;

            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();
            _hasAnimator = _animator != null && _animator.runtimeAnimatorController != null;

            _hasLegacyAnimation = false;
            if (!_hasAnimator)
            {
                if (_legacyAnimation == null)
                    _legacyAnimation = GetComponentInChildren<Animation>();
                _hasLegacyAnimation = _legacyAnimation != null;
            }
        }

#if UNITY_EDITOR
        private void Reset()
        {
            _gpuiPrefab = GetComponent<GPUIPrefabBase>();
            _animator = GetComponentInChildren<Animator>();
            _legacyAnimation = GetComponentInChildren<Animation>();
            if (_animator != null)
                AnimatorWorkflowID = GPUIAWMecanimReader.WORKFLOW_ID;
            //else if (_legacyAnimation != null)
            //    AnimatorWorkflowID = GPUIAWLegacyAnimationReader.WORKFLOW_ID;
            else
                AnimatorWorkflowID = GPUIAWBoneTracker.WORKFLOW_ID;
        }
#endif
        #endregion MonoBehaviour Methods

        public void OnInstancingStatusModified()
        {
            IsProcessed = false;
            _currentMecanimStateHash = 0;
            if (IsInstanced)
                AnimatorWorkflow?.SetupCrowdInstanceForSkinning(this);
            else
                _crowdRenderSource = null;
        }

        public void OnBufferIndexModified(int previousBufferIndex)
        {
            if (_crowdRenderSource == null) // Removed IsInstanced check so the instance data can be cleared when removing an instance.
                return;
            _crowdRenderSource.OnBufferIndexModified(this, previousBufferIndex, _gpuiPrefab.bufferIndex);
        }

        public GPUICrowdRig LoadRig(bool createIfNull = false)
        {
            if (_crowdRig != null)
                return _crowdRig;
            if (IsInstanced && GPUICrowdSkinningSystem.TryGetCrowdRenderSourceGroupWithRenderKey(_gpuiPrefab.renderKey, out var crowdRSG))
                _crowdRig = crowdRSG.rig;
            if (_crowdRig == null)
                _crowdRig = GPUICrowdSkinningSystem.GetCrowdInstanceRig(this, createIfNull);
            return _crowdRig;
        }

        public void SetRig(GPUICrowdRig rig)
        {
            if (Application.isPlaying && LoadRig() != null)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "GPUICrowdInstance already has a rig data assigned. Can not set rig data.");
                return;
            }
            _crowdRig = rig;
            GPUICrowdSkinningSystem.OnCrowdInstanceRigSet(this);
        }

        public void LoadBoneTransforms(bool forceNew = false, bool deoptimizeTransformHierarchy = false)
        {
            LoadRig(true);
            bool requireDeoptimize = deoptimizeTransformHierarchy && !HasTransformHierarchy;
            if (forceNew || requireDeoptimize || _boneTransforms == null || _boneTransforms.Count == 0)
            {
                if (requireDeoptimize)
                    AnimatorUtility.DeoptimizeTransformHierarchy(gameObject);
                _crowdRig.LoadBoneTransforms(this);
                OnInstancingStatusModified();
            }
        }

        [ContextMenu("Reload Bone Transforms")]
        public void ReloadBoneTransforms()
        {
            LoadBoneTransforms(true, false);
        }

        private bool HasAllBoneReferences()
        {
            if (_boneTransforms == null || _boneTransforms.Count != _crowdRig.GetBoneCount())
                return false;
            foreach (var boneTransformRef in _boneTransforms)
                if (boneTransformRef.transform == null)
                    return false;
            return true;
        }

        internal void SetAllBonesRead()
        {
            if (_boneTransforms == null)
                return;
            for (int i = 0; i < _boneTransforms.Count; i++)
            {
                var bRef = _boneTransforms[i];
                bRef.readWriteStatus = 1;
                _boneTransforms[i] = bRef;
            }
        }

        public void ClearAllBoneReadWrite()
        {
            if (_boneTransforms == null)
                return;
            for (int i = 0; i < _boneTransforms.Count; i++)
            {
                var bRef = _boneTransforms[i];
                bRef.readWriteStatus = 0;
                _boneTransforms[i] = bRef;
            }
        }

        /// <summary>
        /// Changes the bone read/write settings for this Crowd Instance.
        /// If called while instancing is active, you must also call GPUICrowdInstance.OnInstancingStatusModified() at the end for the changes to take effect.
        /// </summary>
        /// <param name="boneIndex">The bone index (shown as "idx" in the bone settings).</param>
        /// <param name="readWriteStatus">0 = No read or write, 1 = Read, 2 = Write</param>
        public void SetBoneReadWriteStatus(int boneIndex, int readWriteStatus)
        {
            for (int i = 0; i < _boneTransforms.Count; i++)
            {
                if (_boneTransforms[i].boneIndex == boneIndex)
                {
                    var bt = _boneTransforms[i];
                    bt.readWriteStatus = readWriteStatus;
                    _boneTransforms[i] = bt;
                    return;
                }
            }
        }

        #region Animator Methods
        /// <summary>
        /// Starts playing the specified animation clip on the given crowd instance.
        /// </summary>
        /// <param name="animationClip">The animation clip to be played.</param>
        /// <param name="normalizedClipTime">(Optional) Normalized start time of the clip, between 0f and 1f. Use a negative value to continue from where it left off (e.g., if it was already playing with a blend).</param>
        /// <param name="speed">(Optional) The animation speed.</param>
        /// <param name="transitionTime">(Optional) Transition time from the previous clip.</param>
        /// <param name="isLoopingOverride">(Optional) If null, the clip's looping setting is used. If set to true or false, it forces the animation to loop or not loop.</param>
        /// <param name="isSyncTime">(Optional) If true, synchronizes the normalized time between animation clips during transitions.</param>
        /// <returns>True if the animation starts successfully.</returns>
        public bool StartAnimation(AnimationClip animationClip, float normalizedClipTime = -1.0f, float speed = 1.0f, float transitionTime = 0, bool? isLoopingOverride = null, bool isSyncTime = false)
        {
            return GPUICrowdUtility.StartAnimation(this, animationClip, normalizedClipTime, speed, transitionTime, isLoopingOverride, isSyncTime);
        }

        /// <summary>
        /// Starts blending the specified animation clips with the given weights on the specified crowd instance.
        /// </summary>
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
        public bool StartBlend(Vector4 animationWeights, AnimationClip animationClip1, AnimationClip animationClip2, AnimationClip animationClip3 = null, AnimationClip animationClip4 = null, Vector4? normalizedClipTimes = null, Vector4? animationSpeeds = null, float transitionTime = 0, bool? isLoopingOverride = null, bool isSyncTime = true)
        {
            return GPUICrowdUtility.StartBlend(this, animationWeights, animationClip1, animationClip2, animationClip3, animationClip4, normalizedClipTimes, animationSpeeds, transitionTime, isLoopingOverride, isSyncTime);
        }

        /// <summary>
        /// Sets the animation speed for all active animations.
        /// </summary>
        /// <param name="animationSpeed">Speed value to apply.</param>
        public void SetAnimationSpeeds(float animationSpeed)
        {
            GPUICrowdUtility.SetAnimationSpeeds(this, new Vector4(animationSpeed, animationSpeed, animationSpeed, animationSpeed));
        }

        /// <summary>
        /// Sets the animation speed for all active animations.
        /// </summary>
        /// <param name="animationSpeeds">A Vector4 representing the speed values to apply.</param>
        public void SetAnimationSpeeds(Vector4 animationSpeeds)
        {
            GPUICrowdUtility.SetAnimationSpeeds(this, animationSpeeds);
        }

        /// <summary>
        /// Provides animator data for the currently playing clips.
        /// </summary>
        /// <param name="clipDataList">The given list will be filled with the current GPUICrowdAnimatorClipData entries.</param>
        /// <param name="clipWeights">The current clip weights.</param>
        /// <param name="clips">If not null, this will be filled with the currently playing AnimationClips.</param>
        /// <returns>False if the instance does not have an active animator.</returns>
        public bool TryGetCrowdAnimatorClipData(List<GPUICrowdAnimatorClipData> clipDataList, out Vector4 clipWeights, List<AnimationClip> clips = null)
        {
            clipWeights = Vector4.zero;

            if (!IsInstanced)
                return false;

            if (_crowdRenderSource == null && !GPUICrowdSkinningSystem.TryGetCrowdRenderSource(_gpuiPrefab.renderKey, out _crowdRenderSource))
                return false;

            return _crowdRenderSource.TryGetCrowdAnimatorClipData(_gpuiPrefab.bufferIndex, clipDataList, out clipWeights, clips);
        }

        /// <summary>
        /// Provides animator data for a currently playing clip.
        /// </summary>
        /// <param name="clipIndex">The index of the active clip, ranged from 0 to 3.</param>
        /// <param name="clipData">The current GPUICrowdAnimatorClipData at the given index.</param>
        /// <param name="clipWeight">The weight of the clip at the given index.</param>
        /// <returns>False if the instance does not have an active animator.</returns>
        public bool TryGetCrowdAnimatorClipData(int clipIndex, out GPUICrowdAnimatorClipData clipData, out float clipWeight)
        {
            clipData = default;
            clipWeight = 0f;

            if (!IsInstanced)
                return false;

            if (_crowdRenderSource == null && !GPUICrowdSkinningSystem.TryGetCrowdRenderSource(_gpuiPrefab.renderKey, out _crowdRenderSource))
                return false;

            return _crowdRenderSource.TryGetCrowdAnimatorClipData(clipIndex, _gpuiPrefab.bufferIndex, out clipData, out clipWeight);
        }

        /// <summary>
        /// Provides animator data for a currently playing clip.
        /// </summary>
        /// <param name="clipIndex">The index of the active clip, ranging from 0 to 3.</param>
        /// <param name="clipData">The current GPUICrowdAnimatorClipData at the given index.</param>
        /// <param name="clipWeight">The weight of the clip at the given index.</param>
        /// <param name="animationClip">The AnimationClip playing at the given index.</param>
        /// <returns>False if the instance does not have an active animator.</returns>
        public bool TryGetCrowdAnimatorClipData(int clipIndex, out GPUICrowdAnimatorClipData clipData, out float clipWeight, out AnimationClip animationClip)
        {
            clipData = default;
            clipWeight = 0f;
            animationClip = null;

            if (!IsInstanced)
                return false;

            if (_crowdRenderSource == null && !GPUICrowdSkinningSystem.TryGetCrowdRenderSource(_gpuiPrefab.renderKey, out _crowdRenderSource))
                return false;

            if (!_crowdRenderSource.TryGetCrowdAnimatorClipData(clipIndex, _gpuiPrefab.bufferIndex, out clipData, out clipWeight))
                return false;
            animationClip = _crowdRenderSource.GetAnimationClipFromClipData(clipData);
            return true;
        }

        private static readonly List<GPUICrowdAnimatorClipData> _tempClipDataList = new(); // will hold currently playing clip data
        private static readonly List<AnimationClip> _tempClips = new(); // will hold currently playing AnimationClips

        /// <summary>
        /// Provides animator data by searching active clips using the given AnimationClip.
        /// </summary>
        /// <param name="animationClip">The AnimationClip to search for among the active clips.</param>
        /// <param name="clipData">The GPUICrowdAnimatorClipData for the matching clip.</param>
        /// <param name="weight">The current weight of the matching clip.</param>
        /// <returns>True if the crowd instance is currently playing the specified AnimationClip.</returns>
        public bool TryGetCrowdAnimatorClipData(AnimationClip animationClip, out GPUICrowdAnimatorClipData clipData, out float weight)
        {
            clipData = default;
            weight = 0f;
            if (TryGetCrowdAnimatorClipData(_tempClipDataList, out Vector4 clipWeights, _tempClips))
            {
                for (int i = 0; i < _tempClips.Count; i++)
                {
                    if (_tempClips[i] == animationClip)
                    {
                        clipData = _tempClipDataList[i];
                        weight = clipWeights[i];
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Provides animator data by searching active clips by name.
        /// </summary>
        /// <param name="clipName">The clip name to search for among active clips.</param>
        /// <param name="clipData">The GPUICrowdAnimatorClipData for the matching clip.</param>
        /// <param name="weight">The current weight of the matching clip.</param>
        /// <param name="animationClip">The matching AnimationClip.</param>
        /// <returns>True if the given crowd instance is playing an animation clip with the specified name.</returns>
        public bool TryGetCrowdAnimatorClipData(string clipName, out GPUICrowdAnimatorClipData clipData, out float weight, out AnimationClip animationClip)
        {
            clipData = default;
            weight = 0f;
            animationClip = null;
            if (TryGetCrowdAnimatorClipData(_tempClipDataList, out Vector4 clipWeights, _tempClips))
            {
                for (int i = 0; i < _tempClips.Count; i++)
                {
                    if (_tempClips[i].name == clipName)
                    {
                        clipData = _tempClipDataList[i];
                        weight = clipWeights[i];
                        animationClip = _tempClips[i];
                        return true;
                    }
                }
            }
            return false;
        }

        public void RequireMecanimReaderUpdate()
        {
            _lastMecanimStateCheck = -1;
            _currentMecanimStateHash = 0;
        }

        public void ReadLegacyAnimationStates()
        {
            _legacyStates = new AnimationState[_legacyAnimation.GetClipCount()];
            int stateCount = 0;
            foreach (AnimationState state in _legacyAnimation)
            {
                _legacyStates[stateCount] = state;
                stateCount++;
            }
        }

        public void RequireLegacyAnimationReaderUpdate()
        {
            _lastLegacyStateCheck = -1;
        }

        public bool IsCrowdAnimatorInTransition()
        {
            if (!_hasPrefabComponent || _gpuiPrefab.bufferIndex < 0 || _crowdRenderSource == null)
                return false;
            return _crowdRenderSource.crowdRSG.IsInTransition(_gpuiPrefab.bufferIndex + _crowdRenderSource.renderSource.bufferStartIndex);
        }
        #endregion Animator Methods

        public GPUICrowdRenderSource GetCrowdRenderSource() => _crowdRenderSource;

        [Serializable]
        public struct BoneTransformReference
        {
            public int boneIndex;
            public Transform transform;
            /// <summary>
            /// 0 => none, 1 => read, 2 => write
            /// </summary>
            public int readWriteStatus;
        }
    }
}