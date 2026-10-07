// ScentNode.cs
// ====================================================================
// وحدة بيانات صرفة (مش MonoBehaviour) — بتمثّل نقطة ريحة وحدة بالعالم
// الـ Player بيخلّف ورا منها سلسلة، والكلب بيتبعها
// ====================================================================

using UnityEngine;

public class ScentNode
{
    public Vector3 Position;     // مكان النقطة بالعالم
    public float Strength;     // 1.0 = طازجة, 0.0 = راحت خلص
    public float TimeCreated;  // وقت الإنشاء — منه بنحسب الـ decay
    public int Sequence;     // رقم تسلسلي — يحدد ترتيب الأثر (مهم للتتبع)
    public float Lifetime;     // عمر هالنقطة بالذات (يتحدد حسب سرعة اللاعب وقت إنشائها)

    public ScentNode(Vector3 position, int sequence, float strength = 1f, float lifetime = 20f)
    {
        Position = position;
        Sequence = sequence;
        Strength = strength;
        Lifetime = lifetime;
        TimeCreated = Time.time;
    }
}