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
    [CustomEditor(typeof(GPUICrowdComputePlayer))]
    public class GPUICrowdComputePlayerEditor : GPUIEditor
    {
        private GPUICrowdComputePlayer _computePlayer;
        private static VisualTreeAsset _computePlayerTemplate;

        ObjectField c1, c2, c3, c4;
        Slider w1, w2, w3, w4;
        FloatField s1, s2, s3, s4;
        Button playButton;

        protected override void OnEnable()
        {
            base.OnEnable();

            _computePlayer = (GPUICrowdComputePlayer)target;

            if (_computePlayer.crowdInstance == null)
                _computePlayer.Reset();

            _computePlayerTemplate ??= AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GPUICrowdEditorConstants.GetUIPath() + "GPUICrowdComputePlayerUI.uxml");
        }

        public override void DrawContentGUI(VisualElement root)
        {
            _computePlayerTemplate.CloneTree(root);

            c1 = root.Q<ObjectField>("Clip1PF");
            c2 = root.Q<ObjectField>("Clip2PF");
            c3 = root.Q<ObjectField>("Clip3PF");
            c4 = root.Q<ObjectField>("Clip4PF");
            c1.RegisterValueChangedCallback(OnClipChanged);
            c2.RegisterValueChangedCallback(OnClipChanged);
            c3.RegisterValueChangedCallback(OnClipChanged);
            c4.RegisterValueChangedCallback(OnClipChanged);

            w1 = root.Q<Slider>("Clip1Weight");
            w2 = root.Q<Slider>("Clip2Weight");
            w3 = root.Q<Slider>("Clip3Weight");
            w4 = root.Q<Slider>("Clip4Weight");
            w1.RegisterValueChangedCallback(OnWeightChanged);
            w2.RegisterValueChangedCallback(OnWeightChanged);
            w3.RegisterValueChangedCallback(OnWeightChanged);
            w4.RegisterValueChangedCallback(OnWeightChanged);

            s1 = root.Q<FloatField>("Clip1Speed");
            s2 = root.Q<FloatField>("Clip2Speed");
            s3 = root.Q<FloatField>("Clip3Speed");
            s4 = root.Q<FloatField>("Clip4Speed");
            s1.RegisterValueChangedCallback(OnSpeedChanged);
            s2.RegisterValueChangedCallback(OnSpeedChanged);
            s3.RegisterValueChangedCallback(OnSpeedChanged);
            s4.RegisterValueChangedCallback(OnSpeedChanged);

            playButton = root.Q<Button>("PlayButton");
            playButton.clicked += _computePlayer.Play;
            playButton.SetVisible(Application.isPlaying);
        }

        private void OnClipChanged(ChangeEvent<UnityEngine.Object> evt)
        {
        }

        private void OnWeightChanged(ChangeEvent<float> evt)
        {
            Vector4 weights = _computePlayer.weights;
            float newValue = evt.newValue;
            float remaining = 1f - newValue;

            if (evt.target == w1)
            {
                float totalOthers = weights.y + weights.z + weights.w;
                if (totalOthers == 0f)
                {
                    weights.x = newValue;
                    weights.y = remaining;
                    weights.z = 0f;
                    weights.w = 0f;
                }
                else
                {
                    float scale = remaining / totalOthers;
                    weights.x = newValue;
                    weights.y *= scale;
                    weights.z *= scale;
                    weights.w *= scale;
                }
            }
            else if (evt.target == w2)
            {
                float totalOthers = weights.x + weights.z + weights.w;
                if (totalOthers == 0f)
                {
                    weights.y = newValue;
                    weights.x = remaining;
                    weights.z = 0f;
                    weights.w = 0f;
                }
                else
                {
                    float scale = remaining / totalOthers;
                    weights.y = newValue;
                    weights.x *= scale;
                    weights.z *= scale;
                    weights.w *= scale;
                }
            }
            else if (evt.target == w3)
            {
                float totalOthers = weights.x + weights.y + weights.w;
                if (totalOthers == 0f)
                {
                    weights.z = newValue;
                    weights.x = remaining;
                    weights.y = 0f;
                    weights.w = 0f;
                }
                else
                {
                    float scale = remaining / totalOthers;
                    weights.z = newValue;
                    weights.x *= scale;
                    weights.y *= scale;
                    weights.w *= scale;
                }
            }
            else if (evt.target == w4)
            {
                float totalOthers = weights.x + weights.y + weights.z;
                if (totalOthers == 0f)
                {
                    weights.w = newValue;
                    weights.x = remaining;
                    weights.y = 0f;
                    weights.z = 0f;
                }
                else
                {
                    float scale = remaining / totalOthers;
                    weights.w = newValue;
                    weights.x *= scale;
                    weights.y *= scale;
                    weights.z *= scale;
                }
            }

            _computePlayer.weights = weights;

            w1.SetValueWithoutNotify(weights.x);
            w2.SetValueWithoutNotify(weights.y);
            w3.SetValueWithoutNotify(weights.z);
            w4.SetValueWithoutNotify(weights.w);

            serializedObject.Update();
        }

        private void OnSpeedChanged(ChangeEvent<float> evt)
        {
            Vector4 speeds = _computePlayer.speeds;

            if (evt.target == s1)
            {
                speeds.x = Mathf.Max(0f, evt.newValue);
            }
            else if (evt.target == s2)
            {
                speeds.y = Mathf.Max(0f, evt.newValue);
            }
            else if (evt.target == s3)
            {
                speeds.z = Mathf.Max(0f, evt.newValue);
            }
            else if (evt.target == s4)
            {
                speeds.w = Mathf.Max(0f, evt.newValue);
            }

            _computePlayer.speeds = speeds;

            s1.SetValueWithoutNotify(speeds.x);
            s2.SetValueWithoutNotify(speeds.y);
            s3.SetValueWithoutNotify(speeds.z);
            s4.SetValueWithoutNotify(speeds.w);

            serializedObject.Update();
        }

        public override string GetTitleText() => "GPUI Crowd Compute Player";
        public override string GetVersionNoText() => GPUICrowdEditorConstants.GetVersionNoText();
        public override string GetWikiURLParams() => "title=GPU_Instancer_Pro-Crowd_Animations#GPUI_Crowd_Compute_Player";
    }
}
