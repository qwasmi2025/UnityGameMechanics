using UnityEngine;

namespace PlagueRats
{
    [RequireComponent(typeof(RatLight))]
    public class TorchFuel : MonoBehaviour
    {
        [Header("Fuel")]
        public float maxFuel = 30f;
        public float fuel = -1f;
        public float burnRate = 1f;

        [Header("Light scaling (Point Light)")]
        public Light pointLight;
        public float maxRange = 8f;
        public float minRange = 2f;
        public float maxIntensity = 2.5f;
        public float minIntensity = 0.4f;

        [Header("Rat repulsion scaling")]
        public float maxRatRadius = 5f;
        public float minRatRadius = 1.5f;
        public float maxRatStrength = 150f;
        public float minRatStrength = 60f;

        [Header("Fire particles")]
        [Tooltip("The fire/flame ParticleSystem. Stopped smoothly when fuel runs out.")]
        public ParticleSystem fireParticles;
        [Tooltip("Shrink the flame only in the last part of the fuel (not the whole burn).")]
        public bool scaleParticlesWithFuel = true;
        [Tooltip("Below this fuel fraction the flame starts shrinking (0.3 = last 30%).")]
        [Range(0.05f, 1f)] public float flameShrinkStart = 0.3f;

        [Header("Flicker (optional)")]
        public bool flicker = true;
        public float flickerAmount = 0.15f;
        public float flickerSpeed = 12f;

        RatLight _ratLight;
        float _flickerSeed;
        float _baseEmissionRate = -1f; // captured at startup

        public float FuelNormalized => maxFuel > 0f ? Mathf.Clamp01(fuel / maxFuel) : 0f;
        public bool IsBurning => fuel > 0f;

        void Awake()
        {
            _ratLight = GetComponent<RatLight>();
            if (pointLight == null) pointLight = GetComponentInChildren<Light>();
            if (fireParticles == null) fireParticles = GetComponentInChildren<ParticleSystem>();
            if (fireParticles != null) _baseEmissionRate = fireParticles.emission.rateOverTime.constant;
            if (fuel < 0f) fuel = maxFuel;
            _flickerSeed = Random.value * 100f;
        }

        void OnEnable()
        {
            if (fuel < 0f) fuel = maxFuel;
            if (fuel > 0f && _ratLight) _ratLight.TurnOn();
            if (fuel > 0f && fireParticles != null)
            {
                if (!fireParticles.gameObject.activeSelf)
                    fireParticles.gameObject.SetActive(true);
                SetEmissionRate(_baseEmissionRate);
                if (!fireParticles.isPlaying) fireParticles.Play();
            }
        }

        void Update()
        {
            if (fuel > 0f)
            {
                fuel -= burnRate * Time.deltaTime;
                if (fuel <= 0f) { fuel = 0f; Extinguish(); return; }
            }
            else return;

            float t = FuelNormalized;

            if (pointLight != null)
            {
                float range = Mathf.Lerp(minRange, maxRange, t);
                float intensity = Mathf.Lerp(minIntensity, maxIntensity, t);
                if (flicker)
                {
                    float f = 1f + (Mathf.PerlinNoise(_flickerSeed, Time.time * flickerSpeed) - 0.5f) * 2f * flickerAmount;
                    intensity *= f;
                }
                pointLight.range = range;
                pointLight.intensity = intensity;
            }

            if (_ratLight != null)
            {
                _ratLight.radius = Mathf.Lerp(minRatRadius, maxRatRadius, t);
                _ratLight.strength = Mathf.Lerp(minRatStrength, maxRatStrength, t);
            }

            // shrink the flame ONLY near the end (so it doesn't vanish before the light dies)
            if (scaleParticlesWithFuel && fireParticles != null && _baseEmissionRate >= 0f)
            {
                // full rate until fuel drops below flameShrinkStart, then ramp down to 0
                float flame = flameShrinkStart > 0f
                    ? Mathf.Clamp01(t / flameShrinkStart)
                    : 1f;
                SetEmissionRate(_baseEmissionRate * flame);
            }
        }

        public void Extinguish()
        {
            fuel = 0f;
            if (pointLight != null) pointLight.enabled = false;
            if (_ratLight != null) _ratLight.TurnOff();

            // smooth: stop spawning new particles, let existing ones fade out naturally
            if (fireParticles != null)
                fireParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        public void AddFuel(float seconds)
        {
            fuel = Mathf.Min(maxFuel, fuel + seconds);
            if (fuel > 0f)
            {
                if (pointLight != null) pointLight.enabled = true;
                if (_ratLight != null) _ratLight.TurnOn();

                // restore the flame: re-activate object, reset rate, then play
                if (fireParticles != null)
                {
                    if (!fireParticles.gameObject.activeSelf)
                        fireParticles.gameObject.SetActive(true);
                    SetEmissionRate(_baseEmissionRate);
                    if (!fireParticles.isPlaying) fireParticles.Play();
                }
            }
        }
        public void Refill() => AddFuel(maxFuel);

        void SetEmissionRate(float value)
        {
            if (fireParticles == null || _baseEmissionRate < 0f) return;
            var emission = fireParticles.emission;
            var rate = emission.rateOverTime;
            rate.constant = value;
            emission.rateOverTime = rate;
        }
    }
}