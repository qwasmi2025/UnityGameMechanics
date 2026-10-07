using PlagueRats;

using UnityEngine;

/// <summary>
/// A human the rats want to kill. When standing in darkness it becomes a flow-field
/// goal and rats stream toward it; in light it is ignored. Hook OnDevoured to your
/// damage/death logic.
/// </summary>
public class RatTarget : MonoBehaviour
{
    [Tooltip("If false the rats never target this human (e.g. dead, protected).")]
    public bool AttackableByRats = true;

    void OnEnable() { RatDirector.Register(this); }
    void OnDisable() { RatDirector.Unregister(this); }

    void OnDrawGizmos()
    {
        Gizmos.color = AttackableByRats ? Color.red : Color.gray;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}
