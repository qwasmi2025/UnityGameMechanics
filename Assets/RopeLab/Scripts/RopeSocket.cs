using UnityEngine;
using UnityEngine.Events;

namespace RopeLab
{
    /// <summary>Electrical socket: accepts the end plug of a cable rope and powers things.</summary>
    public class RopeSocket : MonoBehaviour
    {
        public float interactRadius = 1.6f;
        public Renderer indicator;
        public Light indicatorLight;
        public Color offColor = new Color(0.8f, 0.1f, 0.05f);
        public Color onColor = new Color(0.1f, 1f, 0.3f);
        public UnityEvent onPowered = new UnityEvent();
        public UnityEvent onUnpowered = new UnityEvent();

        [HideInInspector] public RopeSim plugged;
        [HideInInspector] public int pluggedNode = -1;
        public bool Powered => plugged != null;
        MaterialPropertyBlock _mpb;

        void Start() => Refresh();

        public void Plug(RopeSim rope, int node)
        {
            plugged = rope; pluggedNode = node;
            rope.SetPin(node, PinKind.Socket, transform, Vector3.zero);
            Refresh();
            onPowered.Invoke();
        }

        public void Unplug()
        {
            if (plugged) plugged.ClearPin(pluggedNode);
            plugged = null; pluggedNode = -1;
            Refresh();
            onUnpowered.Invoke();
        }

        void Refresh()
        {
            Color c = Powered ? onColor : offColor;
            if (indicator)
            {
                _mpb ??= new MaterialPropertyBlock();
                indicator.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", c);
                _mpb.SetColor("_EmissionColor", c * 2f);
                indicator.SetPropertyBlock(_mpb);
            }
            if (indicatorLight) indicatorLight.color = c;
        }
    }
}
