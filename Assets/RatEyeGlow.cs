using UnityEngine;

namespace PlagueRats
{
    /// <summary>
    /// Pulses the emission color of the rat material so the eyes glow on/off.
    /// Because all GPUI rats share one material, they all pulse together.
    /// </summary>
    public class RatEyeGlow : MonoBehaviour
    {
        [Tooltip("The rat material (Rat_Material_01) whose emission will pulse.")]
        public Material ratMaterial;

        [Tooltip("Eye glow color (the red you set in the Emission Map).")]
        [ColorUsage(false, true)] public Color glowColor = Color.red;

        [Tooltip("Lowest emission intensity (eyes dim).")]
        public float minIntensity = 0.3f;

        [Tooltip("Highest emission intensity (eyes bright).")]
        public float maxIntensity = 3f;

        [Tooltip("Pulses per second.")]
        public float pulseSpeed = 1.5f;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        void Update()
        {
            if (ratMaterial == null) return;

            // ping-pong 0..1 with a sine, then map to min..max
            float t = (math_sin(Time.time * pulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
            float intensity = Mathf.Lerp(minIntensity, maxIntensity, t);

            ratMaterial.SetColor(EmissionColor, glowColor * intensity);
        }

        static float math_sin(float x) => Mathf.Sin(x);
    }
}