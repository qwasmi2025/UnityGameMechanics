// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GPUInstancerPro.PrefabModule
{
    public class GPUIMaterialVariationData : IGPUIDisposable
    {
        private GPUIMaterialVariationDefinition _definition;
        private GPUIDataBuffer<Vector4> _variationBuffer;
        private bool _isInitialized;
        private List<int> _renderKeys;

        public GPUIMaterialVariationData(GPUIMaterialVariationDefinition definition)
        {
            _definition = definition;
        }

        internal void Initialize()
        {
            if (_isInitialized || !GPUIRuntimeSettings.IsSupportedPlatform())
                return;

            if (_definition == null)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not find Material Variation Definition.");
                return;
            }
            Shader replacementShader = _definition.replacementShader;
            if (replacementShader == null)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not find Replacement Shader for the Material Variation Definition. Please make sure to Generate Shader for the Material Variation Definition.", _definition);
                replacementShader = GPUIShaderBindings.Instance.ErrorShader;
            }
            _isInitialized = true;
            if (_variationBuffer == null)
                _variationBuffer = new GPUIDataBuffer<Vector4>(_definition.bufferName);
            if (_renderKeys == null)
                _renderKeys = new List<int>();

            if (_definition.material.shader == replacementShader)
                return; // No need to create a new material if the original shader and the replacement shader are the same

            GPUIRenderingSystem.InitializeRenderingSystem();
            GPUIRenderingSystem.Instance.MaterialProvider.OnInitialized -= OnMaterialProviderInitialized;
            GPUIRenderingSystem.Instance.MaterialProvider.OnInitialized += OnMaterialProviderInitialized;

            List<string> keywords = new List<string>() { GPUIPrefabConstants.Kw_GPUI_MATERIAL_VARIATION };
            int materialKey = _definition.material.GetInstanceIDGPUI();
#if GPUI_CROWD
            int crowdMatKey = GPUIUtility.GenerateHash(materialKey, GPUIConstants.EXTENSION_CODE_CROWD.GetHashCode());
#endif
            materialKey = GPUIUtility.GenerateHash(materialKey, string.Concat(keywords).GetHashCode());
            if (GPUIRenderingSystem.Instance.MaterialProvider.TryGetData(materialKey, out Material replacementMaterial))
            {
                if (replacementMaterial != null && replacementMaterial.name.EndsWith("_MV" + GPUIShaderBindings.GPUI_REPLACEMENT_MATERIAL_NAME_SUFFIX))
                    return; // No need to create a new material if a replacement for material variations is already added
            }

            replacementMaterial = new Material(replacementShader);
            replacementMaterial.CopyPropertiesFromMaterial(_definition.material);
            replacementMaterial.name = _definition.material.name + "_MV" + GPUIShaderBindings.GPUI_REPLACEMENT_MATERIAL_NAME_SUFFIX;
            replacementMaterial.hideFlags = HideFlags.HideAndDontSave;
            replacementMaterial.EnableKeyword(GPUIPrefabConstants.Kw_GPUI_MATERIAL_VARIATION);
            GPUIRenderingSystem.Instance.MaterialProvider.AddOrSet(materialKey, replacementMaterial);
#if GPUI_CROWD
            if (_definition.isCrowdAnimations)
                GPUIRenderingSystem.Instance.MaterialProvider.AddOrSet(GPUIUtility.GenerateHash(crowdMatKey, string.Concat(keywords).GetHashCode()), replacementMaterial);
#endif

            keywords.Add(GPUIConstants.Kw_LOD_FADE_CROSSFADE);
            keywords.Sort();
            materialKey = GPUIUtility.GenerateHash(_definition.material.GetInstanceIDGPUI(), string.Concat(keywords).GetHashCode());
            replacementMaterial = new Material(replacementMaterial);
            replacementMaterial.name = _definition.material.name + "_MV" + GPUIShaderBindings.GPUI_REPLACEMENT_MATERIAL_NAME_SUFFIX;
            replacementMaterial.EnableKeyword(GPUIConstants.Kw_LOD_FADE_CROSSFADE);
            GPUIRenderingSystem.Instance.MaterialProvider.AddOrSet(materialKey, replacementMaterial);
#if GPUI_CROWD
            if (_definition.isCrowdAnimations)
                GPUIRenderingSystem.Instance.MaterialProvider.AddOrSet(GPUIUtility.GenerateHash(crowdMatKey, string.Concat(keywords).GetHashCode()), replacementMaterial);
#endif
        }

        /// <summary>
        /// Called when the Material Provider is reset to re-add the replacement materials.
        /// </summary>
        private void OnMaterialProviderInitialized()
        {
            _isInitialized = false;
            Initialize();
        }

        public void ReleaseBuffers()
        {
            if (!_isInitialized)
                return;

            _isInitialized = false;
            if (_variationBuffer != null)
                _variationBuffer.ReleaseBuffers();
        }

        public void Dispose()
        {
            ReleaseBuffers();
            if (_variationBuffer != null)
                _variationBuffer.Dispose();
            _variationBuffer = null;
            _renderKeys = null;
        }

        public void AddVariation(int renderKey, int bufferIndex, Vector4 value)
        {
            Initialize();

            if (!_isInitialized)
                return;

            _variationBuffer.AddOrSet(bufferIndex, value);

            if (!_renderKeys.Contains(renderKey))
            {
                _renderKeys.Add(renderKey);
                GPUIRenderingSystem.AddDependentDisposable(renderKey, this);
            }
        }

        public void UpdateVariationBuffer()
        {
            if (!_isInitialized)
                return;

            if (_variationBuffer.UpdateBufferData())
            {
                foreach (int renderKey in _renderKeys)
                    GPUIRenderingSystem.AddMaterialPropertyOverride(renderKey, _definition.bufferName, _variationBuffer.Buffer);
            }
        }
    }
}
