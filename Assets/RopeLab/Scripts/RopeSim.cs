using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace RopeLab
{
    public enum PinKind { None, Static, Tie, Hand, Coil, Socket, Rigidbody }

    /// <summary>
    /// Position-based-dynamics rope (after Naughty Dog's GDC21 talk):
    /// damping -> gravity -> predict -> N fixed iterations of
    /// distance / bending / tether / collision / static-friction constraints -> v = (p - x) / dt -> velocity friction.
    /// Collision is exact against box colliders (nodes AND segment midpoints, so the tube never cuts corners)
    /// and plane-based against everything else. Any node can be pinned (keyframed) to a transform.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class RopeSim : MonoBehaviour
    {
        [Header("Shape")]
        public float length = 12f;
        public float segmentLength = 0.2f;
        public float radius = 0.03f;
        public bool isCable;
        [Tooltip("Where node 0 starts. Rope is laid out from here along layoutDirection.")]
        public Transform startPoint;
        public Vector3 layoutDirection = Vector3.forward;
        [Tooltip("0 = straight. >0 lays the rope out as a loose meander (degrees of swing).")]
        public float layoutMeander = 0f;
        [Tooltip("Start as a coiled bundle dropped on the floor (loops fanned out a little), like a thrown rope coil.")]
        public bool layoutCoiled;
        public float coilRadius = 0.35f;
        [Tooltip("Optional: node 0 is permanently fixed here (generator, wall ring...).")]
        public Transform permanentAnchor;
        [Tooltip("Optional: last node fixed to this (moving) transform for good — e.g. a dog's collar.")]
        public Transform endAnchor;
        [Tooltip("Collision radius of the last node (e.g. a plug bigger than the cable). 0 = same as the rope.")]
        public float endCollisionRadius = 0f;
        [Tooltip("Weight of the last node relative to the others (a plug is a weighted end).")]
        public float endMassScale = 1f;
        [Tooltip("Optional: last node attached to this rigidbody (crate, cart...).")]
        public Rigidbody endRigidbody;
        public Vector3 endRigidbodyLocalPoint;

        [Header("Solver")]
        public int iterations = 24;
        [Tooltip("Simulation steps per second. 60 = one step per frame at 60 FPS.")]
        public float stepRate = 60f;
        [Tooltip("Catch-up limit after a slow frame. Keep it low: more steps make the next frame slower still.")]
        public int maxStepsPerFrame = 2;
        [HideInInspector] public float damping = 0.01f;       // legacy (was per-step: bled ~70% speed/s)
        [Tooltip("Air drag per SECOND. Low, so a thrown rope keeps its speed and lands where the arc shows.")]
        public float airDrag = 0.3f;
        [Range(1f, 1.9f)] public float overRelaxation = 1.25f;
        public Vector3 gravity = new Vector3(0f, -9.81f, 0f);
        [Tooltip("Ropes further than this from the camera and not held stop simulating.")]
        public float simulationDistance = 45f;

        [Header("Bending (zeta, alpha_F)")]
        [Range(0f, 1f)] public float bendStiffness = 0.05f;
        [Tooltip("Free bending angle per metre of rope, degrees.")]
        public float freeBendDegPerMeter = 60f;

        [Header("Collision & friction")]
        public LayerMask collisionMask = ~0;
        [Range(0f, 1f)] public float velocityFriction = 0.2f;
        [Tooltip("s_FP: proportional pull back to start-of-step position for contacting nodes.")]
        [Range(0f, 1f)] public float staticFrictionProportional = 0.3f;
        [Tooltip("s_FC: radius (m) inside which a contacting node snaps back = static friction.")]
        public float staticFrictionConstant = 0.003f;
        [Tooltip("Extra gap kept between the rope surface and geometry, so the rendered tube never dips into edges.")]
        public float surfaceOffset = 0.006f;

        [Header("Rigidbody coupling")]
        [Tooltip("How fast (m/s per metre of stretch) the rope drags an attached body.")]
        public float bodyPullGain = 15f;
        public float bodyMaxPullSpeed = 2.2f;

        // ---- state ----
        public int NodeCount { get; private set; }
        public float SegmentLength => _l0;
        public Vector3[] Positions => _p;

        Vector3[] _x, _p, _v, _contactNormal;
        float[] _w, _massScale, _contactStrength;
        Pin[] _pins;
        Plane4[] _planes;
        const int MaxBoxesPerNode = 4;
        int[] _nodeBoxes;           // NodeCount * MaxBoxesPerNode indices into _boxes
        int[] _nodeBoxCount;
        // Plain array (not List<T>): a List indexer returns a COPY of the 56-byte struct on every access.
        BoxData[] _boxes = new BoxData[16];
        int _boxCount;
        readonly Dictionary<BoxCollider, int> _boxIndex = new Dictionary<BoxCollider, int>();
        float _l0, _accum, _bendK;
        readonly Collider[] _overlap = new Collider[16];
        static SphereCollider _probe;

        public struct Pin
        {
            public PinKind kind;
            public Transform target;
            public Vector3 localOffset;
            public bool permanent;
            public Rigidbody body;
        }

        struct BoxData
        {
            public Vector3 center, half;
            public Quaternion rot, inv;
        }

        struct Plane4
        {
            public int count;
            public Vector3 n0, n1, n2, n3;
            public float d0, d1, d2, d3;
            public void Add(Vector3 n, float d)
            {
                for (int i = 0; i < count; i++)
                {
                    if (Vector3.Dot(N(i), n) > 0.96f) { if (d > D(i)) Set(i, n, d); return; }
                }
                if (count < 4) Set(count++, n, d);
            }
            public Vector3 N(int i) => i == 0 ? n0 : i == 1 ? n1 : i == 2 ? n2 : n3;
            public float D(int i) => i == 0 ? d0 : i == 1 ? d1 : i == 2 ? d2 : d3;
            void Set(int i, Vector3 n, float d)
            {
                switch (i) { case 0: n0 = n; d0 = d; break; case 1: n1 = n; d1 = d; break; case 2: n2 = n; d2 = d; break; default: n3 = n; d3 = d; break; }
            }
        }

        void Awake() => Build();

        public void Build()
        {
            _l0 = Mathf.Max(0.05f, segmentLength);
            NodeCount = Mathf.Max(3, Mathf.RoundToInt(length / _l0) + 1);
            int n = NodeCount;
            _x = new Vector3[n]; _p = new Vector3[n]; _v = new Vector3[n]; _contactNormal = new Vector3[n];
            _w = new float[n]; _massScale = new float[n]; _contactStrength = new float[n];
            _pins = new Pin[n]; _planes = new Plane4[n];
            _nodeBoxes = new int[n * MaxBoxesPerNode]; _nodeBoxCount = new int[n];

            Vector3 origin = startPoint ? startPoint.position : transform.position;
            Vector3 dir = layoutDirection.sqrMagnitude > 0 ? layoutDirection.normalized : Vector3.forward;
            if (layoutCoiled) LayoutCoil(origin, dir);
            else
            {
                Vector3 pos = origin;
                for (int i = 0; i < n; i++)
                {
                    _x[i] = _p[i] = pos;
                    Vector3 d = dir;
                    if (layoutMeander > 0f) d = Quaternion.AngleAxis(Mathf.Sin(i * 0.33f) * layoutMeander, Vector3.up) * dir;
                    pos += d * _l0;
                }
            }
            for (int i = 0; i < n; i++) { _v[i] = Vector3.zero; _massScale[i] = 1f; }
            _massScale[n - 1] = Mathf.Max(0.01f, endMassScale);
            if (permanentAnchor) SetPin(0, PinKind.Static, permanentAnchor, Vector3.zero, true);
            if (endAnchor) { _x[n - 1] = _p[n - 1] = endAnchor.position; SetPin(n - 1, PinKind.Static, endAnchor, Vector3.zero, true); }
            if (endRigidbody)
                _pins[n - 1] = new Pin { kind = PinKind.Rigidbody, target = endRigidbody.transform, localOffset = endRigidbodyLocalPoint, body = endRigidbody, permanent = true };
            RefreshMasses();
            Wake();
        }

        /// <summary>
        /// A coil dropped on the floor: node 0 starts at the origin (e.g. an anchor), runs down to the floor, then
        /// winds in loose loops whose radius and centre wander a little, so it reads as a thrown bundle rather than
        /// a perfect spiral. Stacked loops settle onto the floor under gravity.
        /// </summary>
        void LayoutCoil(Vector3 origin, Vector3 dir)
        {
            int n = NodeCount;
            var rng = new System.Random(name.GetHashCode());
            float Rand() => (float)rng.NextDouble();

            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();

            float groundY = origin.y;
            if (Physics.Raycast(origin + Vector3.up * 0.05f, Vector3.down, out var hit, 6f, collisionMask, QueryTriggerInteraction.Ignore))
                groundY = hit.point.y;
            groundY += radius;

            Vector3 center = new Vector3(origin.x, groundY, origin.z) + flat * (coilRadius + 0.35f);
            float angle = Mathf.Atan2(-flat.z, -flat.x);             // enter the coil on the side facing the origin
            Vector3 firstOnCoil = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * coilRadius;

            // Lead-in: from the origin straight down/over to the first loop.
            int lead = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(origin, firstOnCoil) / _l0), 1, n - 1);
            for (int i = 0; i < lead; i++)
                _x[i] = _p[i] = Vector3.Lerp(origin, firstOnCoil, i / (float)lead);

            // Loops: radius and centre drift per loop; slight lift so loops lie on top of each other.
            Vector3 drift = Quaternion.AngleAxis(Rand() * 360f, Vector3.up) * Vector3.forward;
            float r = coilRadius, loopStart = angle;
            int loop = 0;
            for (int i = lead; i < n; i++)
            {
                angle += _l0 / r;
                if (angle - loopStart >= Mathf.PI * 2f)
                {
                    loopStart = angle; loop++;
                    r = coilRadius * (0.8f + 0.4f * Rand());
                    center += drift * (0.04f + 0.06f * Rand());
                    drift = Quaternion.AngleAxis((Rand() - 0.5f) * 90f, Vector3.up) * drift;
                }
                float wobble = 1f + 0.08f * Mathf.Sin(angle * 3f + loop);
                Vector3 p = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (r * wobble);
                p.y = groundY + loop * radius * 1.6f;
                _x[i] = _p[i] = p;
            }
        }

        // ------------------------------------------------------------------ API
        public Vector3 GetNode(int i) => _p[Mathf.Clamp(i, 0, NodeCount - 1)];
        public Pin GetPin(int i) => _pins[Mathf.Clamp(i, 0, NodeCount - 1)];
        public bool IsPinned(int i) => _pins[i].kind != PinKind.None;
        public int EndIndex => NodeCount - 1;

        public void SetPin(int i, PinKind kind, Transform target, Vector3 localOffset, bool permanent = false)
        {
            _pins[i] = new Pin { kind = kind, target = target, localOffset = localOffset, permanent = permanent };
            RefreshMasses();
            Wake();
        }

        public void ClearPin(int i)
        {
            if (_pins[i].permanent) return;
            _pins[i] = default;
            RefreshMasses();
            Wake();
        }

        public void ClearPins(PinKind kind)
        {
            for (int i = 0; i < NodeCount; i++) if (_pins[i].kind == kind && !_pins[i].permanent) _pins[i] = default;
            RefreshMasses();
            Wake();
        }

        // ------------------------------------------------------------------ Sleeping
        /// <summary>A rope lying still is not simulated (and not re-meshed) until something disturbs it.</summary>
        public bool IsSleeping => _sleeping;
        /// <summary>Increments whenever node positions change; the renderer skips re-meshing when unchanged.</summary>
        public int Version { get; private set; }
        bool _sleeping, _dormant;
        float _stillTime;
        int _stepCount;

        public void Wake() { _sleeping = false; _stillTime = 0f; Version++; }

        float _bendOffUntil;
        /// <summary>Lets a thrown bundle keep its tight coiled shape in flight (bending would unroll it at once).</summary>
        public void SuspendBending(float seconds) { _bendOffUntil = Time.time + seconds; Wake(); }

        bool ShouldWake()
        {
            for (int i = 0; i < NodeCount; i++)
            {
                var pin = _pins[i];
                if (pin.kind == PinKind.None) continue;
                if (pin.kind == PinKind.Hand || pin.kind == PinKind.Coil) return true;
                if (pin.kind == PinKind.Rigidbody && pin.body && !pin.body.IsSleeping()) return true;
                if (pin.target && (PinWorld(pin) - _x[i]).sqrMagnitude > 1e-6f) return true;   // anchor moved
            }
            return false;
        }

        void UpdateSleep(float frameDt)
        {
            if (HasPin(PinKind.Hand) || HasPin(PinKind.Coil)) { _stillTime = 0f; return; }
            float maxSq = 0f;
            for (int i = 0; i < NodeCount; i++) if (_w[i] != 0f) maxSq = Mathf.Max(maxSq, _v[i].sqrMagnitude);
            if (maxSq > 0.03f * 0.03f) { _stillTime = 0f; return; }
            _stillTime += frameDt;
            if (_stillTime > 0.6f)
            {
                _sleeping = true;
                for (int i = 0; i < NodeCount; i++) _v[i] = Vector3.zero;
            }
        }

        public bool HasPin(PinKind kind)
        {
            for (int i = 0; i < NodeCount; i++) if (_pins[i].kind == kind) return true;
            return false;
        }

        /// <summary>Anything that fixes the rope to the world (not hands, not coils).</summary>
        public static bool IsAnchorKind(PinKind k, bool includeBodies = true) =>
            k == PinKind.Static || k == PinKind.Tie || k == PinKind.Socket || (includeBodies && k == PinKind.Rigidbody);

        public bool HasAnyAnchor()
        {
            for (int i = 0; i < NodeCount; i++) if (IsAnchorKind(_pins[i].kind)) return true;
            return false;
        }

        /// <summary>Nearest node that is fixed in the world (tie, socket, static, rigidbody), searching both directions.</summary>
        public int NearestAnchor(int from, bool includeBodies = true)
        {
            int best = -1, bestDist = int.MaxValue;
            for (int i = 0; i < NodeCount; i++)
            {
                if (!IsAnchorKind(_pins[i].kind, includeBodies)) continue;
                int d = Mathf.Abs(i - from);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        public float PathLength(int a, int b)
        {
            a = Mathf.Clamp(a, 0, NodeCount - 1); b = Mathf.Clamp(b, 0, NodeCount - 1);
            if (a > b) (a, b) = (b, a);
            float s = 0f;
            for (int i = a; i < b; i++) s += Vector3.Distance(_p[i], _p[i + 1]);
            return s;
        }

        public int ClosestNode(Vector3 point, out float dist)
        {
            int best = 0; float bestSq = float.MaxValue;
            for (int i = 0; i < NodeCount; i++)
            {
                float sq = (_p[i] - point).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            dist = Mathf.Sqrt(bestSq);
            return best;
        }

        public void SetVelocity(int i, Vector3 v) { _v[i] = v; Wake(); }
        public Vector3 GetVelocity(int i) => _v[i];

        /// <summary>Heavier node (e.g. player climbing) -> lower inverse mass.</summary>
        public void SetMassScale(int i, float scale) { _massScale[i] = Mathf.Max(0.01f, scale); RefreshMasses(); Wake(); }
        public void ResetMassScale()
        {
            for (int i = 0; i < NodeCount; i++) _massScale[i] = 1f;
            _massScale[NodeCount - 1] = Mathf.Max(0.01f, endMassScale);
            RefreshMasses();
        }

        /// <summary>Collision radius of a node (the last node can be bigger, e.g. a plug).</summary>
        float NodeRadius(int i) => i == NodeCount - 1 && endCollisionRadius > radius ? endCollisionRadius : radius;

        public void Teleport(int i, Vector3 pos) { _x[i] = _p[i] = pos; _v[i] = Vector3.zero; Wake(); }

        public bool InContact(int i) => _contactStrength[i] > 0f;

        void RefreshMasses()
        {
            for (int i = 0; i < NodeCount; i++)
                _w[i] = _pins[i].kind == PinKind.None ? 1f / _massScale[i] : 0f;
        }

        Vector3 PinWorld(in Pin pin) => pin.target ? pin.target.TransformPoint(pin.localOffset) : Vector3.zero;

        // ------------------------------------------------------------------ Loop
        void LateUpdate()
        {
            // Far from the camera: freeze it (no simulation, no re-meshing) and wake it when you come back —
            // even if it froze mid-air, it carries on from where it was.
            bool active = IsActive();
            if (!active)
            {
                if (!_sleeping) { _sleeping = true; _dormant = true; }
                return;
            }
            if (_sleeping)
            {
                if (_dormant) { _dormant = false; Wake(); }
                else if (!ShouldWake()) return;
                else Wake();
            }
            float h = 1f / stepRate;
            _accum = Mathf.Min(_accum + Time.deltaTime, h * maxStepsPerFrame);
            bool stepped = false;
            while (_accum >= h) { if (active) { Step(h); stepped = true; } _accum -= h; }
            // Keep keyframed nodes glued to their targets for rendering this frame.
            for (int i = 0; i < NodeCount; i++)
                if (_pins[i].kind != PinKind.None && _pins[i].target)
                    _p[i] = _x[i] = PinWorld(_pins[i]);
            Version++;                                  // pinned nodes may have moved even without a step
            if (stepped) UpdateSleep(Time.deltaTime);
        }

        bool IsActive()
        {
            var cam = Camera.main;
            if (!cam) return true;
            if (HasPin(PinKind.Hand) || HasPin(PinKind.Coil)) return true;
            float r2 = simulationDistance * simulationDistance;
            return (_p[0] - cam.transform.position).sqrMagnitude < r2
                || (_p[NodeCount / 2] - cam.transform.position).sqrMagnitude < r2
                || (_p[NodeCount - 1] - cam.transform.position).sqrMagnitude < r2;
        }

        static readonly ProfilerMarker s_stepMarker = new ProfilerMarker("RopeLab.Step");
        static readonly ProfilerMarker s_gatherMarker = new ProfilerMarker("RopeLab.GatherContacts");

        void Step(float dt)
        {
            using var _ = s_stepMarker.Auto();
            int n = NodeCount;
            float drag = Mathf.Exp(-airDrag * dt);             // frame-rate independent air drag

            // 1) damping, external forces, prediction.  Keyframed nodes jump to their targets.
            for (int i = 0; i < n; i++)
            {
                if (_w[i] == 0f)
                {
                    _p[i] = _pins[i].target ? PinWorld(_pins[i]) : _x[i];
                    continue;
                }
                _v[i] *= drag;
                _v[i] += gravity * dt;
                _p[i] = _x[i] + _v[i] * dt;
            }

            // 2) nearby geometry per node: exact boxes + planes for everything else.
            //    Physics queries are the expensive part; refresh every other step (contacts barely change in 8 ms).
            if ((_stepCount++ & 1) == 0) GatherContacts();

            // 3) fixed iteration count (changing it would change stiffness).
            float alphaFree = freeBendDegPerMeter * Mathf.Deg2Rad * _l0;
            // PBD stiffness compounds over iterations: k applied N times acts like 1-(1-k)^N. Convert so that
            // bendStiffness is the stiffness per STEP (otherwise 0.05 x 12 passes ≈ 46% — a rope like a steel bar).
            int bendPasses = (iterations + 1) / 2;
            _bendK = 1f - Mathf.Pow(1f - Mathf.Clamp01(bendStiffness), 1f / Mathf.Max(1, bendPasses));
            for (int it = 0; it < iterations; it++)
            {
                bool last = it == iterations - 1;
                float omega = last ? 1f : overRelaxation;

                // Distance constraints, alternating sweep direction to propagate faster.
                if ((it & 1) == 0) for (int i = 0; i < n - 1; i++) SolveDistance(i, i + 1, omega);
                else for (int i = n - 2; i >= 0; i--) SolveDistance(i, i + 1, omega);

                if (bendStiffness > 0f && (it & 1) == 0 && Time.time >= _bendOffUntil)
                    for (int i = 1; i < n - 1; i++) SolveBend(i - 1, i, i + 1, alphaFree);

                SolveTethers();

                // Collision last so the final state is always out of geometry.
                for (int i = 0; i < n; i++)
                {
                    if (_w[i] == 0f) continue;
                    SolveNodeCollision(i);
                    if (_contactStrength[i] > 0f) SolveStaticFriction(i);
                }
                // Segments last: friction may nudge nodes so the line between them cuts a corner again.
                // (Every third iteration — the final passes below always run them.)
                if (it % 8 == 0) for (int i = 0; i < n - 1; i++) SolveSegmentMidpoint(i);
            }
            // Final clean-up pass with no friction: the state that gets rendered is fully outside geometry.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < n; i++) if (_w[i] != 0f) SolveNodeCollision(i);
                for (int i = 0; i < n - 1; i++) SolveSegmentMidpoint(i);
            }

            // 4) velocities from positions, then velocity friction on contacting nodes.
            float invDt = 1f / dt;
            for (int i = 0; i < n; i++)
            {
                Vector3 v = (_p[i] - _x[i]) * invDt;
                if (_contactStrength[i] > 0f)
                {
                    Vector3 nrm = _contactNormal[i];
                    Vector3 vn = Vector3.Dot(v, nrm) * nrm;
                    Vector3 vt = v - vn;
                    if (Vector3.Dot(v, nrm) < 0f) vn = Vector3.zero;     // no bounce into the surface
                    v = vn + vt * (1f - velocityFriction * Mathf.Clamp01(_contactStrength[i]));
                }
                if (_pins[i].kind == PinKind.Rigidbody && _pins[i].body) PullBody(i);
                _v[i] = v;
                _x[i] = _p[i];
            }
        }

        // Rope can only pull, never push: if the first segment is stretched, drive the body's
        // velocity along the rope toward a target proportional to the stretch (stable, mass-independent).
        void PullBody(int i)
        {
            var pin = _pins[i];
            int j = i > 0 ? i - 1 : i + 1;
            Vector3 attach = PinWorld(pin);
            Vector3 d = _p[j] - attach;
            float len = d.magnitude;
            float stretch = len - _l0;
            if (stretch <= 0f || len < 1e-5f) return;
            Vector3 dir = d / len;
            float desired = Mathf.Min(stretch * bodyPullGain * stepRate * 0.1f, bodyMaxPullSpeed);
            float along = Vector3.Dot(pin.body.GetPointVelocity(attach), dir);
            if (along < desired)
                pin.body.AddForceAtPosition(dir * ((desired - along) * pin.body.mass * 0.5f), attach, ForceMode.Impulse);
        }

        void SolveDistance(int a, int b, float omega)
        {
            float wa = _w[a], wb = _w[b], ws = wa + wb;
            if (ws == 0f) return;
            Vector3 d = _p[b] - _p[a];
            float len = d.magnitude;
            if (len < 1e-6f) return;
            float c = (len - _l0) / (len * ws) * omega;
            _p[a] += d * (c * wa);
            _p[b] -= d * (c * wb);
        }

        // Bending: move the three nodes along the same direction, conserving linear and angular momentum.
        void SolveBend(int ia, int ib, int ic, float alphaFree)
        {
            float w1 = _w[ia], w2 = _w[ib], w3 = _w[ic];
            if (w1 + w2 + w3 == 0f) return;
            Vector3 a = _p[ia], b = _p[ib], c = _p[ic];
            Vector3 e = c - a;
            float ee = e.sqrMagnitude;
            if (ee < 1e-8f) return;
            float t = Mathf.Clamp01(Vector3.Dot(b - a, e) / ee);
            Vector3 hv = (a + e * t) - b;
            float h = hv.magnitude;
            if (h < 1e-5f) return;

            float angle = Vector3.Angle(b - a, c - b) * Mathf.Deg2Rad;
            if (angle <= alphaFree) return;
            h *= (angle - alphaFree) / angle;          // only straighten beyond the free bending angle

            Vector3 u = hv / hv.magnitude;
            float denom = w1 * (1 - t) * (1 - t) + w2 + w3 * t * t;
            if (denom < 1e-8f) return;
            float H = _bendK * h / denom;
            _p[ia] += u * (-(1 - t) * w1 * H);
            _p[ib] += u * (w2 * H);
            _p[ic] += u * (-t * w3 * H);
        }

        // Long-range attachment: no node may be further from the nearest fixed node than the rope between them.
        // The limit is a straight line, so it is only valid until the rope wraps around something: past a wall
        // top or a pillar edge, the straight line runs THROUGH the geometry and would drag the rope through it
        // when pulled. So each tether chain stops at the first node that wraps an edge.
        void SolveTethers()
        {
            int n = NodeCount;
            int lastPin = -1;
            for (int i = 0; i < n; i++)
            {
                if (_w[i] == 0f) { lastPin = i; continue; }
                if (lastPin >= 0)
                {
                    ClampTether(i, lastPin);
                    if (WrapsEdge(i)) lastPin = -1;
                }
            }
            lastPin = -1;
            for (int i = n - 1; i >= 0; i--)
            {
                if (_w[i] == 0f) { lastPin = i; continue; }
                if (lastPin >= 0)
                {
                    ClampTether(i, lastPin);
                    if (WrapsEdge(i)) lastPin = -1;
                }
            }
        }

        /// <summary>Node is bent around geometry: touching a wall/side face, or touching anything while sharply bent.</summary>
        bool WrapsEdge(int i)
        {
            if (_contactStrength[i] <= 0f) return false;
            if (Mathf.Abs(_contactNormal[i].y) < 0.7f) return true;             // side of a wall, pillar, crate
            if (i <= 0 || i >= NodeCount - 1) return false;
            return Vector3.Angle(_p[i] - _p[i - 1], _p[i + 1] - _p[i]) > 35f;   // draped over a top edge
        }

        void ClampTether(int i, int pin)
        {
            float max = Mathf.Abs(i - pin) * _l0 * 1.01f;
            Vector3 d = _p[i] - _p[pin];
            float len = d.magnitude;
            if (len > max) _p[i] = _p[pin] + d * (max / len);
        }

        // ------------------------------------------------------------------ Collision
        void SolveNodeCollision(int i)
        {
            float strength = 0f;
            Vector3 p = _p[i];

            int bc = _nodeBoxCount[i];
            for (int k = 0; k < bc; k++)
            {
                if (ProjectBox(_boxes[_nodeBoxes[i * MaxBoxesPerNode + k]], ref p, NodeRadius(i) + surfaceOffset, out var nrm, out bool pushed))
                {
                    _contactNormal[i] = nrm;
                    float s = (pushed ? 0.35f : 0.2f) + Mathf.Clamp01(nrm.y) * 0.65f;
                    if (s > strength) strength = s;
                }
            }

            ref var pl = ref _planes[i];
            for (int k = 0; k < pl.count; k++)
            {
                Vector3 nrm = pl.N(k);
                float pen = pl.D(k) + NodeRadius(i) + surfaceOffset - Vector3.Dot(nrm, p);
                if (pen > -0.01f)
                {
                    if (pen > 0f) p += nrm * pen;
                    _contactNormal[i] = nrm;
                    // Rough tension analysis: resting on top (weight) => more friction.
                    float s = (pen > 0f ? 0.35f : 0.2f) + Mathf.Clamp01(nrm.y) * 0.65f;
                    if (s > strength) strength = s;
                }
            }
            _p[i] = p;
            _contactStrength[i] = strength;
        }

        // Keeps the whole segment between two nodes out of boxes (points at 1/4, 1/2, 3/4), so the rope can't cut
        // across a corner between its nodes — that is what made it vanish into pillar edges.
        // Sixths: includes 1/3 and 2/3, exactly where RopeRenderer puts its rings, so the drawn tube is guarded too.
        static readonly float[] s_segmentSamples = { 1f / 6f, 2f / 6f, 0.5f, 4f / 6f, 5f / 6f };

        readonly int[] _segBoxes = new int[MaxBoxesPerNode * 2];

        void SolveSegmentMidpoint(int i)
        {
            int j = i + 1;
            float wi = _w[i], wj = _w[j];
            if (wi + wj == 0f) return;
            if (_nodeBoxCount[i] == 0 && _nodeBoxCount[j] == 0) return;
            float r = radius + surfaceOffset;

            // Keep only boxes the segment could actually cut: if both ends are beyond the same face of a box,
            // the (straight) segment cannot touch it. This skips the floor under a resting rope — nearly every
            // node has it — which was most of the cost.
            int count = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int node = pass == 0 ? i : j;
                int bc = _nodeBoxCount[node];
                for (int k = 0; k < bc; k++)
                {
                    int bi = _nodeBoxes[node * MaxBoxesPerNode + k];
                    bool dup = false;
                    for (int d = 0; d < count; d++) if (_segBoxes[d] == bi) { dup = true; break; }
                    if (dup || SegmentClearOfBox(_boxes[bi], _p[i], _p[j], r)) continue;
                    _segBoxes[count++] = bi;
                }
            }
            if (count == 0) return;

            foreach (float t in s_segmentSamples)
            {
                Vector3 m = Vector3.LerpUnclamped(_p[i], _p[j], t);
                Vector3 m0 = m;
                for (int k = 0; k < count; k++)
                    ProjectBox(_boxes[_segBoxes[k]], ref m, r, out _, out _);
                Vector3 delta = m - m0;
                if (delta.sqrMagnitude < 1e-12f) continue;
                // Point on the segment p(t) = (1-t) pi + t pj: move the endpoints so p(t) moves by delta,
                // weighted by inverse mass (PBD with gradients (1-t) and t).
                float a = 1f - t;
                float denom = a * a * wi + t * t * wj;
                if (denom < 1e-8f) continue;
                Vector3 lambda = delta / denom;
                _p[i] += lambda * (a * wi);
                _p[j] += lambda * (t * wj);
            }
        }

        /// <summary>True when both segment ends lie beyond the same face of the (radius-inflated) box.</summary>
        static bool SegmentClearOfBox(in BoxData b, Vector3 a, Vector3 c, float r)
        {
            Vector3 qa = b.inv * (a - b.center), qc = b.inv * (c - b.center);
            Vector3 h = b.half;
            float hx = h.x + r, hy = h.y + r, hz = h.z + r;
            return (qa.y > hy && qc.y > hy) || (qa.y < -hy && qc.y < -hy)
                || (qa.x > hx && qc.x > hx) || (qa.x < -hx && qc.x < -hx)
                || (qa.z > hz && qc.z > hz) || (qa.z < -hz && qc.z < -hz);
        }

        /// <summary>Exact sphere-vs-oriented-box projection. Returns true when touching (within a small margin).</summary>
        static bool ProjectBox(in BoxData b, ref Vector3 p, float r, out Vector3 normal, out bool pushed)
        {
            Vector3 q = b.inv * (p - b.center);
            Vector3 h = b.half;
            Vector3 c = new Vector3(Mathf.Clamp(q.x, -h.x, h.x), Mathf.Clamp(q.y, -h.y, h.y), Mathf.Clamp(q.z, -h.z, h.z));
            Vector3 diff = q - c;
            float d2 = diff.sqrMagnitude;
            pushed = false;
            if (d2 > 1e-12f)
            {
                float margin = r + 0.01f;
                if (d2 >= margin * margin) { normal = default; return false; }
                float d = Mathf.Sqrt(d2);
                Vector3 nl = diff / d;
                normal = b.rot * nl;
                if (d < r) { q = c + nl * r; p = b.center + b.rot * q; pushed = true; }
                return true;
            }
            // Centre inside the box: push out through the nearest face.
            float dx = h.x - Mathf.Abs(q.x), dy = h.y - Mathf.Abs(q.y), dz = h.z - Mathf.Abs(q.z);
            Vector3 nlocal;
            if (dx <= dy && dx <= dz) { float s = q.x >= 0 ? 1f : -1f; q.x = s * (h.x + r); nlocal = new Vector3(s, 0, 0); }
            else if (dy <= dz) { float s = q.y >= 0 ? 1f : -1f; q.y = s * (h.y + r); nlocal = new Vector3(0, s, 0); }
            else { float s = q.z >= 0 ? 1f : -1f; q.z = s * (h.z + r); nlocal = new Vector3(0, 0, s); }
            normal = b.rot * nlocal;
            p = b.center + b.rot * q;
            pushed = true;
            return true;
        }

        void SolveStaticFriction(int i)
        {
            Vector3 delta = _x[i] - _p[i];
            Vector3 nrm = _contactNormal[i];
            delta -= Vector3.Dot(delta, nrm) * nrm;          // only tangential
            float s = _contactStrength[i];
            if (delta.magnitude <= staticFrictionConstant * s) _p[i] += delta;  // static: snap back
            else _p[i] += delta * (staticFrictionProportional * s * 0.1f);
        }

        int BoxIndex(BoxCollider b)
        {
            if (_boxIndex.TryGetValue(b, out int idx)) return idx;
            var t = b.transform;
            Vector3 ls = t.lossyScale;
            var data = new BoxData
            {
                center = t.TransformPoint(b.center),
                half = Vector3.Scale(b.size * 0.5f, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z))),
                rot = t.rotation,
                inv = Quaternion.Inverse(t.rotation),
            };
            if (_boxCount == _boxes.Length) System.Array.Resize(ref _boxes, _boxes.Length * 2);
            idx = _boxCount++;
            _boxes[idx] = data;
            _boxIndex[b] = idx;
            return idx;
        }

        void AddBox(int node, BoxCollider b)
        {
            int idx = BoxIndex(b);
            int baseI = node * MaxBoxesPerNode;
            int c = _nodeBoxCount[node];
            for (int k = 0; k < c; k++) if (_nodeBoxes[baseI + k] == idx) return;
            if (c < MaxBoxesPerNode) { _nodeBoxes[baseI + c] = idx; _nodeBoxCount[node] = c + 1; }
        }

        void GatherContacts()
        {
            using var _ = s_gatherMarker.Auto();
            if (!_probe)
            {
                var go = new GameObject("RopeProbe") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.position = new Vector3(0, -10000, 0);
                _probe = go.AddComponent<SphereCollider>();
                _probe.isTrigger = true;
            }
            _boxCount = 0;
            _boxIndex.Clear();

            for (int i = 0; i < NodeCount; i++)
            {
                _planes[i].count = 0;
                _nodeBoxCount[i] = 0;
                _contactStrength[i] = 0f;
                // Pinned nodes still gather boxes so the segments next to them stay out of geometry.
                Vector3 p = _p[i];
                float travel = (_p[i] - _x[i]).magnitude;
                float probeR = radius + _l0 * 0.75f + travel;

                if (_w[i] != 0f && travel > radius &&
                    Physics.SphereCast(_x[i], radius, (_p[i] - _x[i]) / travel, out var hit, travel, collisionMask, QueryTriggerInteraction.Ignore)
                    && !Ignore(hit.collider, i))
                {
                    if (hit.collider is BoxCollider hb) AddBox(i, hb);
                    else _planes[i].Add(hit.normal, Vector3.Dot(hit.normal, hit.point));
                }

                int count = Physics.OverlapSphereNonAlloc(p, probeR, _overlap, collisionMask, QueryTriggerInteraction.Ignore);
                for (int c = 0; c < count; c++)
                {
                    var col = _overlap[c];
                    if (Ignore(col, i)) continue;
                    if (col is BoxCollider box) { AddBox(i, box); continue; }
                    if (_w[i] == 0f) continue;
                    _probe.radius = probeR;
                    if (Physics.ComputePenetration(_probe, p, Quaternion.identity, col, col.transform.position, col.transform.rotation, out var dir, out var dist))
                    {
                        // Sphere of radius probeR at p must move 'dist' along 'dir' to separate -> surface plane.
                        float d = Vector3.Dot(dir, p) + dist - probeR;
                        _planes[i].Add(dir, d);
                    }
                }
            }
        }

        // GetComponentInParent per collider per node per step was a big cost; the answer never changes, so cache it.
        static readonly Dictionary<Collider, bool> s_ignoreCache = new Dictionary<Collider, bool>();

        bool Ignore(Collider c, int node)
        {
            // The body this rope is tied to must not push its own attachment node away.
            if (endRigidbody && node >= NodeCount - 3 && c.attachedRigidbody == endRigidbody) return true;
            if (!s_ignoreCache.TryGetValue(c, out bool ignore))
            {
                ignore = c is CharacterController || c.isTrigger || c.GetComponentInParent<RopeIgnore>() != null;
                s_ignoreCache[c] = ignore;
            }
            return ignore;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (_p == null) return;
            for (int i = 0; i < NodeCount; i++)
            {
                Gizmos.color = _pins[i].kind != PinKind.None ? Color.yellow : (_contactStrength[i] > 0 ? Color.red : Color.white);
                Gizmos.DrawWireSphere(_p[i], radius * 1.5f);
            }
        }
#endif
    }
}
