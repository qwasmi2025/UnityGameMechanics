// DistractionThrowable.cs
// ====================================================================
// حطّه على prefab القنينة/الحجر (لازم عليه Rigidbody + Collider).
// لما يرتطم بأي سطح → يزرع نقطة ريحة قوية تشدّ الكلب لمكان الإلقاء.
// ====================================================================

using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DistractionThrowable : MonoBehaviour
{
    [Header("Distraction")]
    [Tooltip("قوة الريحة المزيفة — أكبر من 1 عشان تغلب ريحة الـ player")]
    public float distractionStrength = 2.2f;

    [Tooltip("بعد كم ثانية يختفي الـ object")]
    public float destroyAfter = 12f;

    private ScentEmitter _playerScent;
    private bool _landed;

    void Start()
    {
        // Unity 6: FindFirstObjectByType بدل FindObjectOfType القديم
        _playerScent = FindFirstObjectByType<ScentEmitter>();
    }

    void OnCollisionEnter(Collision _)
    {
        if (_landed) return;
        _landed = true;

        if (_playerScent != null)
            _playerScent.AddDistractionAt(transform.position, distractionStrength);

        Destroy(gameObject, destroyAfter);
    }
}