using UnityEngine;

namespace PlagueRats
{
    public class SyntyPlayerDeath : MonoBehaviour
    {
        [Header("References")]
        public Animator animator;
        public MonoBehaviour locomotionController;
        public CharacterController characterController;

        [Header("Death layer")]
        public string deathLayerName = "Death";
        public float fadeInDuration = 0.15f;

        public bool IsDead { get; private set; }

        int _deathLayer = -1;
        float _targetWeight;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null)
                _deathLayer = animator.GetLayerIndex(deathLayerName);
        }

        void Update()
        {
            if (_deathLayer < 0 || animator == null) return;
            float w = animator.GetLayerWeight(_deathLayer);
            if (!Mathf.Approximately(w, _targetWeight))
            {
                float speed = fadeInDuration > 0f ? Time.deltaTime / fadeInDuration : 1f;
                w = Mathf.MoveTowards(w, _targetWeight, speed);
                animator.SetLayerWeight(_deathLayer, w);
            }
        }

        public void Die()
        {
            if (IsDead) return;
            IsDead = true;

            _targetWeight = 1f;
            if (_deathLayer >= 0 && animator != null)
                animator.SetLayerWeight(_deathLayer, 0f);

            if (locomotionController != null) locomotionController.enabled = false;
            if (characterController != null) characterController.enabled = false;
        }

        public void Revive()
        {
            IsDead = false;
            _targetWeight = 0f;
            if (characterController != null) characterController.enabled = true;
            if (locomotionController != null) locomotionController.enabled = true;
        }
    }
}