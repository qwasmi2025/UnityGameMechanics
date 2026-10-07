// ScentEmitter.cs
// ====================================================================
// حطّه على الـ Player. يرمي ScentNode + يضعّفها + يرسمها كـ gizmo spheres
// ملوّنة مع نص حالة لكل نقطة (Strength + Age + Sequence).
// ====================================================================

using System.Collections.Generic;
using UnityEngine;

public class ScentEmitter : MonoBehaviour
{
    [Header("Scent Emission")]
    [Tooltip("كم ثانية بين كل نقطة ونقطة")]
    public float emitInterval = 0.5f;

    [Tooltip("كم ثانية تبقى الريحة قبل ما تختفي تماماً (القيمة الأساسية)")]
    public float scentLifetime = 25f;

    [Tooltip("أقل مسافة بين نقطتين — أكبر = نقاط أبعد وأنضف")]
    public float minDistanceBetweenNodes = 1.8f;

    [Header("Speed-Based Decay (تختفي أسرع لما تركض)")]
    [Tooltip("فعّل تغيّر عمر الريحة حسب سرعة اللاعب")]
    public bool speedAffectsDecay = true;

    [Tooltip("سرعة المشي العادية — مرجع للحساب")]
    public float walkSpeed = 2.5f;

    [Tooltip("سرعة الركض — فوقها تُعتبر 'ركض'")]
    public float runSpeed = 6.5f;

    [Tooltip("مضاعف عمر الريحة لما يركض (أقل = تختفي أسرع). 0.4 = تعيش 40% بس")]
    public float runLifetimeMultiplier = 0.45f;

    [Tooltip("مضاعف عمر الريحة لما يوقف (أكبر = تبقى أطول). 2 = تعيش ضعف")]
    public float stillLifetimeMultiplier = 2.5f;

    [Tooltip("لما يركض (فوق runSpeed) ما يخلّف أثر أبداً — تهرب نظيف")]
    public bool noTrailWhenRunning = true;

    [Header("Scent Pooling (تتجمّع لما توقف)")]
    [Tooltip("لما الـ player يوقف بمكان، الريحة حواليه تتقوّى بدل ما تضعف (زي TLOU2)")]
    public bool poolWhenStill = true;

    [Tooltip("نصف قطر تجمّع الريحة حول الـ player الواقف")]
    public float poolRadius = 2.5f;

    [Tooltip("قد إيش بتتقوّى الريحة بالثانية لما توقف")]
    public float poolRate = 0.35f;

    [Tooltip("الـ player يُعتبر 'واقف' لو تحرّك أقل من هيك")]
    public float stillThreshold = 0.3f;

    [Tooltip("ارتفاع النقطة تحت مركز الـ player. لو الـ pivot عند القدمين خليه 0")]
    public float footOffset = 0f;

    [Tooltip("ثبّت النقطة على الأرض الحقيقية عبر raycast (يحل مشكلة الغرق تحت الأرض)")]
    public bool snapToGround = true;

    [Tooltip("الطبقات اللي تُعتبر أرض للـ raycast")]
    public LayerMask groundMask = ~0;

    [Tooltip("ارتفاع رسم الكرة فوق نقطة الأرض (عشان ما تنغرس)")]
    public float gizmoHeightOffset = 0.1f;

    [Header("Strength")]
    public float baseStrength = 1f;
    public string waterTag = "Water";

    [Header("Gizmo Visualization")]
    [Tooltip("ارسم الكرات حتى لو الـ object مش محدّد")]
    public bool alwaysDrawGizmos = true;

    [Tooltip("حجم كرة النقطة الطازجة")]
    public float maxSphereRadius = 0.22f;

    [Tooltip("اعرض نص الحالة فوق كل كرة")]
    public bool showStatusLabels = true;

    [Tooltip("اعرض خط يربط النقاط (شكل الأثر)")]
    public bool drawConnectingLine = true;

