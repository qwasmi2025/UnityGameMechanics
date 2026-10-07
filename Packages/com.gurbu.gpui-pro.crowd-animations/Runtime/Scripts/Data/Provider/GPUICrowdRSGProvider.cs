// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System.Collections.Generic;
using Unity.Collections;

namespace GPUInstancerPro.CrowdAnimations
{
    /// <summary>
    /// Keeps the data required for each RenderSourceGroup that uses crowd animations. Key is the GPUIRenderSourceGroup.Key.
    /// </summary>
    public class GPUICrowdRSGProvider : GPUIDataProvider<int, GPUICrowdRenderSourceGroup>
    {
        public override void ReleaseBuffers()
        {
            base.ReleaseBuffers();

            foreach (var kvPair in _dataDict)
            {
                if (kvPair.Value != null)
                    kvPair.Value.Dispose();
            }
        }

        public virtual void ExecuteCrowdAnimator()
        {
            if (_dataDict == null)
                return;
            foreach (var data in Values)
                data.ExecuteCrowdAnimator();
        }

        public override bool Remove(int key)
        {
            if (TryGetData(key, out var data))
                data.Dispose();
            return base.Remove(key);
        }

        public void RemoveNullValues()
        {
            if (_dataDict == null)
                return;
            List<int> nullKeys = new();
            foreach (var kvp in _dataDict)
            {
                if (kvp.Value.rig == null)
                    nullKeys.Add(kvp.Key);
            }
            foreach (int key in nullKeys)
            {
                _dataDict[key].Dispose();
                _dataDict.Remove(key);
            }
        }

        public bool TryGetDataWithRenderSourceKey(int renderSourceKey, out GPUICrowdRenderSourceGroup crowdRSG)
        {
            crowdRSG = null;
            if (renderSourceKey == 0 || !GPUIRenderingSystem.IsActive || !GPUIRenderingSystem.TryGetRenderSource(renderSourceKey, out var renderSource))
                return false;
            return TryGetData(renderSource.renderSourceGroup.Key, out crowdRSG);
        }
    }

    public class GPUICrowdRenderSourceProvider : GPUIDataProvider<int, GPUICrowdRenderSource>
    {
        public override void ReleaseBuffers()
        {
            base.ReleaseBuffers();

            foreach (var kvPair in _dataDict)
            {
                if (kvPair.Value != null)
                    kvPair.Value.Dispose();
            }
        }

        public override bool Remove(int key)
        {
            if (TryGetData(key, out var data))
                data.Dispose();
            return base.Remove(key);
        }

        public bool TryGetOrCreateData(int renderSourceKey, out GPUICrowdRenderSource crowdRenderSource)
        {
            if (!TryGetData(renderSourceKey, out crowdRenderSource))
            {
                if (renderSourceKey == 0 || !GPUIRenderingSystem.IsActive 
                    || !GPUIRenderingSystem.TryGetRenderSource(renderSourceKey, out var renderSource) 
                    || !GPUICrowdSkinningSystem.TryGetCrowdRenderSourceGroupWithRenderKey(renderSourceKey, out var crowdRSG))
                    return false;
                crowdRenderSource = new GPUICrowdRenderSource(renderSource, crowdRSG);
                _dataDict[renderSourceKey] = crowdRenderSource;
            }
            return true;
        }
    }
}
