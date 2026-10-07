// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GPUInstancerPro
{
    public class GPUIBillboard : ScriptableObject
    {
        #region Serialized Properties
        // Inputs
        [SerializeField]
        public GameObject prefabObject;
        [SerializeField]
        public GPUIBillboardResolution atlasResolution = GPUIBillboardResolution.x2048;
        [Range(1, 32)]
        [SerializeField]
        public int frameCount = 8;
        [Range(0f, 1f)]
        [SerializeField]
        public float brightness = 0.5f;
        [Range(0f, 1f)]
        [SerializeField]
        public float cutoffOverride = 0.5f;
        [Range(0f, 1f)]
        [SerializeField]
        public float normalStrength = 0.5f;
        [SerializeField]
        public GPUIBillboardShaderType billboardShaderType = GPUIBillboardShaderType.Default;
        [SerializeField]
        public List<GPUIBillboardCustomShaderProperties> customShaderProperties;

        // Outputs
        [SerializeField]
        public Vector2 quadSize;
        [SerializeField]
        public float yPivotOffset;

        [SerializeField]
        public Texture2D albedoAtlasTexture;
        [SerializeField]
        public Texture2D normalAtlasTexture;
        #endregion Serialized Properties

        #region Runtime Properties
        [NonSerialized]
        public RenderTexture albedoAtlasRT;
        [NonSerialized]
        public RenderTexture normalAtlasRT;
        [NonSerialized]
        internal Mesh _quadMesh; // Keep a reference to the generated mesh to avoid duplication and memory leak.
        [NonSerialized]
        internal Material _billboardMaterial; // Keep a reference to the generated material to avoid avoid duplication and memory leak.
        #endregion Runtime Properties

        #region Getters/Setters

        public override string ToString()
        {
            return prefabObject.name;
        }

        public Texture GetAlbedoTexture()
        {
            if (albedoAtlasTexture != null)
                return albedoAtlasTexture;
            return albedoAtlasRT;
        }

        public Texture GetNormalTexture()
        {
            if (normalAtlasTexture != null)
                return normalAtlasTexture;
            return normalAtlasRT;
        }

        public GPUIBillboardCustomShaderProperties GetShaderCustomProperty(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName) || customShaderProperties == null)
                return null;
            foreach (var properties in customShaderProperties)
            {
                if (shaderName.Equals(properties.shaderName))
                    return properties;
            }
            return null;
        }

        public bool ClearUnusedCustomShaderProperties(IEnumerable<Shader> shaders)
        {
            if (customShaderProperties == null || customShaderProperties.Count == 0)
                return false;

            bool isModified = false;
            for (int i = 0; i < customShaderProperties.Count; i++)
            {
                var property = customShaderProperties[i];
                if (property == null || string.IsNullOrEmpty(property.shaderName) || Shader.Find(property.shaderName) == null)
                {
                    customShaderProperties.RemoveAt(i);
                    i--;
                    isModified = true;
                    continue;
                }
                bool containsShader = false;
                foreach (var shader in shaders)
                {
                    if (shader != null && shader.name == property.shaderName)
                    {
                        containsShader = true;
                        break;
                    }
                }
                if (!containsShader)
                {
                    customShaderProperties.RemoveAt(i);
                    i--;
                    isModified = true;
                    continue;
                }

            }

            return isModified;
        }

        #endregion Getters/Setters

        public enum GPUIBillboardResolution
        {
            x256 = 256,
            x512 = 512,
            x1024 = 1024,
            x2048 = 2048,
            x4096 = 4096,
            x8192 = 8192
        }

        public enum GPUIBillboardShaderType
        {
            Default = 0,
            SpeedTree = 1,
            TreeCreator = 2,
            SoftOcclusion = 3
        }

        [Serializable]
        public class GPUIBillboardCustomShaderProperties
        {
            [SerializeField]
            public string shaderName;
            [SerializeField]
            public string mainTextureProperty;
            [SerializeField]
            public string mainColorProperty;
            [SerializeField]
            public bool useRedChannelCutoff;
        }
    }
}