    // ===== Internal =====
    private readonly List<ScentNode> _trail = new();
    private float _lastEmitTime;
    private Vector3 _lastEmitPosition;
    private int _sequenceCounter;
    private Vector3 _prevFramePos;       // موقع اللاعب الفريم السابق
    private float _stillTime;          // قد إيش صار واقف
    private float _currentSpeed;       // سرعة اللاعب الحالية (m/s)

    public IReadOnlyList<ScentNode> Trail => _trail;
    public float CurrentSpeed => _currentSpeed;   // للـ debug

    // --------------------------------------------------------------
    void Start()
    {
        _prevFramePos = transform.position;
        _lastEmitPosition = transform.position;
    }

    // --------------------------------------------------------------
    void Update()
    {
        UpdateSpeed();          // احسب سرعة اللاعب أول
        TryEmitNode();
        PoolScentWhenStill();   // الريحة تتقوّى لما توقف
        DecayNodes();
    }

    // احسب سرعة اللاعب الحالية (ناعمة)
    void UpdateSpeed()
    {
        float instantSpeed = Vector3.Distance(transform.position, _prevFramePos)
                             / Mathf.Max(Time.deltaTime, 0.0001f);
        // ننعّمها شوي عشان ما تتذبذب
        _currentSpeed = Mathf.Lerp(_currentSpeed, instantSpeed, 8f * Time.deltaTime);
        _prevFramePos = transform.position;
    }

    // احسب عمر الريحة حسب سرعة اللاعب الحالية
    // ركض → عمر أقصر | وقوف → عمر أطول | مشي → عادي
    float CalculateLifetimeForSpeed()
    {
        if (!speedAffectsDecay) return scentLifetime;

        if (_currentSpeed >= runSpeed)
        {
            // يركض → ريحة تختفي أسرع
            return scentLifetime * runLifetimeMultiplier;
        }
        else if (_currentSpeed <= stillThreshold)
        {
            // واقف → ريحة تبقى أطول
            return scentLifetime * stillLifetimeMultiplier;
        }
        else if (_currentSpeed <= walkSpeed)
        {
            // بين الوقوف والمشي → تدرّج بين still و normal
            float t = Mathf.InverseLerp(stillThreshold, walkSpeed, _currentSpeed);
            return scentLifetime * Mathf.Lerp(stillLifetimeMultiplier, 1f, t);
        }
        else
        {
            // بين المشي والركض → تدرّج بين normal و run
            float t = Mathf.InverseLerp(walkSpeed, runSpeed, _currentSpeed);
            return scentLifetime * Mathf.Lerp(1f, runLifetimeMultiplier, t);
        }
    }

    void TryEmitNode()
    {
        // يركض بسرعة؟ → ما يخلّف أثر (تهرب نظيف)
        if (noTrailWhenRunning && _currentSpeed >= runSpeed)
            return;

        bool cooldownOk = Time.time - _lastEmitTime >= emitInterval;
        bool movedOk = Vector3.Distance(transform.position, _lastEmitPosition)
                          >= minDistanceBetweenNodes;
        if (!cooldownOk || !movedOk) return;

        Vector3 nodePos;

        if (snapToGround &&
            Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down,
                            out RaycastHit hit, 5f, groundMask))
        {
            // ثبّت النقطة على الأرض الحقيقية
            nodePos = hit.point;
        }
        else
        {
            // fallback: استخدم footOffset
            nodePos = new Vector3(transform.position.x,
                                  transform.position.y - footOffset,
                                  transform.position.z);
        }

