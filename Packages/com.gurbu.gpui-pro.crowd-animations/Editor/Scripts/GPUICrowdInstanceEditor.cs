// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GPUInstancerPro.CrowdAnimations
{
    [CustomEditor(typeof(GPUICrowdInstance)), CanEditMultipleObjects]
    public class GPUICrowdInstanceEditor : GPUIEditor
    {
        private GPUICrowdInstance[] _gpuiCrowdInstances;
        private GPUIAnimatorWorkflowBase[] _animatorWorkflows;
        private List<int> _animatorWorkflowChoices;

        private VisualElement _rootElement;
        private static bool _rigDataFoldoutValue;
        private static bool _boneTransformsFoldoutValue;
        private static VisualTreeAsset _activeClipsTemplate;

        protected override void OnEnable()
        {
            base.OnEnable();

            _gpuiCrowdInstances = new GPUICrowdInstance[targets.Length];
            for (int i = 0; i < _gpuiCrowdInstances.Length; i++)
            {
                _gpuiCrowdInstances[i] = targets[i] as GPUICrowdInstance;
                _gpuiCrowdInstances[i].LoadRig();
            }
            serializedObject.Update();

            _animatorWorkflows = GPUICrowdSkinningSystem.GetAnimatorWorkflows();
            _animatorWorkflowChoices = new();
            for (int i = 0; i < _animatorWorkflows.Length; i++)
            {
                GPUIAnimatorWorkflowBase aw = _animatorWorkflows[i];
                _animatorWorkflowChoices.Add(aw.GetID());
            }

            _activeClipsTemplate ??= AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GPUICrowdEditorConstants.GetUIPath() + "GPUICrowdInstanceActiveClipsUI.uxml");

            EditorApplication.update -= UpdateComputeAnimatorInfo;
            if (_gpuiCrowdInstances.Length == 1 && Application.isPlaying)
                EditorApplication.update += UpdateComputeAnimatorInfo;
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            EditorApplication.update -= UpdateComputeAnimatorInfo;
        }

        public override void DrawContentGUI(VisualElement contentElement)
        {
            _rootElement = contentElement;
            DrawCrowdInstance();
        }

        private void DrawCrowdInstance()
        {
            _rootElement.Clear();

            if (_gpuiCrowdInstances.Length == 0)
                return;
            GPUICrowdInstance crowdInstance0 = _gpuiCrowdInstances[0];

            if (!GPUICrowdUtility.IsReadWriteEnabledOnModel(crowdInstance0.gameObject))
                GPUIEditorUtility.DrawErrorMessage(_rootElement, -6103, null, EnableReadWriteOnModel);

            #region Animator Workflow Field
            SerializedProperty animatorWorkflowIDSP = serializedObject.FindProperty("_animatorWorkflowID");
            //_rootElement.Add(DrawSerializedProperty(animatorWorkflowIDSP));

            int awIndex = -1;
            int awID = animatorWorkflowIDSP.intValue;
            bool isAWMixed = false;
            for (int i = 0; i < _animatorWorkflows.Length; i++)
            {
                if (_animatorWorkflows[i].GetID() == awID)
                {
                    awIndex = i;
                    break;
                }
            }
            for (int i = 0; i < _gpuiCrowdInstances.Length; i++)
            {
                if (awID != _gpuiCrowdInstances[i].AnimatorWorkflowID)
                {
                    isAWMixed = true;
                    break;
                }
            }

            GPUIEditorTextUtility.TryGetGPUIText("_animatorWorkflowID", out var gpuiText);
            if (awIndex >= 0)
            {
                PopupField<int> awPopup = new PopupField<int>(gpuiText.title, _animatorWorkflowChoices, awIndex)
                {
                    tooltip = gpuiText.tooltip,
                    showMixedValue = isAWMixed,
                    bindingPath = animatorWorkflowIDSP.propertyPath,
                    formatListItemCallback = OnAnimatorWorkflowFormatListItem,
                    formatSelectedValueCallback = OnAnimatorWorkflowFormatListItem
                };
                awPopup.BindProperty(serializedObject);
                GPUIEditorUtility.RegisterResizeLabelEvent(awPopup);
                _rootElement.Add(awPopup);
                awPopup.RegisterValueChangedCallback((evt) =>
                {
                    if (animatorWorkflowIDSP.intValue == awID) return;
                    if (Application.isPlaying && awID == GPUIAWBoneTracker.WORKFLOW_ID)
                    {
                        for (int i = 0; i < _gpuiCrowdInstances.Length; i++)
                            _gpuiCrowdInstances[i].ClearAllBoneReadWrite();
                    }
                    awID = animatorWorkflowIDSP.intValue;
                    OnInstancingStatusModified();
                    DrawCrowdInstance();
                });
                GPUIEditorUtility.DrawHelpText(_helpBoxes, gpuiText, _rootElement);

                if (animatorWorkflowIDSP.intValue == GPUIAWBoneTracker.WORKFLOW_ID)
                {
                    if (crowdInstance0.Animator != null && !crowdInstance0.Animator.hasTransformHierarchy)
                        GPUIEditorUtility.DrawErrorMessage(_rootElement, -6102, null, DeoptimizeGameObjects);
                }
            }
            else
            {
                // When not able to match the ID with any workflow.
                animatorWorkflowIDSP.intValue = GPUIAWComputeAnimator.WORKFLOW_ID;
                DrawCrowdInstance();
                return;
            }
            #endregion Animator Workflow Field

            #region Apply Root Motion Field
            if (GPUICrowdSkinningSystem.HasAnimatorWorkflowInternalAnimator(crowdInstance0.AnimatorWorkflowID))
            {
                SerializedProperty applyRootMotionSP = serializedObject.FindProperty("_applyRootMotion");
                bool isApplyRootMotion = applyRootMotionSP.boolValue;
                _rootElement.Add(DrawSerializedProperty(applyRootMotionSP, out var applyRootMotionPF));
                applyRootMotionPF.RegisterValueChangeCallback((evt) =>
                {
                    if (applyRootMotionSP.boolValue == isApplyRootMotion) return;
                    isApplyRootMotion = applyRootMotionSP.boolValue;
                    OnInstancingStatusModified();
                });
            }
            #endregion Apply Root Motion Field

            if (_gpuiCrowdInstances.Length != 1)
                return;

            #region Bone Transforms Field
            if (crowdInstance0.Rig != null)
            {
                var boneTransformsVE = new VisualElement();
                _rootElement.Add(boneTransformsVE);
                var boneTransformsFoldout = new Foldout();
                boneTransformsFoldout.value = false;
                boneTransformsVE.Add(boneTransformsFoldout);
                if (crowdInstance0.BoneTransforms == null)
                    crowdInstance0.LoadBoneTransforms();
                GPUICrowdEditorUtility.DrawBoneSettings(crowdInstance0, boneTransformsFoldout);

                if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(crowdInstance0))
                {
                    Button reloadBoneTransformsButton = new Button();
                    reloadBoneTransformsButton.text = "Reload Bones";
                    reloadBoneTransformsButton.style.position = Position.Absolute;
                    reloadBoneTransformsButton.style.width = 120;
                    reloadBoneTransformsButton.style.right = 5;
                    boneTransformsVE.Add(reloadBoneTransformsButton);
                    reloadBoneTransformsButton.clicked += () =>
                    {
                        crowdInstance0.ReloadBoneTransforms();
                        DrawCrowdInstance();
                    };
                }
            }
            #endregion Bone Transforms Field

            #region Rig Data Field
            VisualElement rigDataContainer = new VisualElement();
            rigDataContainer.style.marginTop = 5;
            _rootElement.Add(rigDataContainer);
            DrawRigData(crowdInstance0, rigDataContainer);
            #endregion Rig Data Field

            DrawComputeAnimatorInfo();
        }

        private void DrawRigData(GPUICrowdInstance crowdInstance, VisualElement rigDataContainer)
        {
            rigDataContainer.Clear();

            bool isCrowdInstancePrefabAsset = PrefabUtility.IsPartOfPrefabAsset(crowdInstance);
            if (!isCrowdInstancePrefabAsset && !Application.isPlaying)
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.prefabContentsRoot)
                    isCrowdInstancePrefabAsset = crowdInstance.transform.IsChildOf(stage.prefabContentsRoot.transform);
            }

            var rigData = crowdInstance.LoadRig();
            GPUIEditorTextUtility.TryGetGPUIText("_crowdRig", out var gpuiText);
            Foldout rigDataFoldout = new Foldout();
            rigDataFoldout.value = _rigDataFoldoutValue;
            rigDataFoldout.text = gpuiText.title;
            rigDataFoldout.tooltip = null;
            rigDataFoldout.AddToClassList("gpui-border");
            rigDataFoldout.AddToClassList("gpui-bg-light");
            rigDataFoldout.style.marginLeft = 0;
            rigDataFoldout.style.paddingLeft = 12;
            rigDataContainer.SetVisible(Application.isPlaying || isCrowdInstancePrefabAsset || rigData != null);
            rigDataFoldout.RegisterValueChangedCallback((evt) =>
            {
                if (evt.target == rigDataFoldout)
                    _rigDataFoldoutValue = rigDataFoldout.value;
            });
            rigDataContainer.Add(rigDataFoldout);
            GPUIEditorUtility.DrawHelpText(_helpBoxes, gpuiText, rigDataContainer);
            Toggle rigDataFoldoutHeader = rigDataFoldout.Q<Toggle>();
            if (rigDataFoldoutHeader != null)
                rigDataFoldoutHeader.tooltip = gpuiText.tooltip;

            bool isRigDataAsset = false;
            if (rigData != null)
                isRigDataAsset= AssetDatabase.Contains(rigData);

            if (rigData != null && !isRigDataAsset && isCrowdInstancePrefabAsset)
            {
                var rigDataField = new ObjectField("");
                rigDataField.objectType = typeof(GPUICrowdRig);
                rigDataField.value = null;
                rigDataField.SetEnabled(!Application.isPlaying);
                rigDataField.style.position = Position.Absolute;
                rigDataField.style.left = GPUIEditorConstants.LABEL_WIDTH;
                rigDataField.style.right = 5;
                rigDataField.style.top = 5;
                rigDataField.tooltip = gpuiText.tooltip;

                rigDataField.RegisterValueChangedCallback((evt) =>
                {
                    if (evt.newValue is not GPUICrowdRig newRigData)
                        return;
                    if (rigData == newRigData)
                        return;
                    crowdInstance.SetRig(newRigData);
                    GPUIPrefabUtility.RevertPropertyOnAllPrefabInstances<GPUICrowdInstance>(crowdInstance.gameObject, "_crowdRig");
                });
                GPUIEditorUtility.RegisterResizeLabelEvent(rigDataField);
                rigDataContainer.Add(rigDataField);
            }
            else
            {
                VisualElement crowdRigVE = DrawSerializedProperty(serializedObject.FindProperty("_crowdRig"), "", out var crowdRigPF);
                crowdRigVE.style.position = Position.Absolute;
                crowdRigVE.style.left = GPUIEditorConstants.LABEL_WIDTH;
                crowdRigVE.style.right = 5;
                crowdRigVE.style.top = 5;
                rigDataContainer.Add(crowdRigVE);

                crowdRigPF.tooltip = gpuiText.tooltip;
                crowdRigPF.SetEnabled(isCrowdInstancePrefabAsset && !Application.isPlaying);
                if (isCrowdInstancePrefabAsset && !Application.isPlaying)
                    crowdRigPF.RegisterValueChangeCallback((evt) =>
                    {
                        if (rigData == crowdInstance.Rig)
                            return;
                        rigData = crowdInstance.Rig;
                        GPUICrowdSkinningSystem.OnCrowdInstanceRigSet(crowdInstance);
                        GPUIPrefabUtility.RevertPropertyOnAllPrefabInstances<GPUICrowdInstance>(crowdInstance.gameObject, "_crowdRig");
                        DrawCrowdInstance();
                    });
            }

            if (isCrowdInstancePrefabAsset && !isRigDataAsset && !Application.isPlaying)
            {
                rigDataContainer.Add(GPUIEditorUtility.CreateGPUIHelpBox("createCrowdRigInfo", null, () =>
                {
                    GPUICrowdUtility.CreateRig(crowdInstance.gameObject, false, true);
                    DrawCrowdInstance();
                },
                HelpBoxMessageType.Info, "Create", GPUIEditorConstants.Colors.green));
            }

            if (rigData != null)
            {
                rigDataFoldout.SetEnabled(isCrowdInstancePrefabAsset && isRigDataAsset);
                rigDataFoldout.contentContainer.style.marginLeft = 0;
                GPUICrowdRigEditor.DrawCrowdRig(rigData, new SerializedObject(rigData), rigDataFoldout, _helpBoxes, crowdInstance);
            }
        }

        private VisualElement _activeClipsVE;
        private ObjectField[] _clipOFs;
        private Slider[] _weightSliders, _timeSliders;
        private FloatField[] _speeds;
        private void DrawComputeAnimatorInfo()
        {
            if (_gpuiCrowdInstances.Length != 1)
                return;

            _clipOFs = new ObjectField[4];
            _weightSliders = new Slider[4];
            _timeSliders = new Slider[4];
            _speeds = new FloatField[4];

            _activeClipsTemplate.CloneTree(_rootElement);
            _activeClipsVE = _rootElement.Q("ActiveClipsVE");
            _clipOFs[0] = _rootElement.Q<ObjectField>("Clip1OF");
            _clipOFs[1] = _rootElement.Q<ObjectField>("Clip2OF");
            _clipOFs[2] = _rootElement.Q<ObjectField>("Clip3OF");
            _clipOFs[3] = _rootElement.Q<ObjectField>("Clip4OF");
            _weightSliders[0] = _rootElement.Q<Slider>("Clip1Weight");
            _weightSliders[1] = _rootElement.Q<Slider>("Clip2Weight");
            _weightSliders[2] = _rootElement.Q<Slider>("Clip3Weight");
            _weightSliders[3] = _rootElement.Q<Slider>("Clip4Weight");
            _timeSliders[0] = _rootElement.Q<Slider>("Clip1Time");
            _timeSliders[1] = _rootElement.Q<Slider>("Clip2Time");
            _timeSliders[2] = _rootElement.Q<Slider>("Clip3Time");
            _timeSliders[3] = _rootElement.Q<Slider>("Clip4Time");
            _speeds[0] = _rootElement.Q<FloatField>("Speed1");
            _speeds[1] = _rootElement.Q<FloatField>("Speed2");
            _speeds[2] = _rootElement.Q<FloatField>("Speed3");
            _speeds[3] = _rootElement.Q<FloatField>("Speed4");

            _activeClipsVE.SetEnabled(false);
            _activeClipsVE.SetVisible(false);
        }

        private List<GPUICrowdAnimatorClipData> _clipDataList;
        private List<AnimationClip> _clipList;
        private const float _computeAnimatorInfoUpdateInterval = 0.25f;
        private float _lastComputeAnimatorInfoUpdateTime;
        private void UpdateComputeAnimatorInfo()
        {
            if (_activeClipsVE == null)
                return;

            float currentTime = Time.time;
            if (currentTime - _lastComputeAnimatorInfoUpdateTime < _computeAnimatorInfoUpdateInterval)
                return;
            _lastComputeAnimatorInfoUpdateTime = currentTime;

            _activeClipsVE.SetVisible(false);
            GPUICrowdInstance crowdInstance0 = _gpuiCrowdInstances[0];
            if (GPUICrowdSkinningSystem.IsAnimatorWorkflowBoneDataProvider(crowdInstance0.AnimatorWorkflowID))
                return;

            _clipDataList ??= new();
            _clipList ??= new();
            if (!crowdInstance0.TryGetCrowdAnimatorClipData(_clipDataList, out var weights, _clipList) || _clipDataList.Count == 0)
                return;

            _activeClipsVE.SetVisible(true);

            for (int i = 0; i < 4; i++)
            {
                if (i >= _clipDataList.Count)
                {
                    _clipOFs[i].SetVisible(false);
                    _weightSliders[i].SetVisible(false);
                    _timeSliders[i].SetVisible(false);
                    _speeds[i].SetVisible(false);
                }
                else
                {
                    if (_clipOFs[i].value != _clipList[i])
                        _clipOFs[i].SetValueWithoutNotify(_clipList[i]);
                    if (_weightSliders[i].value != weights[i])
                        _weightSliders[i].SetValueWithoutNotify(weights[i]);
                    float normalizedTime = _clipDataList[i].GetNormalizedTime(currentTime) % 1f;
                    if (_timeSliders[i].value != normalizedTime)
                        _timeSliders[i].SetValueWithoutNotify(normalizedTime);
                    float speed = (float)Math.Round(_clipList[i].length / _clipDataList[i].SpeedRelativeClipLength, 2);
                    if (_speeds[i].value != speed)
                        _speeds[i].SetValueWithoutNotify(speed);

                    _clipOFs[i].SetVisible(true);
                    _weightSliders[i].SetVisible(true);
                    _timeSliders[i].SetVisible(true);
                    _speeds[i].SetVisible(true);
                }
            }
        }

        private void EnableReadWriteOnModel()
        {
            if (GPUICrowdUtility.EnableReadWriteOnModel(_gpuiCrowdInstances[0].gameObject)) 
                DrawCrowdInstance();
        }

        private void DeoptimizeGameObjects()
        {
            if (DeoptimizeGameObjects(_gpuiCrowdInstances[0]))
                DrawCrowdInstance();
        }

        public static bool DeoptimizeGameObjects(GPUICrowdInstance crowdInstance)
        {
            if (EditorUtility.DisplayDialog("Deoptimize GameObjects", "Do you wish to disable the Optimize GameObjects option on the Model Importer settings?", "Yes", "No"))
                if (GPUICrowdUtility.DeoptimizeTransformHierarchyOnModel(crowdInstance))
                    return true;
            return false;
        }

        private string OnAnimatorWorkflowFormatListItem(int arg)
        {
            return GPUICrowdSkinningSystem.GetAnimatorWorkflow(arg).GetName();
        }

        private void OnInstancingStatusModified()
        {
            if (!Application.isPlaying)
                return;
#if GPUIPRO_DEVMODE
            Debug.Log(GPUIConstants.LOG_PREFIX + GPUIConstants.LOG_PREFIX_DEV + "OnInstancingStatusModified");
#endif
            for (int i = 0; i < _gpuiCrowdInstances.Length; i++)
                _gpuiCrowdInstances[i].OnInstancingStatusModified();
        }

        public override string GetTitleText() => "GPUI Crowd Instance";

        public override string GetVersionNoText() => GPUICrowdEditorConstants.GetVersionNoText();

        public override string GetWikiURLParams() => "title=GPU_Instancer_Pro-Crowd_Animations#GPUI_Crowd_Instance";
    }
}
