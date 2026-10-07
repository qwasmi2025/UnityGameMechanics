using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;
using System;


#if GPUI_PRO
using GPUInstancerPro;
using GPUInstancerPro.CrowdAnimations;
#endif

namespace PlagueRats
{
    /// <summary>
    /// Simple rat swarm: rats live inside RatZone boxes, wander/carpet inside them,
    /// and flee any nearby RatLight (the torch). No grid, no flow field, no NavMesh.
    /// Rendering via GPU Instancer Pro - Crowd Animations (Compute Animator).
    /// </summary>
    public class RatDirector : MonoBehaviour
    {
        [Header("Prefab & profile")]
        public GameObject ratCrowdPrefab;
#if GPUI_PRO
        public GPUIProfile gpuiProfile;
#endif

        [Header("Animation clips")]
        public AnimationClip clipIdle;
        public AnimationClip clipIdle2;
        public AnimationClip clipRun;
        public AnimationClip clipFlee;   // optional; falls back to Run if null

        [Header("Movement tuning")]
        public float maxSpeed = 3.5f;
        public float accel = 18f;
        public float sepRadius = 0.35f;
        public float wSep = 0.7f;
        public float wWander = 0.6f;
        public Vector2 scaleRange = new(0.85f, 1.1f);

        [Tooltip("Rats within this distance of the target play the Attack animation.")]
        public float attackRadius = 1.2f;
        public AnimationClip clipAttack; // assign Anim_Attack

        // ---- sim state ----
        int ratCount;
        NativeArray<float2> positions, velocities, positionsRead;
        NativeArray<float2> zoneMin, zoneMax;
        NativeArray<float> groundY;
        NativeArray<RatState> states, prevStates;
        NativeArray<Matrix4x4> matrices;
        NativeArray<LightPoint> lightArray;


        NativeArray<SpotCone> spotArray;

        [Range(0f, 1f)]
        [Tooltip("Fraction of the player's zone that attacks (0.5 = half). Rest keep carpeting.")]
        public float attackFraction = 0.5f;

        // ---- GPUI ----
        int _rendererKey;
        bool _registered;
        Matrix4x4[] _matrixUpload;
#if GPUI_PRO
        GPUIAWComputeAnimator _animator;
#endif

        // light registry (only RatLight objects affect rats — put it on the torch only)
        static readonly List<RatLight> s_Lights = new();
        public static void Register(RatLight l) { if (!s_Lights.Contains(l)) s_Lights.Add(l); }
        public static void Unregister(RatLight l) { s_Lights.Remove(l); }

        // RatTarget = the player the swarm converges on when all torches go out.
        static readonly List<RatTarget> s_Targets = new();
        public static void Register(RatTarget t) { if (!s_Targets.Contains(t)) s_Targets.Add(t); }
        public static void Unregister(RatTarget t) { s_Targets.Remove(t); }

        // ---- debug ----
        public bool Dbg_Registered => _registered;
        public int Dbg_RatCount => ratCount;
        public int Dbg_LightCount => s_Lights.Count;

        void Start()
        {
            BuildRatsFromZones();
            if (ratCount == 0)
            {
                Debug.LogWarning("[RatDirector] No RatZone found in the scene (or all empty). " +
                    "Add a GameObject with a RatZone component.");
                enabled = false; return;
            }
            RegisterWithGpui();
        }

        // ----------------------------------------------------------------- build rats
        void BuildRatsFromZones()
        {
            // total rats across all zones
            var zones = RatZone.All;
            ratCount = 0;
            for (int z = 0; z < zones.Count; z++)
                ratCount += math.max(0, zones[z].ratCount);
            if (ratCount == 0) return;

            int n = ratCount;
            positions = new NativeArray<float2>(n, Allocator.Persistent);
            velocities = new NativeArray<float2>(n, Allocator.Persistent);
            positionsRead = new NativeArray<float2>(n, Allocator.Persistent);
            zoneMin = new NativeArray<float2>(n, Allocator.Persistent);
            zoneMax = new NativeArray<float2>(n, Allocator.Persistent);
            groundY = new NativeArray<float>(n, Allocator.Persistent);
            states = new NativeArray<RatState>(n, Allocator.Persistent);
            prevStates = new NativeArray<RatState>(n, Allocator.Persistent);
            matrices = new NativeArray<Matrix4x4>(n, Allocator.Persistent);

            int idx = 0;
            for (int z = 0; z < zones.Count; z++)
            {
                var zone = zones[z];
                Vector2 mn = zone.MinXZ, mx = zone.MaxXZ;
                float gy = zone.GroundY;
                int count = math.max(0, zone.ratCount);
                for (int k = 0; k < count; k++)
                {
                    float px = Random.Range(mn.x, mx.x);
                    float pz = Random.Range(mn.y, mx.y);
                    positions[idx] = new float2(px, pz);
                    velocities[idx] = float2.zero;
                    zoneMin[idx] = new float2(mn.x, mn.y);
                    zoneMax[idx] = new float2(mx.x, mx.y);
                    groundY[idx] = gy;
                    states[idx] = RatState.Idle;
                    prevStates[idx] = (RatState)255; // force first StartAnimation
                    matrices[idx] = Matrix4x4.TRS(new Vector3(px, gy, pz),
                                                    Quaternion.identity, Vector3.one);
                    idx++;
                }
            }

            lightArray = new NativeArray<LightPoint>(1, Allocator.Persistent);
            lightArray[0] = new LightPoint { strength = 0f, radius = 0f };
        }

