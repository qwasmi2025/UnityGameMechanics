using UnityEngine;

namespace RopeLab
{
    /// <summary>Somewhere a held rope node can be tied (post, pipe, railing).</summary>
    public class RopeTiePoint : MonoBehaviour
    {
        public float interactRadius = 1.6f;
        [HideInInspector] public RopeSim tiedRope;
        [HideInInspector] public int tiedNode = -1;
        public bool IsFree => tiedRope == null;

        public void Tie(RopeSim rope, int node)
        {
            tiedRope = rope; tiedNode = node;
            rope.SetPin(node, PinKind.Tie, transform, Vector3.zero);
        }

        public void Untie()
        {
            if (tiedRope) tiedRope.ClearPin(tiedNode);
            tiedRope = null; tiedNode = -1;
        }
    }
}