        _trail.Add(new ScentNode(nodePos, _sequenceCounter++, baseStrength,
                                 CalculateLifetimeForSpeed()));
        _lastEmitTime = Time.time;
        _lastEmitPosition = transform.position;
    }

    // الريحة تتجمّع وتقوى لما اللاعب يوقف بمكان (زي TLOU2)
    void PoolScentWhenStill()
    {
        if (!poolWhenStill) return;

        // واقف؟ (نستخدم السرعة المحسوبة بـ UpdateSpeed)
        if (_currentSpeed < stillThreshold)
            _stillTime += Time.deltaTime;
        else
            _stillTime = 0f;

        // لسا ما وقف فترة كافية → ما في تجمّع
        if (_stillTime < 0.3f) return;

        // قوّي كل النقاط ضمن poolRadius حول اللاعب
        float r2 = poolRadius * poolRadius;
        foreach (var node in _trail)
        {
            if ((transform.position - node.Position).sqrMagnitude > r2) continue;

            // قوّي الريحة + جدّد وقتها (عشان الـ decay ما يضعّفها)
            node.Strength = Mathf.Min(1f, node.Strength + poolRate * Time.deltaTime);
            node.TimeCreated = Time.time;   // يعيد ضبط العمر → ما تختفي وأنت واقف
        }
    }

    void DecayNodes()
    {
        for (int i = _trail.Count - 1; i >= 0; i--)
        {
            float age = Time.time - _trail[i].TimeCreated;
            // كل نقطة تضعف حسب عمرها الخاص (المحدد بسرعة اللاعب وقت إنشائها)
            _trail[i].Strength = Mathf.Clamp01(1f - age / _trail[i].Lifetime);
            if (_trail[i].Strength <= 0f)
                _trail.RemoveAt(i);
        }
    }

    public void AddDistractionAt(Vector3 worldPos, float strength = 2f)
        => _trail.Add(new ScentNode(worldPos, _sequenceCounter++, strength, scentLifetime));

    public void ClearTrail() => _trail.Clear();

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(waterTag)) ClearTrail();
    }

    // ===================== GIZMO DRAWING =====================
    void OnDrawGizmos()
    {
        if (alwaysDrawGizmos) DrawScentGizmos();
    }

    void OnDrawGizmosSelected()
    {
        if (!alwaysDrawGizmos) DrawScentGizmos();
    }

    void DrawScentGizmos()
    {
        if (_trail == null || _trail.Count == 0) return;

        Vector3 up = Vector3.up * gizmoHeightOffset;

        // ترتيب حسب التسلسل عشان الخط يطلع صح
        for (int i = 0; i < _trail.Count; i++)
        {
            ScentNode node = _trail[i];
            Vector3 drawPos = node.Position + up;   // مرفوعة شوي فوق الأرض

            // اللون: أزرق (راحت) → أصفر → أحمر (طازجة)
            Color c = StrengthToColor(node.Strength);
            Gizmos.color = c;

            // الحجم يصغر مع ضعف الريحة
            float radius = Mathf.Lerp(0.05f, maxSphereRadius, node.Strength);
            Gizmos.DrawSphere(drawPos, radius);

            // حلقة خارجية شفافة (نطاق الإحساس)
            Gizmos.color = new Color(c.r, c.g, c.b, 0.25f);
            Gizmos.DrawWireSphere(drawPos, radius * 1.8f);

            // الخط الواصل بين النقاط المتتالية
            if (drawConnectingLine && i > 0)
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
                Gizmos.DrawLine(_trail[i - 1].Position + up, drawPos);
            }

#if UNITY_EDITOR
            if (showStatusLabels)
            {
                float age = Time.time - node.TimeCreated;
                string status = node.Strength > 0.6f ? "FRESH"
                              : node.Strength > 0.3f ? "FADING"
                              : "WEAK";

                var style = new GUIStyle
                {
                    fontSize = 10,
                    normal = { textColor = c }
                };

                UnityEditor.Handles.Label(
                    drawPos + Vector3.up * 0.3f,
                    $"#{node.Sequence} {status}\n{(node.Strength * 100f):F0}% | {age:F1}s",
                    style);
            }
#endif
        }
    }

    // أزرق→أصفر→أحمر حسب القوة
    Color StrengthToColor(float strength)
    {
        if (strength > 0.5f)
            return Color.Lerp(Color.yellow, Color.red, (strength - 0.5f) * 2f);
        return Color.Lerp(Color.blue, Color.yellow, strength * 2f);
    }
}