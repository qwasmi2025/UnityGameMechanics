// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;
using Unity.Collections.LowLevel.Unsafe;
using System.IO;
using UnityEngine.Profiling;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GPUInstancerPro.CrowdAnimations
{
    [Serializable]
    public class GPUICrowdRig : ScriptableObject, IGPUIDisposable
    {
        #region Serialized Properties
        [SerializeField]
        public List<GPUICrowdBone> bones;
        [SerializeField]
        public List<GPUICrowdSkinnedMeshData> skinnedMeshes;
        [SerializeField]
        public List<GPUICrowdBindPoseData> bindPoseDataList;
        [SerializeField]
        public GPUICrowdSkinWeights skinWeights;
        /// <summary>
        /// Reference to clips saved in <see cref="_serializedClipData"/>
        /// </summary>
        [SerializeField]
#if !GPUIPRO_DEVMODE
        [HideInInspector]
#endif
        private List<AnimationClip> _serializedClips;
        /// <summary>
        /// Header + bakedClipData + bakedBoneData + bakedRootMotionData
        /// </summary>
        [SerializeField]
#if !GPUIPRO_DEVMODE
        [HideInInspector]
#endif
        private TextAsset _serializedClipData;
        /// <summary>
        /// Used for checking the serialized data validity
        /// </summary>
        [SerializeField]
        [HideInInspector]
        private int _rigDataVersion;
        #endregion Serialized Properties

        #region Runtime Properties
        [NonSerialized]
        private NativeArray<GPUITransformData> _bakedBoneData;
        [NonSerialized]
        private bool _bakedBoneDataModified;
        [NonSerialized]
        private GraphicsBuffer _bakedBoneDataBuffer;
        [NonSerialized]
        private NativeArray<GPUICrowdRootMotion> _bakedRootMotionData;
        /// <summary>
        /// Key = Animation Clip Instance ID, Value = BakedClipDataArray Index
        /// </summary>
        [NonSerialized]
        private Dictionary<int, int> _bakedClipIndexDictionary;
        [NonSerialized]
        private NativeArray<GPUICrowdBakedClipData> _bakedClipDataArray;
        [NonSerialized]
        private int _bakedClipCount;
        [NonSerialized]
        private int _bakedBoneDataLength;
        [NonSerialized]
        private int _bakedRootMotionLength;
        /// <summary>
        /// Key = Animation Clip Instance ID
        /// </summary>
        [NonSerialized]
        private Dictionary<int, AnimationClip> _bakedAnimationClips;
        [NonSerialized]
        private bool _isLoadedSerializedClipData;
        #endregion Runtime Properties

        #region Editor Only Properties
#if UNITY_EDITOR
        [SerializeField]
        public List<AnimationClip> editor_clipsToSerialize;
#endif
        #endregion Editor Only Properties

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            LoadSerializedClipDataToBuffers();
        }

        private void OnDisable()
        {
            Dispose();
        }

        public void ReleaseBuffers()
        {
            if (_bakedBoneData.IsCreated)
                _bakedBoneData.Dispose();
            if (_bakedRootMotionData.IsCreated)
                _bakedRootMotionData.Dispose();
            if (_bakedClipDataArray.IsCreated)
                _bakedClipDataArray.Dispose();
            _bakedClipIndexDictionary = null;
            if (_bakedBoneDataBuffer != null)
            {
                _bakedBoneDataBuffer.Dispose();
                _bakedBoneDataBuffer = null;
            }
            _bakedClipCount = 0;
            _bakedBoneDataLength = 0;
            _bakedRootMotionLength = 0;
            _bakedAnimationClips = null;
            _isLoadedSerializedClipData = false;
        }

        public void Dispose()
        {
            ReleaseBuffers();
        }

        public void AddBones(Transform parentTransform, Transform[] boneTransforms)
        {
            bones ??= new();
            foreach (var boneTransform in boneTransforms)
                AddBone(parentTransform, boneTransform);
        }

        private int AddBone(Transform parentTransform, Transform boneTransform)
        {
            if (boneTransform == null)
                return -1;
            string fullPath = GPUICrowdUtility.GenerateBoneFullPath(parentTransform, boneTransform);
            int boneIndex = GetBoneIndex(fullPath);
            if (boneIndex >= 0)
                return boneIndex;
            boneIndex = bones.Count;
            GPUICrowdBone bone = new GPUICrowdBone(fullPath, boneTransform.name);
            bones.Add(bone);
            _rigDataVersion++;
            return boneIndex;
        }

        public int GetBoneIndex(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath) || bones == null) return -1;
            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i].fullPath == fullPath)
                    return i;
            }
            return -1;
        }

        private void ResizeBindPosesBasedOnBoneCount()
        {
            if (bindPoseDataList == null || bones == null)
                return;
            int boneCount = bones.Count;
            foreach (var bindPoseData in bindPoseDataList)
            {
                if (bindPoseData.bindPoses == null)
                    bindPoseData.bindPoses = new Matrix4x4[boneCount];
                else if (bindPoseData.bindPoses.Length != boneCount)
                    Array.Resize(ref bindPoseData.bindPoses, boneCount);
            }
        }

        public GPUICrowdSkinnedMeshData AddSkinnedMesh(Transform parentTransform, SkinnedMeshRenderer skinnedMeshRenderer)
        {
            if (skinnedMeshRenderer == null)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Given Skinned Mesh Renderer is null!");
                return null;
            }
            Mesh skinnedMesh = skinnedMeshRenderer.sharedMesh;
            if (skinnedMesh == null)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Given Skinned Mesh Renderer does not have a mesh assigned!");
                return null;
            }
            GPUICrowdSkinnedMeshData skinnedMeshData = GetMeshDataByMesh(skinnedMesh);
            if (skinnedMeshData != null)
                return skinnedMeshData;

            skinnedMeshData = new GPUICrowdSkinnedMeshData()
            {
                skinnedMesh = skinnedMesh,
                bindPoseIndex = -1
            };
            skinnedMeshes ??= new();
            skinnedMeshes.Add(skinnedMeshData);

            Transform[] bones = skinnedMeshRenderer.bones;
            skinnedMeshData.boneIndexes = new int[bones.Length];

            AddBones(parentTransform, bones);

            for (int i = 0; i < bones.Length; i++)
            {
                Transform boneTransform = bones[i];
                if (boneTransform == null)
                {
                    Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not find bone transform for " + skinnedMesh.name + " at index: " + i, skinnedMesh);
                    continue;
                }
                int boneIndex = GetBoneIndex(GPUICrowdUtility.GenerateBoneFullPath(parentTransform, bones[i]));
                skinnedMeshData.boneIndexes[i] = boneIndex;
                if (boneIndex < 0)
                    Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not find bone index for " + skinnedMesh.name + " at index: " + i, skinnedMesh);
            }
            ResizeBindPosesBasedOnBoneCount();

            bindPoseDataList ??= new();
            SetSkinnedMeshBindPoseData(skinnedMeshData);

            return skinnedMeshData;
        }

        private void SetSkinnedMeshBindPoseData(GPUICrowdSkinnedMeshData skinnedMeshData)
        {
            Matrix4x4[] meshBindPoses = skinnedMeshData.skinnedMesh.bindposes;
            if (meshBindPoses == null)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Given bind pose matrix is null!");
                return;
            }
            if (skinnedMeshData.boneIndexes == null || skinnedMeshData.boneIndexes.Length != meshBindPoses.Length)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Bone indexes does not match the bind pose matrix size!");
                return;
            }

            int boneCount = bones.Count;
            Matrix4x4[] bindPoses = new Matrix4x4[boneCount];
            for (int i = 0; i < skinnedMeshData.boneIndexes.Length; i++)
                bindPoses[skinnedMeshData.boneIndexes[i]] = meshBindPoses[i];

            for (int i = 0; i < bindPoseDataList.Count; i++)
            {
                if (bindPoseDataList[i].IsMatchingBindPose(bindPoses))
                {
                    bindPoseDataList[i].SetUnsetBindPoses(bindPoses);
                    skinnedMeshData.bindPoseIndex = i;
                    break;
                }
            }
            if (skinnedMeshData.bindPoseIndex < 0)
            {
                GPUICrowdBindPoseData bindPoseData = new GPUICrowdBindPoseData()
                {
                    bindPoses = bindPoses
                };
                skinnedMeshData.bindPoseIndex = bindPoseDataList.Count;
                bindPoseDataList.Add(bindPoseData);
            }
        }

        public GPUICrowdSkinnedMeshData GetMeshDataByMesh(Mesh mesh)
        {
            if (skinnedMeshes == null)
                return null;
            foreach (var smd in skinnedMeshes)
            {
                if (smd.skinnedMesh == mesh)
                    return smd;
            }
            return null;
        }

        internal void GenerateVertexBoneData()
        {
            if (skinnedMeshes == null)
                return;
            foreach (var smd in skinnedMeshes)
                smd.GenerateVertexBoneData();
        }

        internal int GetVertexBoneDataSize()
        {
            int count = 0;
            if (skinnedMeshes != null)
            {
                foreach (var smd in skinnedMeshes)
                {
                    if (smd._vertexBoneData != null)
                        count += smd._vertexBoneData.Count;
                }
            }
            return count;
        }

        public bool IsMatchingBoneData(GPUICrowdRig other)
        {
            if (bones == null || other.bones == null) 
                return false;
            foreach (var bone in other.bones)
            {
                int boneIndex = GetBoneIndex(bone.fullPath);
                if (boneIndex < 0)
                    return false;
            }
            return true;
        }

        public void LoadBoneTransforms(GPUICrowdInstance crowdInstance)
        {
            if (bones == null)
                return;
            int boneCount = bones.Count;
            if (crowdInstance._boneTransforms == null)
                crowdInstance._boneTransforms = new();
            else
                crowdInstance._boneTransforms.Clear();
            Transform instanceTransform = crowdInstance.transform;
            for (int i = 0; i < boneCount; i++)
            {
                Transform foundTransform = instanceTransform.Find(bones[i].fullPath);
                if (foundTransform == null)
                    foundTransform = instanceTransform.FindDeepChild(bones[i].boneName);
                if (foundTransform != null)
                    crowdInstance._boneTransforms.Add(new GPUICrowdInstance.BoneTransformReference() { boneIndex = i, transform = foundTransform, readWriteStatus = 0 });
//#if GPUIPRO_DEVMODE
//                else
//                    Debug.LogWarning(GPUIConstants.LOG_PREFIX + "Can not find bone for transform: " + bones[i].fullPath);
//#endif
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
                EditorUtility.SetDirty(crowdInstance.gameObject);
#endif
        }

        public bool TryGetBakedClipDataIndex(AnimationClip animationClip, out int bakedClipDataIndex)
        {
            return TryGetBakedClipDataIndex(animationClip.GetInstanceIDGPUI(), out bakedClipDataIndex);
        }

        public bool TryGetBakedClipData(AnimationClip animationClip, out GPUICrowdBakedClipData bakedClipData)
        {
            return TryGetBakedClipData(animationClip.GetInstanceIDGPUI(), out bakedClipData);
        }

        public bool TryGetBakedClipDataIndex(int animationClipInstanceID, out int bakedClipDataIndex)
        {
            bakedClipDataIndex = -1;
            if (!_bakedClipDataArray.IsCreated)
                return false;
            return _bakedClipIndexDictionary.TryGetValue(animationClipInstanceID, out bakedClipDataIndex);
        }

        public bool TryGetBakedClipData(int animationClipInstanceID, out GPUICrowdBakedClipData bakedClipData)
        {
            bakedClipData = default;
            if (!TryGetBakedClipDataIndex(animationClipInstanceID, out int index))
                return false;
            bakedClipData = _bakedClipDataArray[index];
            return true;
        }

        public int GetOrCreateBakedClipIndex(GPUIRenderSourceGroup renderSourceGroup, AnimationClip animationClip)
        {
            if (TryGetBakedClipDataIndex(animationClip, out var result))
                return result;
            BakeAnimationClip(renderSourceGroup.LODGroupData.prototype.prefabObject.AddOrGetComponent<GPUICrowdInstance>(), animationClip, GPUICrowdConstants.DEFAULT_CLIP_FRAME_RATE);
            return _bakedClipCount - 1;
        }

        public GPUICrowdBakedClipData GetOrCreateBakedClipData(GPUIRenderSourceGroup renderSourceGroup, AnimationClip animationClip)
        {
            if (TryGetBakedClipData(animationClip, out var result))
                return result;
            return BakeAnimationClip(renderSourceGroup.LODGroupData.prototype.prefabObject.AddOrGetComponent<GPUICrowdInstance>(), animationClip, GPUICrowdConstants.DEFAULT_CLIP_FRAME_RATE);
        }

        public GPUICrowdBakedClipData GetOrCreateBakedClipData(GPUICrowdInstance crowdInstance, AnimationClip animationClip)
        {
            if (TryGetBakedClipData(animationClip, out var result))
                return result;
            return BakeAnimationClip(crowdInstance, animationClip, GPUICrowdConstants.DEFAULT_CLIP_FRAME_RATE);
        }

        /// <returns>Baked clip data.</returns>
        private GPUICrowdBakedClipData BakeAnimationClip(GPUICrowdInstance crowdInstance, AnimationClip animationClip, int frameRate)
        {
            var bakedData = GenerateBakedClipData_Internal(animationClip, frameRate);
            var clipSampler = GPUICrowdSkinningSystem.Instance.ClipSamplerProvider.CreateClipSampler(crowdInstance);
            GPUICrowdUtility.BakeAnimationClip_Internal(clipSampler, animationClip, bakedData);
            return bakedData;
        }

        internal GPUICrowdBakedClipData GenerateBakedClipData_Internal(AnimationClip animationClip, int frameRate)
        {
            _bakedClipIndexDictionary ??= new();
            if (!_bakedClipDataArray.IsCreated || _bakedClipCount + 1 >= _bakedClipDataArray.Length)
                GPUIUtility.ResizeNativeArray(ref _bakedClipDataArray, _bakedClipCount + 128, Allocator.Persistent);
            int animationClipInstanceID = animationClip.GetInstanceIDGPUI();
            int boneCount = bones.Count;
            if (!TryGetBakedClipData(animationClipInstanceID, out GPUICrowdBakedClipData bakedData))
            {
                float clipLength = animationClip.length;
                int clipFrameCount = Mathf.CeilToInt(clipLength * frameRate);
                bakedData = new GPUICrowdBakedClipData()
                {
                    clipLength = clipLength,
                    clipFrameCount = clipFrameCount,
                    bakedBoneDataIndex = _bakedBoneDataLength,
                    bakedRootMotionIndex = -1,
                    isLoopingMultiplier = animationClip.isLooping ? 1 : -1
                };
                _bakedBoneDataLength += clipFrameCount * boneCount;
                _bakedBoneData.ResizeNativeArray(_bakedBoneDataLength, Allocator.Persistent);
                GPUICrowdSkinningSystem.Instance.AddDependentDisposable(this);
                _bakedClipDataArray[_bakedClipCount] = bakedData;
                _bakedClipIndexDictionary[animationClipInstanceID] = _bakedClipCount;
                _bakedClipCount++;
                _bakedBoneDataModified = true;

                _bakedAnimationClips ??= new();
                _bakedAnimationClips.Add(animationClipInstanceID, animationClip);
            }
            return bakedData;
        }

        public NativeArray<GPUITransformData> GetBakedBoneData(bool isReadonly = false)
        {
            if (!isReadonly)
                _bakedBoneDataModified = true;
            return _bakedBoneData;
        }

        public NativeArray<GPUICrowdRootMotion> GetBakedRootMotionData()
        {
            return _bakedRootMotionData;
        }

        internal void SetRootMotionData(AnimationClip animationClip, GPUICrowdBakedClipData bakedClipData, GPUICrowdRootMotion[] rootMotionData)
        {
            bakedClipData.bakedRootMotionIndex = _bakedRootMotionLength;
            _bakedRootMotionLength += bakedClipData.clipFrameCount;
            _bakedRootMotionData.ResizeNativeArray(_bakedRootMotionLength, Allocator.Persistent);
            NativeArray<GPUICrowdRootMotion>.Copy(rootMotionData, 0, _bakedRootMotionData, bakedClipData.bakedRootMotionIndex, bakedClipData.clipFrameCount);
            _bakedClipDataArray[_bakedClipIndexDictionary[animationClip.GetInstanceIDGPUI()]] = bakedClipData;
        }

        public GraphicsBuffer GetBakedBoneDataBuffer()
        {
            if (!_bakedBoneData.IsCreated)
                return null;
            if (_bakedBoneDataBuffer != null && _bakedBoneDataBuffer.count != _bakedBoneData.Length)
            {
                _bakedBoneDataBuffer.Dispose();
                _bakedBoneDataBuffer = null;
            }
            if (_bakedBoneDataBuffer == null)
            {
                _bakedBoneDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _bakedBoneData.Length, 40);
                _bakedBoneDataModified = true;
            }
            if (_bakedBoneDataModified)
            {
                _bakedBoneDataBuffer.SetData(_bakedBoneData);
                _bakedBoneDataModified = false;
            }
            return _bakedBoneDataBuffer;
        }

        public int GetBoneCount() => bones == null ? 0 : bones.Count;
        public int GetSkinnedMeshCount() => skinnedMeshes == null ? 0 : skinnedMeshes.Count;
        public int GetBakedBoneDataLength() => _bakedBoneDataLength;
        public int GetBakedRootMotionLength() => _bakedRootMotionLength;
        public int GetBakedClipCount() => _bakedClipCount;
        public NativeArray<GPUICrowdBakedClipData> GetBakedClipDataArray() => _bakedClipDataArray;
        public Dictionary<int, int> GetBakedClipIndexDictionary() => _bakedClipIndexDictionary;
        public AnimationClip GetBakedAnimationClip(int clipInstanceID)
        {
            if (_bakedAnimationClips == null)
                return null;
            if (_bakedAnimationClips.TryGetValue(clipInstanceID, out var result))
                return result;
            return null;
        }
        public AnimationClip GetAnimationClipWithBakedIndex(int bakedIndex)
        {
            if (_bakedClipIndexDictionary == null)
                return null;
            foreach (var item in _bakedClipIndexDictionary)
            {
                if (item.Value == bakedIndex)
                    return GetBakedAnimationClip(item.Key);
            }
            return null;
        }
        public AnimationClip GetBakedAnimationClipWithName(string clipName)
        {
            if (_bakedAnimationClips == null)
                return null;
            foreach (var clip in _bakedAnimationClips.Values)
            {
                if (clip.name == clipName)
                    return clip;
            }
            return null;
        }

        #region SerializedBakedClipData
        [Serializable]
        public struct GPUISerializedBakedClipDataHeader
        {
            public int rigDataVersion;
            public int numBakedClips;
            public int boneCount;
            public int bakedClipDataArrayLength;
            public int bakedBoneDataArrayLength;
            public int bakedRootMotionDataArrayLength;
        }

        public TextAsset GetSerializedClipData() => _serializedClipData;
        public AnimationClip[] GetSerializedClips()
        {
            if (_serializedClips == null)
                return new AnimationClip[0];
            return _serializedClips.ToArray();
        }

        public void LoadSerializedClipDataToBuffers()
        {
            if (_isLoadedSerializedClipData || _serializedClipData == null || _serializedClips == null || _serializedClips.Count == 0)
                return;

#if GPUIPRO_DEVMODE
            float startTime = Time.realtimeSinceStartup;
#endif
            Profiler.BeginSample("GPUICrowdRig.LoadSerializedClipDataToBuffers");

            try
            {
                byte[] bytes = _serializedClipData.bytes;
                int headerSize = UnsafeUtility.SizeOf<GPUISerializedBakedClipDataHeader>();

                unsafe
                {
                    fixed (byte* ptr = bytes)
                    {
                        byte* current = ptr;

                        GPUISerializedBakedClipDataHeader header = UnsafeUtility.ReadArrayElement<GPUISerializedBakedClipDataHeader>(current, 0);
                        current += headerSize;

                        if (header.boneCount == GetBoneCount())
                        {
                            if (header.rigDataVersion != _rigDataVersion)
                                Debug.LogWarning(GPUIConstants.LOG_PREFIX + name + ": Data was modified after the animation clips were saved. It is recommended to re-bake the clips. Attempting to load anyway...");
                            Dispose();

                            _bakedClipCount = header.numBakedClips;
                            GPUIUtility.ResizeNativeArray(ref _bakedClipDataArray, _bakedClipCount + 128, Allocator.Persistent);
                            int bakedClipDataSize = header.bakedClipDataArrayLength * GPUICrowdBakedClipData.STRIDE;
                            UnsafeUtility.MemCpy(_bakedClipDataArray.GetUnsafePtr(), current, bakedClipDataSize);
                            current += bakedClipDataSize;

                            _bakedBoneDataLength = header.bakedBoneDataArrayLength;
                            _bakedBoneData.ResizeNativeArray(_bakedBoneDataLength, Allocator.Persistent);
                            int bakedBoneDataSize = header.bakedBoneDataArrayLength * GPUITransformData.STRIDE;
                            UnsafeUtility.MemCpy(_bakedBoneData.GetUnsafePtr(), current, bakedBoneDataSize);
                            current += bakedBoneDataSize;

                            _bakedRootMotionLength = header.bakedRootMotionDataArrayLength;
                            if (_bakedRootMotionLength > 0)
                            {
                                _bakedRootMotionData.ResizeNativeArray(_bakedRootMotionLength, Allocator.Persistent);
                                int bakedRootMotionSize = header.bakedRootMotionDataArrayLength * GPUICrowdRootMotion.STRIDE;
                                UnsafeUtility.MemCpy(_bakedRootMotionData.GetUnsafePtr(), current, bakedRootMotionSize);
                            }

                            _bakedClipIndexDictionary = new();
                            _bakedAnimationClips = new();
                            for (int i = 0; i < _serializedClips.Count; i++)
                            {
                                AnimationClip clip = _serializedClips[i];
                                int iid = clip != null ? clip.GetInstanceIDGPUI() : 0;
                                _bakedClipIndexDictionary[iid] = i;
                                _bakedAnimationClips[iid] = clip;
                            }
                            _isLoadedSerializedClipData = true;
                        }
                        else
                            Debug.LogError(GPUIConstants.LOG_PREFIX + name + ": Animation data loading aborted! The saved bone count does not match the current bone count.");
                    }
                }
                if (_isLoadedSerializedClipData)
                    _bakedBoneDataModified = true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Dispose();
            }

            Profiler.EndSample();
#if GPUIPRO_DEVMODE
            float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            Debug.Log(GPUIConstants.LOG_PREFIX + GPUIConstants.LOG_PREFIX_DEV + name + " loaded " + _bakedClipCount + " pre-baked animation clips! Time: " + elapsedMs.ToString("0.00") + "ms", this);
#endif
        }

