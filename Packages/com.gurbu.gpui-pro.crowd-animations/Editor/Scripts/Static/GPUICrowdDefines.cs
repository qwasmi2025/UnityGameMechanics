// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GPUInstancerPro.CrowdAnimations
{
    [InitializeOnLoad]
    public static class GPUICrowdDefines
    {
        public static string VERSION_TEXT = "0.10.4"; // Current Crowd Animations version
        public static Version DEMO_UPDATE_VERSION = Version.Parse("0.9.0"); // The package version where the demo scenes were last updated and needs to be reimported.
        public static readonly string PACKAGE_NAME = "com.gurbu.gpui-pro.crowd-animations";
        private static readonly string[] AUTO_PACKAGE_IMPORTER_GUIDS = { };

        public static readonly string SHADERS_BUILTIN_PACKAGE_PATH = "Packages/com.gurbu.gpui-pro.crowd-animations/Editor/Extras/Shaders-Crowd-Builtin.unitypackage";

        static GPUICrowdDefines()
        {
            GPUIDefines.OnPackageVersionChanged -= OnVersionChanged;
            GPUIDefines.OnPackageVersionChanged += OnVersionChanged;
            GPUIDefines.OnImportPackages -= ImportPackages;
            GPUIDefines.OnImportPackages += ImportPackages;

            // Delayed to wait for asset loading
            EditorApplication.delayCall -= DelayedInitialization;
            EditorApplication.delayCall += DelayedInitialization;

            GPUIManagerEditor.OnEnableGPUSkinningModifiedCallback = GPUICrowdEditorConstants.OnEnableGPUSkinningModified;
            GPUIManagerEditor.OnDrawGPUSkinningSettings = GPUICrowdEditorConstants.OnDrawGPUSkinningSettings;
        }

        static void DelayedInitialization()
        {
            if (!GPUIEditorSettings.Instance.IsSubModuleExists(PACKAGE_NAME) || GPUIEditorSettings.Instance.GetVersion(PACKAGE_NAME) != VERSION_TEXT)
                GPUIEditorSettings.Instance.RequirePackageReload();

            GPUICrowdConstants.CheckForComputeCompilerErrors();
        }

        private static void OnVersionChanged(string packageName)
        {
            if (packageName == PACKAGE_NAME && GPUIEditorSettings.Instance.IsSubModuleVersionChanged(PACKAGE_NAME, out string currentVersionText, out string previousVersionText))
            {
#if GPUIPRO_DEVMODE
                Debug.Log(GPUIConstants.LOG_PREFIX + GPUIConstants.LOG_PREFIX_DEV + PACKAGE_NAME + " version changed from: " + previousVersionText + " to: " + currentVersionText);
#endif
                ImportPackages(false);

                GPUIEditorSettings.Instance.SetSubModuleProcessedVersion(packageName, currentVersionText);

                #region Reimport demo scenes
                bool reimportDemos = GPUIProDemoImporter.IsDemosImported() && !GPUIProDemoImporter.IsDemosImportedForPackage("CrowdAnimations");
                if (!reimportDemos && Version.TryParse(previousVersionText, out Version previousVersion) && Version.TryParse(currentVersionText, out Version currentVersion))
                    reimportDemos = GPUIProDemoImporter.IsDemosImported() && previousVersion.CompareTo(DEMO_UPDATE_VERSION) < 0 && currentVersion.CompareTo(DEMO_UPDATE_VERSION) >= 0;
                if (reimportDemos)
                {
                    Debug.Log(GPUIConstants.LOG_PREFIX + "Importing new demo scenes for version " + GPUIEditorSettings.Instance.GetVersion() + "...");
                    GPUIProDemoImporter.ImportDemos(GPUIRuntimeSettings.Instance.RenderPipeline);
                }
                #endregion Reimport demo scenes
            }
        }

        public static void ImportPackages(bool forceReimport)
        {
            #region Import Shader files for render pipeline
            if (GPUIRuntimeSettings.Instance.IsBuiltInRP)
            {
                GPUIEditorSettings.Instance.ImportPackageAtPath(SHADERS_BUILTIN_PACKAGE_PATH);
                Debug.Log(GPUIConstants.LOG_PREFIX + "Importing Crowd builtin shaders for version " + GPUIEditorSettings.Instance.GetVersion() + "...");
            }
            #endregion Import Shader files for render pipeline

            GPUIProPackageImporter.ImportPackages(AUTO_PACKAGE_IMPORTER_GUIDS, forceReimport);
            GPUICrowdConstants.ReimportComputeShaders();
        }
    }
}
