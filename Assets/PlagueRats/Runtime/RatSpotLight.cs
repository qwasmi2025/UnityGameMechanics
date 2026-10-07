using System.Collections.Generic;
using UnityEngine;

namespace PlagueRats
{
    /// <summary>
    /// A real Unity Spot Light the rats flee from. Reads range/spotAngle/intensity
    /// live, so changing the Light in the inspector or at runtime affects the rats.
    /// Put this on the same GameObject as the spot Light (the search light).
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class RatSpotLight : MonoBehaviour
    {
        [Tooltip("Push force applied to rats inside the cone. Scales with intensity.")]
        public float strengthPerIntensity = 2f;

        [Tooltip("Minimum intensity for the light to affect rats at all.")]
        public float minIntensity = 0.05f;

        Light _light;

        public static readonly List<RatSpotLight> All = new();
        void OnEnable() { _light = GetComponent<Light>(); if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public bool IsActive =>
            isActiveAndEnabled && _light != null && _light.enabled
            && _light.intensity > minIntensity;

        // world-space cone data, read fresh each frame by RatDirector
        public Vector3 Position => transform.position;
        public Vector3 Forward => transform.forward;
        public float Range => _light != null ? _light.range : 0f;
        public float HalfAngleRad =>
            _light != null ? _light.spotAngle * 0.5f * Mathf.Deg2Rad : 0f;
        public float Strength =>
            _light != null ? _light.intensity * strengthPerIntensity : 0f;
    }
}