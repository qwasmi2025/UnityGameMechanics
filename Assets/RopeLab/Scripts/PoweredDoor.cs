using UnityEngine;

namespace RopeLab
{
    /// <summary>Shutter that slides up while powered.</summary>
    public class PoweredDoor : MonoBehaviour
    {
        public Vector3 openOffset = new Vector3(0f, 3.2f, 0f);
        public float speed = 1.5f;
        public bool open;
        public Light[] lights;
        Vector3 _closed;

        void Awake() => _closed = transform.localPosition;
        public void SetOpen(bool value)
        {
            open = value;
            if (lights != null) foreach (var l in lights) if (l) l.enabled = value;
        }
        public void Open() => SetOpen(true);
        public void Close() => SetOpen(false);
        void Start() => SetOpen(open);

        void Update()
        {
            Vector3 target = open ? _closed + openOffset : _closed;
            transform.localPosition = Vector3.MoveTowards(transform.localPosition, target, speed * Time.deltaTime);
        }
    }
}
