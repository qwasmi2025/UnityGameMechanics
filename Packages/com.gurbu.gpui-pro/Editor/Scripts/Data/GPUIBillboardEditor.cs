// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace GPUInstancerPro
{
    [CustomEditor(typeof(GPUIBillboard))]
    public class GPUIBillboardEditor : GPUIEditor
    {
        public override void DrawContentGUI(VisualElement contentElement)
        {
            GPUIBillboard billboardAsset = serializedObject.targetObject as GPUIBillboard;
            if (billboardAsset == null)
                return;

            var shaders = GPUIUtility.GetUniqueShaders(billboardAsset.prefabObject);
            if (billboardAsset.ClearUnusedCustomShaderProperties(shaders))
            {
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
            }

            DrawContentGUI(contentElement, serializedObject, _helpBoxes, shaders);
        }

        public static void DrawContentGUI(VisualElement contentElement, SerializedObject serializedObject, List<GPUIHelpBox> helpBoxes, IEnumerable<Shader> shaders)
        {
            if (serializedObject.targetObject == null)
                return;

            GPUIBillboard billboardAsset = serializedObject.targetObject as GPUIBillboard;

            VisualElement container = new VisualElement();
            contentElement.Add(container);

            container.Add(DrawSerializedProperty(serializedObject.FindProperty("prefabObject"), helpBoxes, out PropertyField pf));
            pf.SetEnabled(false);
            container.Add(DrawSerializedProperty(serializedObject.FindProperty("atlasResolution"), helpBoxes));
            container.Add(DrawSerializedProperty(serializedObject.FindProperty("frameCount"), helpBoxes));
            container.Add(DrawSerializedProperty(serializedObject.FindProperty("brightness"), helpBoxes));
            container.Add(DrawSerializedProperty(serializedObject.FindProperty("cutoffOverride"), helpBoxes));
            container.Add(DrawSerializedProperty(serializedObject.FindProperty("normalStrength"), helpBoxes));
            if (GPUIRuntimeSettings.Instance.IsBuiltInRP)
                container.Add(DrawSerializedProperty(serializedObject.FindProperty("billboardShaderType"), helpBoxes));

            VisualElement shaderPropertyFields = new VisualElement();
            container.Add(shaderPropertyFields);
            shaderPropertyFields.style.marginLeft = 4;
            shaderPropertyFields.Add(new IMGUIContainer(() => DrawIMGUIBillboardShaderCustomProperties(billboardAsset, serializedObject, shaders)));

            container.Add(DrawSerializedProperty(serializedObject.FindProperty("albedoAtlasTexture"), helpBoxes, out pf));
            pf.SetEnabled(false);
            container.Add(DrawSerializedProperty(serializedObject.FindProperty("normalAtlasTexture"), helpBoxes, out pf));
            pf.SetEnabled(false);
            GPUIEditorUtility.DrawGPUIHelpBox(container, -1101, null, null, HelpBoxMessageType.Info);

            VisualElement buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            container.Add(buttons);

            Button generateBillboardButton = new(() =>
            {
                GPUIBillboardUtility.GenerateBillboard(billboardAsset, true);
                GPUIRenderingSystem.RegenerateRenderers();
            });
            generateBillboardButton.text = "Regenerate";
            generateBillboardButton.enableRichText = true;
            generateBillboardButton.style.marginLeft = 10;
            generateBillboardButton.style.backgroundColor = GPUIEditorConstants.Colors.green;
            generateBillboardButton.style.color = Color.white;
            generateBillboardButton.style.flexGrow = 1;
            generateBillboardButton.focusable = false;
            buttons.Add(generateBillboardButton);

            if (!Application.isPlaying)
            {
                Button editBillboardButton = new(() => { OnPreviewButtonClickEvent(billboardAsset); });
                editBillboardButton.text = "Preview";
                editBillboardButton.enableRichText = true;
                editBillboardButton.style.marginLeft = 10;
                editBillboardButton.style.backgroundColor = GPUIEditorConstants.Colors.blue;
                editBillboardButton.style.color = Color.white;
                editBillboardButton.style.flexGrow = 0.5f;
                editBillboardButton.focusable = false;
                buttons.Add(editBillboardButton);
            }

            //container.SetEnabled(!Application.isPlaying);
        }

        public static void DrawIMGUIBillboardShaderCustomProperties(GPUIBillboard billboardAsset, SerializedObject serializedObject, IEnumerable<Shader> shaders)
        {
            if (billboardAsset == null || billboardAsset.prefabObject == null || shaders == null)
                return;

            foreach (var shader in shaders)
            {
                if (shader == null)
                    continue;

                string[] texturePropertyNames = shader.GetPropertyNamesForType(ShaderPropertyType.Texture);
                bool hasMainTexProperty = texturePropertyNames.Contains("_MainTex") || texturePropertyNames.Contains("_MainTexture") || texturePropertyNames.Contains("_BaseMap");

                string[] colorPropertyNames = shader.GetPropertyNamesForType(ShaderPropertyType.Color);
                bool hasColorProperty = colorPropertyNames.Contains("_Color") || colorPropertyNames.Contains("_BaseColor");

                if (hasMainTexProperty && hasColorProperty)
                    continue;

                if (texturePropertyNames.Length == 0 && colorPropertyNames.Length == 0)
                    continue;

                texturePropertyNames = texturePropertyNames.AddToBeginningAndReturn("Default");
                colorPropertyNames = colorPropertyNames.AddToBeginningAndReturn("Default");

                float labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = GPUIEditorConstants.LABEL_WIDTH;

                var customShaderProperty = billboardAsset.GetShaderCustomProperty(shader.name);
                if (customShaderProperty == null)
                {
                    customShaderProperty = new GPUIBillboard.GPUIBillboardCustomShaderProperties()
                    {
                        shaderName = shader.name,
                    };
                    billboardAsset.customShaderProperties ??= new();
                    billboardAsset.customShaderProperties.Add(customShaderProperty);
                    if (serializedObject != null)
                    {
                        serializedObject.ApplyModifiedProperties();
                        serializedObject.Update();
                    }
                }

                EditorGUILayout.LabelField(customShaderProperty.shaderName);
                EditorGUI.indentLevel++;

                if (!hasMainTexProperty)
                {
                    int texturePropertySelectedIndex = 0;
                    if (!string.IsNullOrEmpty(customShaderProperty.mainTextureProperty))
                    {
                        for (int i = 1; i < texturePropertyNames.Length; i++)
                        {
                            if (customShaderProperty.mainTextureProperty == texturePropertyNames[i])
                            {
                                texturePropertySelectedIndex = i;
                                break;
                            }
                        }
                    }

                    EditorGUI.BeginChangeCheck();
                    texturePropertySelectedIndex = EditorGUILayout.Popup("Texture Property", texturePropertySelectedIndex, texturePropertyNames);
                    customShaderProperty.useRedChannelCutoff = EditorGUILayout.Toggle("Red Channel Cutoff", customShaderProperty.useRedChannelCutoff);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (texturePropertySelectedIndex == 0)
                            customShaderProperty.mainTextureProperty = null;
                        else
                            customShaderProperty.mainTextureProperty = texturePropertyNames[texturePropertySelectedIndex];
                        if (serializedObject != null)
                        {
                            serializedObject.ApplyModifiedProperties();
                            serializedObject.Update();
                        }
                    }
                }

                if (!hasColorProperty)
                {
                    int colorPropertySelectedIndex = 0;
                    if (!string.IsNullOrEmpty(customShaderProperty.mainColorProperty))
                    {
                        for (int i = 1; i < colorPropertyNames.Length; i++)
                        {
                            if (customShaderProperty.mainColorProperty == colorPropertyNames[i])
                            {
                                colorPropertySelectedIndex = i;
                                break;
                            }
                        }
                    }

                    EditorGUI.BeginChangeCheck();
                    colorPropertySelectedIndex = EditorGUILayout.Popup("Color Property", colorPropertySelectedIndex, colorPropertyNames);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (colorPropertySelectedIndex == 0)
                            customShaderProperty.mainColorProperty = null;
                        else
                            customShaderProperty.mainColorProperty = colorPropertyNames[colorPropertySelectedIndex];
                        if (serializedObject != null)
                        {
                            serializedObject.ApplyModifiedProperties();
                            serializedObject.Update();
                        }
                    }
                }

                EditorGUI.indentLevel--;

                EditorGUIUtility.labelWidth = labelWidth;
            }
        }

        private static void OnPreviewButtonClickEvent(GPUIBillboard billboardAsset)
        {
            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent("Current Scene"), false, () =>
            {
                GameObject previewGO = GPUIBillboardGeneratorWindow.GenerateBillboardPreview(billboardAsset, out _, out _);
                Selection.activeGameObject = previewGO;
                SceneView.lastActiveSceneView.FrameSelected();
            });
            menu.AddItem(new GUIContent("Preview Scene"), false, () =>
            {
                GPUIBillboardGeneratorWindow w = GPUIBillboardGeneratorWindow.ShowWindow();
                if (w != null)
                    w.SetBillboard(billboardAsset);
            });

            // display the menu
            menu.ShowAsContext();
        }

        public override string GetTitleText()
        {
            return "GPUI Billboard";
        }
    }
}
