using System.Collections.Generic;
using UnityEngine;

namespace PlagueRats
{
    public static class RatLightRegistry
    {
        static readonly List<RatLight> _all = new();
        public static IReadOnlyList<RatLight> All => _all;
        public static void Add(RatLight l) { if (!_all.Contains(l)) _all.Add(l); }
        public static void Remove(RatLight l) { _all.Remove(l); }
    }

    public class RatLight : MonoBehaviour
    {
        [Min(0.1f)] public float radius = 4f;
        [Min(0f)] public float strength = 120f;
        public bool isFire = false;
        [SerializeField] bool startsOn = true;
        public bool IsOn { get; private set; }

        void OnEnable() { IsOn = startsOn; RatDirector.Register(this); RatLightRegistry.Add(this); }
        void OnDisable() { RatDirector.Unregister(this); RatLightRegistry.Remove(this); }

        public void TurnOn() => IsOn = true;
        public void TurnOff() => IsOn = false;
        public void Toggle() => IsOn = !IsOn;
        public void SetOn(bool v) => IsOn = v;

        void OnDrawGizmos()
        {
            Gizmos.color = isFire ? new Color(1f, 0.4f, 0.1f, 0.5f) : new Color(1f, 0.95f, 0.6f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}