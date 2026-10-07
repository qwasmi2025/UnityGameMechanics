// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Profiling;

namespace GPUInstancerPro.CrowdAnimations
{
    public unsafe class GPUICrowdRenderSourceGroup : IGPUIDisposable
    {
        /// <summary>
        /// GPUIRenderSourceGroup
        /// </summary>
        public GPUIRenderSourceGroup renderSourceGroup;
        /// <summary>
        /// Current Render Source Group buffer size.
        /// </summary>
        private int _currentBufferSize;
        /// <summary>
        /// Prefab bounds. Used for Animator culling.
        /// </summary>
        public Bounds bounds;
        /// <summary>
        /// The default Animator Workflow ID that will be used for animations.
        /// </summary>
        public int defaultAnimatorWorkflowID;

        /// <summary>
        /// Rig definition.
        /// </summary>
        public GPUICrowdRig rig;
        /// <summary>
        /// GPUITransformData for each bone of each instance. (Object Space, relative to instance transform)
        /// </summary>
        public GraphicsBuffer boneDataBuffer;
        /// <summary>
        /// Contains animator workflow ID for each instance.
        /// </summary>
        public GPUIDataBuffer<GPUICrowdInstanceData> crowdInstanceDataBuffer;
        /// <summary>
        /// Bone data buffer used in vertex/fragment shader. Contains bind pose values for the SkinnedMeshRenderers and current bone matrices for each instance.
        /// </summary>
        public GPUIShaderBuffer shaderBoneBuffer;
        internal int _shaderBoneBufferAnimStartIndex;
        /// <summary>
        /// Matrix4x4 buffer for bind poses.
        /// </summary>
        public GraphicsBuffer bindPoseBuffer;
        /// <summary>
        /// GPUITransformData buffer for inverse bind pose values.
        /// </summary>
        public GraphicsBuffer inverseBindPoseBuffer;

        #region Animator Data
        /// <summary>
        /// Contains active clip data for each instance.
        /// </summary>
        private NativeArray<GPUICrowdAnimatorClipData> _animatorClipDataArray;
        private void* p_animatorClipDataArray;
        /// <summary>
        /// Contains current frame and weight value for each clip for each instance.
        /// </summary>
        private GraphicsBuffer _clipFramesAndWeightsBuffer;
        private bool _requireSetFramesAndWeightsBuffer;
        /// <summary>
        /// Native array for clipFramesAndWeights.
        /// </summary>
        private NativeArray<Vector4> _clipFramesAndWeightsArray;
        private void* p_clipFramesAndWeightsArray;
        /// <summary>
        /// GPU buffer for animatorClipDataArray. Required when using Compute Animator.
        /// </summary>
        private GraphicsBuffer _animatorClipDataBuffer;
        private bool _requireSetClipDataBuffer;
        #endregion Animator Data

        private NativeArray<GPUICrowdTransition> _transitionsArray;
        private void* p_transitionsArray;
        public int TransitionCount { get; internal set; }
        /// <summary>
        /// Key=> bufferIndex, Value=> transitionsArray index
        /// </summary>
        private Dictionary<int, int> _transitionIndexDict;

        public bool IsInitialized => crowdInstanceDataBuffer != null;
        private float _lastExecutionTime;

        /// <summary>
        /// index => Baked Clip Index, Value => List of GPUICrowdAnimationEvent
        /// </summary>
        private List<GPUICrowdAnimationEvent>[] _eventLookup;
        private int _eventCount;

        public List<int> indexesToBindPoseReset;

        public GPUICrowdRenderSourceGroup(GPUIRenderSourceGroup renderSourceGroup, GPUICrowdRig rig)
        {
            this.renderSourceGroup = renderSourceGroup;
            this.rig = rig;

            _transitionIndexDict = new();
            indexesToBindPoseReset = new();

            ApplySkinWeightsKeywords();

            _lastExecutionTime = Time.time;
        }

        internal void ApplySkinWeightsKeywords()
        {
            switch (rig.skinWeights)
            {
                case GPUICrowdSkinWeights.OneBone:
                    renderSourceGroup.AddShaderKeyword(GPUICrowdConstants.Kw_GPUI_CROWD_SKIN_WEIGHTS_1);
                    renderSourceGroup.RemoveShaderKeyword(GPUICrowdConstants.Kw_GPUI_CROWD_SKIN_WEIGHTS_2);
                    break;
                case GPUICrowdSkinWeights.TwoBones:
                    renderSourceGroup.RemoveShaderKeyword(GPUICrowdConstants.Kw_GPUI_CROWD_SKIN_WEIGHTS_1);
                    renderSourceGroup.AddShaderKeyword(GPUICrowdConstants.Kw_GPUI_CROWD_SKIN_WEIGHTS_2);
                    break;
                default:
                    renderSourceGroup.RemoveShaderKeyword(GPUICrowdConstants.Kw_GPUI_CROWD_SKIN_WEIGHTS_1);
                    renderSourceGroup.RemoveShaderKeyword(GPUICrowdConstants.Kw_GPUI_CROWD_SKIN_WEIGHTS_2);
                    break;
            }
            renderSourceGroup.RemoveReplacementMaterials();
        }

        internal void ApplyRenderSourceGroupBufferSizeChanges()
        {
            if (_currentBufferSize == renderSourceGroup.BufferSize)
                return;
#if GPUIPRO_DEVMODE
            Debug.Log(GPUIConstants.LOG_PREFIX + GPUIConstants.LOG_PREFIX_DEV + "Changing crowd RSG data buffer size from: " + _currentBufferSize + " to: " + renderSourceGroup.BufferSize + " for key: " + renderSourceGroup.Key);
#endif
            _currentBufferSize = renderSourceGroup.BufferSize;

            if (_currentBufferSize == 0)
            {
                ReleaseBuffers();
                return;
            }

            int boneCount = rig.bones.Count;
            if (boneCount == 0)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Rig has no bone definitions: " + rig, rig);
                return;
            }

            int previousBufferSize = crowdInstanceDataBuffer.Length;
            int newBoneDataSize = _currentBufferSize * boneCount;

            if (boneDataBuffer == null || newBoneDataSize != boneDataBuffer.count)
            {
                var previousBoneDataBuffer = boneDataBuffer;
                boneDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newBoneDataSize, System.Runtime.InteropServices.Marshal.SizeOf<GPUITransformData>());
                if (previousBoneDataBuffer != null)
                {
                    boneDataBuffer.SetData(previousBoneDataBuffer, 0, 0, Math.Min(boneDataBuffer.count, previousBoneDataBuffer.count));
                    previousBoneDataBuffer.Dispose();
                }

                if (_currentBufferSize > previousBufferSize)
                    GPUICrowdUtility.SetDefaultBoneDataFromBindPose(this, previousBufferSize);
            }

            if (shaderBoneBuffer != null)
                shaderBoneBuffer.Dispose();

