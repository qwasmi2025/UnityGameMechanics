// ScentDogAI.cs  —  ROOT MOTION VERSION
// ====================================================================
// الأنميشن (root motion) يحرّك الكلب. منع اختراق المباني عبر قص الموقع
// على الـ NavMesh في OnAnimatorMove.
//
// إعدادات ضرورية على الكلب:
//   1. Animator → Apply Root Motion = ✔ ON
//   2. NavMeshAgent: شغّال (الكود يضبط updatePosition=false, updateRotation=false)
//   3. عطّل POLYGON_DogAnimationController (uncheck)
//   4. Advanced_Ears / Advanced_Mouth layer weight = 1
// ====================================================================

using UnityEngine;
using UnityEngine.AI;
using System.Collections;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class ScentDogAI : MonoBehaviour
{
    public enum DogState { Patrolling, Investigating, Tracking, Attacking }

    // ===================== INSPECTOR =====================
    [Header("References")]
    public ScentEmitter playerScent;
    public Transform playerTransform;
    public Transform[] patrolPoints;

    [Header("Scent Detection")]
    public float scentRadius = 14f;
    public float strongThreshold = 0.45f;
    public float weakThreshold = 0.1f;

    [Header("Attack Range")]
    [Tooltip("لما يقرب هالمسافة → يهجم")]
    public float attackRange = 2.8f;
    [Tooltip("لو ابتعد الـ player أكثر → يرجع يلاحق")]
    public float attackExitRange = 5.5f;
    [Tooltip("أقل مسافة عن الـ player — يمنع الدخول جوّاه")]
    public float keepDistance = 2f;
    [Tooltip("لو الـ player دخل هالمسافة (حتى بدون ريحة) → يحس فيه ويطارد فوراً")]
    public float directSenseRange = 8f;

    [Header("Scent-Based Attack (يهجم حسب نقاط الأثر)")]
    [Tooltip("يهجم بس لما يتبقى هالعدد أو أقل من نقاط الأثر قدامه (مش بمجرد ما يقرب)")]
    public int attackNodeThreshold = 4;
    [Tooltip("الريحة لازم تكون أقوى من هيك عشان يهجم (مش يهجم على أثر باهت)")]
    public float attackScentStrength = 0.4f;

    [Header("Rotation (manual smooth)")]
    [Tooltip("سرعة لف الكلب نحو وجهته — درجة/ثانية")]
    public float turnSpeed = 240f;
    [Tooltip("لو الزاوية أكبر من هيك يبطّئ المشي عشان يلتف أول")]
    public float slowTurnAngle = 45f;

    [Header("Animation Movement_f (PDF: 0/0.5/1)")]
    [Tooltip("نعومة تغيّر Movement_f — PDF Damp = 0.25")]
    public float movementDampTime = 0.2f;
    public float walkValue = 0.5f;
    public float runValue = 1.0f;

    [Header("Root Motion Speed Multipliers")]
    [Tooltip("مضاعف سرعة الحركة وقت المطاردة/الركض (1=عادي, 1.5=أسرع 50%)")]
    public float chaseSpeedMultiplier = 1.6f;
    [Tooltip("مضاعف سرعة الحركة وقت المشي العادي")]
    public float walkSpeedMultiplier = 1f;

    [Header("Behaviour")]
    [Tooltip("كل قديش متر يمشي قبل ما يوقف يشمشم")]
    public float sniffEveryMeters = 4f;
    public float sniffDuration = 1.6f;
    [Tooltip("الوقت بين كل هجمة")]
    public float attackInterval = 1.3f;
    [Tooltip("قد إيش يقرب من نقطة الوجهة عشان يعتبرها 'وصل'")]
    public float arriveDistance = 1f;

    [Header("Natural Search (يلفلف ويدوّر)")]
    [Tooltip("قد إيش يضل يحقّق ويلفلف قبل ما يقتنع وينتقل للمطاردة (ثواني من الإحساس القوي المتواصل)")]
    public float trackingConfidence = 2f;
    [Tooltip("نصف قطر اللفّان العشوائي وقت ما ما في ريحة قريبة")]
    public float wanderRadius = 9f;
    [Tooltip("كل قديش يختار نقطة لفّان جديدة (ثواني)")]
    public float wanderInterval = 3f;
    [Tooltip("لما يفقد الأثر، قد إيش يفتّش حوالين آخر نقطة قبل ما يستسلم (TLOU2)")]
    public float searchAroundDuration = 15f;

    [Header("Tracking Sniff (يمشي ويشمشم وهو يتتبع)")]
    [Tooltip("وهو يتتبع، كل قديش متر يوقف يشمشم (بدل ما يركض على طول)")]
    public float trackSniffEveryMeters = 6f;
    [Tooltip("مدة الشمشمة وقت التتبع (أقصر من الاستكشاف)")]
    public float trackSniffDuration = 0.9f;
    [Tooltip("لو اللاعب أقرب من هيك، يبطّل شمشمة ويركض عليه مباشرة (المطاردة النهائية)")]
    public float closeChaseDistance = 6f;

    [Header("POLYGON indices (PDF)")]
    public int sniffAction = 11;  // Sniff
    public int barkAction = 1;   // Bark

    [Header("Debug")]
    public bool showGizmos = true;
    [Tooltip("اطبع تفاصيل كل لحظة على الشاشة (OnGUI)")]
    public bool showDebugHUD = true;

    // ===================== ANIMATOR HASHES =====================
    static readonly int P_Movement = Animator.StringToHash("Movement_f");
    static readonly int P_Grounded = Animator.StringToHash("Grounded_b");
    static readonly int P_Blink = Animator.StringToHash("Blink_tr");
    static readonly int P_Action = Animator.StringToHash("ActionType_int");
    static readonly int P_AttackRdy = Animator.StringToHash("AttackReady_b");
    static readonly int P_AttackTyp = Animator.StringToHash("AttackType_int");
    static readonly int P_Ears = Animator.StringToHash("Advanced_Ears_f");
    static readonly int P_MouthOpen = Animator.StringToHash("Advanced_Mouth_Open_f");
    static readonly int P_Tongue = Animator.StringToHash("Advanced_Mouth_Tongue_Out_b");
    static readonly int P_TailV = Animator.StringToHash("Advanced_Tail_Vertical_f");

    // ===================== PRIVATE =====================
    private NavMeshAgent _agent;
    private Animator _anim;
    private DogState _state = DogState.Patrolling;
    private int _patrolIndex;
    private Vector3 _investigateTarget;
    private float _movementVel;
    private float _currentEars;
    private float _blinkTimer;
    private bool _isBusy;
    private Vector3 _lastSniffPos;
    private float _confidenceTimer;   // قد إيش حسّ بأثر قوي متواصل
    private float _wanderTimer;        // عداد اللفّان العشوائي
    private Vector3 _lastKnownScentPos;  // آخر نقطة شمّها (TLOU2)
    private bool _hasLastKnown;
    private float _searchAroundTimer;  // قد إيش يفتّش حوالين آخر نقطة
    private float _searchPointTimer;
    private Vector3 _lastTrackSniffPos;  // آخر مكان شمشم فيه وقت التتبع

    // ===================== LIFECYCLE =====================
    void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
        _anim = GetComponent<Animator>();

        // *** root motion: الأنميشن يحرّك الكلب (مش الـ agent) ***
        // updatePosition = false: الـ agent ما يحرّك — بس يحسب المسار
        // updateRotation = false: نلف يدوياً
        _agent.updatePosition = false;
        _agent.updateRotation = false;

        _anim.applyRootMotion = true;    // الأنميشن يحرّك المكان (root motion)

        _agent.speed = 12f;              // عالية عشان الـ pathfinding يلحق

        _anim.SetBool(P_Grounded, true);
        _blinkTimer = Random.Range(1f, 5f);
        _lastSniffPos = transform.position;

        // تأكيد: exit range لازم يكون أكبر من attack range (يمنع التذبذب)
        if (attackExitRange <= attackRange)
            attackExitRange = attackRange + 1.5f;

        if (playerTransform == null)
            Debug.LogWarning("[ScentDogAI] Player Transform مش مسحوب! الكلب ما رح يهجم. اسحب الـ Player.");

        // يبدأ يلفلف ويدوّر من البداية (سلوك طبيعي)
        SetState(DogState.Investigating);
    }

    void Update()
    {
        // ===== فحص عام: حسب قرب الـ player (من أي state) =====
        // قريب جداً → هجوم. قريب نسبياً → تتبّع مباشر.
        // هاد يكسر أي تعليق ويخلي الكلب يحس فيك فوراً لما تقرب.
        if (playerTransform != null)
        {
            float d = Vector3.Distance(transform.position, playerTransform.position);

            if (CanAttackNow(d) && _state != DogState.Attacking)
            {
                SetState(DogState.Attacking);
            }
            else if (d <= directSenseRange
                     && _state != DogState.Attacking
                     && _state != DogState.Tracking)
            {
                // قريب → حس فيه وطارد فوراً (يكسر تعليق الـ Investigating)
                // احفظ موقعه كآخر نقطة معروفة عشان دايماً يكون عنده وجهة
                _lastKnownScentPos = playerTransform.position;
                _hasLastKnown = true;
                SetState(DogState.Tracking);
            }
        }

        switch (_state)
        {
            case DogState.Patrolling: TickPatrol(); break;
            case DogState.Investigating: TickInvestigate(); break;
            case DogState.Tracking: TickTracking(); break;
            case DogState.Attacking: TickAttacking(); break;
        }

        // ضمان: أي حالة غير الهجوم → أنميشن الهجوم مطفي دايماً
        if (_state != DogState.Attacking)
        {
            if (_anim.GetBool(P_AttackRdy)) _anim.SetBool(P_AttackRdy, false);
            if (_anim.GetInteger(P_AttackTyp) != 0) _anim.SetInteger(P_AttackTyp, 0);
        }

        SmoothRotate();
        DriveLocomotion();
        DriveMood();
        DriveBlink();
    }

    // ===================== ROOT MOTION SYNC =====================
    void OnAnimatorMove()
    {
        // حركة الأنميشن هالفريم
        Vector3 delta = _anim.deltaPosition;
        delta.y = 0f;

        // سرّع وقت المطاردة النهائية فقط
        bool finalChase = _state == DogState.Attacking;
        if (_state == DogState.Tracking && playerTransform != null)
        {
            float dp = Vector3.Distance(transform.position, playerTransform.position);
            finalChase = dp <= closeChaseDistance;
        }
        float speedMult = finalChase ? chaseSpeedMultiplier : walkSpeedMultiplier;
        delta *= speedMult;

        // طبّق الحركة مباشرة (root motion حر)
        Vector3 newPos = transform.position + delta;
        newPos.y = _agent.nextPosition.y;
        transform.position = newPos;
        _agent.nextPosition = transform.position;
    }

    // ===================== SMOOTH ROTATION =====================
    // مع root motion الـ agent.velocity = 0، فنلف نحو الوجهة الحقيقية.
    void SmoothRotate()
    {
        if (_isBusy)
        {
            if (_state == DogState.Attacking && playerTransform != null)
                RotateToward(playerTransform.position - transform.position);
            return;
        }

        bool hasDestination = _agent.hasPath || _agent.pathPending;
        if (!hasDestination) return;

        Vector3 toDest = _agent.destination - transform.position;
        toDest.y = 0f;

        if (toDest.magnitude > arriveDistance)
            RotateToward(toDest);
    }

    void RotateToward(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.1f) return;
        Quaternion target = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    // ===================== LOCOMOTION (Movement_f) =====================
    void DriveLocomotion()
    {
        float target;

        // مع root motion، نحسب المسافة الحقيقية للوجهة
        bool hasDestination = _agent.hasPath || _agent.pathPending;
        float realDist = hasDestination
            ? Vector3.Distance(transform.position, _agent.destination)
            : 0f;

        bool wantsToMove = !_isBusy && hasDestination && realDist > arriveDistance;

        if (!wantsToMove)
        {
            target = 0f;
        }
        else
        {
            // walk أو run حسب الحالة والقرب من اللاعب
            bool running;
            if (_state == DogState.Attacking)
                running = true;
            else if (_state == DogState.Tracking)
            {
                float dp = playerTransform != null
                    ? Vector3.Distance(transform.position, playerTransform.position) : 999f;
                running = dp <= closeChaseDistance;
            }
            else
                running = false;

            target = running ? runValue : walkValue;
        }

        float current = _anim.GetFloat(P_Movement);
        float smooth = Mathf.SmoothDamp(current, target, ref _movementVel, movementDampTime);
        _anim.SetFloat(P_Movement, smooth);
    }

    // ===================== MOOD =====================
    void DriveMood()
    {
        float targetEars = _state switch
        {
            DogState.Patrolling => 0.45f,
            DogState.Investigating => 0.6f,
            DogState.Tracking => 1f,
            DogState.Attacking => 1f,
            _ => 0.5f
        };
        _currentEars = Mathf.Lerp(_currentEars, targetEars, 4f * Time.deltaTime);
        _anim.SetFloat(P_Ears, _currentEars);

        float tail = _state == DogState.Tracking ? 0.6f : 0f;
        _anim.SetFloat(P_TailV, Mathf.Lerp(_anim.GetFloat(P_TailV), tail, 3f * Time.deltaTime));

        bool panting = _state == DogState.Tracking;
        _anim.SetBool(P_Tongue, panting);
        _anim.SetFloat(P_MouthOpen,
            Mathf.Lerp(_anim.GetFloat(P_MouthOpen), panting ? 0.7f : 0f, 4f * Time.deltaTime));
    }

    void DriveBlink()
    {
        _blinkTimer -= Time.deltaTime;
        if (_blinkTimer <= 0f)
        {
            _anim.SetTrigger(P_Blink);
            _blinkTimer = Random.Range(1f, 5f);
        }
    }

    // ===================== PATROL =====================
    void TickPatrol()
    {
        if (patrolPoints.Length > 0 &&
            _agent.hasPath && _agent.remainingDistance < 0.8f)
        {
            _patrolIndex = (_patrolIndex + 1) % patrolPoints.Length;
            _agent.SetDestination(patrolPoints[_patrolIndex].position);
        }
        else if (patrolPoints.Length > 0 && !_agent.hasPath)
        {
            _agent.SetDestination(patrolPoints[_patrolIndex].position);
        }
        ScanScent();
    }

    // ===================== INVESTIGATE (يلفلف + يشمشم + يدوّر) =====================
    // السلوك: يمشي لوجهته بدون توقف. لما يوصل، يشمشم مرة، بعدين يختار وجهة جديدة.
    // هاد يمنع التقطّع (نتفة مشي/وقفة/نتفة).
    void TickInvestigate()
    {
        if (_isBusy) return;   // عم يشمشم → استنى

        ScentNode best = GetStrongestInRange(scentRadius);

        // ===== في ريحة؟ =====
        if (best != null)
        {
            _investigateTarget = best.Position;
            _agent.SetDestination(_investigateTarget);

            if (best.Strength >= strongThreshold)
            {
                _confidenceTimer += Time.deltaTime;
                if (_confidenceTimer >= trackingConfidence)
                    SetState(DogState.Tracking);
            }
            else
            {
                _confidenceTimer = Mathf.Max(0f, _confidenceTimer - Time.deltaTime * 0.5f);
            }

            // وصل لمصدر الريحة الضعيف → يشمشم مرة
            if (_agent.hasPath && !_agent.pathPending &&
                _agent.remainingDistance <= arriveDistance)
            {
                StartCoroutine(SniffRoutine());
            }
        }
        // ===== ما في ريحة → لفلفة (يمشي لنقطة، يوصل، يشمشم، يختار غيرها) =====
        else
        {
            _confidenceTimer = Mathf.Max(0f, _confidenceTimer - Time.deltaTime);

            // وصل لنقطة اللفّان؟ → يشمشم بعدين يختار نقطة جديدة
            if (_agent.hasPath && !_agent.pathPending &&
                _agent.remainingDistance <= arriveDistance)
            {
                StartCoroutine(SniffThenWander());
            }
            // ما عنده وجهة أصلاً → اختار وحدة
            else if (!_agent.hasPath && !_agent.pathPending)
            {
                PickWanderPoint();
            }
        }
    }

    // يشمشم مرة بعدين يختار وجهة لفّان جديدة (متسلسل، مش متقطّع)
    IEnumerator SniffThenWander()
    {
        yield return SniffRoutine();
        if (_state == DogState.Investigating)
            PickWanderPoint();
    }

    // يختار نقطة لفّان عشوائية على الـ NavMesh
    void PickWanderPoint()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Vector3 randomDir = Random.insideUnitSphere * wanderRadius + transform.position;
            if (NavMesh.SamplePosition(randomDir, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            {
                // تأكد إنها بعيدة كفاية عشان يمشي مسافة محترمة (مش نتفة)
                if (Vector3.Distance(transform.position, hit.position) > 2f)
                {
                    _agent.SetDestination(hit.position);
                    return;
                }
            }
        }
    }

    // ===================== TRACKING (run, attack if close) =====================
    // سلوك TLOU2: يطارد الأثر، ولما يفقده يروح لآخر نقطة شمّها ويفتّش
    // حواليها ويقترب أكثر — عشان يجبر اللاعب يتحرك.
    void TickTracking()
    {
        // ===== فحص الهجوم (حسب النقاط + الريحة، مش بس المسافة) =====
        if (playerTransform != null)
        {
            float distToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (CanAttackNow(distToPlayer))
            {
                SetState(DogState.Attacking);
                return;
            }

            // اللاعب ضمن نطاق الإحساس → طارده مباشرة لمكانه الحالي
            if (distToPlayer <= directSenseRange)
            {
                _lastKnownScentPos = playerTransform.position;
                _hasLastKnown = true;
                _searchAroundTimer = searchAroundDuration;
                _agent.SetDestination(playerTransform.position);
                return;
            }
        }

        // ===== تتبّع الأثر =====
        ScentNode next = GetNextNodeToFollow(scentRadius * 1.6f);

        if (next != null)
        {
            // في أثر → اتبعه + احفظ آخر نقطة معروفة + جدّد الـ timeout
            _lastKnownScentPos = next.Position;
            _hasLastKnown = true;
            _searchAroundTimer = searchAroundDuration;  // ريحة حيّة → جدّد المهلة
            _agent.SetDestination(next.Position);

            // وهو يتتبع: يمشي ويشمشم بالتناوب (مش يركض على طول)
            // إلا لو اللاعب قريب → يبطّل شمشمة ويركض عليه (المطاردة النهائية)
            float distToPlayer = playerTransform != null
                ? Vector3.Distance(transform.position, playerTransform.position)
                : 999f;

            if (distToPlayer > closeChaseDistance && !_isBusy)
            {
                // بعيد → كل مسافة يوقف يشمشم
                if (Vector3.Distance(transform.position, _lastTrackSniffPos) >= trackSniffEveryMeters)
                {
                    _lastTrackSniffPos = transform.position;
                    StartCoroutine(TrackSniffRoutine());
                }
            }
        }
        else if (_hasLastKnown)
        {
            // فقد الأثر → سلوك TLOU2: روح لآخر نقطة وفتّش حواليها
            _searchAroundTimer -= Time.deltaTime;

            // لو ما في ولا نقطة ريحة قريبة إطلاقاً (الأثر خلص فعلاً) →
            // ينقص المهلة أسرع (يستسلم أسرع بدل ما يلاحق فاضي)
            bool anyScentNearby = GetStrongestInRange(scentRadius * 1.6f) != null;
            if (!anyScentNearby)
                _searchAroundTimer -= Time.deltaTime * 2f;   // تسريع الاستسلام

            if (_searchAroundTimer <= 0f)
            {
                _hasLastKnown = false;
                SetState(DogState.Investigating);
                return;
            }

            // هل وصل لآخر نقطة معروفة (أو قريب منها)؟
            float distToLast = Vector3.Distance(transform.position, _lastKnownScentPos);

            if (distToLast <= arriveDistance + 0.5f)
            {
                // وصل → فتّش حواليها (يختار نقاط بحث جديدة باستمرار)
                SearchAroundLastKnown();
            }
            else
            {
                // لسا بعيد → تأكد إنه رايح لآخر نقطة (جدّد الوجهة لو ضاعت)
                if (!_agent.hasPath || _agent.remainingDistance < 0.1f)
                    _agent.SetDestination(_lastKnownScentPos);
            }
        }
        else
        {
            // ولا أثر ولا نقطة معروفة → ارجع للبحث
            SetState(DogState.Investigating);
        }
    }

    // فتّش حوالين آخر نقطة معروفة + اقترب من اللاعب لو قريب (يجبره يتحرك)
    void SearchAroundLastKnown()
    {
        _searchPointTimer -= Time.deltaTime;

        // لسا ماشي لنقطة بحث ولسا ما وصلها → خليه يكمّل
        if (_searchPointTimer > 0f && _agent.hasPath &&
            _agent.remainingDistance > arriveDistance) return;

        _searchPointTimer = 1.5f;

        // ===== اللاعب قريب؟ → اقترب من مكانه (TLOU2: يضغط عليك) =====
        if (playerTransform != null)
        {
            float d = Vector3.Distance(transform.position, playerTransform.position);
            if (d < directSenseRange * 2f)   // ضمن ضعف نطاق الإحساس
            {
                Vector3 toDog = (transform.position - playerTransform.position).normalized;
                Vector3 pressurePos = playerTransform.position + toDog * keepDistance;
                if (NavMesh.SamplePosition(pressurePos, out NavMeshHit ph, 3f, NavMesh.AllAreas))
                {
                    _agent.SetDestination(ph.position);
                    return;
                }
            }
        }

        // ===== فتّش بنقطة عشوائية حوالين آخر نقطة (مع محاولات متعددة) =====
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Vector3 around = _lastKnownScentPos + Random.insideUnitSphere * 5f;
            if (NavMesh.SamplePosition(around, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                _agent.SetDestination(hit.position);
                return;
            }
        }

        // ===== فشل كل المحاولات → ارجع للبحث العام (ما يتجمّد) =====
        _hasLastKnown = false;
        SetState(DogState.Investigating);
    }

    // ===================== ATTACKING (keeps distance) =====================
    void TickAttacking()
    {
        if (playerTransform == null) { SetState(DogState.Tracking); return; }

        float dist = Vector3.Distance(transform.position, playerTransform.position);

        // حماية: لو بعيد كثير (أبعد من exit) → اطلع فوراً من الهجوم
        if (dist > attackExitRange)
        {
            // SetState يتكفّل بإيقاف الهجوم وتصفير الأنميشن
            SetState(GetNextNodeToFollow(scentRadius * 1.6f) != null
                     ? DogState.Tracking : DogState.Investigating);
            return;
        }

        if (dist < keepDistance)
        {
            // قرّب زيادة → ارجع للخلف شوي
            _isBusy = false;
            Vector3 away = (transform.position - playerTransform.position).normalized;
            _agent.SetDestination(playerTransform.position + away * (keepDistance + 0.5f));
        }
        else if (dist > attackRange)
        {
            // ابتعد شوي → اقترب لنقطة على بعد keepDistance
            _isBusy = false;
            Vector3 toDog = (transform.position - playerTransform.position).normalized;
            _agent.SetDestination(playerTransform.position + toDog * keepDistance);
        }
        else
        {
            // المدى المثالي → وقف وهاجم
            _isBusy = true;
            _agent.ResetPath();
        }
    }

    // ===================== SCENT QUERIES =====================
    // كم نقطة ريحة باقية بين الكلب وأحدث نقطة (الأقرب للاعب)؟
    // الفكرة: نلاقي أقرب نقطة للكلب، ونعدّ النقاط اللي sequence تبعها أعلى
    // (يعني اللي قدامه نحو اللاعب). عدد قليل = اللاعب قريب على الأثر.
    int CountNodesAhead()
    {
        if (playerScent == null || playerScent.Trail.Count == 0) return int.MaxValue;

        // أقرب نقطة للكلب ضمن نطاق الشم
        ScentNode closest = null;
        float bestDist = float.MaxValue;
        float r2 = (scentRadius * 1.6f) * (scentRadius * 1.6f);

        foreach (var n in playerScent.Trail)
        {
            float d2 = (transform.position - n.Position).sqrMagnitude;
            if (d2 > r2) continue;
            if (d2 < bestDist) { bestDist = d2; closest = n; }
        }

        if (closest == null) return int.MaxValue;

        // عدّ النقاط اللي قدامه (sequence أعلى = أقرب للاعب)
        int ahead = 0;
        foreach (var n in playerScent.Trail)
        {
            if (n.Sequence > closest.Sequence)
                ahead++;
        }
        return ahead;
    }

    // هل يقدر يهجم الآن؟ يجمع كل الشروط:
    //   1. قريب جسدياً (ضمن attackRange)
    //   2. عدد النقاط قدامه قليل (وصل لآخر الأثر) — يعني اللاعب فعلاً قريب على الأثر
    //   3. الريحة قوية (مش أثر باهت قديم)
    // استثناء: لو قريب جداً جداً (نص attackRange) يهجم فوراً بغض النظر عن النقاط
    bool CanAttackNow(float distToPlayer)
    {
        if (playerTransform == null) return false;

        // قريب جداً جداً → هجوم مباشر (احتياط)
        if (distToPlayer <= attackRange * 0.5f) return true;

        // مش قريب كفاية أصلاً → لأ
        if (distToPlayer > attackRange) return false;

        // قريب بس نتحقق من الأثر: لازم يكون وصل لآخر النقاط + ريحة قوية
        int nodesAhead = CountNodesAhead();
        ScentNode strongest = GetStrongestInRange(scentRadius);
        float strength = strongest != null ? strongest.Strength : 0f;

        bool reachedEndOfTrail = nodesAhead <= attackNodeThreshold;
        bool scentStrong = strength >= attackScentStrength;

        return reachedEndOfTrail && scentStrong;
    }

    ScentNode GetStrongestInRange(float radius)
    {
        if (playerScent == null) return null;
        ScentNode best = null; float bestStr = weakThreshold; float r2 = radius * radius;
        foreach (var n in playerScent.Trail)
        {
            if ((transform.position - n.Position).sqrMagnitude > r2) continue;
            if (n.Strength > bestStr) { bestStr = n.Strength; best = n; }
        }
        return best;
    }

    ScentNode GetNextNodeToFollow(float radius)
    {
        if (playerScent == null || playerScent.Trail.Count == 0) return null;
        ScentNode closest = null; float bestDist = float.MaxValue; float r2 = radius * radius;
        foreach (var n in playerScent.Trail)
        {
            float d2 = (transform.position - n.Position).sqrMagnitude;
            if (d2 > r2) continue;
            if (d2 < bestDist) { bestDist = d2; closest = n; }
        }
        if (closest == null) return null;

        ScentNode ahead = null; int bestSeqDiff = int.MaxValue;
        foreach (var n in playerScent.Trail)
        {
            int diff = n.Sequence - closest.Sequence;
            if (diff > 0 && diff < bestSeqDiff) { bestSeqDiff = diff; ahead = n; }
        }
        return ahead ?? closest;
    }

    void ScanScent()
    {
        ScentNode best = GetStrongestInRange(scentRadius);
        if (best == null) return;
        _investigateTarget = best.Position;
        _lastSniffPos = transform.position;
        SetState(best.Strength >= strongThreshold ? DogState.Tracking : DogState.Investigating);
    }

    // ===================== SNIFF =====================
    IEnumerator SniffRoutine()
    {
        _isBusy = true;
        _anim.SetInteger(P_Action, sniffAction);

        float t = 0f;
        while (t < sniffDuration && _state == DogState.Investigating)
        {
            t += Time.deltaTime;
            yield return null;
        }

        _anim.SetInteger(P_Action, 0);
        _isBusy = false;   // دايماً يتصفّر — يمنع التعليق
    }

    // شمشمة قصيرة وقت التتبع — يوقف نتفة يشمشم بدون ما يخرج من Tracking
    IEnumerator TrackSniffRoutine()
    {
        _isBusy = true;
        _anim.SetInteger(P_Action, sniffAction);

        float t = 0f;
        while (t < trackSniffDuration && _state == DogState.Tracking)
        {
            t += Time.deltaTime;
            yield return null;
        }

        _anim.SetInteger(P_Action, 0);
        _isBusy = false;
    }

    // ===================== ATTACK LOOP =====================
    Coroutine _attackLoop;

    void StartAttacking()
    {
        _anim.SetBool(P_AttackRdy, true);
        _attackLoop = StartCoroutine(AttackLoop());
    }

    void StopAttacking()
    {
        if (_attackLoop != null)
        {
            StopCoroutine(_attackLoop);
            _attackLoop = null;
        }
        _isBusy = false;
        // تصفير كامل وفوري لأنميشن الهجوم
        _anim.SetInteger(P_AttackTyp, 0);
        _anim.SetBool(P_AttackRdy, false);

        // إجبار الأنميتور يرجع للـ Locomotion (يكسر التعليق بالـ Attack state)
        // POLYGON: الـ Locomotion state اسمه "Locomotion" على الـ Base Layer
        _anim.CrossFadeInFixedTime("Locomotion", 0.15f, 0);
    }

    IEnumerator AttackLoop()
    {
        // تأكد إننا لسا في Attacking قبل ما نبدأ
        if (_state != DogState.Attacking) yield break;

        yield return new WaitForSeconds(0.4f);  // ندخل وضع ready أول

        while (_state == DogState.Attacking)
        {
            // هجوم random: 1=Bite, 2=Claw, 3=Hit (PDF)
            int attackType = Random.Range(1, 4);
            if (_state != DogState.Attacking) break;   // تأكد قبل ما تكتب
            _anim.SetInteger(P_AttackTyp, attackType);

            yield return new WaitForSeconds(0.5f);

            // صفّر فقط لو لسا في Attacking (غير هيك StopAttacking بيتكفّل)
            if (_state == DogState.Attacking)
                _anim.SetInteger(P_AttackTyp, 0);

            yield return new WaitForSeconds(attackInterval);
        }

        // عند الخروج من اللوب — تصفير نهائي مضمون
        _anim.SetInteger(P_AttackTyp, 0);
    }

    // ===================== STATE MACHINE =====================
    void SetState(DogState next)
    {
        if (_state == next) return;

        // تنظيف الـ state القديم
        if (_state == DogState.Attacking)
            StopAttacking();

        _state = next;

        // ضمان إضافي: أي state غير Attacking لازم يطفّي أنميشن الهجوم
        if (next != DogState.Attacking)
        {
            _anim.SetBool(P_AttackRdy, false);
            _anim.SetInteger(P_AttackTyp, 0);
        }

        switch (next)
        {
            case DogState.Patrolling:
                _isBusy = false;
                if (patrolPoints.Length > 0)
                    _agent.SetDestination(patrolPoints[_patrolIndex].position);
                break;
            case DogState.Investigating:
                _isBusy = false;
                _lastSniffPos = transform.position;
                _wanderTimer = 0f;
                break;
            case DogState.Tracking:
                _isBusy = false;
                _confidenceTimer = trackingConfidence; // ابقى واثق وأنت تطارد
                _searchAroundTimer = searchAroundDuration;
                _searchPointTimer = 0f;
                _lastTrackSniffPos = transform.position;
                break;
            case DogState.Attacking:
                StartAttacking();
                break;
        }
        Debug.Log($"[ScentDogAI] -> {next}");
    }

    // ===================== DEBUG HUD =====================
    void OnGUI()
    {
        if (!showDebugHUD) return;

        float dist = playerTransform != null
            ? Vector3.Distance(transform.position, playerTransform.position) : -1f;

        string info =
            $"State: {_state}\n" +
            $"isBusy: {_isBusy}\n" +
            $"Movement_f: {_anim.GetFloat(P_Movement):F2}\n" +
            $"agent.hasPath: {_agent.hasPath}\n" +
            $"agent.pathPending: {_agent.pathPending}\n" +
            $"remainingDist: {_agent.remainingDistance:F2}\n" +
            $"desiredVel: {_agent.desiredVelocity.magnitude:F2}\n" +
            $"agent.velocity: {_agent.velocity.magnitude:F2}\n" +
            $"distToPlayer: {dist:F2}\n" +
            $"confidence: {_confidenceTimer:F1}\n" +
            $"scentNodes: {(playerScent != null ? playerScent.Trail.Count : 0)}\n" +
            $"nodesAhead: {CountNodesAhead()}\n" +
            $"hasLastKnown: {_hasLastKnown}";

        GUI.color = Color.black;
        GUI.Label(new Rect(11, 11, 400, 300), info);
        GUI.color = Color.green;
        GUI.Label(new Rect(10, 10, 400, 300), info);
    }

    // ===================== GIZMOS =====================
    void OnDrawGizmos()
    {
        if (!showGizmos) return;
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, scentRadius);
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);          // أصفر = نطاق الإحساس المباشر
        Gizmos.DrawWireSphere(transform.position, directSenseRange);
        Gizmos.color = new Color(1f, 0f, 0f, 0.5f);          // أحمر = نطاق الهجوم
        Gizmos.DrawWireSphere(transform.position, attackRange);

#if UNITY_EDITOR
        var style = new GUIStyle { fontSize = 12, normal = { textColor = Color.cyan } };
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.6f, $"{_state}", style);
#endif
    }
}