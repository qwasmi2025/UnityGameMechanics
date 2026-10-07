// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GPUInstancerPro.CrowdAnimations
{
    [CustomEditor(typeof(GPUICrowdRig))]
    public class GPUICrowdRigEditor : GPUIEditor
    {
        private GPUICrowdRig _crowdRig;

        protected override void OnEnable()
        {
            base.OnEnable();

            _crowdRig = target as GPUICrowdRig;
        }

        public override void DrawContentGUI(VisualElement contentElement)
        {
            DrawCrowdRig(_crowdRig, serializedObject, contentElement, _helpBoxes);
        }

        public static void DrawCrowdRig(GPUICrowdRig crowdRig, SerializedObject serializedObject, VisualElement rootElement, List<GPUIHelpBox> helpBoxes, GPUICrowdInstance crowdInstance = null)
        {
            if (crowdRig == null)
                return;

            rootElement.Add(GPUIEditorUtility.DrawSerializedProperty(serializedObject.FindProperty("skinWeights"), "skinWeights", helpBoxes, out var skinWeightsPF));
            skinWeightsPF.style.marginTop = 5;
            if (Application.isPlaying)
            {
                skinWeightsPF.RegisterValueChangeCallback(evt =>
                {
                    if (GPUICrowdSkinningSystem.IsActive)
                        GPUICrowdSkinningSystem.Instance.ApplySkinWeightsKeywords();
                });
            }

            #region Skinned Mesh Data
            Foldout skinnedMeshDataVE = new Foldout();
            rootElement.Add(skinnedMeshDataVE);
            DrawSkinnedMeshData(crowdRig, serializedObject, helpBoxes, skinnedMeshDataVE);
            #endregion Skinned Mesh Data

            #region Baked Data
            VisualElement bakedDataVE = new VisualElement();
            rootElement.Add(bakedDataVE);
            EditorApplication.delayCall += () => DrawRigBakedData(crowdRig, bakedDataVE);
            #endregion Baked Data

            #region Serialized Clip Data
            if (!Application.isPlaying && AssetDatabase.Contains(crowdRig) && (crowdInstance != null || crowdRig.GetSerializedClipData() != null))
            {
                Foldout serializedClipsFoldout = new Foldout();
                serializedClipsFoldout.value = false;
                rootElement.Add(serializedClipsFoldout);
                DrawSerializedClipData(crowdRig, crowdInstance, serializedObject, helpBoxes, serializedClipsFoldout);
            }
            #endregion Serialized Clip Data
        }

        private static void DrawSkinnedMeshData(GPUICrowdRig crowdRig, SerializedObject serializedObject, List<GPUIHelpBox> helpBoxes, Foldout skinnedMeshDataVE)
        {
            skinnedMeshDataVE.Clear();
            skinnedMeshDataVE.value = false;
            skinnedMeshDataVE.text = "Skinned Meshes [" + crowdRig.GetSkinnedMeshCount() + "]";
            skinnedMeshDataVE.contentContainer.SetEnabled(false);
            skinnedMeshDataVE.Add(GPUIEditorUtility.DrawSerializedProperty(serializedObject.FindProperty("skinnedMeshes"), "skinnedMeshes", helpBoxes, out _));
            skinnedMeshDataVE.Add(GPUIEditorUtility.DrawSerializedProperty(serializedObject.FindProperty("bindPoseDataList"), "bindPoseDataList", helpBoxes, out _));
            skinnedMeshDataVE.Add(GPUIEditorUtility.DrawSerializedProperty(serializedObject.FindProperty("bones"), "bones", helpBoxes, out _));
        }

        private static void DrawRigBakedData(GPUICrowdRig crowdRig, VisualElement bakedDataVE)
        {
            bakedDataVE.Clear();
            int bakedClipCount = crowdRig.GetBakedClipCount();

            if (bakedClipCount > 0)
            {
                long boneCount = crowdRig.GetBoneCount();
                long bakedDataSize = 0;

                Foldout bakedClipsFoldout = new Foldout();
                bakedClipsFoldout.value = false;
                bakedClipsFoldout.text = "Baked Clips [" + bakedClipCount + "]";
                bakedDataVE.Add(bakedClipsFoldout);

                var dict = crowdRig.GetBakedClipIndexDictionary();
                var arr = crowdRig.GetBakedClipDataArray();
                foreach (var indexes in dict)
                {
                    VisualElement bakedClipVE = new VisualElement();
                    bakedClipVE.AddToClassList("gpui-border");
                    bakedClipVE.AddToClassList("gpui-bg-light");
                    bakedClipVE.SetEnabled(false);
                    bakedClipsFoldout.Add(bakedClipVE);

                    var clipField = new ObjectField("Clip");
                    clipField.objectType = typeof(AnimationClip);
                    clipField.value = crowdRig.GetBakedAnimationClip(indexes.Key);
                    bakedClipVE.Add(clipField);

                    GPUICrowdBakedClipData bakedClipData = arr[indexes.Value];
                    long clipDataSize = bakedClipData.clipFrameCount * boneCount * GPUITransformData.STRIDE;

                    var frameCountField = new IntegerField("Frame Count");
                    frameCountField.value = bakedClipData.clipFrameCount;
                    bakedClipVE.Add(frameCountField);

                    if (bakedClipData.bakedRootMotionIndex >= 0)
                    {
                        var rootMotionIndexField = new IntegerField("Root Motion Index");
                        rootMotionIndexField.value = bakedClipData.bakedRootMotionIndex;
                        bakedClipVE.Add(rootMotionIndexField);
                        clipDataSize += bakedClipData.clipFrameCount * GPUICrowdRootMotion.STRIDE;
                    }
                    bakedClipVE.Add(new Label("Size: " + GPUIUtility.FormatBytesToString(clipDataSize)));

                    bakedDataSize += clipDataSize;
                }

                bakedClipsFoldout.text += " [" + GPUIUtility.FormatBytesToString(bakedDataSize) + "]";

                if (!Application.isPlaying)
                {
                    Button disposeBakedDataButton = new Button(() =>
                    {
                        crowdRig.Dispose();
                        DrawRigBakedData(crowdRig, bakedDataVE);
                    });
                    disposeBakedDataButton.text = "Dispose Baked Data";
                    disposeBakedDataButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                    disposeBakedDataButton.style.backgroundColor = GPUIEditorConstants.Colors.lightRed;
                    disposeBakedDataButton.focusable = false;
                    bakedClipsFoldout.Add(disposeBakedDataButton);
                }
            }
        }

        private static void DrawSerializedClipData(GPUICrowdRig crowdRig, GPUICrowdInstance crowdInstance, SerializedObject serializedObject, List<GPUIHelpBox> helpBoxes, Foldout containerFoldout)
        {
            containerFoldout.Clear();
            containerFoldout.SetVisible(false);
            containerFoldout.text = "Pre-Baked Clips";

            SerializedProperty serializedClipDataSP = serializedObject.FindProperty("_serializedClipData");
            SerializedProperty serializedClipsSP = serializedObject.FindProperty("_serializedClips");
            if (serializedClipDataSP.objectReferenceValue != null)
            {
                containerFoldout.Add(GPUIEditorUtility.DrawSerializedProperty(serializedClipDataSP, out var serializedClipDataPF));
                serializedClipDataPF.SetEnabled(false);
                containerFoldout.Add(GPUIEditorUtility.DrawSerializedProperty(serializedClipsSP, out var serializedClipsPF));
                serializedClipsPF.SetEnabled(false);

                Button clearBakedDataButton = new Button(() =>
                {
                    if (!EditorUtility.DisplayDialog("Delete Baked Data", "Do wish to delete the baked animation clip data?", "Delete", "Cancel"))
                        return;
                    crowdRig.Editor_ClearSerializedClipData();
                    serializedObject.Update();
                    DrawSerializedClipData(crowdRig, crowdInstance, serializedObject, helpBoxes, containerFoldout);
                });
                clearBakedDataButton.text = "Delete Baked Data";
                clearBakedDataButton.style.marginTop = 5;
                clearBakedDataButton.style.marginBottom = 5;
                containerFoldout.Add(clearBakedDataButton);

                containerFoldout.SetVisible(true);
            }

            if (crowdInstance == null)
                return;
            containerFoldout.SetVisible(true);

            if (crowdRig.editor_clipsToSerialize == null || crowdRig.editor_clipsToSerialize.Count == 0)
            {
                crowdRig.editor_clipsToSerialize = new();
                if (crowdInstance.TryGetComponent<Animator>(out var animator) && animator.runtimeAnimatorController != null)
                {
                    var clips = animator.runtimeAnimatorController.animationClips;
                    foreach (var item in clips)
                    {
                        if (!crowdRig.editor_clipsToSerialize.Contains(item))
                            crowdRig.editor_clipsToSerialize.Add(item);
                    }
                }
                else
                    crowdRig.editor_clipsToSerialize.AddRange(crowdRig.editor_clipsToSerialize);
            }

            SerializedProperty editor_clipListToSerializeSP = serializedObject.FindProperty("editor_clipsToSerialize");
            editor_clipListToSerializeSP.isExpanded = true;
            containerFoldout.Add(GPUIEditorUtility.DrawSerializedProperty(editor_clipListToSerializeSP, "clipsToSerialize", helpBoxes, out _));

            Button bakeClipsButton = new Button(() =>
            {
                crowdRig.Editor_BakeSerializedClips(crowdInstance);
                serializedObject.Update();
                DrawSerializedClipData(crowdRig, crowdInstance, serializedObject, helpBoxes, containerFoldout);
            });
            bakeClipsButton.text = "Bake Clips";
            containerFoldout.Add(bakeClipsButton);
        }

        public override string GetTitleText() => "GPUI Crowd Rig";
        public override string GetVersionNoText() => GPUICrowdEditorConstants.GetVersionNoText();
        public override string GetWikiURLParams() => "title=GPU_Instancer_Pro-Crowd_Animations#GPUI_Crowd_Instance";
    }
}
