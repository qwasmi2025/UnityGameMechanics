// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GPUInstancerPro.TerrainModule
{
    [ExecuteInEditMode]
    [DefaultExecutionOrder(200)]
#if !UNITY_6000_3_0 && !GPUIPRO_NO_HELPURL
    [HelpURL("https://wiki.gurbu.com/index.php?title=GPU_Instancer_Pro:GettingStarted#The_Tree_Manager")]
#endif
    public class GPUITreeManager : GPUITerrainManager<GPUITreePrototypeData>
    {
        #region Serialized Properties
        [SerializeField]
        internal bool _enableTreeInstanceColors;
        [SerializeField]
        internal bool _autoGenerateBillboards = true;
        [SerializeField]
        internal bool _isPersistentTreeDataBuffer = false;
        #endregion Serialized Properties

        #region Runtime Properties
        [NonSerialized]
        private bool _requireUpdate;
        [NonSerialized]
        private int[] _treeInstanceCounts;
        [NonSerialized]
        private GPUITransformBufferData[] _treeTransformBuffers;
        [NonSerialized]
        private int[] _treeTransformBufferStartIndexes;
        [NonSerialized]
        private GPUIDataBuffer<uint> _counterDataBuffer;
        [NonSerialized]
        private bool _reloadTreeInstances;
        [NonSerialized]
        private GraphicsBuffer _treeDataBuffer;
        [NonSerialized]
        private int _lastTreeDataBufferUpdateFrame;

        [NonSerialized]
        private List<GPUITreeTerrainData> _batchedTreeTerrainData;
        [NonSerialized]
        private GraphicsBuffer _batchedTreeTerrainDataBuffer;
        [NonSerialized]
        private List<GPUITerrain> _batchedTerrains;
        [NonSerialized]
        private List<GPUITerrain> _unbatchedTerrains;
        [NonSerialized]
        private uint[] _batchedPrototypeIndexes;

        private const int ERROR_CODE_ADDITION = 500;
        private static readonly List<string> TREE_INSTANCE_COLORS_SHADER_KEYWORDS = new List<string>() { GPUITerrainConstants.Kw_GPUI_TREE_INSTANCE_COLOR };
        private static List<int> TERRAIN_TREE_PROTOTYPE_INDEXES = new List<int>();
        private static readonly int TREE_DATA_BUFFER_DISPOSE_FRAME_COUNT = 100;
        #endregion Runtime Properties

        #region MonoBehaviour Methods

        #endregion MonoBehaviour Methods

        #region Initialize/Dispose

        public override bool IsValid(bool logError = true)
        {
            if (!base.IsValid(logError))
                return false;

            bool hasTerrainPrototype = false;
            int terrainCount = GetTerrainCount();
            for (int t = 0; t < terrainCount; t++)
            {
                GPUITerrain gpuiTerrain = GetTerrain(t);
                if (gpuiTerrain != null && gpuiTerrain.TreePrototypes != null && gpuiTerrain.TreePrototypes.Length > 0)
                {
                    hasTerrainPrototype = true;
                    break;
                }
            }
            if (!hasTerrainPrototype)
            {
                errorCode = -ERROR_CODE_ADDITION - 2; // No tree prototypes on the terrain
                return false;
            }

            return true;
        }

        public override void Initialize()
        {
            base.Initialize();

            int prototypeCount = _prototypes.Length;
            _treeInstanceCounts = new int[prototypeCount];
            _treeTransformBuffers = new GPUITransformBufferData[prototypeCount];
            _treeTransformBufferStartIndexes = new int[prototypeCount];

            _counterDataBuffer = new("Tree Counter Buffer", prototypeCount);

            _batchedTreeTerrainData = new();
            _batchedTerrains = new();
            _unbatchedTerrains = new();

            GPUIRenderingSystem.Instance.OnPreCull -= UpdateTreeMatrices;
            GPUIRenderingSystem.Instance.OnPreCull += UpdateTreeMatrices;

            if (GPUITerrain._terrainsSearchingForTreeManager != null)
            {
                AddTerrains(GPUITerrain._terrainsSearchingForTreeManager);
                GPUITerrain._terrainsSearchingForTreeManager.Clear();
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            _treeInstanceCounts = null;
            _treeTransformBuffers = null;
            _treeTransformBufferStartIndexes = null;

            _batchedTreeTerrainData = null;
            _batchedTerrains = null;
            _unbatchedTerrains = null;
            _batchedPrototypeIndexes = null;

            if (_counterDataBuffer != null)
            {
                _counterDataBuffer.Dispose();
                _counterDataBuffer = null;
            }

            if (GPUIRenderingSystem.IsActive) 
                GPUIRenderingSystem.Instance.OnPreCull -= UpdateTreeMatrices;

            if (_treeDataBuffer != null)
            {
                _treeDataBuffer.Dispose();
                _treeDataBuffer = null;
            }

            if (_batchedTreeTerrainDataBuffer != null)
            {
                _batchedTreeTerrainDataBuffer.Dispose();
                _batchedTreeTerrainDataBuffer = null;
            }
        }

        #endregion Initialize/Dispose

        #region UpdateTreeMatrices

        private void UpdateTreeMatrices(GPUICameraData cameraData)
        {
            UpdateTreeMatrices();
        }

        private void UpdateTreeMatrices()
        {
            if (!GPUIRenderingSystem.IsActive || !IsInitialized)
            {
                if (_treeDataBuffer != null)
                {
                    _treeDataBuffer.Dispose();
                    _treeDataBuffer = null;
                }
                return;
            }
            bool requireSetTreeData = true;
            if (!_requireUpdate)
            {
                CheckPendingTerrainTransformChanges();

                if (!_requireUpdate)
                {
                    if (!_isPersistentTreeDataBuffer && _treeDataBuffer != null && Time.frameCount - _lastTreeDataBufferUpdateFrame > TREE_DATA_BUFFER_DISPOSE_FRAME_COUNT)
                    {
                        _treeDataBuffer.Dispose();
                        _treeDataBuffer = null;
                    }
                    return;
                }
                requireSetTreeData = false;
            }
            _hasPendingTerrainTransformChanges = false;
            _requireUpdate = false;

            int prototypeCount = _prototypes.Length;
            if (prototypeCount == 0)
                return;

            Profiler.BeginSample("GPUITreeManager.UpdateTreeMatrices");

            if (_treeInstanceCounts.Length != prototypeCount)
                _treeInstanceCounts = new int[prototypeCount];
            int terrainCount = GetActiveTerrainCount();
            int counterBufferSize = prototypeCount + terrainCount * prototypeCount;
            if (_counterDataBuffer.Length < counterBufferSize)
                _counterDataBuffer.Resize(counterBufferSize);
            for (int i = 0; i < prototypeCount; i++)
                _counterDataBuffer[i] = 0; // to make sure counter is set to 0
            int maxTreeDataSize = 0;
            int batchTreeInstanceCount = 0;
            _batchedTreeTerrainData.Clear();
            _batchedTerrains.Clear();
            _unbatchedTerrains.Clear();
            if (_batchedPrototypeIndexes == null || _batchedPrototypeIndexes.Length != prototypeCount)
                _batchedPrototypeIndexes = new uint[prototypeCount];
            bool hasBatchedMatrixOffset = false;
            int counterIndex = prototypeCount;
            foreach (GPUITerrain gpuiTerrain in GetActiveTerrainValues())
            {
                if (!IsRenderTerrainTrees(gpuiTerrain)) continue;
                int[] prototypeIndexes = GetTerrainPrototypeIndexes(gpuiTerrain);
                if (prototypeIndexes == null) continue;
                TreeInstance[] treeData = gpuiTerrain.GetTreeInstances(_reloadTreeInstances);
                int treeDataSize = treeData.Length;
                if (treeDataSize == 0) continue;
                maxTreeDataSize = Mathf.Max(maxTreeDataSize, treeDataSize);

                int[] terrainTreeInstanceCounts = gpuiTerrain.TreeInstanceCounts;
                int terrainTreePrototypeCount = prototypeIndexes.Length;
                if (terrainTreeInstanceCounts == null || terrainTreeInstanceCounts.Length != terrainTreePrototypeCount)
                    gpuiTerrain.CalculateTreeInstanceCounts();
                for (int i = 0; i < terrainTreePrototypeCount; i++)
                {
                    int prototypeIndex = prototypeIndexes[i];
                    _treeInstanceCounts[prototypeIndex] += terrainTreeInstanceCounts[i];
                }

                if (terrainCount > 2 && !_enableTreeInstanceColors && gpuiTerrain.terrainHolesSampleMode != GPUITerrain.GPUITerrainHolesSampleMode.Runtime)
                {
                    Matrix4x4 rotationMatrix = GPUIConstants.IDENTITY_Matrix4x4;
                    if (gpuiTerrain.HasRotationSupport())
                    {
                        rotationMatrix = gpuiTerrain.GetRotationMatrix();
                        if (!hasBatchedMatrixOffset)
                            hasBatchedMatrixOffset = !rotationMatrix.EqualsMatrix4x4(GPUIConstants.IDENTITY_Matrix4x4);
                    }

                    _batchedTreeTerrainData.Add(new GPUITreeTerrainData()
                    {
                        terrainSize = gpuiTerrain.GetSize(),
                        terrainPosition = gpuiTerrain.GetPosition(),
                        treeDataStartIndex = batchTreeInstanceCount,
                        treeCount = treeDataSize,
                        gpuiTransformOffset = rotationMatrix,
                    });
                    batchTreeInstanceCount += treeDataSize;

                    for (uint i = 0; i < prototypeCount; i++)
                    {
                        _batchedPrototypeIndexes[i] = uint.MaxValue;
                        for (uint j = 0; j < prototypeIndexes.Length; j++)
                        {
                            if (prototypeIndexes[j] == i)
                            {
                                _batchedPrototypeIndexes[i] = j;
                                break;
                            }
                        }
                    }
                    _counterDataBuffer.SetData(counterIndex, 0, prototypeCount, _batchedPrototypeIndexes);
                    counterIndex += prototypeCount;
                    _batchedTerrains.Add(gpuiTerrain);
                }
                else
                {
                    requireSetTreeData = true;
                    _unbatchedTerrains.Add(gpuiTerrain);
                }
            }
            maxTreeDataSize = Mathf.Max(maxTreeDataSize, batchTreeInstanceCount);
            _reloadTreeInstances = false;

            if (_treeTransformBuffers.Length != prototypeCount)
                _treeTransformBuffers = new GPUITransformBufferData[prototypeCount];
            if (_treeTransformBufferStartIndexes.Length != prototypeCount)
                _treeTransformBufferStartIndexes = new int[prototypeCount];
            for (int i = 0; i < prototypeCount; i++)
            {
                if (!_prototypes[i].isEnabled || _runtimeRenderKeys[i] == 0)
                {
                    _treeTransformBuffers[i] = null;
                    continue;
                }
                int instanceCount = _treeInstanceCounts[i];
                int currentBufferSize = GPUIRenderingSystem.GetBufferSize(_runtimeRenderKeys[i]);
                if (currentBufferSize < instanceCount || currentBufferSize > instanceCount * 2)
                    GPUIRenderingSystem.SetBufferSize(_runtimeRenderKeys[i], instanceCount, false);
                GPUIRenderingSystem.SetInstanceCount(_runtimeRenderKeys[i], instanceCount);
                _prototypeDataArray[i]._treeInstanceDataBuffer?.Release();
                if (instanceCount > 0)
                {
                    if (_enableTreeInstanceColors)
                        _prototypeDataArray[i]._treeInstanceDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, instanceCount, 4 * 4);
                    if (GPUIRenderingSystem.TryGetTransformBufferData(_runtimeRenderKeys[i], out _treeTransformBuffers[i], out _treeTransformBufferStartIndexes[i], out _))
                        _treeTransformBuffers[i].GetTransformBuffer()?.CompleteAsyncRequests();
                    else
                        Debug.LogError(GPUIConstants.LOG_PREFIX + "Tree Manager can not find transform buffer for prototype: " + _prototypes[i]);
                }
                else
                    _treeTransformBuffers[i] = null;
            }

            ComputeShader cs = GPUITerrainConstants.CS_TerrainTreeGenerator;
            if (_enableTreeInstanceColors)
            {
                cs.EnableKeyword(GPUITerrainConstants.Kw_GPUI_TREE_INSTANCE_COLOR);

                for (int i = 0; i < prototypeCount; i++)
                {
                    int instanceCount = _treeInstanceCounts[i];
                    if (instanceCount > 0)
                    {
                        if (GPUIRenderingSystem.TryGetRenderSourceGroup(_runtimeRenderKeys[i], out GPUIRenderSourceGroup rsg))
                            rsg.AddMaterialPropertyOverride(GPUITerrainConstants.PROP_gpuiTreeInstanceDataBuffer, _prototypeDataArray[i]._treeInstanceDataBuffer, -1, -1, true);
                    }
                }

                cs.SetBool(GPUIConstants.PROP_isLinearSpace, QualitySettings.activeColorSpace == ColorSpace.Linear);
            }
            else
                cs.DisableKeyword(GPUITerrainConstants.Kw_GPUI_TREE_INSTANCE_COLOR);

            if (maxTreeDataSize > 0)
            {
                if (_treeDataBuffer != null && _treeDataBuffer.count < maxTreeDataSize)
                {
                    _treeDataBuffer.Dispose();
                    _treeDataBuffer = null;
                }

                if (_treeDataBuffer == null)
                {
                    _treeDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxTreeDataSize, System.Runtime.InteropServices.Marshal.SizeOf(typeof(TreeInstance)));
                    requireSetTreeData = true;
                }

                TERRAIN_TREE_PROTOTYPE_INDEXES ??= new List<int>();

                _counterDataBuffer.UpdateBufferData();
                #region Batched
                if (_batchedTreeTerrainData.Count > 0)
                {
                    ComputeShader csBatched = GPUITerrainConstants.CS_TerrainTreeGeneratorBatched;
                    if (hasBatchedMatrixOffset)
                        csBatched.EnableKeyword(GPUIConstants.Kw_GPUI_TRANSFORM_OFFSET);
                    else
                        csBatched.DisableKeyword(GPUIConstants.Kw_GPUI_TRANSFORM_OFFSET);

                    if (requireSetTreeData)
                    {
                        for (int i = 0; i < _batchedTerrains.Count; i++)
                        {
                            var ttd = _batchedTreeTerrainData[i];
                            _treeDataBuffer.SetData(_batchedTerrains[i].GetTreeInstances(), 0, ttd.treeDataStartIndex, ttd.treeCount);
                        }
                    }

                    int terrainDataCount = _batchedTreeTerrainData.Count;

                    if (_batchedTreeTerrainDataBuffer != null && _batchedTreeTerrainDataBuffer.count < terrainDataCount)
                    {
                        _batchedTreeTerrainDataBuffer.Dispose();
                        _batchedTreeTerrainDataBuffer = null;
                    }
                    if (_batchedTreeTerrainDataBuffer == null)
                    {
                        _batchedTreeTerrainDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, terrainDataCount, System.Runtime.InteropServices.Marshal.SizeOf(typeof(GPUITreeTerrainData)));
                    }
                    _batchedTreeTerrainDataBuffer.SetData(_batchedTreeTerrainData);

                    for (int p = 0; p < prototypeCount; p++)
                    {
                        GPUITransformBufferData transformBufferData = _treeTransformBuffers[p];
                        if (transformBufferData == null)
                            continue;
                        GPUIShaderBuffer transformShaderBuffer = transformBufferData.GetTransformBuffer();
                        if (transformShaderBuffer == null || transformShaderBuffer.Buffer == null)
                            continue;

                        int transformBufferStartIndex = _treeTransformBufferStartIndexes[p];
                        var prototype = _prototypes[p];
                        var prototypeData = _prototypeDataArray[p];

                        csBatched.SetBuffer(0, GPUIConstants.PROP_gpuiTransformBuffer, transformShaderBuffer.Buffer);
                        csBatched.SetBuffer(0, GPUIConstants.PROP_counterBuffer, _counterDataBuffer);
                        csBatched.SetBuffer(0, GPUITerrainConstants.PROP_treeData, _treeDataBuffer);
                        csBatched.SetBuffer(0, GPUITerrainConstants.PROP_terrainDataBuffer, _batchedTreeTerrainDataBuffer);
                        csBatched.SetInt(GPUIConstants.PROP_bufferSize, batchTreeInstanceCount);
                        csBatched.SetInt(GPUITerrainConstants.PROP_terrainDataCount, terrainDataCount);
                        csBatched.SetInt(GPUIConstants.PROP_transformBufferStartIndex, transformBufferStartIndex);
                        csBatched.SetInt(GPUIConstants.PROP_prototypeIndex, p);
                        csBatched.SetInt(GPUIConstants.PROP_prototypeCount, prototypeCount);
                        csBatched.SetVector(GPUITerrainConstants.PROP_prefabScale, prototypeData.isApplyPrefabScale ? prototype.prefabObject.transform.localScale : Vector3.one);
                        csBatched.SetBool(GPUITerrainConstants.PROP_applyRotation, prototypeData.isApplyRotation);
                        csBatched.SetBool(GPUITerrainConstants.PROP_applyHeight, prototypeData.isApplyHeight);

                        csBatched.DispatchXY(0, batchTreeInstanceCount, terrainDataCount);

                        transformBufferData.OnTransformDataModified();
                        transformBufferData.ResetPreviousFrameBuffer();
                    }
                }
                #endregion Batched

                #region Unbatched
                foreach (var gpuiTerrain in _unbatchedTerrains)
                {
                    TreeInstance[] treeInstances = gpuiTerrain.GetTreeInstances();
                    int bufferSize = treeInstances.Length;
                    _treeDataBuffer.SetData(treeInstances);

                    Vector3 terrainSize = gpuiTerrain.GetSize();
                    Vector3 terrainPosition = gpuiTerrain.GetPosition();
                    bool isSampleTerrainHoles = gpuiTerrain.terrainHolesSampleMode == GPUITerrain.GPUITerrainHolesSampleMode.Runtime;
                    Texture holesTexture = gpuiTerrain.GetHolesTexture();

                    bool hasMatrixOffset = false;
                    Matrix4x4 rotationMatrix = GPUIConstants.IDENTITY_Matrix4x4;
                    if (gpuiTerrain.HasRotationSupport())
                    {
                        rotationMatrix = gpuiTerrain.GetRotationMatrix();
                        hasMatrixOffset = !rotationMatrix.EqualsMatrix4x4(GPUIConstants.IDENTITY_Matrix4x4);
                    }

                    for (int p = 0; p < prototypeCount; p++)
                    {
                        GPUITransformBufferData transformBufferData = _treeTransformBuffers[p];
                        if (transformBufferData == null)
                            continue;
                        GPUIShaderBuffer transformShaderBuffer = transformBufferData.GetTransformBuffer();
                        if (transformShaderBuffer == null || transformShaderBuffer.Buffer == null)
                            continue;

                        int transformBufferStartIndex = _treeTransformBufferStartIndexes[p];
                        var prototype = _prototypes[p];
                        var prototypeData = _prototypeDataArray[p];
                        gpuiTerrain.GetTerrainTreePrototypeIndexes(p, ref TERRAIN_TREE_PROTOTYPE_INDEXES);
                        foreach (int terrainPrototypeIndex in TERRAIN_TREE_PROTOTYPE_INDEXES)
                        {
                            cs.SetBuffer(0, GPUIConstants.PROP_gpuiTransformBuffer, transformShaderBuffer.Buffer);
                            cs.SetBuffer(0, GPUITerrainConstants.PROP_treeData, _treeDataBuffer);
                            cs.SetBuffer(0, GPUIConstants.PROP_counterBuffer, _counterDataBuffer);
                            if (_enableTreeInstanceColors)
                                cs.SetBuffer(0, GPUITerrainConstants.PROP_gpuiTreeInstanceDataBuffer, prototypeData._treeInstanceDataBuffer);
                            cs.SetInt(GPUIConstants.PROP_bufferSize, bufferSize);
                            cs.SetInt(GPUIConstants.PROP_transformBufferStartIndex, transformBufferStartIndex);
                            cs.SetInt(GPUIConstants.PROP_prototypeIndex, p);
                            cs.SetInt(GPUITerrainConstants.PROP_terrainPrototypeIndex, terrainPrototypeIndex);
                            cs.SetVector(GPUITerrainConstants.PROP_terrainSize, terrainSize);
                            cs.SetVector(GPUITerrainConstants.PROP_terrainPosition, terrainPosition);
                            cs.SetVector(GPUITerrainConstants.PROP_prefabScale, prototypeData.isApplyPrefabScale ? prototype.prefabObject.transform.localScale : Vector3.one);
                            cs.SetBool(GPUITerrainConstants.PROP_applyRotation, prototypeData.isApplyRotation);
                            cs.SetBool(GPUITerrainConstants.PROP_applyHeight, prototypeData.isApplyHeight);

                            if (isSampleTerrainHoles && holesTexture != null)
                            {
                                cs.EnableKeyword(GPUITerrainConstants.Kw_GPUI_TERRAIN_HOLES);
                                cs.SetTexture(0, GPUITerrainConstants.PROP_terrainHoleTexture, holesTexture);
                            }
                            else
                                cs.DisableKeyword(GPUITerrainConstants.Kw_GPUI_TERRAIN_HOLES);

                            if (hasMatrixOffset)
                            {
                                cs.EnableKeyword(GPUIConstants.Kw_GPUI_TRANSFORM_OFFSET);
                                cs.SetMatrix(GPUIConstants.PROP_gpuiTransformOffset, rotationMatrix);
                            }
                            else
                                cs.DisableKeyword(GPUIConstants.Kw_GPUI_TRANSFORM_OFFSET);

                            cs.DispatchX(0, bufferSize);
                        }

                        transformBufferData.OnTransformDataModified();
                        transformBufferData.ResetPreviousFrameBuffer();
                    }
                }
                #endregion Unbatched

                _lastTreeDataBufferUpdateFrame = Time.frameCount;
            }

            for (int i = 0; i < _treeInstanceCounts.Length; i++)
                _treeInstanceCounts[i] = 0;

            OnLightProbesUpdated();

            Profiler.EndSample();
        }

        private bool IsRenderTerrainTrees(GPUITerrain gpuiTerrain)
        {
            return gpuiTerrain != null && gpuiTerrain.isActiveAndEnabled
#if UNITY_EDITOR
                && !gpuiTerrain.editor_IsDisableTreeRendering
#endif
                ;
        }

        protected override void OnUpdatePerInstanceLightProbes(int prototypeIndex)
        {
            if (GPUIRenderingSystem.TryGetTransformBufferData(_runtimeRenderKeys[prototypeIndex], out var transformBufferData, out int bufferStartIndex, out int bufferSize, false))
            {
                var shaderBuffer = transformBufferData.GetTransformBuffer();
                if (shaderBuffer == null)
                    return;
//#if GPUIPRO_DEVMODE
//                Debug.Log(GPUIConstants.LOG_PREFIX + "TreeManager.OnUpdatePerInstanceLightProbes " + prototypeIndex);
//#endif
                shaderBuffer.CompleteAsyncRequests();
                shaderBuffer.AsyncRequestIntoNativeArray((matrices) =>
                {
//#if GPUIPRO_DEVMODE
//                    Debug.Log(GPUIConstants.LOG_PREFIX + "TreeManager.CalculateInterpolatedLightAndOcclusionProbes " + prototypeIndex);
//#endif
                    transformBufferData.CalculateInterpolatedLightAndOcclusionProbes(matrices, 0, bufferStartIndex, bufferSize);
                    matrices.Dispose();
                });
            }
        }

        #endregion UpdateTreeMatrices

        #region Prototype Changes

        protected override bool AddMissingPrototypesFromTerrain(GPUITerrain gpuiTerrain)
        {
            bool prototypeAdded = false;
            TreePrototype[] treePrototypes = gpuiTerrain.TreePrototypes;
            int[] terrainPrototypeIndexes = GetTerrainPrototypeIndexes(gpuiTerrain);
            for (int i = 0; i < terrainPrototypeIndexes.Length; i++)
            {
                prototypeAdded |= terrainPrototypeIndexes[i] < 0 && AddTreePrototype(treePrototypes[i]) >= 0;
            }

            return prototypeAdded;
        }

        protected override void SetGPUITerrainManager(GPUITerrain gpuiTerrain)
        {
            gpuiTerrain.SetTreeManager(this);
        }


        protected override void RemoveGPUITerrainManager(GPUITerrain gpuiTerrain)
        {
            if (gpuiTerrain.TreeManager == this)
                gpuiTerrain.RemoveTreeManager();
        }

        internal int DetermineTreePrototypeIndex(TreePrototype treePrototype)
        {
            if (_prototypes != null)
            {
                for (int p = 0; p < _prototypes.Length; p++)
                {
                    GPUIPrototype prototype = _prototypes[p];
                    if (treePrototype.prefab == prototype.prefabObject)
                        return p;
                }
            }
            if (_isAutoAddPrototypesBasedOnTerrains)
                _isTerrainsModified = true;
            return -1;
        }

        protected override void DeterminePrototypeIndexes(GPUITerrain gpuiTerrain)
        {
            gpuiTerrain.DetermineTreePrototypeIndexes(this);
        }

        protected override int[] GetTerrainPrototypeIndexes(GPUITerrain gpuiTerrain)
        {
            if (gpuiTerrain.TreePrototypes == null)
                gpuiTerrain.LoadTerrainData();
            if (gpuiTerrain.TreePrototypes != null && (gpuiTerrain.TreePrototypeIndexes == null || gpuiTerrain.TreePrototypes.Length != gpuiTerrain.TreePrototypeIndexes.Length))
                DeterminePrototypeIndexes(gpuiTerrain);
            return gpuiTerrain.TreePrototypeIndexes;
        }

        public int AddTreePrototype(TreePrototype treePrototype)
        {
            if (treePrototype == null || treePrototype.prefab == null)
                return -1;
            if (_prototypes != null)
            {
                for (int i = 0; i < _prototypes.Length; i++)
                {
                    if (_prototypes[i] != null && _prototypes[i].prefabObject == treePrototype.prefab)
                        return i;
                }
            }
            GPUITreePrototypeData treePrototypeData = new(treePrototype);

            int length = 0;
            if (_prototypeDataArray == null)
                _prototypeDataArray = new GPUITreePrototypeData[1];
            else
            {
                length = _prototypeDataArray.Length;
                Array.Resize(ref _prototypeDataArray, length + 1);
            }
            _prototypeDataArray[length] = treePrototypeData;

            GPUIPrototype prototype = new GPUIPrototype(treePrototype.prefab, GetDefaultProfile());
            if (_autoGenerateBillboards && (!treePrototype.prefab.HasComponent<LODGroup>() || treePrototype.prefab.HasComponentInChildren<BillboardRenderer>()))
                prototype.isGenerateBillboard = true;
            int index = AddPrototype(prototype);
            OnNewPrototypeDataCreated(length);
            return index;
        }

        public void RemoveTreePrototypeAtIndex(int index, bool removeFromTerrain)
        {
            if (removeFromTerrain)
            {
                int terrainCount = GetTerrainCount();
                for (int t = 0; t < terrainCount; t++)
                {
                    GPUITerrain gpuiTerrain = GetTerrain(t);
                    if (gpuiTerrain != null)
                        gpuiTerrain.RemoveTreePrototypeAtIndex(index);
                }
            }
            RemovePrototypeAtIndex(index);
        }

        public void AddPrototypeToTerrains(GameObject pickerGameObject, int overwriteIndex)
        {
            int terrainCount = GetTerrainCount();
            for (int t = 0; t < terrainCount; t++)
            {
                GPUITerrain gpuiTerrain = GetTerrain(t);
                if (gpuiTerrain != null)
                    gpuiTerrain.AddTreePrototypeToTerrain(pickerGameObject, overwriteIndex);
            }
        }

        #endregion Prototype Changes

        #region Getters/Setters

        public override void RequireUpdate()
        {
            _requireUpdate = true;
        }

        public void RequireUpdate(bool reloadTreeInstances)
        {
            _reloadTreeInstances = reloadTreeInstances;
            RequireUpdate();
        }

        public override GPUIProfile GetDefaultProfile()
        {
            if (defaultProfile != null)
                return defaultProfile;
            return GPUITerrainConstants.DefaultTreeProfile;
        }

        public override List<string> GetShaderKeywords(int prototypeIndex)
        {
            if (_enableTreeInstanceColors)
                return TREE_INSTANCE_COLORS_SHADER_KEYWORDS;
            return base.GetShaderKeywords(prototypeIndex);
        }

        #endregion Getters/Setters

        public struct GPUITreeTerrainData
        {
            public Vector3 terrainSize;
            public Vector3 terrainPosition;
            public int treeDataStartIndex;
            public int treeCount;
            public Matrix4x4 gpuiTransformOffset;
        }
    }
}