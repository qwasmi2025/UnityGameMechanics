// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using UnityEngine;

namespace GPUInstancerPro.CrowdAnimations
{
    public class GPUICrowdRuntimeSettings : ScriptableObject
    {
        [SerializeField]
        public int mecanimReaderMaxDelay = 20;
        [SerializeField]
        public int mecanimReaderMinReadCount = 10;
        [SerializeField]
        public int legacyReaderMaxDelay = 20;
        [SerializeField]
        public int legacyReaderMinReadCount = 10;

        private static GPUICrowdRuntimeSettings _instance;
        public static GPUICrowdRuntimeSettings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = GetDefaultCrowdRuntimeSettings();
                return _instance;
            }
            set
            {
                _instance = value;
            }
        }

        private static GPUICrowdRuntimeSettings GetDefaultCrowdRuntimeSettings()
        {
            GPUICrowdRuntimeSettings runtimeSettings = null;
            GPUICrowdRuntimeSettingsOverwrite overwrite = GPUIUtility.FindFirstObjectByType<GPUICrowdRuntimeSettingsOverwrite>();
            if (overwrite != null && overwrite.runtimeSettingsOverwrite != null)
            {
                runtimeSettings = overwrite.runtimeSettingsOverwrite;
            }
            if (runtimeSettings == null)
            {
                runtimeSettings = ScriptableObject.CreateInstance<GPUICrowdRuntimeSettings>();
                runtimeSettings.SetDefaultValues();
            }
            return runtimeSettings;
        }

        internal static void OverwriteSettings(GPUICrowdRuntimeSettings overwriteSettings)
        {
            if (overwriteSettings == null || _instance == overwriteSettings) return;
            _instance = overwriteSettings;
        }

#if UNITY_EDITOR
        public void SaveAsAsset()
        {
            this.SaveAsAsset(GPUIConstants.GetDefaultUserDataPath() + GPUIConstants.PATH_SETTINGS, GPUICrowdConstants.FILE_RUNTIME_SETTINGS + ".asset", true);
        }
#endif

        public void SetDefaultValues()
        {
            mecanimReaderMaxDelay = 20; 
            mecanimReaderMinReadCount = 10;
            legacyReaderMaxDelay = 20;
            legacyReaderMinReadCount = 10;
        }
    }
}