#if UNITY_EDITOR
        public void Editor_ClearSerializedClipData()
        {
            if (Application.isPlaying)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not clear serialized clip data at runtime!");
                return;
            }
            _serializedClips?.Clear();
            if (_serializedClipData != null)
            {
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(_serializedClipData));
                _serializedClipData = null;
            }
            EditorUtility.SetDirty(this);
        }

        private void Editor_AddSerializedClips(IEnumerable<AnimationClip> animationClips)
        {
            _serializedClips ??= new();
            for (int i = 0; i < _serializedClips.Count; i++)
            {
                if (_serializedClips[i] == null)
                {
                    _serializedClips.RemoveAt(i);
                    i--;
                }
            }
            foreach (var clip in animationClips)
            {
                if (clip != null && !_serializedClips.Contains(clip))
                    _serializedClips.Add(clip);
            }
        }

        public bool Editor_BakeSerializedClips(GPUICrowdInstance crowdInstance)
        {
            if (Application.isPlaying)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not bake serialized clip data at runtime!");
                return false;
            }
            if (crowdInstance.Rig != this)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Given crowdInstance has a different rig assigned!");
                return false;
            }
            if (!AssetDatabase.Contains(this))
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Rig is not saved as an asset!");
                return false;
            }
            if (editor_clipsToSerialize == null || editor_clipsToSerialize.Count == 0)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Given editor_clipListToSerialize is null or empty!");
                return false;
            }
            if (bones == null || bones.Count == 0)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Rig has no bones defined!");
                return false;
            }

            Editor_ClearSerializedClipData();
            Editor_AddSerializedClips(editor_clipsToSerialize);
            if (_serializedClips == null || _serializedClips.Count == 0)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Clip list is empty!");
                return false;
            }
            editor_clipsToSerialize.Clear();
            editor_clipsToSerialize.AddRange(_serializedClips);

            Dispose(); // Clear existing baked data
            GPUICrowdUtility.BakeAnimationClips(crowdInstance, GPUICrowdConstants.DEFAULT_CLIP_FRAME_RATE, _serializedClips);
            if (!_bakedBoneData.IsCreated || _bakedBoneData.Length == 0)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "No baked animation data!");
                return false;
            }
            _rigDataVersion++;
            Editor_SaveSerializedClips(crowdInstance.name + "_GPUIBakedClipData");

            return true;
        }

        private void Editor_SaveSerializedClips(string fileName)
        {
            string rigPath = AssetDatabase.GetAssetPath(this);
            string folderPath = GPUIUtility.GetFolderPath(rigPath);
            if (string.IsNullOrEmpty(folderPath))
            {
                Debug.LogError("Can not save serialized clips! Folder path is null.");
                return;
            }

            GPUISerializedBakedClipDataHeader header = new GPUISerializedBakedClipDataHeader()
            {
                rigDataVersion = _rigDataVersion,
                numBakedClips = _serializedClips.Count,
                boneCount = bones.Count,
                bakedClipDataArrayLength = _bakedClipDataArray.Length,
                bakedBoneDataArrayLength = _bakedBoneData.Length,
                bakedRootMotionDataArrayLength = _bakedRootMotionData.Length
            };

            int headerSize = UnsafeUtility.SizeOf<GPUISerializedBakedClipDataHeader>();
            int bakedClipDataSize = header.bakedClipDataArrayLength * GPUICrowdBakedClipData.STRIDE;
            int bakedBoneDataSize = header.bakedBoneDataArrayLength * GPUITransformData.STRIDE;
            int bakedRootMotionSize = header.bakedRootMotionDataArrayLength * GPUICrowdRootMotion.STRIDE;

            byte[] bytes = new byte[headerSize + bakedClipDataSize + bakedBoneDataSize + bakedRootMotionSize];
            unsafe
            {
                fixed (byte* ptr = bytes)
                {
                    byte* current = ptr;

                    UnsafeUtility.MemCpy(current, &header, headerSize);
                    current += headerSize;

                    UnsafeUtility.MemCpy(current, _bakedClipDataArray.GetUnsafeReadOnlyPtr(), bakedClipDataSize);
                    current += bakedClipDataSize;

                    UnsafeUtility.MemCpy(current, _bakedBoneData.GetUnsafeReadOnlyPtr(), bakedBoneDataSize);
                    current += bakedBoneDataSize;

                    if (bakedRootMotionSize > 0)
                    {
                        UnsafeUtility.MemCpy(current, _bakedRootMotionData.GetUnsafeReadOnlyPtr(), bakedRootMotionSize);
                    }
                }
            }

            string savePath = folderPath + fileName + ".bytes";
            File.WriteAllBytes(savePath, bytes);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _serializedClipData = AssetDatabase.LoadAssetAtPath<TextAsset>(savePath);
            EditorUtility.SetDirty(this);
            if (_serializedClipData != null)
                Debug.Log(GPUIConstants.LOG_PREFIX + $"Saved {GPUIUtility.FormatBytesToString(bytes.Length)} of clip data to {savePath}", _serializedClipData);
            else
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Animation data could not be saved!");
        }