            int bindPoseCount = rig.bindPoseDataList.Count;
            int shaderBoneBufferSize = (newBoneDataSize * bindPoseCount) * 3;
            _shaderBoneBufferAnimStartIndex = rig.GetVertexBoneDataSize();
            shaderBoneBufferSize += _shaderBoneBufferAnimStartIndex;

            shaderBoneBuffer = new GPUIShaderBuffer(shaderBoneBufferSize, 16);

            int shaderBoneBufferIndex = 0;
            var lodGroupData = renderSourceGroup.LODGroupData;
            foreach (var smd in rig.skinnedMeshes)
            {
                int count = smd._vertexBoneData.Count;
                shaderBoneBuffer.Buffer.SetData(smd._vertexBoneData, 0, shaderBoneBufferIndex, count);

                for (int l = 0; l < lodGroupData.Length; l++)
                {
                    var lod = lodGroupData[l];
                    for (int r = 0; r < lod.Length; r++)
                    {
                        var renderer = lod[r];
                        if (renderer.isSkinnedMesh && renderer.rendererMesh == smd.skinnedMesh)
                            renderSourceGroup.SetCommandShaderOptionalParams(l, r, new Vector2(shaderBoneBufferIndex, smd.bindPoseIndex));
                        //renderSourceGroup.AddMaterialPropertyOverride(GPUICrowdConstants.PROP_gpuiBoneDataIndex, shaderBoneBufferIndex, l, r, true);
                    }
                }
                shaderBoneBufferIndex += count;
            }

            if (GPUIRuntimeSettings.Instance.DisableShaderBuffers)
            {
                renderSourceGroup.AddMaterialPropertyOverride(GPUICrowdConstants.PROP_shaderBoneBuffer, shaderBoneBuffer.Texture, -1, -1, true);
                renderSourceGroup.AddMaterialPropertyOverride(GPUICrowdConstants.PROP_gpuiSkinningValues, new Vector4(boneCount, newBoneDataSize, _shaderBoneBufferAnimStartIndex, shaderBoneBuffer.Texture.width), -1, -1, true);
            }
            else
            {
                renderSourceGroup.AddMaterialPropertyOverride(GPUICrowdConstants.PROP_shaderBoneBuffer, shaderBoneBuffer.Buffer, -1, -1, true);
                renderSourceGroup.AddMaterialPropertyOverride(GPUICrowdConstants.PROP_gpuiSkinningValues, new Vector4(boneCount, newBoneDataSize, _shaderBoneBufferAnimStartIndex, 0), -1, -1, true);
            }

            crowdInstanceDataBuffer.Resize(_currentBufferSize);
            if (previousBufferSize < _currentBufferSize && defaultAnimatorWorkflowID != 0)
                crowdInstanceDataBuffer.SetData(previousBufferSize, _currentBufferSize - previousBufferSize, new GPUICrowdInstanceData() { animatorWorkflowID = defaultAnimatorWorkflowID });

