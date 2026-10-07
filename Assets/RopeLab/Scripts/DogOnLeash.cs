using UnityEngine;

namespace RopeLab
{
    /// <summary>
    /// Ambient dog tied to a post: wanders around inside the leash, stops, sits, now and then naps.
    /// Drives the PolygonDog animator parameters (Movement_f, Sit_b, Sleep_b, Blink_tr) and turns the dog
    /// itself; the animator's root motion moves it forward. It never walks past the leash length.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class DogOnLeash : MonoBehaviour
    {
        public Transform post;
        public float leashLength = 3.5f;
        [Tooltip("Movement_f while walking (0 = idle, ~0.3-0.5 = walk).")]
        public float walkParam = 0.35f;
        public float turnSpeed = 140f;

        enum Mode { Idle, Wander, Sit, Sleep }
        Mode _mode = Mode.Idle;
        float _timer = 1f, _blink = 2f, _move;
        Vector3 _target;
        Animator _a;
        static readonly int MoveHash = Animator.StringToHash("Movement_f");
        static readonly int SitHash = Animator.StringToHash("Sit_b");
        static readonly int SleepHash = Animator.StringToHash("Sleep_b");
        static readonly int GroundHash = Animator.StringToHash("Grounded_b");
        static readonly int BlinkHash = Animator.StringToHash("Blink_tr");

        void Awake()
        {
            _a = GetComponent<Animator>();
            _a.applyRootMotion = true;
        }

        void Update()
        {
            if (!post) return;
            float dt = Time.deltaTime;
            _a.SetBool(GroundHash, true);

            _blink -= dt;
            if (_blink <= 0f) { _a.SetTrigger(BlinkHash); _blink = Random.Range(2.5f, 6f); }

            _timer -= dt;
            if (_timer <= 0f) PickNext();

            Vector3 flatPos = Flat(transform.position), flatPost = Flat(post.position);
            float fromPost = Vector3.Distance(flatPos, flatPost);

            // Nearly at the end of the leash: head back toward the post, whatever it was doing.
            if (_mode == Mode.Wander && fromPost > leashLength - 0.4f &&
                Vector3.Dot(transform.forward, (flatPos - flatPost).normalized) > 0f)
                _target = flatPost + (flatPos - flatPost).normalized * (leashLength * 0.3f);

            float targetMove = 0f;
            if (_mode == Mode.Wander)
            {
                Vector3 to = _target - flatPos;
                if (to.magnitude < 0.35f) { _mode = Mode.Idle; _timer = Random.Range(1.5f, 4f); }
                else
                {
                    var want = Quaternion.LookRotation(to.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * dt);
                    // Walk once roughly facing the target; turn on the spot first.
                    targetMove = Quaternion.Angle(transform.rotation, want) < 50f ? walkParam : 0.05f;
                }
            }
            _move = Mathf.MoveTowards(_move, targetMove, dt * 1.5f);
            _a.SetFloat(MoveHash, _move);
            _a.SetBool(SitHash, _mode == Mode.Sit);
            _a.SetBool(SleepHash, _mode == Mode.Sleep);
        }

        void PickNext()
        {
            float r = Random.value;
            if (r < 0.5f)
            {
                _mode = Mode.Wander;
                Vector2 c = Random.insideUnitCircle * (leashLength * 0.8f);
                _target = Flat(post.position) + new Vector3(c.x, 0f, c.y);
                _timer = 8f;                               // give up if it takes too long
            }
            else if (r < 0.75f) { _mode = Mode.Idle; _timer = Random.Range(2f, 4f); }
            else if (r < 0.92f) { _mode = Mode.Sit; _timer = Random.Range(4f, 8f); }
            else { _mode = Mode.Sleep; _timer = Random.Range(8f, 14f); }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void OnDrawGizmosSelected()
        {
            if (!post) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(post.position, leashLength);
        }
    }
}
