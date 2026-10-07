// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GPUInstancerPro.CrowdAnimations
{
    public static class GPUICrowdEditorUtility
    {
        #region Draw Bone Settings
        private static VisualTreeAsset _bonesUITemplate;
        public static void DrawBoneSettings(GPUICrowdInstance crowdInstance, Foldout boneTransformSettingsVE)
        {
            boneTransformSettingsVE.Clear();
            if (crowdInstance.Rig == null || crowdInstance.BoneTransforms == null)
                return;
            var crowdRig = crowdInstance.Rig;
            var boneTransforms = crowdInstance.BoneTransforms;

            int boneCount = crowdInstance.Rig.GetBoneCount();
            boneTransformSettingsVE.text = "Bone Settings [" + boneCount + "]";

            if (boneCount == 0)
                return;

            List<GPUIParsedBoneData> parsedBones = new();
            ParseBoneData(crowdRig, parsedBones);

            VisualElement boneTransformSettingsRoot = new VisualElement();
            _bonesUITemplate ??= AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GPUICrowdEditorConstants.GetUIPath() + "GPUICrowdBoneSelectionUI.uxml");
            _bonesUITemplate.CloneTree(boneTransformSettingsRoot);
            boneTransformSettingsVE.Add(boneTransformSettingsRoot);

            var readTogglesVE = boneTransformSettingsRoot.Q("ReadToggles");
            var writeTogglesVE = boneTransformSettingsRoot.Q("WriteToggles");
            var boneNamesVE = boneTransformSettingsRoot.Q("BoneNames");
            var clearReadButton = boneTransformSettingsRoot.Q<Button>("ClearReadButton");
            clearReadButton.clicked += () => ClearAllReadBone(crowdInstance, parsedBones, clearReadButton);
            var clearWriteButton = boneTransformSettingsRoot.Q<Button>("ClearWriteButton");
            clearWriteButton.clicked += () => ClearAllWriteBone(crowdInstance, parsedBones, clearWriteButton);
            var expandAllButton = boneTransformSettingsRoot.Q<Button>("ExpandAllButton");
            expandAllButton.clicked += () => ExpandAllBones(parsedBones);
            var collapseAllButton = boneTransformSettingsRoot.Q<Button>("CollapseAllButton");
            collapseAllButton.clicked += () => CollapseAllBones(parsedBones);

            readTogglesVE.Clear();
            writeTogglesVE.Clear();
            boneNamesVE.Clear();
            DrawParsedBoneData(crowdInstance, boneTransformSettingsVE, boneTransformSettingsRoot, readTogglesVE, writeTogglesVE, boneNamesVE, clearReadButton, clearWriteButton, parsedBones, 0, (EventBase evt) => ApplyParsedBoneDataChanges(crowdInstance, parsedBones, boneTransformSettingsVE, clearReadButton, clearWriteButton));
            ApplyParsedBoneDataChanges(crowdInstance, parsedBones, boneTransformSettingsVE, clearReadButton, clearWriteButton);
        }

        private static int GetBoneReadWriteStatus(GPUICrowdInstance crowdInstance, int boneIndex)
        {
            var boneTransforms = crowdInstance.BoneTransforms;
            foreach (var boneTransformReference in boneTransforms)
            {
                if (boneTransformReference.boneIndex == boneIndex)
                    return boneTransformReference.readWriteStatus;
            }
            return -1;
        }

        private static void SetBoneReadWriteStatus(GPUICrowdInstance crowdInstance, int boneIndex, int readWriteStatus)
        {
            var boneTransforms = crowdInstance.BoneTransforms;
            for (int i = 0; i < boneTransforms.Count; i++)
            {
                if (boneTransforms[i].boneIndex == boneIndex)
                {
                    var bt = boneTransforms[i];
                    bt.readWriteStatus = readWriteStatus;
                    boneTransforms[i] = bt;
                    return;
                }
            }
        }

        private static Transform GetBoneTransform(GPUICrowdInstance crowdInstance, int boneIndex)
        {
            var boneTransforms = crowdInstance.BoneTransforms;
            foreach (var boneTransformReference in boneTransforms)
            {
                if (boneTransformReference.boneIndex == boneIndex)
                    return boneTransformReference.transform;
            }
            return null;
        }

        private static void DrawParsedBoneData(GPUICrowdInstance crowdInstance, Foldout boneTransformSettingsVE, VisualElement parentVE, VisualElement readTogglesVE, VisualElement writeTogglesVE, VisualElement boneNamesVE, Button clearReadButton, Button clearWriteButton, List<GPUIParsedBoneData> parsedBones, int depth, EventCallback<EventBase> applyChangesCallback)
        {
            var boneTransforms = crowdInstance.BoneTransforms;
            foreach (var parsedBone in parsedBones)
            {
                int readWriteStatus = GetBoneReadWriteStatus(crowdInstance, parsedBone.boneIndex);

                parsedBone.readToggle = new Toggle();
                parsedBone.readToggle.AddToClassList("gpui-crowd-bone-toggle");
                parsedBone.readToggle.SetEnabled(!Application.isPlaying);
                parsedBone.readToggle.tooltip = "Read from Transform";
                string boneNameSuffix = "<i><size=10> {idx: " + parsedBone.boneIndex + "}</size></i>";
                if (parsedBone.boneIndex < 0 || readWriteStatus < 0)
                {
                    parsedBone.readToggle.SetHidden(true);
                    boneNameSuffix = "";
                }
                else
                    parsedBone.readToggle.value = readWriteStatus == 1;
                parsedBone.readToggle.RegisterValueChangedCallback((evt) => { if (evt.newValue) parsedBone.writeToggle.SetValueWithoutNotify(false); });
                parsedBone.readToggle.RegisterValueChangedCallback(applyChangesCallback);
                readTogglesVE.Add(parsedBone.readToggle);

                parsedBone.writeToggle = new Toggle();
                parsedBone.writeToggle.AddToClassList("gpui-crowd-bone-toggle");
                parsedBone.writeToggle.SetEnabled(!Application.isPlaying);
                parsedBone.writeToggle.tooltip = "Write to Transform";
                if (parsedBone.boneIndex < 0 || readWriteStatus < 0)
                    parsedBone.writeToggle.SetHidden(true);
                else
                    parsedBone.writeToggle.value = readWriteStatus == 2;
                parsedBone.writeToggle.RegisterValueChangedCallback((evt) => { if (evt.newValue) parsedBone.readToggle.SetValueWithoutNotify(false); });
                parsedBone.writeToggle.RegisterValueChangedCallback(applyChangesCallback);
                writeTogglesVE.Add(parsedBone.writeToggle);

                VisualElement boneNameVE = new VisualElement();
                boneNamesVE.Add(boneNameVE);

                if (parsedBone.children.Count == 0)
                {
                    parsedBone.boneNameLabel = new Label();
                    parsedBone.boneNameLabel.AddToClassList("gpui-crowd-bone-label");
                    parsedBone.boneNameLabel.text = parsedBone.transformName + boneNameSuffix;
                    boneNameVE.Add(parsedBone.boneNameLabel);
                }
                else
                {
                    parsedBone.boneNameFoldout = new Foldout();
                    parsedBone.boneNameFoldout.AddToClassList("gpui-crowd-bone-foldout");
                    parsedBone.boneNameFoldout.value = parsedBone.lastExpandedValue;
                    parsedBone.boneNameFoldout.text = parsedBone.transformName + boneNameSuffix;
                    boneNameVE.Add(parsedBone.boneNameFoldout);

                    parsedBone.boneNameFoldout.RegisterValueChangedCallback((evt) =>
                    {
                        if (evt.target == parsedBone.boneNameFoldout)
                            parsedBone.SetExpanded(evt.newValue);
                    });
                }

                Transform transformRef = GetBoneTransform(crowdInstance, parsedBone.boneIndex);
                parsedBone.transformField = new ObjectField("");
                parsedBone.transformField.objectType = typeof(Transform);
                parsedBone.transformField.AddToClassList("gpui-crowd-bone-transform");
                parsedBone.transformField.value = transformRef;
                parsedBone.transformField.SetVisible(transformRef != null);
                parsedBone.transformField.SetEnabled(false);
                boneNameVE.Add(parsedBone.transformField);

                parsedBone.SetVisible(depth == 0);
                parsedBone.SetDepth(depth);
                if (parsedBone.children.Count > 0)
                    DrawParsedBoneData(crowdInstance, boneTransformSettingsVE, parentVE, readTogglesVE, writeTogglesVE, boneNamesVE, clearReadButton, clearWriteButton, parsedBone.children, depth + 1, applyChangesCallback);
            }
        }

        private static void ParseBoneData(GPUICrowdRig crowdRig, List<GPUIParsedBoneData> parsedBones)
        {
            parsedBones.Clear();
            for (int i = 0; i < crowdRig.bones.Count; i++)
            {
                GPUICrowdBone bone = crowdRig.bones[i];
                string[] fullPathNames = bone.fullPath.Split('/');
                string fullPath = fullPathNames[0];
                int splitIndex = 0;
                do
                {
                    GPUIParsedBoneData parentBoneData = null;
                    string transformName = fullPathNames[splitIndex];
                    if (splitIndex > 0)
                    {
                        parentBoneData = GetParsedBoneData(parsedBones, fullPath);
                        fullPath += "/" + transformName;
                    }
                    GPUIParsedBoneData parsedBoneData = GetParsedBoneData(parsedBones, fullPath);
                    if (parsedBoneData == null)
                    {
                        parsedBoneData = new GPUIParsedBoneData()
                        {
                            transformName = transformName,
                            fullPath = fullPath,
                            boneIndex = crowdRig.GetBoneIndex(fullPath),
                            children = new()
                        };
                        if (parentBoneData != null)
                            parentBoneData.children.Add(parsedBoneData);
                        else
                            parsedBones.Add(parsedBoneData);
                    }

                    splitIndex++;
                }
                while (splitIndex < fullPathNames.Length);
            }
        }

        private static GPUIParsedBoneData GetParsedBoneData(List<GPUIParsedBoneData> parsedBones, string fullPath)
        {
            foreach (var bone in parsedBones)
            {
                if (bone.fullPath == fullPath)
                    return bone;
                else if (fullPath.StartsWith(bone.fullPath))
                    return GetParsedBoneData(bone.children, fullPath);
            }
            return null;
        }

        private static bool ApplyParsedBoneDataChanges(GPUICrowdInstance crowdInstance, List<GPUIParsedBoneData> parsedBones, Foldout boneTransformSettingsVE, Button clearReadButton, Button clearWriteButton)
        {
            var boneTransforms = crowdInstance.BoneTransforms;
            bool isModified = ApplyParsedBoneDataChangesRecursive(crowdInstance, parsedBones);

            int readCount = 0;
            int writeCount = 0;
            foreach (var bt in boneTransforms)
            {
                if (bt.readWriteStatus == 1)
                    readCount++;
                if (bt.readWriteStatus == 2)
                    writeCount++;
            }

            clearReadButton.SetVisible(readCount > 0 && !Application.isPlaying);
            clearReadButton.text = "Clear All Read [" + readCount + "]";
            clearWriteButton.SetVisible(writeCount > 0 && !Application.isPlaying);
            clearWriteButton.text = "Clear All Write [" + writeCount + "]";

            boneTransformSettingsVE.text = "Bone Settings [R: " + readCount + "] [W: " + writeCount + "]";

            if (isModified)
                EditorUtility.SetDirty(crowdInstance);

            return isModified;
        }

        private static bool ApplyParsedBoneDataChangesRecursive(GPUICrowdInstance crowdInstance, List<GPUIParsedBoneData> parsedBones)
        {
            var crowdRig = crowdInstance.Rig;
            bool isModified = false;
            foreach (var parsedBone in parsedBones)
            {
                if (parsedBone.boneIndex >= 0)
                {
                    GPUICrowdBone crowdBone = crowdRig.bones[parsedBone.boneIndex];
                    int readWriteStatus = GetBoneReadWriteStatus(crowdInstance, parsedBone.boneIndex);
                    int newStatus = parsedBone.readToggle.value ? 1 : (parsedBone.writeToggle.value ? 2 : 0);

                    if (readWriteStatus >= 0 && readWriteStatus != newStatus)
                    {
                        isModified = true;
                        SetBoneReadWriteStatus(crowdInstance, parsedBone.boneIndex, newStatus);
                    }
                    if (newStatus != 1 && parsedBone.readToggle.value)
                        parsedBone.readToggle.SetValueWithoutNotify(false);
                    if (newStatus != 2 && parsedBone.writeToggle.value)
                        parsedBone.writeToggle.SetValueWithoutNotify(false);
                }
                if (parsedBone.children.Count > 0)
                    ApplyParsedBoneDataChangesRecursive(crowdInstance, parsedBone.children);
            }
            if (isModified)
                EditorUtility.SetDirty(crowdInstance.gameObject);

            return isModified;
        }

        private static void ClearAllReadBone(GPUICrowdInstance crowdInstance, List<GPUIParsedBoneData> parsedBones, Button clearReadButton)
        {
            var boneTransforms = crowdInstance.BoneTransforms;
            for (int i = 0; i < boneTransforms.Count; i++)
            {
                if (boneTransforms[i].readWriteStatus == 1)
                {
                    var boneTransform = boneTransforms[i];
                    boneTransform.readWriteStatus = 0;
                    boneTransforms[i] = boneTransform;
                }
            }
            EditorUtility.SetDirty(crowdInstance);
            ClearAllReadBone(parsedBones);
            clearReadButton.SetVisible(false);
        }

        private static void ClearAllReadBone(List<GPUIParsedBoneData> parsedBones)
        {
            foreach (var bone in parsedBones)
            {
                bone.readToggle.value = false;
                if (bone.children.Count > 0)
                    ClearAllReadBone(bone.children);
            }
        }

        private static void ClearAllWriteBone(GPUICrowdInstance crowdInstance, List<GPUIParsedBoneData> parsedBones, Button clearWriteButton)
        {
            var boneTransforms = crowdInstance.BoneTransforms;
            for (int i = 0; i < boneTransforms.Count; i++)
            {
                if (boneTransforms[i].readWriteStatus == 2)
                {
                    var boneTransform = boneTransforms[i];
                    boneTransform.readWriteStatus = 0;
                    boneTransforms[i] = boneTransform;
                }
            }
            EditorUtility.SetDirty(crowdInstance);
            ClearAllReadBone(parsedBones);
            clearWriteButton.SetVisible(false);
        }

        private static void ClearAllWriteBone(List<GPUIParsedBoneData> parsedBones)
        {
            foreach (var bone in parsedBones)
            {
                bone.writeToggle.value = false;
                if (bone.children.Count > 0)
                    ClearAllWriteBone(bone.children);
            }
        }

        private static void ExpandAllBones(List<GPUIParsedBoneData> parsedBones)
        {
            foreach (var bone in parsedBones)
            {
                if (!bone.isExpanded && bone.boneNameFoldout != null)
                    bone.boneNameFoldout.value = true;
                ExpandAllBones(bone.children);
            }
        }

        private static void CollapseAllBones(List<GPUIParsedBoneData> parsedBones)
        {
            foreach (var bone in parsedBones)
            {
                if (bone.isExpanded)
                    bone.boneNameFoldout.value = false;
                CollapseAllBones(bone.children);
            }
        }

        private class GPUIParsedBoneData
        {
            public int boneIndex;
            public string transformName;
            public string fullPath;
            public List<GPUIParsedBoneData> children;
            public Toggle readToggle;
            public Toggle writeToggle;
            public Foldout boneNameFoldout;
            public Label boneNameLabel;
            public ObjectField transformField;
            public bool isExpanded => boneNameFoldout != null ? boneNameFoldout.value : false;
            public bool lastExpandedValue;

            public void SetExpanded(bool val)
            {
                lastExpandedValue = val;
                foreach (var item in children)
                {
                    item.SetVisible(val);
                    if (val && item.isExpanded)
                        item.SetExpanded(true);
                    else if (!val)
                        item.SetExpanded(false);
                }
            }

            public void SetVisible(bool isVisible)
            {
                readToggle.SetVisible(isVisible);
                writeToggle.SetVisible(isVisible);
                boneNameFoldout?.SetVisible(isVisible);
                boneNameLabel?.SetVisible(isVisible);
            }

            public void SetDepth(int depth)
            {
                if (boneNameFoldout != null)
                    boneNameFoldout.style.paddingLeft = depth * 12 + 12;
                if (boneNameLabel != null)
                    boneNameLabel.style.paddingLeft = depth * 12 + 16;
            }
        }
        #endregion Draw Bone Settings
    }
}
