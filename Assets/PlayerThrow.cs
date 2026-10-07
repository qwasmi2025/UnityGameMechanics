// PlayerThrow.cs
// ====================================================================
// حطّه على الـ Player. اضغط G لترمي قنينة تشتّت الكلب.
// مستقل تماماً عن الـ Synty controller — بس بيقرأ input.
// ====================================================================

using UnityEngine;

public class PlayerThrow : MonoBehaviour
{
    [Header("Throw")]
    public GameObject throwablePrefab;   // اسحب prefab القنينة
    public Transform throwOrigin;       // نقطة الإطلاق (لو فاضية = موقع الراس)
    public float throwForce = 13f;
    public float upwardAngle = 12f;
    public KeyCode throwKey = KeyCode.G;

    void Update()
    {
        if (Input.GetKeyDown(throwKey))
            Throw();
    }

    void Throw()
    {
        if (throwablePrefab == null) return;

        Vector3 origin = throwOrigin != null
            ? throwOrigin.position
            : transform.position + Vector3.up * 1.5f;

        Vector3 dir = Quaternion.AngleAxis(-upwardAngle, transform.right)
                      * transform.forward;

        GameObject obj = Instantiate(throwablePrefab, origin, Random.rotation);

        if (obj.TryGetComponent<Rigidbody>(out var rb))
            rb.AddForce(dir * throwForce, ForceMode.Impulse);
    }
}