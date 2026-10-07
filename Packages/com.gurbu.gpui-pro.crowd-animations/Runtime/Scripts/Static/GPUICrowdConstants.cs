// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GPUInstancerPro.CrowdAnimations
{
    public static class GPUICrowdConstants
    {
        #region Paths & File Names

        public const string FILE_DEFAULT_CROWD_PROFILE = "GPUIDefaultCrowdProfile";
        public const string FILE_CS_BoneBufferUtility = "GPUICrowdBoneBufferUtility";
        public const string FILE_CS_AnimatorController = "GPUICrowdAnimatorController";
        public const string FILE_CS_AnimateFromBakedClips = "GPUICrowdAnimateFromBakedClips";
        public const string FILE_RUNTIME_SETTINGS = "GPUICrowdRuntimeSettings";
        public const string PATH_CROWD = "Crowd/";

        private static string _packagesPath;
        public static string GetPackagesPath()
        {
            if (string.IsNullOrEmpty(_packagesPath))
                _packagesPath = "Packages/com.gurbu.gpui-pro.crowd-animations/";
            return _packagesPath;
        }

        public static string GetCrowdRigPath()
        {
            return GPUIConstants.GetExtensionsUserDataPath() + PATH_CROWD;
        }

        #endregion Paths & File Names

        #region Shaders
        public const string Kw_GPUI_CROWD_SKIN_WEIGHTS_2 = "GPUI_CROWD_SKIN_WEIGHTS_2";
        public const string Kw_GPUI_CROWD_SKIN_WEIGHTS_1 = "GPUI_CROWD_SKIN_WEIGHTS_1";
        #endregion Shaders

        #region Default Assets
        private static GPUIProfile _defaultCrowdProfile;
        public static GPUIProfile DefaultCrowdProfile
        {
            get
            {
                if (_defaultCrowdProfile == null)
                {
#if UNITY_EDITOR
                    _defaultCrowdProfile = AssetDatabase.LoadAssetAtPath<GPUIProfile>(GetPackagesPath() + GPUIConstants.PATH_RUNTIME + GPUIConstants.PATH_PROFILES + FILE_DEFAULT_CROWD_PROFILE + ".asset");
                    if (_defaultCrowdProfile == null)
                    {
#endif
                        _defaultCrowdProfile = ScriptableObject.CreateInstance<GPUIProfile>();
                        _defaultCrowdProfile.isShadowFrustumCulling = true;
                        _defaultCrowdProfile.isShadowOcclusionCulling = true;
                        _defaultCrowdProfile.isDefaultProfile = true;
#if UNITY_EDITOR
                    }
#endif
                }
                return _defaultCrowdProfile;
            }
        }
        #endregion  Default Assets

        #region Compute Shaders 

#if UNITY_EDITOR
        /// <summary>
        /// Sometimes Unity does not import Compute Shader files correctly the first time when they have file references in other packages.
        /// So we check for compiler errors here and reimport the Compute Shaders.
        /// </summary>
        public static void CheckForComputeCompilerErrors()
        {
            if (GPUIUtility.ComputeShaderHasCompilerErrors(CS_BoneBufferUtility))
                ReimportComputeShaders();
        }

        public static void ReimportComputeShaders()
        {
            GPUIUtility.ReimportFilesInFolder(GetPackagesPath() + GPUIConstants.PATH_RUNTIME + GPUIConstants.PATH_COMPUTE, "*.hlsl");
            GPUIUtility.ReimportFilesInFolder(GetPackagesPath() + GPUIConstants.PATH_RUNTIME + GPUIConstants.PATH_COMPUTE, "*.compute");
        }
#endif

        private static ComputeShader _CS_BoneBufferUtility;
        public static ComputeShader CS_BoneBufferUtility
        {
            get
            {
                if (_CS_BoneBufferUtility == null)
                    _CS_BoneBufferUtility = GPUIUtility.LoadResource<ComputeShader>(FILE_CS_BoneBufferUtility);
                return _CS_BoneBufferUtility;
            }
        }

        private static ComputeShader _CS_AnimatorController;
        public static ComputeShader CS_AnimatorController
        {
            get
            {
                if (_CS_AnimatorController == null)
                    _CS_AnimatorController = GPUIUtility.LoadResource<ComputeShader>(FILE_CS_AnimatorController);
                return _CS_AnimatorController;
            }
        }

        private static ComputeShader _CS_AnimateFromBakedClips;
        public static ComputeShader CS_AnimateFromBakedClips
        {
            get
            {
                if (_CS_AnimateFromBakedClips == null)
                    _CS_AnimateFromBakedClips = GPUIUtility.LoadResource<ComputeShader>(FILE_CS_AnimateFromBakedClips);
                return _CS_AnimateFromBakedClips;
            }
        }

        #endregion Compute Shaders 

        #region Shader Props

        public static readonly int PROP_shaderBoneBuffer = Shader.PropertyToID("shaderBoneBuffer");
        public static readonly int PROP_gpuiBoneBufferTexture = Shader.PropertyToID("gpuiBoneBufferTexture");
        public static readonly int PROP_crowdInstanceBuffer = Shader.PropertyToID("crowdInstanceBuffer");
        public static readonly int PROP_boneDataBuffer = Shader.PropertyToID("boneDataBuffer");
        public static readonly int PROP_gpuiSkinningValues = Shader.PropertyToID("gpuiSkinningValues");
        public static readonly int PROP_bindPoseBuffer = Shader.PropertyToID("bindPoseBuffer");
        public static readonly int PROP_boneCount = Shader.PropertyToID("boneCount");
        public static readonly int PROP_bindPoseNo = Shader.PropertyToID("bindPoseNo");
        public static readonly int PROP_animatorWorkflowID = Shader.PropertyToID("animatorWorkflowID");
        public static readonly int PROP_clipFramesAndWeightsBuffer = Shader.PropertyToID("clipFramesAndWeightsBuffer");
        public static readonly int PROP_bakedAnimationClipData = Shader.PropertyToID("bakedAnimationClipData");
        public static readonly int PROP_crowdAnimatorClipBuffer = Shader.PropertyToID("crowdAnimatorClipBuffer");
        public static readonly int PROP_statusBuffer = Shader.PropertyToID("statusBuffer");
        public static readonly int PROP_gpuiBoneDataIndex = Shader.PropertyToID("gpuiBoneDataIndex");

        #endregion Shader Props

        #region Default Values
        public static int DEFAULT_CLIP_FRAME_RATE = 30;
        public static int BONE_INDEX_WEIGHT_UV = 2;
        public const int ANIMATOR_MAX_CLIPS = 4;
        public static readonly GPUICrowdBakedClipData EMPTY_BAKED_CLIP_DATA = default;
        public static readonly Vector4 DEFAULT_CLIP_WEIGHT = new Vector4(1.0f, 0, 0, 0);
        public static readonly Vector4 DEFAULT_CLIP_START_TIMES = new Vector4(-1.0f, -1.0f, -1.0f, -1.0f);
        public static readonly Vector4 DEFAULT_CLIP_SPEEDS = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        public static readonly GPUICrowdRootMotion DEFAULT_ROOT_MOTION = new GPUICrowdRootMotion { position = Vector3.zero, rotation = Quaternion.identity, motionType = 0 };
        public static readonly GPUICrowdTransition DEFAULT_TRANSITION = default;
        public static readonly GPUICrowdAnimatorClipData DEFAULT_CLIP_DATA = default;
        public static readonly float MIN_CLIP_SPEED = 1e-8f;
        internal static int[] BAKED_CLIP_INDEXES = new int[ANIMATOR_MAX_CLIPS];
        internal static AnimationClip[] CLIPS_ARRAY = new AnimationClip[ANIMATOR_MAX_CLIPS];
        internal static GPUICrowdAnimatorClipData[] ANIMATOR_CLIP_DATA_VALUES = new GPUICrowdAnimatorClipData[ANIMATOR_MAX_CLIPS];
        internal static GPUICrowdAnimatorClipData[] PREVIOUS_ANIMATOR_CLIP_DATA_VALUES = new GPUICrowdAnimatorClipData[ANIMATOR_MAX_CLIPS];
        internal static List<AnimatorClipInfo> ANIMATOR_CLIP_INFO_LIST = new();
        public const float CLIP_SPEED_MULTIPLIER = 0.01f;
        #endregion Default Values

        #region Error Texts
        public const string ERROR_NO_ANIMATOR = "The {0} workflow does not contain an internal animator system!";
        public const string ERROR_NO_PREFAB_COMPONENT = "The instance is not rendered by Prefab Manager. Please use the Animator methods with a RenderKey and BufferIndex instead!";
        public const string ERROR_NO_RENDER_KEY = "Can not find renderer with key: {0}";
        public const string ERROR_NO_CROWD_DATA = "Can not find Crowd data for renderer with key: {0}. Please make sure the Crowd Instance component is added to the prefab.";
        public const string ERROR_BUFFER_INDEX_OUT_OF_BOUNDS = "Given bufferIndex: {0} is out of bounds. Buffer size: {1}";
        public const string ERROR_NULL_ANIMATION_CLIP = "Given animation clip is null! Can not play animation.";
        #endregion Error Texts
    }
}