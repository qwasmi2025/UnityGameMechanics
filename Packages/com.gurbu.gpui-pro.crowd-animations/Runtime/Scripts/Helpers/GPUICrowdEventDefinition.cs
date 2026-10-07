// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;
using System.Collections;

namespace GPUInstancerPro.CrowdAnimations
{
    /// <summary>
    /// Using the GPUI Crowd Event Definition, you can define events at specific times within animation clips for selected prototypes when using Compute Animator. 
    /// </summary>
#if !UNITY_6000_3_0 && !GPUIPRO_NO_HELPURL
    [HelpURL("https://wiki.gurbu.com/index.php?title=GPU_Instancer_Pro-Crowd_Animations#GPUI_Crowd_Event_Definition")]
#endif
    [DefaultExecutionOrder(1200)]
    public class GPUICrowdEventDefinition : MonoBehaviour
    {
        [SerializeField]
        public bool isDontDestroyOnLoad = true;
        [SerializeField]
        public GPUICrowdInstance crowdPrefab;
        [SerializeField]
        public List<GPUICrowdClipEventDefinition> clipEventDefinitions;

        [NonSerialized]
        private int _renderKey;
        [NonSerialized]
        private int _prefabID;
        [NonSerialized]
        private GPUICrowdRenderSourceGroup _registeredCrowdRSG;
        [NonSerialized]
        private bool _isRegisteredEvents;

        private void OnEnable()
        {
            if (Application.isPlaying && isDontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);

            if (crowdPrefab == null || clipEventDefinitions == null || clipEventDefinitions.Count == 0)
                return;

            if (crowdPrefab.TryGetComponent<GPUIPrefabBase>(out var prefabComponent))
                _prefabID = prefabComponent.GetPrefabID();

            StartCoroutine(RegisterEvents());
        }

        private IEnumerator RegisterEvents()
        {
            while (!GPUIRenderingSystem.IsActive)
                yield return null;
            int rsgKey = 0;
            while (rsgKey == 0)
            {
                foreach (var rsg in GPUIRenderingSystem.Instance.RenderSourceGroupProvider.Values)
                {
                    var prototype = rsg.Prototype;
                    if (prototype == null)
                        continue;
                    if (prototype.prefabObject == crowdPrefab.gameObject)
                    {
                        rsgKey = rsg.Key;
                        break;
                    }
                    else if (_prefabID != 0 && prototype.prefabObject.TryGetComponent<GPUIPrefabBase>(out var prefabComponent) && _prefabID == prefabComponent.GetPrefabID())
                    {
                        rsgKey = rsg.Key;
                        break;
                    }
                }
                yield return null;
            }

            if (!GPUICrowdSkinningSystem.TryGetCrowdRenderSourceGroup(rsgKey, out _registeredCrowdRSG))
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not find Crowd Render Source Group definition for key: " + rsgKey);
            }
            else
            {
                foreach (var clipEventDefinition in clipEventDefinitions)
                {
                    if (clipEventDefinition == null || clipEventDefinition.animationClip == null || clipEventDefinition.animationEvents == null || clipEventDefinition.animationEvents.Count == 0)
                        continue;
                    _registeredCrowdRSG.AddAnimationEvents(clipEventDefinition.animationClip, clipEventDefinition.animationEvents);
                    _isRegisteredEvents = true;
                }
            }
        }

        private void OnDisable()
        {
            if (!_isRegisteredEvents)
                return;
            if (_registeredCrowdRSG.IsInitialized)
            {
                foreach (var clipEventDefinition in clipEventDefinitions)
                {
                    if (clipEventDefinition == null || clipEventDefinition.animationClip == null || clipEventDefinition.animationEvents == null || clipEventDefinition.animationEvents.Count == 0)
                        continue;
                    _registeredCrowdRSG.RemoveAnimationEvents(clipEventDefinition.animationClip, clipEventDefinition.animationEvents);
                }
            }
            _registeredCrowdRSG = null;
        }

        public void ExampleEvent(GPUICrowdInstance crowdInstance, GPUICrowdEventParameters parameters)
        {
            if (crowdInstance == null)
                Debug.Log(GPUIConstants.LOG_PREFIX + "Triggered event with floatParam: " + parameters.floatParam + " intParam:" + parameters.intParam + " stringParam: " + parameters.stringParam);
            else
                Debug.Log(GPUIConstants.LOG_PREFIX + "Triggered event for " + crowdInstance.name + " floatParam: " + parameters.floatParam + " intParam:" + parameters.intParam + " stringParam: " + parameters.stringParam, crowdInstance.gameObject);
        }

        public void ExampleStartAnimationEvent(GPUICrowdInstance crowdInstance, GPUICrowdEventParameters parameters)
        {
            if (crowdInstance == null || string.IsNullOrEmpty(parameters.stringParam))
                return;
            AnimationClip clip = crowdInstance.Rig.GetBakedAnimationClipWithName(parameters.stringParam);
            if (clip == null)
            {
                Debug.LogWarning(GPUIConstants.LOG_PREFIX + "Can not find animation clip with name: " + parameters.stringParam);
                return;
            }
            crowdInstance.StartAnimation(clip, 0f, 1f, parameters.floatParam);
        }
    }

    [Serializable]
    public class GPUICrowdClipEventDefinition
    {
        public AnimationClip animationClip;
        public List<GPUICrowdAnimationEvent> animationEvents;
    }

    [Serializable]
    public struct GPUICrowdEventParameters
    {
        public float floatParam;
        public int intParam;
        public string stringParam;
    }

    [Serializable]
    public class GPUICrowdAnimationEvent : UnityEvent<GPUICrowdInstance, GPUICrowdEventParameters>
    {
        [SerializeField]
        [Range(0f, 1f)]
        public float normalizedTriggerTime;
        [SerializeField]
        public GPUICrowdEventParameters parameters;

        public bool IsInvokeEvent(GPUICrowdAnimatorClipData clipData, float currentTime, float deltaTime)
        {
            if (clipData.packedSpeedRelativeLengthAndSync == 0f)
                return false;

            float speedRelativeClipLength = Math.Abs(clipData.packedSpeedRelativeLengthAndSync);

            float playTimeNow = currentTime - clipData.clipStartTime;
            float playTimeBefore = playTimeNow - deltaTime;

            float currentNormalizedTime = playTimeNow / speedRelativeClipLength;
            float previousNormalizedTime = playTimeBefore / speedRelativeClipLength;

            if (clipData.packedFrameCountLoopAndSpeed > 0) // IsLooping
            {
                currentNormalizedTime %= 1f;
                previousNormalizedTime %= 1f;
            }
            else
            {
                currentNormalizedTime = Math.Min(currentNormalizedTime, 1f);
                previousNormalizedTime = Math.Min(previousNormalizedTime, 1f);
            }

            return currentNormalizedTime != previousNormalizedTime && 
                ((previousNormalizedTime < normalizedTriggerTime && currentNormalizedTime >= normalizedTriggerTime)
                || (previousNormalizedTime > currentNormalizedTime && previousNormalizedTime < normalizedTriggerTime) // For looping animations that started a new loop.
                );
        }

        public void InvokeEvent(GPUICrowdInstance crowdInstance)
        {
            Invoke(crowdInstance, parameters);
        }
    }
}