        // ----------------------------------------------------------------- GPUI registration
        void RegisterWithGpui()
        {
            if (ratCrowdPrefab == null)
            {
                Debug.LogError("[RatDirector] Assign the rat crowd prefab (GPUI Crowd Instance, Compute Animator).");
                enabled = false; return;
            }
#if GPUI_PRO
            if (gpuiProfile == null) gpuiProfile = GPUIProfile.DefaultProfile;
            if (gpuiProfile == null)
            {
                Debug.LogError("[RatDirector] No GPUI Profile assigned and no default found.");
                enabled = false; return;
            }

            bool ok = GPUICoreAPI.RegisterRenderer(this, ratCrowdPrefab, gpuiProfile, out _rendererKey);
            if (!ok || _rendererKey == 0)
            {
                Debug.LogError("[RatDirector] GPUI RegisterRenderer failed. Check the prefab has a " +
                    "GPUI Crowd Instance (Compute Animator) with baked clips.");
                enabled = false; return;
            }

            _matrixUpload = new Matrix4x4[ratCount];
            for (int i = 0; i < ratCount; i++) _matrixUpload[i] = matrices[i];
            GPUICoreAPI.SetTransformBufferData(_rendererKey, _matrixUpload, 0, 0, ratCount);

            _animator = GPUIAWComputeAnimator.Instance;
            _registered = true;

            for (int i = 0; i < ratCount; i++)
                _animator.StartAnimation(_rendererKey, i, clipIdle, Random.value);
#else
            Debug.LogWarning("[RatDirector] GPUI_PRO not defined; sim runs but nothing draws.");
            _matrixUpload = new Matrix4x4[ratCount];
            _registered = true;
#endif
        }

        // ----------------------------------------------------------------- main loop
        void Update()
        {
            if (!_registered) return;
            float dt = Time.deltaTime;

            BuildLightArray();
            BuildSpotArray();

            positions.CopyTo(positionsRead);

            bool anyLightOn = false;
            for (int li = 0; li < s_Lights.Count; li++)
                if (s_Lights[li] != null && s_Lights[li].IsOn) { anyLightOn = true; break; }
            if (!anyLightOn)
            {
                var spots = RatSpotLight.All;
                for (int si = 0; si < spots.Count; si++)
                    if (spots[si] != null && spots[si].IsActive) { anyLightOn = true; break; }
            }

            float2 targetPos = default;
            byte hasTarget = 0;
            for (int ti = 0; ti < s_Targets.Count; ti++)
            {
                if (s_Targets[ti] == null || !s_Targets[ti].AttackableByRats) continue;
                Vector3 tp = s_Targets[ti].transform.position;
                targetPos = new float2(tp.x, tp.z);
                hasTarget = 1;
                break; // first valid target
            }

            var sim = new RatSimJob
            {
                positionsRead = positionsRead,
                zoneMin = zoneMin,
                zoneMax = zoneMax,
                lights = lightArray,
                dt = dt,
                time = Time.time,
                maxSpeed = maxSpeed,
                accel = accel,
                sepRadius = sepRadius,
                wSep = wSep,
                wWander = wWander,
                neighborSampleRadius = 0f,
                targetPos = targetPos,
                hasTarget = hasTarget,
                anyLightOn = (byte)(anyLightOn ? 1 : 0),
                attackRadius = attackRadius,
                positions = positions,
                velocities = velocities,
                states = states,
                attackFraction = attackFraction,
                spots = spotArray,
            }.Schedule(ratCount, 64);

            var mtx = new BuildMatricesJob
            {
                positions = positions,
                velocities = velocities,
                groundY = groundY,
                scaleMin = scaleRange.x,
                scaleMax = scaleRange.y,
                matrices = matrices
            }.Schedule(ratCount, 64, sim);

            mtx.Complete();

#if GPUI_PRO
            if (!_registered || !Application.isPlaying || _rendererKey == 0) return;
            try
            {
                matrices.CopyTo(_matrixUpload);
                GPUICoreAPI.SetTransformBufferData(_rendererKey, _matrixUpload, 0, 0, ratCount);
                ApplyAnimationStateChanges();
            }
            catch
            {
                // GPUI disposed the renderer (e.g. on stop Play) — stop touching it.
                _registered = false;
            }
#endif
        }