            if (_animatorClipDataArray.IsCreated)
                CreateAnimatorClipDataArray();
            if (_clipFramesAndWeightsArray.IsCreated)
                CreateClipFramesAndWeightsArray();
        }

        internal void ApplyRenderSourceBufferSizeChanges(GPUICrowdRenderSource crowdRenderSource)
        {
            if (crowdRenderSource._currentBufferSize == crowdRenderSource.renderSource.bufferSize)
                return;
            int previousBufferSize = crowdRenderSource._currentBufferSize;
            crowdRenderSource._currentBufferSize = crowdRenderSource.renderSource.bufferSize;
            int sizeToCopy = crowdRenderSource.renderSource.renderSourceGroup.BufferSize - crowdRenderSource.renderSource.bufferSize - crowdRenderSource.renderSource.bufferStartIndex;
            if (previousBufferSize == 0 || sizeToCopy <= 0)
                return;
            if (_animatorClipDataArray.IsCreated)
            {
                NativeArray<GPUICrowdAnimatorClipData>.Copy(_animatorClipDataArray, previousBufferSize * GPUICrowdConstants.ANIMATOR_MAX_CLIPS, _animatorClipDataArray, crowdRenderSource._currentBufferSize * GPUICrowdConstants.ANIMATOR_MAX_CLIPS, sizeToCopy * GPUICrowdConstants.ANIMATOR_MAX_CLIPS);
                _requireSetClipDataBuffer = true;
            }
            if (_clipFramesAndWeightsArray.IsCreated)
            {
                NativeArray<Vector4>.Copy(_clipFramesAndWeightsArray, previousBufferSize * 2, _clipFramesAndWeightsArray, crowdRenderSource._currentBufferSize * 2, sizeToCopy * 2);
                _requireSetFramesAndWeightsBuffer = true;
            }
        }

        public void ReleaseBuffers()
        {
            boneDataBuffer?.Dispose();
            boneDataBuffer = null;
            if (shaderBoneBuffer != null)
            {
                shaderBoneBuffer.Dispose();
                shaderBoneBuffer = null;
            }
            if (bindPoseBuffer != null)
            {
                bindPoseBuffer.Dispose();
                bindPoseBuffer = null;
            }
            if (inverseBindPoseBuffer != null)
            {
                inverseBindPoseBuffer.Dispose();
                inverseBindPoseBuffer = null;
            }
            if (_animatorClipDataArray.IsCreated)
                _animatorClipDataArray.Dispose();
            if (_clipFramesAndWeightsArray.IsCreated)
                _clipFramesAndWeightsArray.Dispose();
            if (_clipFramesAndWeightsBuffer != null)
            {
                _clipFramesAndWeightsBuffer.Dispose();
                _clipFramesAndWeightsBuffer = null;
            }
            if (_animatorClipDataBuffer != null)
            {
                _animatorClipDataBuffer.Dispose();
                _animatorClipDataBuffer = null;
            }
            if (_transitionsArray.IsCreated)
                _transitionsArray.Dispose();
            _transitionIndexDict.Clear();
            indexesToBindPoseReset.Clear();
            _eventLookup = null;
            _eventCount = 0;
            if (crowdInstanceDataBuffer != null)
                crowdInstanceDataBuffer.Dispose();
        }

        public void Dispose()
        {
            crowdInstanceDataBuffer?.Dispose();
            crowdInstanceDataBuffer = null;
            ReleaseBuffers();
        }

        public void ExecuteCrowdAnimator()
        {
            crowdInstanceDataBuffer.UpdateBufferData();
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            if (indexesToBindPoseReset.Count > 0)
            {
#if GPUIPRO_DEVMODE
                Debug.Log(GPUIConstants.LOG_PREFIX + GPUIConstants.LOG_PREFIX_DEV + "Resetting " + indexesToBindPoseReset.Count + " instances to bind pose.");
#endif
                GPUICrowdUtility.SetDefaultBoneDataFromBindPose(this, indexesToBindPoseReset);
                indexesToBindPoseReset.Clear();
            }

            ExecuteTransitions();
            ExecuteAnimationEvents();
            ExecuteAnimatorController();
            AnimateFromBakedClips();

            _lastExecutionTime = Time.time;
        }

        private void ExecuteAnimatorController()
        {
            if (_currentBufferSize == 0)
                return;
            if (GetClipFramesAndWeightsBuffer() == null || GetAnimatorClipDataBuffer() == null || crowdInstanceDataBuffer.Buffer == null)
                return;

            ComputeShader cs = GPUICrowdConstants.CS_AnimatorController;

            //int kernelIndex = 1; // Fix weights
            //cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_clipFramesAndWeightsBuffer, _clipFramesAndWeightsBuffer);
            //cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_crowdAnimatorClipBuffer, _animatorClipDataBuffer);
            //cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_crowdInstanceBuffer, instanceData.Buffer);
            //cs.SetInt(GPUIConstants.PROP_instanceCount, _currentBufferSize);
            //cs.SetInt(GPUIConstants.PROP_startIndex, 0);
            //cs.DispatchX(kernelIndex, _currentBufferSize);

            int kernelIndex = 0; // Execute animator
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_clipFramesAndWeightsBuffer, _clipFramesAndWeightsBuffer);
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_crowdAnimatorClipBuffer, _animatorClipDataBuffer);
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_crowdInstanceBuffer, crowdInstanceDataBuffer.Buffer);
            cs.SetInt(GPUIConstants.PROP_instanceCount, _currentBufferSize);
            cs.SetInt(GPUIConstants.PROP_startIndex, 0);
            cs.SetFloat(GPUIConstants.PROP_currentTime, Time.time);
            cs.DispatchX(kernelIndex, _currentBufferSize);
        }

        private void AnimateFromBakedClips()
        {
            if (boneDataBuffer == null || _clipFramesAndWeightsBuffer == null || crowdInstanceDataBuffer.Buffer == null)
                return;
            GraphicsBuffer bakedBoneDataBuffer = rig.GetBakedBoneDataBuffer();
            if (bakedBoneDataBuffer == null)
                return;

            ComputeShader cs = GPUICrowdConstants.CS_AnimateFromBakedClips;
            int kernelIndex = 0;
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_boneDataBuffer, boneDataBuffer);
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_clipFramesAndWeightsBuffer, _clipFramesAndWeightsBuffer);
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_crowdInstanceBuffer, crowdInstanceDataBuffer.Buffer);
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_bakedAnimationClipData, bakedBoneDataBuffer);
            cs.SetInt(GPUICrowdConstants.PROP_boneCount, rig.bones.Count);
            cs.SetInt(GPUIConstants.PROP_instanceCount, _currentBufferSize);
            cs.SetInt(GPUIConstants.PROP_startIndex, 0);
            cs.DispatchXY(kernelIndex, _currentBufferSize, rig.bones.Count);
        }

        private unsafe void CreateAnimatorClipDataArray()
        {
            _animatorClipDataArray.ResizeNativeArray(_currentBufferSize * GPUICrowdConstants.ANIMATOR_MAX_CLIPS, Allocator.Persistent);
            p_animatorClipDataArray = NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(_animatorClipDataArray);
            _requireSetClipDataBuffer = true;
        }

        private void CreateClipFramesAndWeightsArray()
        {
            _clipFramesAndWeightsArray.ResizeNativeArray(_currentBufferSize * 2, Allocator.Persistent);
            p_clipFramesAndWeightsArray = NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(_clipFramesAndWeightsArray);
            _requireSetFramesAndWeightsBuffer = true;
        }

        public NativeArray<GPUICrowdAnimatorClipData> GetAnimatorClipDataArray(bool isReadonly = false)
        {
            if (!_animatorClipDataArray.IsCreated)
                CreateAnimatorClipDataArray();
            if (!isReadonly)
                _requireSetClipDataBuffer = true;
            return _animatorClipDataArray;
        }

        internal unsafe void* GetUnsafeAnimatorClipDataArrayPtr(bool isReadonly = false)
        {
            if (!_animatorClipDataArray.IsCreated)
                CreateAnimatorClipDataArray();
            if (!isReadonly)
                _requireSetClipDataBuffer = true;
            return p_animatorClipDataArray;
        }

        public GraphicsBuffer GetAnimatorClipDataBuffer()
        {
            if (!IsInitialized || !_clipFramesAndWeightsArray.IsCreated)
                return null;
            int count = GPUICrowdConstants.ANIMATOR_MAX_CLIPS * _currentBufferSize;
            if (_animatorClipDataBuffer == null || _animatorClipDataBuffer.count != count)
            {
                if (_animatorClipDataBuffer != null)
                    _animatorClipDataBuffer.Dispose();
                _animatorClipDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, System.Runtime.InteropServices.Marshal.SizeOf<GPUICrowdAnimatorClipData>());
                _requireSetClipDataBuffer = true;
            }
            if (_requireSetClipDataBuffer)
            {
                _animatorClipDataBuffer.SetData(_animatorClipDataArray);
                _requireSetClipDataBuffer = false;
            }
            return _animatorClipDataBuffer;
        }

        public NativeArray<Vector4> GetClipFramesAndWeightsArray(bool isReadonly = false)
        {
            if (!_clipFramesAndWeightsArray.IsCreated)
                CreateClipFramesAndWeightsArray();
            if (!isReadonly)
                _requireSetFramesAndWeightsBuffer = true;
            return _clipFramesAndWeightsArray;
        }

        internal unsafe void* GetUnsafeClipFramesAndWeightsArrayPtr(bool isReadonly = false)
        {
            if (!_clipFramesAndWeightsArray.IsCreated)
                CreateClipFramesAndWeightsArray();
            if (!isReadonly)
                _requireSetFramesAndWeightsBuffer = true;
            return p_clipFramesAndWeightsArray;
        }

        public GraphicsBuffer GetClipFramesAndWeightsBuffer()
        {
            if (!IsInitialized || !_clipFramesAndWeightsArray.IsCreated)
                return null;
            int count = _currentBufferSize * 2;
            if (_clipFramesAndWeightsBuffer == null || _clipFramesAndWeightsBuffer.count != count)
            {
                if (_clipFramesAndWeightsBuffer != null)
                    _clipFramesAndWeightsBuffer.Dispose();
                _clipFramesAndWeightsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4 * GPUICrowdConstants.ANIMATOR_MAX_CLIPS);
                _requireSetFramesAndWeightsBuffer = true;
            }
            if (_requireSetFramesAndWeightsBuffer)
            {
                _clipFramesAndWeightsBuffer.SetData(_clipFramesAndWeightsArray);
                _requireSetFramesAndWeightsBuffer = false;
            }
            return _clipFramesAndWeightsBuffer;
        }

        public unsafe void OnBufferIndexModified(int previousBufferIndex, int newBufferIndex) // previous index or new index can be -1
        {
            if (!_animatorClipDataArray.IsCreated || !_clipFramesAndWeightsArray.IsCreated)
                return;

            if (previousBufferIndex < 0 && newBufferIndex >= 0 && newBufferIndex < _currentBufferSize)
            {
                GPUICrowdUtility.ClearAnimatorClipData(this, newBufferIndex);
                return;
            }

            if (previousBufferIndex < 0 || newBufferIndex < 0 || previousBufferIndex >= _currentBufferSize || newBufferIndex >= _currentBufferSize)
                return;
            
            NativeArray<GPUICrowdAnimatorClipData>.Copy(_animatorClipDataArray, previousBufferIndex * GPUICrowdConstants.ANIMATOR_MAX_CLIPS, _animatorClipDataArray, newBufferIndex * GPUICrowdConstants.ANIMATOR_MAX_CLIPS, GPUICrowdConstants.ANIMATOR_MAX_CLIPS);
            _requireSetClipDataBuffer = true;

            NativeArray<Vector4>.Copy(_clipFramesAndWeightsArray, previousBufferIndex * 2, _clipFramesAndWeightsArray, newBufferIndex * 2, 2);
            _requireSetFramesAndWeightsBuffer = true;

            int transitionIndex = GetTransitionIndex(previousBufferIndex);
            if (transitionIndex >= 0)
            {
                _transitionIndexDict.Remove(previousBufferIndex);
                _transitionIndexDict[newBufferIndex] = transitionIndex;

                var transition = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdTransition>(p_transitionsArray, transitionIndex, GPUICrowdTransition.STRIDE);
                transition.bufferIndex = newBufferIndex;
                UnsafeUtility.WriteArrayElementWithStride(p_transitionsArray, transitionIndex, GPUICrowdTransition.STRIDE, transition);
            }
        }
        
        #region Transition Methods
        public void ExecuteTransitions()
        {
            if (TransitionCount <= 0 || !_transitionsArray.IsCreated || !_clipFramesAndWeightsArray.IsCreated)
                return;
            Profiler.BeginSample("GPUICrowdRenderSourceGroup.ExecuteTransitions");
            GPUIApplyTransitionsJob applyTransitionsJob = new GPUIApplyTransitionsJob()
            {
                p_animatorClipDataArray = p_animatorClipDataArray,
                p_clipFramesAndWeightsArray = p_clipFramesAndWeightsArray,
                p_bakedClipDataArray = rig.GetBakedClipDataArray().GetUnsafeReadOnlyPtr(),
                p_transitionsArray = p_transitionsArray,
                currentTime = Time.time,
                clipFramesAndWeightsArraySize = _clipFramesAndWeightsArray.Length
            };
            applyTransitionsJob.Schedule(TransitionCount, 32).Complete();
            _requireSetFramesAndWeightsBuffer = true;
            _requireSetClipDataBuffer = true;

            RemoveCompletedTransitions();
            Profiler.EndSample();
        }

        public bool IsInTransition(int rsgBufferIndex) => GetTransitionIndex(rsgBufferIndex) >= 0;

        public unsafe void AddTransition(int rsgBufferIndex, float startTime, float transitionLength, Vector4 startWeights, Vector4 targetWeights)
        {
            GPUICrowdTransition transition = new GPUICrowdTransition()
            {
                bufferIndex = rsgBufferIndex,
                startTime = startTime,
                transitionLength = transitionLength,
                startWeights = startWeights,
                targetWeights = targetWeights
            };

            int transitionCount = TransitionCount;
            int transitionIndex = transitionCount;
            transitionCount++;
            if (!_transitionsArray.IsCreated || _transitionsArray.Length < transitionCount)
            {
                _transitionsArray.ResizeNativeArray(transitionCount + 20, Allocator.Persistent);
                p_transitionsArray = NativeArrayUnsafeUtility.GetUnsafePtr(_transitionsArray);
            }

            UnsafeUtility.WriteArrayElementWithStride(p_transitionsArray, transitionIndex, GPUICrowdTransition.STRIDE, transition);
            TransitionCount = transitionCount;
            _transitionIndexDict[rsgBufferIndex] = transitionIndex;
        }

        public unsafe int GetTransitionIndex(int rsgBufferIndex)
        {
            int transitionCount = TransitionCount;
            if (transitionCount == 0 || !_transitionIndexDict.TryGetValue(rsgBufferIndex, out int transitionIndex))
                return -1;
            return transitionIndex;
        }

        public unsafe void CompleteTransition(int rsgBufferIndex)
        {
            int transitionCount = TransitionCount;
            if (transitionCount == 0 || !_transitionIndexDict.TryGetValue(rsgBufferIndex, out int transitionIndex))
                return;

            var transition = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdTransition>(p_transitionsArray, transitionIndex, GPUICrowdTransition.STRIDE);
            UnsafeUtility.WriteArrayElementWithStride(p_clipFramesAndWeightsArray, rsgBufferIndex * 2 + 1, 16, transition.targetWeights); // Complete the transition
            transitionCount--;
            if (transitionIndex != transitionCount)
            {
                GPUICrowdTransition lastElement = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdTransition>(p_transitionsArray, transitionCount, GPUICrowdTransition.STRIDE);
                UnsafeUtility.WriteArrayElementWithStride(p_transitionsArray, transitionIndex, GPUICrowdTransition.STRIDE, lastElement); // Swap back

                _transitionIndexDict[lastElement.bufferIndex] = transitionIndex;
            }
            TransitionCount = transitionCount;
            _transitionIndexDict.Remove(rsgBufferIndex);
        }

        public unsafe void RemoveCompletedTransitions()
        {
            int transitionCount = TransitionCount;
            if (transitionCount == 0)
                return;
            float currentTime = Time.time;
            for (int i = transitionCount - 1; i >= 0; i--)
            {
                var transition = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdTransition>(p_transitionsArray, i, GPUICrowdTransition.STRIDE);
                if (transition.startTime + transition.transitionLength <= currentTime)
                {
                    transitionCount--;
                    if (i != transitionCount)
                    {
                        GPUICrowdTransition lastElement = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdTransition>(p_transitionsArray, transitionCount, GPUICrowdTransition.STRIDE);
                        UnsafeUtility.WriteArrayElementWithStride(p_transitionsArray, i, GPUICrowdTransition.STRIDE, lastElement); // Swap back

                        _transitionIndexDict[lastElement.bufferIndex] = i;
                    }
                    _transitionIndexDict.Remove(transition.bufferIndex);
                }
            }
            TransitionCount = transitionCount;
        }
        #endregion Transition Methods

        #region Event Methods
        public unsafe void ExecuteAnimationEvents()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            if (_eventCount == 0 || !_clipFramesAndWeightsArray.IsCreated)
                return;

            Profiler.BeginSample("GPUICrowdRenderSourceGroup.ExecuteAnimationEvents");
            float currentTime = Time.time;
            float deltaTime = currentTime - _lastExecutionTime;
            int instanceCount = renderSourceGroup.InstanceCount;
            int rsgKey = renderSourceGroup.Key;
            void* p_crowdInstanceDataBuffer = crowdInstanceDataBuffer.GetUnsafeNativeArrayPtr();
            int eventLookupSize = _eventLookup.Length;

            for (int i = 0; i < instanceCount; i++)
            {
                GPUICrowdInstanceData crowdInstanceData = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdInstanceData>(p_crowdInstanceDataBuffer, i, GPUICrowdInstanceData.STRIDE);
                if (crowdInstanceData.animatorWorkflowID != GPUIAWComputeAnimator.WORKFLOW_ID)
                    continue;

                Vector4 weights = UnsafeUtility.ReadArrayElementWithStride<Vector4>(p_clipFramesAndWeightsArray, i * 2 + 1, 16);
                for (int c = 0; c < GPUICrowdConstants.ANIMATOR_MAX_CLIPS; c++)
                {
                    if (weights[c] <= 0f)
                        break;

                    GPUICrowdAnimatorClipData clipData = UnsafeUtility.ReadArrayElementWithStride<GPUICrowdAnimatorClipData>(p_animatorClipDataArray, i * 4 + c, GPUICrowdAnimatorClipData.STRIDE);
                    int bakedClipIndex = clipData.BakedClipIndex;
                    if (bakedClipIndex >= eventLookupSize)
                        continue;
                    var eventList = _eventLookup[bakedClipIndex];
                    if (eventList == null)
                        continue;
                    for (int e = 0; e < eventList.Count; e++)
                    {
                        var animationEvent = eventList[e];

                        if (!animationEvent.IsInvokeEvent(clipData, currentTime, deltaTime))
                            continue;
                        GPUICrowdInstance crowdInstance = null;
                        var crowdInstanceTransform = GPUIRenderingSystem.Instance.GetInstanceTransformFromRSG(rsgKey, i);
                        if (crowdInstanceTransform != null)
                            crowdInstance = crowdInstanceTransform.GetComponent<GPUICrowdInstance>();
                        animationEvent.InvokeEvent(crowdInstance);
                    }
                }
            }

            Profiler.EndSample();
        }

        /// <returns>Number of added events.</returns>
        public int AddAnimationEvents(AnimationClip animationClip, IEnumerable<GPUICrowdAnimationEvent> events)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return 0;
