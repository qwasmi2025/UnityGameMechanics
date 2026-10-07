// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Profiling;

namespace GPUInstancerPro.CrowdAnimations
{
    public class GPUICrowdClipSamplerProvider : GPUIDataProvider<int, GPUICrowdClipSamplerProvider.GPUICrowdClipSampler>
    {
        public override void ReleaseBuffers()
        {
            base.ReleaseBuffers();

            foreach (var s in _dataDict.Values)
                s?.Dispose();
        }

        public override bool Remove(int key)
        {
            if (TryGetData(key, out var data))
                data.Dispose();
            return base.Remove(key);
        }

        public void DestroyClipSamplers()
        {
            foreach (var s in _dataDict.Values)
                s?.Dispose();
            _dataDict.Clear();
        }

        private static readonly Type[] BAKER_SAMPLE_COMPONENTS = new Type[] { typeof(Animator), typeof(Animation), typeof(GPUICrowdInstance)/*, typeof(SkinnedMeshRenderer)*/ };
        internal GPUICrowdClipSampler CreateClipSampler(GPUICrowdInstance crowdInstance)
        {
            int key = crowdInstance.GetInstanceIDGPUI();
            if (TryGetData(key, out var result))
                return result;
            result = new GPUICrowdClipSampler();
            AddOrSet(key, result);

            Profiler.BeginSample("GPUICrowdClipSamplerProvider.CreateClipSampler");
            GameObject prefabGO = crowdInstance.gameObject;
            if (crowdInstance.PrefabComponent != null && GPUIRenderingSystem.IsActive)
            {
                GameObject foundPrefabGO = GPUIRenderingSystem.Instance.LODGroupDataProvider.FindPrefabObjectFromPrefabID(crowdInstance.PrefabComponent.GetPrefabID());
                if (foundPrefabGO != null)
                    prefabGO = foundPrefabGO;
            }
#if GPUIPRO_DEVMODE
            Debug.Log(GPUIConstants.LOG_PREFIX + GPUIConstants.LOG_PREFIX_DEV + "Creating Crowd Sampler for: " + prefabGO.name, prefabGO);
#endif
            result.sampleGO = GPUIUtility.InstantiateWithStrippedComponents(prefabGO, Vector3.zero, Quaternion.identity, BAKER_SAMPLE_COMPONENTS);
            result.sampleGO.hideFlags = HideFlags.DontSave;
            result.sampleGO.name += "(Baker)";
            result.sampleGO.transform.localScale = Vector3.one;
            result.sampleCrowdInstance = result.sampleGO.GetComponent<GPUICrowdInstance>();

            Animator sampleAnimator = result.sampleGO.AddOrGetComponent<Animator>();
            sampleAnimator.enabled = true;
            sampleAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            sampleAnimator.applyRootMotion = true;
            result.sampleCrowdInstance.LoadBoneTransforms(false, true);

            result.playableGraph = PlayableGraph.Create("GPUIAnimationSampler");
            result.playableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            AnimationPlayableOutput.Create(result.playableGraph, "GPUIAnimationOutput", sampleAnimator);

            int boneCount = result.sampleCrowdInstance._crowdRig.GetBoneCount();
            result.boneTransforms = new();
            for (int b = 0; b < boneCount; b++)
            {
                Transform boneTransform = result.sampleCrowdInstance._boneTransforms[b].transform;
                if (boneTransform != null)
                    result.boneTransforms.Add(new() { boneTransform = boneTransform, boneIndex = b });
            }

            Profiler.EndSample();

            return result;
        }

        public class GPUICrowdClipSampler : IDisposable
        {
            public GameObject sampleGO;
            public GPUICrowdInstance sampleCrowdInstance;
            public PlayableGraph playableGraph;
            public List<SamplerBoneTransform> boneTransforms;

            public struct SamplerBoneTransform
            {
                public Transform boneTransform;
                public int boneIndex;
            }

            public void Dispose()
            {
                Profiler.BeginSample("GPUICrowdClipSamplerProvider.CleanBakerSample");
#if UNITY_EDITOR
                if (sampleCrowdInstance != null && sampleCrowdInstance._crowdRig != null)
                    UnityEditor.EditorUtility.SetDirty(sampleCrowdInstance._crowdRig);
#endif
                if (playableGraph.IsValid())
                    playableGraph.Destroy();
                if (sampleGO != null)
                    UnityEngine.Object.DestroyImmediate(sampleGO);
                boneTransforms = null;
                Profiler.EndSample();
            }
        }
    }
}