#endif // UNITY_EDITOR
        #endregion SerializedBakedClipData
    }

    [Serializable]
    public class GPUICrowdBone
    {
        public string boneName;
        public string fullPath;

        public GPUICrowdBone(string fullPath, string boneName)
        {
            this.fullPath = fullPath;
            this.boneName = boneName;
        }
    }

    [Serializable]
    public class GPUICrowdSkinnedMeshData
    {
        public Mesh skinnedMesh;
        public int[] boneIndexes;
        public int bindPoseIndex;
        [NonSerialized]
        internal List<Vector4> _vertexBoneData;

        internal void GenerateVertexBoneData()
        {
            if (_vertexBoneData != null && _vertexBoneData.Count > 0)
                return;
            _vertexBoneData = GPUICrowdUtility.GenerateVertexBoneData(skinnedMesh, this);
        }
    }

    [Serializable]
    public class GPUICrowdBindPoseData
    {
        public Matrix4x4[] bindPoses;

        public GPUITransformData[] GetInverseTransformDataArray()
        {
            int count = bindPoses.Length;
            GPUITransformData[] result = new GPUITransformData[count];
            GPUITransformData transformData = GPUIConstants.TRANSFORM_DATA_IDENTITY;
            for (int i = 0; i < count; i++)
            {
                Matrix4x4 bindPose = bindPoses[i];
                if (bindPose.m33 == 0f)
                    bindPose = GPUIConstants.IDENTITY_Matrix4x4;
                transformData.SetFromMatrix(bindPose.inverse);
                result[i] = transformData;
            }
            return result;
        }

        public bool IsMatchingBindPose(Matrix4x4[] otherBindPoses)
        {
            if (bindPoses == null || otherBindPoses == null || bindPoses.Length != otherBindPoses.Length)
                return false;

            Matrix4x4 zeroMatrix = Matrix4x4.zero;
            // Manually compare the array elements
            for (int i = 0; i < bindPoses.Length; i++)
            {
                if (bindPoses[i].EqualsMatrix4x4(zeroMatrix) || otherBindPoses[i].EqualsMatrix4x4(zeroMatrix)) // Skip unset matrices
                    continue;
                if (!bindPoses[i].EqualsMatrix4x4(otherBindPoses[i]))
                    return false;
            }

            return true;
        }

        internal void SetUnsetBindPoses(Matrix4x4[] otherBindPoses)
        {
            Matrix4x4 zeroMatrix = Matrix4x4.zero;
            for (int i = 0; i < bindPoses.Length; i++)
            {
                if (bindPoses[i].EqualsMatrix4x4(zeroMatrix))
                    bindPoses[i] = otherBindPoses[i];
            }
        }
    }

    [Serializable]
    public struct GPUICrowdBakedClipData
    {
        public float clipLength;
        public int clipFrameCount;
        public int bakedBoneDataIndex;
        public int bakedRootMotionIndex;
        /// <summary>
        /// 1 -> looping, -1 -> not looping
        /// </summary>
        public int isLoopingMultiplier;

        public const int STRIDE = 20;

        public bool IsLooping => isLoopingMultiplier > 0;
    }

    [Serializable]
    public struct GPUICrowdRootMotion
    {
        private const float ROOT_MOTION_TOLERANCE = 0.001f;

        public float3 position;
        public quaternion rotation;
        /// <summary>
        /// 0 => No motion, 1 => Position Only, 2 => Position and Rotation
        /// </summary>
        public int motionType;

        public const int STRIDE = 32;

        public void SetMotionType()
        {
            if (Quaternion.Angle(rotation, quaternion.identity) > ROOT_MOTION_TOLERANCE)
                motionType = 2;
            else if (math.distance(position, float3.zero) > ROOT_MOTION_TOLERANCE)
                motionType = 1;
            else
                motionType = 0;
        }
    }

    public enum GPUICrowdSkinWeights
    {
        FourBones = 0,
        TwoBones = 2,
        OneBone = 1,
    }
}
