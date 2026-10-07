using UnityEngine;

namespace PlagueRats
{
    /// <summary>
    /// A refuel zone. When a TorchFuel comes within range, it refills over time
    /// (or instantly). Place at braziers, oil pots, campfires, etc.
    /// </summary>
    public class FuelStation : MonoBehaviour
    {
        [Tooltip("How close the torch must be to refuel.")]
        public float radius = 2f;

        [Tooltip("Fuel seconds added per second while in range. Use a big value for near-instant.")]
        public float refuelRate = 20f;

        [Tooltip("Refill instantly on enter instead of gradually.")]
        public bool instant = false;

        [Tooltip("Optional: only this torch can refuel here. Leave empty to allow any.")]
        public TorchFuel specificTorch;

        [Header("Optional feedback")]
        [Tooltip("Particles/glow to enable while refueling.")]
        public GameObject activeEffect;

        float _radiusSq;

        void Awake() => _radiusSq = radius * radius;

        void Update()
        {
            bool refuelingThisFrame = false;

            if (specificTorch != null)
            {
                refuelingThisFrame = TryRefuel(specificTorch);
            }
            else
            {
                // check every active torch in the scene
                var torches = FindObjectsByType<TorchFuel>(FindObjectsSortMode.None);
                for (int i = 0; i < torches.Length; i++)
                    if (TryRefuel(torches[i])) refuelingThisFrame = true;
            }

            if (activeEffect != null && activeEffect.activeSelf != refuelingThisFrame)
                activeEffect.SetActive(refuelingThisFrame);
        }

        bool TryRefuel(TorchFuel torch)
        {
            if (torch == null) return false;

            float distSq = (torch.transform.position - transform.position).sqrMagnitude;
            if (distSq > _radiusSq) return false;

            // already full? nothing to do
            if (torch.fuel >= torch.maxFuel) return false;

            if (instant)
                torch.Refill();
            else
                torch.AddFuel(refuelRate * Time.deltaTime);

            return true;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.7f, 0.1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.color = new Color(1f, 0.7f, 0.1f, 0.12f);
            Gizmos.DrawSphere(transform.position, radius);
        }
    }
}