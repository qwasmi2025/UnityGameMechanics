// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GPUInstancerPro.CrowdAnimations
{
    public static class GPUICrowdEditorConstants
    {
        [MenuItem("Assets/Create/Rendering/GPU Instancer Pro/Crowd Rig Data", validate = false, priority = 5211)]
        public static void CreateRigDataMenuItem()
        {
            GameObject[] selectedGameObjects = Selection.GetFiltered<GameObject>(SelectionMode.Editable);
            foreach (GameObject go in selectedGameObjects)
            {
                if (go.HasComponentInChildren<SkinnedMeshRenderer>())
                    Selection.activeObject = GPUICrowdUtility.CreateRig(go, false, true);
            }
        }
        [MenuItem("Assets/Create/Rendering/GPU Instancer Pro/Crowd Rig Data", validate = true, priority = 5211)]
        public static bool CreateRigDataMenuItemValidate()
        {
            GameObject[] selectedGameObjects = Selection.GetFiltered<GameObject>(SelectionMode.Editable);
            foreach (GameObject go in selectedGameObjects)
            {
                if (go.HasComponentInChildren<SkinnedMeshRenderer>())
                    return true;
            }
            return false;
        }

        [MenuItem("Tools/GPU Instancer Pro/Crowd Animations/Shaders/Setup Selected Materials for Crowd Animations", validate = false, priority = 253)]
        public static void ToolbarSetupShaderForGPUIPro()
        {
            SetupShaderForCrowdMenuItem();
        }

        [MenuItem("Tools/GPU Instancer Pro/Crowd Animations/Shaders/Setup Selected Materials for Crowd Animations", validate = true, priority = 253)]
        public static bool ToolbarSetupShaderForGPUIProValidate()
        {
            return SetupShaderForCrowdMenuItemValidate();
        }

        [MenuItem("Assets/Create/Rendering/GPU Instancer Pro/Crowd Animations/Setup Shaders for Crowd Animations", validate = false, priority = 2201)]
        public static void SetupShaderForCrowdMenuItem()
        {
            if (!EditorUtility.DisplayDialog("Setup Shaders for Crowd Animations", "Automatic shader conversion for Crowd Animations is an experimental feature and may not work with all shaders. It is currently designed for and tested with shaders included in Unity’s URP and HDRP packages, such as the Lit shader.\n\nDo you want to try automatically converting the selected shaders?", "Yes", "Cancel"))
                return;
            GPUIShaderBindings.Instance.ClearEmptyShaderInstances();
            Shader[] shaders = Selection.GetFiltered<Shader>(SelectionMode.Assets);
            if (shaders != null)
            {
                for (int i = 0; i < shaders.Length; i++)
                {
                    GPUIShaderUtility.SetupShaderForGPUI(shaders[i], GPUIConstants.EXTENSION_CODE_CROWD, true, true, false);
                }
            }

            Material[] materials = Selection.GetFiltered<Material>(SelectionMode.Assets);
            if (materials != null)
            {
                for (int i = 0; i < materials.Length; i++)
                {
                    Material mat = materials[i];
                    if (!GPUIShaderBindings.Instance.IsShaderSetupForGPUI(mat.shader.name, GPUIConstants.EXTENSION_CODE_CROWD))
                    {
                        GPUIShaderUtility.SetupShaderForGPUI(mat.shader, GPUIConstants.EXTENSION_CODE_CROWD, true, true, false);
                    }
                    if (GPUIShaderBindings.Instance.IsShaderSetupForGPUI(mat.shader.name, GPUIConstants.EXTENSION_CODE_CROWD))
                    {
                        GPUIShaderUtility.AddShaderVariantToCollection(mat, GPUIConstants.EXTENSION_CODE_CROWD);
                        Debug.Log(GPUIConstants.LOG_PREFIX + mat.name + " material has been successfully added to Shader Variant Collection.", mat);
                    }
                }
            }
        }

        [MenuItem("Assets/Create/Rendering/GPU Instancer Pro/Crowd Animations/Setup Shaders for Crowd Animations", validate = true, priority = 2201)]
        public static bool SetupShaderForCrowdMenuItemValidate()
        {
            Shader[] shaders = Selection.GetFiltered<Shader>(SelectionMode.Assets);
            Material[] materials = Selection.GetFiltered<Material>(SelectionMode.Assets);
            return (shaders != null && shaders.Length > 0) || (materials != null && materials.Length > 0);
        }

        [MenuItem("Tools/GPU Instancer Pro/Crowd Animations/Add Batch Compute Player", validate = false, priority = 141)]
        public static void ToolbarAddBatchComputePlayer()
        {
            GameObject go = new GameObject("GPUI Batch Compute Player");
            var batchComputePlayer = go.AddComponent<GPUICrowdBatchComputePlayer>();
            GPUIManager[] managers = GPUIUtility.FindObjectsByType<GPUIManager>();
            for (int i = 0; i < managers.Length; i++)
            {
                var manager = managers[i];
                for (int p = 0; p < manager.GetPrototypeCount(); p++)
                {
                    var prefab = manager.GetPrototype(p).prefabObject;
                    if (prefab != null && prefab.TryGetComponent(out GPUICrowdInstance crowdInstance))
                    {
                        batchComputePlayer.prefab = crowdInstance;
                        break;
                    }
                }
            }

            Selection.activeGameObject = go;
            Undo.RegisterCreatedObjectUndo(go, "Add GPUI Crowd Batch Compute Player");
        }

        [MenuItem("Tools/GPU Instancer Pro/Crowd Animations/Add Event Definition", validate = false, priority = 241)]
        public static void ToolbarAddEventDefinition()
        {
            GameObject go = new GameObject("GPUI Crowd Event Definition");
            var eventDefinition = go.AddComponent<GPUICrowdEventDefinition>();
            GPUIManager[] managers = GPUIUtility.FindObjectsByType<GPUIManager>();
            for (int i = 0; i < managers.Length; i++)
            {
                var manager = managers[i];
                for (int p = 0; p < manager.GetPrototypeCount(); p++)
                {
                    var prefab = manager.GetPrototype(p).prefabObject;
                    if (prefab != null && prefab.TryGetComponent(out GPUICrowdInstance crowdInstance))
                    {
                        eventDefinition.crowdPrefab = crowdInstance;
                        break;
                    }
                }
            }

            Selection.activeGameObject = go;
            Undo.RegisterCreatedObjectUndo(go, "Add GPUI Crowd Event Definition");
        }

        public static void OnEnableGPUSkinningModified(GameObject prefab, bool enabled)
        {
            if (prefab == null)
                return;
            if (enabled && !prefab.HasComponent<GPUICrowdInstance>())
            {
                GPUICrowdInstance crowdInstance = GPUIPrefabUtility.AddOrGetComponentToPrefab<GPUICrowdInstance>(prefab);
                crowdInstance.LoadBoneTransforms(true);
                if (prefab.TryGetComponent(out GPUIPrefabBase prefabBase))
                {
                    prefabBase.GetPrefabID();
                    GPUIPrefabUtility.SavePrefabAsset(prefab);
                    GPUIPrefabUtility.MergeAllPrefabInstances(prefab);
                    EditorUtility.SetDirty(prefab);
                }
            }
            else if (!enabled && prefab.HasComponent<GPUICrowdInstance>() && EditorUtility.DisplayDialog("!!! WARNING !!! Destroy Crowd Instance", "Are you sure you want to destroy the Crowd Instance component on the prefab?\n\nAll references to this component in scenes will be lost.", "Destroy", "No"))
            {
                GPUIPrefabUtility.RemoveComponentFromPrefab<GPUICrowdInstance>(prefab);
                if (prefab.TryGetComponent(out GPUIPrefabBase prefabBase))
                {
                    prefabBase.GetPrefabID();
                    GPUIPrefabUtility.SavePrefabAsset(prefab);
                    GPUIPrefabUtility.MergeAllPrefabInstances(prefab);
                    EditorUtility.SetDirty(prefab);
                }
            }
        }

        public static void OnDrawGPUSkinningSettings(GPUIManager manager, VisualElement _rootElement, List<GPUIHelpBox> _helpBoxes, Action redrawCallback)
        {
            if (manager == null)
                return;
            if (manager.editor_selectedPrototypeIndexes.Count != 1)
                return;
            var prototype0 = manager.GetPrototype(manager.editor_selectedPrototypeIndexes[0]);
            if (prototype0 == null || prototype0.prefabObject == null)
                return;
            var crowdInstance0 = prototype0.prefabObject.GetComponent<GPUICrowdInstance>();
            if (crowdInstance0 == null)
                return;

            if (!GPUICrowdUtility.IsReadWriteEnabledOnModel(crowdInstance0.gameObject))
                GPUIEditorUtility.DrawErrorMessage(_rootElement, -6103, null, () =>
                {
                    if (GPUICrowdUtility.EnableReadWriteOnModel(crowdInstance0.gameObject))
                        redrawCallback?.Invoke();
                });

            _rootElement.Add(GPUIEditorUtility.CreateGPUIHelpBox("crowdSelectPrefab", null, () =>
            {
                Selection.activeGameObject = crowdInstance0.gameObject;
                GPUIEditorUtility.WaitAndExecute(0.5f, () =>
                {
                    if (Highlighter.Highlight("Inspector", "Animator Workflow"))
                    {
                        GPUIEditorUtility.WaitAndExecute(1f, () =>
                        {
                            Highlighter.Stop();
                        });
                    }
                });
            }, HelpBoxMessageType.Info, "Select", GPUIEditorConstants.Colors.green));
        }

        public static string GetUIPath()
        {
            return GPUICrowdConstants.GetPackagesPath() + GPUIConstants.PATH_EDITOR + GPUIEditorConstants.PATH_UI;
        }

        public static string GetVersionNoText()
        {
            return GPUIConstants.UNITY_VERSION_TITLE + " c" + GPUIEditorSettings.Instance.GetVersion(GPUICrowdDefines.PACKAGE_NAME);
        }
    }
}
