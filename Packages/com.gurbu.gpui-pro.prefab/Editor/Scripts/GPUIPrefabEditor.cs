// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GPUInstancerPro.PrefabModule
{
    [CustomEditor(typeof(GPUIPrefab))]
    public class GPUIPrefabEditor : GPUIEditor
    {
        private GPUIPrefab _gpuiPrefab;

        protected override void OnEnable()
        {
            base.OnEnable();

            _gpuiPrefab = target as GPUIPrefab;
        }

        public override void DrawContentGUI(VisualElement contentElement)
        {
            VisualElement prefabBaseHeader = new VisualElement();
            contentElement.Add(prefabBaseHeader);

            var prefabIDVE = DrawSerializedProperty(serializedObject.FindProperty("_prefabID"));
            prefabIDVE.SetEnabled(false);
            prefabBaseHeader.Add(prefabIDVE);
            var childOptionalRenderersSP = serializedObject.FindProperty("childOptionalRenderers");
            if (childOptionalRenderersSP.arraySize > 0)
            {
                var childOptionalRenderersVE = DrawSerializedProperty(childOptionalRenderersSP);
                childOptionalRenderersVE.SetEnabled(false);
                childOptionalRenderersVE.style.marginLeft = -12;
                prefabBaseHeader.Add(childOptionalRenderersVE);
            }

            contentElement.Add(new IMGUIContainer(DrawIMGUIInstancingStatus));
        }

        public void DrawIMGUIInstancingStatus()
        {
            if (_gpuiPrefab.IsInstanced)
            {
                EditorGUILayout.LabelField("Instancing is active.");
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField("Registered Manager", _gpuiPrefab.registeredManager, typeof(GPUIPrefabManager), true);
                EditorGUILayout.IntField("Render Key", _gpuiPrefab.renderKey);
                EditorGUILayout.IntField("Buffer Index", _gpuiPrefab.bufferIndex);
#if GPUIPRO_DEVMODE
                var childOptionalRenderersSP = serializedObject.FindProperty("childOptionalRenderers");
                if (childOptionalRenderersSP.arraySize > 0)
                {
                    EditorGUILayout.TextField("Optional R. Status", GPUIUtility.UintToBinaryString(_gpuiPrefab.optionalRendererStatus, childOptionalRenderersSP.arraySize));
                    if (_gpuiPrefab.optionalRendererStatusExtra != null)
                    {
                        for (int i = 0; i < _gpuiPrefab.optionalRendererStatusExtra.Length; i++)
                        {
                            EditorGUILayout.TextField("Optional R. Status " + (i + 1), GPUIUtility.UintToBinaryString(_gpuiPrefab.optionalRendererStatusExtra[i]));
                        }
                    }
                }
#endif
                EditorGUI.EndDisabledGroup();
            }
            else if (Application.isPlaying && !GPUIPrefabUtility.IsPrefabAsset(_gpuiPrefab.gameObject, out _, false))
                EditorGUILayout.LabelField("Instancing has not been initialized.");
        }

        public override string GetTitleText()
        {
            return "GPUI Prefab";
        }

        public override string GetWikiURLParams()
        {
            return "title=GPU_Instancer_Pro:GettingStarted#GPUI_Prefab";
        }
    }
}
