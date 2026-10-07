// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using UnityEngine;
using UnityEngine.UI;

namespace GPUInstancerPro.CrowdAnimations
{
    [DefaultExecutionOrder(1200)]
    public class GPUICrowdHybridDemoController : MonoBehaviour
    {
        public GPUIManager prefabManager;
        public GPUICrowdBatchComputePlayer batchComputePlayer;
        public Slider totalInstanceCountSlider;
        public Text computeAnimatorCountText;
        public Slider computeAnimatorCountSlider;
        public Text mecanimReaderCountText;
        public Slider mecanimReaderCountSlider;
        public Text boneTrackerCountText;
        public Slider boneTrackerCountSlider;

        private int _currentInstanceCount;
        private int _caCount;
        private int _mrCount;
        private int _btCount;
        private bool _updateWorkflows;

        private void Start()
        {
            OnInstanceCountsModified(totalInstanceCountSlider);
        }

        public void OnInstanceCountsModified(Slider modifyingSlider)
        {
            _currentInstanceCount = (int)totalInstanceCountSlider.value;

            if (_currentInstanceCount == 0)
            {
                _caCount = _mrCount = _btCount = 0;
                computeAnimatorCountText.text = "0";
                mecanimReaderCountText.text = "0";
                boneTrackerCountText.text = "0";
                return;
            }

            // Read current slider values
            float ca = computeAnimatorCountSlider.value;
            float mr = mecanimReaderCountSlider.value;
            float bt = boneTrackerCountSlider.value;

            // Capture modifying value
            float modifyingValue = modifyingSlider.value;
            float remaining = 1f - modifyingValue;
            if (remaining < 0f) remaining = 0f;

            // Redistribute remaining value to other sliders fairly
            float v1 = 0f, v2 = 0f;
            if (modifyingSlider == computeAnimatorCountSlider)
            {
                float total = mr + bt;
                if (total > 1e-5f)
                {
                    v1 = (mr / total) * remaining;
                    v2 = (bt / total) * remaining;
                }
                else
                {
                    v1 = v2 = remaining * 0.5f;
                }

                ca = modifyingValue;
                mr = v1;
                bt = v2;
            }
            else if (modifyingSlider == mecanimReaderCountSlider)
            {
                float total = ca + bt;
                if (total > 1e-5f)
                {
                    v1 = (ca / total) * remaining;
                    v2 = (bt / total) * remaining;
                }
                else
                {
                    v1 = v2 = remaining * 0.5f;
                }

                mr = modifyingValue;
                ca = v1;
                bt = v2;
            }
            else if (modifyingSlider == boneTrackerCountSlider)
            {
                float total = ca + mr;
                if (total > 1e-5f)
                {
                    v1 = (ca / total) * remaining;
                    v2 = (mr / total) * remaining;
                }
                else
                {
                    v1 = v2 = remaining * 0.5f;
                }

                bt = modifyingValue;
                ca = v1;
                mr = v2;
            }

            // Calculate instance counts
            _caCount = Mathf.RoundToInt(ca * _currentInstanceCount);
            _mrCount = Mathf.RoundToInt(mr * _currentInstanceCount);
            _btCount = _currentInstanceCount - _caCount - _mrCount; // ensure total is exact

            // Update sliders (except the one being changed)
            if (modifyingSlider != computeAnimatorCountSlider)
                computeAnimatorCountSlider.SetValueWithoutNotify(ca);
            if (modifyingSlider != mecanimReaderCountSlider)
                mecanimReaderCountSlider.SetValueWithoutNotify(mr);
            if (modifyingSlider != boneTrackerCountSlider)
                boneTrackerCountSlider.SetValueWithoutNotify(bt);

            // Update text
            computeAnimatorCountText.text = _caCount.ToString();
            mecanimReaderCountText.text = _mrCount.ToString();
            boneTrackerCountText.text = _btCount.ToString();

            _updateWorkflows = true;
        }

        private void Update()
        {
            if (_updateWorkflows && SetHybridWorkflows())
                _updateWorkflows = false;
        }

        private bool SetHybridWorkflows()
        {
            var crowdInstances = batchComputePlayer.GetCrowdInstances();
            if (crowdInstances.Count < _currentInstanceCount)
                return false;
            for (int i = 0; i < crowdInstances.Count; i++)
            {
                var crowdInstance = crowdInstances[i];
                if (crowdInstance == null)
                    continue;
                if (i < _btCount)
                    crowdInstance.AnimatorWorkflowID = GPUIAWBoneTracker.WORKFLOW_ID;
                else
                {
                    AnimatorUtility.OptimizeTransformHierarchy(crowdInstance.gameObject, null);
                    if (i <  _btCount + _mrCount)
                        crowdInstances[i].AnimatorWorkflowID = GPUIAWMecanimReader.WORKFLOW_ID;
                    else
                        crowdInstances[i].AnimatorWorkflowID = GPUIAWComputeAnimator.WORKFLOW_ID;
                }
            }
            batchComputePlayer.RestartAnimations();
            return true;
        }

        public void EnablePrefabManager(bool enabled)
        {
            prefabManager.gameObject.SetActive(enabled);
            computeAnimatorCountSlider.interactable = enabled;
            mecanimReaderCountSlider.interactable = enabled;
            boneTrackerCountSlider.interactable = enabled;
            if (enabled)
            {
                totalInstanceCountSlider.GetComponent<RectTransform>().parent.GetComponent<Image>().color = new Color(27f / 255f, 168f / 255f, 0f, 200f / 255f);
                OnInstanceCountsModified(totalInstanceCountSlider);
            }
            else
            {
                totalInstanceCountSlider.GetComponent<RectTransform>().parent.GetComponent<Image>().color = new Color(168f / 255f, 27f / 255f, 0f, 200f / 255f);

                computeAnimatorCountText.text = "0";
                mecanimReaderCountText.text = "0";
                boneTrackerCountText.text = "0";

                var crowdInstances = batchComputePlayer.GetCrowdInstances();
                for (int i = 0; i < crowdInstances.Count; i++)
                {
                    crowdInstances[i].Animator.enabled = true;
                }
            }
        }
    }
}
