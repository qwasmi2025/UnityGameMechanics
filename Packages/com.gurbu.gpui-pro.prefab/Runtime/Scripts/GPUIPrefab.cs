// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Profiling;

namespace GPUInstancerPro.PrefabModule
{
    /// <summary>
    /// This component is automatically attached to prefabs that are used as GPUI prototypes to identify them
    /// </summary>
    [DefaultExecutionOrder(200)]
#if !UNITY_6000_3_0 && !GPUIPRO_NO_HELPURL
    [HelpURL("https://wiki.gurbu.com/index.php?title=GPU_Instancer_Pro:GettingStarted#GPUI_Prefab")]
#endif
    public class GPUIPrefab : GPUIPrefabBase
    {
        [SerializeField]
        private bool _isRenderersDisabled;
        /// <summary>
        /// True when Mesh Renderer components are disabled
        /// </summary>
        public bool IsRenderersDisabled => _isRenderersDisabled;

        /// <summary>
        /// Prefab Manager that is currently rendering this instance
        /// </summary>
        public GPUIPrefabManager registeredManager { get; internal set; }

        internal bool _isBeingAddedToThePrefabManager;

        internal void SetInstancingData(GPUIPrefabManager registeredManager, int prefabID, int renderKey, int bufferIndex)
        {
            //Debug.Assert(_prefabID == 0 || _prefabID == prefabID, "Prefab ID mismatch. Current ID: " + _prefabID + " Given ID: " + prefabID, gameObject);
            this.registeredManager = registeredManager;
            this.renderKey = renderKey;
            this.bufferIndex = bufferIndex;
            if (_prefabID == 0)
                _prefabID = prefabID;
            registeredManager.SetPrefabInstanceRenderersEnabled(this, false);
            _isBeingAddedToThePrefabManager = false;
            OnInstancingStatusModified?.Invoke();
        }

        internal void ClearInstancingData(bool enableRenderers)
        {
            if (enableRenderers && registeredManager != null)
                registeredManager.SetPrefabInstanceRenderersEnabled(this, true);
            registeredManager = null;
            renderKey = 0;
            SetBufferIndex(-1); // Using SetBufferIndex to trigger OnBufferIndexModified
            _isBeingAddedToThePrefabManager = false;
            OnInstancingStatusModified?.Invoke();
        }

        public void RemovePrefabInstance()
        {
            if (!IsInstanced) return;
            if (!registeredManager.RemovePrefabInstance(this))
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not remove prefab instance with prefab ID: " + GetPrefabID(), this);
        }

        internal void UpdateTransformData()
        {
            if (!IsInstanced) return;
            if (CachedTransform.hasChanged)
                registeredManager.UpdateTransformData(this);
        }

        public void SetRenderersEnabled(bool enabled)
        {
            if (_isRenderersDisabled != enabled)
                return;
            Profiler.BeginSample("GPUIPrefabManager.SetRenderersEnabled");

            GPUIRenderingSystem.prefabRendererList.Clear();
            transform.GetPrefabRenderers(GPUIRenderingSystem.prefabRendererList);
            foreach (Renderer renderer in GPUIRenderingSystem.prefabRendererList)
                renderer.enabled = enabled;

            if (TryGetComponent(out LODGroup lodGroup))
                lodGroup.enabled = enabled;
            _isRenderersDisabled = !enabled;
            Profiler.EndSample();
        }

        internal void SetBufferIndex(int bufferIndex)
        {
            int previousBufferIndex = this.bufferIndex;
            if (previousBufferIndex != bufferIndex)
            {
                this.bufferIndex = bufferIndex;
                OnBufferIndexModified?.Invoke(previousBufferIndex);
            }
        }

        public override void OnOptionalRendererStatusChanged()
        {
            if (!IsInstanced || !registeredManager.IsInitialized || !registeredManager.TryGetPrefabPrototypeDataWithPrefabID(_prefabID, out var prototypeData))
                return;

            int numORStatusPerIndex = (prototypeData._optionalRendererCount - 1) / 32 + 1;
            prototypeData.optionalRendererStatusData[bufferIndex * numORStatusPerIndex] = optionalRendererStatus;
            prototypeData.isOptionalRendererStatusModified = true;
        }

        public override void OnOptionalRendererStatusExtraChanged()
        {
            if (!IsInstanced || !registeredManager.IsInitialized || optionalRendererStatusExtra == null || !registeredManager.TryGetPrefabPrototypeDataWithPrefabID(_prefabID, out var prototypeData))
                return;

            int numORStatusPerIndex = (prototypeData._optionalRendererCount - 1) / 32 + 1;
            for (int i = 0; i < numORStatusPerIndex - 1 && i < optionalRendererStatusExtra.Length; i++)
            {
                prototypeData.optionalRendererStatusData[bufferIndex * numORStatusPerIndex + i + 1] = optionalRendererStatusExtra[i];
            }
            prototypeData.isOptionalRendererStatusModified = true;
        }

        public override void SetMaterialVariation(int index, Vector4 variationValue)
        {
            if (!TryGetComponent(out GPUIMaterialVariationInstance variationInstance))
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "The prefab instance does not contain a GPUIMaterialVariationInstance component, so the variation cannot be set.");
                return;
            }
            variationInstance.SetVariation(index, variationValue);
        }
    }
}