#endif
            int bakedClipIndex = rig.GetOrCreateBakedClipIndex(renderSourceGroup, animationClip);
            if (_eventLookup == null)
                _eventLookup = new List<GPUICrowdAnimationEvent>[bakedClipIndex + 1];
            else if (bakedClipIndex >= _eventLookup.Length)
                Array.Resize(ref _eventLookup, bakedClipIndex + 1);

            List<GPUICrowdAnimationEvent> eventList = _eventLookup[bakedClipIndex];
            if (eventList == null)
            {
                eventList = new();
                _eventLookup[bakedClipIndex] = eventList;
            }

            int addedCount = 0;
            foreach (var item in events)
            {
                if (!eventList.Contains(item))
                {
                    eventList.Add(item);
                    addedCount++;
                }
            }
            _eventCount += addedCount;
#if GPUIPRO_DEVMODE
            if (addedCount > 0)
                Debug.Log(GPUIConstants.LOG_PREFIX + GPUIConstants.LOG_PREFIX_DEV + "Added " + addedCount + " animation events for bakedClipIndex: " + bakedClipIndex + " rsgKey: " + renderSourceGroup.Key);
#endif
            return addedCount;
        }

        /// <returns>Number of removed events.</returns>
        public int RemoveAnimationEvents(AnimationClip animationClip, IEnumerable<GPUICrowdAnimationEvent> events)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return 0;
