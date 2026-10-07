using UnityEngine;

namespace RopeLab
{
    public enum TestGoal { None, SocketPowered, RopeEndInZone, BodyInZone, PlayerInZone }

    /// <summary>A test bay: shows its objective when the player enters, checks a completion goal, can reset its rope.</summary>
    public class RopeTestZone : MonoBehaviour
    {
        public int number = 1;
        public string title = "Test";
        [TextArea] public string description;
        public Transform spawnPoint;

        [Header("Completion")]
        public TestGoal goal;
        public RopeSocket socket;
        public RopeSim rope;
        public Collider goalZone;
        public Rigidbody goalBody;

        [Header("Reset")]
        public RopeSim[] ropesToReset;
        public Rigidbody[] bodiesToReset;

        public bool Completed { get; private set; }
        Vector3[] _bodyPos; Quaternion[] _bodyRot;

        void Start()
        {
            _bodyPos = new Vector3[bodiesToReset.Length];
            _bodyRot = new Quaternion[bodiesToReset.Length];
            for (int i = 0; i < bodiesToReset.Length; i++)
            {
                _bodyPos[i] = bodiesToReset[i].position; _bodyRot[i] = bodiesToReset[i].rotation;
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (!other.GetComponentInParent<RopeInteractor>()) return;
            RopeInteractor.CurrentZone = this;
            RopeHUD.Instance?.SetObjective($"Test {number:00} · {title}", description);
            if (Completed) RopeHUD.Instance?.SetStatus("COMPLETE");
        }

        void Update()
        {
            if (Completed) return;
            bool done = goal switch
            {
                TestGoal.SocketPowered => socket && socket.Powered,
                TestGoal.RopeEndInZone => rope && goalZone && (goalZone.bounds.Contains(rope.GetNode(rope.EndIndex)) || goalZone.bounds.Contains(rope.GetNode(0))),
                TestGoal.BodyInZone => goalBody && goalZone && goalZone.bounds.Contains(goalBody.worldCenterOfMass),
                TestGoal.PlayerInZone => goalZone && RopeInteractor.Player && goalZone.bounds.Contains(RopeInteractor.Player.transform.position + Vector3.up),
                _ => false
            };
            if (done)
            {
                Completed = true;
                RopeHUD.Instance?.Toast($"TEST {number:00} COMPLETE");
                if (RopeInteractor.CurrentZone == this) RopeHUD.Instance?.SetStatus("COMPLETE");
            }
        }

        public void ResetTest()
        {
            Completed = false;
            foreach (var t in GetComponentsInChildren<RopeTiePoint>()) if (t.tiedRope) t.Untie();
            foreach (var s in GetComponentsInChildren<RopeSocket>()) if (s.plugged) s.Unplug();
            RopeInteractor.Player?.ReleaseAll();
            foreach (var r in ropesToReset) r.Build();
            for (int i = 0; i < bodiesToReset.Length; i++)
            {
                var b = bodiesToReset[i];
                b.linearVelocity = Vector3.zero; b.angularVelocity = Vector3.zero;
                b.position = _bodyPos[i]; b.rotation = _bodyRot[i];
                b.transform.SetPositionAndRotation(_bodyPos[i], _bodyRot[i]);
            }
            RopeHUD.Instance?.SetStatus("");
            RopeHUD.Instance?.Toast("Test reset");
        }
    }
}
