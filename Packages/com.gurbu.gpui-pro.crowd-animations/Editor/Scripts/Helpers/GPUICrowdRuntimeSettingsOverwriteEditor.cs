// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Search;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GPUInstancerPro.CrowdAnimations
{
    [CustomEditor(typeof(GPUICrowdRuntimeSettingsOverwrite))]
    public class GPUICrowdRuntimeSettingsOverwriteEditor : GPUIEditor
    {
        private GPUICrowdRuntimeSettingsOverwrite _runtimeSettingsOverwrite;
        private VisualElement _contentElement;

        protected override void OnEnable()
        {
            base.OnEnable();

            _runtimeSettingsOverwrite = target as GPUICrowdRuntimeSettingsOverwrite;
        }

        public override void DrawContentGUI(VisualElement contentElement)
        {
            if (_runtimeSettingsOverwrite == null)
                return;
            _contentElement = contentElement;
            DrawContents();
        }

        private void DrawContents()
        {
            _contentElement.Clear();
            _contentElement.Add(DrawSerializedProperty(serializedObject.FindProperty("runtimeSettingsOverwrite"), out PropertyField runtimeSettingsOverwritePF));
            runtimeSettingsOverwritePF.RegisterValueChangedCallbackDelayed((evt) => DrawContents());
            if (_runtimeSettingsOverwrite.runtimeSettingsOverwrite != null)
            {
                GPUICrowdRuntimeSettingsEditor.DrawContentGUI(_contentElement, new SerializedObject(_runtimeSettingsOverwrite.runtimeSettingsOverwrite), _helpBoxes);
            }
            else
            {
                Button createButton = new Button(OnCreateButtonClicked);
                createButton.text = "Create";
                _contentElement.Add(createButton);
            }
        }

        private void OnCreateButtonClicked()
        {
            serializedObject.ApplyModifiedProperties();
            GPUICrowdRuntimeSettings runtimeSettings = CreateInstance<GPUICrowdRuntimeSettings>();
            runtimeSettings.SaveAsAsset();
            _runtimeSettingsOverwrite.runtimeSettingsOverwrite = runtimeSettings;
            serializedObject.Update();
            DrawContents();
        }

        public override string GetVersionNoText() => GPUICrowdEditorConstants.GetVersionNoText();

        public override string GetWikiURLParams() => "title=GPU_Instancer_Pro:GettingStarted#GPU_Instancer_Pro_Runtime_Settings";

        public override string GetTitleText() => "GPUI Crowd Runtime Settings";

        [MenuItem("Tools/GPU Instancer Pro/Crowd Animations/Add Runtime Settings Overwrite", validate = false, priority = 301)]
        public static GPUICrowdRuntimeSettingsOverwrite ToolbarAddRuntimeSettingsOverwrite()
        {
            GameObject go = new GameObject("GPUI Crowd Runtime Settings");
            GPUICrowdRuntimeSettingsOverwrite runtimeSettingsOverwrite = go.AddComponent<GPUICrowdRuntimeSettingsOverwrite>();

            Selection.activeGameObject = go;
            Undo.RegisterCreatedObjectUndo(go, "Add Crowd Runtime Settings Overwrite");

            return runtimeSettingsOverwrite;
        }
    }
}
