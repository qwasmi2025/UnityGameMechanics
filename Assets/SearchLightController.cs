using UnityEngine;

namespace PlagueRats
{
    /// <summary>
    /// Lets the player rotate a search light left/right with T and Y.
    /// The attached RatLight makes rats flee from the light's circle.
    /// </summary>
    public class SearchLightController : MonoBehaviour
    {
        [Tooltip("Rotation speed in degrees per second.")]
        public float rotateSpeed = 60f;

        [Tooltip("Key to rotate left (counter-clockwise).")]
        public KeyCode rotateLeftKey = KeyCode.T;

        [Tooltip("Key to rotate right (clockwise).")]
        public KeyCode rotateRightKey = KeyCode.Y;

        [Tooltip("Limit how far it can turn from its start angle. 0 = no limit.")]
        public float maxAngle = 0f;

        float _startYaw;

        void Start() => _startYaw = transform.eulerAngles.y;

        void Update()
        {
            float dir = 0f;
            if (Input.GetKey(rotateLeftKey)) dir -= 1f;
            if (Input.GetKey(rotateRightKey)) dir += 1f;
            if (dir == 0f) return;

            transform.Rotate(0f, dir * rotateSpeed * Time.deltaTime, 0f, Space.World);

            // optional clamp around the starting angle
            if (maxAngle > 0f)
            {
                float delta = Mathf.DeltaAngle(_startYaw, transform.eulerAngles.y);
                delta = Mathf.Clamp(delta, -maxAngle, maxAngle);
                Vector3 e = transform.eulerAngles;
                e.y = _startYaw + delta;
                transform.eulerAngles = e;
            }
        }
    }
} 