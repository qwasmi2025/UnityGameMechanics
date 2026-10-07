// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GPUInstancerPro.CrowdAnimations
{
    [ExecuteInEditMode]
    [DefaultExecutionOrder(-1000)]
#if !UNITY_6000_3_0 && !GPUIPRO_NO_HELPURL
    [HelpURL("https://wiki.gurbu.com/index.php?title=GPU_Instancer_Pro-Crowd_Animations#GPUI_Crowd_Runtime_Settings")]
#endif
    public class GPUICrowdRuntimeSettingsOverwrite : MonoBehaviour
    {
        public GPUICrowdRuntimeSettings runtimeSettingsOverwrite;

        private void OnEnable()
        {
            GPUICrowdRuntimeSettings.OverwriteSettings(runtimeSettingsOverwrite);
        }
    }
}
