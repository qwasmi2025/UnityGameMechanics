using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RopeLab
{
    /// <summary>
    /// Player-side rope interaction, TLOU2 style:
    /// grab any node (the rope then slides through the hand as you walk away), gather/coil a loose rope
    /// node by node, throw (aim arc), tie to posts, plug cables into sockets, climb hanging ropes.
    /// When the rope is anchored and fully paid out, it limits how far the player can walk (taut line).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class RopeInteractor : MonoBehaviour
    {
        public static RopeInteractor Player { get; private set; }
        public static RopeTestZone CurrentZone;

        enum State { Free, Holding, Gathering, Coiled, Climbing }

        [Header("Refs (auto)")]
        public Transform hand;
        public Transform coilMount;
        public Camera cam;
        [Tooltip("Locomotion scripts disabled while climbing.")]
        public Behaviour[] disableWhileClimbing;

        [Header("Tuning")]
        public float grabRadius = 1.7f;
        public float holdToGather = 0.45f;
        public float gatherSpeed = 4.5f;
        public float minThrowSpeed = 9f, maxThrowSpeed = 24f, chargeTime = 0.9f;
        public float climbSpeed = 1.3f;
        public float maxStretch = 1.03f;
        [Tooltip("Rope slides through the hand when one side is pulled taut. Hold Left Alt to grip tight.")]
        public bool slideThroughHand = true;
        [Tooltip("Also run the rope through the left palm. Off by default: without a carry animation the arms swing apart.")]
        public bool twoHandHold = false;
        [Tooltip("Stretch ratio of the rope next to the hand that starts it sliding.")]
        public float slideThreshold = 1.02f;

        State _state;
        RopeSim _rope;
        int _node = -1;
        Transform _holdPoint, _reelPoint;
        CharacterController _cc;
        RopeSim[] _ropes;
        RopeTiePoint[] _ties;
        RopeSocket[] _sockets;
        float _eHeld, _charge;
        bool _aiming, _eDown;
        LineRenderer _arc;
        // gather
        int _gLo, _gHi, _gSlots, _reelNode = -1;
        // coil
        int _coilTieSide = -1;           // -1 none, 0 = node 0 tied
        // climb
        float _climbS;
        int _climbAnchor;
        Vector3 _climbAway;
        // animation
        float _throwAt = -1f;            // pending throw
        Vector3 _throwVel;
        bool _pulling;                   // player is pulling against the rope (pull pose)
        float _pullTimer;
        float _mantleEnd = -1f;
        Vector3 _mantleTarget;
        // hands: the rope runs through the right palm and a second node through the left palm
        Animator _animator;
        Transform _leftPoint, _grabPoint;
        int _support = -1;               // node held by the left hand
        float _grabT = -1f;              // >= 0 while reaching down to pick the rope up
        Vector3 _grabFrom;
        static readonly Vector3 CarryLocal = new Vector3(0.22f, 1.05f, 0.38f);
        // grip smoothing (arm swing must not whip the rope) and last-frame position (for the taut-rope limit)
        Vector3 _holdLocal, _holdVel, _lastPos;
        [Tooltip("How much the grip follows the animated hand (0 = fixed carry point in front of the chest).")]
        [Range(0f, 1f)] public float followHand = 0.6f;
        public float gripSmoothing = 0.12f;

        RopeHUD Hud => RopeHUD.Instance;

        void Awake()
        {
            Player = this;
            _cc = GetComponent<CharacterController>();
            if (!hand) hand = FindDeep(transform, "Hand_R");
            if (!coilMount)
            {
                var spine = FindDeep(transform, "Spine_03");
                coilMount = new GameObject("RopeCoilMount").transform;
                coilMount.SetParent(spine ? spine : transform, false);
                coilMount.position = transform.position + Vector3.up * 1.15f - transform.forward * 0.22f;
                coilMount.rotation = transform.rotation;
            }
            _animator = GetComponentInChildren<Animator>();
            // Grip points are placed in the palms every LateUpdate (after animation).
            _holdPoint = new GameObject("RopeGripRight").transform;
            _holdPoint.SetParent(transform, false);
            _holdPoint.localPosition = _holdLocal = CarryLocal;
            _leftPoint = new GameObject("RopeGripLeft").transform;
            _leftPoint.SetParent(transform, false);
            _grabPoint = new GameObject("RopeGrabPoint").transform;
            _reelPoint = new GameObject("RopeReelPoint").transform;

            if (disableWhileClimbing == null || disableWhileClimbing.Length == 0)
            {
                var list = new List<Behaviour>();
                foreach (var mb in GetComponents<MonoBehaviour>())
                    if (mb.GetType().Name.Contains("PlayerAnimationController")) list.Add(mb);
                disableWhileClimbing = list.ToArray();
            }

            // Throw preview: a soft, glowing white arc (a bright core line + a wide faint halo) that fades in from
            // the hand and gets stronger toward where it lands, ending in a soft glow blob.
            var arcMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = SoftLineTexture() };
            _arc = MakeArcLine("ThrowArc", arcMat,
                new AnimationCurve(new Keyframe(0f, 0.015f), new Keyframe(0.2f, 0.05f), new Keyframe(1f, 0.07f)),
                0f, 0.55f, 0.85f);
            _arcGlow = MakeArcLine("ThrowArcGlow", arcMat,
                new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(0.2f, 0.2f), new Keyframe(1f, 0.26f)),
                0f, 0.1f, 0.16f);

            var blob = GameObject.CreatePrimitive(PrimitiveType.Quad);
            blob.name = "ThrowLandingGlow";
            Destroy(blob.GetComponent<Collider>());
            blob.transform.SetParent(transform, false);
            var blobMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = GlowTexture() };
            var br = blob.GetComponent<MeshRenderer>();
            br.sharedMaterial = blobMat;
            _landingMat = blobMat;
            br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            br.receiveShadows = false;
            _landingBlob = blob.transform;
            blob.SetActive(false);
        }

        LineRenderer _arcGlow;
        Transform _landingBlob;
        Material _landingMat;

        LineRenderer MakeArcLine(string name, Material mat, AnimationCurve width, float aStart, float aMid, float aEnd)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.transform.SetParent(transform, false);
            lr.positionCount = 0;
            lr.material = mat;
            lr.textureMode = LineTextureMode.Stretch;
            lr.widthCurve = width;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(aStart, 0f), new GradientAlphaKey(aMid, 0.18f), new GradientAlphaKey(aEnd, 1f) });
            lr.colorGradient = g;
            return lr;
        }

        /// <summary>Soft across the line's width (V), so the arc has feathered, glowing edges.</summary>
        static Texture2D SoftLineTexture()
        {
            var t = new Texture2D(4, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
            {
                float v = (y + 0.5f) / 64f * 2f - 1f;            // -1..1 across the line
                float a = Mathf.Exp(-v * v * 4.5f);               // gaussian falloff
                for (int x = 0; x < 4; x++) t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            t.Apply();
            return t;
        }

        /// <summary>Radial glow for the landing point.</summary>
        static Texture2D GlowTexture()
        {
            const int s = 64;
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s / 2f, s / 2f)) / (s / 2f);
                float a = Mathf.Clamp01(Mathf.Exp(-d * d * 5f) * 1.1f);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            t.Apply();
            return t;
        }
        /// <summary>Throws start over the right shoulder, beside the player (not in front of her).</summary>
        Vector3 ThrowOriginLocal => new Vector3(0.38f, 1.6f, 0.05f);
        Vector3 ThrowOrigin => transform.TransformPoint(ThrowOriginLocal);

        void Start()
        {
            if (!cam) cam = Camera.main;
            _ropes = FindObjectsByType<RopeSim>(FindObjectsSortMode.None);
            _ties = FindObjectsByType<RopeTiePoint>(FindObjectsSortMode.None);
            _sockets = FindObjectsByType<RopeSocket>(FindObjectsSortMode.None);
        }

        // ================================================================ Update
        static readonly Unity.Profiling.ProfilerMarker s_updateMarker = new Unity.Profiling.ProfilerMarker("RopeLab.Interactor");
        static readonly Unity.Profiling.ProfilerMarker s_climbMarker = new Unity.Profiling.ProfilerMarker("RopeLab.FindClimb");

        void Update()
        {
            using var _u = s_updateMarker.Auto();
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null) return;

            for (int i = 0; i < 6; i++)
                if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) TeleportToTest(i + 1);
            if (kb.rKey.wasPressedThisFrame && CurrentZone) CurrentZone.ResetTest();

            // E: tap = grab / drop, hold = gather.
            if (kb.eKey.wasPressedThisFrame) { _eDown = true; _eHeld = 0f; }
            if (_eDown && kb.eKey.isPressed) _eHeld += Time.deltaTime;

            switch (_state)
            {
                case State.Free: UpdateFree(kb); break;
                case State.Holding: UpdateHolding(kb, mouse); break;
                case State.Gathering: UpdateGathering(kb); break;
                case State.Coiled: UpdateCoiled(kb, mouse); break;
                case State.Climbing: UpdateClimbing(kb); break;
            }

            if (kb.eKey.wasReleasedThisFrame) _eDown = false;
        }

        // No rope-specific character animation: the player keeps the Synty locomotion.
        bool PlayPickUpIfLow(Vector3 nodePos) => false;
        void PlayPush() { }

        /// <summary>Releases the rope on the next update.</summary>
        void BeginThrow(Vector3 vel)
        {
            _throwVel = vel;
            _throwAt = Time.time;
        }

        bool ETapped(Keyboard kb) => kb.eKey.wasReleasedThisFrame && _eDown && _eHeld < holdToGather;
        bool EHeldLong() => _eDown && _eHeld >= holdToGather;

        void LateUpdate()
        {
            UpdateHoldPoint();
            if (!_aiming)
            {
                if (_arcGlow.positionCount > 0) _arcGlow.positionCount = 0;
                if (_landingBlob.gameObject.activeSelf) _landingBlob.gameObject.SetActive(false);
            }
            else if (_landingBlob.gameObject.activeSelf && cam)
            {
                // Glow always faces the camera and breathes a little.
                _landingBlob.rotation = Quaternion.LookRotation(_landingBlob.position - cam.transform.position);
                float s = 0.42f * (1f + 0.08f * Mathf.Sin(Time.time * 5f));
                _landingBlob.localScale = new Vector3(s, s, s);
            }
            if (_state == State.Holding)
            {
                UpdateGrab();
                bool grip = Keyboard.current != null && Keyboard.current.leftAltKey.isPressed;
                if (slideThroughHand && !grip && _grabT < 0f) SlideThroughHand();
                UpdateSupportPin();
                EnforceTether(_node);
            }
            else if (_state == State.Coiled && _coilTieSide >= 0) UpdateCoilPayout();
            else if (_state != State.Gathering) Hud?.ShowTension(false, 0);
            _lastPos = transform.position;
        }

        // ================================================================ Free
        void UpdateFree(Keyboard kb)
        {
            var sock = NearestSocket(true);
            var tie = NearestTie(false);
            FindGrab(out var rope, out int node, out _);
            bool canClimb = FindClimb(out var cRope, out float cS, out int cAnchor, out bool fromTop);

            if (sock)
            {
                Hud?.Prompt("F", "Unplug cable");
                if (kb.fKey.wasPressedThisFrame)
                {
                    var r = sock.plugged; int n = sock.pluggedNode;
                    sock.Unplug();
                    StartHolding(r, n);
                    PlayPush();
                    return;
                }
            }
            else if (tie)
            {
                Hud?.Prompt("F", "Untie rope");
                if (kb.fKey.wasPressedThisFrame)
                {
                    var r = tie.tiedRope; int n = tie.tiedNode;
                    tie.Untie();
                    StartHolding(r, n);
                    PlayPush();
                    return;
                }
            }

            if (rope)
            {
                bool gatherable = CanGather(rope);
                // Floating prompt right next to the rope node the player would grab; it follows that node.
                Hud?.WorldPrompt(rope.GetNode(node), "E", rope.isCable ? "Carry" : "Grab");
                if (gatherable) Hud?.Prompt("Hold E", "Gather & coil rope");
                if (gatherable && _eDown) Hud?.ShowCharge(true, _eHeld / holdToGather);

                if (gatherable && EHeldLong())
                {
                    _eDown = false;
                    Hud?.ShowCharge(false, 0);
                    PlayPickUpIfLow(rope.GetNode(node));
                    StartGathering(rope, node);
                    return;
                }
                if (ETapped(kb) || (!gatherable && kb.eKey.wasPressedThisFrame))
                {
                    _eDown = false;
                    Hud?.ShowCharge(false, 0);
                    StartHolding(rope, node, true);
                    return;
                }
            }
            else Hud?.ShowCharge(false, 0);

            if (canClimb)
            {
                Hud?.Prompt("Q", fromTop ? "Climb down" : "Climb");
                if (kb.qKey.wasPressedThisFrame) StartClimb(cRope, cS, cAnchor);
            }
        }

        // ================================================================ Holding
        void StartHolding(RopeSim rope, int node, bool fromGround = false)
        {
            _rope = rope; _node = node; _state = State.Holding;
            _aiming = false; _charge = 0; _support = -1;
            _lastPos = transform.position;
            if (fromGround && Vector3.Distance(rope.GetNode(node), _holdPoint.position) > 0.3f)
            {
                // Bring the rope up to the hand over a moment instead of snapping it (a snap whips the rope).
                _grabFrom = rope.GetNode(node);
                _grabPoint.position = _grabFrom;
                _grabT = 0f;
                _rope.SetPin(node, PinKind.Hand, _grabPoint, Vector3.zero);
            }
            else
            {
                _grabT = -1f;
                _rope.SetPin(node, PinKind.Hand, _holdPoint, Vector3.zero);
            }
        }

        void UpdateGrab()
        {
            if (_grabT < 0f) return;
            _grabT += Time.deltaTime;
            const float reach = 0f, lift = 0.25f;       // lift the rope into the hand smoothly
            float t = Mathf.Clamp01((_grabT - reach) / lift);
            t = t * t * (3f - 2f * t);
            _grabPoint.position = Vector3.Lerp(_grabFrom, _holdPoint.position, t);
            if (_grabT >= reach + lift)
            {
                _rope.SetPin(_node, PinKind.Hand, _holdPoint, Vector3.zero);
                _grabT = -1f;
            }
        }

        /// <summary>
        /// Second hand: the node two segments along the rope (toward the anchor, or toward the longer part of a
        /// free rope) sits in the left palm, so the rope visibly runs through both hands.
        /// </summary>
        void UpdateSupportPin()
        {
            if (_support >= 0) { _rope.ClearPin(_support); _support = -1; }
            if (!twoHandHold || _state != State.Holding || _aiming || _throwAt >= 0f || _grabT >= 0f) return;
            int anchor = _rope.NearestAnchor(_node);
            int dir = anchor >= 0 ? (anchor < _node ? -1 : 1) : (_node > _rope.EndIndex / 2 ? -1 : 1);
            int s = _node + dir * 2;
            if (s < 0 || s > _rope.EndIndex || _rope.IsPinned(s)) return;
            _support = s;
            _rope.SetPin(s, PinKind.Hand, _leftPoint, Vector3.zero);
        }

        /// <summary>Lets go of the rope with both hands.</summary>
        void ReleaseHands()
        {
            if (_rope)
            {
                _rope.ClearPin(_node);
                if (_support >= 0) _rope.ClearPin(_support);
            }
            _support = -1; _grabT = -1f;
        }

        void UpdateHolding(Keyboard kb, Mouse mouse)
        {
            // Throw in progress: keep the rope in the hand until the animation's release frame.
            if (_throwAt >= 0f)
            {
                if (Time.time < _throwAt) return;
                _throwAt = -1f;
                int anchor = _rope.NearestAnchor(_node);
                if (anchor >= 0)
                {
                    // Rope fixed at its other end (e.g. a floor ring): enough rope stays on the ground to reach
                    // back to the anchor, the rest is balled up and thrown as a bundle — and you let go.
                    int away = anchor < _node ? 1 : -1;
                    float reach = Vector3.Distance(_rope.GetNode(anchor), transform.position);
                    int keep = Mathf.Clamp(Mathf.CeilToInt(reach * 1.15f / _rope.SegmentLength) + 2, 0, Mathf.Abs(_node - anchor));
                    var bundle = new List<int>();
                    for (int i = anchor + away * keep; i != _node + away; i += away) bundle.Add(i);
                    ReleaseHands();
                    LaunchBundle(_rope, bundle, _throwVel);
                    Hud?.Toast("Thrown", 0.8f);
                    EndHold();
                }
                else
                {
                    // Free rope: the whole rope is balled up and thrown, and it leaves your hand
                    // (holding on made the bundle snap back at the end of the rope).
                    var bundle = new List<int>();
                    for (int i = 0; i <= _rope.EndIndex; i++) bundle.Add(i);
                    var rope = _rope;
                    ReleaseHands();
                    LaunchBundle(rope, bundle, _throwVel);
                    Hud?.Toast("Thrown", 0.8f);
                    EndHold();
                }
                return;
            }

            bool nearEnd = _node <= 1 || _node >= _rope.EndIndex - 1;
            int endNode = _node <= 1 ? 0 : _rope.EndIndex;
            var sock = NearestSocket(false);
            var tie = NearestTie(true);

            if (_rope.isCable && sock)
            {
                if (nearEnd)
                {
                    Hud?.Prompt("F", "Plug in cable");
                    if (kb.fKey.wasPressedThisFrame)
                    {
                        ReleaseHands();
                        sock.Plug(_rope, endNode);
                        PlayPush();
                        Hud?.Toast("Power connected");
                        EndHold();
                        return;
                    }
                }
                else Hud?.Prompt("—", "Walk away to slide to the plug end");
            }
            else if (tie)
            {
                Hud?.Prompt("F", "Tie rope here");
                if (kb.fKey.wasPressedThisFrame)
                {
                    ReleaseHands();
                    tie.Tie(_rope, _node);
                    PlayPush();
                    EndHold();
                    return;
                }
            }

            Hud?.Prompt("LMB", "Hold to aim · release to throw");
            bool gatherable = CanGather(_rope);
            Hud?.Prompt("E", gatherable ? "Drop · Hold E gather" : "Drop");
            if (slideThroughHand) Hud?.Prompt("Alt", "Grip tight (stop sliding)");
            if (gatherable && _eDown && !_aiming) Hud?.ShowCharge(true, _eHeld / holdToGather);

            if (gatherable && EHeldLong() && !_aiming)
            {
                _eDown = false;
                Hud?.ShowCharge(false, 0);
                int n = _node;
                ReleaseHands();
                _state = State.Free;
                StartGathering(_rope, n);
                return;
            }
            if (ETapped(kb) && !_aiming)
            {
                _eDown = false;
                Hud?.ShowCharge(false, 0);
                ReleaseHands();
                EndHold();
                return;
            }
            if (HandleThrowInput(mouse, out Vector3 vel)) BeginThrow(vel);
        }

        /// <summary>
        /// Throw the whole rope out from the held node: the free side flies with the throw and spreads out,
        /// the anchored side (if any) only follows a little.
        /// </summary>
        void ThrowFrom(RopeSim rope, int held, Vector3 vel)
        {
            int n = rope.NodeCount;
            int anchor = rope.NearestAnchor(held);
            for (int i = 0; i < n; i++)
            {
                if (rope.IsPinned(i)) continue;
                int d = Mathf.Abs(i - held);
                bool towardAnchor = anchor >= 0 && (anchor < held ? i < held : i > held);
                float f;
                if (towardAnchor) f = Mathf.Clamp01(1f - d / 8f) * 0.8f;
                else
                {
                    // The far end leads a little and everything streams out behind it.
                    int span = i < held ? held : (n - 1 - held);
                    float t = span > 0 ? d / (float)span : 0f;
                    f = Mathf.Lerp(1f, 0.75f, t);
                }
                // Slight spread so the rope opens up in the air instead of flying as a clump.
                Vector3 spread = new Vector3(Mathf.Sin(i * 1.7f), 0.3f, Mathf.Cos(i * 1.3f)) * 0.25f * f;
                rope.SetVelocity(i, vel * f + spread);
            }
        }

        /// <summary>
        /// TLOU-style throw: the given nodes are wound into a tight bundle at the throw origin (right shoulder)
        /// and all launched with the same velocity, so the weight flies as one lump along the predicted arc.
        /// Nodes are ordered from the side connected to your hand/anchor, so the rope pays out of the bundle
        /// in the right order while it flies.
        /// </summary>
        void LaunchBundle(RopeSim rope, List<int> nodes, Vector3 vel)
        {
            if (nodes.Count == 0) return;
            Vector3 origin = ThrowOrigin;
            Vector3 fwd = vel.sqrMagnitude > 1e-4f ? vel.normalized : transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            if (right.sqrMagnitude < 1e-4f) right = transform.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(fwd, right);
            const float r = 0.12f;                           // bundle radius
            float ang = 0f, along = 0f;
            int count = nodes.Count;
            for (int j = 0; j < count; j++)
            {
                int i = nodes[j];
                if (rope.IsPinned(i)) continue;
                ang += rope.SegmentLength / r;
                along += 0.005f;                             // loops stack toward the front: the lead end is ahead
                Vector3 p = origin + (right * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * r + fwd * along;
                rope.Teleport(i, p);
                // Unfurl in flight: the lead end (last in the list) flies 30% faster, the trailing end 30% slower.
                // The average is exactly the throw velocity, and the solver conserves momentum, so the rope's
                // centre still follows the predicted arc to the glow dot while the rope stretches out along the
                // path — aim the dot at a wall's top edge and the rope lies across it, lead end hanging over.
                float t = count > 1 ? j / (float)(count - 1) : 1f;
                rope.SetVelocity(i, vel * Mathf.Lerp(0.7f, 1.3f, t));
            }
            rope.SuspendBending(0.15f);                      // leaves the hand as a coil, then opens up
        }

        /// <summary>For a rope fixed at one end: can the throw actually reach this point?</summary>
        bool InReach(Vector3 point)
        {
            if (!_rope) return true;
            int from = _state == State.Holding ? _node : _rope.EndIndex;
            int anchor = _rope.NearestAnchor(from);
            if (anchor >= 0)
            {
                float available = (anchor < from ? _rope.EndIndex - anchor : anchor) * _rope.SegmentLength;
                return Vector3.Distance(_rope.GetNode(anchor), point) <= available * 0.95f;
            }
            // Free rope you keep holding: it can only fly as far as the rope beyond your hand.
            int side = _state == State.Holding ? Mathf.Max(_rope.EndIndex - _node, _node) : _rope.EndIndex;
            return Vector3.Distance(transform.position, point) <= side * _rope.SegmentLength * 0.95f;
        }

        void EndHold()
        {
            _state = State.Free; _rope = null; _node = -1;
            _aiming = false; _arc.positionCount = 0;
            _throwAt = -1f; _pulling = false; _support = -1; _grabT = -1f;
            Hud?.ShowCharge(false, 0);
        }

        // Grip points sit in the palms (between the wrist and the middle-finger knuckle), after animation.
        void UpdateHoldPoint()
        {
            // In the right hand, but eased and pulled toward a steady carry point: the walk cycle swings the arm
            // fast enough to whip the rope around if the grip followed the bone exactly.
            Vector3 palmLocal = transform.InverseTransformPoint(Palm(HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal, CarryLocal));
            Vector3 target = Vector3.Lerp(CarryLocal, palmLocal, followHand);
            // Winding up a throw: the rope hand rises beside the right shoulder, where the throw starts.
            if (_aiming || _throwAt >= 0f) target = ThrowOriginLocal;
            _holdLocal = Vector3.SmoothDamp(_holdLocal, target, ref _holdVel, gripSmoothing);
            _holdPoint.localPosition = _holdLocal;
            _leftPoint.position = Palm(HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal, CarryLocal + new Vector3(-0.3f, 0f, 0f));
        }

        Vector3 Palm(HumanBodyBones handBone, HumanBodyBones fingerBone, Vector3 fallbackLocal)
        {
            if (_animator && _animator.isHuman)
            {
                var h = _animator.GetBoneTransform(handBone);
                var f = _animator.GetBoneTransform(fingerBone);
                if (h && f) return Vector3.Lerp(h.position, f.position, 0.6f);
                if (h) return h.position;
            }
            return transform.TransformPoint(fallbackLocal);
        }

        // Rope pays out through the hand, only AWAY from the anchor, while the anchor side is taut.
        // A free (unanchored) rope never slides: you drag the whole rope, like TLOU2.
        void SlideThroughHand()
        {
            if (!_rope) return;
            int anchor = _rope.NearestAnchor(_node);
            if (anchor < 0) return;
            int away = anchor < _node ? 1 : -1;
            for (int step = 0; step < 3; step++)
            {
                int k = _node, next = k + away;
                if (next < 0 || next > _rope.EndIndex || _rope.IsPinned(next)) return;   // reached the end
                float rest = Mathf.Abs(k - anchor) * _rope.SegmentLength;
                float path = _rope.PathLength(anchor, k);
                float straight = Vector3.Distance(_rope.GetNode(anchor), _holdPoint.position);
                if (Mathf.Max(path, straight) < rest * slideThreshold) return;          // still slack
                _rope.ClearPin(k);
                _rope.SetPin(next, PinKind.Hand, _holdPoint, Vector3.zero);
                _node = next;
            }
        }

        void EnforceTether(int held)
        {
            // Pull pose only while the player is actually pulling against the rope (held back at the end of
            // the rope, or dragging a body). It lingers briefly so it does not flicker, then returns to hold.
            _pullTimer = Mathf.Max(0f, _pullTimer - Time.deltaTime);
            _pulling = _pullTimer > 0f;
            if (!_rope) { _pulling = false; _pullTimer = 0f; return; }
            int anchor = _rope.NearestAnchor(held);
            if (anchor < 0) { _pulling = false; _pullTimer = 0f; Hud?.ShowTension(false, 0); return; }

            float rest = Mathf.Abs(anchor - held) * _rope.SegmentLength;
            float totalRest = (anchor < held ? _rope.EndIndex - anchor : anchor) * _rope.SegmentLength;
            float straight = Vector3.Distance(_rope.GetNode(anchor), _holdPoint.position);

            // Measure from the BODY, not the animated hands: the rope up to a node a few segments before the
            // hands, then straight to a fixed point in front of the chest. Hand animation (pull/throw) no longer
            // reads as "stretch", so it can't push the player around or keep the pull pose looping.
            int towardAnchor = anchor < held ? -1 : 1;
            int refNode = Mathf.Abs(held - anchor) > 3 ? held + towardAnchor * 3 : anchor;
            Vector3 body = transform.TransformPoint(CarryLocal);
            float bodyPath = _rope.PathLength(anchor, refNode) + Vector3.Distance(_rope.GetNode(refNode), body);
            float bodyRest = Mathf.Abs(refNode - anchor) * _rope.SegmentLength + Mathf.Abs(held - refNode) * _rope.SegmentLength;
            Hud?.ShowTension(true, 1f - Mathf.Clamp01(Mathf.Max(straight, bodyPath * 0.92f) / Mathf.Max(0.01f, totalRest)));

            // Can more rope still slide through the hand? Then don't hold the player back.
            int feed = held - towardAnchor;
            bool canFeed = slideThroughHand && feed >= 0 && feed <= _rope.EndIndex && !_rope.IsPinned(feed)
                           && !(Keyboard.current != null && Keyboard.current.leftAltKey.isPressed);

            float excess = bodyPath - bodyRest * maxStretch;
            if (excess > 0f && !canFeed)
            {
                // Out of rope: take back this frame's own movement, only as much as overshot. The player retraces
                // ground they just walked on, so they can never be shoved into or through a wall (pushing toward
                // the rope did that when it wrapped around a wall). Walking sideways along the limit still works,
                // because sideways motion barely lengthens the rope.
                Vector3 moved = transform.position - _lastPos;
                moved.y = 0f;
                float back = Mathf.Min(excess, moved.magnitude);
                if (back > 1e-4f)
                    _cc.Move(-moved.normalized * back);
                if (excess > 0.6f) Hud?.Prompt("!", "Out of rope");
                // Pull pose only when the player is actively trying to move against the rope.
                if (excess > 0.03f && HasMoveInput()) { _pullTimer = 0.3f; _pulling = true; }
            }
        }

        static bool HasMoveInput()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.wKey.isPressed || kb.aKey.isPressed || kb.sKey.isPressed || kb.dKey.isPressed
                               || kb.upArrowKey.isPressed || kb.downArrowKey.isPressed || kb.leftArrowKey.isPressed || kb.rightArrowKey.isPressed))
                return true;
            var pad = Gamepad.current;
            return pad != null && pad.leftStick.ReadValue().sqrMagnitude > 0.04f;
        }

        // ================================================================ Gather (coil node by node)
        bool CanGather(RopeSim rope)
        {
            for (int i = 0; i < rope.NodeCount; i++)
                if (RopeSim.IsAnchorKind(rope.GetPin(i).kind)) return false;
            return true;
        }

        void StartGathering(RopeSim rope, int node)
        {
            _rope = rope; _state = State.Gathering;
            _gLo = _gHi = node; _gSlots = 0; _reelNode = -1;
            rope.SetPin(node, PinKind.Coil, coilMount, CoilOffset(_gSlots++, rope.SegmentLength));
            Hud?.Toast("Gathering rope…", 1.2f);
        }

        void UpdateGathering(Keyboard kb)
        {
            int n = _rope.NodeCount;
            float progress = _gSlots / (float)n;
            Hud?.Prompt("E", "Stop gathering (drop)");
            Hud?.ShowTension(true, progress);

            if (kb.eKey.wasPressedThisFrame)
            {
                _eDown = false;
                _rope.ClearPins(PinKind.Coil);
                _rope.ClearPins(PinKind.Hand);
                _reelNode = -1;
                _state = State.Free; _rope = null;
                Hud?.ShowTension(false, 0);
                return;
            }

            // Pick the next node to pull in: whichever side's next node is nearer the hand.
            if (_reelNode < 0)
            {
                bool lo = _gLo > 0, hi = _gHi < n - 1;
                if (!lo && !hi) { FinishGathering(); return; }
                int cand;
                if (lo && hi)
                    cand = (_rope.GetNode(_gLo - 1) - _holdPoint.position).sqrMagnitude <= (_rope.GetNode(_gHi + 1) - _holdPoint.position).sqrMagnitude ? _gLo - 1 : _gHi + 1;
                else cand = lo ? _gLo - 1 : _gHi + 1;
                _reelNode = cand;
                _reelPoint.position = _rope.GetNode(cand);
                _rope.SetPin(cand, PinKind.Hand, _reelPoint, Vector3.zero);
            }

            // Hand-over-hand: pull the node to the hand; the rest of the rope is dragged in behind it.
            _reelPoint.position = Vector3.MoveTowards(_reelPoint.position, _holdPoint.position, gatherSpeed * Time.deltaTime);
            if ((_reelPoint.position - _holdPoint.position).sqrMagnitude < 0.0025f)
            {
                _rope.SetPin(_reelNode, PinKind.Coil, coilMount, CoilOffset(_gSlots++, _rope.SegmentLength));
                if (_reelNode < _gLo) _gLo = _reelNode; else _gHi = _reelNode;
                _reelNode = -1;
            }
        }

        void FinishGathering()
        {
            _state = State.Coiled; _coilTieSide = -1; _reelNode = -1;
            Hud?.ShowTension(false, 0);
            Hud?.Toast("Rope coiled — ready to throw or tie");
        }

        static Vector3 CoilOffset(int slot, float segLen)
        {
            const float r = 0.2f;
            float circumference = 2f * Mathf.PI * r;
            float s = slot * segLen;
            float a = s / circumference * Mathf.PI * 2f;
            float turns = s / circumference;
            return new Vector3(turns * 0.012f - 0.05f, Mathf.Sin(a) * r, Mathf.Cos(a) * r - 0.02f);
        }

        // ================================================================ Coiled
        void UpdateCoiled(Keyboard kb, Mouse mouse)
        {
            if (_throwAt >= 0f)
            {
                if (Time.time < _throwAt) return;
                _throwAt = -1f;
                ReleaseCoil(_throwVel);
                return;
            }
            var tie = _coilTieSide < 0 ? NearestTie(true) : null;
            if (tie)
            {
                Hud?.Prompt("F", "Tie end here");
                if (kb.fKey.wasPressedThisFrame)
                {
                    _rope.ClearPin(0);
                    _rope.Teleport(0, tie.transform.position);
                    tie.Tie(_rope, 0);
                    PlayPush();
                    _coilTieSide = 0;
                    Hud?.Toast("Rope tied — throw the rest");
                    return;
                }
            }
            Hud?.Prompt("LMB", "Hold to aim · release to throw coil");
            Hud?.Prompt("E", "Drop coil");

            if (ETapped(kb) && !_aiming)
            {
                _eDown = false;
                _rope.ClearPins(PinKind.Coil);
                EndCoil();
                return;
            }
            if (HandleThrowInput(mouse, out Vector3 vel)) BeginThrow(vel);
        }

        void ReleaseCoil(Vector3 vel)
        {
            int n = _rope.NodeCount;
            var rope = _rope;
            bool tied = _coilTieSide >= 0;
            // The coil flies as one bundle and leaves your hand; if tied, rope nearest the tie peels out first.
            var bundle = new List<int>();
            if (tied) { for (int i = 0; i < n; i++) if (rope.GetPin(i).kind == PinKind.Coil) bundle.Add(i); }
            else { for (int i = n - 1; i >= 0; i--) bundle.Add(i); }
            rope.ClearPins(PinKind.Coil);
            LaunchBundle(rope, bundle, vel);
            EndCoil();
            Hud?.Toast("Thrown", 0.8f);
        }

        // While tied and coiled, pay out rope from the coil as the player walks away from the tie.
        void UpdateCoilPayout()
        {
            float dist = Vector3.Distance(_rope.GetNode(0), coilMount.position);
            int needed = Mathf.Clamp(Mathf.CeilToInt(dist / _rope.SegmentLength * 1.25f) + 3, 1, _rope.NodeCount);
            for (int i = 1; i < needed; i++)
                if (_rope.GetPin(i).kind == PinKind.Coil) { _rope.ClearPin(i); _rope.Teleport(i, coilMount.position); }
            bool allOut = _rope.GetPin(_rope.EndIndex).kind != PinKind.Coil;
            if (allOut) { EndCoil(); Hud?.Toast("Out of rope"); }
            else Hud?.ShowTension(true, 1f - needed / (float)_rope.NodeCount);
        }

        void EndCoil()
        {
            _state = State.Free; _rope = null; _coilTieSide = -1;
            _aiming = false; _arc.positionCount = 0; _throwAt = -1f;
            Hud?.ShowCharge(false, 0);
        }

        // ================================================================ Throw
        bool HandleThrowInput(Mouse mouse, out Vector3 velocity)
        {
            velocity = default;
            if (mouse == null) return false;
            if (mouse.leftButton.wasPressedThisFrame) { _aiming = true; _charge = 0f; }
            if (_aiming && mouse.leftButton.isPressed)
            {
                _charge = Mathf.Min(1f, _charge + Time.deltaTime / chargeTime);
                Hud?.ShowCharge(true, _charge);
                DrawArc(ThrowVelocity());
            }
            if (_aiming && mouse.leftButton.wasReleasedThisFrame)
            {
                _aiming = false; _arc.positionCount = 0;
                Hud?.ShowCharge(false, 0);
                velocity = ThrowVelocity();
                return true;
            }
            return false;
        }

        Vector3 ThrowVelocity()
        {
            // Aim follows the camera, lifted well above the view line so a lob can clear high walls:
            // look up to throw higher (up to 75°).
            Vector3 fwd = cam ? cam.transform.forward : transform.forward;
            Vector3 flat = new Vector3(fwd.x, 0f, fwd.z).normalized;
            // A rope throw is a lob: ~45° by default so it arcs up and over walls; the camera only nudges it
            // (half of where you look), and the charge sets the distance. A flat, fast throw can't clear a wall.
            float camPitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1, 1)) * Mathf.Rad2Deg;
            float pitch = Mathf.Clamp(45f + camPitch * 0.5f, 25f, 75f);
            Vector3 dir = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, flat)) * flat;
            return dir * Mathf.Lerp(minThrowSpeed, maxThrowSpeed, _charge) + _cc.velocity * 0.5f;
        }

        void DrawArc(Vector3 v)
        {
            const int steps = 80;
            const float dt = 0.03f;
            _arc.positionCount = steps;
            Vector3 p = ThrowOrigin; Vector3 vel = v;
            int count = steps;
            bool landed = false;
            for (int i = 0; i < steps; i++)
            {
                _arc.SetPosition(i, p);
                Vector3 next = p + vel * dt;
                vel += Physics.gravity * dt;
                if (Physics.Linecast(p, next, out var hit, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.GetComponentInParent<RopeInteractor>())
                {
                    _arc.SetPosition(Mathf.Min(i + 1, steps - 1), hit.point);
                    count = Mathf.Min(i + 2, steps);
                    DrawLanding(hit.point, hit.normal);
                    landed = true;
                    break;
                }
                p = next;
            }
            _arc.positionCount = count;
            if (!landed) _landingBlob.gameObject.SetActive(false);

            // Halo follows the same path.
            _arcGlow.positionCount = _arc.positionCount;
            for (int i = 0; i < _arc.positionCount; i++) _arcGlow.SetPosition(i, _arc.GetPosition(i));
        }

        void DrawLanding(Vector3 point, Vector3 normal)
        {
            _landingBlob.gameObject.SetActive(true);
            _landingBlob.position = point + normal * 0.06f;
            // White = the rope can get there. Red = past the rope's length (it is tied at the other end).
            _landingMat.color = InReach(point) ? Color.white : new Color(1f, 0.35f, 0.3f);
        }

        // ================================================================ Climb
        readonly List<int> _anchorScratch = new List<int>();

        bool FindClimb(out RopeSim rope, out float s, out int anchor, out bool fromTop)
        {
            using var _c = s_climbMarker.Auto();
            rope = null; s = 0; anchor = -1; fromTop = false;
            Vector3 feet = transform.position;
            float best = 1.2f;
            foreach (var r in _ropes)
            {
                // Anchor points of this rope, found once (was: a full search for every node, every frame).
                _anchorScratch.Clear();
                for (int i = 0; i < r.NodeCount; i++)
                    if (RopeSim.IsAnchorKind(r.GetPin(i).kind, false)) _anchorScratch.Add(i);
                if (_anchorScratch.Count == 0) continue;      // nothing holds it up: can't climb it

                for (int i = 1; i < r.NodeCount - 1; i++)
                {
                    Vector3 p = r.GetNode(i);
                    Vector3 hp = p - feet; hp.y = 0f;
                    if (hp.sqrMagnitude > 1.21f) continue;   // cheap reject before anything else
                    int a = -1, ad = int.MaxValue;
                    foreach (int ai in _anchorScratch) { int d = Mathf.Abs(ai - i); if (d < ad) { ad = d; a = ai; } }
                    if (a < 0 || a == i) continue;
                    int awayStep = a < i ? 1 : -1;
                    Vector3 below = r.GetNode(i + awayStep);
                    Vector3 toAnchor = r.GetNode(i - awayStep);
                    bool hanging = p.y - below.y > r.SegmentLength * 0.7f;
                    if (!hanging) continue;

                    float dBottom = Mathf.Abs(p.y - (feet.y + 1.6f));
                    bool lip = Mathf.Abs(p.y - feet.y) < 0.35f && Mathf.Abs(toAnchor.y - p.y) < r.SegmentLength * 0.6f;
                    float dTop = lip ? 0.05f : float.MaxValue;
                    float dd = Mathf.Min(dBottom, dTop);
                    if (dd < best) { best = dd; rope = r; s = i; anchor = a; fromTop = dTop < dBottom; }
                }
            }
            return rope != null;
        }

        void StartClimb(RopeSim rope, float s, int anchor)
        {
            _rope = rope; _climbS = s; _climbAnchor = anchor; _state = State.Climbing;
            Vector3 p = rope.GetNode(Mathf.RoundToInt(s));
            Vector3 away = transform.position - p; away.y = 0f;
            if (away.sqrMagnitude < 1e-3f) away = -transform.forward;
            Vector3 anchorFlat = rope.GetNode(anchor) - p; anchorFlat.y = 0f;
            if (anchorFlat.sqrMagnitude > 0.04f) away = -anchorFlat;
            _climbAway = away.normalized;
            foreach (var b in disableWhileClimbing) if (b) b.enabled = false;
            _cc.enabled = false;
            Hud?.Toast("Climbing");
        }

        void UpdateClimbing(Keyboard kb)
        {
            // Climbing over the lip: the animation plays in place, then the player is moved on top.
            if (_mantleEnd >= 0f)
            {
                if (Time.time < _mantleEnd) return;
                _mantleEnd = -1f;
                transform.position = _mantleTarget;
                StopClimb(Vector3.zero);
                Hud?.Toast("Climbed up", 1f);
                return;
            }

            Hud?.Prompt("W / S", "Climb up / down");
            Hud?.Prompt("Space", "Let go");

            int dirToAnchor = _climbAnchor < _climbS ? -1 : 1;
            float input = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            float ds = input * climbSpeed * Time.deltaTime / _rope.SegmentLength;
            float next = _climbS + ds * dirToAnchor;

            int nearIdx = Mathf.RoundToInt(_climbS);
            _rope.ResetMassScale();
            _rope.SetMassScale(nearIdx, 25f);        // the player's weight pulls the rope taut

            int upIdx = Mathf.Clamp(nearIdx + dirToAnchor, 0, _rope.EndIndex);
            bool atLip = Mathf.Abs(_rope.GetNode(upIdx).y - _rope.GetNode(nearIdx).y) < _rope.SegmentLength * 0.35f
                         || Mathf.Abs(next - _climbAnchor) < 1f;
            if (input > 0f && atLip && TryMantle()) return;

            if (Mathf.RoundToInt(next) <= 0 || Mathf.RoundToInt(next) >= _rope.EndIndex) next = _climbS;
            if (dirToAnchor < 0) next = Mathf.Max(next, _climbAnchor + 1); else next = Mathf.Min(next, _climbAnchor - 1);
            _climbS = next;

            Vector3 grip = SampleRope(_climbS);
            Vector3 target = grip - Vector3.up * 1.65f + _climbAway * 0.32f;
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-20f * Time.deltaTime));
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(-_climbAway), 10f * Time.deltaTime);

            if (input < 0f && Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, 0.3f, ~0, QueryTriggerInteraction.Ignore))
            { StopClimb(Vector3.zero); return; }
            if (kb.spaceKey.wasPressedThisFrame)
            {
                StopClimb(_climbAway * 1.5f);
            }
        }

        bool TryMantle()
        {
            Vector3 lip = SampleRope(_climbS);
            Vector3 origin = lip - _climbAway * 0.8f + Vector3.up * 1.8f;
            if (Physics.Raycast(origin, Vector3.down, out var hit, 2.5f, ~0, QueryTriggerInteraction.Ignore)
                && hit.point.y > lip.y - 0.6f && Vector3.Dot(hit.normal, Vector3.up) > 0.7f)
            {
                transform.position = hit.point + Vector3.up * 0.05f;
                StopClimb(Vector3.zero);
                Hud?.Toast("Climbed up", 1f);
                return true;
            }
            return false;
        }

        void StopClimb(Vector3 push)
        {
            _rope.ResetMassScale();
            _state = State.Free; _rope = null;
            _cc.enabled = true;
            foreach (var b in disableWhileClimbing) if (b) b.enabled = true;
            if (push != Vector3.zero) _cc.Move(push * 0.3f);
        }

        Vector3 SampleRope(float s)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt(s), 0, _rope.EndIndex - 1);
            return Vector3.Lerp(_rope.GetNode(i), _rope.GetNode(i + 1), s - i);
        }

        // ================================================================ Queries
        void FindGrab(out RopeSim rope, out int node, out float dist)
        {
            rope = null; node = -1; dist = grabRadius;
            Vector3 a = transform.position + Vector3.up * 0.15f, b = transform.position + Vector3.up * 1.7f;
            Vector3 fwd = transform.forward;
            foreach (var r in _ropes)
            {
                for (int i = 0; i < r.NodeCount; i++)
                {
                    if (r.GetPin(i).kind != PinKind.None) continue;
                    Vector3 p = r.GetNode(i);
                    float d = DistToSegment(p, a, b);
                    Vector3 to = p - transform.position; to.y = 0;
                    if (to.sqrMagnitude > 0.01f) d += (1f - Vector3.Dot(to.normalized, fwd)) * 0.25f;
                    if (d < dist) { dist = d; rope = r; node = i; }
                }
            }
        }

        RopeTiePoint NearestTie(bool wantFree)
        {
            RopeTiePoint best = null; float bd = float.MaxValue;
            foreach (var t in _ties)
            {
                if (t.IsFree != wantFree) continue;
                float d = Vector3.Distance(Flat(t.transform.position), Flat(transform.position));
                if (d < t.interactRadius && Mathf.Abs(t.transform.position.y - transform.position.y) < 2.2f && d < bd) { bd = d; best = t; }
            }
            return best;
        }

        RopeSocket NearestSocket(bool wantPowered)
        {
            RopeSocket best = null; float bd = float.MaxValue;
            foreach (var s in _sockets)
            {
                if (s.Powered != wantPowered) continue;
                float d = Vector3.Distance(s.transform.position, transform.position + Vector3.up);
                if (d < s.interactRadius && d < bd) { bd = d; best = s; }
            }
            return best;
        }

        public void ReleaseAll()
        {
            if (_rope)
            {
                if (_state == State.Holding) ReleaseHands();
                if (_state == State.Coiled || _state == State.Gathering) { _rope.ClearPins(PinKind.Coil); _rope.ClearPins(PinKind.Hand); }
                if (_state == State.Climbing) StopClimb(Vector3.zero);
            }
            _state = State.Free; _rope = null; _node = -1; _reelNode = -1; _aiming = false; _arc.positionCount = 0;
            _throwAt = -1f; _mantleEnd = -1f; _pulling = false;
            Hud?.ShowTension(false, 0);
        }

        void TeleportToTest(int number)
        {
            foreach (var z in FindObjectsByType<RopeTestZone>(FindObjectsSortMode.None))
            {
                if (z.number != number || !z.spawnPoint) continue;
                ReleaseAll();
                _cc.enabled = false;
                transform.SetPositionAndRotation(z.spawnPoint.position, z.spawnPoint.rotation);
                _cc.enabled = true;
                return;
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var r = FindDeep(c, name);
                if (r) return r;
            }
            return null;
        }
    }
}
