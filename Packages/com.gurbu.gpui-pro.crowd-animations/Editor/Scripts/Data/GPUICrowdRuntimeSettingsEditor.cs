// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GPUInstancerPro.CrowdAnimations
{
    [CustomEditor(typeof(GPUICrowdRuntimeSettings))]
    public class GPUICrowdRuntimeSettingsEditor : GPUIEditor
    {
        public override void DrawContentGUI(VisualElement contentElement)
        {
            DrawContentGUI(contentElement, serializedObject, _helpBoxes);
        }

        public static void DrawContentGUI(VisualElement contentElement, SerializedObject serializedObject, List<GPUIHelpBox> helpBoxes)
        {
            Foldout mecanimReaderFoldout = GPUIEditorUtility.DrawBoxContainer(contentElement, "Mecanim Reader", true);
            mecanimReaderFoldout.Add(DrawSerializedProperty(serializedObject.FindProperty("mecanimReaderMaxDelay"), helpBoxes));
            mecanimReaderFoldout.Add(DrawSerializedProperty(serializedObject.FindProperty("mecanimReaderMinReadCount"), helpBoxes));

            Foldout legacyReaderFoldout = GPUIEditorUtility.DrawBoxContainer(contentElement, "Legacy Animation Reader", true);
            legacyReaderFoldout.Add(DrawSerializedProperty(serializedObject.FindProperty("legacyReaderMaxDelay"), helpBoxes));
            legacyReaderFoldout.Add(DrawSerializedProperty(serializedObject.FindProperty("legacyReaderMinReadCount"), helpBoxes));

            //if (Application.isPlaying)
            //    contentElement.SetEnabled(false);
        }

        public override string GetTitleText() => "GPUI Crowd Runtime Settings";
        public override string GetVersionNoText() => GPUICrowdEditorConstants.GetVersionNoText();
        public override string GetWikiURLParams() => "title=GPU_Instancer_Pro-Crowd_Animations#GPUI_Crowd_Runtime_Settings";
    }
}
