// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GPUInstancerPro.CrowdAnimations
{
    [ExecuteInEditMode]
    [DefaultExecutionOrder(-110)]
    public class GPUICrowdSkinningSystem : GPUISystemExtension
    {
        #region Runtime Properties
        public static GPUICrowdSkinningSystem Instance { get; private set; }
        public static bool IsActive => Instance != null && Instance.IsInitialized;
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// <para>key => Render Source Group Key</para>
        /// <para>value => Crowd Render Source Group Data</para>
        /// </summary>
        public GPUICrowdRSGProvider RenderSourceGroupProvider { get; private set; }

        /// <summary>
        /// <para>key => Render Source Key</para>
        /// <para>value => Crowd Render Source Data</para>
        /// </summary>
        public GPUICrowdRenderSourceProvider RenderSourceProvider { get; private set; }

        /// <summary>
        /// <para>key => Crowd Instance IID</para>
        /// <para>value => Clip Sampler</para>
        /// </summary>
        public GPUICrowdClipSamplerProvider ClipSamplerProvider { get; private set; }

        /// <summary>
        /// <para>key => Prefab Instance ID</para>
        /// <para>value => Rig</para>
        /// </summary>
        private Dictionary<int, GPUICrowdRig> _prefabInstanceIDRigDict;

        /// <summary>
        /// <para>key => GPUIPrefab PrefabID</para>
        /// <para>value => Rig</para>
        /// </summary>
        private Dictionary<int, GPUICrowdRig> _prefabIDRigDict;
        /// <summary>
        /// Contains a list of disposables (e.g. GPUICrowdRig) that will be disposed when this is disposed
        /// </summary>
        private List<IGPUIDisposable> _dependentDisposables;

        /// <summary>
        /// Keeps the animator workflow definitions with their IDs.
        /// </summary>
        private static Dictionary<int, GPUIAnimatorWorkflowBase> _animatorWorkflows;
        private static GPUIAnimatorWorkflowBase[] _animatorWorkflowArray;

        private int _lastPreCullExecutionFrame = -1;
        private int _lastPreRenderExecutionFrame = -1;
        private int _lastPostRenderExecutionFrame = -1;
        private float _lastBoneWriteExecutionTime = 0f;
        #endregion Runtime Properties

        #region MonoBehaviour Methods
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                DestroyInstance();
                return;
            }
            else if (Instance == null)
            {
                Instance = this;
                Initialize();
            }
        }

        private void OnEnable()
        {
            if (Instance == null)
                Instance = this;
            if (CheckIsSingleton())
            {
                Initialize();
#if UNITY_EDITOR
                Editor_HandlePlayModeStates();
#endif
            }
            _animatorWorkflows ??= new();
        }

        private void OnDisable()
        {
            Dispose();
        }
        #endregion MonoBehaviour Methods

        #region Initialize/Dispose
        public static void InitializeSystem()
        {
            GPUIProfile.defaultGPUSkinningProfile = GPUICrowdConstants.DefaultCrowdProfile;
            if (IsActive || !GPUIRuntimeSettings.IsSupportedPlatform()) return;
            if (Instance == null)
            {
                GameObject go = new GameObject();
                Instance = go.AddComponent<GPUICrowdSkinningSystem>();
                if (Instance == null)
                    return;
                go.name = "===GPUI Skinning System [" + Instance.GetInstanceIDGPUI() + "]===";
#if GPUIPRO_DEVMODE
                go.hideFlags = HideFlags.DontSave;
#else
                go.hideFlags = HideFlags.HideAndDontSave;
#endif
            }
            Instance.Initialize();
        }

        private void DestroyInstance()
        {
            gameObject.DestroyGeneric();
        }

        private bool CheckIsSingleton()
        {
            if (Instance == null)
            {
                DestroyInstance();
                return false;
            }
            else if (Instance != this)
            {
                DestroyInstance();
                return false;
            }
            return true;
        }

        private void Initialize()
        {
            if (IsInitialized)
                return;

            if (!GPUIRuntimeSettings.IsSupportedPlatform())
            {
                DestroyInstance();
                return;
            }

            IsInitialized = true;
            _animatorWorkflows ??= new();

            GPUIRenderingSystem.InitializeRenderingSystem();
            GPUIRenderingSystem.Instance.AddRenderingSystemExtension(this);

            _prefabInstanceIDRigDict = new();
            _prefabIDRigDict = new();

            RenderSourceGroupProvider = new();
            RenderSourceGroupProvider.Initialize();

            RenderSourceProvider = new();
            RenderSourceProvider.Initialize();

            ClipSamplerProvider = new();
            ClipSamplerProvider.Initialize();
        }

        public override void Dispose()
        {
            IsInitialized = false;

            if (_dependentDisposables != null)
            {
                foreach (IGPUIDisposable disposable in _dependentDisposables)
                    disposable?.Dispose();
                _dependentDisposables = null;
            }

            _prefabInstanceIDRigDict = null;
            _prefabIDRigDict = null;

            if (RenderSourceGroupProvider != null)
            {
                RenderSourceGroupProvider.Dispose();
                RenderSourceGroupProvider = null;
            }

            if (RenderSourceProvider != null)
            {
                RenderSourceProvider.Dispose();
                RenderSourceProvider = null;
            }

            if (ClipSamplerProvider != null)
            {
                ClipSamplerProvider.Dispose();
                ClipSamplerProvider = null;
            }

            if (GPUIRenderingSystem.IsActive)
                GPUIRenderingSystem.Instance.RemoveRenderingSystemExtension(this);

            _lastPreCullExecutionFrame = -1;
            _lastPreRenderExecutionFrame = -1;
            _lastPostRenderExecutionFrame = -1;
            _lastBoneWriteExecutionTime = 0f;
        }
        #endregion Initialize/Dispose

        #region Rendering System Extension Methods
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AddOnRenderingSystemInitializedListener() => GPUIRenderingSystem.AddOnRenderingSystemInitializedListener(InitializeSystem);

        public override void OnCreatedRenderSourceGroup(GPUIRenderSourceGroup renderSourceGroup)
        {
            if (!GPUIRuntimeSettings.Instance.DisableShaderBuffers)
                renderSourceGroup.AddMaterialPropertyOverride(GPUICrowdConstants.PROP_shaderBoneBuffer, GPUIRenderingSystem.Instance.DummyGraphicsBuffer, -1, -1, true); // Set dummy buffer to avoid binding warnings.
            if (!IsInitialized || !Application.isPlaying)
                return;
            GPUILODGroupData lodGroupData = renderSourceGroup.LODGroupData;
            // Check if this is a skinned mesh renderer
            if (lodGroupData == null || lodGroupData.prototype == null || lodGroupData.prototype.prefabObject == null || !lodGroupData.HasSkinning || !lodGroupData.prototype.prefabObject.TryGetComponent(out GPUICrowdInstance crowdInstance))
                return;

            GPUICrowdRig crowdRig = crowdInstance.LoadRig(true);
            crowdRig.LoadSerializedClipDataToBuffers();
            SkinnedMeshRenderer[] skinnedMeshRenderers = crowdInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true); // Re-add the SkinnedMeshRenderers in case there are new ones with different meshes that is sharing the same rig data with a previously generated one.
            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr.sharedMesh != null)
                    crowdRig.AddSkinnedMesh(crowdInstance.transform, smr);
            }
            if (crowdRig.bones.Count == 0)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not load bone transforms for Crowd prototype: " + crowdInstance.name, crowdInstance);
                return;
            }
            crowdRig.GenerateVertexBoneData();

            GPUICrowdRenderSourceGroup crowdRSG = new GPUICrowdRenderSourceGroup(renderSourceGroup, crowdRig)
            {
                bounds = lodGroupData.bounds,
                defaultAnimatorWorkflowID = crowdInstance._animatorWorkflowID,
                crowdInstanceDataBuffer = new("CrowdInstanceBuffer-" + renderSourceGroup.Key)
            };
            RenderSourceGroupProvider.AddOrSet(renderSourceGroup.Key, crowdRSG);
