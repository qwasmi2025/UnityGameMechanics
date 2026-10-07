// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Profiling;

namespace GPUInstancerPro.PrefabModule
{
    [Serializable]
    public class GPUIPrefabPrototypeData : GPUIPrototypeData
    {
        [SerializeField]
        public bool isAutoUpdateTransformData;
        [SerializeField]
        public GPUIPrefabInstances registeredInstances;

        [SerializeField]
        public bool isAutoEnableDisablePrototype = false;
        [SerializeField]
        public Vector2Int autoEnableDisableMinMaxCounts = new Vector2Int(50, 100);

        #region Runtime Properties
        /// <summary>
        /// Matrix4x4 data for each instance
        /// </summary>
        [NonSerialized]
        private NativeArray<Matrix4x4> _matrixArray;
        /// <summary>
        /// Transform for each instance
        /// </summary>
        [NonSerialized]
        public Transform[] instanceTransforms;
        /// <summary>
        /// TransformAccess for each instance
        /// </summary>
        [NonSerialized]
        public TransformAccessArray transformAccessArray;
        /// <summary>
        /// True when instanceTransforms array is modified, false after changes are applied to the transformAccessArray
        /// </summary>
        [NonSerialized]
        public bool isTransformReferencesModified;
        /// <summary>
        /// Unique prefab ID
        /// </summary>
        [NonSerialized]
        public int prefabID;
        /// <summary>
        /// List of instances to add to buffers
        /// </summary>
        [NonSerialized]
        public List<GPUIPrefab> instancesToAdd;
        /// <summary>
        /// List of instance indexes to remove from buffers
        /// </summary>
        [NonSerialized]
        public SortedSet<int> indexesToRemove;
        /// <summary>
        /// True when a change has been made to transform data
        /// </summary>
        [NonSerialized]
        public bool isMatrixArrayModified;
        /// <summary>
        /// Number of registered instances for this prototype
        /// </summary>
        [NonSerialized]
        internal int instanceCount;
        /// <summary>
        /// True if there are optional renderers on the prefab.
        /// </summary>
        internal bool hasOptionalRenderers => _optionalRendererCount > 0;
        [NonSerialized]
        internal int _optionalRendererCount;
        /// <summary>
        /// Keeps bit-wise enabled/disabled status for each instance's optional renderers. Each instance is represented with a 32 bit uint - so they can have maximum 32 optional renderers. 0 means enabled, 1 means disabled, so all renderers would be enabled by default.
        /// </summary>
        [NonSerialized]
        internal NativeArray<uint> optionalRendererStatusData;
        /// <summary>
        /// True when a change has been made to optionalRendererStatusData
        /// </summary>
        [NonSerialized]
        internal bool isOptionalRendererStatusModified;

        [NonSerialized]
        public NativeArray<int> isModifiedArray;
        [NonSerialized]
        internal int minModifiedIndex;
        [NonSerialized]
        internal int maxModifiedIndex;
        [NonSerialized]
        internal GPUIAutoUpdateTransformsJob autoUpdateTransformsJob;
        [NonSerialized]
        internal bool _isAutoUpdateTransformJobsStarted;

        [NonSerialized]
        internal int _lastEnabledStatusChangeFrame;
        #endregion Runtime Properties

        #region Editor Properties
#if UNITY_EDITOR
        [NonSerialized]
        internal int editor_editModeRenderKey;
#endif
        #endregion Editor Properties

        public override bool Initialize(GPUIPrototype prototype)
        {
            if (base.Initialize(prototype))
            {
                instancesToAdd ??= new();
                indexesToRemove ??= new(new IntInverseComparer());

                _optionalRendererCount = 0;
                if (prototype.prefabObject.TryGetComponent(out GPUIPrefab gpuiPrefab))
                {
                    prefabID = gpuiPrefab.GetPrefabID();

                    if (gpuiPrefab.childOptionalRenderers != null)
                    {
                        _optionalRendererCount = gpuiPrefab.childOptionalRenderers.Count;
                        if (_optionalRendererCount > 0)
                            optionalRendererStatusData = new(0, Allocator.Persistent);
                    }
                }
                else
                    prefabID = prototype.GetKey();

                _matrixArray = new(0, Allocator.Persistent);
                isModifiedArray = new(0, Allocator.Persistent);
                minModifiedIndex = int.MaxValue;
                maxModifiedIndex = -1;

                return true;
            }

            return false;
        }

        public override void Dispose()
        {
            base.Dispose();
            instanceCount = 0;
            instanceTransforms = null;
            instancesToAdd?.Clear();
            indexesToRemove?.Clear();
        }

        public override void ReleaseBuffers()
        {
            base.ReleaseBuffers();
            if (_matrixArray.IsCreated)
                _matrixArray.Dispose();
            if (isModifiedArray.IsCreated)
                isModifiedArray.Dispose();
            if (transformAccessArray.isCreated)
                transformAccessArray.Dispose();
            if (optionalRendererStatusData.IsCreated)
                optionalRendererStatusData.Dispose();
        }

        public int GetRegisteredInstanceCount()
        {
            if (registeredInstances != null && registeredInstances.prefabInstances != null)
                return registeredInstances.prefabInstances.Length;
            return 0;
        }

        public override NativeArray<Matrix4x4> GetTransformationMatrixArray() => _matrixArray;

        internal void UpdateTransformAccessArray()
        {
            if (!isTransformReferencesModified)
                return;
            Profiler.BeginSample("UpdateTransformAccessArray");
            if (transformAccessArray.isCreated)
                transformAccessArray.Dispose();
            TransformAccessArray.Allocate(instanceTransforms.Length, -1, out transformAccessArray);
            for (int i = 0; i < instanceCount; i++)
                transformAccessArray.Add(instanceTransforms[i]); // SetTransforms causes warning with null references

            isTransformReferencesModified = false;
            Profiler.EndSample();
        }

        public bool HasMatrixArray()
        {
            return _matrixArray.IsCreated;
        }

        internal unsafe void* GetMatrixArrayUnsafePtr()
        {
            return NativeArrayUnsafeUtility.GetUnsafePtr(_matrixArray);
        }
        
        internal void ResizeMatrixArray(int newSize)
        {
            _matrixArray.ResizeNativeArray(newSize, Allocator.Persistent);
            isModifiedArray.ResizeNativeArray(newSize, Allocator.Persistent);
        }

        public int GetMatrixLength()
        {
            return _matrixArray.Length;
        }

        public void SetMatrix(int index, Matrix4x4 matrix)
        {
            _matrixArray[index] = matrix;
            minModifiedIndex = Mathf.Min(index, minModifiedIndex);
            maxModifiedIndex = Mathf.Max(index, maxModifiedIndex);
        }

        public void SetMatrixModified(int index)
        {
            minModifiedIndex = Mathf.Min(index, minModifiedIndex);
            maxModifiedIndex = Mathf.Max(index, maxModifiedIndex);
        }

        public void SetAllMatricesModified()
        {
            isMatrixArrayModified = true;
            minModifiedIndex = 0;
            maxModifiedIndex = int.MaxValue;
        }

        public Matrix4x4 GetMatrix(int index)
        {
            return _matrixArray[index];
        }

        public int GetInstanceCount() => instanceCount;

        [Serializable]
        public class GPUIPrefabInstances
        {
            [SerializeField]
            public GameObject[] prefabInstances;
            [NonSerialized]
            public List<GPUIPrefab> runtimeCachedInstances = new();
        }

        private class IntInverseComparer : IComparer<int>
        {
            public int Compare(int x, int y)
            {
                return y.CompareTo(x);
            }
        }
    }
}