#endif
            if (_eventCount == 0)
                return 0;
            if (!rig.TryGetBakedClipDataIndex(animationClip, out int bakedClipIndex))
                return 0;
            if (_eventLookup.Length <= bakedClipIndex || _eventLookup[bakedClipIndex] == null)
                return 0;
            int removedCount = 0;
            var eventList = _eventLookup[bakedClipIndex];
            foreach (var item in events)
            {
                int indexToRemove = eventList.IndexOf(item);
                if (indexToRemove >= 0)
                {
                    eventList.RemoveAt(indexToRemove);
                    removedCount++;
                }
            }
            _eventCount -= removedCount;
#if GPUIPRO_DEVMODE
            if (removedCount > 0)
                Debug.Log(GPUIConstants.LOG_PREFIX + GPUIConstants.LOG_PREFIX_DEV + "Removed " + removedCount + " animation events for bakedClipIndex: " + bakedClipIndex + " rsgKey: " + renderSourceGroup.Key);
#endif
            return removedCount;
        }
        #endregion Event Methods
    }

    public class GPUICrowdRenderSource : IDisposable
    {
        public GPUIRenderSource renderSource;
        /// <summary>
        /// Current Render Source Group buffer size.
        /// </summary>
        internal int _currentBufferSize;
        public GPUICrowdRenderSourceGroup crowdRSG;
        internal List<GPUICrowdInstance> _crowdInstancesToProcess;
        public int boneCount;
        internal List<GPUICrowdInstance> _mecanimReaderInstances;
        internal List<GPUICrowdInstance> _legacyReaderInstances;

        public Func<NativeArray<Matrix4x4>> getTransformMatrixDelegate;
        public Action setTransformMatrixModifiedDelegate;
        public Func<TransformAccessArray> getTransformAccessArrayDelegate;

        private GPUIBoneTransformRWData _boneRWData;

        private int _lastBoneTAABufferSize;
        public bool HasComputeRootMotion { get; private set; }
        public bool HasBoneRead => _boneRWData._hasRead;
        public bool HasBoneWrite => _boneRWData._hasWrite;
        private bool _isDisposed;

        public GPUICrowdRenderSource(GPUIRenderSource renderSource, GPUICrowdRenderSourceGroup crowdRSG)
        {
            this.renderSource = renderSource;
            this.crowdRSG = crowdRSG;
            boneCount = crowdRSG.rig.bones.Count;
            _crowdInstancesToProcess = new();
            _mecanimReaderInstances = new();
            _legacyReaderInstances = new();

            if (renderSource.source is IGPUIInstanceTransformProvider transformMatrixProvider)
            {
                int prefabID = transformMatrixProvider.GetPrefabID(renderSource.renderSourceGroup.LODGroupData.prototype.prefabObject);
                if (prefabID != 0)
                {
                    getTransformMatrixDelegate = () => transformMatrixProvider.GetTransformMatrix(prefabID);
                    setTransformMatrixModifiedDelegate = () => transformMatrixProvider.SetTransformMatrixModified(prefabID);
                    getTransformAccessArrayDelegate = () => transformMatrixProvider.GetTransformAccessArray(prefabID);
                }
            }

            _boneRWData = new(boneCount);
        }

        public virtual void Dispose()
        {
            _isDisposed = true;
            if (_boneRWData != null)
                _boneRWData.Dispose();
        }

        internal void ApplyRenderSourceBufferSizeChanges()
        {
            crowdRSG.ApplyRenderSourceBufferSizeChanges(this);
        }

        public void OnBufferIndexModified(GPUICrowdInstance crowdInstance, int previousBufferIndex, int newBufferIndex)
        {            
            crowdRSG.OnBufferIndexModified(previousBufferIndex < 0 ? -1 : renderSource.bufferStartIndex + previousBufferIndex, newBufferIndex < 0 ? -1 : renderSource.bufferStartIndex + newBufferIndex);
            if (previousBufferIndex >= 0)
                _boneRWData.ClearReferences(previousBufferIndex);
            crowdInstance._hasAssignedBoneReferences = false;
            crowdInstance.IsProcessed = false;
            if (newBufferIndex >= 0)
                _crowdInstancesToProcess.Add(crowdInstance);
        }

        public unsafe virtual void ProcessCrowdInstances()
        {
            if (_isDisposed)
                return;
            int count = _crowdInstancesToProcess.Count;
            if (count == 0)
                return;
            _boneRWData.ClearInvalidTransformRefs();
            void* p_crowdInstanceDataArray = crowdRSG.crowdInstanceDataBuffer.GetUnsafeNativeArrayPtr();
            for (int i = 0; i < count; i++)
            {
                GPUICrowdInstance crowdInstance = _crowdInstancesToProcess[i];
                if (!crowdInstance.IsInstanced || crowdInstance.IsProcessed)
                    continue;
                int bufferIndex = crowdInstance.PrefabComponent.bufferIndex;
                int rsgBufferIndex = renderSource.bufferStartIndex + bufferIndex;
                GPUICrowdInstanceData crowdInstanceData = crowdInstance._crowdInstanceData;
                UnsafeUtility.WriteArrayElementWithStride(p_crowdInstanceDataArray, rsgBufferIndex, GPUICrowdInstanceData.STRIDE, crowdInstanceData);

                if (crowdInstanceData.ApplyCustomRootMotion)
                    HasComputeRootMotion = true;

                _boneRWData.AddBoneTransforms(crowdInstance, bufferIndex, crowdInstance._animatorWorkflowID == GPUIAWComputeAnimator.WORKFLOW_ID);

                #region Mecanim Reader
                int mrIndex = _mecanimReaderInstances.IndexOf(crowdInstance);
                if (crowdInstance._animatorWorkflowID == GPUIAWMecanimReader.WORKFLOW_ID && crowdInstance._hasPrefabComponent && crowdInstance._hasAnimator)
                {
                    if (mrIndex < 0)
                        _mecanimReaderInstances.Add(crowdInstance);
                    crowdInstance._lastMecanimStateCheck = -1;
                }
                else if (mrIndex >= 0)
                    _mecanimReaderInstances.RemoveAtSwapBack(mrIndex);
                #endregion Mecanim Reader

                #region Legacy Reader
                int lrIndex = _legacyReaderInstances.IndexOf(crowdInstance);
                if (crowdInstance._animatorWorkflowID == GPUIAWLegacyAnimationReader.WORKFLOW_ID && crowdInstance._hasPrefabComponent && crowdInstance._hasLegacyAnimation)
                {
                    if (lrIndex < 0)
                        _legacyReaderInstances.Add(crowdInstance);
                    crowdInstance._lastLegacyStateCheck = -1;
                    crowdInstance.ReadLegacyAnimationStates();
                }
                else if (lrIndex >= 0)
                {
                    _legacyReaderInstances.RemoveAtSwapBack(lrIndex);
                    crowdInstance._legacyStates = null;
                }
                #endregion Legacy Reader

                crowdInstance.IsProcessed = true; // We set it at the end because the OnProcessed action will be invoked.
            }
            _crowdInstancesToProcess.Clear();
        }

        public TransformAccessArray GetBoneRWTAA() => _boneRWData.GetBoneTAA();
        public NativeArray<int2> GetBoneReadWriteStatusData() => _boneRWData.GetBoneReadWriteStatusData();
        internal unsafe void* GetUnsafeBoneReadWriteStatusDataPtr() => _boneRWData.GetUnsafeBoneReadWriteStatusDataPtr();
        public GraphicsBuffer GetBoneReadWriteStatusBuffer() => _boneRWData.GetBoneReadWriteStatusBuffer();
        public int GetBoneRWTransformCount() => _boneRWData.GetTransformCount();
        public GPUIDataBuffer<GPUITransformData> GetBoneRWTransformData() => _boneRWData.GetBoneRWTransformData();

        public bool TryGetCrowdAnimatorClipData(int bufferIndex, List<GPUICrowdAnimatorClipData> clipDataList, out Vector4 clipWeights, List<AnimationClip> clips = null)
        {
            clipWeights = Vector4.zero;
            if (renderSource.bufferStartIndex < 0)
                return false;

            bool hasClips = false;
            if (clips != null)
            {
                hasClips = true;
                clips.Clear();
            }

            int rsgBufferIndex = renderSource.bufferStartIndex + bufferIndex;

            var animatorClipDataArray = crowdRSG.GetAnimatorClipDataArray(true);
            if (!animatorClipDataArray.IsCreated || animatorClipDataArray.Length <= rsgBufferIndex * GPUICrowdConstants.ANIMATOR_MAX_CLIPS + 3)
                return false;
            var clipFramesAndWeightsArray = crowdRSG.GetClipFramesAndWeightsArray(true);
            if (!clipFramesAndWeightsArray.IsCreated || clipFramesAndWeightsArray.Length <= rsgBufferIndex * 2 + 1)
                return false;

            GetCrowdAnimatorData(animatorClipDataArray, clipFramesAndWeightsArray, rsgBufferIndex, clipDataList, out clipWeights);

            if (hasClips)
            {
                for (int i = 0; i < clipDataList.Count; i++)
                    clips.Add(crowdRSG.rig.GetAnimationClipWithBakedIndex(clipDataList[i].BakedClipIndex));
            }

            return true;
        }

        public bool TryGetCrowdAnimatorClipData(int clipIndex, int bufferIndex, out GPUICrowdAnimatorClipData clipData, out float clipWeight)
        {
            clipData = default;
            clipWeight = 0;
            if (renderSource.bufferStartIndex < 0)
                return false;

            int rsgBufferIndex = renderSource.bufferStartIndex + bufferIndex;

            var animatorClipDataArray = crowdRSG.GetAnimatorClipDataArray(true);
            if (!animatorClipDataArray.IsCreated || animatorClipDataArray.Length <= rsgBufferIndex * GPUICrowdConstants.ANIMATOR_MAX_CLIPS + 3)
                return false;
            var clipFramesAndWeightsArray = crowdRSG.GetClipFramesAndWeightsArray(true);
            if (!clipFramesAndWeightsArray.IsCreated || clipFramesAndWeightsArray.Length <= rsgBufferIndex * 2 + 1)
                return false;

            clipData = GetCrowdAnimatorData(clipIndex, animatorClipDataArray, clipFramesAndWeightsArray, rsgBufferIndex, out clipWeight);

            return true;
        }

        public AnimationClip GetAnimationClipFromClipData(GPUICrowdAnimatorClipData clipData)
        {
            return crowdRSG.rig.GetAnimationClipWithBakedIndex(clipData.BakedClipIndex);
        }

        public void GetCrowdAnimatorData(NativeArray<GPUICrowdAnimatorClipData> animatorClipDataArray, NativeArray<Vector4> clipFramesAndWeightsArray, int rsgBufferIndex, List<GPUICrowdAnimatorClipData> clipDataList, out Vector4 clipWeights)
        {
            clipDataList.Clear();
            int clipDataIndex = rsgBufferIndex * GPUICrowdConstants.ANIMATOR_MAX_CLIPS;

            clipWeights = clipFramesAndWeightsArray[rsgBufferIndex * 2 + 1];
            for (int i = 0; i < GPUICrowdConstants.ANIMATOR_MAX_CLIPS; i++)
            {
                if (clipWeights[i] <= 0)
                    continue;
                var clipData = animatorClipDataArray[clipDataIndex + i];
                if (clipData.IsValid)
                    clipDataList.Add(clipData);
            }
        }

        public GPUICrowdAnimatorClipData GetCrowdAnimatorData(int clipIndex, NativeArray<GPUICrowdAnimatorClipData> animatorClipDataArray, NativeArray<Vector4> clipFramesAndWeightsArray, int rsgBufferIndex, out float clipWeight)
        {
            clipWeight = clipFramesAndWeightsArray[rsgBufferIndex * 2 + 1][clipIndex];
            return animatorClipDataArray[rsgBufferIndex * GPUICrowdConstants.ANIMATOR_MAX_CLIPS + clipIndex];
        }

        internal unsafe class GPUIBoneTransformRWData : IDisposable
        {
            private int _totalBoneCount;
            private TransformAccessArray _transformAA;
            /// <summary>
            /// x=> bone buffer index, y=> read/write status
            /// </summary>
            private NativeArray<int2> _boneReadWriteStatusData;
            private void* p_boneReadWriteStatusData;
            private GraphicsBuffer _boneReadWriteStatusBuffer;
            private GPUIDataBuffer<GPUITransformData> _boneRWTransformData;
            private bool _isRWStatusDataModified;
            internal bool _hasRead;
            internal bool _hasWrite;

            public GPUIBoneTransformRWData(int totalBoneCount)
            {
                _totalBoneCount = totalBoneCount;
                _hasRead = false;
                _hasWrite = false;
            }

            public void Dispose()
            {
                if (_transformAA.isCreated)
                    _transformAA.Dispose();
                if (_boneReadWriteStatusData.IsCreated)
                    _boneReadWriteStatusData.Dispose();
                if (_boneReadWriteStatusBuffer != null)
                {
                    _boneReadWriteStatusBuffer.Dispose();
                    _boneReadWriteStatusBuffer = null;
                }
                if (_boneRWTransformData != null)
                {
                    _boneRWTransformData.Dispose();
                    _boneRWTransformData = null;
                }
                _isRWStatusDataModified = false;
            }

            internal unsafe void ClearInvalidTransformRefs()
            {
                int transformCount = GetTransformCount();
                if (transformCount <= 0)
                    return;
                int lastIndex = transformCount - 1;
                for (int i = lastIndex; i >= 0; i--)
                {
                    int2 status = UnsafeUtility.ReadArrayElementWithStride<int2>(p_boneReadWriteStatusData, i, 8);
                    if (status.y <= 0 || _transformAA[i] == null)
                    {
                        if (lastIndex != i)
                        {
                            UnsafeUtility.WriteArrayElementWithStride(p_boneReadWriteStatusData, i, 8, UnsafeUtility.ReadArrayElementWithStride<int2>(p_boneReadWriteStatusData, lastIndex, 8));
                            _isRWStatusDataModified = true;
                        }
                        _transformAA.RemoveAtSwapBack(i);
                        lastIndex--;
                    }
                }
            }

            public unsafe void ClearReferences(int rsBufferIndex)
            {
                int transformCount = GetTransformCount();
                if (transformCount <= 0)
                    return;
                int startIndex = rsBufferIndex * _totalBoneCount;
                int endIndex = startIndex + _totalBoneCount;
                int lastIndex = transformCount - 1;
                for (int i = lastIndex; i >= 0; i--)
                {
                    int2 status = UnsafeUtility.ReadArrayElementWithStride<int2>(p_boneReadWriteStatusData, i, 8);
                    if (status.x >= startIndex && status.x < endIndex)
                    {
                        if (lastIndex != i)
                        {
                            UnsafeUtility.WriteArrayElementWithStride(p_boneReadWriteStatusData, i, 8, UnsafeUtility.ReadArrayElementWithStride<int2>(p_boneReadWriteStatusData, lastIndex, 8));
                            _isRWStatusDataModified = true;
                        }
                        _transformAA.RemoveAtSwapBack(i);
                        lastIndex--;
                    }
                }
            }

            public unsafe void AddBoneTransforms(GPUICrowdInstance crowdInstance, int rsBufferIndex, bool allowWrite)
            {
                if (crowdInstance._boneTransforms == null)
                    return;
                int refCount = crowdInstance._boneTransforms.Count;
                if (refCount == 0)
                    return;
                if (crowdInstance._hasAssignedBoneReferences)
                {
                    crowdInstance._hasAssignedBoneReferences = false;
                    ClearReferences(rsBufferIndex);
                }

                int transformCount = GetTransformCount();
                if (transformCount + refCount >= GetTransformCapacity())
                    ExpandCapacity(Math.Max(refCount * 10, _totalBoneCount * 2));

                int2 status = int2.zero;
                int boneBufferIndex = rsBufferIndex * _totalBoneCount;
                int addedCount = 0;
                for (int i = 0; i < refCount; i++)
                {
                    var boneRef = crowdInstance._boneTransforms[i];
                    if (boneRef.readWriteStatus <= 0 || boneRef.readWriteStatus > 2 || boneRef.transform == null)
                        continue;
                    if (boneRef.readWriteStatus == 1)
                        _hasRead = true;
                    else if (boneRef.readWriteStatus == 2)
                    {
                        if (!allowWrite)
                            continue;
                        _hasWrite = true;
                    }
                    _transformAA.Add(boneRef.transform);
                    status.x = boneBufferIndex + boneRef.boneIndex;
                    status.y = boneRef.readWriteStatus;
                    UnsafeUtility.WriteArrayElementWithStride(p_boneReadWriteStatusData, transformCount, 8, status);
                    transformCount++;
                    addedCount++;
                }
                if (addedCount > 0)
                {
                    _isRWStatusDataModified = true;
                    crowdInstance._hasAssignedBoneReferences = true;
                }
            }

            public unsafe void ExpandCapacity(int expansionSize)
            {
                if (expansionSize <= 0)
                    return;
                int transformCount = GetTransformCount();
                int capacity = GetTransformCapacity() + expansionSize;
                TransformAccessArray.Allocate(capacity, -1, out var newTAA);
                for (int i = 0; i < transformCount; i++)
                    newTAA.Add(_transformAA[i]);
                if (_transformAA.isCreated)
                    _transformAA.Dispose();
                _transformAA = newTAA;

                _boneReadWriteStatusData.ResizeNativeArray(capacity, Allocator.Persistent);
                p_boneReadWriteStatusData = NativeArrayUnsafeUtility.GetUnsafePtr(_boneReadWriteStatusData);

                _isRWStatusDataModified = true;
            }

            public GraphicsBuffer GetBoneReadWriteStatusBuffer()
            {
                int transformCount = GetTransformCount();
                if (transformCount == 0)
                    return null;
                if (_isRWStatusDataModified)
                {
                    _isRWStatusDataModified = false;
                    if (_boneReadWriteStatusBuffer == null || _boneReadWriteStatusBuffer.count < _transformAA.capacity)
                    {
                        if (_boneReadWriteStatusBuffer != null)
                            _boneReadWriteStatusBuffer.Dispose();
                        _boneReadWriteStatusBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _transformAA.capacity, 8);
                    }
                    _boneReadWriteStatusBuffer.SetData(_boneReadWriteStatusData, 0, 0, transformCount);
                }
                return _boneReadWriteStatusBuffer;
            }

            public TransformAccessArray GetBoneTAA() => _transformAA;

            public NativeArray<int2> GetBoneReadWriteStatusData() => _boneReadWriteStatusData;
            internal unsafe void* GetUnsafeBoneReadWriteStatusDataPtr() => p_boneReadWriteStatusData;

            public int GetTransformCount() => _transformAA.isCreated ? _transformAA.length : 0;
            public int GetTransformCapacity() => _transformAA.isCreated ? _transformAA.capacity : 0;

            public GPUIDataBuffer<GPUITransformData> GetBoneRWTransformData()
            {
                int capacity = _transformAA.capacity;
                if (_boneRWTransformData == null)
                    _boneRWTransformData = new GPUIDataBuffer<GPUITransformData>("BoneRWTransformData", capacity);
                else if (_boneRWTransformData.Length < capacity)
                    _boneRWTransformData.ResizeWithoutCopy(capacity);
                return _boneRWTransformData;
            }
        }
    }

    [Serializable]
    public struct GPUICrowdInstanceData
    {
        [SerializeField]
        public int animatorWorkflowID;
        [SerializeField]
        internal uint _settings;

        public const int STRIDE = 8;
        private const uint MaxValue = 1u;
        private const uint MinValue = 0u;

        internal bool ApplyCustomRootMotion
        {
            get => GetSetting(0) > MinValue;
            set => SetSetting(0, value ? MaxValue : MinValue);
        }

        private void SetSetting(int index, uint value)
        {
            _settings &= ~(MaxValue << index); // Clear bits
            _settings |= (value & MaxValue) << index; // Set new value
        }

        private uint GetSetting(int index)
        {
            return (_settings & (MaxValue << index)) >> index;
        }
    }

    [Serializable]
    public struct GPUICrowdAnimatorClipData
    {
        /// <summary>
        /// FrameStartIndex - 20 bits, Max 1,048,575
        /// BakedClipIndex - 12 bits, Max 4,095
        /// </summary>
        [SerializeField]
        internal int packedFrameStartAndBakedClipIndex;
        /// <summary>
        /// sign bit (31): is looping
        /// 15 bits: frame count (0–32,767)
        /// 15 bits: stored as (speed * 1000), range 0–65.535
        /// </summary>
        [SerializeField]
        internal int packedFrameCountLoopAndSpeed;
        [SerializeField]
        internal float clipStartTime;
        /// <summary>
        /// Absolute value: blended speed relative clip length, sign: is synced with other blended clips
        /// </summary>
        [SerializeField]
        internal float packedSpeedRelativeLengthAndSync;

        public int FrameStartIndex => packedFrameStartAndBakedClipIndex >> 12;
        public int BakedClipIndex => packedFrameStartAndBakedClipIndex & 0xFFF; // Mask lower 12 bits

        public bool IsLooping => packedFrameCountLoopAndSpeed >= 0; // sign bit as looping
        public int FrameCount => (Math.Abs(packedFrameCountLoopAndSpeed) >> 16) & 0x7FFF;
        public float TargetSpeed => (Math.Abs(packedFrameCountLoopAndSpeed) & 0xFFFF) / 1000f;

        public bool IsValid => packedSpeedRelativeLengthAndSync != 0f;
        public float SpeedRelativeClipLength => Math.Abs(packedSpeedRelativeLengthAndSync);
        public bool IsSynched => packedSpeedRelativeLengthAndSync > 0f;

        public const int STRIDE = 16;

        public float GetNormalizedTime(float currentTime)
        {
            float playTime = currentTime - clipStartTime;
            float speedRelativeClipLength = Math.Abs(packedSpeedRelativeLengthAndSync);
            if (packedFrameCountLoopAndSpeed < 0 && playTime >= speedRelativeClipLength)
                return 1f;
            return playTime / speedRelativeClipLength;
        }

        public void SetFrameStartIndexAndBakedClipIndex(int bakedBoneDataIndex, int boneCount, int bakedClipIndex)
        {
            packedFrameStartAndBakedClipIndex = ((bakedBoneDataIndex / boneCount) << 12) | (bakedClipIndex & 0xFFF);
        }

        public void SetFrameCountSpeedAndLoop(int frameCount, float targetSpeed, bool isLooping)
        {
            frameCount = Mathf.Clamp(frameCount, 0, 0x7FFF); // 15 bits
            int speedInt = Mathf.Clamp(Mathf.RoundToInt(targetSpeed * 1000f), 0, 0xFFFF); // 16 bits

            int packed = (frameCount << 16) | speedInt;

            packedFrameCountLoopAndSpeed = isLooping ? packed : -packed;
        }

        public void SetSpeed(float newSpeed, float clipLength, float currentTime)
        {
            float oldSpeed = TargetSpeed;
            if (oldSpeed == newSpeed)
                return;
            SetFrameCountSpeedAndLoop(FrameCount, newSpeed, IsLooping);
            newSpeed = Mathf.Max(newSpeed, GPUICrowdConstants.MIN_CLIP_SPEED);
            float normalizedClipTime = GetNormalizedTime(currentTime);
            clipStartTime = currentTime - clipLength * normalizedClipTime / newSpeed;
            packedSpeedRelativeLengthAndSync = clipLength / newSpeed * Mathf.Sign(packedSpeedRelativeLengthAndSync);
        }
    }

    [Serializable]
    public struct GPUICrowdTransition
    {
        public int bufferIndex;
        public float startTime;
        public float transitionLength;
        public Vector4 startWeights;
        public Vector4 targetWeights;

        public const int STRIDE = 44;
    }
}