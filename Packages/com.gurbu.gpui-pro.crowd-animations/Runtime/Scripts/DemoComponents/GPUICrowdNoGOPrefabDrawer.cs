// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace GPUInstancerPro.CrowdAnimations
{
    [ExecuteInEditMode]
    public class GPUICrowdNoGOPrefabDrawer : MonoBehaviour
    {
        public GPUICrowdInstance crowdInstancePrefab;
        public GPUIProfile profile;
        public int randomSeed = 42;
        public AnimationClip[] animationClips;
        [SerializeField]
        [Range(0, 2000)]
        private int _instanceCount = 256;
        public Vector2 spacing = new Vector2(1, 1);
        public Quaternion rotation = Quaternion.identity;
        public bool3 randomRotation;
        public Vector3 scale = Vector3.one;
        public bool randomScale;
        public Vector2 randomScaleRange = new Vector2(0.5f, 3f);
        public Text instanceCountText;
        public float InstanceCount
        {
            set
            {
                _instanceCount = (int)value;
                if (_instanceCount < 0)
                    _instanceCount = 0;
                RegisterRenderers();
            }
        }

        private int _rendererKey;
        private GraphicsBuffer _colorBuffer;
        private int _currentInstanceCount;
        private const int MAX_INSTANCE_COUT = 2000;
        private Vector2 _currentSpacing;

        public void OnEnable()
        {
            RegisterRenderers();
        }

        public void OnDisable()
        {
            DisposeRenderers();
        }

        private void OnValidate()
        {
            if (GPUIRenderingSystem.IsActive && _rendererKey != 0)
                RegisterRenderers();
        }

        private void RegisterRenderers()
        {
            if (_instanceCount <= 0 || spacing != _currentSpacing)
                DisposeRenderers();
            if (_instanceCount > 0 && crowdInstancePrefab != null)
            {
                _instanceCount = Mathf.Min(_instanceCount, MAX_INSTANCE_COUT);

                if (_rendererKey == 0)
                {
                    GPUICoreAPI.RegisterRenderer(this, crowdInstancePrefab.gameObject, profile, out _rendererKey); // Register the prefab as renderer
                    GPUICoreAPI.SetTransformBufferData(_rendererKey, GenerateMatrixArray(MAX_INSTANCE_COUT)); // Set matrices for the renderer
                }
                GPUICoreAPI.SetInstanceCount(_rendererKey, _instanceCount);

#if UNITY_EDITOR
                if (!Application.isPlaying) // No need to play animations in edit mode.
                    return;
#endif

                GPUIAWComputeAnimator computeAnimator = GPUICrowdAPI.GetAnimatorWorkflow<GPUIAWComputeAnimator>(); // Get Compute Animator.
                GPUICrowdAPI.SetAnimatorWorkflowForAll(_rendererKey, computeAnimator); // Set Animator Workflow to Compute Animator for all instances.

                if (animationClips != null && animationClips.Length > 0 && animationClips[0] != null)
                {
                    int clipCount = animationClips.Length;
                    UnityEngine.Random.InitState(randomSeed);
                    for (int i = _currentInstanceCount; i < _instanceCount; i++)
                    {
                        AnimationClip clip = animationClips[UnityEngine.Random.Range(0, clipCount)];
                        if (clip == null)
                            continue;
                        computeAnimator.StartAnimation(_rendererKey, i, clip, UnityEngine.Random.Range(0f, 1f), 1f, 0f, true); // Start playing the animation clip for each instance.
                    }
                }
                _currentInstanceCount = _instanceCount;
            }
            else
                _currentInstanceCount = 0;

            if (instanceCountText != null)
                instanceCountText.text = _currentInstanceCount.FormatNumberWithSuffix();

        }
        
        private void DisposeRenderers()
        {
            if (_rendererKey != 0)
            {
                GPUICoreAPI.DisposeRenderer(_rendererKey); // Clear renderer data
                _rendererKey = 0;
            }
            if (_colorBuffer != null)
            {
                _colorBuffer.Dispose();
                _colorBuffer = null;
            }
            _currentInstanceCount = 0;
        }

        private Matrix4x4[] GenerateMatrixArray(int instanceCount)
        {
            _currentSpacing = spacing;
            Matrix4x4[] matrix4X4s = new Matrix4x4[instanceCount];
            Matrix4x4 matrix4X4 = Matrix4x4.TRS(Vector3.zero, rotation, scale);
            Vector3 originPos = transform.position;
            int size = Mathf.CeilToInt(Mathf.Sqrt(instanceCount));

            int[] dx = { 1, 0, -1, 0 };
            int[] dy = { 0, 1, 0, -1 };

            int x = 0, y = 0, index = 0, direction = 0;
            int steps = 1, stepIncreaseCounter = 0;
            UnityEngine.Random.InitState(randomSeed);

            while (index < instanceCount)
            {
                for (int i = 0; i < steps; i++)
                {
                    if (x >= 0 && x < size && y >= 0 && y < size)
                    {
                        Vector3 pos = new Vector3(x * spacing.x, 0, y * spacing.y) + originPos;
                        if (randomRotation.x || randomRotation.y || randomRotation.z || randomScale)
                        {
                            Vector3 rotEuler = rotation.eulerAngles;
                            Quaternion randomRotation = Quaternion.Euler(new Vector3(this.randomRotation.x ? UnityEngine.Random.Range(0, 360) : rotEuler.x, this.randomRotation.y ? UnityEngine.Random.Range(0, 360) : rotEuler.y, this.randomRotation.z ? UnityEngine.Random.Range(0, 360) : rotEuler.z));
                            matrix4X4.SetTRS(pos, randomRotation, randomScale ? scale * UnityEngine.Random.Range(randomScaleRange.x, randomScaleRange.y) : scale);
                        }
                        else
                            matrix4X4.SetPosition(pos);
                        matrix4X4s[index++] = matrix4X4;
                    }


                    x += dx[direction];
                    y += dy[direction];

                    if (index >= instanceCount)
                        break;
                }

                direction = (direction + 1) % 4;
                stepIncreaseCounter++;

                if (stepIncreaseCounter % 2 == 0) steps++;
            }

            return matrix4X4s;
        }

        public void SetInstanceCount(int instanceCount)
        {
            this._instanceCount = instanceCount;
            RegisterRenderers();
        }
    }
}