#if GPUIPRO_DEVMODE
            Debug.Log(GPUIConstants.LOG_PREFIX + GPUIConstants.LOG_PREFIX_DEV + "Created Crowd RSG data for: " + crowdInstance.name + " with key: " + renderSourceGroup.Key, crowdInstance);
#endif
            GPUICrowdUtility.SetBindPoseBufferData(crowdRSG);
        }

        public override void OnRemovedRenderSourceGroup(int renderSourceGroupKey)
        {
            if (!IsInitialized)
                return;
            RenderSourceGroupProvider.Remove(renderSourceGroupKey);
        }

        public override void OnRenderSourceGroupBufferSizeChanged(GPUIRenderSourceGroup renderSourceGroup)
        {
            if (!IsInitialized || !RenderSourceGroupProvider.TryGetData(renderSourceGroup.Key, out var crowdRSG))
                return;
            crowdRSG.ApplyRenderSourceGroupBufferSizeChanges();
        }

        public override void OnRemovedRenderSource(int rendererKey)
        {
            if (!IsInitialized)
                return;
            RenderSourceProvider.Remove(rendererKey);
        }

        public override void OnRenderSourceBufferSizeChanged(GPUIRenderSource renderSource, int previousBufferSize)
        {
            if (!IsInitialized || !RenderSourceProvider.TryGetData(renderSource.Key, out var crowdRSData))
                return;
            crowdRSData.ApplyRenderSourceBufferSizeChanges();
        }

        public override void ExecuteOnPreCull(GPUICameraData cameraData)
        {
            if (!IsInitialized || RenderSourceGroupProvider.Count == 0 || !Application.isPlaying) return;

            ClipSamplerProvider.DestroyClipSamplers();

            // Check frame count to avoid running the same process for multiple cameras
            int frameCount = Time.frameCount;
            if (frameCount == _lastPreCullExecutionFrame)
                return;
            _lastPreCullExecutionFrame = frameCount;

            Profiler.BeginSample("GPUICrowdSkinningSystem.ProcessCrowdInstances");
            foreach (var crowdRenderSource in RenderSourceProvider.Values)
                crowdRenderSource.ProcessCrowdInstances();
            Profiler.EndSample();

            Profiler.BeginSample("GPUICrowdSkinningSystem.ExecuteOnPreCull");
            var animatorWorkflows = _animatorWorkflows.Values;
            foreach (var aw in animatorWorkflows)
                aw.ExecuteOnPreCull();
            Profiler.EndSample();

            float currentTime = Time.time;
            Profiler.BeginSample("GPUICrowdSkinningSystem.WriteToBoneTransforms");
            foreach (var crowdRenderSource in RenderSourceProvider.Values)
                GPUICrowdUtility.WriteToBoneTransforms(crowdRenderSource, currentTime - _lastBoneWriteExecutionTime);
            _lastBoneWriteExecutionTime = currentTime;
            Profiler.EndSample();
        }

        public override void ExecuteOnPreRender(GPUICameraData cameraData)
        {
            if (!IsInitialized || RenderSourceGroupProvider.Count == 0) return;

            // Check frame count to avoid running the same process for multiple cameras
            int frameCount = Time.frameCount;
            if (frameCount == _lastPreRenderExecutionFrame)
                return;
            _lastPreRenderExecutionFrame = frameCount;

            if (Application.isPlaying)
            {
                Profiler.BeginSample("GPUICrowdSkinningSystem.ExecuteOnPreGPUAnimator");
                foreach (var aw in _animatorWorkflows.Values)
                    aw.ExecuteOnPreGPUAnimator();
                Profiler.EndSample();
            }

            Profiler.BeginSample("GPUICrowdSkinningSystem.ExecuteCrowdAnimator");
            RenderSourceGroupProvider.ExecuteCrowdAnimator();
            Profiler.EndSample();

            if (Application.isPlaying)
            {
                Profiler.BeginSample("GPUICrowdSkinningSystem.ReadFromBoneTransforms");
                foreach (var crowdRenderSource in RenderSourceProvider.Values)
                    GPUICrowdUtility.ReadFromBoneTransforms(crowdRenderSource);
                Profiler.EndSample();

                Profiler.BeginSample("GPUICrowdSkinningSystem.ExecuteOnPreRender");
                foreach (var aw in _animatorWorkflows.Values)
                    aw.ExecuteOnPreRender();
                Profiler.EndSample();
            }

            Profiler.BeginSample("GPUICrowdSkinningSystem.SetMaterialBuffers");
            foreach (var crowdRSG in RenderSourceGroupProvider.Values)
            {
                if (crowdRSG.boneDataBuffer == null /*|| crowdRSG.shaderBoneBuffer == null || crowdRSG.shaderBoneBuffer.Buffer == null*/)
                    continue;
                GPUICrowdUtility.CopyBoneDataBufferToMatrixBuffer(crowdRSG);
            }
            Profiler.EndSample();
        }

        public override void ExecuteOnPostRender(GPUICameraData cameraData)
        {
            if (!IsInitialized || RenderSourceGroupProvider.Count == 0 || !Application.isPlaying) return;

            // Check frame count to avoid running the same process for multiple cameras
            int frameCount = Time.frameCount;
            if (frameCount == _lastPostRenderExecutionFrame)
                return;
            _lastPostRenderExecutionFrame = frameCount;
        }
        #endregion Rendering System Extension Methods

        #region Crowd Instance Methods
        public static GPUICrowdRig GetCrowdInstanceRig(GPUICrowdInstance crowdInstance, bool createIfNull)
        {
            InitializeSystem();
            if (crowdInstance._crowdRig != null)
                return crowdInstance._crowdRig;

            GPUICrowdRig result;

            int prefabID = 0;
            if (crowdInstance._hasPrefabComponent)
            {
                prefabID = crowdInstance.PrefabComponent.GetPrefabID();
                if (Instance._prefabIDRigDict.TryGetValue(prefabID, out result) && result != null)
                    return result;
            }

            int prefabInstanceID = crowdInstance.gameObject.GetInstanceIDGPUI();
            if (Instance._prefabInstanceIDRigDict.TryGetValue(prefabInstanceID, out result) && result != null)
                return result;

            if (createIfNull)
            {
                result = GPUICrowdUtility.CreateRig(crowdInstance.gameObject);
                Instance._prefabInstanceIDRigDict[prefabInstanceID] = result;
                if (prefabID != 0)
                    Instance._prefabIDRigDict[prefabID] = result;
            }

            return result;
        }

        public static void OnCrowdInstanceRigSet(GPUICrowdInstance crowdInstance)
        {
            InitializeSystem();

            if (crowdInstance.PrefabComponent != null)
            {
                if (crowdInstance._crowdRig == null)
                    Instance._prefabIDRigDict.Remove(crowdInstance.PrefabComponent.GetPrefabID());
                else
                    Instance._prefabIDRigDict[crowdInstance.PrefabComponent.GetPrefabID()] = crowdInstance._crowdRig;
            }

            if (crowdInstance._crowdRig == null)
                Instance._prefabInstanceIDRigDict.Remove(crowdInstance.gameObject.GetInstanceIDGPUI());
            else
                Instance._prefabInstanceIDRigDict[crowdInstance.gameObject.GetInstanceIDGPUI()] = crowdInstance._crowdRig;
        }

        public void ApplySkinWeightsKeywords()
        {
            foreach (var crowdRSG in RenderSourceGroupProvider)
                crowdRSG.Value.ApplySkinWeightsKeywords();
        }

        internal void AddDependentDisposable(IGPUIDisposable gpuiDisposable)
        {
            _dependentDisposables ??= new List<IGPUIDisposable>();
            if (!_dependentDisposables.Contains(gpuiDisposable))
                _dependentDisposables.Add(gpuiDisposable);
        }
        #endregion Crowd Instance Methods

        #region Animator Workflow Methods
        public static void AddAnimatorWorkflow(GPUIAnimatorWorkflowBase animatorWorkflow)
        {
            _animatorWorkflows ??= new();
            _animatorWorkflowArray = null;
            _animatorWorkflows[animatorWorkflow.GetID()] = animatorWorkflow;

            foreach (GPUIAnimatorWorkflowBase aw in _animatorWorkflows.Values)
            {
                foreach (GPUIAnimatorWorkflowBase aw2 in _animatorWorkflows.Values)
                {
                    if (aw != aw2 && aw.GetName() == aw2.GetName())
                    {
                        Debug.LogError(GPUIConstants.LOG_PREFIX + "There are multiple Animator Workflows with the same name! Please assign unique names for each workflow. Type1: " + aw.GetType().Name + " Type2: " + aw2.GetType().Name);
                        return;
                    }
                }
            }
        }

        public static GPUIAnimatorWorkflowBase[] GetAnimatorWorkflows()
        {
            if (_animatorWorkflowArray != null)
                return _animatorWorkflowArray;
            _animatorWorkflows ??= new();
            _animatorWorkflowArray = new GPUIAnimatorWorkflowBase[_animatorWorkflows.Count];
            int i = 0;
            foreach (var item in _animatorWorkflows.Values)
            {
                _animatorWorkflowArray[i] = item;
                i++;
            }
            Array.Sort(_animatorWorkflowArray);
            return _animatorWorkflowArray;
        }

        public static GPUIAnimatorWorkflowBase GetAnimatorWorkflow(int id)
        {
            if (_animatorWorkflows.TryGetValue(id, out GPUIAnimatorWorkflowBase result))
                return result;
            return null;
        }

        public static GPUIAnimatorWorkflowBase GetAnimatorWorkflow(Type workflowType)
        {
            foreach (var item in _animatorWorkflows.Values)
            {
                if (item.GetType() == workflowType)
                    return item;
            }
            return null;
        }

        public static int GetAnimatorWorkflowIDForInstance(int rendererKey, int bufferIndex)
        {
            if (!IsActive)
                return -1;
            if (GPUIRenderingSystem.TryGetRenderSource(rendererKey, out var renderSource))
            {
                int index = renderSource.bufferStartIndex + bufferIndex;
                if (TryGetCrowdRenderSourceGroup(renderSource.renderSourceGroup.Key, out var crowdData) && crowdData.crowdInstanceDataBuffer != null && crowdData.crowdInstanceDataBuffer.Length > index)
                    return crowdData.crowdInstanceDataBuffer[index].animatorWorkflowID;
            }
            return -1;
        }

        /// <summary>
        /// True for workflows that has an internal custom animation system (e.g., Compute Animator).
        /// </summary>
        public static bool HasAnimatorWorkflowInternalAnimator(int id)
        {
            return id < 300 && id >= 200;
        }
        /// <summary>
        /// True for workflows that provide bone transform data directly (e.g., Bone Tracker).
        /// </summary>
        public static bool IsAnimatorWorkflowBoneDataProvider(int id)
        {
            return id >= 100 && id < 200;
        }
        #endregion Animator Workflow Methods

        #region Getter/Setter
        public static bool TryGetCrowdRenderSourceGroupWithRenderKey(int runtimeRenderKey, out GPUICrowdRenderSourceGroup crowdRSG)
        {
            crowdRSG = null;
            if (!IsActive)
                return false;
            return GPUIRenderingSystem.TryGetRenderSource(runtimeRenderKey, out var renderSource) && Instance.RenderSourceGroupProvider.TryGetData(renderSource.renderSourceGroup.Key, out crowdRSG);
        }

        public static bool TryGetCrowdRenderSourceGroup(int renderSourceGroupKey, out GPUICrowdRenderSourceGroup crowdRSG)
        {
            crowdRSG = null;
            if (!IsActive)
                return false;
            return Instance.RenderSourceGroupProvider.TryGetData(renderSourceGroupKey, out crowdRSG);
        }

        public static bool TryGetCrowdRenderSource(int runtimeRenderKey, out GPUICrowdRenderSource crowdRenderSource)
        {
            crowdRenderSource = null;
            if (!IsActive)
                return false;
            return Instance.RenderSourceProvider.TryGetData(runtimeRenderKey, out crowdRenderSource);
        }

        public static bool TryGetOrCreateCrowdRenderSource(int runtimeRenderKey, out GPUICrowdRenderSource crowdRenderSource)
        {
            crowdRenderSource = null;
            if (!IsActive)
                return false;
            return Instance.RenderSourceProvider.TryGetOrCreateData(runtimeRenderKey, out crowdRenderSource);
        }
        #endregion Getter/Setter

        #region Editor Methods
#if UNITY_EDITOR
        private void Editor_HandlePlayModeStates()
        {
            EditorApplication.playModeStateChanged -= Editor_HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += Editor_HandlePlayModeStateChanged;
        }

        private static void Editor_HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (!IsActive)
                return;
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                case PlayModeStateChange.ExitingPlayMode:
                    Instance.Dispose();
                    Instance.Initialize();
                    break;
            }
        }
#endif
        #endregion Editor Methods
    }
}