        void ApplyAnimationStateChanges()
        {
#if GPUI_PRO
            if (!_registered || _rendererKey == 0 || _animator == null || !Application.isPlaying) return;
            for (int i = 0; i < ratCount; i++)
            {
                RatState s = states[i];
                if (s == prevStates[i]) continue;
                prevStates[i] = s;

                AnimationClip clip;
                float transition = 0.12f;
                bool? loopOverride = true;

                switch (s)
                {
                    case RatState.Run: clip = clipRun; break;
                    case RatState.Flee: clip = clipFlee != null ? clipFlee : clipRun; transition = 0.06f; break;
                    case RatState.Attack: clip = clipAttack != null ? clipAttack : clipRun; transition = 0.06f; break;
                    default:
                        clip = (i & 1) == 0 ? clipIdle : (clipIdle2 ? clipIdle2 : clipIdle);
                        break;
                }
                if (clip == null) clip = clipIdle;

                _animator.StartAnimation(_rendererKey, i, clip, -1f, 1f, transition, loopOverride);
            }
#endif
        }
        void BuildLightArray()
        {
            int count = math.max(1, s_Lights.Count);
            if (lightArray.IsCreated && lightArray.Length != count) lightArray.Dispose();
            if (!lightArray.IsCreated) lightArray = new NativeArray<LightPoint>(count, Allocator.Persistent);

            for (int i = 0; i < s_Lights.Count; i++)
            {
                var L = s_Lights[i];
                Vector3 wp = L.transform.position;
                lightArray[i] = new LightPoint
                {
                    pos = new float2(wp.x, wp.z),
                    radius = L.radius,
                    strength = L.IsOn ? L.strength : 0f
                };
            }
            if (s_Lights.Count == 0) lightArray[0] = new LightPoint { strength = 0f, radius = 0f };
        }
        // ----------------------------------------------------------------- lights -> native
        void BuildSpotArray()
        {
            var spots = RatSpotLight.All;
            int count = math.max(1, spots.Count);
            if (spotArray.IsCreated && spotArray.Length != count) spotArray.Dispose();
            if (!spotArray.IsCreated) spotArray = new NativeArray<SpotCone>(count, Allocator.Persistent);

            for (int i = 0; i < spots.Count; i++)
            {
                var sp = spots[i];
                Vector3 p = sp.Position;
                Vector3 f = sp.Forward;
                float2 fwd = math.normalizesafe(new float2(f.x, f.z));
                spotArray[i] = new SpotCone
                {
                    pos = new float2(p.x, p.z),
                    forward = fwd,
                    range = sp.Range,
                    cosHalf = math.cos(sp.HalfAngleRad),
                    strength = sp.IsActive ? sp.Strength : 0f
                };
            }
            if (spots.Count == 0)
                spotArray[0] = new SpotCone { strength = 0f, range = 0f };
        }

        // ----------------------------------------------------------------- cleanup
        void OnDestroy()
        {
            _registered = false;
#if GPUI_PRO
            if (_rendererKey != 0)
            {
                GPUICoreAPI.DisposeRenderer(_rendererKey);
                _rendererKey = 0;
            }
#endif
            if (positions.IsCreated) positions.Dispose();
            if (velocities.IsCreated) velocities.Dispose();
            if (positionsRead.IsCreated) positionsRead.Dispose();
            if (zoneMin.IsCreated) zoneMin.Dispose();
            if (zoneMax.IsCreated) zoneMax.Dispose();
            if (zoneMax.IsCreated) zoneMax.Dispose();
            if (groundY.IsCreated) groundY.Dispose();
            if (states.IsCreated) states.Dispose();
            if (prevStates.IsCreated) prevStates.Dispose();
            if (matrices.IsCreated) matrices.Dispose();
            if (lightArray.IsCreated) lightArray.Dispose();
            if (spotArray.IsCreated) spotArray.Dispose();
        }

        void OnDisable() { _registered = false; }
        void OnApplicationQuit() { _registered = false; }
    }
}